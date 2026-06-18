"""データセット読み込みモジュール。

評価対象の画像群を 3 つの形式から読み込み、共通の EvalSample リストに正規化する。

サポートする形式:
  - mvtec : MVTec AD 形式  (<category>/test/<defect_type>/*.png、good=正常)
  - folder: フォルダ階層形式 (<root>/<label>/*.jpg)
  - csv   : ラベル CSV 形式  (image_path,label の 2 列)

各サンプルは生ラベル (true_label) と OK/NG 正規化ラベル (true_class) の両方を保持する。
現状の評価は OK/NG 二値（外観検査の実際の判定単位）を基準とする。
多クラス欠陥分類の評価は将来拡張とする。
"""

from __future__ import annotations

import csv
from dataclasses import dataclass
from pathlib import Path

# 推論 API と同じ対応拡張子（MVTec の .png を含む）
SUPPORTED_EXTS = {".jpg", ".jpeg", ".png", ".bmp"}

# OK（正常）とみなす生ラベル（小文字比較）。これ以外はすべて NG（不良）扱い。
NORMAL_LABELS = {"good", "ok", "none", "normal", "pass", "negative", "正常"}

OK = "OK"
NG = "NG"


@dataclass(frozen=True)
class EvalSample:
    """評価対象の 1 サンプル。"""

    image_path: str   # 画像ファイルの絶対パス
    true_label: str   # 生ラベル（フォルダ名 / CSV のラベル / MVTec の欠陥種別）
    true_class: str   # OK/NG 正規化ラベル


def normalize_label(raw_label: str) -> str:
    """生ラベルを OK / NG に正規化する。"""
    return OK if raw_label.strip().lower() in NORMAL_LABELS else NG


def _collect_images(directory: Path) -> list[Path]:
    """ディレクトリ配下（再帰）の対応画像を名前順で収集する。"""
    return sorted(
        p for p in directory.rglob("*")
        if p.is_file() and p.suffix.lower() in SUPPORTED_EXTS
    )


# ──────────────────────────────────────────────────────────────
#  形式別ローダー
# ──────────────────────────────────────────────────────────────

def load_folder(root: str | Path) -> list[EvalSample]:
    """フォルダ階層形式を読み込む。

    直下の各サブフォルダ名を生ラベルとし、その配下（再帰）の画像を収集する。
    例: dataset/OK/*.jpg, dataset/NG/*.jpg
    """
    root_path = Path(root)
    if not root_path.is_dir():
        raise NotADirectoryError(f"データセットフォルダが存在しません: {root_path}")

    samples: list[EvalSample] = []
    for label_dir in sorted(p for p in root_path.iterdir() if p.is_dir()):
        label = label_dir.name
        for img in _collect_images(label_dir):
            samples.append(EvalSample(str(img.resolve()), label, normalize_label(label)))

    if not samples:
        raise ValueError(
            f"画像が見つかりませんでした: {root_path}\n"
            f"<root>/<label>/*.jpg の構造になっているか確認してください。"
        )
    return samples


def load_mvtec(root: str | Path) -> list[EvalSample]:
    """MVTec AD 形式を読み込む。

    <category>/test/<defect_type>/*.png を対象とし、defect_type が "good" のものを
    OK、それ以外（scratch, crack など）を NG とする。train/ と ground_truth/ は無視する。
    root 直下に test/ が無い場合は root 自身を test ディレクトリとみなす。
    """
    root_path = Path(root)
    if not root_path.is_dir():
        raise NotADirectoryError(f"MVTecデータセットフォルダが存在しません: {root_path}")

    test_dir = root_path / "test"
    base = test_dir if test_dir.is_dir() else root_path

    samples: list[EvalSample] = []
    for defect_dir in sorted(p for p in base.iterdir() if p.is_dir()):
        defect_type = defect_dir.name
        true_class = OK if defect_type.lower() == "good" else NG
        for img in _collect_images(defect_dir):
            samples.append(EvalSample(str(img.resolve()), defect_type, true_class))

    if not samples:
        raise ValueError(
            f"MVTec形式の画像が見つかりませんでした: {root_path}\n"
            f"<category>/test/<defect_type>/*.png の構造を確認してください。"
        )
    return samples


def load_csv(csv_path: str | Path) -> list[EvalSample]:
    """ラベル CSV 形式を読み込む。

    各行は image_path,label の 2 列。相対パスは CSV ファイルの位置を基準に解決する。
    1 行目が image_path/label を含むヘッダーの場合は自動でスキップする。
    """
    csv_file = Path(csv_path)
    if not csv_file.is_file():
        raise FileNotFoundError(f"ラベルCSVが存在しません: {csv_file}")

    base_dir = csv_file.parent
    samples: list[EvalSample] = []

    with open(csv_file, encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        for line_no, row in enumerate(reader, start=1):
            if not row or all(not c.strip() for c in row):
                continue
            if len(row) < 2:
                raise ValueError(
                    f"CSV {line_no}行目: image_path,label の2列が必要です: {row}"
                )
            raw_path, label = row[0].strip(), row[1].strip()

            # ヘッダー行を自動スキップ
            if line_no == 1 and raw_path.lower() in {"image_path", "path", "image", "file"}:
                continue

            img_path = Path(raw_path)
            if not img_path.is_absolute():
                img_path = (base_dir / img_path).resolve()

            samples.append(EvalSample(str(img_path), label, normalize_label(label)))

    if not samples:
        raise ValueError(f"CSVから有効なサンプルを読み込めませんでした: {csv_file}")
    return samples


def load_dataset(path: str | Path, fmt: str) -> list[EvalSample]:
    """形式名を指定してデータセットを読み込むディスパッチャ。"""
    loaders = {
        "mvtec": load_mvtec,
        "folder": load_folder,
        "csv": load_csv,
    }
    key = fmt.strip().lower()
    if key not in loaders:
        raise ValueError(f"未対応の形式です: {fmt} (mvtec / folder / csv のいずれか)")
    return loaders[key](path)
