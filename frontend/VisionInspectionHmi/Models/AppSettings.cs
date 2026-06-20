namespace VisionInspectionHmi.Models;

public class AppSettings
{
    // API設定
    public string ApiUrl            { get; set; } = "http://localhost:8000";
    public int    ApiTimeoutSeconds { get; set; } = 30;

    // 検査設定
    public double NgThreshold      { get; set; } = 0.5;   // 分類モデル用（確信度しきい値）
    public bool   InferenceEnabled { get; set; } = true;

    // 異常検知（EfficientAD）用しきい値。pred_score >= AnomalyThreshold → NG。
    // 分類用 NgThreshold とは意味が異なるため独立管理する。
    public double AnomalyThreshold { get; set; } = 0.5;

    // 保存設定（空文字列 = デフォルトフォルダを使用）
    public string CsvDirectory    { get; set; } = "";
    public string NgImageDirectory { get; set; } = "";

    // カメラ設定
    public CameraSettings CameraSettings { get; set; } = new();

    // 推論モード設定
    public string InferenceMode  { get; set; } = "FastAPI"; // "FastAPI" | "ONNX"
    public string OnnxModelPath  { get; set; } = "";

    // ONNX モデル種別。"Auto"（出力名から自動判定）/ "Classification" / "Anomaly"。
    public string OnnxModelType  { get; set; } = "Auto";

    // PLC 連携設定
    public PlcSettings PlcSettings { get; set; } = new();
}
