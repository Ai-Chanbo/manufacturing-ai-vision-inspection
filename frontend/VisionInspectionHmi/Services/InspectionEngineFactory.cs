using Microsoft.ML.OnnxRuntime;

namespace VisionInspectionHmi.Services;

/// <summary>
/// モデル種別を解決し、対応する <see cref="IInspectionEngine"/> を生成するファクトリ。
///
/// 種別指定（AppSettings.OnnxModelType, 段階3以降で追加予定）:
///   "Auto"           … モデル出力名から自動判定（pred_score + anomaly_map → 異常検知）
///   "Anomaly"        … 異常検知（AnomalyInspectionService）を明示指定
///   "Classification" … 分類（OnnxInspectionService）を明示指定
///
/// 段階2時点では MainForm からは未使用。エンジン生成の単体検証用に追加する。
/// </summary>
public static class InspectionEngineFactory
{
    /// <summary>
    /// モデル種別を解決してエンジンを生成し、モデルをロードして返す。
    /// </summary>
    /// <param name="modelPath">.onnx ファイルパス</param>
    /// <param name="modelType">"Auto"（既定）/ "Anomaly" / "Classification"</param>
    public static IInspectionEngine Create(string modelPath, string modelType = "Auto")
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"ONNXモデルが見つかりません: {modelPath}");

        var kind = ResolveKind(modelPath, modelType);

        IInspectionEngine engine = kind == InspectionEngineKind.Anomaly
            ? new AnomalyInspectionService()
            : new OnnxInspectionService();

        engine.LoadModel(modelPath);
        return engine;
    }

    /// <summary>
    /// モデル種別を解決する。明示指定があればそれを優先し、"Auto" の場合のみ
    /// モデルの出力メタデータを覗いて自動判定する。
    /// </summary>
    public static InspectionEngineKind ResolveKind(string modelPath, string modelType)
    {
        if (modelType.Equals("Anomaly", StringComparison.OrdinalIgnoreCase))
            return InspectionEngineKind.Anomaly;
        if (modelType.Equals("Classification", StringComparison.OrdinalIgnoreCase))
            return InspectionEngineKind.Classification;

        // "Auto"（またはそれ以外）: 出力名から判定
        return DetectKind(modelPath);
    }

    /// <summary>
    /// モデルの出力名を覗いて種別を推定する。
    /// pred_score / anomaly_map（mask は除く）を持てば異常検知、そうでなければ分類。
    /// ※判定のためだけに一時セッションを生成する（呼び出しは設定変更時など低頻度を想定）。
    /// </summary>
    public static InspectionEngineKind DetectKind(string modelPath)
    {
        using var session = new InferenceSession(modelPath);
        var outKeys = session.OutputMetadata.Keys.ToList();

        bool hasScore = outKeys.Any(k => k.Contains("score", StringComparison.OrdinalIgnoreCase));
        bool hasMap   = outKeys.Any(k => k.Contains("map", StringComparison.OrdinalIgnoreCase)
                                         && !k.Contains("mask", StringComparison.OrdinalIgnoreCase));

        return hasScore && hasMap
            ? InspectionEngineKind.Anomaly
            : InspectionEngineKind.Classification;
    }
}
