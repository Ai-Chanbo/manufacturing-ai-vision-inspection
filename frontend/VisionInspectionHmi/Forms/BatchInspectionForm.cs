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
    private TextBox       _lblFolder  = null!;   // フォルダパス表示（ReadOnly・最大2行）
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
        // DPI(100/125/150%)でも崩れないよう None で統一し、TableLayoutPanel で
        // 行・列を管理する。重なり・はみ出し・文字切れを構造的に防止する。
        AutoScaleMode   = AutoScaleMode.None;
        Text            = "フォルダ検査（MVTec AD 評価）";
        Size            = new Size(1000, 750);
        MinimumSize     = new Size(900, 700);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = true;
        MinimizeBox     = false;
        BackColor       = Color.FromArgb(245, 245, 250);
        Padding         = new Padding(18, 14, 18, 14);   // 外周余白（右端も 18px 確保）

        var baseFont = new Font("Meiryo UI", 9);

        // ── ルート（縦方向の行管理）────────────────────────────────
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 0 ヘッダー
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));    // 1 フォルダパス（最大2行）
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));    // 2 モニター行
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));    // 3 進捗行
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // 4 ログ（残り領域）
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));    // 5 閉じる行

        // 0) ヘッダー
        var header = new Label
        {
            Text = $"対象フォルダを再帰検索して連続検査します（閾値 {_threshold:F2}）",
            Dock = DockStyle.Fill, AutoSize = true,
            Font = new Font("Meiryo UI", 10, FontStyle.Bold),
            Margin = new Padding(2, 2, 2, 4),
        };

        // 1) フォルダパス（ReadOnly TextBox・最大2行）
        _lblFolder = new TextBox
        {
            Text = _folder, Dock = DockStyle.Fill,
            ReadOnly = true, Multiline = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(248, 248, 250), ForeColor = Color.DimGray,
            Font = baseFont, Margin = new Padding(2, 0, 2, 6),
        };

        // 2) モニター行（[モニターモード] [遅延(ms)] [NumericUpDown] [開始] を横並び管理）
        var monitorRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(2, 0, 2, 0),
        };
        monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // チェック（左・残り占有）
        monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 遅延ラベル
        monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84)); // NumericUpDown
        monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172));// 開始ボタン
        _chkMonitor = new CheckBox
        {
            Text = "モニターモード（デモ表示：1枚ずつ画面更新）",
            AutoSize = true, Font = baseFont, Checked = false,
            Anchor = AnchorStyles.Left, Margin = new Padding(0),
        };
        var lblDelay = new Label
        {
            Text = "遅延(ms):", AutoSize = true, Font = baseFont,
            Anchor = AnchorStyles.Right, TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(8, 0, 6, 0),
        };
        _nudDelay = new NumericUpDown
        {
            Width = 74, Font = baseFont, Anchor = AnchorStyles.Left,
            Minimum = 0, Maximum = 5000, Increment = 100, Value = 500,
            Margin = new Padding(0, 0, 0, 0),
        };
        _btnStart = new Button
        {
            Dock = DockStyle.Fill, Text = "▶ 開始",
            Font = new Font("Meiryo UI", 10, FontStyle.Bold),
            BackColor = Color.SeaGreen, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 6, 0, 6),
        };
        _btnStart.Click += async (_, _) => await StartAsync();
        monitorRow.Controls.Add(_chkMonitor, 0, 0);
        monitorRow.Controls.Add(lblDelay,    1, 0);
        monitorRow.Controls.Add(_nudDelay,   2, 0);
        monitorRow.Controls.Add(_btnStart,   3, 0);

        // 3) 進捗行（ProgressBar 横幅いっぱい ＋ 件数ラベル右寄せ）
        var progressRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(2, 4, 2, 4),
        };
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96)); // 「9999 / 9999」でも収まる幅
        _progress = new ProgressBar
        {
            Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous,
            Margin = new Padding(0, 2, 8, 2),
        };
        _lblCount = new Label
        {
            Text = "0 / 0", Dock = DockStyle.Fill, Font = new Font("Meiryo UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(0),
        };
        progressRow.Controls.Add(_progress, 0, 0);
        progressRow.Controls.Add(_lblCount, 1, 0);

        // 4) 結果ログ（残り領域を占有・WordWrap＋縦スクロール）
        _txtResults = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true, ReadOnly = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10),
            Text = "「▶ 開始」で検査を開始します。",
            Margin = new Padding(2, 2, 2, 6),
        };

        // 5) 閉じる行（右下に右寄せ）
        var bottomRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(2, 4, 2, 2),
        };
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        _btnAction = new Button
        {
            Text = "閉じる", Width = 112, Height = 38,
            Font = new Font("Meiryo UI", 10, FontStyle.Bold),
            BackColor = Color.DimGray, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Right, Margin = new Padding(0, 4, 0, 4),
        };
        _btnAction.Click += (_, _) =>
        {
            if (_done || !_running) { Close(); return; }
            _cancelled = true;
            _btnAction.Enabled = false;
            _btnAction.Text = "中止中...";
        };
        bottomRow.Controls.Add(new Label { Dock = DockStyle.Fill }, 0, 0); // スペーサ
        bottomRow.Controls.Add(_btnAction, 1, 0);

        // 行を組み立て
        root.Controls.Add(header,      0, 0);
        root.Controls.Add(_lblFolder,  0, 1);
        root.Controls.Add(monitorRow,  0, 2);
        root.Controls.Add(progressRow, 0, 3);
        root.Controls.Add(_txtResults, 0, 4);
        root.Controls.Add(bottomRow,   0, 5);

        Controls.Add(root);
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
