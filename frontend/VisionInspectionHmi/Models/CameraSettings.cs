namespace VisionInspectionHmi.Models;

public class CameraSettings
{
    // カメラインデックス（ライブプレビュー / PLC撮像で共用）
    public int CameraIndex { get; set; } = 0;

    // PLCトリガ時の自動撮像
    public bool   UseCameraOnPlcTrigger   { get; set; } = false;
    public bool   SaveCapturedImage       { get; set; } = true;
    public string CapturedImageDirectory  { get; set; } = "";
    public int    CaptureTimeoutMs        { get; set; } = 5000;

    // シミュレーター設定
    public bool   UseFakeCamera       { get; set; } = true;
    public string FakeCameraImagePath { get; set; } = "";
}
