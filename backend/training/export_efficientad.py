"""学習済み EfficientAD チェックポイントを ONNX へエクスポートする（S3）。

使い方:
  .venv-train/Scripts/python export_efficientad.py --ckpt results/.../model.ckpt
"""

from __future__ import annotations

import argparse
from pathlib import Path

from anomalib.models import EfficientAd
from anomalib.engine import Engine
from anomalib.deploy import ExportType


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--ckpt", required=True, help="学習済み .ckpt のパス")
    ap.add_argument("--imagenet-dir", default="datasets/imagenette")
    ap.add_argument("--model-size", default="small", choices=["small", "medium"])
    ap.add_argument("--export-root", default="exported")
    ap.add_argument("--height", type=int, default=256)
    ap.add_argument("--width", type=int, default=256)
    args = ap.parse_args()

    model = EfficientAd(imagenet_dir=args.imagenet_dir, model_size=args.model_size)
    engine = Engine(accelerator="cpu", devices=1, default_root_dir="results")

    out = engine.export(
        model=model,
        export_type=ExportType.ONNX,
        export_root=args.export_root,
        input_size=(args.height, args.width),
        ckpt_path=args.ckpt,
    )
    print(f"\n=== ONNX_EXPORTED: {out} ===")


if __name__ == "__main__":
    main()
