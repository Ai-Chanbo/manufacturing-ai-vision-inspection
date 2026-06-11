using HslCommunication.ModBus;
using VisionInspectionHmi.Models;

namespace VisionInspectionHmi.Services;

/// <summary>
/// HslCommunication の ModbusTcpNet を使用した実 PLC 通信実装。
/// 接続・切断・保持レジスタ読み書きを非同期で提供する。
/// </summary>
public sealed class ModbusTcpPlcCommunicationService : IPlcCommunicationService
{
    private readonly PlcSettings _settings;
    private ModbusTcpNet? _client;
    private bool _disposed;

    public bool IsConnected { get; private set; }

    public ModbusTcpPlcCommunicationService(PlcSettings settings)
    {
        _settings = settings;
    }

    public async Task<bool> ConnectAsync()
    {
        Disconnect();

        _client = new ModbusTcpNet(_settings.IpAddress, _settings.Port)
        {
            Station = 1,
        };

        var result = await Task.Run(() => _client.ConnectServer());
        IsConnected = result.IsSuccess;

        if (IsConnected)
            AppLogger.Info($"PLC接続成功: {_settings.IpAddress}:{_settings.Port}");
        else
            AppLogger.Warn($"PLC接続失敗 [{_settings.IpAddress}:{_settings.Port}]: {result.Message}");

        return IsConnected;
    }

    public void Disconnect()
    {
        if (_client == null) return;
        _client.ConnectClose();
        _client = null;
        IsConnected = false;
        AppLogger.Info("PLC切断");
    }

    public async Task<short?> ReadRegisterAsync(int address)
    {
        if (_client == null || !IsConnected) return null;

        var result = await Task.Run(() => _client.ReadInt16(address.ToString()));
        if (result.IsSuccess) return result.Content;

        AppLogger.Warn($"レジスタ読み取り失敗 [{address}]: {result.Message}");
        IsConnected = false;
        return null;
    }

    public async Task<bool> WriteRegisterAsync(int address, short value)
    {
        if (_client == null || !IsConnected) return false;

        var result = await Task.Run(() => _client.Write(address.ToString(), value));
        if (result.IsSuccess) return true;

        AppLogger.Warn($"レジスタ書き込み失敗 [{address}={value}]: {result.Message}");
        IsConnected = false;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}
