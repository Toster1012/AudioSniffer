"""
Запуск FastAPI сервера для AudioSniffer
Положите этот файл в папку Core/ (рядом с папкой app)
"""
import uvicorn
import sys
import os
from pathlib import Path

core_dir = Path(__file__).parent
sys.path.insert(0, str(core_dir))

os.environ["PYTHONPATH"] = str(core_dir)

if __name__ == "__main__":
    uvicorn.run(
        "app.main:app",
        host="127.0.0.1",
        port=5000,
        reload=True,
        log_level="info"
    )
