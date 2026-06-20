using System.Text.Json.Serialization;

namespace VisionInspectionHmi.Models;

public class InspectionResult
{
    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("defect_type")]
    public string DefectType { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("inference_ms")]
    public double InferenceMs { get; set; }

    // ONNX推論のみ設定される。FastAPI モードでは空文字列 / 空リスト。
    [JsonIgnore]
    public string ClassName { get; set; } = "";

    [JsonIgnore]
    public List<Top5Entry> Top5Candidates { get; set; } = [];

    // ── 異常検知（EfficientAD）専用フィールド ──────────────────────
    // Anomaly エンジンのみ設定される。分類 / FastAPI モードでは null / 0。
    // すべて [JsonIgnore] のため CSV・JSON 出力には影響しない（後方互換）。

    /// <summary>異常マップの生値（長さ = Width × Height、行優先）。未設定時は null。</summary>
    [JsonIgnore]
    public float[]? AnomalyMap { get; set; }

    /// <summary>異常マップの幅。</summary>
    [JsonIgnore]
    public int AnomalyMapWidth { get; set; }

    /// <summary>異常マップの高さ。</summary>
    [JsonIgnore]
    public int AnomalyMapHeight { get; set; }

    /// <summary>異常マップの最大値（ヒートマップ正規化・しきい値表示用）。</summary>
    [JsonIgnore]
    public double AnomalyMapMax { get; set; }
}

/// <summary>Top-N推論候補の1エントリ。</summary>
public sealed record Top5Entry(int Rank, string Label, double Score);
