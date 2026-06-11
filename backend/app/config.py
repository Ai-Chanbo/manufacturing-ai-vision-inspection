import os
from pathlib import Path

BASE_DIR = Path(__file__).parent.parent

# 環境変数で上書き可能。本番環境では .env ファイルまたは OS 環境変数で設定する。
MODEL_PATH = Path(os.environ.get("MODEL_PATH", str(BASE_DIR / "models" / "sample_model.onnx")))
INPUT_SIZE = (224, 224)
NG_THRESHOLD = float(os.environ.get("NG_THRESHOLD", "0.5"))
API_HOST = os.environ.get("API_HOST", "0.0.0.0")
API_PORT = int(os.environ.get("API_PORT", "8000"))
# デノイズ処理は品質向上に寄与するが推論時間が増加する（224×224で+100〜500ms）
ENABLE_DENOISING = os.environ.get("ENABLE_DENOISING", "false").lower() == "true"
