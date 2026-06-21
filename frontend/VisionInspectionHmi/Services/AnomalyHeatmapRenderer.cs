using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VisionInspectionHmi.Services;

/// <summary>
/// 異常検知の anomaly_map（float 配列）を「異常検知閾値基準」でカラーヒートマップ化し、
/// 元画像へ重畳するレンダラ。
///
/// 旧実装の画像ごと min/max 正規化は、正常画像でも微小差分が強調されて全体が
/// レインボー表示になる課題があったため廃止。代わりに次の方針で描画する:
///   - 閾値未満の画素 … 透明（元画像をそのまま見せる）
///   - 閾値以上の画素 … 異常度に応じて yellow → orange → red、α は 0.45〜0.60
/// これにより正常画像はほぼ原画像のまま、欠陥画像は異常箇所だけが赤系で目立つ。
/// </summary>
public static class AnomalyHeatmapRenderer
{
    // 閾値からどれだけ上回ると最大強調（赤）になるかの幅。
    // EfficientAD の正規化出力（pred_score / anomaly_map が概ね 0.5 近傍）に合わせた既定値。
    private const float DefaultHighlightSpan = 0.008f;

    // 閾値以上の画素の不透明度（0.45〜0.60）。
    private const float MinAlpha = 0.45f; // 閾値ちょうど
    private const float MaxAlpha = 0.60f; // 最大異常

    /// <summary>
    /// anomaly_map を閾値基準のカラーヒートマップ Bitmap（width×height, 32bppArgb）に変換する。
    /// 閾値未満の画素は完全透明（α=0）。
    /// </summary>
    /// <param name="threshold">異常検知閾値（AppSettings.AnomalyThreshold）。これ以上を異常として着色。</param>
    /// <param name="highlightSpan">閾値からの強調幅（threshold + span で赤に飽和）。</param>
    public static Bitmap Render(float[] map, int width, int height, double threshold,
                                float highlightSpan = DefaultHighlightSpan)
    {
        if (map.Length < width * height)
            throw new ArgumentException(
                $"map 長 {map.Length} が {width}×{height} に不足しています。");

        float thr  = (float)threshold;
        float span = highlightSpan <= 1e-6f ? 1e-6f : highlightSpan;

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        int stride = data.Stride;
        var buf = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float v   = map[y * width + x];
                int   idx = y * stride + x * 4;   // BGRA

                if (v < thr)
                {
                    // 閾値未満：透明（元画像をそのまま見せる）
                    buf[idx + 0] = 0; buf[idx + 1] = 0; buf[idx + 2] = 0; buf[idx + 3] = 0;
                    continue;
                }

                // 閾値以上：異常度 t に応じて yellow(0) → orange → red(1)
                float t = Math.Clamp((v - thr) / span, 0f, 1f);
                byte r = 255;
                byte g = (byte)(255f * (1f - t)); // 黄(255,255,0) → 赤(255,0,0)
                byte b = 0;
                byte a = (byte)(255f * (MinAlpha + (MaxAlpha - MinAlpha) * t));

                buf[idx + 0] = b;
                buf[idx + 1] = g;
                buf[idx + 2] = r;
                buf[idx + 3] = a;
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
        bmp.UnlockBits(data);
        return bmp;
    }

    /// <summary>
    /// 元画像にヒートマップを重畳した新しい Bitmap を返す（元画像サイズ）。
    /// 閾値未満は透明なので、正常画像ではほぼ原画像のまま表示される。
    /// </summary>
    public static Bitmap Overlay(Image baseImage, float[] map, int width, int height,
                                 double threshold, float highlightSpan = DefaultHighlightSpan)
    {
        using var heat = Render(map, width, height, threshold, highlightSpan);

        var result = new Bitmap(baseImage.Width, baseImage.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // 背景に元画像
        g.DrawImage(baseImage, 0, 0, result.Width, result.Height);

        // ヒートマップを画素ごとの α で重ねる（透明部はそのまま元画像が見える）
        g.CompositingMode = CompositingMode.SourceOver;
        var dest = new Rectangle(0, 0, result.Width, result.Height);
        g.DrawImage(heat, dest, 0, 0, heat.Width, heat.Height, GraphicsUnit.Pixel);

        return result;
    }
}
