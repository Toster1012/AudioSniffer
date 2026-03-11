import librosa
import soundfile as sf
import subprocess
import shutil
import tempfile
import os
from pathlib import Path
from app.models import AudioMetadata


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
    MAX_FILE_SIZE_MB = 50

    @staticmethod
    def validate_audio_file(file_path: str) -> tuple[bool, str]:

        path = Path(file_path)

        if not path.exists():
            print(f"Validation failed: File not found - {file_path}")
            return False, "Файл не найден"

        if path.suffix.lower() not in AudioProcessor.SUPPORTED_FORMATS:
            print(f"Validation failed: Unsupported format - {path.suffix}. Supported: {AudioProcessor.SUPPORTED_FORMATS}")
            return False, f"Неподдерживаемый формат. Разрешены: {', '.join(AudioProcessor.SUPPORTED_FORMATS)}"

        file_size_mb = path.stat().st_size / (1024 * 1024)
        if file_size_mb > AudioProcessor.MAX_FILE_SIZE_MB:
            print(f"Validation failed: File too large - {file_size_mb:.1f} MB. Max: {AudioProcessor.MAX_FILE_SIZE_MB} MB")
            return False, f"Файл слишком большой ({file_size_mb:.1f} MB). Максимум {AudioProcessor.MAX_FILE_SIZE_MB} MB"

        print(f"Validation passed for file: {file_path}")
        return True, "OK"

    @staticmethod
    def load_audio(file_path: str) -> tuple:
        path = Path(file_path)
        fmt = path.suffix.lower()

        if fmt not in FORMATS_NEEDING_CONVERSION:
            y, sr = librosa.load(file_path, sr=None)
            return y, sr

        ffmpeg_path = find_ffmpeg()

        if ffmpeg_path is None:
            print(f"ffmpeg not found in PATH or common locations, trying librosa direct for {fmt}")
            try:
                y, sr = librosa.load(file_path, sr=None)
                return y, sr
            except Exception as e:
                raise RuntimeError(
                    f"Не удалось загрузить {fmt}: установите ffmpeg (https://ffmpeg.org/download.html) "
                    f"и добавьте в PATH"
                ) from e

        print(f"Using ffmpeg at: {ffmpeg_path}")
        wav_tmp = tempfile.NamedTemporaryFile(delete=False, suffix='.wav')
        wav_tmp.close()

        try:
            result = subprocess.run(
                [ffmpeg_path, '-y', '-i', file_path, wav_tmp.name],
                capture_output=True,
                timeout=60
            )
            if result.returncode != 0:
                stderr = result.stderr.decode(errors='replace')
                print(f"ffmpeg stderr: {stderr}")
                raise RuntimeError(f"ffmpeg не смог сконвертировать {fmt} в WAV")

            y, sr = librosa.load(wav_tmp.name, sr=None)
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
            duration_seconds=duration,
            sample_rate=sr,
            channels=channels,
            format=fmt
        )

    @staticmethod
    def convert_to_wav(input_path: str, output_path: str) -> bool:

        try:
            y, sr = AudioProcessor.load_audio(input_path)
            sf.write(output_path, y, sr)
            return True
        except Exception:
            return False