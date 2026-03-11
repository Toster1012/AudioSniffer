from fastapi import FastAPI, File, UploadFile, HTTPException, Form
from fastapi.middleware.cors import CORSMiddleware
from pathlib import Path
import tempfile
import os
import logging
from datetime import datetime
from typing import Optional

import librosa
import numpy as np

from app.models import AnalysisResult, AudioMetadata, DetectionResult, DetectorType, TimeMarker
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

@app.get("/")
async def root():
    return {"status": "healthy", "version": "1.0.0", "detectors": ["silence", "pitch", "splice"]}

@app.get("/health")
async def health_check():
    return {"status": "healthy", "version": "1.0.0", "detectors": ["silence", "pitch", "splice"]}

@app.post("/analyze")
async def analyze_audio(
        file: UploadFile = File(...),
        audio_file_id: Optional[str] = Form(None)
):
    if audio_file_id is None:
        audio_file_id = f"audio_{datetime.utcnow().timestamp():.0f}"

    temp_file = None

    try:
        if file.size and file.size > 50 * 1024 * 1024:
            raise HTTPException(status_code=400, detail="File too large. Max 50 MB")

        suffix = Path(file.filename or "audio.wav").suffix.lower()
        if suffix not in AudioProcessor.SUPPORTED_FORMATS:
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

        audio_data, sample_rate = librosa.load(temp_file.name, sr=None)

        mfcc = librosa.feature.mfcc(y=audio_data, sr=sample_rate, n_mfcc=13)
        mfcc_variance = float(np.var(mfcc, axis=1).mean())

        spectral_centroid = librosa.feature.spectral_centroid(y=audio_data, sr=sample_rate)
        spectral_centroid_mean = float(spectral_centroid.mean())
        spectral_centroid_variance = float(np.var(spectral_centroid))

        zero_crossing_rate = librosa.feature.zero_crossing_rate(y=audio_data)
        zero_crossing_mean = float(zero_crossing_rate.mean())

        spectral_rolloff = librosa.feature.spectral_rolloff(y=audio_data, sr=sample_rate)
        spectral_rolloff_mean = float(spectral_rolloff.mean())

        spectral_flatness = librosa.feature.spectral_flatness(y=audio_data)
        spectral_flatness_mean = float(spectral_flatness.mean())

        rms = librosa.feature.rms(y=audio_data)
        rms_variance = float(np.var(rms))

        harmonic, percussive = librosa.effects.hpss(y=audio_data)
        harmonic_ratio = float(np.mean(np.abs(harmonic)) / (np.mean(np.abs(harmonic)) + np.mean(np.abs(percussive)) + 1e-8))

        pitch_variance = 0.0
        try:
            fundamental_frequency, voiced_flag, _ = librosa.pyin(
                y=audio_data,
                fmin=librosa.note_to_hz('C2'),
                fmax=librosa.note_to_hz('C7'),
                sr=sample_rate
            )
            voiced_f0 = fundamental_frequency[voiced_flag]
            if len(voiced_f0) > 0:
                pitch_variance = float(np.var(voiced_f0))
        except Exception:
            pass

        feature_based_ai_score = 0.0
        if mfcc_variance < 150.0:
            feature_based_ai_score += 0.15
        if spectral_centroid_mean < 2000.0:
            feature_based_ai_score += 0.15
        if spectral_centroid_variance < 80000.0:
            feature_based_ai_score += 0.10
        if zero_crossing_mean < 0.07:
            feature_based_ai_score += 0.10
        if spectral_rolloff_mean < 5000.0:
            feature_based_ai_score += 0.10
        if spectral_flatness_mean > 0.25:
            feature_based_ai_score += 0.08
        if rms_variance < 0.008:
            feature_based_ai_score += 0.12
        if harmonic_ratio > 0.82:
            feature_based_ai_score += 0.10
        if pitch_variance < 120.0:
            feature_based_ai_score += 0.10

        silence_weight = 0.22
        pitch_weight = 0.28
        splice_weight = 0.22
        feature_weight = 0.28

        overall_confidence = (
            silence_result.confidence * silence_weight +
            pitch_result.confidence * pitch_weight +
            splice_result.confidence * splice_weight +
            feature_based_ai_score * feature_weight
        )
        overall_confidence = min(1.0, max(0.0, overall_confidence))

        is_neural_network = overall_confidence > 0.58

        return AnalysisResult(
            audio_file_id=audio_file_id,
            overall_confidence=overall_confidence,
            is_neural_network=is_neural_network,
            detections=detections,
            metadata=metadata
        )

    except HTTPException:
        raise
    except Exception as analysis_exception:
        logger.error(f"Analysis error: {analysis_exception}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Analysis failed: {str(analysis_exception)}")
    finally:
        if temp_file and os.path.exists(temp_file.name):
            try:
                os.unlink(temp_file.name)
            except Exception as cleanup_exception:
                logger.error(f"Cannot delete temp file: {cleanup_exception}")