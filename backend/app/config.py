from pathlib import Path

BASE_DIR = Path(__file__).parent.parent

MODEL_PATH = BASE_DIR / "models" / "sample_model.onnx"
PREPROCESSED_DIR = BASE_DIR / "preprocessed"
INPUT_SIZE = (224, 224)
NG_THRESHOLD = 0.5
API_HOST = "0.0.0.0"
API_PORT = 8000
