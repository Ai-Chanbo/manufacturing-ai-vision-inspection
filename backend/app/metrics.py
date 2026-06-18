"""評価指標計算モジュール。

Accuracy / Precision / Recall / F1 / Confusion Matrix を numpy のみで算出する
（scikit-learn 等の追加依存を増やさない方針）。

外観検査の判定単位に合わせ、ラベルは既定で ["OK", "NG"] の二値を扱い、
不良検出性能を測る観点から NG を陽性 (positive) クラスとして主要指標を報告する。
"""

from __future__ import annotations

import numpy as np

DEFAULT_LABELS = ["OK", "NG"]


def confusion_matrix(
    y_true: list[str], y_pred: list[str], labels: list[str]
) -> np.ndarray:
    """混同行列を返す。行=正解ラベル、列=予測ラベル（labels の順）。"""
    index = {label: i for i, label in enumerate(labels)}
    matrix = np.zeros((len(labels), len(labels)), dtype=int)
    for true, pred in zip(y_true, y_pred):
        if true in index and pred in index:
            matrix[index[true]][index[pred]] += 1
    return matrix


def _safe_div(numerator: float, denominator: float) -> float:
    return numerator / denominator if denominator > 0 else 0.0


def classification_report(
    y_true: list[str],
    y_pred: list[str],
    labels: list[str] | None = None,
    positive: str = "NG",
) -> dict:
    """分類性能のレポートを辞書で返す。

    含まれる内容:
      - accuracy            : 全体正解率
      - per_class           : クラス別の precision / recall / f1 / support
      - macro_avg           : クラス別指標の単純平均
      - positive_class      : 陽性クラス名（既定 NG）
      - precision/recall/f1 : 陽性クラス基準の主要指標（トップレベルにも展開）
      - confusion_matrix    : labels 順の混同行列（リスト形式）
      - labels              : 使用したラベル順
      - support             : 評価サンプル総数
    """
    if labels is None:
        labels = DEFAULT_LABELS

    matrix = confusion_matrix(y_true, y_pred, labels)
    total = int(matrix.sum())
    accuracy = _safe_div(int(np.trace(matrix)), total)

    per_class: dict[str, dict] = {}
    for i, label in enumerate(labels):
        tp = int(matrix[i, i])
        fp = int(matrix[:, i].sum() - tp)
        fn = int(matrix[i, :].sum() - tp)
        support = int(matrix[i, :].sum())
        precision = _safe_div(tp, tp + fp)
        recall = _safe_div(tp, tp + fn)
        f1 = _safe_div(2 * precision * recall, precision + recall)
        per_class[label] = {
            "precision": round(precision, 4),
            "recall": round(recall, 4),
            "f1": round(f1, 4),
            "support": support,
        }

    macro_avg = {
        "precision": round(float(np.mean([c["precision"] for c in per_class.values()])), 4),
        "recall": round(float(np.mean([c["recall"] for c in per_class.values()])), 4),
        "f1": round(float(np.mean([c["f1"] for c in per_class.values()])), 4),
    }

    pos = per_class.get(positive, {"precision": 0.0, "recall": 0.0, "f1": 0.0})

    return {
        "accuracy": round(accuracy, 4),
        "positive_class": positive,
        "precision": pos["precision"],
        "recall": pos["recall"],
        "f1": pos["f1"],
        "per_class": per_class,
        "macro_avg": macro_avg,
        "confusion_matrix": matrix.tolist(),
        "labels": labels,
        "support": total,
    }
