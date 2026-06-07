import os
import shutil
import subprocess
import tempfile
import numpy as np
import librosa
import soundfile as sf
from pathlib import Path
from app.models import AudioMetadata, SpectrogramData

_FFMPEG_WINDOWS_PATHS = [
    r"C:\ffmpeg\bin\ffmpeg.exe",
    r"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
    r"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
    r"C:\tools\ffmpeg\bin\ffmpeg.exe",
    r"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
]

_FORMATS_NEEDING_CONVERSION = {'.aac', '.wma', '.opus'}
_TARGET_SR = 22050
_LOAD_DURATION = 60


def _find_ffmpeg() -> str | None:
    found = shutil.which('ffmpeg')
    if found:
        return found
    for p in _FFMPEG_WINDOWS_PATHS:
        if os.path.isfile(p):
            return p
    return None


class AudioProcessor:
    SUPPORTED_FORMATS = {'.wav', '.mp3', '.ogg', '.flac', '.m4a', '.aac'}
    MAX_FILE_SIZE_MB = 50

    @staticmethod
    def validate_audio_file(file_path: str) -> tuple[bool, str]:
        path = Path(file_path)
        if not path.exists():
            return False, "Файл не найден"
        if path.suffix.lower() not in AudioProcessor.SUPPORTED_FORMATS:
            return False, f"Неподдерживаемый формат: {', '.join(sorted(AudioProcessor.SUPPORTED_FORMATS))}"
        size_mb = path.stat().st_size / (1024 * 1024)
        if size_mb > AudioProcessor.MAX_FILE_SIZE_MB:
            return False, f"Файл слишком большой ({size_mb:.1f} MB). Макс. {AudioProcessor.MAX_FILE_SIZE_MB} MB"
        return True, "OK"

    @staticmethod
    def load_audio(file_path: str) -> tuple[np.ndarray, int]:
        path = Path(file_path)
        fmt = path.suffix.lower()

        if fmt not in _FORMATS_NEEDING_CONVERSION:
            try:
                y, sr = librosa.load(file_path, sr=_TARGET_SR, mono=True, duration=_LOAD_DURATION, res_type='kaiser_fast')
                return y, sr
            except Exception:
                y, sr = librosa.load(file_path, sr=_TARGET_SR, mono=True, res_type='kaiser_fast')
                return y, sr

        ffmpeg_path = _find_ffmpeg()
        if ffmpeg_path is None:
            y, sr = librosa.load(file_path, sr=_TARGET_SR, mono=True, duration=_LOAD_DURATION, res_type='kaiser_fast')
            return y, sr

        wav_tmp = tempfile.NamedTemporaryFile(delete=False, suffix='.wav')
        wav_tmp.close()
        try:
            result = subprocess.run(
                [ffmpeg_path, '-y', '-i', file_path, '-ar', str(_TARGET_SR), '-ac', '1', wav_tmp.name],
                capture_output=True,
                timeout=60
            )
            if result.returncode != 0:
                raise RuntimeError(f"ffmpeg не смог сконвертировать {fmt}")
            y, sr = librosa.load(wav_tmp.name, sr=_TARGET_SR, mono=True, res_type='kaiser_fast')
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
    def extract_spectrogram(file_path: str, n_mels: int = 80, max_frames: int = 256) -> SpectrogramData:
        try:
            y, sr = AudioProcessor.load_audio(file_path)
            hop_length = 512
            n_fft = 2048

            mel_spec = librosa.feature.melspectrogram(
                y=y, sr=sr, n_mels=n_mels, n_fft=n_fft,
                hop_length=hop_length, fmax=sr // 2, power=2.0
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
            region_start = 0.0
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
                        "intensity": float(np.mean(spectral_flux[max(0, i - 5):i + 1]))
                    })

            return SpectrogramData(
                frequencies=[round(float(f), 2) for f in mel_freqs],
                times=times,
                magnitudes=norm_db,
                suspicious_regions=suspicious_regions
            )
        except Exception:
            return SpectrogramData()

    @staticmethod
    def extract_waveform(file_path: str, n_samples: int = 512) -> list:
        try:
            y, sr = AudioProcessor.load_audio(file_path)
            if len(y) > n_samples:
                chunk_size = len(y) // n_samples
                chunks = y[:chunk_size * n_samples].reshape(n_samples, chunk_size)
                rms_vals = np.sqrt(np.mean(chunks ** 2, axis=1))
                signs = np.sign(np.mean(chunks, axis=1))
                signs[signs == 0] = 1.0
                waveform = (rms_vals * signs).tolist()
            else:
                waveform = y.tolist()

            max_val = max(abs(v) for v in waveform) if waveform else 1.0
            if max_val > 0:
                waveform = [v / max_val for v in waveform]
            return waveform
        except Exception:
            return []

    @staticmethod
    def convert_to_wav(input_path: str, output_path: str) -> bool:
        try:
            y, sr = AudioProcessor.load_audio(input_path)
            sf.write(output_path, y, sr)
            return True
        except Exception:
            return False
