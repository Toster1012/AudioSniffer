import librosa
import soundfile as sf
import subprocess
import shutil
import tempfile
import os
import numpy as np
from pathlib import Path
from app.models import AudioMetadata, SpectrogramData


FFMPEG_WINDOWS_PATHS = [
    r"C:\ffmpeg\bin\ffmpeg.exe",
    r"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
    r"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
    r"C:\tools\ffmpeg\bin\ffmpeg.exe",
    r"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
]

FORMATS_NEEDING_CONVERSION = {'.aac', '.wma', '.opus'}


def find_ffmpeg() -> str | None:
    found = shutil.which('ffmpeg')
    if found:
        return found
    for path in FFMPEG_WINDOWS_PATHS:
        if os.path.isfile(path):
            return path
    return None


class AudioProcessor:
    SUPPORTED_FORMATS = {'.wav', '.mp3', '.ogg', '.flac', '.m4a', '.aac'}
    MAX_FILE_SIZE_MB = 25

    @staticmethod
    def validate_audio_file(file_path: str) -> tuple[bool, str]:
        path = Path(file_path)

        if not path.exists():
            return False, "Файл не найден"

        if path.suffix.lower() not in AudioProcessor.SUPPORTED_FORMATS:
            return False, f"Неподдерживаемый формат. Разрешены: {', '.join(sorted(AudioProcessor.SUPPORTED_FORMATS))}"

        file_size_mb = path.stat().st_size / (1024 * 1024)
        if file_size_mb > AudioProcessor.MAX_FILE_SIZE_MB:
            return False, f"Файл слишком большой ({file_size_mb:.1f} MB). Максимум {AudioProcessor.MAX_FILE_SIZE_MB} MB"

        return True, "OK"

    @staticmethod
    def load_audio(file_path: str) -> tuple:
        path = Path(file_path)
        fmt = path.suffix.lower()

        if fmt not in FORMATS_NEEDING_CONVERSION:
            try:
                y, sr = librosa.load(file_path, sr=22050, mono=True, duration=30)
                return y, sr
            except Exception:
                y, sr = librosa.load(file_path, sr=22050, mono=True)
                return y, sr

        ffmpeg_path = find_ffmpeg()

        if ffmpeg_path is None:
            try:
                y, sr = librosa.load(file_path, sr=22050, mono=True, duration=30)
                return y, sr
            except Exception as e:
                raise RuntimeError(
                    f"Не удалось загрузить {fmt}: ffmpeg не найден. "
                    f"Установите ffmpeg: https://ffmpeg.org/download.html"
                ) from e

        wav_tmp = tempfile.NamedTemporaryFile(delete=False, suffix='.wav')
        wav_tmp.close()

        try:
            result = subprocess.run(
                [ffmpeg_path, '-y', '-i', file_path, '-ar', '44100', '-ac', '1', wav_tmp.name],
                capture_output=True,
                timeout=60
            )
            if result.returncode != 0:
                raise RuntimeError(f"ffmpeg не смог сконвертировать {fmt} в WAV")

            y, sr = librosa.load(wav_tmp.name, sr=None, mono=True)
            return y, sr
        finally:
            if os.path.exists(wav_tmp.name):
                os.unlink(wav_tmp.name)

    @staticmethod
    def extract_metadata(file_path: str) -> AudioMetadata:
        y, sr = AudioProcessor.load_audio(file_path)
        duration = librosa.get_duration(y=y, sr=sr)

        try:
            info = sf.info(file_path)
            channels = info.channels
        except Exception:
            channels = 1 if y.ndim == 1 else y.shape[0]

        fmt = Path(file_path).suffix[1:].lower()

        return AudioMetadata(
            duration_seconds=float(duration),
            sample_rate=int(sr),
            channels=int(channels),
            format=fmt
        )

    @staticmethod
    def extract_spectrogram(file_path: str, n_mels: int = 64, max_frames: int = 200) -> SpectrogramData:
        """
        Извлекает mel-спектрограмму и помечает подозрительные регионы
        (резкие скачки спектральной плотности)
        """
        try:
            y, sr = AudioProcessor.load_audio(file_path)
            hop_length = 512
            n_fft = 2048
            mel_spec = librosa.feature.melspectrogram(
                y=y, sr=sr, n_mels=n_mels, n_fft=n_fft,
                hop_length=hop_length, fmax=sr // 2
            )
            mel_db = librosa.power_to_db(mel_spec, ref=np.max)
            n_frames = mel_db.shape[1]
            if n_frames > max_frames:
                indices = np.linspace(0, n_frames - 1, max_frames, dtype=int)
                mel_db = mel_db[:, indices]
                n_frames = max_frames
            mel_freqs = librosa.mel_frequencies(n_mels=n_mels, fmax=sr // 2)
            total_duration = librosa.get_duration(y=y, sr=sr)
            times = np.linspace(0, total_duration, n_frames).tolist()
            min_db = float(mel_db.min())
            max_db = float(mel_db.max())
            db_range = max_db - min_db if max_db != min_db else 1.0
            norm_db = ((mel_db - min_db) / db_range).tolist()
            spectral_flux = np.sqrt(np.sum(np.diff(mel_spec, axis=1) ** 2, axis=0))
            if spectral_flux.max() > 0:
                spectral_flux /= spectral_flux.max()

            suspicious_regions = []
            threshold = 0.75
            in_region = False
            region_start = 0

            flux_times = np.linspace(0, total_duration, len(spectral_flux))
            for i, (t, flux) in enumerate(zip(flux_times, spectral_flux)):
                if flux > threshold and not in_region:
                    in_region = True
                    region_start = float(t)
                elif flux <= threshold and in_region:
                    in_region = False
                    suspicious_regions.append({
                        "start": region_start,
                        "end": float(t),
                        "intensity": float(np.mean(spectral_flux[max(0, i-5):i+1]))
                    })

            return SpectrogramData(
                frequencies=[round(float(f), 2) for f in mel_freqs],
                times=times,
                magnitudes=norm_db,
                suspicious_regions=suspicious_regions
            )

        except Exception as e:
            print(f"Spectrogram extraction failed: {e}")
            return SpectrogramData()

    @staticmethod
    def extract_waveform(file_path: str, n_samples: int = 400) -> list:
        """Извлекает правильную форму волны из аудиофайла"""
        try:
            y, sr = AudioProcessor.load_audio(file_path)
            target = n_samples
            if len(y) > target:
                chunk = len(y) // target
                waveform = []
                for i in range(target):
                    start = i * chunk
                    end = min(start + chunk, len(y))
                    chunk_data = y[start:end]
                    rms = float(np.sqrt(np.mean(chunk_data ** 2)))
                    sign = 1.0 if np.mean(chunk_data) >= 0 else -1.0
                    waveform.append(rms * sign)
            else:
                waveform = y.tolist()
            max_val = max(abs(v) for v in waveform) if waveform else 1.0
            if max_val > 0:
                waveform = [v / max_val for v in waveform]

            return waveform
        except Exception as e:
            print(f"Waveform extraction failed: {e}")
            return []

    @staticmethod
    def convert_to_wav(input_path: str, output_path: str) -> bool:
        try:
            y, sr = AudioProcessor.load_audio(input_path)
            sf.write(output_path, y, sr)
            return True
        except Exception:
            return False
