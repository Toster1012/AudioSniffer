
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
            hop_length = 512
            scores = {}
            markers = []

            # === 1. MFCC вариативность ===
            # Живой голос: std(MFCC по времени) обычно 12-30
            # TTS/синтетика: < 8, очень "гладкий"
            mfcc = librosa.feature.mfcc(y=y, sr=sr, n_mfcc=20, hop_length=hop_length)
            mfcc_delta = librosa.feature.delta(mfcc)
            mfcc_delta2 = librosa.feature.delta(mfcc, order=2)

            mfcc_temporal_var = float(np.mean(np.std(mfcc, axis=1)))
            mfcc_delta_mean   = float(np.mean(np.abs(mfcc_delta)))
            mfcc_delta2_mean  = float(np.mean(np.abs(mfcc_delta2)))

            mfcc_score = 0.0
            if mfcc_temporal_var < 6.0:
                mfcc_score = 1.0
            elif mfcc_temporal_var < 9.0:
                mfcc_score = 0.7
            elif mfcc_temporal_var < 13.0:
                mfcc_score = 0.35
            elif mfcc_temporal_var < 18.0:
                mfcc_score = 0.1
            # Живой голос с var > 18 → score ~ 0

            if mfcc_delta_mean < 0.4:
                mfcc_score += 0.15
            if mfcc_delta2_mean < 0.25:
                mfcc_score += 0.10
            scores['mfcc'] = min(mfcc_score, 1.0)

            # === 2. Spectral flatness ===
            # Живой голос: flatness 0.01-0.05 (гармоничный, неровный)
            # TTS: 0.06-0.15 (более "белый шум")
            flatness = librosa.feature.spectral_flatness(y=y)[0]
            mean_flatness = float(np.mean(flatness))
            flatness_std  = float(np.std(flatness))

            flatness_score = 0.0
            if mean_flatness > 0.12:
                flatness_score = 0.9
            elif mean_flatness > 0.08:
                flatness_score = 0.6
            elif mean_flatness > 0.055:
                flatness_score = 0.3
            # < 0.055 → живой голос, score = 0

            if flatness_std < 0.025:
                flatness_score += 0.2  # слишком стабильный → синтетика
            scores['flatness'] = min(flatness_score, 1.0)

            # === 3. Pitch regularity — быстрый YIN вместо PYIN ===
            # yin примерно в 8x быстрее pyin и достаточно точен
            try:
                f0 = librosa.yin(y, fmin=librosa.note_to_hz('C2'),
                                 fmax=librosa.note_to_hz('C7'),
                                 sr=sr, hop_length=hop_length)
                valid_f0 = f0[(f0 > 50) & (f0 < 800)]  # отфильтровываем артефакты
            except Exception:
                valid_f0 = np.array([])

            pitch_score = 0.0
            if len(valid_f0) > 30:
                pitch_cv = float(np.std(valid_f0) / (np.mean(valid_f0) + 1e-8))
                # Живой голос: CV обычно 0.15-0.40
                # TTS: CV < 0.07 (ровный, монотонный)
                if pitch_cv < 0.04:
                    pitch_score = 0.95
                elif pitch_cv < 0.07:
                    pitch_score = 0.70
                elif pitch_cv < 0.10:
                    pitch_score = 0.40
                elif pitch_cv < 0.15:
                    pitch_score = 0.15
                # > 0.15 → живой, score = 0

                # Маркеры подозрительно ровных сегментов
                window = 40
                for i in range(0, len(valid_f0) - window, window // 2):
                    seg = valid_f0[i:i + window]
                    seg_cv = np.std(seg) / (np.mean(seg) + 1e-8)
                    if seg_cv < 0.025:
                        t_start = float(i * hop_length / sr)
                        markers.append(TimeMarker(
                            start_time=t_start,
                            end_time=min(t_start + window * hop_length / sr, duration),
                            confidence=0.75,
                            description=f"Подозрительно ровный тон (CV={seg_cv:.4f})"
                        ))
            scores['pitch'] = min(pitch_score, 1.0)

            # === 4. HNR (Harmonic-to-Noise Ratio) ===
            harmonic, percussive = librosa.effects.hpss(y)
            hnr = float(np.mean(harmonic ** 2) / (np.mean(percussive ** 2) + 1e-8))
            hnr_db = 10 * np.log10(hnr + 1e-8)

            # Живой голос: HNR 10-20 дБ (есть шум дыхания, комнаты)
            # TTS: > 25 дБ (почти без шума)
            hnr_score = 0.0
            if hnr_db > 30:
                hnr_score = 0.85
            elif hnr_db > 25:
                hnr_score = 0.55
            elif hnr_db > 20:
                hnr_score = 0.25
            elif hnr_db > 15:
                hnr_score = 0.05
            scores['hnr'] = hnr_score

            # === 5. Energy envelope ===
            rms = librosa.feature.rms(y=y, frame_length=2048, hop_length=hop_length)[0]
            rms_cv = float(np.std(rms) / (np.mean(rms) + 1e-8))

            # Живой голос: CV > 0.5 (динамичный)
            # TTS: CV < 0.25 (очень ровный)
            energy_score = 0.0
            if rms_cv < 0.15:
                energy_score = 0.80
            elif rms_cv < 0.25:
                energy_score = 0.50
            elif rms_cv < 0.40:
                energy_score = 0.20
            elif rms_cv < 0.55:
                energy_score = 0.05
            scores['energy'] = energy_score

            # === 6. Spectral bandwidth CV ===
            bandwidth = librosa.feature.spectral_bandwidth(y=y, sr=sr, hop_length=hop_length)[0]
            bw_cv = float(np.std(bandwidth) / (np.mean(bandwidth) + 1e-8))

            bw_score = 0.0
            if bw_cv < 0.06:
                bw_score = 0.75
            elif bw_cv < 0.10:
                bw_score = 0.40
            elif bw_cv < 0.15:
                bw_score = 0.15
            scores['bandwidth'] = bw_score

            # === 7. Spectral rolloff CV (новый признак) ===
            rolloff = librosa.feature.spectral_rolloff(y=y, sr=sr, hop_length=hop_length)[0]
            rolloff_cv = float(np.std(rolloff) / (np.mean(rolloff) + 1e-8))

            rolloff_score = 0.0
            if rolloff_cv < 0.05:
                rolloff_score = 0.70
            elif rolloff_cv < 0.09:
                rolloff_score = 0.35
            elif rolloff_cv < 0.14:
                rolloff_score = 0.10
            scores['rolloff'] = rolloff_score

            # === Взвешенная итоговая уверенность ===
            weights = {
                'mfcc':      0.32,
                'pitch':     0.22,
                'hnr':       0.16,
                'energy':    0.12,
                'flatness':  0.08,
                'bandwidth': 0.06,
                'rolloff':   0.04,
            }

            confidence = sum(scores.get(k, 0.0) * w for k, w in weights.items())
            confidence = float(min(confidence, 1.0))

            # Описание
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

            description = ("Признаки ИИ: " + ", ".join(desc_parts) + ".") if desc_parts \
                else "Характеристики близки к живому голосу."

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
            import traceback
            print(f"AIDetector error: {e}\n{traceback.format_exc()}")
            return DetectionResult(
                type=DetectorType.AI,
                title="ИИ-детектор",
                confidence=0.0,
                description=f"Ошибка анализа: {str(e)}",
                markers=[],
                additional_data={}
            )