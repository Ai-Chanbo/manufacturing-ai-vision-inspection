import time
import numpy as np
import logging
from pathlib import Path
from app.config import MODEL_PATH, NG_THRESHOLD

logger = logging.getLogger(__name__)

_session = None
_use_dummy = False

DEFECT_LABELS = ["none", "scratch", "stain", "crack", "shape", "label", "unknown"]
DEFECT_MESSAGES = {
    "none":    "異常は検出されませんでした",
    "scratch": "キズの可能性があります",
    "stain":   "汚れの可能性があります",
    "crack":   "欠けの可能性があります",
    "shape":   "形状異常の可能性があります",
    "label":   "ラベル・印字ズレの可能性があります",
    "unknown": "異常が検出されました",
}


def _load_session():
    global _session, _use_dummy
    if not Path(MODEL_PATH).exists():
        logger.warning("ONNXモデルが見つかりません。ダミー判定を使用します: %s", MODEL_PATH)
        _use_dummy = True
        return
    try:
        import onnxruntime as ort
        providers = []
        try:
            import onnxruntime as _ort
            available = _ort.get_available_providers()
            for p in ["CUDAExecutionProvider", "DmlExecutionProvider", "CPUExecutionProvider"]:
                if p in available:
                    providers.append(p)
        except Exception:
            providers = ["CPUExecutionProvider"]

        _session = ort.InferenceSession(str(MODEL_PATH), providers=providers)
        logger.info("ONNXモデルを読み込みました (providers=%s)", providers)
    except Exception as e:
        logger.warning("ONNXモデルの読み込みに失敗しました。ダミー判定を使用します: %s", e)
        _use_dummy = True


def _dummy_infer(tensor: np.ndarray) -> tuple[float, str]:
    """ルールベースのダミー判定。
    - std > 0.08 (テクスチャ異常) → NG
    - mean < 0.65 かつ std 大 → stain 疑い
    - mean >= 0.65 かつ std 大  → scratch 疑い
    - それ以外 → OK
    """
    mean_val = float(tensor.mean())
    std_val = float(tensor.std())
    noise_score = min(std_val / 0.08, 1.0)   # std=0.08で1.0に到達
    dark_score  = max(0.65 - mean_val, 0.0)  # 低輝度ほど大
    score = min(noise_score * 0.7 + dark_score * 0.3, 1.0)

    if score >= NG_THRESHOLD:
        defect_type = "scratch" if mean_val >= 0.65 else "stain"
    else:
        defect_type = "none"
    return round(score, 4), defect_type


def _onnx_infer(tensor: np.ndarray) -> tuple[float, str]:
    input_name = _session.get_inputs()[0].name
    outputs = _session.run(None, {input_name: tensor})
    logits = outputs[0][0]                     # shape: (num_classes,)
    probs = _softmax(logits)
    defect_idx = int(np.argmax(probs))
    score = float(probs[defect_idx])
    defect_type = DEFECT_LABELS[defect_idx] if defect_idx < len(DEFECT_LABELS) else "unknown"
    return round(score, 4), defect_type


def _softmax(x: np.ndarray) -> np.ndarray:
    e = np.exp(x - x.max())
    return e / e.sum()


def run_inference(tensor: np.ndarray) -> dict:
    global _session, _use_dummy
    if _session is None and not _use_dummy:
        _load_session()

    t0 = time.perf_counter()
    if _use_dummy:
        score, defect_type = _dummy_infer(tensor)
    else:
        try:
            score, defect_type = _onnx_infer(tensor)
        except Exception as e:
            logger.error("ONNX推論エラー。ダミー判定にフォールバックします: %s", e)
            score, defect_type = _dummy_infer(tensor)
    inference_ms = round((time.perf_counter() - t0) * 1000, 2)

    result = "NG" if (score >= NG_THRESHOLD and defect_type != "none") else "OK"
    if result == "OK":
        defect_type = "none"
        score = 1.0 - score if score < NG_THRESHOLD else score

    return {
        "result": result,
        "score": score,
        "defect_type": defect_type,
        "message": DEFECT_MESSAGES.get(defect_type, "異常が検出されました"),
        "inference_ms": inference_ms,
    }


def is_real_model_loaded() -> bool:
    """実 ONNX モデルがロード済みか（ダミー判定でないか）を返す。

    データセット評価では、モデル未配置時のダミー判定（ルールベース）で
    評価指標を算出してしまうことを防ぐため、評価開始前にこの関数で確認する。
    """
    global _session, _use_dummy
    if _session is None and not _use_dummy:
        _load_session()
    return _session is not None and not _use_dummy


def get_model_status() -> dict:
    """モデルの読み込み状態を返す（UI / 評価レポート / ログ用）。"""
    global _session, _use_dummy
    if _session is None and not _use_dummy:
        _load_session()
    return {
        "model_path": str(MODEL_PATH),
        "model_exists": Path(MODEL_PATH).exists(),
        "loaded": _session is not None,
        "using_dummy": _use_dummy,
    }


# モジュール読み込み時に初期化
_load_session()
