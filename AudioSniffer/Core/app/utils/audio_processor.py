import librosa
import soundfile as sf
from pathlib import Path
from app.models import AudioMetadata


class AudioProcessor:
    SUPPORTED_FORMATS = {'.wav', '.mp3', '.ogg', '.flac', '.m4a'}
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
    def extract_metadata(file_path: str) -> AudioMetadata:

        y, sr = librosa.load(file_path, sr=None)
        duration = librosa.get_duration(y=y, sr=sr)

        info = sf.info(file_path)

        return AudioMetadata(
            duration_seconds=duration,
            sample_rate=sr,
            channels=info.channels,
            format=Path(file_path).suffix[1:]
        )

    @staticmethod
    def convert_to_wav(input_path: str, output_path: str) -> bool:

        try:
            y, sr = librosa.load(input_path, sr=None)
            sf.write(output_path, y, sr)
            return True
        except Exception:
            return False