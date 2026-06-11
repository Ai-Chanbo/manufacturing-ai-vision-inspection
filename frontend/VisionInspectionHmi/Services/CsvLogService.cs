using System.Text;
using VisionInspectionHmi.Models;

namespace VisionInspectionHmi.Services;

public static class CsvLogService
{
    private static readonly string DefaultLogsDir =
        Path.Combine(AppContext.BaseDirectory, "Logs");

    private static string? _logsDir;

    /// <summary>CSV保存フォルダ。空文字列または null の場合はデフォルトフォルダを使用。</summary>
    public static string LogsDir
    {
        get => string.IsNullOrWhiteSpace(_logsDir) ? DefaultLogsDir : _logsDir;
        set => _logsDir = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static readonly string[] Header =
    [
        "検査日時", "画像ファイル名", "画像パス", "撮像画像パス", "判定結果",
        "スコア", "異常種別", "メッセージ", "API通信結果", "推論時間(ms)"
    ];

    public static void Save(InspectionHistory record)
    {
        var dir = LogsDir;
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, $"inspection_log_{DateTime.Now:yyyyMMdd}.csv");
        bool writeHeader = !File.Exists(filePath);

        using var sw = new StreamWriter(filePath, append: true, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        if (writeHeader)
            sw.WriteLine(string.Join(",", Header.Select(EscapeCsv)));

        sw.WriteLine(string.Join(",", new[]
        {
            record.InspectedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            record.ImageFileName,
            record.ImagePath,
            record.CapturedImagePath,
            record.Result,
            record.Score.ToString("F4"),
            record.DefectType,
            record.Message,
            record.ApiStatus,
            record.InferenceMs > 0 ? record.InferenceMs.ToString("F2") : "",
        }.Select(EscapeCsv)));
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return $"\"{value}\"";
    }
}
