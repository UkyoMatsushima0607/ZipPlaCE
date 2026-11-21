using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace ZipPla
{
    /// <summary>
    /// GDI+ を使わない面積平均の縮小（P4）。サムネイルの切り抜き（DoJustClipping）と注目点の計算（BitmapAnalyzer.GetFocus）で使う。
    /// GDI+ の Graphics.DrawImage はプロセス全体で直列に実行され、サムネイルを複数のスレッドで作っても縮小が並列にならなかった
    /// （297x426 → 200x200 が 1 回 2 ms で、4 スレッドでも同じ）。縮小（拡大を含まない）のときだけ使う
    /// </summary>
    public static class AreaAverageResizer
    {
        /// <summary>src の srcRect の部分を dst（24bpp）の dstRect に面積平均で縮小して描く。縮小でなければ false（何もしない）</summary>
        public static unsafe bool TryDraw(Bitmap src, RectangleF srcRect, Bitmap dst, Rectangle dstRect)
        {
            if (dst.PixelFormat != PixelFormat.Format24bppRgb) return false;
            if (dstRect.Width <= 0 || dstRect.Height <= 0 || srcRect.Width <= 0 || srcRect.Height <= 0) return false;
            var sx = srcRect.Width / dstRect.Width;
            var sy = srcRect.Height / dstRect.Height;
            if (sx < 1 || sy < 1) return false; // 拡大は従来どおり GDI+ で
            var srcPf = src.PixelFormat;
            int srcBpp;
            if (srcPf == PixelFormat.Format24bppRgb) srcBpp = 3;
            else if (srcPf == PixelFormat.Format32bppRgb || srcPf == PixelFormat.Format32bppArgb || srcPf == PixelFormat.Format32bppPArgb) srcBpp = 4;
            else return false;
            if (srcBpp == 4 && srcPf != PixelFormat.Format32bppRgb && !IsOpaque(src)) return false; // 透明な画素があれば従来どおり

            // srcRect が元画像の外にはみ出す場合は、はみ出した分を端の画素で埋めたものとして扱う
            var sw = src.Width;
            var sh = src.Height;
            var dw = dstRect.Width;
            var dh = dstRect.Height;
            var x0 = srcRect.X;
            var y0 = srcRect.Y;
            var rowStart = Math.Max(0, Math.Min(sh - 1, (int)Math.Floor(y0)));
            var rowEnd = Math.Max(0, Math.Min(sh - 1, (int)Math.Ceiling(y0 + srcRect.Height) - 1));

            var sData = src.LockBits(new Rectangle(0, 0, sw, sh), ImageLockMode.ReadOnly, srcPf);
            try
            {
                // 横方向: 必要な行ごとに dw 個の画素へ
                var rows = rowEnd - rowStart + 1;
                var hBuf = new float[rows * dw * 3];
                var sStride = sData.Stride;
                for (var r = 0; r < rows; r++)
                {
                    var srow = (byte*)sData.Scan0 + (long)(rowStart + r) * sStride;
                    var o = r * dw * 3;
                    for (var x = 0; x < dw; x++)
                    {
                        var a = x0 + x * sx;
                        var b = a + sx;
                        float s0 = 0, s1 = 0, s2 = 0;
                        var i0 = (int)Math.Floor(a);
                        var i1 = (int)Math.Ceiling(b) - 1;
                        for (var i = i0; i <= i1; i++)
                        {
                            var w = Math.Min(b, i + 1) - Math.Max(a, i);
                            if (w <= 0) continue;
                            var ci = i < 0 ? 0 : i >= sw ? sw - 1 : i;
                            var p = srow + ci * srcBpp;
                            s0 += p[0] * w; s1 += p[1] * w; s2 += p[2] * w;
                        }
                        hBuf[o + x * 3] = s0 / sx;
                        hBuf[o + x * 3 + 1] = s1 / sx;
                        hBuf[o + x * 3 + 2] = s2 / sx;
                    }
                }

                // 縦方向
                var dData = dst.LockBits(dstRect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try
                {
                    for (var y = 0; y < dh; y++)
                    {
                        var a = y0 + y * sy;
                        var b = a + sy;
                        var j0 = (int)Math.Floor(a);
                        var j1 = (int)Math.Ceiling(b) - 1;
                        var drow = (byte*)dData.Scan0 + (long)y * dData.Stride;
                        for (var x = 0; x < dw; x++)
                        {
                            float s0 = 0, s1 = 0, s2 = 0;
                            for (var j = j0; j <= j1; j++)
                            {
                                var w = Math.Min(b, j + 1) - Math.Max(a, j);
                                if (w <= 0) continue;
                                var cj = j < 0 ? 0 : j >= sh ? sh - 1 : j;
                                var r = cj - rowStart;
                                if (r < 0) r = 0; else if (r >= rows) r = rows - 1;
                                var o = (r * dw + x) * 3;
                                s0 += hBuf[o] * w; s1 += hBuf[o + 1] * w; s2 += hBuf[o + 2] * w;
                            }
                            drow[x * 3] = ToByte(s0 / sy);
                            drow[x * 3 + 1] = ToByte(s1 / sy);
                            drow[x * 3 + 2] = ToByte(s2 / sy);
                        }
                    }
                }
                finally
                {
                    dst.UnlockBits(dData);
                }
            }
            finally
            {
                src.UnlockBits(sData);
            }
            return true;
        }

        private static byte ToByte(float v)
        {
            var i = (int)(v + 0.5f);
            return (byte)(i < 0 ? 0 : i > 255 ? 255 : i);
        }

        private static unsafe bool IsOpaque(Bitmap src)
        {
            var data = src.LockBits(new Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, src.PixelFormat);
            try
            {
                for (var y = 0; y < data.Height; y++)
                {
                    var row = (byte*)data.Scan0 + (long)y * data.Stride;
                    for (var x = 3; x < data.Width * 4; x += 4) if (row[x] != 255) return false;
                }
                return true;
            }
            finally
            {
                src.UnlockBits(data);
            }
        }
    }
}
