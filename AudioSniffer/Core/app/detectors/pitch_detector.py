import librosa
import numpy as np
from scipy import stats
from typing import List
from app.models import DetectionResult, TimeMarker, DetectorType


class PitchDetector:

    def __init__(self,
                 frame_length: int = 2048,
                 hop_length: int = 512,
                 anomaly_threshold: float = 2.5):
        self.frame_length = frame_length
        self.hop_length = hop_length
        self.anomaly_threshold = anomaly_threshold

    def analyze(self, audio_path: str) -> DetectionResult:

        y, sr = librosa.load(audio_path, sr=None)

        pitches, magnitudes = librosa.piptrack(
            y=y,
            sr=sr,
            fmin=50,
            fmax=400,
            n_fft=self.frame_length,
            hop_length=self.hop_length
        )

        pitch_values = []
        for t in range(pitches.shape[1]):
            index = magnitudes[:, t].argmax()
            pitch = pitches[index, t]
            if pitch > 0:
                pitch_values.append(pitch)

        if len(pitch_values) == 0:
            return DetectionResult(
                type=DetectorType.PITCH,
                title="Анализ тона",
                confidence=0.0,
                description="Недостаточно данных для анализа тона",
                markers=[],
                additional_data={}
            )

        pitch_array = np.array(pitch_values)
        mean_pitch = np.mean(pitch_array)
        std_pitch = np.std(pitch_array)

        z_scores = np.abs(stats.zscore(pitch_array))
        anomaly_indices = np.where(z_scores > self.anomaly_threshold)[0]

        markers = self._group_anomalies(anomaly_indices, pitch_array, sr)
        confidence = min(len(markers) * 0.15, 1.0)

        return DetectionResult(
            type=DetectorType.PITCH,
            title="Анализ тона",
            confidence=confidence,
            description=f"Обнаружено {len(markers)} аномалий в тональности",
            markers=markers,
            additional_data={
                "mean_pitch_hz": float(mean_pitch),
                "std_pitch_hz": float(std_pitch),
                "pitch_range_hz": float(np.max(pitch_array) - np.min(pitch_array)),
                "anomaly_count": len(anomaly_indices)
            }
        )

    def _group_anomalies(self, indices: np.ndarray, pitch_array: np.ndarray, sr: int) -> List[TimeMarker]:
        markers = []

        if len(indices) == 0:
            return markers

        groups = []
        current_group = [indices[0]]

        for i in range(1, len(indices)):
            if indices[i] - indices[i - 1] <= 5:
                current_group.append(indices[i])
            else:
                groups.append(current_group)
                current_group = [indices[i]]
        groups.append(current_group)

        for group in groups:
            start_idx = group[0]
            end_idx = group[-1]

            start_time = start_idx * self.hop_length / sr
            end_time = end_idx * self.hop_length / sr

            pitch_change = abs(pitch_array[end_idx] - pitch_array[start_idx])
            confidence = min(pitch_change / 100.0, 1.0)

            markers.append(TimeMarker(
                start_time=start_time,
                end_time=end_time,
                confidence=confidence,
                description=f"Резкое изменение тона: {pitch_change:.1f} Гц"
            ))

        return markers