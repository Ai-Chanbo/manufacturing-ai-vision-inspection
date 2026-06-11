namespace VisionInspectionHmi.Services;

/// <summary>
/// 実カメラ不要のカメラシミュレーター。
/// <list type="bullet">
///   <item>fixedImagePath が存在する場合 → そのパスをそのまま返す（既存画像を転用）</item>
///   <item>パス未設定の場合 → System.Drawing でタイムスタンプ入りテスト画像を生成して返す</item>
/// </list>
/// </summary>
public sealed class FakeCameraCaptureService : ICameraCaptureService
{
    private readonly string _fixedImagePath;
    private bool _disposed;

    public FakeCameraCaptureService(string fixedImagePath = "")
    {
        _fixedImagePath = fixedImagePath;
    }

    public async Task<string> CaptureAsync(
        int cameraIndex,
        string saveDirectory,
        int timeoutMs = 5000,
        CancellationToken ct = default)
    {
        await Task.Delay(150, ct); // 撮像時間をシミュレート

        // 設定されたパスが有効であればそれを返す（実画像でAI検証可能）
        if (!string.IsNullOrEmpty(_fixedImagePath) && File.Exists(_fixedImagePath))
        {
            AppLogger.Info($"FakeCamera: 固定画像を使用: {Path.GetFileName(_fixedImagePath)}");
            return _fixedImagePath;
        }

        // フォールバック: System.Drawing でタイムスタンプ入り 224×224 テスト画像を生成
        Directory.CreateDirectory(saveDirectory);
        var ts       = DateTime.Now;
        var filePath = Path.Combine(saveDirectory, $"fake_{ts:yyyyMMdd_HHmmss_fff}.jpg");
        GenerateTestImage(filePath, ts);

        AppLogger.Info($"FakeCamera: テスト画像を生成: {Path.GetFileName(filePath)}");
        return filePath;
    }

    private static void GenerateTestImage(string filePath, DateTime ts)
    {
        using var bmp = new Bitmap(224, 224, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(200, 210, 220));

        using var titleFont = new Font("Arial", 12, FontStyle.Bold);
        using var timeFont  = new Font("Arial",  9);
        using var borderPen = new Pen(Color.DarkSlateGray, 3);

        g.DrawRectangle(borderPen, 4, 4, 215, 215);
        g.DrawString("FakeCamera",          titleFont, Brushes.DarkSlateBlue, 52,  88);
        g.DrawString(ts.ToString("HH:mm:ss.fff"), timeFont, Brushes.DimGray, 62, 114);

        bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Jpeg);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
