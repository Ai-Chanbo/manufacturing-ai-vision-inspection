"""EfficientAD を bottle カテゴリで学習する（CPU 可）。

初回実行時に anomalib が以下を自動取得する:
  - PDN 教師の事前学習重み（小）
  - ImageNette ペナルティ集合 imagenette2.tgz（約1.5GB）→ --imagenet-dir へ展開

使い方（backend/training で実行）:
  .venv-train/Scripts/python train_efficientad.py --max-steps 1000
"""

from __future__ import annotations

import argparse
from pathlib import Path

from anomalib.data import MVTecAD
from anomalib.models import EfficientAd
from anomalib.engine import Engine


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--data-root", default="datasets/MVTecAD")
    ap.add_argument("--category", default="bottle")
    ap.add_argument("--imagenet-dir", default="datasets/imagenette")
    ap.add_argument("--model-size", default="small", choices=["small", "medium"])
    ap.add_argument("--max-steps", type=int, default=1000, help="学習ステップ数（CPUでは小さめ推奨）")
    ap.add_argument("--max-epochs", type=int, default=-1)
    ap.add_argument("--results", default="results")
    args = ap.parse_args()

    datamodule = MVTecAD(
        root=args.data_root,
        category=args.category,
        train_batch_size=1,   # EfficientAD はバッチ1必須
        eval_batch_size=8,
        num_workers=0,        # Windows のマルチプロセス問題回避
    )

    model = EfficientAd(
        imagenet_dir=args.imagenet_dir,
        model_size=args.model_size,
    )

    engine = Engine(
        max_steps=args.max_steps,
        max_epochs=args.max_epochs,
        accelerator="cpu",
        devices=1,
        default_root_dir=args.results,
        num_sanity_val_steps=0,
    )

    engine.fit(model=model, datamodule=datamodule)
    test_results = engine.test(model=model, datamodule=datamodule)

    print("\n=== TEST METRICS ===")
    print(test_results)

    # 最良チェックポイントのパスを出力（S3 エクスポートで使用）
    ckpt = engine.trainer.checkpoint_callback.best_model_path if engine.trainer.checkpoint_callback else ""
    if not ckpt:
        ckpt = engine.trainer.checkpoint_callback.last_model_path if engine.trainer.checkpoint_callback else ""
    print(f"\n=== CKPT_PATH: {ckpt} ===")


if __name__ == "__main__":
    main()
