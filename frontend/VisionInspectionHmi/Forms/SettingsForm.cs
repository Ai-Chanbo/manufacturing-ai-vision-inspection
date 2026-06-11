using VisionInspectionHmi.Models;

namespace VisionInspectionHmi.Forms;

public class SettingsForm : Form
{
    // API設定
    private TextBox        txtApiUrl     = null!;
    private NumericUpDown  nudApiTimeout = null!;

    // 検査設定
    private NumericUpDown nudNgThreshold     = null!;
    private CheckBox      chkInferenceEnabled = null!;

    // 保存設定
    private TextBox txtCsvDir = null!;
    private TextBox txtNgDir  = null!;

    // カメラ設定
    private ComboBox cmbCameraIndex = null!;

    // 推論モード設定
    private RadioButton rbFastApi     = null!;
    private RadioButton rbOnnx        = null!;
    private TextBox     txtOnnxPath   = null!;
    private Label       lblOnnxPath   = null!;
    private Button      btnBrowseOnnx = null!;

    // PLC設定
    private TextBox       txtPlcIp       = null!;
    private NumericUpDown nudPlcPort     = null!;
    private NumericUpDown nudPlcInterval = null!;
    private CheckBox      chkPlcFake    = null!;

    // 結果
    public AppSettings? ResultSettings { get; private set; }

    private readonly AppSettings _originalSettings;

    public SettingsForm(AppSettings current)
    {
        _originalSettings = current;
        InitializeComponent(current);
    }

    private void InitializeComponent(AppSettings s)
    {
        Text            = "設定";
        Size            = new Size(520, 710);
        MinimumSize     = new Size(520, 710);
        MaximumSize     = new Size(520, 710);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        BackColor       = Color.FromArgb(245, 245, 250);

        var baseFont = new Font("Meiryo UI", 9);

        // ── API設定 ──────────────────────────────────────────────
        var gbApi = CreateGroupBox("API設定", 12, 8, 490, 90);

        var lblUrl = CreateLabel("API URL:", 8, 24, 72);
        txtApiUrl  = new TextBox
        {
            Left = 84, Top = 22, Width = 398, Height = 22,
            Text = s.ApiUrl, Font = baseFont,
        };

        var lblTimeout = CreateLabel("タイムアウト(秒):", 8, 52, 120);
        nudApiTimeout  = new NumericUpDown
        {
            Left = 132, Top = 50, Width = 70, Height = 22, Font = baseFont,
            Minimum = 5, Maximum = 120, Value = Math.Clamp(s.ApiTimeoutSeconds, 5, 120),
        };
        var lblTimeoutNote = CreateLabel("(5〜120秒)", 210, 52, 80, Color.Gray, 8);

        gbApi.Controls.AddRange([lblUrl, txtApiUrl, lblTimeout, nudApiTimeout, lblTimeoutNote]);

        // ── 検査設定 ─────────────────────────────────────────────
        var gbInspect = CreateGroupBox("検査設定", 12, 106, 490, 90);

        var lblThreshold = CreateLabel("NG判定閾値:", 8, 24, 90);
        nudNgThreshold   = new NumericUpDown
        {
            Left = 102, Top = 22, Width = 80, Height = 22, Font = baseFont,
            Minimum = (decimal)0.01, Maximum = (decimal)0.99,
            DecimalPlaces = 2, Increment = (decimal)0.05,
            Value = (decimal)Math.Clamp(s.NgThreshold, 0.01, 0.99),
        };
        var lblThreshNote = CreateLabel("(0.01〜0.99)", 190, 24, 100, Color.Gray, 8);

        chkInferenceEnabled = new CheckBox
        {
            Left = 8, Top = 52, Width = 400, Height = 22, Font = baseFont,
            Text    = "推論APIを実行する（チェックを外すと検査がスキップされます）",
            Checked = s.InferenceEnabled,
        };

        gbInspect.Controls.AddRange([lblThreshold, nudNgThreshold, lblThreshNote, chkInferenceEnabled]);

        // ── 保存設定 ─────────────────────────────────────────────
        var gbSave = CreateGroupBox("保存設定", 12, 204, 490, 116);

        var lblCsvDir = CreateLabel("CSV保存先:", 8, 24, 80);
        txtCsvDir     = new TextBox
        {
            Left = 92, Top = 22, Width = 316, Height = 22, Font = baseFont,
            Text = s.CsvDirectory, PlaceholderText = "空欄 = デフォルト (Logs フォルダ)",
        };
        var btnBrowseCsv = CreateSmallButton("参照...", 414, 21);
        btnBrowseCsv.Click += (_, _) => BrowseFolder(txtCsvDir);

        var lblNgDir = CreateLabel("NG画像保存先:", 8, 54, 92);
        txtNgDir     = new TextBox
        {
            Left = 104, Top = 52, Width = 304, Height = 22, Font = baseFont,
            Text = s.NgImageDirectory, PlaceholderText = "空欄 = デフォルト (Results/NG)",
        };
        var btnBrowseNg = CreateSmallButton("参照...", 414, 51);
        btnBrowseNg.Click += (_, _) => BrowseFolder(txtNgDir);

        var lblSaveNote = CreateLabel(
            "空欄の場合はEXEと同じフォルダ内のデフォルトフォルダを使用します",
            8, 84, 470, Color.Gray, 8);

        gbSave.Controls.AddRange([lblCsvDir, txtCsvDir, btnBrowseCsv,
                                   lblNgDir, txtNgDir, btnBrowseNg, lblSaveNote]);

        // ── カメラ設定 ───────────────────────────────────────────
        var gbCamera = CreateGroupBox("カメラ設定", 12, 328, 490, 70);

        var lblCamIdx = CreateLabel("カメラ番号(0〜3):", 8, 24, 122);
        cmbCameraIndex = new ComboBox
        {
            Left = 134, Top = 22, Width = 60, Height = 22, Font = baseFont,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        cmbCameraIndex.Items.AddRange(["0", "1", "2", "3"]);
        cmbCameraIndex.SelectedIndex = Math.Clamp(s.CameraIndex, 0, 3);

        var lblResNote = CreateLabel("解像度はカメラ起動後に確認できます", 210, 24, 270, Color.Gray, 8);

        gbCamera.Controls.AddRange([lblCamIdx, cmbCameraIndex, lblResNote]);

        // ── 推論モード設定 ────────────────────────────────────────
        var gbMode = CreateGroupBox("推論モード設定", 12, 406, 490, 116);

        rbFastApi = new RadioButton
        {
            Left = 8, Top = 24, Width = 460, Height = 22, Font = baseFont,
            Text    = "FastAPIモード（バックエンドAPIを経由して推論）",
            Checked = s.InferenceMode != "ONNX",
        };
        rbOnnx = new RadioButton
        {
            Left = 8, Top = 48, Width = 460, Height = 22, Font = baseFont,
            Text    = "ONNXモード（ローカルモデルを直接使用、APIなし）",
            Checked = s.InferenceMode == "ONNX",
        };

        lblOnnxPath = CreateLabel("モデルパス:", 28, 76, 80);
        txtOnnxPath = new TextBox
        {
            Left = 112, Top = 74, Width = 286, Height = 22, Font = baseFont,
            Text = s.OnnxModelPath, PlaceholderText = ".onnxファイルを選択してください",
        };
        btnBrowseOnnx = CreateSmallButton("参照...", 404, 73);
        btnBrowseOnnx.Click += (_, _) => BrowseOnnxFile();

        rbFastApi.CheckedChanged += (_, _) => UpdateOnnxControls();
        rbOnnx.CheckedChanged   += (_, _) => UpdateOnnxControls();

        gbMode.Controls.AddRange([rbFastApi, rbOnnx, lblOnnxPath, txtOnnxPath, btnBrowseOnnx]);

        UpdateOnnxControls();

        // ── PLC設定 ──────────────────────────────────────────────
        var plc = s.PlcSettings;
        var gbPlc = CreateGroupBox("PLC連携設定（Modbus TCP）", 12, 528, 490, 96);

        var lblPlcIp = CreateLabel("IPアドレス:", 8, 24, 82);
        txtPlcIp = new TextBox
        {
            Left = 94, Top = 22, Width = 150, Height = 22, Font = baseFont,
            Text = plc.IpAddress,
        };

        var lblPlcPort = CreateLabel("ポート:", 256, 24, 46);
        nudPlcPort = new NumericUpDown
        {
            Left = 306, Top = 22, Width = 70, Height = 22, Font = baseFont,
            Minimum = 1, Maximum = 65535, Value = Math.Clamp(plc.Port, 1, 65535),
        };

        var lblPlcInterval = CreateLabel("ポーリング(ms):", 8, 52, 106);
        nudPlcInterval = new NumericUpDown
        {
            Left = 118, Top = 50, Width = 70, Height = 22, Font = baseFont,
            Minimum = 50, Maximum = 5000, Increment = 50,
            Value = Math.Clamp(plc.PollingIntervalMs, 50, 5000),
        };

        chkPlcFake = new CheckBox
        {
            Left = 8, Top = 74, Width = 470, Height = 18, Font = baseFont,
            Text    = "シミュレーターモード（実 PLC 不要 — FakePlcCommunicationService を使用）",
            Checked = plc.UseFakeService,
        };

        gbPlc.Controls.AddRange([lblPlcIp, txtPlcIp, lblPlcPort, nudPlcPort,
                                  lblPlcInterval, nudPlcInterval, chkPlcFake]);

        // ── 保存 / キャンセルボタン ──────────────────────────────
        var btnSave = new Button
        {
            Left = 300, Top = 634, Width = 96, Height = 32,
            Text = "保存", Font = new Font("Meiryo UI", 9, FontStyle.Bold),
            BackColor = Color.SeaGreen, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Left = 404, Top = 634, Width = 96, Height = 32,
            Text = "キャンセル", Font = new Font("Meiryo UI", 9),
            BackColor = Color.DimGray, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };

        btnSave.Click   += BtnSave_Click;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange([gbApi, gbInspect, gbSave, gbCamera, gbMode, gbPlc, btnSave, btnCancel]);
    }

    private void UpdateOnnxControls()
    {
        bool onnxSelected = rbOnnx.Checked;
        lblOnnxPath.Enabled   = onnxSelected;
        txtOnnxPath.Enabled   = onnxSelected;
        btnBrowseOnnx.Enabled = onnxSelected;
        txtOnnxPath.BackColor = onnxSelected ? Color.White : Color.FromArgb(235, 235, 235);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        // API URL バリデーション
        var url = txtApiUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url) ||
            (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
             !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("API URL は http:// または https:// で始まる有効なURLを入力してください。",
                "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtApiUrl.Focus();
            return;
        }

        // ONNXモード時のモデルパス バリデーション
        if (rbOnnx.Checked)
        {
            var modelPath = txtOnnxPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                MessageBox.Show("ONNXモードではモデルパスを指定してください。",
                    "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtOnnxPath.Focus();
                return;
            }
            if (!File.Exists(modelPath))
            {
                MessageBox.Show($"指定されたONNXモデルファイルが見つかりません:\n{modelPath}",
                    "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtOnnxPath.Focus();
                return;
            }
            if (!modelPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("ONNXモデルファイルは .onnx 拡張子である必要があります。",
                    "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtOnnxPath.Focus();
                return;
            }
        }

        ResultSettings = new AppSettings
        {
            ApiUrl            = url,
            ApiTimeoutSeconds = (int)nudApiTimeout.Value,
            NgThreshold       = (double)nudNgThreshold.Value,
            InferenceEnabled  = chkInferenceEnabled.Checked,
            CsvDirectory      = txtCsvDir.Text.Trim(),
            NgImageDirectory  = txtNgDir.Text.Trim(),
            CameraIndex       = int.Parse(cmbCameraIndex.SelectedItem?.ToString() ?? "0"),
            InferenceMode     = rbOnnx.Checked ? "ONNX" : "FastAPI",
            OnnxModelPath     = txtOnnxPath.Text.Trim(),
            PlcSettings       = new PlcSettings
            {
                IpAddress         = txtPlcIp.Text.Trim(),
                Port              = (int)nudPlcPort.Value,
                PollingIntervalMs = (int)nudPlcInterval.Value,
                UseFakeService    = chkPlcFake.Checked,
                // レジスタアドレスは UI 非公開のため現在の設定値を引き継ぐ
                TriggerAddress    = _originalSettings.PlcSettings.TriggerAddress,
                BusyAddress       = _originalSettings.PlcSettings.BusyAddress,
                ResultAddress     = _originalSettings.PlcSettings.ResultAddress,
                ErrorCodeAddress  = _originalSettings.PlcSettings.ErrorCodeAddress,
                HeartbeatAddress  = _originalSettings.PlcSettings.HeartbeatAddress,
            },
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void BrowseFolder(TextBox target)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description         = "フォルダを選択してください",
            UseDescriptionForTitle = true,
        };
        if (!string.IsNullOrWhiteSpace(target.Text) && Directory.Exists(target.Text))
            dlg.InitialDirectory = target.Text;

        if (dlg.ShowDialog() == DialogResult.OK)
            target.Text = dlg.SelectedPath;
    }

    private void BrowseOnnxFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "ONNXモデルファイルを選択",
            Filter = "ONNXモデル (*.onnx)|*.onnx|すべてのファイル (*.*)|*.*",
        };
        if (!string.IsNullOrWhiteSpace(txtOnnxPath.Text))
        {
            var dir = Path.GetDirectoryName(txtOnnxPath.Text);
            if (dir != null && Directory.Exists(dir))
                dlg.InitialDirectory = dir;
        }
        if (dlg.ShowDialog() == DialogResult.OK)
            txtOnnxPath.Text = dlg.FileName;
    }

    // ── ファクトリ ────────────────────────────────────────────────

    private static GroupBox CreateGroupBox(string title, int x, int y, int w, int h) =>
        new()
        {
            Text    = title,
            Left = x, Top = y, Width = w, Height = h,
            Font    = new Font("Meiryo UI", 9, FontStyle.Bold),
            Padding = new Padding(4, 20, 4, 4),
        };

    private static Label CreateLabel(string text, int x, int y, int w,
                                     Color? color = null, float fontSize = 9f) =>
        new()
        {
            Text      = text,
            Left = x, Top = y, Width = w, Height = 22,
            Font      = new Font("Meiryo UI", fontSize),
            ForeColor = color ?? Color.FromArgb(30, 30, 30),
            AutoSize  = false,
        };

    private static Button CreateSmallButton(string text, int x, int y) =>
        new()
        {
            Text      = text,
            Left = x, Top = y, Width = 70, Height = 24,
            Font      = new Font("Meiryo UI", 8),
            BackColor = Color.FromArgb(100, 100, 120),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
        };
}
