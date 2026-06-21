using VisionInspectionHmi.Models;
using VisionInspectionHmi.Services;

namespace VisionInspectionHmi.Forms;

public class MainForm : Form
{
    // --- サービス ---
    private InspectionApiClient _apiClient = new();
    // ローカル推論エンジン。ONNX モード時に InspectionEngineFactory で生成（分類 / 異常検知）。
    // FastAPI モードや未ロード時は null。
    private IInspectionEngine? _engine;

    // モデル名ラベルにフルパスを表示する ToolTip
    private readonly ToolTip _modelPathTip = new()
    {
        AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 100,
    };

    // --- 左パネルコントロール ---
    private Button     btnSelectImage = null!;
    private Button     btnInspect     = null!;
    private Button     btnCheckApi    = null!;
    private Button     btnExportCsv   = null!;
    private Button     btnFolderInspect = null!;
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

    // モデル情報パネル
    private Label lblModelMode   = null!;
    private Label lblModelStatus = null!;
    private Label lblModelName   = null!;
    private Label lblModelInput  = null!;

    // Top5推論候補
    private DataGridView _dgvTop5 = null!;

    // ステータスバー
    private StatusStrip          _statusStrip = null!;
    private ToolStripStatusLabel ssApi        = null!;
    private ToolStripStatusLabel ssCamera     = null!;
    private ToolStripStatusLabel ssModel      = null!;

    // --- 状態 ---
    private CameraService? _cameraService;
    private Bitmap?        _lastCameraFrame;
    private string?        _selectedImagePath;

    // 異常検知ヒートマップ表示用（最後の検査結果と対象画像を保持し、トグルで再描画）
    private CheckBox          chkHeatmap = null!;
    private InspectionResult? _lastAnomalyResult;
    private string?           _lastInspectedImagePath;
    private readonly List<InspectionHistory> _histories = [];
    private int _totalCount;
    private int _okCount;
    private int _ngCount;

    // --- PLC 連携 ---
    private IPlcCommunicationService? _plcService;
    private PlcInspectionBridge?      _plcBridge;
    private ICameraCaptureService?    _cameraCaptureService;
    private Button btnPlcConnect     = null!;
    private Button btnPlcDisconnect  = null!;
    private Button btnPlcMonitor     = null!;
    private Button btnPlcTestFire    = null!;
    private Label  lblPlcStatus      = null!;
    private Label  lblPlcLastTrigger = null!;
    private ToolStripStatusLabel ssPlc = null!;

    public MainForm()
    {
        AppLogger.Start();
        AppSettingsService.Load();
        InitializeComponent();
        ApplySettings(AppSettingsService.Current);
        // PLC ボタン初期状態（未接続時）
        btnPlcDisconnect.Enabled = false;
        btnPlcMonitor.Enabled    = false;
        btnPlcTestFire.Enabled   = false;
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
        Size           = new Size(1180, 865);
        MinimumSize    = new Size(1180, 855);
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
            Left = 8, Top = 495, Width = 200, Height = 22,
            Font      = new Font("Meiryo UI", 10, FontStyle.Bold),
            ForeColor = Color.Gray,
            AutoEllipsis = true,
        };

        // フォルダ検査（バッチ評価）ボタン — API ステータス行の右側に配置
        btnFolderInspect = CreateButton("フォルダ検査", 214, 491, Color.Teal);
        btnFolderInspect.Width  = 216;
        btnFolderInspect.Height = 28;

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

        // ── PLC連携パネル ────────────────────────────────────────
        var plcGroup = new GroupBox
        {
            Text    = "PLC連携（Modbus TCP）",
            Left = 8, Top = 737, Width = 422, Height = 80,
            Font    = new Font("Meiryo UI", 9),
            Padding = new Padding(6, 20, 6, 6),
        };

        btnPlcConnect    = CreateButton("PLC接続",   8,   24, Color.SteelBlue);
        btnPlcDisconnect = CreateButton("PLC切断",   110, 24, Color.DimGray);
        btnPlcMonitor    = CreateButton("▶ 監視開始", 212, 24, Color.SeaGreen);
        btnPlcTestFire   = CreateButton("テスト発火", 336, 24, Color.DarkOrange);
        foreach (var b in new[] { btnPlcConnect, btnPlcDisconnect, btnPlcMonitor, btnPlcTestFire })
        {
            b.Width  = 98;
            b.Height = 28;
            b.Font   = new Font("Meiryo UI", 8, FontStyle.Bold);
        }

        lblPlcStatus = new Label
        {
            Text      = "状態: 未接続",
            Left = 8, Top = 56, Width = 200, Height = 18,
            Font      = new Font("Meiryo UI", 8),
            ForeColor = Color.Gray,
        };
        lblPlcLastTrigger = new Label
        {
            Text      = "最終トリガ: --",
            Left = 214, Top = 56, Width = 200, Height = 18,
            Font      = new Font("Meiryo UI", 8),
            ForeColor = Color.DimGray,
        };

        plcGroup.Controls.AddRange([
            btnPlcConnect, btnPlcDisconnect, btnPlcMonitor, btnPlcTestFire,
            lblPlcStatus, lblPlcLastTrigger,
        ]);

        leftPanel.Controls.AddRange([picImage, lblImagePath,
            btnSelectImage, btnInspect, btnCameraStart, btnCameraStop,
            btnCheckApi, btnExportCsv, btnSettings,
            lblApiStatus, btnFolderInspect, resultGroup, plcGroup]);

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
        hColDefect.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; hColDefect.FillWeight = 100; hColDefect.MinimumWidth = 160;
        hColMs.AutoSizeMode     = DataGridViewAutoSizeColumnMode.None; hColMs.Width     = 140;
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

        // モデル情報パネル ────────────────────────────────────────
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
        lblModelMode   = new Label { Text = "推論モード: ---",   Left = 8,   Top = 20, Width = 400, Height = 22, Font = infoFont, AutoEllipsis = true };
        lblModelStatus = new Label { Text = "読込状態: ---",     Left = 416, Top = 20, Width = 220, Height = 22, Font = infoFont };
        lblModelName   = new Label { Text = "モデル名: ---",     Left = 8,   Top = 44, Width = 628, Height = 22, Font = infoFont, AutoEllipsis = true };
        lblModelInput  = new Label { Text = "入力サイズ: ---",   Left = 8,   Top = 68, Width = 360, Height = 22, Font = infoFont };
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
        // Controls.Add 順序: Fill → 後から追加したTop系が上になる（最後追加=最上部）
        rightPanel.Controls.Add(dgvHistory);
        rightPanel.Controls.Add(top5Panel);
        rightPanel.Controls.Add(modelInfoPanel);
        rightPanel.Controls.Add(statsPanel);
        rightPanel.Controls.Add(histLabel);

        // ステータスバー（フォーム下部）。Spring=true で横幅に応じ4項目を均等配分
        _statusStrip = new StatusStrip { SizingGrip = false };
        ssApi = new ToolStripStatusLabel
        {
            Text        = "API: ---",
            Spring      = true,
            TextAlign   = ContentAlignment.MiddleLeft,
            BorderSides = ToolStripStatusLabelBorderSides.Right,
            BorderStyle = Border3DStyle.Etched,
        };
        ssCamera = new ToolStripStatusLabel
        {
            Text        = "CAMERA: 停止",
            Spring      = true,
            TextAlign   = ContentAlignment.MiddleLeft,
            BorderSides = ToolStripStatusLabelBorderSides.Right,
            BorderStyle = Border3DStyle.Etched,
        };
        ssModel = new ToolStripStatusLabel
        {
            Text        = "MODEL: ---",
            Spring      = true,
            TextAlign   = ContentAlignment.MiddleLeft,
            BorderSides = ToolStripStatusLabelBorderSides.Right,
            BorderStyle = Border3DStyle.Etched,
        };
        ssPlc = new ToolStripStatusLabel
        {
            Text        = "PLC: 未接続",
            Spring      = true,
            TextAlign   = ContentAlignment.MiddleLeft,
        };
        _statusStrip.Items.AddRange([ssApi, ssCamera, ssModel, ssPlc]);

        Controls.Add(rightPanel);
        Controls.Add(leftPanel);
        Controls.Add(_statusStrip);

        // ヒートマップ表示トグル（異常検知結果がある場合のみ表示。picImage 左上に重ねる）
        chkHeatmap = new CheckBox
        {
            Text      = "ヒートマップ",
            Left = 6, Top = 6, Width = 110, Height = 22,
            Font      = new Font("Meiryo UI", 8, FontStyle.Bold),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(40, 40, 80),
            Checked   = true,
            Visible   = false,
        };
        chkHeatmap.CheckedChanged += (_, _) => RenderHeatmapOrOriginal();
        picImage.Controls.Add(chkHeatmap);

        // --- イベント登録 ---
        btnSelectImage.Click  += BtnSelectImage_Click;
        btnInspect.Click      += BtnInspect_Click;
        btnCameraStart.Click  += BtnCameraStart_Click;
        btnCameraStop.Click   += BtnCameraStop_Click;
        btnCheckApi.Click     += BtnCheckApi_Click;
        btnExportCsv.Click    += BtnExportCsv_Click;
        btnFolderInspect.Click += BtnFolderInspect_Click;
        btnSettings.Click     += BtnSettings_Click;
        btnPlcConnect.Click   += BtnPlcConnect_Click;
        btnPlcDisconnect.Click += BtnPlcDisconnect_Click;
        btnPlcMonitor.Click   += BtnPlcMonitor_Click;
        btnPlcTestFire.Click  += BtnPlcTestFire_Click;
    }

    // ══════════════════════════════════════════════════════════════
    //  ボタンイベント
    // ══════════════════════════════════════════════════════════════

    private void BtnSelectImage_Click(object? sender, EventArgs e)
    {
        // ダイアログ表示前に重い処理は行わない。
        // 前回パスのフォルダが有効なときだけ InitialDirectory に使う（壊れていても固まらない）。
        string? initialDir = null;
        try
        {
            var prevDir = Path.GetDirectoryName(_selectedImagePath);
            if (!string.IsNullOrEmpty(prevDir) && Directory.Exists(prevDir))
                initialDir = prevDir;
        }
        catch { initialDir = null; }

        string selectedPath;
        using (var dlg = new OpenFileDialog
        {
            Title            = "検査画像を選択してください",
            Filter           = "Image Files|*.jpg;*.jpeg;*.png;*.bmp|All Files|*.*",
            Multiselect      = false,
            RestoreDirectory = true,
        })
        {
            if (initialDir != null) dlg.InitialDirectory = initialDir;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            selectedPath = dlg.FileName;
        }

        // ダイアログで選択されたファイルだけを、ファイルロックを残さず読み込む。
        try
        {
            var image = LoadImageCopy(selectedPath);
            picImage.Image?.Dispose();
            picImage.Image     = image;
            _selectedImagePath = selectedPath;
            lblImagePath.Text  = Path.GetFileName(selectedPath);
            // 新規画像選択でヒートマップ表示をリセット
            _lastAnomalyResult = null;
            chkHeatmap.Visible = false;
        }
        catch (Exception ex)
        {
            AppLogger.Error("画像の読み込みに失敗しました", ex);
            ShowError($"画像の表示に失敗しました: {ex.Message}");
        }
    }

    // フォルダ内の画像を連続検査し、MVTec AD 基準で定量評価する（バッチ評価）。
    private void BtnFolderInspect_Click(object? sender, EventArgs e)
    {
        var cfg = AppSettingsService.Current;
        if (cfg.InferenceMode != "ONNX" || _engine is null || !_engine.IsLoaded)
        {
            ShowError("フォルダ検査には ONNX モードのモデル読込が必要です。\n" +
                      "設定画面で「ONNXモード」を選択し、モデルファイルを指定してください。");
            return;
        }

        using var fbd = new FolderBrowserDialog
        {
            Description            = "検査するフォルダを選択してください（サブフォルダも再帰検索）",
            UseDescriptionForTitle = true,
        };
        if (fbd.ShowDialog(this) != DialogResult.OK) return;

        double threshold = ResolveThreshold(cfg);
        string csvDir = string.IsNullOrWhiteSpace(cfg.CsvDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Logs")
            : Path.Combine(cfg.CsvDirectory, "Logs");

        using var dlg = new BatchInspectionForm(
            _engine, threshold, fbd.SelectedPath, csvDir,
            onMonitorImage:  MonitorShowImage,
            onMonitorResult: MonitorShowResult);
        dlg.ShowDialog(this);
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
                if (_engine is null)
                    throw new InvalidOperationException(
                        "ONNXモデルが読み込まれていません。設定画面でモデルファイルを指定してください。");

                // 閾値はエンジン種別で切替（分類=NgThreshold / 異常検知=AnomalyThreshold）
                result      = await _engine.InspectAsync(_selectedImagePath, ResolveThreshold(cfg));
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

            // 異常検知ヒートマップ表示の更新（分類・FastAPI では AnomalyMap=null のため無効）
            _lastAnomalyResult      = result;
            _lastInspectedImagePath = _selectedImagePath;
            UpdateHeatmapDisplay();

            AppLogger.LogInspection(fileName, result.Result, result.Score,
                                    result.DefectType, inferenceMs,
                                    useOnnx ? "ONNX" : "FastAPI");
        }
        catch (Exception ex)
        {
            sw.Stop();
            apiStatus = $"エラー: {ex.Message}";
            ShowError(ex.Message);
            AppLogger.LogInferenceFailed(fileName, ex);
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
        ssApi.Text             = "API: 確認中";

        bool ok = await _apiClient.CheckHealthAsync();

        lblApiStatus.Text      = ok ? "API: 接続中 ✓" : "API: 未接続 ✗";
        lblApiStatus.ForeColor = ok ? Color.SeaGreen : Color.Crimson;
        btnCheckApi.Enabled    = true;
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
            AppLogger.LogSettingsChanged();
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

        AppLogger.LogsDir = string.IsNullOrWhiteSpace(s.CsvDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Logs")
            : Path.Combine(s.CsvDirectory, "Logs");

        // ONNX モードならモデル種別を解決してエンジンを生成・事前ロード
        if (s.InferenceMode == "ONNX" && !string.IsNullOrWhiteSpace(s.OnnxModelPath))
        {
            try
            {
                _engine?.Dispose();
                _engine = InspectionEngineFactory.Create(s.OnnxModelPath, s.OnnxModelType);
                AppLogger.LogModelLoaded(s.OnnxModelPath);
            }
            catch (Exception ex)
            {
                _engine?.Dispose();
                _engine = null;
                AppLogger.LogModelLoadFailed(s.OnnxModelPath, ex);
                ShowError($"ONNXモデルの読み込みに失敗しました:\n{ex.Message}");
            }
        }
        else
        {
            // FastAPI モード等ではローカルエンジンを解放
            _engine?.Dispose();
            _engine = null;
        }

        // API ステータスとStatusStrip更新
        if (lblApiStatus != null)
        {
            if (s.InferenceMode == "ONNX")
            {
                lblApiStatus.Text      = "ONNXモード (API不要)";
                lblApiStatus.ForeColor = Color.DarkCyan;
                ssApi.Text      = "API: (ONNX不使用)";
                ssApi.ForeColor = Color.DimGray;
            }
            else
            {
                lblApiStatus.Text      = "API: 未確認";
                lblApiStatus.ForeColor = Color.Gray;
                ssApi.Text      = "API: 未確認";
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

        if (lblModelMode != null)
            UpdateModelInfo(s);

        CsvLogService.LogsDir           = s.CsvDirectory;
        NgImageSaverService.NgDirectory = s.NgImageDirectory;

        // 設定変更時に PLC リソースをリセット（再接続を促す）
        if (_plcService != null)
        {
            DisposePlcResources();
            if (lblPlcStatus != null)
                SetPlcStatusDisplay("設定変更 — 再接続が必要", Color.DarkOrange);
            if (btnPlcDisconnect != null) btnPlcDisconnect.Enabled = false;
            if (btnPlcMonitor    != null)
            {
                btnPlcMonitor.Enabled    = false;
                btnPlcMonitor.Text       = "▶ 監視開始";
                btnPlcMonitor.BackColor  = Color.SeaGreen;
            }
        }
    }

    /// <summary>
    /// 現在のエンジン種別に応じて使用する OK/NG 判定閾値を返す。
    /// 異常検知=AnomalyThreshold / 分類・FastAPI=NgThreshold。
    /// </summary>
    private double ResolveThreshold(AppSettings cfg) =>
        _engine?.Kind == InspectionEngineKind.Anomaly ? cfg.AnomalyThreshold : cfg.NgThreshold;

    private void UpdateModelInfo(AppSettings s)
    {
        if (s.InferenceMode == "ONNX")
        {
            lblModelMode.Text   = $"推論モード: ONNX [{_engine?.ModelModeText ?? "未読込"}]";
            lblModelName.Text   = $"モデル名: {_engine?.LoadedModelName ?? "未設定"}";
            lblModelInput.Text  = $"入力サイズ: {_engine?.InputShapeText ?? "---"}";
            // ラベルは短い名前を表示し、フルパスは ToolTip で確認できるようにする
            _modelPathTip.SetToolTip(lblModelName,
                string.IsNullOrWhiteSpace(s.OnnxModelPath) ? "" : s.OnnxModelPath);
            bool loaded = _engine?.IsLoaded ?? false;
            lblModelStatus.Text      = loaded ? "読込状態: 正常 ✓" : "読込状態: 未読込 ✗";
            lblModelStatus.ForeColor = loaded ? Color.SeaGreen : Color.Crimson;
            ssModel.Text      = loaded ? "MODEL: 正常 ✓" : "MODEL: 未読込 ✗";
            ssModel.ForeColor = loaded ? Color.SeaGreen : Color.Crimson;
        }
        else
        {
            lblModelMode.Text        = "推論モード: FastAPI";
            lblModelName.Text        = "モデル名: (サーバー側)";
            lblModelInput.Text       = "入力サイズ: (サーバー側)";
            _modelPathTip.SetToolTip(lblModelName, "");
            lblModelStatus.Text      = "読込状態: (サーバー側)";
            lblModelStatus.ForeColor = Color.Gray;
            ssModel.Text      = "MODEL: FastAPI";
            ssModel.ForeColor = Color.DimGray;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  カメラ操作
    // ══════════════════════════════════════════════════════════════

    private void BtnCameraStart_Click(object? sender, EventArgs e)
    {
        int cameraIndex = AppSettingsService.Current.CameraSettings.CameraIndex;
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
            // カメラ稼働中はヒートマップ表示を無効化
            _lastAnomalyResult = null;
            chkHeatmap.Visible = false;

            AppLogger.LogCameraStarted(cameraIndex);
            ssCamera.Text      = $"CAMERA: 起動中 (idx={cameraIndex})";
            ssCamera.ForeColor = Color.SeaGreen;
        }
        catch (Exception ex)
        {
            _cameraService?.Dispose();
            _cameraService = null;
            AppLogger.LogCameraError(cameraIndex, ex);
            ShowError($"カメラの起動に失敗しました:\n{ex.Message}");
        }
    }

    private void BtnCameraStop_Click(object? sender, EventArgs e) => StopCamera();

    // ══════════════════════════════════════════════════════════════
    //  PLC 連携ボタンイベント
    // ══════════════════════════════════════════════════════════════

    private async void BtnPlcConnect_Click(object? sender, EventArgs e)
    {
        btnPlcConnect.Enabled = false;
        SetPlcStatusDisplay("接続中...", Color.DarkOrange);

        var cfg = AppSettingsService.Current;
        var plcCfg = cfg.PlcSettings;

        DisposePlcResources();

        _plcService = plcCfg.UseFakeService
            ? (IPlcCommunicationService)new FakePlcCommunicationService()
            : new ModbusTcpPlcCommunicationService(plcCfg);

        bool ok = await _plcService.ConnectAsync();

        if (ok)
        {
            SetPlcStatusDisplay(
                plcCfg.UseFakeService ? "接続中 ✓ (シミュレーター)" : "接続中 ✓",
                Color.SeaGreen);
            btnPlcDisconnect.Enabled = true;
            btnPlcMonitor.Enabled    = true;
            btnPlcTestFire.Enabled   = plcCfg.UseFakeService;
        }
        else
        {
            SetPlcStatusDisplay("接続失敗 ✗", Color.Crimson);
            DisposePlcResources();
            ShowError($"PLCへの接続に失敗しました。\nIP: {plcCfg.IpAddress}:{plcCfg.Port}\n" +
                      "IPアドレス・ポート番号を確認するか、シミュレーターモードを使用してください。");
        }

        btnPlcConnect.Enabled = true;
    }

    private async void BtnPlcDisconnect_Click(object? sender, EventArgs e)
    {
        await StopPlcMonitorAsync();
        DisposePlcResources();
        SetPlcStatusDisplay("未接続", Color.Gray);
        btnPlcDisconnect.Enabled = false;
        btnPlcMonitor.Enabled    = false;
        btnPlcTestFire.Enabled   = false;
        btnPlcMonitor.Text       = "▶ 監視開始";
        btnPlcMonitor.BackColor  = Color.SeaGreen;
    }

    private async void BtnPlcMonitor_Click(object? sender, EventArgs e)
    {
        if (_plcService?.IsConnected != true)
        {
            ShowError("PLC が接続されていません。先に「PLC接続」を実行してください。");
            return;
        }

        if (_plcBridge?.IsPolling == true)
        {
            // 監視停止
            btnPlcMonitor.Enabled = false;
            await StopPlcMonitorAsync();
            btnPlcMonitor.Text      = "▶ 監視開始";
            btnPlcMonitor.BackColor = Color.SeaGreen;
            btnPlcMonitor.Enabled   = true;
            return;
        }

        // 推論モードチェック
        var cfg = AppSettingsService.Current;
        if (cfg.InferenceMode != "ONNX" || _engine is null || !_engine.IsLoaded)
        {
            ShowError("PLC 連携には ONNX モードのモデル読込が必要です。\n" +
                      "設定画面で「ONNXモード」を選択し、モデルファイルを指定してください。");
            return;
        }

        // カメラ撮像サービスを初期化（監視開始のたびに再作成）
        _cameraCaptureService?.Dispose();
        var camCfg = cfg.CameraSettings;
        _cameraCaptureService = camCfg.UseFakeCamera
            ? (ICameraCaptureService)new FakeCameraCaptureService(camCfg.FakeCameraImagePath)
            : new OpenCvCameraCaptureService();

        if (camCfg.UseCameraOnPlcTrigger)
            AppLogger.Info($"PLCカメラ撮像モード: " +
                           (camCfg.UseFakeCamera ? "FakeCamera" : $"CameraIndex={camCfg.CameraIndex}"));

        _plcBridge = new PlcInspectionBridge(_plcService!, _engine, cfg.PlcSettings);
        _plcBridge.InspectionCompleted += OnPlcInspectionCompleted;
        _plcBridge.StatusChanged       += OnPlcStatusChanged;
        // 閾値はエンジン種別で切替（分類=NgThreshold / 異常検知=AnomalyThreshold）
        _plcBridge.StartPolling(AcquireInspectionImageAsync, ResolveThreshold(cfg));

        btnPlcMonitor.Text      = "■ 監視停止";
        btnPlcMonitor.BackColor = Color.Crimson;
    }

    private void BtnPlcTestFire_Click(object? sender, EventArgs e)
    {
        if (_plcService is FakePlcCommunicationService fake)
        {
            var plcCfg = AppSettingsService.Current.PlcSettings;
            fake.SetTriggerAddress(plcCfg.TriggerAddress);
            fake.FireManualTrigger();
            AppLogger.Info("手動テストトリガ発火");
        }
        else
        {
            ShowError("テスト発火はシミュレーターモード専用です。");
        }
    }

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

            AppLogger.LogCameraStopped();
            ssCamera.Text      = "CAMERA: 停止";
            ssCamera.ForeColor = Color.Gray;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  PLC ヘルパー
    // ══════════════════════════════════════════════════════════════

    private void OnPlcInspectionCompleted(object? sender, PlcInspectionEventArgs e)
    {
        if (IsDisposed) return;
        try { Invoke(() => ProcessPlcResult(e)); } catch { }
    }

    private void ProcessPlcResult(PlcInspectionEventArgs e)
    {
        ShowResult(e.Result, e.Result.InferenceMs);
        lblPlcLastTrigger.Text = $"最終トリガ: {e.InspectedAt:HH:mm:ss}";

        var camCfg = AppSettingsService.Current.CameraSettings;
        var history = new InspectionHistory
        {
            InspectedAt       = e.InspectedAt,
            ImageFileName     = Path.GetFileName(e.ImagePath),
            ImagePath         = e.ImagePath,
            CapturedImagePath = camCfg.UseCameraOnPlcTrigger ? e.ImagePath : "",
            Result            = e.Result.Result,
            Score             = e.Result.Score,
            DefectType        = !string.IsNullOrEmpty(e.Result.ClassName)
                                ? e.Result.ClassName : e.Result.DefectType,
            Message           = e.Result.Message,
            ApiStatus         = "PLC",
            InferenceMs       = e.Result.InferenceMs,
        };

        AddHistory(history);
        _totalCount++;
        if      (history.Result == "OK") _okCount++;
        else if (history.Result == "NG") _ngCount++;
        UpdateStats();

        AppLogger.LogInspection(history.ImageFileName, history.Result, history.Score,
                                history.DefectType, history.InferenceMs, "PLC+ONNX");

        if (history.Result == "NG")
            try { NgImageSaverService.Save(e.ImagePath, history.InspectedAt); } catch { }

        try { CsvLogService.Save(history); }
        catch (Exception ex) { ShowError($"CSV保存に失敗しました: {ex.Message}"); }
    }

    private void OnPlcStatusChanged(object? sender, string status)
    {
        if (IsDisposed) return;
        try { Invoke(() => SetPlcStatusDisplay(status, null)); } catch { }
    }

    private void SetPlcStatusDisplay(string status, Color? color)
    {
        lblPlcStatus.Text      = $"状態: {status}";
        lblPlcStatus.ForeColor = color ?? Color.FromArgb(40, 40, 80);
        ssPlc.Text             = $"PLC: {status}";
        ssPlc.ForeColor        = color ?? Color.FromArgb(40, 40, 80);
    }

    private async Task<string?> AcquireInspectionImageAsync(CancellationToken ct)
    {
        var camCfg = AppSettingsService.Current.CameraSettings;

        if (camCfg.UseCameraOnPlcTrigger && _cameraCaptureService != null)
        {
            // カメラ撮像モード
            var saveDir = string.IsNullOrWhiteSpace(camCfg.CapturedImageDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "CapturedImages", DateTime.Now.ToString("yyyyMMdd"))
                : Path.Combine(camCfg.CapturedImageDirectory, DateTime.Now.ToString("yyyyMMdd"));

            try
            {
                var path = await _cameraCaptureService.CaptureAsync(
                    camCfg.CameraIndex, saveDir, camCfg.CaptureTimeoutMs, ct);

                AppLogger.Info($"PLCトリガ撮像: {Path.GetFileName(path)}");

                if (IsHandleCreated && !IsDisposed)
                    try { Invoke(() => lblPlcLastTrigger.Text = $"最終撮像: {DateTime.Now:HH:mm:ss}"); }
                    catch { }

                return path;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Error("PLCトリガ撮像失敗", ex);
                return null;
            }
        }

        // フォールバック: ライブプレビューフレームまたは選択画像
        if (_cameraService?.IsRunning == true && _lastCameraFrame != null)
        {
            try
            {
                var tempDir  = Path.Combine(Path.GetTempPath(), "VisionInspectionHmi");
                Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, $"plc_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");

                Bitmap? snapshot = null;
                if (IsHandleCreated && !IsDisposed)
                    try { Invoke(() => { if (_lastCameraFrame != null) snapshot = new Bitmap(_lastCameraFrame); }); }
                    catch { }

                snapshot?.Save(tempPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                snapshot?.Dispose();
                return File.Exists(tempPath) ? tempPath : _selectedImagePath;
            }
            catch (Exception ex)
            {
                AppLogger.Error("PLC用ライブフレーム保存失敗", ex);
            }
        }

        return _selectedImagePath;
    }

    private async Task StopPlcMonitorAsync()
    {
        if (_plcBridge == null) return;
        _plcBridge.InspectionCompleted -= OnPlcInspectionCompleted;
        _plcBridge.StatusChanged       -= OnPlcStatusChanged;
        await _plcBridge.StopPollingAsync();
        _plcBridge.Dispose();
        _plcBridge = null;
    }

    private void DisposePlcResources()
    {
        _plcBridge?.Dispose();
        _plcBridge = null;
        _cameraCaptureService?.Dispose();
        _cameraCaptureService = null;
        _plcService?.Dispose();
        _plcService = null;
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

    // ── 異常検知ヒートマップ表示 ──────────────────────────────────────

    /// <summary>
    /// 直近の検査結果に anomaly_map がある場合のみヒートマップ表示を有効化する。
    /// カメラ稼働中・分類/FastAPI 結果では無効（トグルを隠し元画像のまま）。
    /// </summary>
    private void UpdateHeatmapDisplay()
    {
        bool hasMap = _lastAnomalyResult?.AnomalyMap != null
                      && _cameraService?.IsRunning != true
                      && !string.IsNullOrEmpty(_lastInspectedImagePath)
                      && File.Exists(_lastInspectedImagePath);

        chkHeatmap.Visible = hasMap;
        if (hasMap) RenderHeatmapOrOriginal();
    }

    /// <summary>トグル状態に応じて picImage にヒートマップ重畳 / 元画像を表示する。</summary>
    private void RenderHeatmapOrOriginal()
    {
        var r = _lastAnomalyResult;
        if (r?.AnomalyMap == null || _lastInspectedImagePath == null
            || !File.Exists(_lastInspectedImagePath)) return;

        try
        {
            var prev = picImage.Image;
            if (chkHeatmap.Checked)
            {
                // 異常検知閾値を基準に着色（閾値未満は透明）。閾値は設定値を使用。
                double threshold = AppSettingsService.Current.AnomalyThreshold;
                using var baseImg = LoadImageCopy(_lastInspectedImagePath);
                picImage.Image = AnomalyHeatmapRenderer.Overlay(
                    baseImg, r.AnomalyMap, r.AnomalyMapWidth, r.AnomalyMapHeight, threshold);
            }
            else
            {
                picImage.Image = LoadImageCopy(_lastInspectedImagePath);
            }
            // カメラフレームは別管理。それ以外の旧 Image のみ破棄。
            if (prev != _lastCameraFrame) prev?.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Error("ヒートマップ描画に失敗しました", ex);
        }
    }

    /// <summary>
    /// ファイルロックを残さない画像読み込み。FileStream 経由で読み、独立した Bitmap を返す。
    /// （Image.FromFile / new Bitmap(path) は元ファイルをロックし続けるため使用しない）
    /// </summary>
    private static Image LoadImageCopy(string path)
    {
        using var fs  = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var tmp = Image.FromStream(fs);
        return new Bitmap(tmp);
    }

    // ── フォルダ検査「モニターモード」用の表示更新（BatchInspectionForm から呼ばれる）──

    /// <summary>モニターモード: 推論前に対象画像をプレビュー表示し、結果欄をクリアする。</summary>
    internal void MonitorShowImage(string imagePath)
    {
        try
        {
            var img = LoadImageCopy(imagePath);
            picImage.Image?.Dispose();
            picImage.Image = img;
        }
        catch (Exception ex) { AppLogger.Error("モニター: 画像表示に失敗", ex); }

        _selectedImagePath = imagePath;
        lblImagePath.Text  = Path.GetFileName(imagePath);
        ClearResult();
    }

    /// <summary>モニターモード: 推論結果を判定ラベル・ヒートマップ・履歴・統計へ反映する。</summary>
    internal void MonitorShowResult(string imagePath, InspectionResult result)
    {
        ShowResult(result, result.InferenceMs);

        _lastAnomalyResult      = result;
        _lastInspectedImagePath = imagePath;
        UpdateHeatmapDisplay();

        var history = new InspectionHistory
        {
            InspectedAt   = DateTime.Now,
            ImageFileName = Path.GetFileName(imagePath),
            ImagePath     = imagePath,
            Result        = result.Result,
            Score         = result.Score,
            DefectType    = !string.IsNullOrEmpty(result.ClassName)
                            ? result.ClassName : result.DefectType,
            Message       = result.Message,
            ApiStatus     = "Batch",
            InferenceMs   = result.InferenceMs,
        };
        AddHistory(history);

        _totalCount++;
        if      (result.Result == "OK") _okCount++;
        else if (result.Result == "NG") _ngCount++;
        UpdateStats();
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
        // PLC ブリッジは同期的に停止（フォームクローズのため簡易処理）
        _plcBridge?.Dispose();
        _cameraCaptureService?.Dispose();
        _plcService?.Dispose();
        base.OnFormClosed(e);
        _apiClient.Dispose();
        _engine?.Dispose();
        _modelPathTip.Dispose();
        picImage.Image?.Dispose();
        AppLogger.Stop();
    }
}
