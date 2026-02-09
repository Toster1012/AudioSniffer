from fastapi import FastAPI, File, UploadFile, HTTPException, Form
from fastapi.middleware.cors import CORSMiddleware
from pathlib import Path
import tempfile
import os
import logging
from datetime import datetime
from typing import Optional

from app.models import AnalysisResult, HealthResponse
from app.detectors.silence_detector import SilenceDetector
from app.detectors.pitch_detector import PitchDetector
from app.detectors.splice_detector import SpliceDetector
from app.utils.audio_processor import AudioProcessor

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(
    title="AudioSniffer ML Service",
    description="Audio fake detection service",
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
    return HealthResponse(status="healthy", version="1.0.0")


@app.post("/analyze", response_model=AnalysisResult)
async def analyze_audio(
        file: UploadFile = File(...),
        audio_file_id: Optional[str] = Form(None)
):
    if audio_file_id is None:
        audio_file_id = f"audio_{datetime.utcnow().timestamp()}"

    temp_file = None

    try:
        if file.size and file.size > 50 * 1024 * 1024:
            raise HTTPException(status_code=400, detail="File too large. Max 50 MB")

        suffix = Path(file.filename or "audio.wav").suffix
        if suffix.lower() not in AudioProcessor.SUPPORTED_FORMATS:
            raise HTTPException(status_code=400, detail="Unsupported format")

        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)
        content = await file.read()

        if len(content) == 0:
            raise HTTPException(status_code=400, detail="Empty file")

        temp_file.write(content)
        temp_file.close()

        is_valid, message = AudioProcessor.validate_audio_file(temp_file.name)
        if not is_valid:
            raise HTTPException(status_code=400, detail=message)

        metadata = AudioProcessor.extract_metadata(temp_file.name)

        if metadata.duration_seconds < 1.0:
            raise HTTPException(status_code=400, detail="Audio too short. Minimum 1 second")

        if metadata.duration_seconds > 600:
            raise HTTPException(status_code=400, detail="Audio too long. Maximum 10 minutes")

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

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Analysis error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Analysis failed: {str(e)}")
    finally:
        if temp_file and os.path.exists(temp_file.name):
            try:
                os.unlink(temp_file.name)
            except Exception as e:
                logger.error(f"Cannot delete temp file: {e}")