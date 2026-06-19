using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VisionInspectionHmi.Models;

namespace VisionInspectionHmi.Services;

/// <summary>
/// ONNX Runtime でローカルモデルを実行する推論サービス。
/// 出力クラス数を自動検出し、ImageNet1000クラスとカスタム7クラスの両モードに対応する。
/// </summary>
public sealed class OnnxInspectionService : IInspectionEngine
{
    // ── カスタム7クラスモデル用定義 ────────────────────────────────
    private static readonly string[] DefectLabels =
        ["none", "scratch", "stain", "crack", "shape", "label", "unknown"];

    private static readonly Dictionary<string, string> DefectMessages = new()
    {
        ["none"]    = "異常は検出されませんでした",
        ["scratch"] = "キズの可能性があります",
        ["stain"]   = "汚れの可能性があります",
        ["crack"]   = "欠けの可能性があります",
        ["shape"]   = "形状異常の可能性があります",
        ["label"]   = "ラベル・印字ズレの可能性があります",
        ["unknown"] = "異常が検出されました",
    };

    // ── ImageNet正規化定数 ─────────────────────────────────────────
    private static readonly float[] ImageNetMean = [0.485f, 0.456f, 0.406f]; // RGB
    private static readonly float[] ImageNetStd  = [0.229f, 0.224f, 0.225f]; // RGB

    private const int InputSize = 224;

    private InferenceSession? _session;
    private string   _loadedPath  = "";
    private string[] _classLabels = DefectLabels;
    private int      _outputSize  = 7;
    private bool     _disposed;

    // 出力クラス数が1000なら ImageNet モードとして扱う
    private bool IsImageNetMode => _outputSize == 1000;

    // ── モデル情報プロパティ ──────────────────────────────────────
    /// <summary>本サービスは常に分類エンジン。</summary>
    public InspectionEngineKind Kind => InspectionEngineKind.Classification;

    public bool IsLoaded => _session != null;

    public string LoadedModelName =>
        string.IsNullOrEmpty(_loadedPath) ? "未設定" : Path.GetFileName(_loadedPath);

    public string InputShapeText
    {
        get
        {
            if (_session == null) return "---";
            var meta = _session.InputMetadata.Values.FirstOrDefault();
            if (meta == null) return "---";
            return string.Join("×", meta.Dimensions.Select(d => d < 0 ? "?" : d.ToString()));
        }
    }

    /// <summary>モデル種別テキスト (UI表示用)。</summary>
    public string ModelModeText =>
        IsImageNetMode ? $"ImageNet {_outputSize}クラス" : $"カスタム {_outputSize}クラス";

    // ──────────────────────────────────────────────────────────────
    //  公開メソッド
    // ──────────────────────────────────────────────────────────────

    /// <summary>モデルを読み込む。既に同じパスなら再ロードしない。</summary>
    public void LoadModel(string modelPath)
    {
        if (modelPath == _loadedPath && _session != null) return;

        _session?.Dispose();
        _session     = null;
        _loadedPath  = "";
        _outputSize  = 7;
        _classLabels = DefectLabels;

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"ONNXモデルが見つかりません: {modelPath}");

        var opts = new SessionOptions();
        opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        _session    = new InferenceSession(modelPath, opts);
        _loadedPath = modelPath;

        // ── 出力クラス数の自動検出 ─────────────────────────────
        var outMeta = _session.OutputMetadata.Values.FirstOrDefault();
        if (outMeta != null)
        {
            // 最後の次元をクラス数とみなす
            int last = outMeta.Dimensions.Length > 0
                ? outMeta.Dimensions[^1]
                : -1;
            _outputSize = last > 0 ? last : 7;
        }

        // ── ラベル読み込み ────────────────────────────────────
        _classLabels = IsImageNetMode
            ? LoadImageNetLabels(modelPath) ?? GenerateFallbackLabels(_outputSize)
            : DefectLabels;
    }

    /// <summary>画像を推論し結果を返す。</summary>
    public Task<InspectionResult> InspectAsync(string imagePath, double ngThreshold)
    {
        if (_session is null)
            throw new InvalidOperationException(
                "モデルが読み込まれていません。LoadModel() を先に呼び出してください。");
        return Task.Run(() => RunInference(imagePath, ngThreshold));
    }

    // ──────────────────────────────────────────────────────────────
    //  推論パイプライン
    // ──────────────────────────────────────────────────────────────

    private InspectionResult RunInference(string imagePath, double ngThreshold)
    {
        var sw = Stopwatch.StartNew();

        var tensor      = Preprocess(imagePath);
        var inputName   = _session!.InputMetadata.Keys.First();
        var denseTensor = new DenseTensor<float>(tensor, new[] { 1, 3, InputSize, InputSize });
        var inputs      = new List<NamedOnnxValue>
            { NamedOnnxValue.CreateFromTensor(inputName, denseTensor) };

        using var outputs = _session.Run(inputs);
        sw.Stop();

        var logits = outputs.First().AsEnumerable<float>().ToArray();
        var probs  = Softmax(logits);

        // Top-5 取得（確率降順）
        var ranked = probs
            .Select((p, i) => (Prob: (double)p, Idx: i))
            .OrderByDescending(x => x.Prob)
            .Take(5)
            .ToArray();

        var top5 = ranked
            .Select((x, r) => new Top5Entry(
                r + 1,
                x.Idx < _classLabels.Length ? _classLabels[x.Idx] : $"class_{x.Idx}",
                Math.Round(x.Prob, 4)))
            .ToList();

        int    top1Idx   = ranked[0].Idx;
        double top1Score = ranked[0].Prob;
        string top1Label = top5[0].Label;

        return IsImageNetMode
            ? BuildImageNetResult(top1Label, top1Score, ngThreshold, top5, sw)
            : BuildCustomResult(top1Idx, top1Score, ngThreshold, top5, sw);
    }

    // ── ImageNetモード結果生成 ──────────────────────────────────
    private static InspectionResult BuildImageNetResult(
        string top1Label, double top1Score, double ngThreshold,
        List<Top5Entry> top5, Stopwatch sw)
    {
        // 信頼度が閾値以上 → OK（モデルが自信を持って識別できている）
        bool isOk = top1Score >= ngThreshold;
        return new InspectionResult
        {
            Result        = isOk ? "OK" : "NG",
            Score         = Math.Round(top1Score, 4),
            DefectType    = isOk ? "none" : "unknown",
            ClassName     = top1Label,
            Message       = isOk
                                ? $"識別クラス: {top1Label}"
                                : $"識別不確実 ({top1Label})",
            InferenceMs   = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
            Top5Candidates = top5,
        };
    }

    // ── カスタムモデル結果生成 ──────────────────────────────────
    private InspectionResult BuildCustomResult(
        int top1Idx, double top1Score, double ngThreshold,
        List<Top5Entry> top5, Stopwatch sw)
    {
        string defectType = top1Idx < DefectLabels.Length
            ? DefectLabels[top1Idx]
            : "unknown";

        bool isNg = top1Score >= ngThreshold && defectType != "none";
        if (!isNg) defectType = "none";

        return new InspectionResult
        {
            Result        = isNg ? "NG" : "OK",
            Score         = Math.Round(top1Score, 4),
            DefectType    = defectType,
            Message       = DefectMessages.GetValueOrDefault(defectType, "異常が検出されました"),
            InferenceMs   = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
            Top5Candidates = top5,
        };
    }

    // ──────────────────────────────────────────────────────────────
    //  前処理: Bitmap → NCHW float32 テンソル
    // ──────────────────────────────────────────────────────────────

    private float[] Preprocess(string imagePath)
    {
        using var src     = new Bitmap(imagePath);
        using var resized = new Bitmap(InputSize, InputSize,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(resized);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, 0, 0, InputSize, InputSize);

        // LockBits + Marshal.Copy で一括転送 (GetPixelより約100倍高速)
        var bmpData = resized.LockBits(
            new Rectangle(0, 0, InputSize, InputSize),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        int stride  = bmpData.Stride;
        var rawData = new byte[stride * InputSize];
        System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, rawData, 0, rawData.Length);
        resized.UnlockBits(bmpData);

        bool normalize = IsImageNetMode; // ImageNet pretrained モデルは mean/std 正規化が必要
        var tensor = new float[3 * InputSize * InputSize];

        for (int y = 0; y < InputSize; y++)
        {
            for (int x = 0; x < InputSize; x++)
            {
                int rawIdx    = y * stride + x * 3;
                int tensorIdx = y * InputSize + x;

                // Format24bppRgb は BGR 順で格納されている
                float r = rawData[rawIdx + 2] / 255f;
                float gv = rawData[rawIdx + 1] / 255f;
                float b = rawData[rawIdx + 0] / 255f;

                if (normalize)
                {
                    // ImageNet正規化: (pixel - mean) / std
                    tensor[0 * InputSize * InputSize + tensorIdx] = (r  - ImageNetMean[0]) / ImageNetStd[0];
                    tensor[1 * InputSize * InputSize + tensorIdx] = (gv - ImageNetMean[1]) / ImageNetStd[1];
                    tensor[2 * InputSize * InputSize + tensorIdx] = (b  - ImageNetMean[2]) / ImageNetStd[2];
                }
                else
                {
                    // カスタムモデル: /255 のみ
                    tensor[0 * InputSize * InputSize + tensorIdx] = r;
                    tensor[1 * InputSize * InputSize + tensorIdx] = gv;
                    tensor[2 * InputSize * InputSize + tensorIdx] = b;
                }
            }
        }
        return tensor;
    }

    // ──────────────────────────────────────────────────────────────
    //  ラベルファイル読み込み
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// モデルファイルと同ディレクトリにある imagenet_labels.txt を読み込む。
    /// 見つからない場合は null を返す。
    /// </summary>
    private static string[]? LoadImageNetLabels(string modelPath)
    {
        var candidates = new[]
        {
            Path.Combine(Path.GetDirectoryName(modelPath) ?? "", "imagenet_labels.txt"),
            Path.Combine(AppContext.BaseDirectory, "imagenet_labels.txt"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            var lines = File.ReadAllLines(path, Encoding.UTF8)
                            .Select(l => l.Trim().Trim('"', ' '))
                            .Where(l => l.Length > 0)
                            .ToArray();
            if (lines.Length >= 100)  // 最低100行以上あればラベルファイルと判断
                return lines;
        }
        return null;
    }

    private static string[] GenerateFallbackLabels(int count) =>
        Enumerable.Range(0, count).Select(i => $"class_{i}").ToArray();

    // ──────────────────────────────────────────────────────────────
    //  数学ユーティリティ
    // ──────────────────────────────────────────────────────────────

    private static float[] Softmax(float[] logits)
    {
        float max = logits.Max();
        var   exp = logits.Select(v => MathF.Exp(v - max)).ToArray();
        float sum = exp.Sum();
        return exp.Select(v => v / sum).ToArray();
    }

    // ──────────────────────────────────────────────────────────────
    //  IDisposable
    // ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
    }
}
