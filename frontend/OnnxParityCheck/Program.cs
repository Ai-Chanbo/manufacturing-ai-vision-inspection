// S4: C# ONNX Runtime パリティ検証コンソール
//
// Python (export_reference_csv.py) が生成した基準データと C# ONNX Runtime の
// 推論結果を突合する。HMI 本体には一切依存しない独立ツール。
//
//   Tier 1 … Python が書き出した前処理済みテンソル(.bin)をそのまま入力し、
//            出力(pred_score / pred_label / anomaly_map)を基準と比較。
//            → 前処理差を排除し、ORT ランタイム間の純粋な数値差のみを評価。
//   Tier 2 … C# 側で画像から前処理（resize bicubic / div255 / RGB / NCHW）して推論。
//            → 前処理移植の正しさを評価（PIL bicubic と C# bicubic の補間差を許容）。
//
// 使い方:
//   dotnet run -- [referenceDir]
//   referenceDir 省略時は backend/training/reference を上方探索で自動解決。

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

// ── 合格判定のしきい値 ───────────────────────────────────────────────
const double Tier1ScoreAtol = 1e-4;   // pred_score 絶対差（ランタイム純粋比較）
const double Tier1AmapAtol   = 1e-3;   // anomaly_map 要素最大絶対差
const double Tier2ScoreAtol  = 1e-2;   // pred_score 絶対差（前処理込み・補間差許容）
const double NgThreshold     = 0.5;    // OK/NG 判定しきい値（pred_score >= で NG）

// ── パス解決 ─────────────────────────────────────────────────────────
string referenceDir = args.Length > 0
    ? Path.GetFullPath(args[0])
    : ResolveReferenceDir();

if (!Directory.Exists(referenceDir))
{
    Console.Error.WriteLine($"[ERROR] reference ディレクトリが見つかりません: {referenceDir}");
    Console.Error.WriteLine("        先に backend/training/export_reference_csv.py を実行してください。");
    return 2;
}

string trainingDir = Directory.GetParent(referenceDir)!.FullName; // .../backend/training
string manifestPath = Path.Combine(referenceDir, "manifest.json");
string csvPath      = Path.Combine(referenceDir, "reference.csv");

var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
    ?? throw new InvalidOperationException("manifest.json の読み込みに失敗しました");

string onnxPath = Path.GetFullPath(Path.Combine(trainingDir, manifest.onnx_path));
int inputSize   = manifest.input_size;

Console.WriteLine("=== S4 パリティ検証 ===");
Console.WriteLine($"  reference : {referenceDir}");
Console.WriteLine($"  onnx      : {onnxPath}");
Console.WriteLine($"  input     : {manifest.input_name}  size={inputSize}  (Python ORT {manifest.ort_version})");
Console.WriteLine($"  outputs   : {string.Join(", ", manifest.output_names)}");
Console.WriteLine();

if (!File.Exists(onnxPath))
{
    Console.Error.WriteLine($"[ERROR] ONNX モデルが見つかりません: {onnxPath}");
    return 2;
}

var rows = ReadCsv(csvPath);

using var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
using var session = new InferenceSession(onnxPath, opts);
string inputName = session.InputMetadata.Keys.First();

// ── 集計用 ───────────────────────────────────────────────────────────
double maxT1Score = 0, maxT1Amap = 0, maxT2Score = 0;
int labelMismatch = 0, decisionMismatch = 0;
int n = rows.Count;

Console.WriteLine($"{"image_id",-22} | {"T1 dScore",10} | {"T1 dAmap",10} | {"label",7} | {"T2 dScore",10}");
Console.WriteLine(new string('-', 78));

foreach (var row in rows)
{
    string id = row["image_id"];
    double refScore = double.Parse(row["pred_score"], CultureInfo.InvariantCulture);
    int    refLabel = int.Parse(row["pred_label"], CultureInfo.InvariantCulture);

    // ── Tier 1: 基準テンソルを直接入力 ──────────────────────────────
    float[] inputTensor = ReadF32(Path.Combine(referenceDir, row["input_bin"]));
    var (t1Score, t1Label, t1Amap) = RunInference(session, inputName, inputTensor, inputSize);

    float[] refAmap = ReadF32(Path.Combine(referenceDir, row["amap_bin"]));
    double dT1Score = Math.Abs(t1Score - refScore);
    double dT1Amap  = MaxAbsDiff(t1Amap, refAmap);
    bool labelOk    = t1Label == refLabel;
    if (!labelOk) labelMismatch++;

    // OK/NG 判定の一致（C# Tier1 score vs Python 基準 score、同一しきい値）
    bool refNg = refScore >= NgThreshold;
    bool csNg  = t1Score  >= NgThreshold;
    if (refNg != csNg) decisionMismatch++;

    // ── Tier 2: 画像から C# 前処理して推論 ──────────────────────────
    string imgPath = Path.GetFullPath(Path.Combine(trainingDir, row["image_path"]));
    double dT2Score = double.NaN;
    if (File.Exists(imgPath))
    {
        float[] csTensor = PreprocessImage(imgPath, inputSize);
        var (t2Score, _, _) = RunInference(session, inputName, csTensor, inputSize);
        dT2Score = Math.Abs(t2Score - refScore);
        maxT2Score = Math.Max(maxT2Score, dT2Score);
    }

    maxT1Score = Math.Max(maxT1Score, dT1Score);
    maxT1Amap  = Math.Max(maxT1Amap, dT1Amap);

    Console.WriteLine($"{id,-22} | {dT1Score,10:E2} | {dT1Amap,10:E2} | " +
                      $"{(labelOk ? "ok" : "MISS"),7} | {dT2Score,10:E2}");
}

// ── 合否判定 ─────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== サマリ ===");
Console.WriteLine($"  画像数              : {n}");
Console.WriteLine($"  Tier1 max dScore    : {maxT1Score:E3}  (許容 {Tier1ScoreAtol:E0})");
Console.WriteLine($"  Tier1 max dAmap     : {maxT1Amap:E3}  (許容 {Tier1AmapAtol:E0})");
Console.WriteLine($"  pred_label 不一致    : {labelMismatch}  (許容 0)");
Console.WriteLine($"  OK/NG 判定 不一致    : {decisionMismatch}  (許容 0)");
Console.WriteLine($"  Tier2 max dScore    : {maxT2Score:E3}  (許容 {Tier2ScoreAtol:E0})");
Console.WriteLine();

bool t1Pass       = maxT1Score <= Tier1ScoreAtol && maxT1Amap <= Tier1AmapAtol;
bool labelPass    = labelMismatch == 0;
bool decisionPass = decisionMismatch == 0;
bool t2Pass       = maxT2Score <= Tier2ScoreAtol;
bool allPass      = t1Pass && labelPass && decisionPass && t2Pass;

Console.WriteLine($"  [Tier1 ランタイム] {(t1Pass ? "PASS" : "FAIL")}");
Console.WriteLine($"  [pred_label一致 ] {(labelPass ? "PASS" : "FAIL")}");
Console.WriteLine($"  [OK/NG判定一致  ] {(decisionPass ? "PASS" : "FAIL")}");
Console.WriteLine($"  [Tier2 前処理込 ] {(t2Pass ? "PASS" : "FAIL")}");
Console.WriteLine();
Console.WriteLine(allPass ? "総合: PASS ✓" : "総合: FAIL ✗");
return allPass ? 0 : 1;

// ─────────────────────────────────────────────────────────────────────
//  ヘルパー
// ─────────────────────────────────────────────────────────────────────

// 推論を1回実行し pred_score / pred_label / anomaly_map を返す。
static (double score, int label, float[] amap) RunInference(
    InferenceSession session, string inputName, float[] tensor, int inputSize)
{
    var dense = new DenseTensor<float>(tensor, new[] { 1, 3, inputSize, inputSize });
    var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, dense) };
    using var results = session.Run(inputs);

    double score = 0;
    int label = -1;
    float[] amap = Array.Empty<float>();

    foreach (var r in results)
    {
        switch (r.Name)
        {
            case "pred_score":
                score = r.AsEnumerable<float>().First();
                break;
            case "pred_label":
                label = r.AsEnumerable<bool>().First() ? 1 : 0;
                break;
            case "anomaly_map":
                amap = r.AsEnumerable<float>().ToArray();
                break;
        }
    }
    return (score, label, amap);
}

// Python と同一の前処理: RGB / resize(bicubic) / div255 / NCHW（正規化なし）。
static float[] PreprocessImage(string path, int size)
{
    using var src = new Bitmap(path);
    using var resized = new Bitmap(size, size, PixelFormat.Format24bppRgb);
    using (var g = Graphics.FromImage(resized))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic; // PIL 既定(BICUBIC)に対応
        g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, size, size);
    }

    var data = resized.LockBits(new Rectangle(0, 0, size, size),
        ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
    int stride = data.Stride;
    var raw = new byte[stride * size];
    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, raw, 0, raw.Length);
    resized.UnlockBits(data);

    int plane = size * size;
    var tensor = new float[3 * plane];
    for (int y = 0; y < size; y++)
    {
        for (int x = 0; x < size; x++)
        {
            int rawIdx = y * stride + x * 3;   // Format24bppRgb は BGR 順
            int idx = y * size + x;
            tensor[0 * plane + idx] = raw[rawIdx + 2] / 255f; // R
            tensor[1 * plane + idx] = raw[rawIdx + 1] / 255f; // G
            tensor[2 * plane + idx] = raw[rawIdx + 0] / 255f; // B
        }
    }
    return tensor;
}

// リトルエンディアン float32 の生バイト列を読み込む。
static float[] ReadF32(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    var arr = new float[bytes.Length / 4];
    Buffer.BlockCopy(bytes, 0, arr, 0, arr.Length * 4);
    return arr;
}

static double MaxAbsDiff(float[] a, float[] b)
{
    if (a.Length != b.Length)
        throw new InvalidOperationException($"配列長不一致: {a.Length} vs {b.Length}");
    double max = 0;
    for (int i = 0; i < a.Length; i++)
        max = Math.Max(max, Math.Abs((double)a[i] - b[i]));
    return max;
}

// 単純な CSV パーサ（値にカンマ・引用符を含まない前提）。
static List<Dictionary<string, string>> ReadCsv(string path)
{
    var lines = File.ReadAllLines(path);
    var header = lines[0].Split(',');
    var rows = new List<Dictionary<string, string>>();
    for (int i = 1; i < lines.Length; i++)
    {
        if (string.IsNullOrWhiteSpace(lines[i])) continue;
        var cells = lines[i].Split(',');
        var dict = new Dictionary<string, string>();
        for (int c = 0; c < header.Length; c++)
            dict[header[c]] = c < cells.Length ? cells[c] : "";
        rows.Add(dict);
    }
    return rows;
}

// 実行ディレクトリから上方へ backend/training/reference を探索する。
static string ResolveReferenceDir()
{
    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    while (dir != null)
    {
        string candidate = Path.Combine(dir.FullName, "backend", "training", "reference");
        if (Directory.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    // 見つからなければ既定の相対位置（リポジトリ構成依存）
    return Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "backend", "training", "reference"));
}

// ── manifest.json マッピング ─────────────────────────────────────────
record Manifest(
    string onnx_path,
    string input_name,
    string[] output_names,
    int input_size,
    string input_layout,
    string preprocess,
    string byte_order,
    string ort_version,
    int num_images);
