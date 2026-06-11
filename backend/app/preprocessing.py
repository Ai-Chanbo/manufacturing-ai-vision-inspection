import cv2
import numpy as np
from app.config import INPUT_SIZE, ENABLE_DENOISING


def load_image(image_bytes: bytes) -> np.ndarray:
    arr = np.frombuffer(image_bytes, np.uint8)
    img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
    if img is None:
        raise ValueError("画像のデコードに失敗しました。ファイルが破損しているか非対応形式です。")
    return img


def preprocess(img: np.ndarray) -> np.ndarray:
    resized = cv2.resize(img, INPUT_SIZE)
    if ENABLE_DENOISING:
        # fastNlMeansDenoisingColored は推論時間を大幅に増加させる（+100〜500ms）。
        # 本番運用では照明・カメラ側でノイズ対策を行い、通常は無効のままにする。
        resized = cv2.fastNlMeansDenoisingColored(
            resized, h=10, hColor=10, templateWindowSize=7, searchWindowSize=21
        )
    return resized


def to_tensor(img: np.ndarray) -> np.ndarray:
    """HWC BGR → NCHW RGB float32、[0, 1] 正規化"""
    rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
    tensor = rgb.astype(np.float32) / 255.0
    tensor = np.transpose(tensor, (2, 0, 1))   # HWC → CHW
    tensor = np.expand_dims(tensor, axis=0)     # CHW → NCHW
    return tensor
