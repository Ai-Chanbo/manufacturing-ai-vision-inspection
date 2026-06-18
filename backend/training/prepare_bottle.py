"""TheoM55/mvtec_all_objects_split の bottle parquet を標準 MVTec AD フォルダ構成へ展開する。

出力構成（anomalib MVTecAD datamodule がそのまま読める形）:
    datasets/MVTecAD/bottle/
        train/good/000.png ...
        test/good/000.png ...
        test/<defect>/000.png ...
        ground_truth/<defect>/000_mask.png ...

parquet スキーマ: image_path{bytes,path}, split, object, defect, label, mask_path{bytes,path}
"""

from __future__ import annotations

from pathlib import Path
import pyarrow.parquet as pq

RAW = Path(__file__).parent / "datasets" / "_raw"
OUT = Path(__file__).parent / "datasets" / "MVTecAD" / "bottle"


def _write_split(parquet_path: Path) -> dict[str, int]:
    table = pq.read_table(parquet_path)
    rows = table.to_pylist()
    counters: dict[str, int] = {}
    for r in rows:
        split = r["split"]              # "train" / "test"
        defect = r["defect"]            # "good" / "broken_large" / ...
        img_bytes = r["image_path"]["bytes"]
        mask = r["mask_path"]["bytes"] if r["mask_path"] else None

        img_dir = OUT / split / defect
        img_dir.mkdir(parents=True, exist_ok=True)
        key = f"{split}/{defect}"
        idx = counters.get(key, 0)
        counters[key] = idx + 1
        name = f"{idx:03d}"

        (img_dir / f"{name}.png").write_bytes(img_bytes)

        # 欠陥画像のマスクは ground_truth/<defect>/<name>_mask.png へ（任意・画素評価用）
        if mask and defect != "good":
            gt_dir = OUT / "ground_truth" / defect
            gt_dir.mkdir(parents=True, exist_ok=True)
            (gt_dir / f"{name}_mask.png").write_bytes(mask)
    return counters


def main() -> None:
    total: dict[str, int] = {}
    for pq_name in ["bottle.train.parquet", "bottle.test.parquet"]:
        c = _write_split(RAW / pq_name)
        for k, v in c.items():
            total[k] = total.get(k, 0) + v
    print("展開完了:", OUT)
    for k in sorted(total):
        print(f"  {k}: {total[k]} 枚")


if __name__ == "__main__":
    main()
