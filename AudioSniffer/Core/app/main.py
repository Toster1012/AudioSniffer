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
    version="1.0.0"
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


@app.get("/", response_model=HealthResponse)
async def root():
    return HealthResponse()


@app.get("/health", response_model=HealthResponse)
async def health_check():
    try:
        # Test that all detectors can be initialized
        silence_detector = SilenceDetector()
        pitch_detector = PitchDetector()
        splice_detector = SpliceDetector()

        return HealthResponse(
            status="healthy",
            version="1.0.0",
            detectors=["silence", "pitch", "splice"]
        )
    except Exception as e:
        import traceback
        error_details = f"Health check failed: {str(e)}\n{traceback.format_exc()}"
        print(error_details)
        return HealthResponse(
            status="unhealthy",
            version="1.0.0",
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
        suffix = Path(file.filename).suffix
        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)

        content = await file.read()
        temp_file.write(content)
        temp_file.close()

        is_valid, message = AudioProcessor.validate_audio_file(temp_file.name)
        if not is_valid:
            raise HTTPException(status_code=400, detail=message)

        metadata = AudioProcessor.extract_metadata(temp_file.name)

        silence_detector = SilenceDetector()
        pitch_detector = PitchDetector()
        splice_detector = SpliceDetector()

        silence_result = silence_detector.analyze(temp_file.name)
        pitch_result = pitch_detector.analyze(temp_file.name)
        splice_result = splice_detector.analyze(temp_file.name)

        detections = [silence_result, pitch_result, splice_result]

        overall_confidence = sum(d.confidence for d in detections) / len(detections)
        is_suspicious = overall_confidence > 0.5

        return AnalysisResult(
            audio_file_id=audio_file_id,
            overall_confidence=overall_confidence,
            is_suspicious=is_suspicious,
            detections=detections,
            metadata=metadata
        )

    except Exception as e:
        import traceback
        error_details = f"Ошибка анализа: {str(e)}\n{traceback.format_exc()}"
        print(error_details)  # Логирование в консоль для отладки
        raise HTTPException(status_code=500, detail=f"Ошибка анализа: {str(e)}")

    finally:
        if temp_file and os.path.exists(temp_file.name):
            try:
                os.unlink(temp_file.name)
            except Exception as e:
                print(f"Не удалось удалить временный файл: {str(e)}")


@app.post("/analyze/silence")
async def analyze_silence_only(file: UploadFile = File(...)):
    temp_file = None
    try:
        suffix = Path(file.filename).suffix
        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)

        content = await file.read()
        temp_file.write(content)
        temp_file.close()

        detector = SilenceDetector()
        result = detector.analyze(temp_file.name)

        return result

    finally:
        if temp_file and os.path.exists(temp_file.name):
            os.unlink(temp_file.name)


@app.post("/analyze/pitch")
async def analyze_pitch_only(file: UploadFile = File(...)):
    temp_file = None
    try:
        suffix = Path(file.filename).suffix
        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)

        content = await file.read()
        temp_file.write(content)
        temp_file.close()

        detector = PitchDetector()
        result = detector.analyze(temp_file.name)

        return result

    finally:
        if temp_file and os.path.exists(temp_file.name):
            os.unlink(temp_file.name)


@app.post("/analyze/splice")
async def analyze_splice_only(file: UploadFile = File(...)):
    temp_file = None
    try:
        suffix = Path(file.filename).suffix
        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)

        content = await file.read()
        temp_file.write(content)
        temp_file.close()

        detector = SpliceDetector()
        result = detector.analyze(temp_file.name)

        return result

    finally:
        if temp_file and os.path.exists(temp_file.name):
            os.unlink(temp_file.name)