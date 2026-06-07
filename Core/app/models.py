from pydantic import BaseModel, Field
from typing import List, Dict, Any, Optional
from enum import Enum


class DetectorType(str, Enum):
    SILENCE = "silence"
    PITCH = "pitch"
    SPLICE = "splice"
    AI = "ai"


class TimeMarker(BaseModel):
    start_time: float = Field(..., description="Начало в секундах")
    end_time: float = Field(..., description="Конец в секундах")
    confidence: float = Field(..., ge=0.0, le=1.0)
    description: str = Field(default="")


class DetectionResult(BaseModel):
    type: DetectorType
    title: str
    confidence: float = Field(..., ge=0.0, le=1.0)
    description: str
    markers: List[TimeMarker] = Field(default_factory=list)
    additional_data: Dict[str, Any] = Field(default_factory=dict)


class SpectrogramData(BaseModel):
    frequencies: List[float] = Field(default_factory=list)
    times: List[float] = Field(default_factory=list)
    magnitudes: List[List[float]] = Field(default_factory=list)
    suspicious_regions: List[Dict[str, Any]] = Field(default_factory=list)


class AudioMetadata(BaseModel):
    duration_seconds: float
    sample_rate: int
    channels: int
    format: str = "wav"


class AnalysisResult(BaseModel):
    audio_file_id: str
    file_name: str = ""
    overall_confidence: float = Field(..., ge=0.0, le=1.0)
    is_suspicious: bool
    detections: List[DetectionResult]
    metadata: AudioMetadata
    spectrogram_data: Optional[SpectrogramData] = None


class BatchAnalysisResult(BaseModel):
    total_files: int
    analyzed_files: int
    failed_files: int
    results: List[AnalysisResult]
    errors: List[Dict[str, Any]] = Field(default_factory=list)


class HealthResponse(BaseModel):
    status: str = "healthy"
    version: str = "3.1.0"
    detectors: List[str] = ["silence", "pitch", "splice", "ai"]
    error: Optional[str] = None
