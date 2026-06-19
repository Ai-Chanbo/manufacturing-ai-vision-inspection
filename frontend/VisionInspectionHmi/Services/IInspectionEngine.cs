using VisionInspectionHmi.Models;

namespace VisionInspectionHmi.Services;

/// <summary>
/// 推論エンジンの種別。判定ロジック・UI表示・閾値の選択分岐に使用する。
///   Classification … 分類モデル（Softmax / Top5 / 確信度ベース。高スコア=自信あり）
///   Anomaly         … 異常検知モデル（EfficientAD 等。pred_score / anomaly_map。高スコア=異常）
/// </summary>
public enum InspectionEngineKind
{
    Classification,
    Anomaly,
}

/// <summary>
/// ローカル ONNX 推論エンジンの共通インターフェース。
/// 分類モデル（OnnxInspectionService）と異常検知モデル（AnomalyInspectionService, 段階2以降）を
/// MainForm / PlcInspectionBridge から具象型に依存せず切り替えられるようにする。
/// </summary>
public interface IInspectionEngine : IDisposable
{
    /// <summary>モデルが読み込み済みか。</summary>
    bool IsLoaded { get; }

    /// <summary>読み込み済みモデルのファイル名（未設定時は「未設定」）。</summary>
    string LoadedModelName { get; }

    /// <summary>入力テンソル形状の表示テキスト（UI 表示用）。</summary>
    string InputShapeText { get; }

    /// <summary>モデル種別の表示テキスト（UI 表示用）。</summary>
    string ModelModeText { get; }

    /// <summary>エンジン種別。判定・表示・閾値の分岐に使用する。</summary>
    InspectionEngineKind Kind { get; }

    /// <summary>モデルを読み込む。既に同じパスなら再ロードしない。</summary>
    void LoadModel(string modelPath);

    /// <summary>画像を推論し結果を返す。threshold の意味はエンジン種別に依存する。</summary>
    Task<InspectionResult> InspectAsync(string imagePath, double threshold);
}
