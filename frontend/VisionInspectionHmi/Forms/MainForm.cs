using VisionInspectionHmi.Models;
using VisionInspectionHmi.Services;

namespace VisionInspectionHmi.Forms;

public class MainForm : Form
{
    // --- サービス ---
    private InspectionApiClient   _apiClient   = new();
    private OnnxInspectionService _onnxService = new();

    // --- 左パネルコントロール ---
    private Button     btnSelectImage = null!;
    private Button     btnInspect     = null!;
    private Button     btnCheckApi    = null!;
    private Button     btnExportCsv   = null!;
    private Button     btnCameraStart = null!;
    private Button     btnCameraStop  = null!;
    private Button     btnSettings    = null!;
    private PictureBox picImage       = null!;
    private Label      lblImagePath   = null!;
    private Label      lblApiStatus   = null!;

    // 検査結果
    private Panel _resultBanner  = null!;
    private Label lblResult      = null!;
    private Label lblScore       = null!;
    private Label lblDefect      = null!;
    private Label lblInferenceMs = null!;

    // --- 右パネルコントロール ---
    private DataGridView dgvHistory  = null!;
    private Label lblStatTotal = null!;
    private Label lblStatOk    = null!;
    private Label lblStatNg    = null!;
    private Label lblStatRate  = null!;

    // Feature 1: モデル情報
    private Label lblModelMode   = null!;
    private Label lblModelStatus = null!;
    private Label lblModelName   = null!;
    private Label lblModelInput  = null!;

    // Top5推論候補表示
    private DataGridView _dgvTop5 = null!;

    // Feature 3: StatusStrip
    private StatusStrip          _statusStrip = null!;
    private ToolStripStatusLabel ssApi        = null!;
    private ToolStripStatusLabel ssCamera     = null!;
    private ToolStripStatusLabel ssModel      = null!;

    // --- 状態 ---
    private CameraService? _cameraService;
    private Bitmap?        _lastCameraFrame;
    private string?        _selectedImagePath;
    private readonly List<InspectionHistory> _histories = [];
    private int _totalCount;
    private int _okCount;
    private int _ngCount;

    public MainForm()
    {
        AppLogger.Start();  // Feature 4
        AppSettingsService.Load();
        InitializeComponent();
        ApplySettings(AppSettingsService.Current);
        Shown += MainForm_Shown;
    }

    private void MainForm_Shown(object? sender, EventArgs e)
    {
        var args = Environment.GetCommandLineArgs();
        int idx  = Array.IndexOf(args, "--test-image");
        if (idx >= 0 && idx < args.Length - 1)
        {
            var path = args[idx + 1];
            if (File.Exists(path))
            {
                _selectedImagePath = path;
                lblImagePath.Text  = Path.GetFileName(path);
                try { picImage.Image = Image.FromFile(path); } catch { }
            }
        }
    }

    private void InitializeComponent()
    {
        AutoScaleMode  = AutoScaleMode.None;
        Text           = "製造業向け 外観検査画像解析システム";
        Size           = new Size(1100, 830);
        MinimumSize    = new Size(1100, 820);
        StartPosition  = FormStartPosition.CenterScreen;
        BackColor      = Color.FromArgb(240, 240, 245);

        // ══════════════════════════════════════════════════════════
        //  左パネル（画像・ボタン・検査結果）
        // ══════════════════════════════════════════════════════════
        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 440 };

        picImage = new PictureBox
        {
            SizeMode    = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor   = Color.White,
            Left = 8, Top = 8, Width = 422, Height = 342,
        };

        lblImagePath = new Label
        {
            Text        = "画像が選択されていません",
            Left = 8, Top = 354, Width = 422, Height = 17,
            ForeColor   = Color.Gray,
            Font        = new Font("Meiryo UI", 7.5f),
            AutoEllipsis = true,
        };

        // ボタン行1: 画像選択 / 検査開始
        btnSelectImage = CreateButton("画像を選択", 8,   375, Color.SteelBlue);
        btnInspect     = CreateButton("検査開始",   222, 375, Color.SeaGreen);
        // ボタン行2: カメラ開始 / カメラ停止
        btnCameraStart = CreateButton("カメラ開始", 8,   415, Color.CadetBlue);
        btnCameraStop  = CreateButton("カメラ停止", 222, 415, Color.DimGray);
        btnCameraStop.Enabled = false;
        foreach (var b in new[] { btnSelectImage, btnInspect, btnCameraStart, btnCameraStop })
            b.Width = 206;

        // ボタン行3: API確認 / CSVフォルダ / 設定
        btnCheckApi  = CreateButton("API接続確認", 8,   455, Color.DimGray);
        btnExportCsv = CreateButton("CSVを開く",  152, 455, Color.DarkSlateGray);
        btnSettings  = CreateButton("設定",        296, 455, Color.SlateBlue);
        foreach (var b in new[] { btnCheckApi, btnExportCsv, btnSettings })
            b.Width = 140;

        lblApiStatus = new Label
        {
            Text      = "API: 未確認",
            Left = 8, Top = 495, Width = 422, Height = 22,
            Font      = new Font("Meiryo UI", 10, FontStyle.Bold),
            ForeColor = Color.Gray,
        };

        // ── 検査結果グループ ──────────────────────────────────────
        var resultGroup = new GroupBox
        {
            Text    = "検査結果",
            Left = 8, Top = 521, Width = 422, Height = 210,
            Font    = new Font("Meiryo UI", 9),
            Padding = new Padding(6, 24, 6, 6),
        };

        _resultBanner = new Panel
        {
            Left = 6, Top = 28, Width = 406, Height = 54,
            BackColor = Color.FromArgb(200, 200, 200),
        };
        lblResult = new Label
        {
            Text      = "判定: ---",
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font      = new Font("Meiryo UI", 20, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = false,
        };
        _resultBanner.Controls.Add(lblResult);

        lblScore  = CreateResultLabel("確信度: ---",    6,  86, 11, FontStyle.Regular);
        lblDefect = CreateResultLabel("推論クラス: ---", 6, 118, 11, FontStyle.Regular);
        foreach (var l in new[] { lblScore, lblDefect }) { l.Width = 406; l.Height = 28; }

        lblInferenceMs = CreateResultLabel("推論時間: ---", 6, 150, 10, FontStyle.Regular);
        lblInferenceMs.Width     = 406;
        lblInferenceMs.Height    = 26;
        lblInferenceMs.ForeColor = Color.FromArgb(80, 80, 100);

        resultGroup.Controls.AddRange(
            [_resultBanner, lblScore, lblDefect, lblInferenceMs]);

        leftPanel.Controls.AddRange([picImage, lblImagePath,
            btnSelectImage, btnInspect, btnCameraStart, btnCameraStop,
            btnCheckApi, btnExportCsv, btnSettings,
            lblApiStatus, resultGroup]);

        // ══════════════════════════════════════════════════════════
        //  右パネル（履歴・統計・モデル情報）
        // ══════════════════════════════════════════════════════════
        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        var histLabel = new Label
        {
            Text      = "検査履歴",
            Dock      = DockStyle.Top,
            Height    = 28,
            Font      = new Font("Meiryo UI", 10, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        dgvHistory = new DataGridView
        {
            Dock                    = DockStyle.Fill,
            ReadOnly                = true,
            AutoSizeColumnsMode     = DataGridViewAutoSizeColumnsMode.Fill,
            ScrollBars              = ScrollBars.Vertical,
            RowHeadersVisible       = false,
            AllowUserToAddRows      = false,
            BackgroundColor         = Color.White,
            BorderStyle             = BorderStyle.None,
            Font                    = new Font("Meiryo UI", 8),
        };
        var hColAt     = new DataGridViewTextBoxColumn { Name = "InspectedAt", HeaderText = "検査日時"     };
        var hColResult = new DataGridViewTextBoxColumn { Name = "Result",      HeaderText = "判定"         };
        var hColScore  = new DataGridViewTextBoxColumn { Name = "Score",       HeaderText = "確信度"       };
        var hColDefect = new DataGridViewTextBoxColumn { Name = "DefectType",  HeaderText = "推論クラス"   };
        var hColMs     = new DataGridViewTextBoxColumn { Name = "InferenceMs", HeaderText = "推論時間(ms)" };
        dgvHistory.Columns.AddRange([hColAt, hColResult, hColScore, hColDefect, hColMs]);
        hColAt.AutoSizeMode     = DataGridViewAutoSizeColumnMode.None; hColAt.Width     = 150;
        hColResult.AutoSizeMode = DataGridViewAutoSizeColumnMode.None; hColResult.Width = 60;
        hColScore.AutoSizeMode  = DataGridViewAutoSizeColumnMode.None; hColScore.Width  = 80;
        hColDefect.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        hColMs.AutoSizeMode     = DataGridViewAutoSizeColumnMode.None; hColMs.Width     = 120;
        dgvHistory.RowTemplate.Height = 24;
        dgvHistory.EnableHeadersVisualStyles               = false;
        dgvHistory.ColumnHeadersHeightSizeMode             = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgvHistory.ColumnHeadersHeight                     = 30;
        dgvHistory.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        dgvHistory.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(225, 230, 245);
        dgvHistory.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(50, 50, 80);

        // 統計パネル
        var statsPanel = new FlowLayoutPanel
        {
            Dock          = DockStyle.Top,
            Height        = 40,
            BackColor     = Color.FromArgb(225, 225, 238),
            FlowDirection = FlowDirection.LeftToRight,
            Padding       = new Padding(8, 0, 8, 0),
            WrapContents  = false,
        };
        var statFont   = new Font("Meiryo UI", 10, FontStyle.Bold);
        var statMargin = new Padding(0, 10, 24, 0);
        lblStatTotal = new Label { Text = "検査数: 0", AutoSize = true, Font = statFont, ForeColor = Color.FromArgb(50, 50, 70), Margin = statMargin };
        lblStatOk    = new Label { Text = "OK: 0",     AutoSize = true, Font = statFont, ForeColor = Color.SeaGreen,             Margin = statMargin };
        lblStatNg    = new Label { Text = "NG: 0",     AutoSize = true, Font = statFont, ForeColor = Color.Crimson,              Margin = statMargin };
        lblStatRate  = new Label { Text = "OK率: ---", AutoSize = true, Font = statFont, ForeColor = Color.FromArgb(50, 50, 70), Margin = statMargin };
        statsPanel.Controls.AddRange([lblStatTotal, lblStatOk, lblStatNg, lblStatRate]);

        // Feature 1: モデル情報パネル ──────────────────────────────
        var modelInfoPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 100,
            BackColor = Color.FromArgb(232, 238, 250),
        };
        var infoTitleLbl = new Label
        {
            Text      = "推論エンジン情報",
            Left = 8, Top = 2, Width = 200, Height = 16,
            Font      = new Font("Meiryo UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 80, 130),
        };
        var infoFont = new Font("Meiryo UI", 8.5f);
        lblModelMode   = new Label { Text = "推論モード: ---",   Left = 8,   Top = 20, Width = 240, Height = 22, Font = infoFont };
        lblModelStatus = new Label { Text = "読込状態: ---",     Left = 258, Top = 20, Width = 160, Height = 22, Font = infoFont };
        lblModelName   = new Label { Text = "モデル名: ---",     Left = 8,   Top = 44, Width = 400, Height = 22, Font = infoFont, AutoEllipsis = true };
        lblModelInput  = new Label { Text = "入力サイズ: ---",   Left = 8,   Top = 68, Width = 240, Height = 22, Font = infoFont };
        modelInfoPanel.Controls.AddRange(
            [infoTitleLbl, lblModelMode, lblModelStatus, lblModelName, lblModelInput]);

        // ── Top5推論候補パネル ──────────────────────────────────────
        var top5Panel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 168,
            BackColor = Color.White,
        };
        var top5TitleLbl = new Label
        {
            Text      = "Top5 推論候補",
            Dock      = DockStyle.Top,
            Height    = 24,
            Font      = new Font("Meiryo UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 80, 130),
            Padding   = new Padding(4, 4, 0, 0),
            BackColor = Color.FromArgb(232, 238, 250),
        };
        _dgvTop5 = new DataGridView
        {
            Dock                = DockStyle.Fill,
            ReadOnly            = true,
            AllowUserToAddRows  = false,
            RowHeadersVisible   = false,
            ColumnHeadersVisible = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ScrollBars          = ScrollBars.Vertical,
            BackgroundColor     = Color.White,
            BorderStyle         = BorderStyle.None,
            Font                = new Font("Meiryo UI", 8f),
            SelectionMode       = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect         = false,
            CellBorderStyle     = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor           = Color.FromArgb(220, 220, 230),
        };
        var t5ColRank  = new DataGridViewTextBoxColumn { HeaderText = "順位",    DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } };
        var t5ColLabel = new DataGridViewTextBoxColumn { HeaderText = "クラス名" };
        var t5ColConf  = new DataGridViewTextBoxColumn { HeaderText = "確信度",  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight  } };
        _dgvTop5.Columns.AddRange([t5ColRank, t5ColLabel, t5ColConf]);
        t5ColRank.AutoSizeMode  = DataGridViewAutoSizeColumnMode.None; t5ColRank.Width  = 50;
        t5ColLabel.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        t5ColConf.AutoSizeMode  = DataGridViewAutoSizeColumnMode.None; t5ColConf.Width  = 110;
        _dgvTop5.RowTemplate.Height = 21;
        _dgvTop5.EnableHeadersVisualStyles               = false;
        _dgvTop5.ColumnHeadersHeightSizeMode             = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _dgvTop5.ColumnHeadersHeight                     = 30;
        _dgvTop5.ColumnHeadersDefaultCellStyle.Font      = new Font("Meiryo UI", 7.5f, FontStyle.Bold);
        _dgvTop5.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        _dgvTop5.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(225, 230, 245);
        _dgvTop5.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(50, 50, 80);

        top5Panel.Controls.Add(_dgvTop5);
        top5Panel.Controls.Add(top5TitleLbl);

        // Controls.Add 順序: Fill → 後から追加したTop系が上になる (最後追加=最上部)
        rightPanel.Controls.Add(dgvHistory);
        rightPanel.Controls.Add(top5Panel);        // Top5 (modelInfoPanelの下)
        rightPanel.Controls.Add(modelInfoPanel);   // Feature 1
        rightPanel.Controls.Add(statsPanel);
        rightPanel.Controls.Add(histLabel);        // 最上部

        // ══════════════════════════════════════════════════════════
        //  Feature 3: StatusStrip（フォーム下部）
        // ══════════════════════════════════════════════════════════
        _statusStrip = new StatusStrip { SizingGrip = false };
        ssApi = new ToolStripStatusLabel
        {
            Text        = "API: ---",
            BorderSides = ToolStripStatusLabelBorderSides.Right,
            BorderStyle = Border3DStyle.Etched,
        };
        ssCamera = new ToolStripStatusLabel
        {
            Text        = "CAMERA: 停止",
            BorderSides = ToolStripStatusLabelBorderSides.Right,
            BorderStyle = Border3DStyle.Etched,
        };
        ssModel = new ToolStripStatusLabel { Text = "MODEL: ---" };
        _statusStrip.Items.AddRange([ssApi, ssCamera, ssModel]);

        Controls.Add(rightPanel);
        Controls.Add(leftPanel);
        Controls.Add(_statusStrip);  // Feature 3

        // --- イベント登録 ---
        btnSelectImage.Click += BtnSelectImage_Click;
        btnInspect.Click     += BtnInspect_Click;
        btnCameraStart.Click += BtnCameraStart_Click;
        btnCameraStop.Click  += BtnCameraStop_Click;
        btnCheckApi.Click    += BtnCheckApi_Click;
        btnExportCsv.Click   += BtnExportCsv_Click;
        btnSettings.Click    += BtnSettings_Click;
    }

    // ══════════════════════════════════════════════════════════════
    //  ボタンイベント
    // ══════════════════════════════════════════════════════════════

    private void BtnSelectImage_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "検査画像を選択してください",
            Filter = "画像ファイル|*.jpg;*.jpeg;*.png;*.bmp|すべてのファイル|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _selectedImagePath = dlg.FileName;
        lblImagePath.Text  = Path.GetFileName(_selectedImagePath);
        try
        {
            picImage.Image?.Dispose();
            picImage.Image = Image.FromFile(_selectedImagePath);
        }
        catch (Exception ex) { ShowError($"画像の表示に失敗しました: {ex.Message}"); }
    }

    private async void BtnInspect_Click(object? sender, EventArgs e)
    {
        // カメラモード: 現在フレームをテンポラリ JPEG に保存
        if (_cameraService?.IsRunning == true)
        {
            if (_lastCameraFrame == null)
            {
                ShowError("カメラ映像を取得できません。カメラが起動しているか確認してください。");
                return;
            }
            try
            {
                var tempDir  = Path.Combine(Path.GetTempPath(), "VisionInspectionHmi");
                Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, $"camera_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                _lastCameraFrame.Save(tempPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                _selectedImagePath = tempPath;
                lblImagePath.Text  = Path.GetFileName(tempPath);
            }
            catch (Exception ex)
            {
                ShowError($"カメラフレームの保存に失敗しました: {ex.Message}");
                return;
            }
        }

        if (string.IsNullOrEmpty(_selectedImagePath))
        {
            ShowError("画像が選択されていません。先に画像を選択するか、カメラを起動してください。");
            return;
        }
        if (!File.Exists(_selectedImagePath))
        {
            ShowError($"画像ファイルが存在しません: {_selectedImagePath}");
            return;
        }

        var cfg = AppSettingsService.Current;
        if (!cfg.InferenceEnabled)
        {
            ShowError("推論が無効に設定されています。\n設定画面から「推論APIを実行する」を有効にしてください。");
            return;
        }

        btnInspect.Enabled = false;
        btnInspect.Text    = "検査中...";
        ClearResult();

        string apiStatus  = "成功";
        InspectionResult? result = null;
        double inferenceMs = 0;
        bool   useOnnx     = cfg.InferenceMode == "ONNX";
        string fileName    = Path.GetFileName(_selectedImagePath);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (useOnnx)
            {
                result     = await _onnxService.InspectAsync(_selectedImagePath, cfg.NgThreshold);
                inferenceMs = result.InferenceMs;
            }
            else
            {
                result = await _apiClient.InspectAsync(_selectedImagePath);
                sw.Stop();
                result.Result = result.Score >= cfg.NgThreshold ? "NG" : "OK";
                if (result.Result == "OK") result.DefectType = "none";
                inferenceMs = result.InferenceMs > 0 ? result.InferenceMs : sw.ElapsedMilliseconds;
            }
            ShowResult(result, inferenceMs);
            // Feature 4: 検査ログ
            AppLogger.LogInspection(fileName, result.Result, result.Score,
                                    result.DefectType, inferenceMs,
                                    useOnnx ? "ONNX" : "FastAPI");
        }
        catch (Exception ex)
        {
            sw.Stop();
            apiStatus = $"エラー: {ex.Message}";
            ShowError(ex.Message);
            AppLogger.LogInferenceFailed(fileName, ex);  // Feature 4
        }
        finally
        {
            btnInspect.Enabled = true;
            btnInspect.Text    = "検査開始";
        }

        var history = new InspectionHistory
        {
            InspectedAt   = DateTime.Now,
            ImageFileName = Path.GetFileName(_selectedImagePath),
            ImagePath     = _selectedImagePath,
            Result        = result?.Result     ?? "エラー",
            Score         = result?.Score      ?? 0,
            DefectType    = !string.IsNullOrEmpty(result?.ClassName)
                            ? result.ClassName
                            : (result?.DefectType ?? string.Empty),
            Message       = result?.Message    ?? apiStatus,
            ApiStatus     = apiStatus,
            InferenceMs   = inferenceMs,
        };
        AddHistory(history);

        _totalCount++;
        if      (history.Result == "OK") _okCount++;
        else if (history.Result == "NG") _ngCount++;
        UpdateStats();

        // NG 画像保存
        if (history.Result == "NG" && !string.IsNullOrEmpty(_selectedImagePath))
        {
            try
            {
                var saved = NgImageSaverService.Save(_selectedImagePath, history.InspectedAt);
                if (saved == null) ShowError("NG画像の保存に失敗しました。");
            }
            catch (Exception ex) { ShowError($"NG画像保存エラー: {ex.Message}"); }
        }

        try { CsvLogService.Save(history); }
        catch (Exception ex) { ShowError($"CSV保存に失敗しました: {ex.Message}"); }
    }

    private async void BtnCheckApi_Click(object? sender, EventArgs e)
    {
        btnCheckApi.Enabled    = false;
        lblApiStatus.Text      = "API: 確認中...";
        lblApiStatus.ForeColor = Color.DarkOrange;
        ssApi.Text             = "API: 確認中";   // Feature 3

        bool ok = await _apiClient.CheckHealthAsync();

        lblApiStatus.Text      = ok ? "API: 接続中 ✓" : "API: 未接続 ✗";
        lblApiStatus.ForeColor = ok ? Color.SeaGreen : Color.Crimson;
        btnCheckApi.Enabled    = true;
        // Feature 3
        ssApi.Text      = ok ? "API: 接続中 ✓" : "API: 未接続 ✗";
        ssApi.ForeColor = ok ? Color.SeaGreen : Color.Crimson;
    }

    private void BtnExportCsv_Click(object? sender, EventArgs e)
    {
        var logsDir = CsvLogService.LogsDir ?? Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(logsDir);
        try { System.Diagnostics.Process.Start("explorer.exe", logsDir); }
        catch (Exception ex) { ShowError($"フォルダを開けませんでした: {ex.Message}"); }
    }

    private void BtnSettings_Click(object? sender, EventArgs e)
    {
        using var form = new SettingsForm(AppSettingsService.Current);
        if (form.ShowDialog(this) == DialogResult.OK && form.ResultSettings != null)
        {
            AppSettingsService.Save(form.ResultSettings);
            ApplySettings(form.ResultSettings);
            AppLogger.LogSettingsChanged();  // Feature 4
            MessageBox.Show("設定を保存しました。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  設定適用
    // ══════════════════════════════════════════════════════════════

    private void ApplySettings(AppSettings s)
    {
        _apiClient.Dispose();
        _apiClient = new InspectionApiClient(s.ApiUrl, s.ApiTimeoutSeconds);

        // Feature 4: ログ保存先
        AppLogger.LogsDir = string.IsNullOrWhiteSpace(s.CsvDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Logs")
            : Path.Combine(s.CsvDirectory, "Logs");

        // ONNX モードならモデルを事前ロード
        if (s.InferenceMode == "ONNX" && !string.IsNullOrWhiteSpace(s.OnnxModelPath))
        {
            try
            {
                _onnxService.LoadModel(s.OnnxModelPath);
                AppLogger.LogModelLoaded(s.OnnxModelPath);  // Feature 4
            }
            catch (Exception ex)
            {
                AppLogger.LogModelLoadFailed(s.OnnxModelPath, ex);  // Feature 4
                ShowError($"ONNXモデルの読み込みに失敗しました:\n{ex.Message}");
            }
        }

        // API ステータスとStatusStrip更新
        if (lblApiStatus != null)
        {
            if (s.InferenceMode == "ONNX")
            {
                lblApiStatus.Text      = "ONNXモード (API不要)";
                lblApiStatus.ForeColor = Color.DarkCyan;
                ssApi.Text      = "API: (ONNX不使用)";  // Feature 3
                ssApi.ForeColor = Color.DimGray;
            }
            else
            {
                lblApiStatus.Text      = "API: 未確認";
                lblApiStatus.ForeColor = Color.Gray;
                ssApi.Text      = "API: 未確認";  // Feature 3
                ssApi.ForeColor = Color.Gray;
            }
        }

        // ONNXモード時はAPI接続確認を非表示・無効化し、残ボタンを再配置
        if (btnCheckApi != null)
        {
            bool isOnnx = s.InferenceMode == "ONNX";
            btnCheckApi.Visible = !isOnnx;
            btnCheckApi.Enabled = !isOnnx;
            if (isOnnx)
            {
                btnExportCsv.Left  = 8;
                btnExportCsv.Width = 210;
                btnSettings.Left   = 222;
                btnSettings.Width  = 210;
            }
            else
            {
                btnExportCsv.Left  = 152;
                btnExportCsv.Width = 140;
                btnSettings.Left   = 296;
                btnSettings.Width  = 140;
            }
        }

        // Feature 1: モデル情報パネル更新
        if (lblModelMode != null)
            UpdateModelInfo(s);

        CsvLogService.LogsDir           = s.CsvDirectory;
        NgImageSaverService.NgDirectory = s.NgImageDirectory;
    }

    // Feature 1: モデル情報パネル更新 ────────────────────────────
    private void UpdateModelInfo(AppSettings s)
    {
        if (s.InferenceMode == "ONNX")
        {
            lblModelMode.Text   = $"推論モード: ONNX [{_onnxService.ModelModeText}]";
            lblModelName.Text   = $"モデル名: {_onnxService.LoadedModelName}";
            lblModelInput.Text  = $"入力サイズ: {_onnxService.InputShapeText}";
            bool loaded = _onnxService.IsLoaded;
            lblModelStatus.Text      = loaded ? "読込状態: 正常 ✓" : "読込状態: 未読込 ✗";
            lblModelStatus.ForeColor = loaded ? Color.SeaGreen : Color.Crimson;
            ssModel.Text      = loaded ? "MODEL: 正常 ✓" : "MODEL: 未読込 ✗";  // Feature 3
            ssModel.ForeColor = loaded ? Color.SeaGreen : Color.Crimson;
        }
        else
        {
            lblModelMode.Text        = "推論モード: FastAPI";
            lblModelName.Text        = "モデル名: (サーバー側)";
            lblModelInput.Text       = "入力サイズ: (サーバー側)";
            lblModelStatus.Text      = "読込状態: (サーバー側)";
            lblModelStatus.ForeColor = Color.Gray;
            ssModel.Text      = "MODEL: FastAPI";  // Feature 3
            ssModel.ForeColor = Color.DimGray;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  カメラ操作
    // ══════════════════════════════════════════════════════════════

    private void BtnCameraStart_Click(object? sender, EventArgs e)
    {
        int cameraIndex = AppSettingsService.Current.CameraIndex;
        try
        {
            _cameraService = new CameraService();
            _cameraService.FrameReady += OnCameraFrameReady;
            _cameraService.Start(cameraIndex);

            btnCameraStart.Enabled = false;
            btnCameraStop.Enabled  = true;
            btnSelectImage.Enabled = false;
            lblImagePath.Text      = "カメラ映像";
            _selectedImagePath     = null;

            AppLogger.LogCameraStarted(cameraIndex);               // Feature 4
            ssCamera.Text      = $"CAMERA: 起動中 (idx={cameraIndex})";  // Feature 3
            ssCamera.ForeColor = Color.SeaGreen;
        }
        catch (Exception ex)
        {
            _cameraService?.Dispose();
            _cameraService = null;
            AppLogger.LogCameraError(cameraIndex, ex);  // Feature 4
            ShowError($"カメラの起動に失敗しました:\n{ex.Message}");
        }
    }

    private void BtnCameraStop_Click(object? sender, EventArgs e) => StopCamera();

    private void StopCamera()
    {
        if (_cameraService == null) return;

        _cameraService.FrameReady -= OnCameraFrameReady;
        _cameraService.Stop();
        _cameraService.Dispose();
        _cameraService = null;

        if (_lastCameraFrame != null)
        {
            if (picImage.Image == _lastCameraFrame)
                picImage.Image = null;
            _lastCameraFrame.Dispose();
            _lastCameraFrame = null;
        }

        if (!IsDisposed)
        {
            btnCameraStart.Enabled = true;
            btnCameraStop.Enabled  = false;
            btnSelectImage.Enabled = true;
            lblImagePath.Text      = "画像が選択されていません";
            _selectedImagePath     = null;

            AppLogger.LogCameraStopped();       // Feature 4
            ssCamera.Text      = "CAMERA: 停止";  // Feature 3
            ssCamera.ForeColor = Color.Gray;
        }
    }

    private void OnCameraFrameReady(object? sender, Bitmap bmp)
    {
        if (IsDisposed || !IsHandleCreated) { bmp.Dispose(); return; }
        try
        {
            Invoke(() =>
            {
                var prev         = _lastCameraFrame;
                _lastCameraFrame = bmp;
                picImage.Image   = bmp;
                prev?.Dispose();
            });
        }
        catch { bmp.Dispose(); }
    }

    // ══════════════════════════════════════════════════════════════
    //  表示ヘルパー
    // ══════════════════════════════════════════════════════════════

    private void ShowResult(InspectionResult r, double inferenceMs = 0)
    {
        bool isOk = r.Result == "OK";
        _resultBanner.BackColor = isOk ? Color.SeaGreen : Color.Crimson;
        lblResult.Text          = $"判定: {r.Result}";
        lblResult.ForeColor     = Color.White;
        lblScore.Text       = $"確信度: {r.Score * 100:F1}%";
        string inferredClass = !string.IsNullOrEmpty(r.ClassName) ? r.ClassName : r.DefectType;
        lblDefect.Text      = $"推論クラス: {inferredClass}";
        lblInferenceMs.Text = inferenceMs > 0 ? $"推論時間: {inferenceMs:F1}ms" : "推論時間: ---";

        // Top5候補表示
        UpdateTop5Grid(r.Top5Candidates);
    }

    private void ClearResult()
    {
        _resultBanner.BackColor = Color.DarkOrange;
        lblResult.Text          = "判定: 検査中...";
        lblResult.ForeColor     = Color.White;
        lblScore.Text       = "確信度: ---";
        lblDefect.Text      = "推論クラス: ---";
        lblInferenceMs.Text = "推論時間: ---";
        _dgvTop5.Rows.Clear();
    }

    private void UpdateTop5Grid(List<Top5Entry> candidates)
    {
        _dgvTop5.Rows.Clear();
        foreach (var e in candidates)
        {
            int row = _dgvTop5.Rows.Add(e.Rank, e.Label, $"{e.Score * 100:F2}%");
            // Top-1 は背景を薄い緑でハイライト
            if (e.Rank == 1)
                _dgvTop5.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(235, 255, 235);
        }
    }

    private void AddHistory(InspectionHistory h)
    {
        _histories.Insert(0, h);
        int rowIdx = dgvHistory.Rows.Add(
            h.InspectedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            h.Result,
            $"{h.Score * 100:F1}%",
            h.DefectType,
            h.InferenceMs > 0 ? $"{h.InferenceMs:F1}" : "-"
        );
        var row   = dgvHistory.Rows[rowIdx];
        bool isOk = h.Result == "OK";
        row.DefaultCellStyle.ForeColor = isOk ? Color.SeaGreen : Color.Crimson;
        row.DefaultCellStyle.BackColor = isOk ? Color.White : Color.FromArgb(255, 240, 240);
        if (!isOk)
            row.DefaultCellStyle.Font = new Font("Meiryo UI", 8, FontStyle.Bold);
    }

    private void UpdateStats()
    {
        double rate = _totalCount > 0 ? (double)_okCount / _totalCount * 100.0 : 0;
        lblStatTotal.Text = $"検査数: {_totalCount}";
        lblStatOk.Text    = $"OK: {_okCount}";
        lblStatNg.Text    = $"NG: {_ngCount}";
        lblStatRate.Text  = _totalCount > 0 ? $"OK率: {rate:F1}%" : "OK率: ---";
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    // ══════════════════════════════════════════════════════════════
    //  ファクトリ
    // ══════════════════════════════════════════════════════════════

    private static Button CreateButton(string text, int x, int y, Color color) =>
        new()
        {
            Text      = text, Left = x, Top = y, Width = 186, Height = 36,
            BackColor = color, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Meiryo UI", 9, FontStyle.Bold),
            Cursor    = Cursors.Hand,
        };

    private static Label CreateResultLabel(string text, int x, int y, int size, FontStyle style) =>
        new()
        {
            Text = text, Left = x, Top = y, Width = 360, Height = 24,
            Font = new Font("Meiryo UI", size, style), AutoEllipsis = true,
        };

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopCamera();
        base.OnFormClosed(e);
        _apiClient.Dispose();
        _onnxService.Dispose();
        picImage.Image?.Dispose();
        AppLogger.Stop();  // Feature 4
    }
}
