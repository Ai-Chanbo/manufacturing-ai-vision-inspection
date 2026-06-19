using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VisionInspectionHmi.Models;

namespace VisionInspectionHmi.Services;

/// <summary>
/// EfficientAD 等の異常検知 ONNX を ONNX Runtime で実行する推論サービス。
///
/// 前処理は S4（C# ↔ Python パリティ検証）で一致確認済みの方式をそのまま使用する:
///   RGB / resize(bicubic) / div255 / NCHW（mean/std 正規化なし、入力 256×256）。
///
/// 出力は pred_score（異常スコア）/ pred_label（bool）/ anomaly_map（H×W ヒートマップ）。
/// 判定は「pred_score &gt;= threshold → NG」。threshold は分類用 NgThreshold とは
/// 意味が異なる異常スコア閾値（AnomalyThreshold, 段階3以降で設定画面に追加予定）。
///
/// 段階2時点では anomaly_map は内部取得のみ（InspectionResult への格納・画面表示は
/// 段階3以降）。pred_score / pred_label / anomaly_map が取得できることを確認する目的。
/// </summary>
public sealed class AnomalyInspectionService : IInspectionEngine
{
    private InferenceSession? _session;
    private string _loadedPath = "";
    private string _inputName  = "input";
    private int    _inputSize  = 256;

    // 出力名（モデルに合わせて解決）
    private string? _scoreOutput;
    private string? _labelOutput;
    private string? _mapOutput;

    private bool _disposed;

    // ── IInspectionEngine: モデル情報 ─────────────────────────────
    public InspectionEngineKind Kind => InspectionEngineKind.Anomaly;

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

    public string ModelModeText => "EfficientAD 異常検知";

    // ──────────────────────────────────────────────────────────────
    //  モデル読み込み
    // ──────────────────────────────────────────────────────────────

    public void LoadModel(string modelPath)
    {
        if (modelPath == _loadedPath && _session != null) return;

        _session?.Dispose();
        _session     = null;
        _loadedPath  = "";
        _scoreOutput = _labelOutput = _mapOutput = null;

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"ONNXモデルが見つかりません: {modelPath}");

        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _session    = new InferenceSession(modelPath, opts);
        _loadedPath = modelPath;

        // 入力名・入力サイズの解決（NCHW を想定し H を採用）
        var inMeta = _session.InputMetadata.FirstOrDefault();
        if (!string.IsNullOrEmpty(inMeta.Key)) _inputName = inMeta.Key;
        var dims = inMeta.Value?.Dimensions;
        if (dims is { Length: 4 } && dims[2] > 0) _inputSize = dims[2];

        // 出力名の解決（pred_score / pred_label / anomaly_map）
        var outKeys = _session.OutputMetadata.Keys.ToList();
        _scoreOutput = outKeys.FirstOrDefault(k => k.Equals("pred_score", StringComparison.OrdinalIgnoreCase))
                       ?? outKeys.FirstOrDefault(k => k.Contains("score", StringComparison.OrdinalIgnoreCase));
        _labelOutput = outKeys.FirstOrDefault(k => k.Equals("pred_label", StringComparison.OrdinalIgnoreCase))
                       ?? outKeys.FirstOrDefault(k => k.Contains("label", StringComparison.OrdinalIgnoreCase));
        _mapOutput   = outKeys.FirstOrDefault(k => k.Equals("anomaly_map", StringComparison.OrdinalIgnoreCase))
                       ?? outKeys.FirstOrDefault(k => k.Contains("map", StringComparison.OrdinalIgnoreCase)
                                                     && !k.Contains("mask", StringComparison.OrdinalIgnoreCase));

        if (_scoreOutput == null)
            throw new InvalidOperationException(
                "異常検知モデルに pred_score 相当の出力が見つかりません。");
    }

    // ──────────────────────────────────────────────────────────────
    //  推論
    // ──────────────────────────────────────────────────────────────

    public Task<InspectionResult> InspectAsync(string imagePath, double threshold)
    {
        if (_session is null)
            throw new InvalidOperationException(
                "モデルが読み込まれていません。LoadModel() を先に呼び出してください。");
        return Task.Run(() => RunInference(imagePath, threshold));
    }

    private InspectionResult RunInference(string imagePath, double threshold)
    {
        var sw = Stopwatch.StartNew();

        var tensor = Preprocess(imagePath, _inputSize);
        var dense  = new DenseTensor<float>(tensor, new[] { 1, 3, _inputSize, _inputSize });
        var inputs = new List<NamedOnnxValue>
            { NamedOnnxValue.CreateFromTensor(_inputName, dense) };

        using var outputs = _session!.Run(inputs);
        sw.Stop();

        // pred_score
        double predScore = 0;
        var scoreVal = outputs.FirstOrDefault(o => o.Name == _scoreOutput);
        if (scoreVal != null) predScore = scoreVal.AsEnumerable<float>().First();

        // pred_label（bool。無い場合は threshold 判定にフォールバック）
        int? predLabel = null;
        if (_labelOutput != null)
        {
            var labelVal = outputs.FirstOrDefault(o => o.Name == _labelOutput);
            if (labelVal != null)
            {
                try { predLabel = labelVal.AsEnumerable<bool>().First() ? 1 : 0; }
                catch { /* bool 以外の型なら無視 */ }
            }
        }

        // anomaly_map（取得確認。段階2では統計のみ使用）
        double mapMax = double.NaN;
        if (_mapOutput != null)
        {
            var mapVal = outputs.FirstOrDefault(o => o.Name == _mapOutput);
            if (mapVal != null)
            {
                float max = float.NegativeInfinity;
                foreach (var v in mapVal.AsEnumerable<float>())
                    if (v > max) max = v;
                mapMax = max;
            }
        }

        bool isNg = predScore >= threshold;
        string mapInfo = double.IsNaN(mapMax) ? "" : $" / map_max={mapMax:F4}";
        string labelInfo = predLabel.HasValue ? $" (model_label={predLabel.Value})" : "";

        return new InspectionResult
        {
            Result      = isNg ? "NG" : "OK",
            Score       = Math.Round(predScore, 4),
            DefectType  = isNg ? "anomaly" : "none",
            ClassName   = ModelModeText,
            Message     = isNg
                            ? $"異常を検出しました{labelInfo}{mapInfo}"
                            : $"異常は検出されませんでした{labelInfo}{mapInfo}",
            InferenceMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
            // Top5Candidates は異常検知では生成しない（空のまま）
        };
    }

    // ──────────────────────────────────────────────────────────────
    //  前処理: Bitmap → NCHW float32（RGB / bicubic / div255 / 正規化なし）
    //  S4 (OnnxParityCheck) と同一実装。
    // ──────────────────────────────────────────────────────────────

    private static float[] Preprocess(string imagePath, int size)
    {
        using var src     = new Bitmap(imagePath);
        using var resized = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; // PIL 既定(BICUBIC)に対応
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, size, size);
        }

        var bmpData = resized.LockBits(
            new Rectangle(0, 0, size, size),
            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        int stride  = bmpData.Stride;
        var rawData = new byte[stride * size];
        System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, rawData, 0, rawData.Length);
        resized.UnlockBits(bmpData);

        int plane  = size * size;
        var tensor = new float[3 * plane];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int rawIdx = y * stride + x * 3;   // Format24bppRgb は BGR 順
                int idx    = y * size + x;
                tensor[0 * plane + idx] = rawData[rawIdx + 2] / 255f; // R
                tensor[1 * plane + idx] = rawData[rawIdx + 1] / 255f; // G
                tensor[2 * plane + idx] = rawData[rawIdx + 0] / 255f; // B
            }
        }
        return tensor;
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
