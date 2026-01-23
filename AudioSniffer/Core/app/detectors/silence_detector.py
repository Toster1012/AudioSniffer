import librosa
import numpy as np
from typing import List
from app.models import DetectionResult, TimeMarker, DetectorType


class SilenceDetector:

    def __init__(self,
                 top_db: float = 40.0,
                 min_silence_duration: float = 0.3,
                 max_natural_silence: float = 2.0):
        self.top_db = top_db
        self.min_silence_duration = min_silence_duration
        self.max_natural_silence = max_natural_silence

    def analyze(self, audio_path: str) -> DetectionResult:

        y, sr = librosa.load(audio_path, sr=None)

        intervals = librosa.effects.split(y, top_db=self.top_db)

        silence_intervals = []
        for i in range(len(intervals) - 1):
            silence_start = intervals[i][1] / sr
            silence_end = intervals[i + 1][0] / sr
            silence_duration = silence_end - silence_start

            if silence_duration >= self.min_silence_duration:
                silence_intervals.append({
                    'start': silence_start,
                    'end': silence_end,
                    'duration': silence_duration
                })

        markers = []
        suspicious_count = 0

        for interval in silence_intervals:
            suspicion_score = self._calculate_suspicion(interval, y, sr)

            if suspicion_score > 0.6:
                suspicious_count += 1
                markers.append(TimeMarker(
                    start_time=interval['start'],
                    end_time=interval['end'],
                    confidence=suspicion_score,
                    description=self._get_description(interval, suspicion_score)
                ))

        total_silences = len(silence_intervals)
        confidence = min(suspicious_count / max(total_silences, 1), 1.0) if total_silences > 0 else 0.0

        return DetectionResult(
            type=DetectorType.SILENCE,
            title="Анализ пауз",
            confidence=confidence,
            description=f"Обнаружено {total_silences} пауз, из них {suspicious_count} подозрительных",
            markers=markers,
            additional_data={
                "total_silences": total_silences,
                "suspicious_silences": suspicious_count,
                "avg_silence_duration": np.mean([s['duration'] for s in silence_intervals]) if silence_intervals else 0
            }
        )

    def _calculate_suspicion(self, interval: dict, y: np.ndarray, sr: int) -> float:

        suspicion = 0.0
        duration = interval['duration']

        if duration > self.max_natural_silence:
            suspicion += 0.4

        start_sample = int(interval['start'] * sr)
        end_sample = int(interval['end'] * sr)
        silence_segment = y[start_sample:end_sample]

        if len(silence_segment) > 0:
            rms = np.sqrt(np.mean(silence_segment ** 2))
            if rms < 0.001:
                suspicion += 0.3

        if end_sample + sr // 10 < len(y):
            after_silence = y[end_sample:end_sample + sr // 10]
            transition_energy = np.max(np.abs(after_silence))
            if transition_energy > 0.5:
                suspicion += 0.2

        return min(suspicion, 1.0)

    def _get_description(self, interval: dict, score: float) -> str:
        duration = interval['duration']

        if duration > self.max_natural_silence:
            return f"Неестественно длинная пауза ({duration:.2f}с)"
        elif score > 0.8:
            return "Возможная склейка: резкий переход"
        else:
            return f"Подозрительная пауза ({duration:.2f}с)"