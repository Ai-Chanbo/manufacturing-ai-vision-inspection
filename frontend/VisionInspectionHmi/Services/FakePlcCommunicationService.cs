namespace VisionInspectionHmi.Services;

/// <summary>
/// ソフトウェア PLC シミュレーター。
/// 実 PLC なしで UI とフローを確認するために使用する。
/// <list type="bullet">
///   <item>FireManualTrigger() — UI「テスト発火」ボタンから手動発火</item>
///   <item>AutoTriggerIntervalMs &gt; 0 で自動タイマー発火（デモ用）</item>
/// </list>
/// </summary>
public sealed class FakePlcCommunicationService : IPlcCommunicationService
{
    private readonly Dictionary<int, short> _registers = new();
    private readonly System.Timers.Timer _autoTriggerTimer;
    private int  _triggerAddress = 100;
    private bool _disposed;

    public bool IsConnected { get; private set; }

    /// <summary>自動トリガ間隔 (ms)。0 で無効。最小 500ms。</summary>
    public int AutoTriggerIntervalMs
    {
        get => (int)_autoTriggerTimer.Interval;
        set
        {
            bool enable = value > 0;
            _autoTriggerTimer.Interval = enable ? Math.Max(value, 500) : 5000;
            _autoTriggerTimer.Enabled  = IsConnected && enable;
        }
    }

    public FakePlcCommunicationService()
    {
        _autoTriggerTimer = new System.Timers.Timer(5000) { AutoReset = true, Enabled = false };
        _autoTriggerTimer.Elapsed += (_, _) => SetTrigger();
    }

    public Task<bool> ConnectAsync()
    {
        IsConnected = true;
        AppLogger.Info("FakePLC: 仮想接続成功（シミュレーターモード）");
        return Task.FromResult(true);
    }

    public void Disconnect()
    {
        _autoTriggerTimer.Enabled = false;
        IsConnected = false;
        AppLogger.Info("FakePLC: 仮想切断");
    }

    public Task<short?> ReadRegisterAsync(int address)
    {
        if (!IsConnected) return Task.FromResult<short?>(null);
        _registers.TryGetValue(address, out var val);
        return Task.FromResult<short?>(val);
    }

    public Task<bool> WriteRegisterAsync(int address, short value)
    {
        if (!IsConnected) return Task.FromResult(false);
        _registers[address] = value;
        return Task.FromResult(true);
    }

    /// <summary>
    /// 手動トリガ発火。UI の「テスト発火」ボタンから呼び出す。
    /// 次のポーリングサイクルで監視ループがトリガを検出する。
    /// </summary>
    public void FireManualTrigger()
    {
        if (!IsConnected) return;
        SetTrigger();
    }

    /// <summary>FakePLC が使用するトリガアドレスを設定する。PlcSettings.TriggerAddress と同期する。</summary>
    public void SetTriggerAddress(int address) => _triggerAddress = address;

    private void SetTrigger()
    {
        _registers[_triggerAddress] = 1;
        AppLogger.Info("FakePLC: 撮像トリガ発火");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _autoTriggerTimer.Dispose();
    }
}
