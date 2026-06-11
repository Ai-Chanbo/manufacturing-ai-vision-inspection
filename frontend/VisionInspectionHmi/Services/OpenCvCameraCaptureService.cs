using OpenCvSharp;

namespace VisionInspectionHmi.Services;

/// <summary>
/// OpenCvSharp を使用した実カメラ向け単発撮像実装。
/// VideoCapture を都度開閉するため、ライブプレビュー中は同一インデックスと
/// 競合しないよう注意すること（PoC では別インデックスまたはカメラ停止後に使用）。
/// </summary>
public sealed class OpenCvCameraCaptureService : ICameraCaptureService
{
    private bool _disposed;

    public Task<string> CaptureAsync(
        int cameraIndex,
        string saveDirectory,
        int timeoutMs = 5000,
        CancellationToken ct = default)
        => Task.Run(() => DoCapture(cameraIndex, saveDirectory, ct), ct);

    private static string DoCapture(int cameraIndex, string saveDirectory, CancellationToken ct)
    {
        using var cap = new VideoCapture(cameraIndex);
        if (!cap.IsOpened())
            throw new InvalidOperationException(
                $"カメラ(index={cameraIndex})を開けませんでした。" +
                "接続状態・他アプリの使用中を確認してください。");

        using var mat = new Mat();

        // ウォームアップ: 最初の数フレームは露出が安定しない場合がある
        const int WarmupFrames = 5;
        for (int i = 0; i < WarmupFrames && !ct.IsCancellationRequested; i++)
        {
            cap.Read(mat);
            Thread.Sleep(30);
        }

        ct.ThrowIfCancellationRequested();

        if (!cap.Read(mat) || mat.Empty())
            throw new InvalidOperationException(
                "カメラからフレームを取得できませんでした。");

        var ts       = DateTime.Now;
        var fileName = $"capture_{ts:yyyyMMdd_HHmmss_fff}.jpg";
        Directory.CreateDirectory(saveDirectory);
        var filePath = Path.Combine(saveDirectory, fileName);
        Cv2.ImWrite(filePath, mat);

        AppLogger.Info($"撮像完了: {Path.GetFileName(filePath)}");
        return filePath;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
