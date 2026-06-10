namespace VisionInspectionHmi.Services;

public static class NgImageSaverService
{
    private static readonly string DefaultNgDir =
        Path.Combine(AppContext.BaseDirectory, "Results", "NG");

    private static string? _ngDir;

    /// <summary>NG画像保存フォルダ。空文字列または null の場合はデフォルトフォルダを使用。</summary>
    public static string NgDirectory
    {
        get => string.IsNullOrWhiteSpace(_ngDir) ? DefaultNgDir : _ngDir;
        set => _ngDir = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static string? Save(string sourcePath, DateTime inspectedAt)
    {
        try
        {
            var dir = NgDirectory;
            Directory.CreateDirectory(dir);
            var ext  = Path.GetExtension(sourcePath);
            var stem = Path.GetFileNameWithoutExtension(sourcePath);
            var name = $"{inspectedAt:yyyyMMdd_HHmmss}_{stem}{ext}";
            var dest = Path.Combine(dir, name);
            File.Copy(sourcePath, dest, overwrite: true);
            return dest;
        }
        catch
        {
            return null;
        }
    }
}
