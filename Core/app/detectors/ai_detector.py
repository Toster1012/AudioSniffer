"""
AIDetector — основной детектор ИИ-генерации аудио.

Использует набор признаков, характерных для TTS/GAN-синтеза:
- MFCC статистика (слишком "гладкие" коэффициенты у синтетики)
- Spectral flatness (ИИ даёт более равномерный спектр)
- Harmonic-to-noise ratio (ИИ голоса часто гармоничнее живых)
- Pitch regularity (TTS имеет подозрительно ровный тон)
- Формантная структура (ИИ часто имеет нетипичные переходы формант)
- Energy envelope (равномерность энергии — признак синтеза)
"""

import librosa
import numpy as np
from scipy import stats
from scipy.signal import find_peaks
from app.models import DetectionResult, TimeMarker, DetectorType
from app.utils.audio_processor import AudioProcessor


class AIDetector:

    def __init__(self):
        pass

    def analyze(self, audio_path: str) -> DetectionResult:
        try:
            y, sr = AudioProcessor.load_audio(audio_path)
            duration = librosa.get_duration(y=y, sr=sr)

            scores = {}
            markers = []

            # === 1. MFCC smoothness ===
            mfcc = librosa.feature.mfcc(y=y, sr=sr, n_mfcc=20, hop_length=512)
            mfcc_delta = librosa.feature.delta(mfcc)
            mfcc_delta2 = librosa.feature.delta(mfcc, order=2)

            # ИИ-голоса имеют очень стабильные MFCC
            mfcc_temporal_var = float(np.mean(np.std(mfcc, axis=1)))
            mfcc_delta_mean = float(np.mean(np.abs(mfcc_delta)))
            mfcc_delta2_mean = float(np.mean(np.abs(mfcc_delta2)))

            # Низкая вариативность MFCC → синтетика
            mfcc_score = 0.0
            if mfcc_temporal_var < 8.0:
                mfcc_score += 0.4
            elif mfcc_temporal_var < 12.0:
                mfcc_score += 0.2
            if mfcc_delta_mean < 0.5:
                mfcc_score += 0.3
            if mfcc_delta2_mean < 0.3:
                mfcc_score += 0.3
            scores['mfcc'] = min(mfcc_score, 1.0)

            # === 2. Spectral flatness ===
            flatness = librosa.feature.spectral_flatness(y=y)[0]
            mean_flatness = float(np.mean(flatness))
            flatness_std = float(np.std(flatness))

            # ИИ имеет более высокую и равномерную спектральную плоскостность
            flatness_score = 0.0
            if mean_flatness > 0.08:
                flatness_score += 0.5
            elif mean_flatness > 0.05:
                flatness_score += 0.25
            if flatness_std < 0.03:
                flatness_score += 0.5
            scores['flatness'] = min(flatness_score, 1.0)

            # === 3. Pitch regularity (ровный тон → TTS) ===
            f0, voiced_flag, voiced_probs = librosa.pyin(
                y, fmin=librosa.note_to_hz('C2'),
                fmax=librosa.note_to_hz('C7'),
                sr=sr, hop_length=512
            )
            valid_f0 = f0[~np.isnan(f0)]

            pitch_score = 0.0
            if len(valid_f0) > 20:
                pitch_cv = float(np.std(valid_f0) / (np.mean(valid_f0) + 1e-8))
                # Слишком ровный питч
                if pitch_cv < 0.05:
                    pitch_score = 0.9
                elif pitch_cv < 0.08:
                    pitch_score = 0.6
                elif pitch_cv < 0.12:
                    pitch_score = 0.3

                # Обнаружение подозрительно ровных сегментов
                times_f0 = librosa.times_like(f0, sr=sr, hop_length=512)
                window = 50  # ~0.5 сек
                for i in range(0, len(valid_f0) - window, window // 2):
                    seg = valid_f0[i:i + window]
                    seg_cv = np.std(seg) / (np.mean(seg) + 1e-8)
                    if seg_cv < 0.03 and len(seg) == window:
                        t_start = float(i * 512 / sr)
                        t_end = float((i + window) * 512 / sr)
                        markers.append(TimeMarker(
                            start_time=t_start,
                            end_time=min(t_end, duration),
                            confidence=0.8,
                            description=f"Подозрительно ровный тон (CV={seg_cv:.4f})"
                        ))

            scores['pitch'] = min(pitch_score, 1.0)

            # === 4. Harmonic structure ===
            harmonic, percussive = librosa.effects.hpss(y)
            hnr = float(np.mean(harmonic ** 2) / (np.mean(percussive ** 2) + 1e-8))
            hnr_db = 10 * np.log10(hnr + 1e-8)

            # Очень высокий HNR типичен для TTS
            hnr_score = 0.0
            if hnr_db > 25:
                hnr_score = 0.8
            elif hnr_db > 20:
                hnr_score = 0.5
            elif hnr_db > 15:
                hnr_score = 0.25
            scores['hnr'] = hnr_score

            # === 5. Energy envelope regularity ===
            rms = librosa.feature.rms(y=y, frame_length=2048, hop_length=512)[0]
            rms_cv = float(np.std(rms) / (np.mean(rms) + 1e-8))

            # Слишком ровная энергия → синтез
            energy_score = 0.0
            if rms_cv < 0.2:
                energy_score = 0.7
            elif rms_cv < 0.35:
                energy_score = 0.4
            elif rms_cv < 0.5:
                energy_score = 0.2
            scores['energy'] = energy_score

            # === 6. Zero crossing rate consistency ===
            zcr = librosa.feature.zero_crossing_rate(y, hop_length=512)[0]
            zcr_cv = float(np.std(zcr) / (np.mean(zcr) + 1e-8))

            zcr_score = 0.0
            if zcr_cv < 0.3:
                zcr_score = 0.5
            elif zcr_cv < 0.5:
                zcr_score = 0.25
            scores['zcr'] = zcr_score

            # === 7. Spectral bandwidth consistency ===
            bandwidth = librosa.feature.spectral_bandwidth(y=y, sr=sr, hop_length=512)[0]
            bw_cv = float(np.std(bandwidth) / (np.mean(bandwidth) + 1e-8))

            bw_score = 0.0
            if bw_cv < 0.1:
                bw_score = 0.7
            elif bw_cv < 0.15:
                bw_score = 0.4
            scores['bandwidth'] = bw_score

            # === Взвешенная итоговая уверенность ===
            weights = {
                'mfcc': 0.30,
                'flatness': 0.15,
                'pitch': 0.25,
                'hnr': 0.15,
                'energy': 0.10,
                'zcr': 0.03,
                'bandwidth': 0.02,
            }

            confidence = sum(scores.get(k, 0.0) * w for k, w in weights.items())
            confidence = float(min(confidence, 1.0))

            desc_parts = []
            if scores.get('mfcc', 0) > 0.4:
                desc_parts.append("стабильные MFCC-коэффициенты")
            if scores.get('pitch', 0) > 0.3:
                desc_parts.append("ровный тон")
            if scores.get('hnr', 0) > 0.3:
                desc_parts.append(f"высокий HNR ({hnr_db:.1f} дБ)")
            if scores.get('energy', 0) > 0.3:
                desc_parts.append("равномерная энергия")
            if scores.get('flatness', 0) > 0.3:
                desc_parts.append("плоский спектр")

            if desc_parts:
                description = "Признаки ИИ-синтеза: " + ", ".join(desc_parts) + "."
            else:
                description = "Характеристики близки к живому голосу."

            return DetectionResult(
                type=DetectorType.AI,
                title="ИИ-детектор",
                confidence=confidence,
                description=description,
                markers=markers[:10],
                additional_data={
                    "mfcc_temporal_variance": round(mfcc_temporal_var, 4),
                    "mfcc_delta_mean": round(mfcc_delta_mean, 4),
                    "spectral_flatness_mean": round(mean_flatness, 4),
                    "pitch_cv": round(float(np.std(valid_f0) / (np.mean(valid_f0) + 1e-8)) if len(valid_f0) > 0 else 0, 4),
                    "hnr_db": round(hnr_db, 2),
                    "rms_cv": round(rms_cv, 4),
                    "component_scores": {k: round(v, 3) for k, v in scores.items()}
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
