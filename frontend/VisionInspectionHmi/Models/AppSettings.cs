namespace VisionInspectionHmi.Models;

public class AppSettings
{
    // API設定
    public string ApiUrl            { get; set; } = "http://localhost:8000";
    public int    ApiTimeoutSeconds { get; set; } = 30;

    // 検査設定
    public double NgThreshold      { get; set; } = 0.5;
    public bool   InferenceEnabled { get; set; } = true;

    // 保存設定（空文字列 = デフォルトフォルダを使用）
    public string CsvDirectory    { get; set; } = "";
    public string NgImageDirectory { get; set; } = "";

    // カメラ設定
    public int CameraIndex { get; set; } = 0;

    // 推論モード設定
    public string InferenceMode  { get; set; } = "FastAPI"; // "FastAPI" | "ONNX"
    public string OnnxModelPath  { get; set; } = "";

    // PLC 連携設定
    public PlcSettings PlcSettings { get; set; } = new();
}
