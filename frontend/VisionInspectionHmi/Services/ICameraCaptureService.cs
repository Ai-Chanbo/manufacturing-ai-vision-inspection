namespace VisionInspectionHmi.Services;

/// <summary>
/// PLCトリガ時の単発カメラ撮像インターフェース。
/// 実カメラ (OpenCvCameraCaptureService) とシミュレーター (FakeCameraCaptureService) の
/// 両実装をサポートする。
/// </summary>
public interface ICameraCaptureService : IDisposable
{
    /// <summary>
    /// 1フレームを撮像し、保存したファイルパスを返す。
    /// </summary>
    /// <param name="cameraIndex">使用するカメラのインデックス番号</param>
    /// <param name="saveDirectory">画像の保存先ディレクトリ（存在しない場合は自動作成）</param>
    /// <param name="timeoutMs">撮像タイムアウト (ms)</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>保存した画像ファイルの絶対パス</returns>
    Task<string> CaptureAsync(
        int cameraIndex,
        string saveDirectory,
        int timeoutMs = 5000,
        CancellationToken ct = default);
}
