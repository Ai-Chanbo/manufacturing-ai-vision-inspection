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
}

/// <summary>Top-N推論候補の1エントリ。</summary>
public sealed record Top5Entry(int Rank, string Label, double Score);
