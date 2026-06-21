using VisionInspectionHmi.Services;

namespace VisionInspectionHmi.Forms;

/// <summary>
/// フォルダ一括検査（バッチ評価）ダイアログ。
/// 選択フォルダ配下の画像を 1 枚ずつ IInspectionEngine で推論し、MVTec AD 形式
/// （親フォルダ good=OK / それ以外=NG）で TP/TN/FP/FN・Accuracy/Precision/Recall/F1 を集計、
/// CSV を出力する。推論は await で逐次実行するため UI はフリーズしない。
/// </summary>
public sealed class BatchInspectionForm : Form
{
    private readonly IInspectionEngine _engine;
    private readonly double            _threshold;
    private readonly string            _folder;
    private readonly string            _csvDir;

    private ProgressBar _progress  = null!;
    private Label       _lblFolder = null!;
    private Label       _lblCount  = null!;
    private TextBox     _txtResults = null!;
    private Button      _btnAction = null!;

    private volatile bool _cancelled;
    private bool _done;

    public BatchInspectionForm(IInspectionEngine engine, double threshold, string folder, string csvDir)
    {
        _engine    = engine;
        _threshold = threshold;
        _folder    = folder;
        _csvDir    = csvDir;
        InitializeComponent();
        Shown        += async (_, _) => await RunAsync();
        FormClosing  += (_, _) => _cancelled = true;
    }

    private void InitializeComponent()
    {
        Text            = "フォルダ検査（MVTec AD 評価）";
        Size            = new Size(620, 470);
        MinimumSize     = new Size(620, 470);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        BackColor       = Color.FromArgb(245, 245, 250);

        var header = new Label
        {
            Text = $"対象フォルダを再帰検索して連続検査します（閾値 {_threshold:F2}）",
            Left = 12, Top = 10, Width = 584, Height = 20,
            Font = new Font("Meiryo UI", 9, FontStyle.Bold),
        };

        _lblFolder = new Label
        {
            Text = _folder,
            Left = 12, Top = 34, Width = 584, Height = 20,
            Font = new Font("Meiryo UI", 8), ForeColor = Color.DimGray,
            AutoEllipsis = true,
        };

        _progress = new ProgressBar
        {
            Left = 12, Top = 60, Width = 494, Height = 22,
            Style = ProgressBarStyle.Continuous,
        };
        _lblCount = new Label
        {
            Text = "0 / 0",
            Left = 514, Top = 60, Width = 82, Height = 22,
            Font = new Font("Meiryo UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
        };

        _txtResults = new TextBox
        {
            Left = 12, Top = 92, Width = 584, Height = 296,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            Font = new Font("Consolas", 10),
            Text = "検査中...",
        };

        _btnAction = new Button
        {
            Left = 500, Top = 396, Width = 96, Height = 32,
            Text = "キャンセル",
            Font = new Font("Meiryo UI", 9, FontStyle.Bold),
            BackColor = Color.DimGray, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        _btnAction.Click += (_, _) =>
        {
            if (_done) { Close(); return; }
            _cancelled = true;
            _btnAction.Enabled = false;
            _btnAction.Text = "中止中...";
        };

        Controls.AddRange([header, _lblFolder, _progress, _lblCount, _txtResults, _btnAction]);
    }

    private async Task RunAsync()
    {
        List<string> files;
        try
        {
            files = BatchEvaluationService.CollectImages(_folder);
        }
        catch (Exception ex)
        {
            AppLogger.Error("バッチ: 画像収集に失敗", ex);
            _txtResults.Text = $"画像の収集に失敗しました:\r\n{ex.Message}";
            Finish();
            return;
        }

        if (files.Count == 0)
        {
            _lblCount.Text   = "0 / 0";
            _txtResults.Text = "対象画像（png/jpg/jpeg/bmp）が見つかりませんでした。";
            Finish();
            return;
        }

        _progress.Minimum = 0;
        _progress.Maximum = files.Count;
        _progress.Value   = 0;

        var metrics = new BatchMetrics();
        var rows    = new List<BatchRow>(files.Count);
        int errors  = 0;

        for (int i = 0; i < files.Count; i++)
        {
            if (_cancelled) break;

            string file     = files[i];
            string expected = BatchEvaluationService.ExpectedLabel(file);

            string predicted;
            double score = 0, ms = 0;
            try
            {
                var r     = await _engine.InspectAsync(file, _threshold);
                if (IsDisposed) return;   // 実行中に閉じられた場合は中断
                predicted = r.Result;
                score     = r.Score;
                ms        = r.InferenceMs;
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                predicted = "ERROR";
                errors++;
                AppLogger.Error($"バッチ検査失敗: {file}", ex);
            }

            metrics.Add(expected, predicted);
            rows.Add(new BatchRow(file, expected, predicted, score, ms));

            _progress.Value = i + 1;
            _lblCount.Text  = $"{i + 1} / {files.Count}";
        }

        string csvPath = "";
        try
        {
            csvPath = BatchEvaluationService.WriteCsv(rows, _csvDir);
        }
        catch (Exception ex)
        {
            AppLogger.Error("バッチ: CSV 出力に失敗", ex);
        }

        ShowSummary(metrics, files.Count, rows.Count, errors, csvPath);
        Finish();
    }

    private void ShowSummary(BatchMetrics m, int total, int processed, int errors, string csvPath)
    {
        int okImgs = m.Tn + m.Fp;   // 正解が OK の画像数
        int ngImgs = m.Tp + m.Fn;   // 正解が NG の画像数

        var sb = new System.Text.StringBuilder();
        if (_cancelled) sb.AppendLine("※ キャンセルされました（途中までの集計）").AppendLine();

        sb.AppendLine($"総件数        : {total}（処理 {processed} / 正解OK {okImgs} / 正解NG {ngImgs}）");
        if (errors > 0) sb.AppendLine($"推論エラー    : {errors}（集計対象外）");
        sb.AppendLine();
        sb.AppendLine("=== 混同行列（陽性 = NG）===");
        sb.AppendLine($"  TP（NG→NG 検出）: {m.Tp}");
        sb.AppendLine($"  TN（OK→OK 正常）: {m.Tn}");
        sb.AppendLine($"  FP（OK→NG 過検出）: {m.Fp}");
        sb.AppendLine($"  FN（NG→OK 見逃し）: {m.Fn}");
        sb.AppendLine();
        sb.AppendLine("=== 評価指標 ===");
        sb.AppendLine($"  Accuracy  : {m.Accuracy:F4}");
        sb.AppendLine($"  Precision : {m.Precision:F4}");
        sb.AppendLine($"  Recall    : {m.Recall:F4}");
        sb.AppendLine($"  F1        : {m.F1:F4}");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrEmpty(csvPath) ? "CSV: 出力に失敗しました" : $"CSV: {csvPath}");

        _txtResults.Text = sb.ToString();
    }

    private void Finish()
    {
        _done = true;
        if (_progress.Maximum > 0) _progress.Value = _progress.Maximum;
        _btnAction.Text    = "閉じる";
        _btnAction.Enabled = true;
        _btnAction.BackColor = Color.SeaGreen;
    }
}
