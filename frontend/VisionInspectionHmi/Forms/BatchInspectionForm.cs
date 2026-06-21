using VisionInspectionHmi.Models;
using VisionInspectionHmi.Services;

namespace VisionInspectionHmi.Forms;

/// <summary>
/// フォルダ一括検査（バッチ評価）ダイアログ。
/// 選択フォルダ配下の画像を 1 枚ずつ IInspectionEngine で推論し、MVTec AD 形式
/// （親フォルダ good=OK / それ以外=NG）で TP/TN/FP/FN・Accuracy/Precision/Recall/F1 を集計、
/// CSV を出力する。推論は await で逐次実行するため UI はフリーズしない。
///
/// 「モニターモード」ON 時は、各画像ごとに MainForm 側のプレビュー・判定ラベル・
/// ヒートマップ・履歴を更新し、指定 Delay 待機しながらデモ表示する。
/// 評価ロジック（指標・CSV 出力）はモードに関わらず共通。
/// </summary>
public sealed class BatchInspectionForm : Form
{
    private readonly IInspectionEngine _engine;
    private readonly double            _threshold;
    private readonly string            _folder;
    private readonly string            _csvDir;

    // モニターモード用コールバック（推論前=画像表示 / 推論後=結果・ヒートマップ・履歴）
    private readonly Action<string>?                   _onMonitorImage;
    private readonly Action<string, InspectionResult>? _onMonitorResult;

    private CheckBox      _chkMonitor = null!;
    private NumericUpDown _nudDelay   = null!;
    private Button        _btnStart   = null!;
    private ProgressBar   _progress   = null!;
    private Label         _lblFolder  = null!;
    private Label         _lblCount   = null!;
    private TextBox       _txtResults = null!;
    private Button        _btnAction  = null!;

    private volatile bool _cancelled;
    private bool _done;
    private bool _running;

    public BatchInspectionForm(
        IInspectionEngine engine, double threshold, string folder, string csvDir,
        Action<string>? onMonitorImage = null,
        Action<string, InspectionResult>? onMonitorResult = null)
    {
        _engine          = engine;
        _threshold       = threshold;
        _folder          = folder;
        _csvDir          = csvDir;
        _onMonitorImage  = onMonitorImage;
        _onMonitorResult = onMonitorResult;
        InitializeComponent();
        Shown       += (_, _) => PositionBesideOwner();
        FormClosing += (_, _) => _cancelled = true;
    }

    private void InitializeComponent()
    {
        // DPI(125%等)でも崩れないよう None で統一（ウィンドウ全体を一律に扱う）。
        AutoScaleMode   = AutoScaleMode.None;
        Text            = "フォルダ検査（MVTec AD 評価）";
        Size            = new Size(620, 580);
        MinimumSize     = new Size(620, 580);
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
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _lblFolder = new Label
        {
            Text = _folder,
            Left = 12, Top = 34, Width = 584, Height = 20,
            Font = new Font("Meiryo UI", 8), ForeColor = Color.DimGray,
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        // ── モニターモード行（チェックボックスは単独行：横方向の重なりを防止）──
        _chkMonitor = new CheckBox
        {
            Text = "モニターモード（デモ表示：1枚ずつ画面更新）",
            Left = 12, Top = 60, AutoSize = true,
            Font = new Font("Meiryo UI", 9),
            Checked = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };

        // ── 遅延・開始行 ───────────────────────────────────────
        var lblDelay = new Label
        {
            Text = "遅延(ms):",
            Left = 12, Top = 92, Width = 64, Height = 20,
            Font = new Font("Meiryo UI", 9),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _nudDelay = new NumericUpDown
        {
            Left = 80, Top = 90, Width = 72, Height = 22,
            Font = new Font("Meiryo UI", 9),
            Minimum = 0, Maximum = 5000, Increment = 100, Value = 500,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _btnStart = new Button
        {
            Left = 164, Top = 88, Width = 120, Height = 28,
            Text = "▶ 開始",
            Font = new Font("Meiryo UI", 9, FontStyle.Bold),
            BackColor = Color.SeaGreen, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _btnStart.Click += async (_, _) => await StartAsync();

        // ── 進捗・件数行 ───────────────────────────────────────
        _progress = new ProgressBar
        {
            Left = 12, Top = 126, Width = 494, Height = 22,
            Style = ProgressBarStyle.Continuous,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _lblCount = new Label
        {
            Text = "0 / 0",
            Left = 514, Top = 126, Width = 82, Height = 22,
            Font = new Font("Meiryo UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        // ── 結果表示（長い CSV パスは WordWrap で折り返し＋縦スクロール）──
        _txtResults = new TextBox
        {
            Left = 12, Top = 156, Width = 584, Height = 324,
            Multiline = true, ReadOnly = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            Font = new Font("Consolas", 10),
            Text = "「▶ 開始」で検査を開始します。",
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };

        _btnAction = new Button
        {
            Left = 500, Top = 490, Width = 96, Height = 32,
            Text = "閉じる",
            Font = new Font("Meiryo UI", 9, FontStyle.Bold),
            BackColor = Color.DimGray, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        _btnAction.Click += (_, _) =>
        {
            if (_done || !_running) { Close(); return; }
            _cancelled = true;
            _btnAction.Enabled = false;
            _btnAction.Text = "中止中...";
        };

        Controls.AddRange([header, _lblFolder, _chkMonitor, lblDelay, _nudDelay, _btnStart,
                            _progress, _lblCount, _txtResults, _btnAction]);
    }

    // モニター表示中に MainForm のプレビューが隠れないよう、オーナー右端に寄せる。
    private void PositionBesideOwner()
    {
        if (Owner == null) return;
        var ob = Owner.Bounds;
        int x = Math.Max(ob.Right - Width - 16, ob.Left + 8);
        int y = ob.Top + Math.Max((ob.Height - Height) / 2, 8);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(x, y);
    }

    private async Task StartAsync()
    {
        if (_running || _done) return;
        bool monitor = _chkMonitor.Checked && _onMonitorResult != null;
        int  delay   = (int)_nudDelay.Value;

        _chkMonitor.Enabled = false;
        _nudDelay.Enabled   = false;
        _btnStart.Enabled   = false;
        _btnAction.Text     = "キャンセル";
        _running = true;

        await RunAsync(monitor, delay);

        _running = false;
        Finish();
    }

    private async Task RunAsync(bool monitor, int delayMs)
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
            return;
        }

        if (files.Count == 0)
        {
            _lblCount.Text   = "0 / 0";
            _txtResults.Text = "対象画像（png/jpg/jpeg/bmp）が見つかりませんでした。";
            return;
        }

        _progress.Minimum = 0;
        _progress.Maximum = files.Count;
        _progress.Value   = 0;
        _txtResults.Text  = monitor ? "モニターモードで検査中..." : "検査中...";

        var metrics = new BatchMetrics();
        var rows    = new List<BatchRow>(files.Count);
        int errors  = 0;

        for (int i = 0; i < files.Count; i++)
        {
            if (_cancelled) break;
            string file     = files[i];
            string expected = BatchEvaluationService.ExpectedLabel(file);

            // モニターモード: 推論前に画像をプレビュー表示（画面更新）
            if (monitor)
            {
                try { _onMonitorImage?.Invoke(file); } catch (Exception ex) { AppLogger.Error("モニター画像更新失敗", ex); }
            }

            // 推論
            string predicted; double score = 0, ms = 0;
            InspectionResult? result = null;
            try
            {
                result = await _engine.InspectAsync(file, _threshold);
                if (IsDisposed) return;
                predicted = result.Result;
                score     = result.Score;
                ms        = result.InferenceMs;
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                predicted = "ERROR";
                errors++;
                AppLogger.Error($"バッチ検査失敗: {file}", ex);
            }

            // モニターモード: 推論後に結果・ヒートマップ・履歴を更新
            if (monitor && result != null)
            {
                try { _onMonitorResult?.Invoke(file, result); } catch (Exception ex) { AppLogger.Error("モニター結果更新失敗", ex); }
            }

            metrics.Add(expected, predicted);
            rows.Add(new BatchRow(file, expected, predicted, score, ms));

            _progress.Value = i + 1;
            _lblCount.Text  = $"{i + 1} / {files.Count}";

            // モニターモード: 次の画像まで待機
            if (monitor && delayMs > 0 && !_cancelled)
            {
                await Task.Delay(delayMs);
                if (IsDisposed) return;
            }
        }

        string csvPath = "";
        try { csvPath = BatchEvaluationService.WriteCsv(rows, _csvDir); }
        catch (Exception ex) { AppLogger.Error("バッチ: CSV 出力に失敗", ex); }

        ShowSummary(metrics, files.Count, rows.Count, errors, csvPath);
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
        _btnAction.Text      = "閉じる";
        _btnAction.Enabled   = true;
        _btnAction.BackColor = Color.SeaGreen;
    }
}
