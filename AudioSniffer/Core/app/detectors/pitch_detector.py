import librosa
import numpy as np
from scipy import stats
from scipy.signal import medfilt
from app.models import DetectionResult, TimeMarker, DetectorType
from app.utils.audio_processor import AudioProcessor


class PitchDetector:

    def __init__(self,
                 frame_length: int = 2048,
                 hop_length: int = 512,
                 anomaly_threshold: float = 2.8):
        self.frame_length = frame_length
        self.hop_length = hop_length
        self.anomaly_threshold = anomaly_threshold

    def analyze(self, audio_path: str) -> DetectionResult:
        y, sr = AudioProcessor.load_audio(audio_path)

        pitches, magnitudes = librosa.piptrack(
            y=y, sr=sr,
            fmin=60, fmax=500,
            n_fft=self.frame_length,
            hop_length=self.hop_length
        )

        pitch_values = []
        pitch_times = []
        for t in range(pitches.shape[1]):
            idx = magnitudes[:, t].argmax()
            p = pitches[idx, t]
            if p > 0:
                pitch_values.append(p)
                pitch_times.append(t * self.hop_length / sr)

        if len(pitch_values) < 10:
            return DetectionResult(
                type=DetectorType.PITCH,
                title="Анализ тона",
                confidence=0.0,
                description="Недостаточно вокальных данных для анализа",
                markers=[],
                additional_data={}
            )

        pitch_array = np.array(pitch_values)

        smoothed = medfilt(pitch_array, kernel_size=5)

        z_scores = np.abs(stats.zscore(smoothed))
        anomaly_indices = np.where(z_scores > self.anomaly_threshold)[0]

        markers = self._group_anomalies(anomaly_indices, pitch_array, pitch_times)

        mfcc = librosa.feature.mfcc(y=y, sr=sr, n_mfcc=13, hop_length=self.hop_length)
        mfcc_delta = librosa.feature.delta(mfcc)
        mfcc_instability = float(np.mean(np.abs(mfcc_delta)))

        spectral_centroid = librosa.feature.spectral_centroid(y=y, sr=sr, hop_length=self.hop_length)[0]
        centroid_std = float(np.std(spectral_centroid))
        centroid_mean = float(np.mean(spectral_centroid))
        centroid_cv = centroid_std / (centroid_mean + 1e-8)

        zcr = librosa.feature.zero_crossing_rate(y, hop_length=self.hop_length)[0]
        zcr_mean = float(np.mean(zcr))

        anomaly_score = min(len(markers) * 0.12, 0.5)

        mfcc_score = 0.0
        if mfcc_instability < 0.8:
            mfcc_score = 0.2

        centroid_score = 0.0
        if centroid_cv < 0.12:
            centroid_score = 0.15

        zcr_score = 0.0
        if zcr_mean > 0.15:
            zcr_score = 0.15

        confidence = float(min(anomaly_score + mfcc_score + centroid_score + zcr_score, 1.0))

        return DetectionResult(
            type=DetectorType.PITCH,
            title="Анализ тона и спектра",
            confidence=confidence,
            description=(
                f"Аномалий тона: {len(markers)}. "
                f"Нестабильность MFCC: {mfcc_instability:.3f}. "
                f"CV центроида: {centroid_cv:.3f}"
            ),
            markers=markers,
            additional_data={
                "anomaly_count": len(anomaly_indices),
                "mean_pitch_hz": round(float(np.mean(pitch_array)), 2),
                "std_pitch_hz": round(float(np.std(pitch_array)), 2),
                "mfcc_instability": round(mfcc_instability, 4),
                "spectral_centroid_cv": round(centroid_cv, 4),
                "zcr_mean": round(zcr_mean, 4)
            }
        )

    def _group_anomalies(self, indices: np.ndarray, pitch_array: np.ndarray, pitch_times: list) -> list:
        markers = []
        if len(indices) == 0:
            return markers

        groups = []
        current = [indices[0]]
        for i in range(1, len(indices)):
            if indices[i] - indices[i - 1] <= 8:
                current.append(indices[i])
            else:
                groups.append(current)
                current = [indices[i]]
        groups.append(current)

        for group in groups:
            if len(group) < 2:
                continue
            s = group[0]
            e = group[-1]
            if s >= len(pitch_times) or e >= len(pitch_times):
                continue
            pitch_change = float(abs(pitch_array[e] - pitch_array[s]))
            conf = float(min(pitch_change / 80.0, 1.0))
            markers.append(TimeMarker(
                start_time=float(pitch_times[s]),
                end_time=float(pitch_times[e]),
                confidence=conf,
                description=f"Аномалия тона: перепад {pitch_change:.1f} Гц"
            ))

        return markers