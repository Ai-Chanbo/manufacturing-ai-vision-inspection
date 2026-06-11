namespace VisionInspectionHmi.Services;

/// <summary>
/// PLC 通信サービスの抽象インターフェース。
/// 実装は ModbusTcpPlcCommunicationService（実 PLC）と
/// FakePlcCommunicationService（シミュレーター）の 2 種類。
/// </summary>
public interface IPlcCommunicationService : IDisposable
{
    bool IsConnected { get; }

    Task<bool>   ConnectAsync();
    void         Disconnect();
    Task<short?> ReadRegisterAsync(int address);
    Task<bool>   WriteRegisterAsync(int address, short value);
}
