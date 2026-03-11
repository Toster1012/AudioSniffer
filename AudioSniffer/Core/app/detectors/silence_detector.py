import librosa
import numpy as np
from app.models import DetectionResult, TimeMarker, DetectorType
from app.utils.audio_processor import AudioProcessor


class SilenceDetector:

    def __init__(self,
                 top_db: float = 35.0,
                 min_silence_duration: float = 0.25,
                 max_natural_silence: float = 2.5):
        self.top_db = top_db
        self.min_silence_duration = min_silence_duration
        self.max_natural_silence = max_natural_silence

    def analyze(self, audio_path: str) -> DetectionResult:
        y, sr = AudioProcessor.load_audio(audio_path)

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

        rms = librosa.feature.rms(y=y, frame_length=2048, hop_length=512)[0]
        rms_mean = float(np.mean(rms))
        rms_std = float(np.std(rms))
        rms_cv = rms_std / (rms_mean + 1e-8)

        markers = []
        suspicious_count = 0
        scores = []

        for interval in silence_intervals:
            score = self._score_interval(interval, y, sr, rms_mean)
            scores.append(score)
            if score > 0.5:
                suspicious_count += 1
                markers.append(TimeMarker(
                    start_time=interval['start'],
                    end_time=interval['end'],
                    confidence=float(min(score, 1.0)),
                    description=self._describe(interval, score)
                ))

        total = len(silence_intervals)

        silence_score = (suspicious_count / max(total, 1)) * 0.5 if total > 0 else 0.0

        uniformity_score = 0.0
        if len(scores) >= 3:
            durations = [s['duration'] for s in silence_intervals]
            dur_std = float(np.std(durations))
            if dur_std < 0.05 and total >= 4:
                uniformity_score = 0.35

        dynamic_score = 0.0
        if rms_cv < 0.15 and librosa.get_duration(y=y, sr=sr) > 3.0:
            dynamic_score = 0.25

        confidence = float(min(silence_score + uniformity_score + dynamic_score, 1.0))

        return DetectionResult(
            type=DetectorType.SILENCE,
            title="Анализ пауз и динамики",
            confidence=confidence,
            description=(
                f"Обнаружено {total} пауз, {suspicious_count} подозрительных. "
                f"Вариативность динамики: {rms_cv:.2f}"
            ),
            markers=markers,
            additional_data={
                "total_silences": total,
                "suspicious_silences": suspicious_count,
                "rms_coefficient_of_variation": round(rms_cv, 4),
                "silence_uniformity_score": round(uniformity_score, 4),
                "avg_silence_duration": round(float(np.mean([s['duration'] for s in silence_intervals])), 4) if silence_intervals else 0.0
            }
        )

    def _score_interval(self, interval: dict, y: np.ndarray, sr: int, rms_mean: float) -> float:
        score = 0.0
        duration = interval['duration']

        if duration > self.max_natural_silence:
            score += 0.4
        elif duration > self.max_natural_silence * 0.6:
            score += 0.2

        start_s = int(interval['start'] * sr)
        end_s = int(interval['end'] * sr)
        segment = y[start_s:end_s]

        if len(segment) > 0:
            rms = float(np.sqrt(np.mean(segment ** 2)))
            if rms < 1e-5:
                score += 0.3
            elif rms < rms_mean * 0.02:
                score += 0.15

        window = sr // 10
        if end_s + window < len(y):
            after = y[end_s:end_s + window]
            energy_jump = float(np.max(np.abs(after)))
            if energy_jump > 0.6:
                score += 0.25
            elif energy_jump > 0.4:
                score += 0.1

        if start_s > window:
            before = y[start_s - window:start_s]
            before_rms = float(np.sqrt(np.mean(before ** 2)))
            after_segment = y[end_s:end_s + window] if end_s + window < len(y) else np.array([])
            if len(after_segment) > 0:
                after_rms = float(np.sqrt(np.mean(after_segment ** 2)))
                ratio = max(before_rms, after_rms) / (min(before_rms, after_rms) + 1e-8)
                if ratio > 8.0:
                    score += 0.2

        return score

    def _describe(self, interval: dict, score: float) -> str:
        duration = interval['duration']
        if duration > self.max_natural_silence:
            return f"Неестественно длинная пауза ({duration:.2f}с)"
        elif score > 0.7:
            return "Резкий переход: возможная точка склейки"
        else:
            return f"Подозрительная пауза ({duration:.2f}с)"