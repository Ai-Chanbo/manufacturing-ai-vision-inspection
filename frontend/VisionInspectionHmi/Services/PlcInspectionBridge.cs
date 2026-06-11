using VisionInspectionHmi.Models;

namespace VisionInspectionHmi.Services;

/// <summary>
/// PLC 撮像トリガを監視し、AI 推論を実行して結果を PLC へ返却するブリッジ。
/// IPlcCommunicationService 経由で実 PLC / FakePLC の両方に対応する。
/// </summary>
public sealed class PlcInspectionBridge : IDisposable
{
    // 判定コード (ResultAddress / D102)
    public const short ResultPending = 0;
    public const short ResultOk      = 1;
    public const short ResultNg      = 2;

    // エラーコード (ErrorCodeAddress / D103)
    public const short ErrorNone          = 0;
    public const short ErrorCommunication = 1;
    public const short ErrorInference     = 2;

    private readonly IPlcCommunicationService _plc;
    private readonly OnnxInspectionService    _onnx;
    private readonly PlcSettings              _settings;

    private CancellationTokenSource? _cts;
    private Task?                    _pollingTask;
    private volatile bool            _isInspecting;
    private int                      _heartbeatCounter;
    private bool                     _disposed;

    /// <summary>推論完了時に発火。UI スレッドへの Invoke が必要。</summary>
    public event EventHandler<PlcInspectionEventArgs>? InspectionCompleted;

    /// <summary>ステータス文字列変化時に発火。UI スレッドへの Invoke が必要。</summary>
    public event EventHandler<string>? StatusChanged;

    public bool IsPolling => _pollingTask is { IsCompleted: false };

    public PlcInspectionBridge(
        IPlcCommunicationService plc,
        OnnxInspectionService    onnx,
        PlcSettings              settings)
    {
        _plc      = plc;
        _onnx     = onnx;
        _settings = settings;
    }

    // ──────────────────────────────────────────────────────────────
    //  公開メソッド
    // ──────────────────────────────────────────────────────────────

    /// <summary>PLC ポーリングを開始する。</summary>
    /// <param name="getImagePath">呼び出しのたびに最新の検査対象画像パスを返す関数</param>
    /// <param name="ngThreshold">OK/NG 判定スコア閾値</param>
    public void StartPolling(Func<string?> getImagePath, double ngThreshold)
    {
        if (IsPolling) return;
        _cts         = new CancellationTokenSource();
        _pollingTask = Task.Run(() => PollingLoop(getImagePath, ngThreshold, _cts.Token));

        AppLogger.Info($"PLCポーリング開始 ({_settings.IpAddress}:{_settings.Port}" +
                       $" interval={_settings.PollingIntervalMs}ms)");
        StatusChanged?.Invoke(this, "監視中");
    }

    /// <summary>PLC ポーリングを安全に停止する（完了を非同期待機）。</summary>
    public async Task StopPollingAsync()
    {
        if (_cts == null) return;
        _cts.Cancel();
        if (_pollingTask != null)
            await _pollingTask.ConfigureAwait(false);
        _pollingTask = null;
        AppLogger.Info("PLCポーリング停止");
        StatusChanged?.Invoke(this, "停止");
    }

    // ──────────────────────────────────────────────────────────────
    //  ポーリングループ
    // ──────────────────────────────────────────────────────────────

    private async Task PollingLoop(Func<string?> getImagePath, double ngThreshold, CancellationToken ct)
    {
        short prevTrigger = 0;
        int   hbTick      = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // ハートビート更新（約1秒周期）
                hbTick++;
                if (hbTick * _settings.PollingIntervalMs >= 1000)
                {
                    hbTick = 0;
                    _heartbeatCounter = (_heartbeatCounter + 1) % 32767;
                    await _plc.WriteRegisterAsync(
                        _settings.HeartbeatAddress, (short)_heartbeatCounter);
                }

                // トリガ読み取り
                var trigger = await _plc.ReadRegisterAsync(_settings.TriggerAddress);
                if (trigger == null)
                {
                    StatusChanged?.Invoke(this, "通信エラー");
                    await _plc.WriteRegisterAsync(_settings.ErrorCodeAddress, ErrorCommunication);
                    await Task.Delay(1000, ct);
                    prevTrigger = 0;
                    continue;
                }

                // 立ち上がりエッジ検出（0→1）・二重実行防止
                if (trigger == 1 && prevTrigger == 0 && !_isInspecting)
                {
                    _isInspecting = true;
                    var imagePath = getImagePath();
                    _ = RunInspectionAsync(imagePath, ngThreshold, ct);
                }

                prevTrigger = trigger.Value;
                await Task.Delay(_settings.PollingIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Error("PLCポーリングループ例外", ex);
                await Task.Delay(500, ct);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  検査実行
    // ──────────────────────────────────────────────────────────────

    private async Task RunInspectionAsync(string? imagePath, double ngThreshold, CancellationToken ct)
    {
        try
        {
            // 検査中フラグ ON
            await _plc.WriteRegisterAsync(_settings.BusyAddress, 1);
            await _plc.WriteRegisterAsync(_settings.ResultAddress, ResultPending);
            await _plc.WriteRegisterAsync(_settings.ErrorCodeAddress, ErrorNone);

            StatusChanged?.Invoke(this, "検査中");

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                AppLogger.Warn("PLCトリガ: 検査対象画像が見つかりません");
                await _plc.WriteRegisterAsync(_settings.ErrorCodeAddress, ErrorInference);
                return;
            }

            InspectionResult result;
            try
            {
                result = await _onnx.InspectAsync(imagePath, ngThreshold);
            }
            catch (Exception ex)
            {
                AppLogger.Error("PLCトリガ推論失敗", ex);
                await _plc.WriteRegisterAsync(_settings.ErrorCodeAddress, ErrorInference);
                return;
            }

            short resultCode = result.Result == "OK" ? ResultOk : ResultNg;
            await _plc.WriteRegisterAsync(_settings.ResultAddress, resultCode);
            await _plc.WriteRegisterAsync(_settings.ErrorCodeAddress, ErrorNone);

            AppLogger.Info(
                $"PLCトリガ検査完了: {result.Result} score={result.Score:F4}" +
                $" defect={result.DefectType} {result.InferenceMs:F1}ms");

            InspectionCompleted?.Invoke(this,
                new PlcInspectionEventArgs(imagePath, result, DateTime.Now));

            StatusChanged?.Invoke(this, "監視中");

            // トリガが 0 に戻るまで最大 2 秒待機（連続トリガ防止）
            for (int i = 0; i < 20 && !ct.IsCancellationRequested; i++)
            {
                var t = await _plc.ReadRegisterAsync(_settings.TriggerAddress);
                if (t is null or 0) break;
                await Task.Delay(100, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Error("PLCトリガ処理例外", ex);
            await _plc.WriteRegisterAsync(_settings.ErrorCodeAddress, ErrorInference);
        }
        finally
        {
            await _plc.WriteRegisterAsync(_settings.BusyAddress, 0);
            _isInspecting = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

/// <summary>PLC トリガによる検査完了イベント引数。</summary>
public sealed class PlcInspectionEventArgs : EventArgs
{
    public string           ImagePath   { get; }
    public InspectionResult Result      { get; }
    public DateTime         InspectedAt { get; }

    public PlcInspectionEventArgs(string imagePath, InspectionResult result, DateTime inspectedAt)
    {
        ImagePath   = imagePath;
        Result      = result;
        InspectedAt = inspectedAt;
    }
}
