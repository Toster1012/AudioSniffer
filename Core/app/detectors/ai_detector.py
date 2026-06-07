import numpy as np
import librosa
from app.models import DetectionResult, TimeMarker, DetectorType
from app.utils.audio_processor import AudioProcessor


class AIDetector:
    def analyze(self, audio_path: str) -> DetectionResult:
        y, sr = AudioProcessor.load_audio(audio_path)
        return self._analyze_array(y, sr)

    def _analyze_array(self, y: np.ndarray, sr: int) -> DetectionResult:
        try:
            hop_length = 512
            n_fft = 2048

            duration = librosa.get_duration(y=y, sr=sr)
            scores: dict[str, float] = {}
            markers: list[TimeMarker] = []

            mfcc = librosa.feature.mfcc(y=y, sr=sr, n_mfcc=40, hop_length=hop_length, n_fft=n_fft)
            mfcc_delta = librosa.feature.delta(mfcc)
            mfcc_delta2 = librosa.feature.delta(mfcc, order=2)

            mfcc_var = float(np.mean(np.std(mfcc, axis=1)))
            delta_mean = float(np.mean(np.abs(mfcc_delta)))
            delta2_mean = float(np.mean(np.abs(mfcc_delta2)))

            mfcc_score = 0.0
            if mfcc_var < 8.0:    mfcc_score = 1.00
            elif mfcc_var < 12.0: mfcc_score = 0.85
            elif mfcc_var < 18.0: mfcc_score = 0.60
            elif mfcc_var < 24.0: mfcc_score = 0.30

            if delta_mean < 0.5:   mfcc_score += 0.15
            if delta2_mean < 0.35: mfcc_score += 0.10
            scores['mfcc'] = min(mfcc_score, 1.0)

            mel_spec = librosa.feature.melspectrogram(
                y=y, sr=sr, n_mels=128, n_fft=n_fft, hop_length=hop_length
            )
            mel_db = librosa.power_to_db(mel_spec, ref=np.max)

            freq_bands = np.array_split(mel_db, 8, axis=0)
            band_stds = np.array([np.std(b) for b in freq_bands])
            band_uniformity = float(1.0 - np.std(band_stds) / (np.mean(band_stds) + 1e-8))
            scores['mel_uniformity'] = float(np.clip(band_uniformity, 0, 1))

            flatness = librosa.feature.spectral_flatness(y=y, hop_length=hop_length)[0]
            mean_flatness = float(np.mean(flatness))
            flatness_std = float(np.std(flatness))

            flat_score = 0.0
            if mean_flatness > 0.10:   flat_score = 1.00
            elif mean_flatness > 0.06: flat_score = 0.75
            elif mean_flatness > 0.03: flat_score = 0.40
            if flatness_std < 0.03: flat_score += 0.25
            scores['flatness'] = min(flat_score, 1.0)

            try:
                f0 = librosa.yin(y, fmin=60, fmax=500, sr=sr, hop_length=hop_length, frame_length=n_fft)
                valid_f0 = f0[(f0 > 50) & (f0 < 800)]
            except Exception:
                valid_f0 = np.array([])

            pitch_score = 0.0
            if len(valid_f0) > 20:
                pitch_cv = float(np.std(valid_f0) / (np.mean(valid_f0) + 1e-8))
                if pitch_cv < 0.06:    pitch_score = 1.00
                elif pitch_cv < 0.12:  pitch_score = 0.85
                elif pitch_cv < 0.22:  pitch_score = 0.50
                elif pitch_cv < 0.35:  pitch_score = 0.20

                window = 30
                step = window // 2
                for i in range(0, len(valid_f0) - window, step):
                    seg = valid_f0[i:i + window]
                    seg_cv = float(np.std(seg) / (np.mean(seg) + 1e-8))
                    if seg_cv < 0.025:
                        t_start = float(i * hop_length / sr)
                        markers.append(TimeMarker(
                            start_time=t_start,
                            end_time=min(t_start + window * hop_length / sr, duration),
                            confidence=min(0.9, 1.0 - seg_cv * 10),
                            description=f"Монотонный тон (CV={seg_cv:.4f})"
                        ))
            scores['pitch'] = min(pitch_score, 1.0)

            harmonic, percussive = librosa.effects.hpss(y, margin=3.0)
            h_power = float(np.mean(harmonic ** 2))
            p_power = float(np.mean(percussive ** 2))
            hnr_db = 10.0 * np.log10(h_power / (p_power + 1e-8) + 1e-8)

            hnr_score = 0.0
            if hnr_db > 28:   hnr_score = 1.00
            elif hnr_db > 22: hnr_score = 0.75
            elif hnr_db > 16: hnr_score = 0.45
            scores['hnr'] = hnr_score

            rms = librosa.feature.rms(y=y, frame_length=n_fft, hop_length=hop_length)[0]
            rms_cv = float(np.std(rms) / (np.mean(rms) + 1e-8))

            energy_score = 0.0
            if rms_cv < 0.20:   energy_score = 1.00
            elif rms_cv < 0.35: energy_score = 0.70
            elif rms_cv < 0.55: energy_score = 0.35
            scores['energy'] = energy_score

            bw = librosa.feature.spectral_bandwidth(y=y, sr=sr, hop_length=hop_length)[0]
            bw_cv = float(np.std(bw) / (np.mean(bw) + 1e-8))
            bw_score = 0.0
            if bw_cv < 0.08:   bw_score = 1.00
            elif bw_cv < 0.13: bw_score = 0.60
            scores['bandwidth'] = bw_score

            rolloff = librosa.feature.spectral_rolloff(y=y, sr=sr, hop_length=hop_length)[0]
            rolloff_cv = float(np.std(rolloff) / (np.mean(rolloff) + 1e-8))
            rolloff_score = 0.0
            if rolloff_cv < 0.08:   rolloff_score = 1.00
            elif rolloff_cv < 0.13: rolloff_score = 0.60
            scores['rolloff'] = rolloff_score

            zero_cross = librosa.feature.zero_crossing_rate(y, hop_length=hop_length)[0]
            zcr_cv = float(np.std(zero_cross) / (np.mean(zero_cross) + 1e-8))
            zcr_score = 1.0 if zcr_cv < 0.15 else (0.5 if zcr_cv < 0.30 else 0.0)
            scores['zcr'] = zcr_score

            weights = {
                'mfcc':           0.28,
                'pitch':          0.22,
                'hnr':            0.14,
                'mel_uniformity': 0.12,
                'energy':         0.09,
                'flatness':       0.07,
                'bandwidth':      0.04,
                'rolloff':        0.03,
                'zcr':            0.01,
            }

            confidence = sum(scores.get(k, 0.0) * w for k, w in weights.items())
            confidence = float(min(confidence * 1.55, 1.0))

            desc_parts = []
            if scores.get('mfcc', 0) > 0.5:
                desc_parts.append(f"сглаженные MFCC (var={mfcc_var:.1f})")
            if scores.get('pitch', 0) > 0.4:
                desc_parts.append("монотонный питч")
            if scores.get('hnr', 0) > 0.35:
                desc_parts.append(f"высокий HNR ({hnr_db:.0f} дБ)")
            if scores.get('energy', 0) > 0.35:
                desc_parts.append(f"ровная энергия (CV={rms_cv:.2f})")
            if scores.get('flatness', 0) > 0.35:
                desc_parts.append("плоский спектр")
            if scores.get('mel_uniformity', 0) > 0.7:
                desc_parts.append("однородные частотные полосы")

            description = ("Признаки ИИ: " + ", ".join(desc_parts) + ".") if desc_parts else "Характеристики близки к живому голосу."

            return DetectionResult(
                type=DetectorType.AI,
                title="ИИ-детектор",
                confidence=confidence,
                description=description,
                markers=markers[:10],
                additional_data={
                    "mfcc_var":       round(mfcc_var, 2),
                    "delta_mean":     round(delta_mean, 4),
                    "flatness":       round(mean_flatness, 4),
                    "pitch_cv":       round(float(np.std(valid_f0) / (np.mean(valid_f0) + 1e-8)) if len(valid_f0) > 0 else 0, 4),
                    "hnr_db":         round(float(hnr_db), 1),
                    "rms_cv":         round(rms_cv, 3),
                    "mel_uniformity": round(float(band_uniformity), 3),
                    "scores":         {k: round(v, 3) for k, v in scores.items()},
                }
            )

        except Exception as e:
            return DetectionResult(
                type=DetectorType.AI,
                title="ИИ-детектор",
                confidence=0.0,
                description=f"Ошибка анализа: {str(e)}",
                markers=[],
                additional_data={}
            )
