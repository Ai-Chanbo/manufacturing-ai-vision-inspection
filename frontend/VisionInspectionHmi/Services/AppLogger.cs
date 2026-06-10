using System.Collections.Concurrent;

namespace VisionInspectionHmi.Services;

/// <summary>
/// アプリケーションログをファイルに書き出す静的サービス。
/// スレッドセーフ・非同期書き込み。ファイルは日付ごとにローテーション。
/// </summary>
public static class AppLogger
{
    private static readonly ConcurrentQueue<string> _queue = new();
    private static readonly SemaphoreSlim _signal = new(0);
    private static string? _logsDir;
    private static Thread? _worker;
    private static volatile bool _stopping;

    public static string LogsDir
    {
        get => string.IsNullOrWhiteSpace(_logsDir)
                ? Path.Combine(AppContext.BaseDirectory, "Logs")
                : _logsDir;
        set => _logsDir = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // ── 起動 / 停止 ──────────────────────────────────────────────

    public static void Start()
    {
        if (_worker?.IsAlive == true) return;
        _stopping = false;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "AppLogger" };
        _worker.Start();
        Info("アプリケーション起動");
    }

    public static void Stop()
    {
        Info("アプリケーション終了");
        _stopping = true;
        _signal.Release();
        _worker?.Join(3000);
    }

    // ── ログ書き込みメソッド ──────────────────────────────────────

    public static void Info(string message)  => Enqueue("INFO ", message);
    public static void Warn(string message)  => Enqueue("WARN ", message);
    public static void Error(string message, Exception? ex = null)
    {
        var msg = ex is null ? message : $"{message}\n  {ex.GetType().Name}: {ex.Message}";
        Enqueue("ERROR", msg);
    }

    // ── 専用イベントログ ─────────────────────────────────────────

    public static void LogInspection(string fileName, string result, double score,
                                     string defectType, double inferenceMs, string mode)
    {
        Info($"検査実行 [{mode}] {fileName} → {result} score={score:F4} " +
             $"defect={defectType} {inferenceMs:F1}ms");
    }

    public static void LogInferenceFailed(string fileName, Exception ex)
        => Error($"推論失敗: {fileName}", ex);

    public static void LogCameraStarted(int index)
        => Info($"カメラ起動: index={index}");

    public static void LogCameraStopped()
        => Info("カメラ停止");

    public static void LogCameraError(int index, Exception ex)
        => Error($"カメラエラー: index={index}", ex);

    public static void LogModelLoaded(string modelPath)
        => Info($"ONNXモデル読込完了: {Path.GetFileName(modelPath)}");

    public static void LogModelLoadFailed(string modelPath, Exception ex)
        => Error($"ONNXモデル読込失敗: {Path.GetFileName(modelPath)}", ex);

    public static void LogSettingsChanged()
        => Info("設定変更を適用");

    // ── 内部実装 ─────────────────────────────────────────────────

    private static void Enqueue(string level, string message)
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        _queue.Enqueue($"[{ts}][{level}] {message}");
        _signal.Release();
    }

    private static void WorkerLoop()
    {
        while (true)
        {
            _signal.Wait();
            if (_stopping && _queue.IsEmpty) break;

            while (_queue.TryDequeue(out var line))
            {
                try
                {
                    Directory.CreateDirectory(LogsDir);
                    var filePath = Path.Combine(LogsDir, $"app_{DateTime.Now:yyyyMMdd}.log");
                    File.AppendAllText(filePath, line + Environment.NewLine,
                                       System.Text.Encoding.UTF8);
                }
                catch
                {
                    // ログ失敗は無視（ループ継続）
                }
            }
        }
    }
}
