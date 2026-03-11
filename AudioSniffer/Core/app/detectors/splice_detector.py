import librosa
import numpy as np
from app.models import DetectionResult, TimeMarker, DetectorType
from app.utils.audio_processor import AudioProcessor


class SpliceDetector:

    def __init__(self,
                 n_fft: int = 2048,
                 hop_length: int = 512,
                 discontinuity_threshold: float = 0.75):
        self.n_fft = n_fft
        self.hop_length = hop_length
        self.discontinuity_threshold = discontinuity_threshold

    def analyze(self, audio_path: str) -> DetectionResult:
        y, sr = AudioProcessor.load_audio(audio_path)

        stft = librosa.stft(y, n_fft=self.n_fft, hop_length=self.hop_length)
        magnitude = np.abs(stft)
        phase = np.angle(stft)

        spectral_flux = np.sqrt(np.sum(np.diff(magnitude, axis=1) ** 2, axis=0))
        if np.max(spectral_flux) > 0:
            spectral_flux /= np.max(spectral_flux)

        phase_diff = np.abs(np.diff(phase, axis=1))
        phase_discontinuity = np.mean(phase_diff, axis=0)
        if np.max(phase_discontinuity) > 0:
            phase_discontinuity /= np.max(phase_discontinuity)

        energy = librosa.feature.rms(y=y, frame_length=self.n_fft, hop_length=self.hop_length)[0]
        energy_diff = np.abs(np.diff(energy))
        if np.max(energy_diff) > 0:
            energy_diff /= np.max(energy_diff)

        zcr = librosa.feature.zero_crossing_rate(y, frame_length=self.n_fft, hop_length=self.hop_length)[0]
        zcr_diff = np.abs(np.diff(zcr))
        if np.max(zcr_diff) > 0:
            zcr_diff /= np.max(zcr_diff)

        min_len = min(len(spectral_flux), len(phase_discontinuity), len(energy_diff), len(zcr_diff))
        combined = (
            spectral_flux[:min_len] * 0.35 +
            phase_discontinuity[:min_len] * 0.30 +
            energy_diff[:min_len] * 0.20 +
            zcr_diff[:min_len] * 0.15
        )

        candidates = np.where(combined > self.discontinuity_threshold)[0]
        markers = self._filter_splices(candidates, combined, sr)

        chroma = librosa.feature.chroma_stft(y=y, sr=sr, hop_length=self.hop_length)
        chroma_diff = np.sum(np.abs(np.diff(chroma, axis=1)), axis=0)
        if np.max(chroma_diff) > 0:
            chroma_diff /= np.max(chroma_diff)
        chroma_jumps = int(np.sum(chroma_diff > 0.8))

        spectral_rolloff = librosa.feature.spectral_rolloff(y=y, sr=sr, hop_length=self.hop_length)[0]
        rolloff_cv = float(np.std(spectral_rolloff) / (np.mean(spectral_rolloff) + 1e-8))

        splice_score = float(min(len(markers) * 0.18, 0.55))
        chroma_score = float(min(chroma_jumps * 0.04, 0.25))
        rolloff_score = 0.2 if rolloff_cv < 0.08 else 0.0

        confidence = float(min(splice_score + chroma_score + rolloff_score, 1.0))

        return DetectionResult(
            type=DetectorType.SPLICE,
            title="Поиск склеек и разрывов",
            confidence=confidence,
            description=(
                f"Найдено {len(markers)} точек склейки. "
                f"Хроматических скачков: {chroma_jumps}. "
                f"CV rolloff: {rolloff_cv:.3f}"
            ),
            markers=markers,
            additional_data={
                "confirmed_splices": len(markers),
                "chroma_jumps": chroma_jumps,
                "spectral_rolloff_cv": round(rolloff_cv, 4),
                "avg_spectral_flux": round(float(np.mean(spectral_flux)), 4)
            }
        )

    def _filter_splices(self, candidates: np.ndarray, scores: np.ndarray, sr: int) -> list:
        markers = []
        if len(candidates) == 0:
            return markers

        min_distance = int(0.4 * sr / self.hop_length)

        filtered = [candidates[0]]
        for i in range(1, len(candidates)):
            if candidates[i] - filtered[-1] > min_distance:
                filtered.append(candidates[i])

        for idx in filtered:
            time_sec = float(idx * self.hop_length / sr)
            conf = float(min(scores[idx], 1.0))
            markers.append(TimeMarker(
                start_time=max(0.0, time_sec - 0.08),
                end_time=time_sec + 0.08,
                confidence=conf,
                description="Разрыв спектра и фазы: возможная точка монтажа"
            ))

        return markers