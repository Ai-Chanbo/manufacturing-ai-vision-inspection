"""データセット評価コア。

評価対象画像群を既存の Python 推論パイプライン（/inspect と同一ロジック）で一括推論し、
評価指標を算出して詳細 CSV と evaluation_summary.json を出力する。

評価エンジン基準について:
  本評価は FastAPI / Python 側（onnxruntime + app.inference / app.preprocessing）を基準とする。
  Python 前処理は /255 正規化のみ（preprocessing.to_tensor）であり、
  C# HMI の ONNX ImageNet モードが行う ImageNet mean/std 正規化とは前処理が異なる。
  そのため、同一モデルでも C# HMI 側の推論結果と差異が生じうる点に注意する
  （summary["notes"] にも明記し、README にも記載する）。
"""

from __future__ import annotations

import csv
import json
import logging
import time
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from app import inference
from app.metrics import classification_report
from app.preprocessing import load_image, preprocess, to_tensor
from app.dataset_loader import EvalSample

logger = logging.getLogger(__name__)

ENGINE_NAME = "FastAPI / Python (onnxruntime)"

PREPROCESSING_NOTE = (
    "本評価は FastAPI/Python を基準エンジンとする。Python 前処理は /255 正規化のみ"
    "（preprocessing.to_tensor）であり、C# HMI の ONNX ImageNet モードが行う "
    "ImageNet mean/std 正規化とは前処理が異なるため、同一モデルでも HMI 側の推論結果と"
    "差異が生じうる。"
)

DETAIL_CSV_NAME = "evaluation_results.csv"
SUMMARY_JSON_NAME = "evaluation_summary.json"


class ModelNotLoadedError(RuntimeError):
    """実 ONNX モデルが未ロード（ダミー判定状態）のまま評価しようとした場合の例外。"""


@dataclass
class _Prediction:
    sample: EvalSample
    result: str          # "OK" / "NG"
    score: float
    defect_type: str
    message: str
    inference_ms: float


def _ensure_model_loaded() -> dict:
    """評価開始前にモデルロード状態を確認する。

    実モデルが未ロード（ダミー判定にフォールバックする状態）なら ModelNotLoadedError。
    ダミー判定で評価指標を算出してしまうことを防ぐためのガード。
    """
    status = inference.get_model_status()
    if not inference.is_real_model_loaded():
        raise ModelNotLoadedError(
            "実 ONNX モデルがロードされていません。ダミー判定での評価を防ぐため中止します。\n"
            f"  model_path  : {status['model_path']}\n"
            f"  model_exists: {status['model_exists']}\n"
            "MODEL_PATH 環境変数で有効な .onnx を指定してください。"
        )
    return status


def _predict_one(sample: EvalSample) -> _Prediction:
    """1 サンプルを既存推論パイプラインで推論する（/inspect と同一の流れ）。"""
    with open(sample.image_path, "rb") as f:
        image_bytes = f.read()
    img = load_image(image_bytes)
    tensor = to_tensor(preprocess(img))
    result = inference.run_inference(tensor)
    return _Prediction(
        sample=sample,
        result=result["result"],
        score=float(result["score"]),
        defect_type=result["defect_type"],
        message=result["message"],
        inference_ms=float(result.get("inference_ms", 0.0)),
    )


def _write_detail_csv(path: Path, predictions: list[_Prediction]) -> None:
    """サンプル単位の詳細結果を CSV 出力する（UTF-8 BOM 付き、Excel 互換）。"""
    header = [
        "image_path", "true_label", "true_class",
        "predicted_result", "predicted_defect_type", "score",
        "inference_ms", "correct",
    ]
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(header)
        for p in predictions:
            writer.writerow([
                p.sample.image_path,
                p.sample.true_label,
                p.sample.true_class,
                p.result,
                p.defect_type,
                f"{p.score:.4f}",
                f"{p.inference_ms:.2f}",
                "1" if p.result == p.sample.true_class else "0",
            ])


def _inference_time_stats(predictions: list[_Prediction]) -> dict:
    times = sorted(p.inference_ms for p in predictions)
    if not times:
        return {"avg_ms": 0.0, "min_ms": 0.0, "max_ms": 0.0, "p95_ms": 0.0}
    p95_idx = min(len(times) - 1, int(round(0.95 * (len(times) - 1))))
    return {
        "avg_ms": round(sum(times) / len(times), 2),
        "min_ms": round(times[0], 2),
        "max_ms": round(times[-1], 2),
        "p95_ms": round(times[p95_idx], 2),
    }


def evaluate_dataset(
    samples: list[EvalSample],
    output_dir: str | Path,
    *,
    require_model: bool = True,
    dataset_path: str = "",
    dataset_format: str = "",
) -> dict:
    """データセットを評価し、CSV / JSON を出力してサマリ辞書を返す。

    Args:
        samples: 評価対象サンプル（dataset_loader で読み込んだもの）。
        output_dir: 出力先ルート。実行ごとのタイムスタンプ付きサブフォルダを作成する。
        require_model: True の場合、実モデル未ロードなら ModelNotLoadedError で中止する。
        dataset_path / dataset_format: サマリに記録するメタ情報（任意）。

    Returns:
        サマリ辞書（evaluation_summary.json と同一内容）。
    """
    if not samples:
        raise ValueError("評価対象サンプルが空です。")

    model_status = _ensure_model_loaded() if require_model else inference.get_model_status()

    timestamp = datetime.now()
    run_dir = Path(output_dir) / f"eval_{timestamp:%Y%m%d_%H%M%S}"
    run_dir.mkdir(parents=True, exist_ok=True)

    predictions: list[_Prediction] = []
    errors: list[dict] = []

    wall_start = time.perf_counter()
    for sample in samples:
        try:
            predictions.append(_predict_one(sample))
        except Exception as e:  # 1 枚の失敗で全体を止めない
            logger.warning("評価中に画像処理失敗: %s (%s)", sample.image_path, e)
            errors.append({"image_path": sample.image_path, "error": str(e)})
    wall_elapsed = time.perf_counter() - wall_start

    if not predictions:
        raise RuntimeError("全サンプルの推論に失敗しました。画像・モデルを確認してください。")

    y_true = [p.sample.true_class for p in predictions]
    y_pred = [p.result for p in predictions]
    metrics = classification_report(y_true, y_pred, labels=["OK", "NG"], positive="NG")

    summary = {
        "evaluated_at": timestamp.isoformat(timespec="seconds"),
        "engine": ENGINE_NAME,
        "model": model_status,
        "dataset": {
            "path": dataset_path,
            "format": dataset_format,
            "total_samples": len(samples),
            "evaluated": len(predictions),
            "failed": len(errors),
        },
        "metrics": metrics,
        "inference_time": _inference_time_stats(predictions),
        "wall_time_sec": round(wall_elapsed, 2),
        "errors": errors,
        "notes": PREPROCESSING_NOTE,
    }

    detail_path = run_dir / DETAIL_CSV_NAME
    summary_path = run_dir / SUMMARY_JSON_NAME
    _write_detail_csv(detail_path, predictions)
    with open(summary_path, "w", encoding="utf-8") as f:
        json.dump(summary, f, ensure_ascii=False, indent=2)

    summary["output"] = {
        "run_dir": str(run_dir.resolve()),
        "detail_csv": str(detail_path.resolve()),
        "summary_json": str(summary_path.resolve()),
    }
    logger.info(
        "評価完了: accuracy=%.4f NG(recall)=%.4f n=%d -> %s",
        metrics["accuracy"], metrics["recall"], len(predictions), run_dir,
    )
    return summary
