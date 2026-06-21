using System.Text;

namespace VisionInspectionHmi.Services;

/// <summary>
/// フォルダ一括検査（バッチ評価）のロジック。
/// 画像収集・MVTec AD 形式の正解ラベル付与・混同行列/指標の集計・CSV 出力を担う。
/// UI（進捗・結果表示）は BatchInspectionForm 側が担当する。
/// </summary>
public static class BatchEvaluationService
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp"];

    /// <summary>対象フォルダ配下（再帰）の画像ファイルを決定的な順序で収集する。</summary>
    public static List<string> CollectImages(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// MVTec AD 形式の正解ラベル。直近の親フォルダ名が "good" なら OK、それ以外は NG。
    /// </summary>
    public static string ExpectedLabel(string filePath)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");
        return string.Equals(parent, "good", StringComparison.OrdinalIgnoreCase) ? "OK" : "NG";
    }

    /// <summary>評価結果を CSV へ出力し、出力先パスを返す。</summary>
    public static string WriteCsv(IEnumerable<BatchRow> rows, string dir)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"batch_eval_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        var sb = new StringBuilder();
        sb.AppendLine("FilePath,Expected,Predicted,Score,InferenceMs");
        foreach (var r in rows)
            sb.AppendLine($"{Escape(r.FilePath)},{r.Expected},{r.Predicted}," +
                          $"{r.Score.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                          $"{r.InferenceMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}");

        // Excel 互換のため UTF-8 BOM 付きで出力
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return path;
    }

    private static string Escape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}

/// <summary>バッチ評価の 1 件分の結果行。</summary>
public sealed record BatchRow(string FilePath, string Expected, string Predicted, double Score, double InferenceMs);

/// <summary>
/// 混同行列と評価指標。陽性クラスは NG（欠陥検出）とする。
///   TP: NG を NG と判定 / TN: OK を OK と判定 / FP: OK を NG と誤検出 / FN: NG を OK と見逃し
/// </summary>
public sealed class BatchMetrics
{
    public int Tp { get; private set; }
    public int Tn { get; private set; }
    public int Fp { get; private set; }
    public int Fn { get; private set; }

    public int Total => Tp + Tn + Fp + Fn;

    public double Accuracy  => Total == 0 ? 0 : (double)(Tp + Tn) / Total;
    public double Precision => (Tp + Fp) == 0 ? 0 : (double)Tp / (Tp + Fp);
    public double Recall    => (Tp + Fn) == 0 ? 0 : (double)Tp / (Tp + Fn);

    public double F1
    {
        get
        {
            double p = Precision, r = Recall;
            return (p + r) == 0 ? 0 : 2 * p * r / (p + r);
        }
    }

    /// <summary>正解(OK/NG)と判定(OK/NG)を 1 件加算する。判定が OK/NG 以外（エラー等）は無視。</summary>
    public void Add(string expected, string predicted)
    {
        bool actualNg = expected == "NG";
        if (predicted == "NG")
        {
            if (actualNg) Tp++; else Fp++;
        }
        else if (predicted == "OK")
        {
            if (actualNg) Fn++; else Tn++;
        }
        // それ以外（ERROR 等）は集計対象外
    }
}
