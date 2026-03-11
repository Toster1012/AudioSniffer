from fastapi import FastAPI, File, UploadFile, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pathlib import Path
import tempfile
import os
from datetime import datetime

from app.models import AnalysisResult, HealthResponse, AudioMetadata
from app.detectors.silence_detector import SilenceDetector
from app.detectors.pitch_detector import PitchDetector
from app.detectors.splice_detector import SpliceDetector
from app.utils.audio_processor import AudioProcessor

app = FastAPI(
    title="AudioSniffer ML Service",
    description="Микросервис для детекции поддельных аудиозаписей",
    version="2.0.0"
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
    "silence": 0.30,
    "pitch":   0.40,
    "splice":  0.30,
}


def compute_overall_confidence(detections: list) -> float:
    if not detections:
        return 0.0

    type_map = {d.type.value: d.confidence for d in detections}

    weighted = sum(
        type_map.get(name, 0.0) * weight
        for name, weight in DETECTOR_WEIGHTS.items()
    )

    peak = max(d.confidence for d in detections)
    combined = weighted * 0.65 + peak * 0.35

    return float(min(combined, 1.0))


@app.get("/", response_model=HealthResponse)
async def root():
    return HealthResponse()


@app.get("/health", response_model=HealthResponse)
async def health_check():
    try:
        SilenceDetector()
        PitchDetector()
        SpliceDetector()
        return HealthResponse(
            status="healthy",
            version="2.0.0",
            detectors=["silence", "pitch", "splice"]
        )
    except Exception as e:
        import traceback
        print(f"Health check failed: {e}\n{traceback.format_exc()}")
        return HealthResponse(
            status="unhealthy",
            version="2.0.0",
            detectors=[],
            error=str(e)
        )


@app.post("/analyze", response_model=AnalysisResult)
async def analyze_audio(
        file: UploadFile = File(...),
        audio_file_id: str = None
):
    if audio_file_id is None:
        audio_file_id = f"audio_{datetime.utcnow().timestamp()}"

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

        print(f"Original filename: {file.filename}, decoded: {decoded_filename}")
        suffix = Path(decoded_filename).suffix.lower()
        if not suffix:
            suffix = '.tmp'
        print(f"Extracted suffix: {suffix}")

        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)
        content = await file.read()
        temp_file.write(content)
        temp_file.close()

        print(f"Temp file: {temp_file.name}, size: {os.path.getsize(temp_file.name)}")

        is_valid, message = AudioProcessor.validate_audio_file(temp_file.name)
        if not is_valid:
            raise HTTPException(status_code=400, detail=message)

        metadata = AudioProcessor.extract_metadata(temp_file.name)

        silence_result = SilenceDetector().analyze(temp_file.name)
        pitch_result   = PitchDetector().analyze(temp_file.name)
        splice_result  = SpliceDetector().analyze(temp_file.name)

        detections = [silence_result, pitch_result, splice_result]

        overall_confidence = compute_overall_confidence(detections)
        is_suspicious = overall_confidence > 0.28

        return AnalysisResult(
            audio_file_id=audio_file_id,
            overall_confidence=overall_confidence,
            is_suspicious=is_suspicious,
            detections=detections,
            metadata=metadata
        )

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


@app.post("/analyze/silence")
async def analyze_silence_only(file: UploadFile = File(...)):
    return await _run_single_detector(file, SilenceDetector())


@app.post("/analyze/pitch")
async def analyze_pitch_only(file: UploadFile = File(...)):
    return await _run_single_detector(file, PitchDetector())


@app.post("/analyze/splice")
async def analyze_splice_only(file: UploadFile = File(...)):
    return await _run_single_detector(file, SpliceDetector())


async def _run_single_detector(file: UploadFile, detector):
    temp_file = None
    try:
        suffix = Path(file.filename or "upload").suffix or '.tmp'
        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)
        content = await file.read()
        temp_file.write(content)
        temp_file.close()
        return detector.analyze(temp_file.name)
    finally:
        if temp_file and os.path.exists(temp_file.name):
            os.unlink(temp_file.name)