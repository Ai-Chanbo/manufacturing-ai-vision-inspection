using OpenCvSharp;

namespace VisionInspectionHmi.Services;

/// <summary>
/// Webカメラのフレームキャプチャを管理するサービス。
/// FrameReady イベントで Bitmap を提供する。受け取った側で Dispose すること。
/// </summary>
public sealed class CameraService : IDisposable
{
    private VideoCapture? _capture;
    private Thread?       _thread;
    private volatile bool _running;
    private volatile bool _disposed;

    /// <summary>新フレームが到着するたびに発火。Bitmap の Dispose は受信側の責務。</summary>
    public event EventHandler<Bitmap>? FrameReady;

    public bool IsRunning => _running;

    /// <summary>指定インデックスのカメラを起動し、バックグラウンドキャプチャを開始する。</summary>
    public void Start(int index = 0)
    {
        if (_running || _disposed) return;

        var cap = new VideoCapture(index);
        if (!cap.IsOpened())
        {
            cap.Dispose();
            throw new InvalidOperationException(
                $"カメラ(index={index})を開けませんでした。\n" +
                "カメラが接続されているか、他のアプリで使用中でないか確認してください。");
        }

        _capture = cap;
        _running = true;
        _thread  = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name         = "CameraCapture",
        };
        _thread.Start();
    }

    /// <summary>キャプチャを停止してカメラリソースを解放する。</summary>
    public void Stop()
    {
        _running = false;
        _thread?.Join(2000);
        _thread = null;

        _capture?.Dispose();
        _capture = null;
    }

    private void CaptureLoop()
    {
        using var mat = new Mat();
        while (_running && !_disposed)
        {
            try
            {
                if (_capture?.Read(mat) == true && !mat.Empty())
                {
                    var bmp = MatToBitmap(mat);
                    FrameReady?.Invoke(this, bmp);
                }
            }
            catch
            {
                // フレーム取得エラーは無視してループ継続
            }

            Thread.Sleep(33); // ~30fps
        }
    }

    /// <summary>Mat → Bitmap 変換（BMP エンコード経由）</summary>
    private static Bitmap MatToBitmap(Mat mat)
    {
        // Cv2.ImEncode で BMP バイト列を生成し、MemoryStream 経由で Bitmap に変換
        Cv2.ImEncode(".bmp", mat, out var buf);
        return new Bitmap(new MemoryStream(buf));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
