"""データセット評価 CLI ランナー。

使い方（backend ディレクトリで実行）:
    python -m app.evaluate_cli --dataset <path> --format mvtec|folder|csv [--output <dir>]

例:
    python -m app.evaluate_cli --dataset ./datasets/bottle --format mvtec
    python -m app.evaluate_cli --dataset ./datasets/sample --format folder
    python -m app.evaluate_cli --dataset ./labels.csv --format csv --output ./eval_output

注意:
    実 ONNX モデルが未ロード（ダミー判定状態）の場合は、評価精度が無意味になるため
    既定で中止する。検証目的でダミー判定のまま評価したい場合のみ --allow-dummy を付与する。
"""

from __future__ import annotations

import argparse
import logging
import sys

from app.dataset_loader import load_dataset
from app.evaluation import evaluate_dataset, ModelNotLoadedError


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="app.evaluate_cli",
        description="データセットを評価し、CSV と evaluation_summary.json を出力する。",
    )
    parser.add_argument("--dataset", required=True, help="データセットのパス（フォルダ or CSV）")
    parser.add_argument("--format", required=True, choices=["mvtec", "folder", "csv"],
                        help="データセット形式")
    parser.add_argument("--output", default="eval_output", help="出力先ルート（既定: eval_output）")
    parser.add_argument("--allow-dummy", action="store_true",
                        help="検証用: 実モデル未ロードでもダミー判定で評価を続行する")
    args = parser.parse_args(argv)

    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(name)s: %(message)s")

    try:
        samples = load_dataset(args.dataset, args.format)
    except Exception as e:
        print(f"[エラー] データセット読み込み失敗: {e}", file=sys.stderr)
        return 2

    try:
        summary = evaluate_dataset(
            samples,
            args.output,
            require_model=not args.allow_dummy,
            dataset_path=args.dataset,
            dataset_format=args.format,
        )
    except ModelNotLoadedError as e:
        print(f"[中止] {e}", file=sys.stderr)
        return 3
    except Exception as e:
        print(f"[エラー] 評価失敗: {e}", file=sys.stderr)
        return 1

    m = summary["metrics"]
    print("\n========== 評価結果 ==========")
    print(f"エンジン      : {summary['engine']}")
    print(f"モデル        : {summary['model']['model_path']} "
          f"(dummy={summary['model']['using_dummy']})")
    print(f"サンプル数    : {summary['dataset']['evaluated']} / "
          f"{summary['dataset']['total_samples']} (失敗 {summary['dataset']['failed']})")
    print(f"Accuracy      : {m['accuracy']:.4f}")
    print(f"NG Precision  : {m['per_class']['NG']['precision']:.4f}")
    print(f"NG Recall     : {m['per_class']['NG']['recall']:.4f}")
    print(f"NG F1         : {m['per_class']['NG']['f1']:.4f}")
    print(f"混同行列(行=正解, 列=予測, 順={m['labels']}):")
    for label, row in zip(m["labels"], m["confusion_matrix"]):
        print(f"  {label}: {row}")
    print(f"推論時間      : avg {summary['inference_time']['avg_ms']}ms / "
          f"p95 {summary['inference_time']['p95_ms']}ms")
    print(f"出力先        : {summary['output']['run_dir']}")
    print("==============================\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
