using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VisionInspectionHmi.Services;

/// <summary>
/// 異常検知の anomaly_map（float 配列）をカラーヒートマップ化し、
/// 元画像へ重畳（αブレンド）するレンダラ。
///
/// 配色は jet（低=青 → 高=赤）。値は画像ごとの min/max で正規化し、
/// 異常領域（高スコア）のコントラストを最大化する。
/// </summary>
public static class AnomalyHeatmapRenderer
{
    /// <summary>
    /// anomaly_map をカラーヒートマップ Bitmap（width×height, 32bppArgb）に変換する。
    /// </summary>
    public static Bitmap Render(float[] map, int width, int height)
    {
        if (map.Length < width * height)
            throw new ArgumentException(
                $"map 長 {map.Length} が {width}×{height} に不足しています。");

        // 画像ごとの min/max で正規化
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        for (int i = 0; i < width * height; i++)
        {
            float v = map[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }
        float range = max - min;
        if (range <= 1e-12f) range = 1f; // ほぼ平坦なら 0 除算回避

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        int stride = data.Stride;
        var buf = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float t = (map[y * width + x] - min) / range; // [0,1]
                var (r, g, b) = Jet(t);
                int idx = y * stride + x * 4;   // BGRA
                buf[idx + 0] = b;
                buf[idx + 1] = g;
                buf[idx + 2] = r;
                buf[idx + 3] = 255;
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
        bmp.UnlockBits(data);
        return bmp;
    }

    /// <summary>
    /// 元画像にヒートマップを α 合成した新しい Bitmap を返す（元画像サイズ）。
    /// heatmap は元画像サイズへ拡大して重畳する。
    /// </summary>
    public static Bitmap Overlay(Image baseImage, float[] map, int width, int height, float alpha = 0.5f)
    {
        using var heat = Render(map, width, height);

        var result = new Bitmap(baseImage.Width, baseImage.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // 背景に元画像
        g.DrawImage(baseImage, 0, 0, result.Width, result.Height);

        // ヒートマップを alpha で重ねる
        var cm = new ColorMatrix { Matrix33 = Math.Clamp(alpha, 0f, 1f) };
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

        var dest = new Rectangle(0, 0, result.Width, result.Height);
        g.DrawImage(heat, dest, 0, 0, heat.Width, heat.Height, GraphicsUnit.Pixel, ia);

        return result;
    }

    // jet カラーマップ: t∈[0,1] → RGB。低=青 / 中=緑 / 高=赤。
    private static (byte r, byte g, byte b) Jet(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float r = Math.Clamp(1.5f - MathF.Abs(4f * t - 3f), 0f, 1f);
        float g = Math.Clamp(1.5f - MathF.Abs(4f * t - 2f), 0f, 1f);
        float b = Math.Clamp(1.5f - MathF.Abs(4f * t - 1f), 0f, 1f);
        return ((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f));
    }
}
