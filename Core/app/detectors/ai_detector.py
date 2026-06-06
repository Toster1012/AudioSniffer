import librosa
import numpy as np
from app.models import DetectionResult, TimeMarker, DetectorType
from app.utils.audio_processor import AudioProcessor

class AIDetector:
    def analyze(self, audio_path: str) -> DetectionResult:
        y, sr = AudioProcessor.load_audio(audio_path)
        return self.analyze_array(y, sr)

    def analyze_array(self, y: np.ndarray, sr: int) -> DetectionResult:
        try:
            duration = librosa.get_duration(y=y, sr=sr)
            hop_length = 1024
            scores = {}

            contrast = librosa.feature.spectral_contrast(y=y, sr=sr)
            contrast_var = float(np.mean(np.std(contrast, axis=1)))
            bandwidth = librosa.feature.spectral_bandwidth(y=y, sr=sr)[0]
            bandwidth_cv = float(np.std(bandwidth)/(np.mean(bandwidth)+1e-8))
            try:
                tempo, _ = librosa.beat.beat_track(y=y, sr=sr)
            except Exception:
                tempo = 0
            
            markers = []

            mfcc = librosa.feature.mfcc(y=y, sr=sr, n_mfcc=20, hop_length=hop_length)
            mfcc_delta = librosa.feature.delta(mfcc)
            mfcc_delta2 = librosa.feature.delta(mfcc, order=2)

            mfcc_temporal_var = float(np.mean(np.std(mfcc, axis=1)))
            mfcc_delta_mean = float(np.mean(np.abs(mfcc_delta)))
            mfcc_delta2_mean = float(np.mean(np.abs(mfcc_delta2)))

            mfcc_score = 0.0
            if mfcc_temporal_var < 10.0:
                mfcc_score = 1.0
            elif mfcc_temporal_var < 15.0:
                mfcc_score = 0.8
            elif mfcc_temporal_var < 22.0:
                mfcc_score = 0.5
            elif mfcc_temporal_var < 28.0:
                mfcc_score = 0.2

            if mfcc_delta_mean < 0.6:
                mfcc_score += 0.2
            if mfcc_delta2_mean < 0.4:
                mfcc_score += 0.15
            scores['mfcc'] = min(mfcc_score, 1.0)

            flatness = librosa.feature.spectral_flatness(y=y)[0]
            mean_flatness = float(np.mean(flatness))
            flatness_std = float(np.std(flatness))

            flatness_score = 0.0
            if mean_flatness > 0.08:
                flatness_score = 1.0
            elif mean_flatness > 0.05:
                flatness_score = 0.7
            elif mean_flatness > 0.03:
                flatness_score = 0.4

            if flatness_std < 0.04:
                flatness_score += 0.3
            scores['flatness'] = min(flatness_score, 1.0)

            try:
                f0 = librosa.yin(y, fmin=65, fmax=400, sr=sr, hop_length=hop_length)
                valid_f0 = f0[(f0 > 50) & (f0 < 800)]
            except Exception:
                valid_f0 = np.array([])

            pitch_score = 0.0
            if len(valid_f0) > 15:
                pitch_cv = float(np.std(valid_f0) / (np.mean(valid_f0) + 1e-8))
                if pitch_cv < 0.08:
                    pitch_score = 1.0
                elif pitch_cv < 0.15:
                    pitch_score = 0.8
                elif pitch_cv < 0.25:
                    pitch_score = 0.5
                elif pitch_cv < 0.35:
                    pitch_score = 0.2

                window = 20
                for i in range(0, len(valid_f0) - window, window // 2):
                    seg = valid_f0[i:i + window]
                    seg_cv = np.std(seg) / (np.mean(seg) + 1e-8)
                    if seg_cv < 0.03:
                        t_start = float(i * hop_length / sr)
                        markers.append(TimeMarker(
                            start_time=t_start,
                            end_time=min(t_start + window * hop_length / sr, duration),
                            confidence=0.8,
                            description=f"Ровный тон (CV={seg_cv:.4f})"
                        ))
            scores['pitch'] = min(pitch_score, 1.0)

            harmonic, percussive = librosa.effects.hpss(y)
            hnr = float(np.mean(harmonic ** 2) / (np.mean(percussive ** 2) + 1e-8))
            hnr_db = 10 * np.log10(hnr + 1e-8)

            hnr_score = 0.0
            if hnr_db > 25:
                hnr_score = 1.0
            elif hnr_db > 20:
                hnr_score = 0.7
            elif hnr_db > 15:
                hnr_score = 0.4
            scores['hnr'] = hnr_score

            rms = librosa.feature.rms(y=y, frame_length=2048, hop_length=hop_length)[0]
            rms_cv = float(np.std(rms) / (np.mean(rms) + 1e-8))

            energy_score = 0.0
            if rms_cv < 0.25:
                energy_score = 1.0
            elif rms_cv < 0.40:
                energy_score = 0.7
            elif rms_cv < 0.60:
                energy_score = 0.4
            scores['energy'] = energy_score

            bandwidth = librosa.feature.spectral_bandwidth(y=y, sr=sr, hop_length=hop_length)[0]
            bw_cv = float(np.std(bandwidth) / (np.mean(bandwidth) + 1e-8))

            bw_score = 0.0
            if bw_cv < 0.10:
                bw_score = 1.0
            elif bw_cv < 0.15:
                bw_score = 0.6
            scores['bandwidth'] = bw_score

            rolloff = librosa.feature.spectral_rolloff(y=y, sr=sr, hop_length=hop_length)[0]
            rolloff_cv = float(np.std(rolloff) / (np.mean(rolloff) + 1e-8))

            rolloff_score = 0.0
            if rolloff_cv < 0.10:
                rolloff_score = 1.0
            elif rolloff_cv < 0.15:
                rolloff_score = 0.6
            scores['rolloff'] = rolloff_score

            weights = {
                'mfcc':      0.30,
                'pitch':     0.25,
                'hnr':       0.15,
                'energy':    0.10,
                'flatness':  0.10,
                'bandwidth': 0.05,
                'rolloff':   0.05,
            }

            confidence = sum(scores.get(k, 0.0) * w for k, w in weights.items())
            confidence = float(min(confidence * 1.6, 1.0))

            desc_parts = []
            if scores.get('mfcc', 0) > 0.5:
                desc_parts.append(f"гладкие MFCC (var={mfcc_temporal_var:.1f})")
            if scores.get('pitch', 0) > 0.4:
                desc_parts.append("монотонный питч")
            if scores.get('hnr', 0) > 0.3:
                desc_parts.append(f"высокий HNR ({hnr_db:.0f} дБ)")
            if scores.get('energy', 0) > 0.3:
                desc_parts.append(f"ровная энергия (CV={rms_cv:.2f})")
            if scores.get('flatness', 0) > 0.3:
                desc_parts.append("плоский спектр")

            description = ("Признаки ИИ: " + ", ".join(desc_parts) + ".") if desc_parts else "Характеристики близки к живому голосу."

            return DetectionResult(
                type=DetectorType.AI,
                title="ИИ-детектор",
                confidence=confidence,
                description=description,
                markers=markers[:8],
                additional_data={
                    "mfcc_temporal_variance": round(mfcc_temporal_var, 2),
                    "mfcc_delta_mean":        round(mfcc_delta_mean, 4),
                    "spectral_flatness":      round(mean_flatness, 4),
                    "pitch_cv":               round(float(np.std(valid_f0) / (np.mean(valid_f0) + 1e-8)) if len(valid_f0) > 0 else 0, 4),
                    "hnr_db":                 round(hnr_db, 1),
                    "rms_cv":                 round(rms_cv, 3),
                    "scores":                 {k: round(v, 3) for k, v in scores.items()}
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