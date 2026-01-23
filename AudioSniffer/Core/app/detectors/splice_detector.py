import librosa
import numpy as np
from typing import List
from app.models import DetectionResult, TimeMarker, DetectorType


class SpliceDetector:

    def __init__(self,
                 n_fft: int = 2048,
                 hop_length: int = 512,
                 discontinuity_threshold: float = 0.7):
        self.n_fft = n_fft
        self.hop_length = hop_length
        self.discontinuity_threshold = discontinuity_threshold

    def analyze(self, audio_path: str) -> DetectionResult:

        y, sr = librosa.load(audio_path, sr=None)

        stft = librosa.stft(y, n_fft=self.n_fft, hop_length=self.hop_length)
        magnitude = np.abs(stft)

        spectral_flux = np.sqrt(np.sum(np.diff(magnitude, axis=1) ** 2, axis=0))
        spectral_flux = spectral_flux / np.max(spectral_flux) if np.max(spectral_flux) > 0 else spectral_flux

        zcr = librosa.feature.zero_crossing_rate(y, frame_length=self.n_fft, hop_length=self.hop_length)[0]

        energy = librosa.feature.rms(y=y, frame_length=self.n_fft, hop_length=self.hop_length)[0]
        energy_diff = np.abs(np.diff(energy))
        energy_diff = energy_diff / np.max(energy_diff) if np.max(energy_diff) > 0 else energy_diff

        min_len = min(len(spectral_flux), len(zcr), len(energy_diff))
        combined_score = (spectral_flux[:min_len] + energy_diff[:min_len]) / 2

        splice_candidates = np.where(combined_score > self.discontinuity_threshold)[0]

        markers = self._filter_and_group_splices(splice_candidates, combined_score, sr)

        confidence = min(len(markers) * 0.2, 1.0)

        return DetectionResult(
            type=DetectorType.SPLICE,
            title="Поиск склеек",
            confidence=confidence,
            description=f"Обнаружено {len(markers)} возможных склеек",
            markers=markers,
            additional_data={
                "total_candidates": len(splice_candidates),
                "confirmed_splices": len(markers),
                "avg_spectral_flux": float(np.mean(spectral_flux)),
            }
        )

    def _filter_and_group_splices(self, candidates: np.ndarray, scores: np.ndarray, sr: int) -> List[TimeMarker]:
        markers = []

        if len(candidates) == 0:
            return markers

        min_distance = int(0.5 * sr / self.hop_length)

        filtered = [candidates[0]]
        for i in range(1, len(candidates)):
            if candidates[i] - filtered[-1] > min_distance:
                filtered.append(candidates[i])

        for idx in filtered:
            time_sec = idx * self.hop_length / sr
            confidence = float(scores[idx])

            markers.append(TimeMarker(
                start_time=max(0, time_sec - 0.1),
                end_time=time_sec + 0.1,
                confidence=confidence,
                description="Возможная склейка: резкий разрыв в спектре"
            ))

        return markers