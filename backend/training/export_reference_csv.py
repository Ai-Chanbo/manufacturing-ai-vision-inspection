"""Python onnxruntime の基準推論結果を書き出す（S4: C# パリティ検証用）。

`verify_onnx_infer.py` と完全に同じ前処理を用い、C# 側と突合するための
基準データを生成する。出力は次の3種類:

  1. reference.csv          … 画像ごとの pred_score / pred_label / anomaly_map 統計
  2. inputs/<id>.bin        … 前処理済み入力テンソル (float32 LE, 1x3xHxW)
                              → Tier1（前処理差を排除したランタイム純粋比較）用
  3. outputs/<id>.amap.bin  … anomaly_map 生出力 (float32 LE, フラット化)
                              → Tier1 で要素ごとに突合する基準

  manifest.json            … モデルパス / 入力サイズ / ORT バージョン等の追跡情報

C# 側はこれらの .bin を BinaryReader でそのまま読めるよう、すべて
リトルエンディアン float32 で保存する。

使い方:
  .venv-train/Scripts/python export_reference_csv.py \
      --onnx exported/weights/onnx/model.onnx
"""

from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image

DEFAULT_ONNX = "exported/weights/onnx/model.onnx"
DEFAULT_TEST_DIR = "datasets/MVTecAD/bottle/test"
INPUT_SIZE = 256


def preprocess(path: Path, input_size: int) -> np.ndarray:
    """verify_onnx_infer.py と同一の前処理（RGB→resize→/255→NCHW、正規化なし）。"""
    img = Image.open(path).convert("RGB").resize((input_size, input_size))
    arr = np.asarray(img, dtype=np.float32) / 255.0      # [0,1] RGB HWC
    arr = np.transpose(arr, (2, 0, 1))[None, ...]        # NCHW
    return np.ascontiguousarray(arr)


def save_f32_le(path: Path, arr: np.ndarray) -> None:
    """リトルエンディアン float32 の生バイト列として保存（C# BinaryReader 互換）。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(np.ascontiguousarray(arr, dtype="<f4").tobytes())


def collect_images(test_dir: Path, per_class: int) -> list[tuple[str, Path]]:
    """各クラスから先頭 per_class 枚を決定的な順序で収集する。"""
    items: list[tuple[str, Path]] = []
    for cls_dir in sorted(test_dir.iterdir()):
        if not cls_dir.is_dir():
            continue
        for img_path in sorted(cls_dir.glob("*.png"))[:per_class]:
            image_id = f"{cls_dir.name}__{img_path.stem}"
            items.append((image_id, img_path))
    return items


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--onnx", default=DEFAULT_ONNX)
    ap.add_argument("--test-dir", default=DEFAULT_TEST_DIR)
    ap.add_argument("--per-class", type=int, default=3, help="各クラスから書き出す枚数")
    ap.add_argument("--out-dir", default="reference", help="基準データの出力先")
    ap.add_argument("--input-size", type=int, default=INPUT_SIZE)
    args = ap.parse_args()

    onnx_path = Path(args.onnx)
    test_dir = Path(args.test_dir)
    out_dir = Path(args.out_dir)
    if not onnx_path.exists():
        raise FileNotFoundError(f"ONNX が見つかりません: {onnx_path}")
    if not test_dir.exists():
        raise FileNotFoundError(f"テスト画像ディレクトリが見つかりません: {test_dir}")

    sess = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    in_name = sess.get_inputs()[0].name
    out_names = [o.name for o in sess.get_outputs()]

    items = collect_images(test_dir, args.per_class)
    if not items:
        raise RuntimeError(f"対象画像が見つかりません: {test_dir}")

    rows: list[dict] = []
    for image_id, img_path in items:
        x = preprocess(img_path, args.input_size)
        named = dict(zip(out_names, sess.run(out_names, {in_name: x})))

        pred_score = float(np.ravel(named["pred_score"])[0]) if "pred_score" in named else float("nan")
        pred_label = int(np.ravel(named["pred_label"])[0]) if "pred_label" in named else -1

        amap = next((named[n] for n in out_names if "map" in n.lower() and "mask" not in n.lower()), None)
        if amap is None:
            raise RuntimeError("anomaly_map 出力が見つかりません")
        amap = np.asarray(amap, dtype=np.float32)
        amap_shape = amap.shape          # 通常 (1,1,H,W)
        amap_h, amap_w = int(amap_shape[-2]), int(amap_shape[-1])

        # Tier1 用の生データ保存
        save_f32_le(out_dir / "inputs" / f"{image_id}.bin", x)
        save_f32_le(out_dir / "outputs" / f"{image_id}.amap.bin", amap)

        rows.append({
            "image_id": image_id,
            "image_path": str(img_path).replace("\\", "/"),
            "class": image_id.split("__", 1)[0],
            "pred_score": f"{pred_score:.8f}",
            "pred_label": pred_label,
            "amap_h": amap_h,
            "amap_w": amap_w,
            "amap_min": f"{float(amap.min()):.8f}",
            "amap_max": f"{float(amap.max()):.8f}",
            "amap_mean": f"{float(amap.mean()):.8f}",
            "input_bin": f"inputs/{image_id}.bin",
            "amap_bin": f"outputs/{image_id}.amap.bin",
        })
        print(f"  [{image_id}] score={pred_score:.4f} label={pred_label} "
              f"amap={amap_shape} min={amap.min():.4f} max={amap.max():.4f}")

    # CSV 出力
    out_dir.mkdir(parents=True, exist_ok=True)
    csv_path = out_dir / "reference.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)

    # manifest（追跡情報）
    manifest = {
        "onnx_path": str(onnx_path).replace("\\", "/"),
        "input_name": in_name,
        "output_names": out_names,
        "input_size": args.input_size,
        "input_layout": "NCHW",
        "preprocess": "RGB / resize(bilinear PIL default) / div255 / no mean-std",
        "byte_order": "little-endian float32",
        "ort_version": ort.__version__,
        "num_images": len(rows),
    }
    (out_dir / "manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"\n=== 書き出し完了 ===")
    print(f"  CSV      : {csv_path}")
    print(f"  inputs   : {out_dir / 'inputs'} ({len(rows)} files)")
    print(f"  outputs  : {out_dir / 'outputs'} ({len(rows)} files)")
    print(f"  manifest : {out_dir / 'manifest.json'}  (ORT {ort.__version__})")


if __name__ == "__main__":
    main()
