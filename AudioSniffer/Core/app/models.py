from pydantic import BaseModel, Field
from typing import List, Dict, Any, Optional
from enum import Enum


class DetectorType(str, Enum):
    SILENCE = "silence"
    PITCH = "pitch"
    SPLICE = "splice"


class TimeMarker(BaseModel):
    start_time: float = Field(..., description="Начало в секундах")
    end_time: float = Field(..., description="Конец в секундах")
    confidence: float = Field(..., ge=0.0, le=1.0, description="Уверенность 0-1")
    description: str = Field(default="", description="Описание аномалии")


class DetectionResult(BaseModel):
    type: DetectorType
    title: str
    confidence: float = Field(..., ge=0.0, le=1.0)
    description: str
    markers: List[TimeMarker] = Field(default_factory=list)
    additional_data: Dict[str, Any] = Field(default_factory=dict)


class AudioMetadata(BaseModel):
    duration_seconds: float
    sample_rate: int
    channels: int
    format: str = "wav"


class AnalysisResult(BaseModel):
    audio_file_id: str
    overall_confidence: float = Field(..., ge=0.0, le=1.0)
    is_suspicious: bool
    detections: List[DetectionResult]
    metadata: AudioMetadata


class HealthResponse(BaseModel):
    status: str = "healthy"
    version: str = "1.0.0"
    detectors: List[str] = ["silence", "pitch", "splice"]
    error: Optional[str] = None
