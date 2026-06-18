"""エクスポートした EfficientAD ONNX を onnxruntime で検証する（S3b）。

確認項目:
  - ONNX 入出力の名前 / 形状 / 型
  - 前処理（Resize / 正規化）がグラフへ焼き込まれているか（グラフ走査）
  - pred_score / pred_label / anomaly_map の取得可否と内容
  - good 画像と欠陥画像で異常スコアが分離するか（異常判定の妥当性）

使い方:
  .venv-train/Scripts/python verify_onnx_infer.py --onnx exported/.../model.onnx
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
from PIL import Image

TEST_DIR = Path("datasets/MVTecAD/bottle/test")
INPUT_SIZE = 256


def inspect_graph(onnx_path: str) -> None:
    model = onnx.load(onnx_path)
    g = model.graph
    print("=== GRAPH I/O ===")
    for inp in g.input:
        shape = [d.dim_value if d.dim_value > 0 else d.dim_param for d in inp.type.tensor_type.shape.dim]
        print(f"  INPUT  {inp.name}  shape={shape}  dtype={inp.type.tensor_type.elem_type}")
    for out in g.output:
        shape = [d.dim_value if d.dim_value > 0 else d.dim_param for d in out.type.tensor_type.shape.dim]
        print(f"  OUTPUT {out.name}  shape={shape}  dtype={out.type.tensor_type.elem_type}")

    op_types = [n.op_type for n in g.node]
    from collections import Counter
    print("\n=== OP TYPE COUNTS ===")
    print(Counter(op_types))

    # 前処理焼込みの痕跡（Resize / 正規化系オペ）
    pre_ops = [op for op in ["Resize", "Sub", "Div", "Mul", "Normalize"] if op in op_types]
    print("\n=== 前処理焼込みの痕跡 ===")
    print("  検出されたオペ:", pre_ops)
    print("  → Resize があれば任意サイズ入力可 / Sub・Div・Mul があれば正規化がグラフ内の可能性")


def preprocess(path: Path) -> np.ndarray:
    img = Image.open(path).convert("RGB").resize((INPUT_SIZE, INPUT_SIZE))
    arr = np.asarray(img, dtype=np.float32) / 255.0      # [0,1] RGB HWC
    arr = np.transpose(arr, (2, 0, 1))[None, ...]        # NCHW
    return np.ascontiguousarray(arr)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--onnx", required=True)
    ap.add_argument("--per-class", type=int, default=3, help="各クラスから検証する枚数")
    args = ap.parse_args()

    inspect_graph(args.onnx)

    sess = ort.InferenceSession(args.onnx, providers=["CPUExecutionProvider"])
    in_name = sess.get_inputs()[0].name
    out_meta = sess.get_outputs()
    out_names = [o.name for o in out_meta]
    print("\n=== ORT RUNTIME I/O ===")
    print("  input :", sess.get_inputs()[0].name, sess.get_inputs()[0].shape, sess.get_inputs()[0].type)
    for o in out_meta:
        print("  output:", o.name, o.shape, o.type)

    print("\n=== 推論結果（クラス別）===")
    scores_by_class: dict[str, list[float]] = {}
    for cls_dir in sorted(TEST_DIR.iterdir()):
        if not cls_dir.is_dir():
            continue
        imgs = sorted(cls_dir.glob("*.png"))[: args.per_class]
        for img_path in imgs:
            x = preprocess(img_path)
            outs = sess.run(out_names, {in_name: x})
            named = dict(zip(out_names, outs))

            # pred_score 優先、無ければ anomaly_map の最大値
            if "pred_score" in named:
                score = float(np.ravel(named["pred_score"])[0])
            else:
                amap = next((named[n] for n in out_names if "map" in n.lower()), None)
                score = float(np.max(amap)) if amap is not None else float("nan")

            label = None
            if "pred_label" in named:
                label = int(np.ravel(named["pred_label"])[0])

            amap = next((named[n] for n in out_names if "map" in n.lower()), None)
            amap_info = f"map.shape={amap.shape} min={amap.min():.4f} max={amap.max():.4f}" if amap is not None else "no map"

            scores_by_class.setdefault(cls_dir.name, []).append(score)
            print(f"  [{cls_dir.name:14s}] {img_path.name}: score={score:.4f} label={label} | {amap_info}")

    print("\n=== クラス別 平均スコア（good < 欠陥 が期待）===")
    for cls, scores in scores_by_class.items():
        print(f"  {cls:14s}: mean={np.mean(scores):.4f}  (n={len(scores)})")

    good = np.mean(scores_by_class.get("good", [float("nan")]))
    defects = [s for c, ss in scores_by_class.items() if c != "good" for s in ss]
    if defects:
        print(f"\n  good平均={good:.4f}  欠陥平均={np.mean(defects):.4f}  "
              f"→ 分離 {'OK ✓' if np.mean(defects) > good else 'NG ✗（要追加学習）'}")


if __name__ == "__main__":
    main()
