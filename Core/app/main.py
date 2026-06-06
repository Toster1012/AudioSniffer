from fastapi import FastAPI, File, UploadFile, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pathlib import Path
import tempfile
import os
import zipfile
import asyncio
from datetime import datetime
from typing import List

from app.models import AnalysisResult, HealthResponse, AudioMetadata, BatchAnalysisResult
from app.detectors.silence_detector import SilenceDetector
from app.detectors.pitch_detector import PitchDetector
from app.detectors.splice_detector import SpliceDetector
from app.detectors.ai_detector import AIDetector
from app.utils.audio_processor import AudioProcessor

app = FastAPI(
    title="AudioSniffer ML Service",
    description="Микросервис для детекции поддельных аудиозаписей",
    version="3.0.0"
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

UPLOAD_DIR = Path("uploads")
UPLOAD_DIR.mkdir(exist_ok=True)

DETECTOR_WEIGHTS = {
    "silence": 0.03,
    "pitch":   0.12,
    "splice":  0.15,
    "ai":      0.70,
}

SUPPORTED_AUDIO = {'.wav', '.mp3', '.ogg', '.flac', '.m4a', '.aac'}


def compute_overall_confidence(detections: list) -> float:
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

    metadata = AudioProcessor.extract_metadata(file_path)
    spectrogram_data = AudioProcessor.extract_spectrogram(file_path)

    silence_result = SilenceDetector().analyze(file_path)
    pitch_result   = PitchDetector().analyze(file_path)
    splice_result  = SpliceDetector().analyze(file_path)
    ai_result      = AIDetector().analyze(file_path)

    detections = [silence_result, pitch_result, splice_result, ai_result]
    overall_confidence = compute_overall_confidence(detections)
    is_suspicious = overall_confidence > 0.30

    audio_file_id = f"audio_{datetime.utcnow().timestamp()}_{file_name}"

    return AnalysisResult(
        audio_file_id=audio_file_id,
        file_name=file_name,
        overall_confidence=overall_confidence,
        is_suspicious=is_suspicious,
        detections=detections,
        metadata=metadata,
        spectrogram_data=spectrogram_data
    )


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
        return HealthResponse(
            status="healthy",
            version="3.0.0",
            detectors=["silence", "pitch", "splice", "ai"]
        )
    except Exception as e:
        import traceback
        print(f"Health check failed: {e}\n{traceback.format_exc()}")
        return HealthResponse(
            status="unhealthy",
            version="3.0.0",
            detectors=[],
            error=str(e)
        )


@app.post("/analyze", response_model=AnalysisResult)
async def analyze_audio(
        file: UploadFile = File(...),
        audio_file_id: str = None
):
    temp_file = None
    try:
        decoded_filename = file.filename or "upload"
        if file.filename and '=' in file.filename:
            try:
                from email.header import decode_header
                decoded_header = decode_header(file.filename)
                if decoded_header and decoded_header[0]:
                    part = decoded_header[0][0]
                    if isinstance(part, bytes):
                        decoded_filename = part.decode(decoded_header[0][1] or 'utf-8')
                    else:
                        decoded_filename = part
            except Exception as e:
                print(f"Failed to decode filename: {e}")

        suffix = Path(decoded_filename).suffix.lower()
        if not suffix:
            suffix = '.tmp'

        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)
        content = await file.read()
        temp_file.write(content)
        temp_file.close()

        result = await _analyze_file(temp_file.name, decoded_filename)
        return result

    except HTTPException:
        raise
    except Exception as e:
        import traceback
        print(f"Ошибка анализа: {e}\n{traceback.format_exc()}")
        raise HTTPException(status_code=500, detail=f"Ошибка анализа: {str(e)}")

    finally:
        if temp_file and os.path.exists(temp_file.name):
            try:
                os.unlink(temp_file.name)
            except Exception as e:
                print(f"Не удалось удалить временный файл: {str(e)}")


@app.post("/analyze/batch", response_model=BatchAnalysisResult)
async def analyze_zip(file: UploadFile = File(...)):
    """Анализ ZIP-архива с аудиофайлами"""
    if not file.filename or not file.filename.lower().endswith('.zip'):
        raise HTTPException(status_code=400, detail="Ожидается ZIP-архив")

    zip_temp = None
    extract_dir = None

    try:
        zip_temp = tempfile.NamedTemporaryFile(delete=False, suffix='.zip')
        content = await file.read()
        zip_temp.write(content)
        zip_temp.close()

        extract_dir = tempfile.mkdtemp()

        with zipfile.ZipFile(zip_temp.name, 'r') as zf:
            members = zf.namelist()
            audio_members = [
                m for m in members
                if Path(m).suffix.lower() in SUPPORTED_AUDIO
                and not m.startswith('__MACOSX')
                and not Path(m).name.startswith('.')
            ]

            if not audio_members:
                raise HTTPException(
                    status_code=400,
                    detail=f"В архиве не найдено аудиофайлов. Поддерживаемые форматы: {', '.join(sorted(SUPPORTED_AUDIO))}"
                )

            if len(audio_members) > 20:
                raise HTTPException(status_code=400, detail="Максимум 20 аудиофайлов в архиве")

            zf.extractall(extract_dir)

        results = []
        errors = []

        for member in audio_members:
            file_path = os.path.join(extract_dir, member)
            file_name = Path(member).name
            try:
                result = await _analyze_file(file_path, file_name)
                results.append(result)
            except Exception as e:
                errors.append({"file": file_name, "error": str(e)})

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
        raise HTTPException(status_code=400, detail="Повреждённый или невалидный ZIP-архив")
    except Exception as e:
        import traceback
        print(f"Ошибка batch-анализа: {e}\n{traceback.format_exc()}")
        raise HTTPException(status_code=500, detail=f"Ошибка обработки архива: {str(e)}")

    finally:
        if zip_temp and os.path.exists(zip_temp.name):
            try:
                os.unlink(zip_temp.name)
            except Exception:
                pass
        if extract_dir and os.path.exists(extract_dir):
            import shutil
            try:
                shutil.rmtree(extract_dir)
            except Exception:
                pass


@app.post("/waveform")
async def get_waveform(file: UploadFile = File(...)):
    """Возвращает форму волны для визуализации"""
    temp_file = None
    try:
        suffix = Path(file.filename or "upload").suffix.lower() or '.tmp'
        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)
        content = await file.read()
        temp_file.write(content)
        temp_file.close()

        waveform = AudioProcessor.extract_waveform(temp_file.name)
        return JSONResponse(content=waveform)
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        if temp_file and os.path.exists(temp_file.name):
            try:
                os.unlink(temp_file.name)
            except Exception:
                pass
