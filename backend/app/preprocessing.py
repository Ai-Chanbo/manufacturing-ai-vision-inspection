import cv2
import numpy as np
from pathlib import Path
from app.config import INPUT_SIZE, PREPROCESSED_DIR


def load_image(image_bytes: bytes) -> np.ndarray:
    arr = np.frombuffer(image_bytes, np.uint8)
    img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
    if img is None:
        raise ValueError("画像のデコードに失敗しました。ファイルが破損しているか非対応形式です。")
    return img


def preprocess(img: np.ndarray) -> np.ndarray:
    resized = cv2.resize(img, INPUT_SIZE)
    denoised = cv2.fastNlMeansDenoisingColored(resized, h=10, hColor=10, templateWindowSize=7, searchWindowSize=21)
    return denoised


def to_tensor(img: np.ndarray) -> np.ndarray:
    """HWC BGR → CHW RGB float32 normalized to [0,1]."""
    rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
    tensor = rgb.astype(np.float32) / 255.0
    tensor = np.transpose(tensor, (2, 0, 1))          # CHW
    tensor = np.expand_dims(tensor, axis=0)            # NCHW
    return tensor


def save_preprocessed(img: np.ndarray, filename: str) -> None:
    out_dir = Path(PREPROCESSED_DIR)
    out_dir.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(out_dir / filename), img)
