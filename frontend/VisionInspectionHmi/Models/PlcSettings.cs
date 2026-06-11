namespace VisionInspectionHmi.Models;

/// <summary>
/// PLC Modbus TCP 接続設定。
/// すべての値は設定画面または settings.json から変更可能。
/// レジスタアドレスは 0 始まりの保持レジスタ番号（Modbus FC3/FC6）。
/// </summary>
public class PlcSettings
{
    // 接続設定
    public string IpAddress         { get; set; } = "127.0.0.1";
    public int    Port              { get; set; } = 502;
    public int    PollingIntervalMs { get; set; } = 100;

    // 実 PLC の代わりに FakePlcCommunicationService を使用する（PoC・デモ用）
    public bool UseFakeService { get; set; } = true;

    // ── レジスタマップ ──────────────────────────────────────────────
    // D100: PLC → PC  撮像トリガ    (0=待機, 1=検査開始)
    public int TriggerAddress   { get; set; } = 100;

    // D101: PC → PLC  検査中フラグ  (0=待機, 1=処理中)
    public int BusyAddress      { get; set; } = 101;

    // D102: PC → PLC  判定結果      (0=未判定, 1=OK, 2=NG)
    public int ResultAddress    { get; set; } = 102;

    // D103: PC → PLC  エラーコード  (0=正常, 1=通信異常, 2=推論異常)
    public int ErrorCodeAddress { get; set; } = 103;

    // D104: PC → PLC  ハートビート  (PC 稼働監視用カウンタ, 毎秒インクリメント)
    public int HeartbeatAddress { get; set; } = 104;
}
