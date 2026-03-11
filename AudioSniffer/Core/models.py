from pydantic import BaseModel, Field, field_validator
from typing import List, Dict, Any, Optional
from enum import Enum

class DetectorType(str, Enum):
    SILENCE = "silence"
    PITCH = "pitch"
    SPLICE = "splice"

class TimeMarker(BaseModel):
    start_time: float = Field(..., ge=0.0)
    end_time: float = Field(..., ge=0.0)
    confidence: float = Field(..., ge=0.0, le=1.0)
    description: str = Field(default="")

    @field_validator('end_time')
    def validate_end_time(cls, v, info):
        if 'start_time' in info.data and v < info.data['start_time']:
            raise ValueError('end_time must be greater than or equal to start_time')
        return v

class DetectionResult(BaseModel):
    type: DetectorType
    title: str
    confidence: float = Field(..., ge=0.0, le=1.0)
    description: str
    markers: List[TimeMarker] = Field(default_factory=list)
    additional_data: Dict[str, Any] = Field(default_factory=dict)

class AudioMetadata(BaseModel):
    duration_seconds: float = Field(..., gt=0.0)
    sample_rate: int = Field(..., ge=8000)
    channels: int = Field(..., ge=1)
    format: str = Field(default="wav")

class AnalysisResult(BaseModel):
    audio_file_id: str
    overall_confidence: float = Field(..., ge=0.0, le=1.0)
    is_neural_network: bool
    detections: List[DetectionResult]
    metadata: AudioMetadata

class HealthResponse(BaseModel):
    status: str = "healthy"
    version: str = "1.0.0"
    detectors: List[str] = ["silence", "pitch", "splice"]