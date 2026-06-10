using System.Text.Json;
using VisionInspectionHmi.Models;

namespace VisionInspectionHmi.Services;

public static class AppSettingsService
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static AppSettings _current = new();

    public static AppSettings Current => _current;

    /// <summary>settings.json を読み込む。存在しない場合はデフォルト値を使用する。</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath, System.Text.Encoding.UTF8);
                _current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            _current = new AppSettings();
        }
    }

    /// <summary>設定を settings.json に保存し、Current を更新する。</summary>
    public static void Save(AppSettings settings)
    {
        _current = settings;
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(FilePath, json, System.Text.Encoding.UTF8);
    }
}
