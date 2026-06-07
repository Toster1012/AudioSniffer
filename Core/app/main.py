import os
import re
import zipfile
import asyncio
import tempfile
import shutil
from datetime import datetime
from pathlib import Path
from typing import List, Optional

from fastapi import FastAPI, File, UploadFile, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.middleware.trustedhost import TrustedHostMiddleware
from fastapi.responses import JSONResponse
from starlette.middleware.base import BaseHTTPMiddleware

from app.models import AnalysisResult, HealthResponse, AudioMetadata, BatchAnalysisResult
from app.detectors.silence_detector import SilenceDetector
from app.detectors.pitch_detector import PitchDetector
from app.detectors.splice_detector import SpliceDetector
from app.detectors.ai_detector import AIDetector
from app.utils.audio_processor import AudioProcessor

MAX_UPLOAD_BYTES = 50 * 1024 * 1024
MAX_ZIP_BYTES = 200 * 1024 * 1024
MAX_ZIP_MEMBERS = 20
SUPPORTED_AUDIO = {'.wav', '.mp3', '.ogg', '.flac', '.m4a', '.aac'}
_SAFE_NAME = re.compile(r'[^\w\s\-.]')

DETECTOR_WEIGHTS = {
    "silence": 0.03,
    "pitch":   0.12,
    "splice":  0.15,
    "ai":      0.70,
}

app = FastAPI(
    title="AudioSniffer ML Service",
    version="3.1.0",
    docs_url=None,
    redoc_url=None,
    openapi_url=None,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["https://localhost:8000"],
    allow_credentials=False,
    allow_methods=["POST", "GET"],
    allow_headers=["Content-Type"],
)

app.add_middleware(
    TrustedHostMiddleware,
    allowed_hosts=["localhost", "127.0.0.1", "::1"],
)


class SecurityHeadersMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next):
        response = await call_next(request)
        response.headers["X-Content-Type-Options"] = "nosniff"
        response.headers["X-Frame-Options"] = "DENY"
        response.headers["Cache-Control"] = "no-store"
        return response


app.add_middleware(SecurityHeadersMiddleware)


def _sanitize_filename(name: str) -> str:
    name = Path(name).name
    name = _SAFE_NAME.sub('', name)
    return name[:128] or "upload"


def _compute_overall_confidence(detections: list) -> float:
    if not detections:
        return 0.0
    type_map = {d.type.value: d.confidence for d in detections}
    weighted = sum(
        type_map.get(name, 0.0) * weight
        for name, weight in DETECTOR_WEIGHTS.items()
    )
    peak = max(d.confidence for d in detections)
    combined = weighted * 0.85 + peak * 0.15
    return float(min(combined, 1.0))


async def _analyze_file(file_path: str, file_name: str) -> AnalysisResult:
    is_valid, message = AudioProcessor.validate_audio_file(file_path)
    if not is_valid:
        raise HTTPException(status_code=400, detail=message)

    loop = asyncio.get_event_loop()

    metadata, spectrogram_data, detections = await asyncio.gather(
        loop.run_in_executor(None, AudioProcessor.extract_metadata, file_path),
        loop.run_in_executor(None, AudioProcessor.extract_spectrogram, file_path),
        _run_detectors_parallel(file_path, loop),
    )

    overall_confidence = _compute_overall_confidence(detections)
    is_suspicious = overall_confidence > 0.30

    audio_file_id = f"audio_{datetime.utcnow().timestamp()}_{_sanitize_filename(file_name)}"

    return AnalysisResult(
        audio_file_id=audio_file_id,
        file_name=_sanitize_filename(file_name),
        overall_confidence=overall_confidence,
        is_suspicious=is_suspicious,
        detections=detections,
        metadata=metadata,
        spectrogram_data=spectrogram_data
    )


async def _run_detectors_parallel(file_path: str, loop) -> list:
    silence_fut = loop.run_in_executor(None, SilenceDetector().analyze, file_path)
    pitch_fut   = loop.run_in_executor(None, PitchDetector().analyze, file_path)
    splice_fut  = loop.run_in_executor(None, SpliceDetector().analyze, file_path)
    ai_fut      = loop.run_in_executor(None, AIDetector().analyze, file_path)
    return list(await asyncio.gather(silence_fut, pitch_fut, splice_fut, ai_fut))


@app.get("/", response_model=HealthResponse)
async def root():
    return HealthResponse()


@app.get("/health", response_model=HealthResponse)
async def health_check():
    try:
        SilenceDetector()
        PitchDetector()
        SpliceDetector()
        AIDetector()
        return HealthResponse(status="healthy", version="3.1.0", detectors=["silence", "pitch", "splice", "ai"])
    except Exception as e:
        return HealthResponse(status="unhealthy", version="3.1.0", detectors=[], error=str(e))


@app.post("/analyze", response_model=AnalysisResult)
async def analyze_audio(file: UploadFile = File(...)):
    content = await file.read(MAX_UPLOAD_BYTES + 1)
    if len(content) > MAX_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail="Файл превышает 50 МБ")

    safe_name = _sanitize_filename(file.filename or "upload")
    suffix = Path(safe_name).suffix.lower() or ".tmp"
    if suffix not in SUPPORTED_AUDIO and suffix != ".tmp":
        raise HTTPException(status_code=400, detail="Неподдерживаемый формат файла")

    temp_file = None
    try:
        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)
        temp_file.write(content)
        temp_file.close()
        return await _analyze_file(temp_file.name, safe_name)
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Ошибка анализа: {str(e)}")
    finally:
        if temp_file and os.path.exists(temp_file.name):
            try:
                os.unlink(temp_file.name)
            except Exception:
                pass


@app.post("/analyze/batch", response_model=BatchAnalysisResult)
async def analyze_zip(file: UploadFile = File(...)):
    if not (file.filename or "").lower().endswith(".zip"):
        raise HTTPException(status_code=400, detail="Ожидается ZIP-архив")

    content = await file.read(MAX_ZIP_BYTES + 1)
    if len(content) > MAX_ZIP_BYTES:
        raise HTTPException(status_code=413, detail="Архив превышает 200 МБ")

    zip_temp = None
    extract_dir = None
    try:
        zip_temp = tempfile.NamedTemporaryFile(delete=False, suffix=".zip")
        zip_temp.write(content)
        zip_temp.close()

        extract_dir = tempfile.mkdtemp()

        with zipfile.ZipFile(zip_temp.name, "r") as zf:
            for info in zf.infolist():
                if info.compress_size > 0 and info.file_size / info.compress_size > 100:
                    raise HTTPException(status_code=400, detail="Подозрительное соотношение сжатия (zip bomb?)")

            members = zf.namelist()
            audio_members = [
                m for m in members
                if Path(m).suffix.lower() in SUPPORTED_AUDIO
                and not m.startswith("__MACOSX")
                and not Path(m).name.startswith(".")
                and ".." not in m
            ]

            if not audio_members:
                raise HTTPException(
                    status_code=400,
                    detail=f"Нет аудиофайлов в архиве. Форматы: {', '.join(sorted(SUPPORTED_AUDIO))}"
                )

            if len(audio_members) > MAX_ZIP_MEMBERS:
                raise HTTPException(status_code=400, detail=f"Максимум {MAX_ZIP_MEMBERS} файлов в архиве")

            zf.extractall(extract_dir)

        tasks = []
        for member in audio_members:
            file_path = os.path.join(extract_dir, member)
            file_name = _sanitize_filename(Path(member).name)
            tasks.append(_analyze_file(file_path, file_name))

        task_results = await asyncio.gather(*tasks, return_exceptions=True)

        results = []
        errors = []
        for i, res in enumerate(task_results):
            file_name = _sanitize_filename(Path(audio_members[i]).name)
            if isinstance(res, Exception):
                errors.append({"file": file_name, "error": str(res)})
            else:
                results.append(res)

        return BatchAnalysisResult(
            total_files=len(audio_members),
            analyzed_files=len(results),
            failed_files=len(errors),
            results=results,
            errors=errors
        )

    except HTTPException:
        raise
    except zipfile.BadZipFile:
        raise HTTPException(status_code=400, detail="Повреждённый ZIP-архив")
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Ошибка обработки архива: {str(e)}")
    finally:
        if zip_temp and os.path.exists(zip_temp.name):
            try:
                os.unlink(zip_temp.name)
            except Exception:
                pass
        if extract_dir and os.path.exists(extract_dir):
            try:
                shutil.rmtree(extract_dir)
            except Exception:
                pass


@app.post("/waveform")
async def get_waveform(file: UploadFile = File(...)):
    content = await file.read(MAX_UPLOAD_BYTES + 1)
    if len(content) > MAX_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail="Файл слишком большой")

    safe_name = _sanitize_filename(file.filename or "upload")
    suffix = Path(safe_name).suffix.lower() or ".tmp"

    temp_file = None
    try:
        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)
        temp_file.write(content)
        temp_file.close()
        loop = asyncio.get_event_loop()
        waveform = await loop.run_in_executor(None, AudioProcessor.extract_waveform, temp_file.name)
        return JSONResponse(content=waveform)
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        if temp_file and os.path.exists(temp_file.name):
            try:
                os.unlink(temp_file.name)
            except Exception:
                pass
