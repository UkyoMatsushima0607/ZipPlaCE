using Alteridem.WinTouch;
using Common;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZipPla
{
    public partial class ViewerForm
    {
        // 色のある画素が見つかったら loop.Stop() で残りの行を打ち切る（V11。Break は前の行をすべて処理し終えるまで止まらない）
        private static unsafe bool IsGrayScaleBitmap(Bitmap bmp)
        {
            if (bmp == null) throw new ArgumentNullException("bmp");
            var pixelFormat = bmp.PixelFormat;
            if ((pixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
            {
                var palette = bmp.Palette;
                if((palette.Flags & 2) == 2)
                {
                    return true;
                }
                else
                {
                    return palette.Entries.All(c => { var r = c.R; return r == c.G && r == c.B; });
                }
            }
            if (pixelFormat == PixelFormat.Format16bppGrayScale) return true;
            if (!(
                pixelFormat == PixelFormat.Format24bppRgb ||
                pixelFormat == PixelFormat.Format32bppRgb ||
                pixelFormat == PixelFormat.Format32bppArgb ||
                pixelFormat == PixelFormat.Format32bppPArgb ||
                pixelFormat == PixelFormat.Format16bppArgb1555 ||
                pixelFormat == PixelFormat.Format16bppRgb555 ||
                pixelFormat == PixelFormat.Format16bppRgb565 ||
                pixelFormat == PixelFormat.Format48bppRgb ||
                pixelFormat == PixelFormat.Format64bppArgb ||
                pixelFormat == PixelFormat.Format64bppPArgb
               ))
            {
                return false;
            }
            var w = bmp.Width;
            var h = bmp.Height;
            var rect = new Rectangle(0, 0, w, h);

            var srcData = bmp.LockBits(rect, ImageLockMode.ReadOnly, pixelFormat);
            try
            {
                var srcScan0 = srcData.Scan0;
                var srcStride = srcData.Stride;
                var bpp = Image.GetPixelFormatSize(pixelFormat);
                var buffInSize = (bpp * w + 7) / 8;
                
                var srcByteAdr = (byte*)srcScan0;
                var srcUShortAdr = (ushort*)srcScan0;
                var srcStrideForUShort = srcStride / 2;
                var srcUIntAdr = (uint*)srcScan0;
                var srcStrideForUInt = srcStride / 4;
                int buffInLength;
                var result = true;
                switch (pixelFormat)
                {
                    case PixelFormat.Format24bppRgb:
                        Parallel.For(0, h,
                            (j, loop) =>
                            {
                                var buffIn = srcByteAdr + j * srcStride;
                                int v = 0;
                                while (v < buffInSize)
                                {
                                    var b = buffIn[v++];
                                    if (b != buffIn[v++] || b != buffIn[v++])
                                    {
                                        result = false;
                                        loop.Stop();
                                        return;
                                    }
                                }
                            });
                        return result;
                    case PixelFormat.Format32bppRgb:
                    case PixelFormat.Format32bppArgb:
                    case PixelFormat.Format32bppPArgb:
                    case PixelFormat_Format32bppCMYK:
                        Parallel.For(0, h,
                            (j, loop) =>
                            {
                                var buffIn = srcByteAdr + j * srcStride;
                                int v = 0;
                                while (v < buffInSize)
                                {
                                    var b = buffIn[v++];
                                    if (b != buffIn[v++] || b != buffIn[v])
                                    {
                                        result = false;
                                        loop.Stop();
                                        return;
                                    }
                                    v += 2;
                                }
                            });
                        return result;
                    case PixelFormat.Format16bppArgb1555:
                        Parallel.For(0, h,
                            (j, loop) =>
                            {
                                var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                for (var v = 0; v < w; v++)
                                {
                                    var s = buffIn[v];
                                    if ((s & 0x8000) != 0)
                                    {
                                        var b = (s << 3 | 4);
                                        if (b != ((0xFF8 & (s >> 2)) | 4) || b != ((0xFF8 & (s >> 7)) | 4))
                                        {
                                            result = false;
                                            loop.Stop();
                                            return;
                                        }
                                    }
                                }
                            });
                        return result;
                    case PixelFormat.Format16bppRgb555:
                        Parallel.For(0, h,
                            (j, loop) =>
                            {
                                var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                for (var v = 0; v < w; v++)
                                {
                                    var s = buffIn[v];
                                    var b = s << 3;
                                    if (b != (0xFF8 & (s >> 2)) || b != (0xFF8 & (s >> 7)))
                                    {
                                        result = false;
                                        loop.Stop();
                                        return;
                                    }
                                }
                            });
                        return result;
                    case PixelFormat.Format16bppRgb565:
                        Parallel.For(0, h,
                            (j, loop) =>
                            {
                                var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                for (var v = 0; v < w; v++)
                                {
                                    var s = buffIn[v];
                                    var b = s << 3;
                                    if (b != (0xFFC & (s >> 3)) || b != (0xFF8 & (s >> 8)))
                                    {
                                        result = false;
                                        loop.Stop();
                                        return;
                                    }
                                }
                            });
                        return result;
                    case PixelFormat.Format48bppRgb:
                        buffInLength = buffInSize >> 1;
                        Parallel.For(0, h,
                            (j, loop) =>
                            {
                                var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                var u = 0;
                                while (u < buffInLength)
                                {
                                    var b = buffIn[u++];
                                    if (b != buffIn[u++] || b != buffIn[u++])
                                    {
                                        result = false;
                                        loop.Stop();
                                        return;
                                    }
                                }
                            });
                        return result;
                    case PixelFormat.Format64bppArgb:
                    case PixelFormat.Format64bppPArgb:
                        buffInLength = buffInSize >> 1;
                        Parallel.For(0, h,
                            (j, loop) =>
                            {
                                var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                int v = 0;
                                while (v < buffInLength)
                                {
                                    var b = buffIn[v++];
                                    if (b != buffIn[v++] || b != buffIn[v]) // 以前は b ではなく高さ h と比べていて、常にカラーと判定していた（B1）
                                    {
                                        result = false;
                                        loop.Stop();
                                        return;
                                    }
                                    v += 2;
                                }
                            });
                        return result;
                    default: return false;
                }
            }
            finally
            {
                bmp.UnlockBits(srcData);
            }
        }

        private const PixelFormat PixelFormat_Format32bppCMYK = (PixelFormat)8207;

        private static Bitmap ToBitmap24Unsafe(Bitmap bmp)
        {
            return ToBitmap24Unsafe(bmp, leaveOpenOriginal: false);
        }

        public static Bitmap ToBitmap24Unsafe(Bitmap bmp, bool leaveOpenOriginal)
        {
            return ToOtehrPixeoFormat(bmp, PixelFormat.Format24bppRgb, leaveOpenOriginal);
        }

        public static unsafe Bitmap ToOtehrPixeoFormat(Bitmap bmp, PixelFormat newPixelFormat, bool leaveOpenOriginal)
        {
            if (bmp == null) throw new ArgumentNullException(nameof(bmp));
            if (bmp.PixelFormat == newPixelFormat) return bmp;
            var w = bmp.Width;
            var h = bmp.Height;
            var rect = new Rectangle(0, 0, w, h);
            var dst = null as Bitmap;
            try
            {
                if (bmp.PixelFormat == PixelFormat.Format32bppArgb && newPixelFormat == PixelFormat.Format24bppRgb)
                {
                    // 透明度付き（RGBA の PNG など）を白の背景に合成して 24bpp にする。GDI+ の DrawImage（非 PArgb の合成）は
                    // 3000x4300 で約 25ms かかるので、行ごとに並列で処理する（C8）。不透明な画素はそのままコピーする
                    dst = new Bitmap(w, h, newPixelFormat);
                    var srcData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        var dstData = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                        try
                        {
                            var srcScan0 = (byte*)srcData.Scan0;
                            var dstScan0 = (byte*)dstData.Scan0;
                            int srcStride = srcData.Stride, dstStride = dstData.Stride;
                            Parallel.For(0, h, y =>
                            {
                                var s = srcScan0 + (long)y * srcStride;
                                var d = dstScan0 + (long)y * dstStride;
                                for (var x = 0; x < w; x++, s += 4, d += 3)
                                {
                                    int a = s[3];
                                    if (a == 255)
                                    {
                                        d[0] = s[0]; d[1] = s[1]; d[2] = s[2];
                                    }
                                    else
                                    {
                                        var white = 255 * (255 - a) + 127;
                                        d[0] = (byte)((s[0] * a + white) / 255);
                                        d[1] = (byte)((s[1] * a + white) / 255);
                                        d[2] = (byte)((s[2] * a + white) / 255);
                                    }
                                }
                            });
                        }
                        finally
                        {
                            dst.UnlockBits(dstData);
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(srcData);
                    }
                }
                else if ((bmp.PixelFormat & PixelFormat.Alpha) == PixelFormat.Alpha)
                {
                    dst = new Bitmap(w, h, newPixelFormat);
                    bmp.SetResolution(dst.HorizontalResolution, dst.VerticalResolution);
                    using (var g = Graphics.FromImage(dst))
                    {
                        g.FillRectangle(Brushes.White, rect);
                        g.DrawImageUnscaled(bmp, 0, 0);
                    }
                }
                else
                {
                    dst = new Bitmap(w, h, newPixelFormat);
                    var dstData = dst.LockBits(rect, ImageLockMode.WriteOnly, newPixelFormat);
                    bmp.SetResolution(dst.HorizontalResolution, dst.VerticalResolution);
                    try
                    {
                        var bmp2 = null as Bitmap;
                        var cmykBmp = null as Bitmap;
                        try
                        {
                            if (bmp.PixelFormat == PixelFormat_Format32bppCMYK)
                            {
                                cmykBmp = new Bitmap(bmp);
                                bmp2 = cmykBmp;
                            }
                            else
                            {
                                bmp2 = bmp;
                            }

                            var srcData = bmp2.LockBits(rect, ImageLockMode.ReadOnly, newPixelFormat);
                            try
                            {
                                CopyMemory(dstData.Scan0, srcData.Scan0, dstData.Stride * dstData.Height);
                            }
                            finally
                            {
                                bmp2.UnlockBits(srcData);
                            }
                        }
                        finally
                        {
                            cmykBmp?.Dispose();
                        }
                    }
                    finally
                    {
                        dst.UnlockBits(dstData);
                    }
                }

                if (!leaveOpenOriginal) bmp.Dispose();
                return dst;
            }
#if DEBUG
            catch (Exception ex)
            {
                var message = ex.ToString();
#else
            catch
            {
#endif
                dst?.Dispose();
                throw;
            }
        }

        private static byte[] ToBitmap24_RGBLookUp;

        private static unsafe Bitmap ToBitmap8Unsafe(Bitmap bmp)
        {
            return ToBitmap8Unsafe(bmp, leaveOpenOriginal: false);
        }

        public static unsafe Bitmap ToBitmap8Unsafe(Bitmap bmp, bool leaveOpenOriginal)
        {
            if (bmp == null) throw new ArgumentNullException(nameof(bmp));
            var pixelFormat = bmp.PixelFormat;
            // GDI+ がデコードした 8bpp のグレーは、パレットが恒等でも Flags に GrayScale が付かないことが多く、作り直していた。
            // Flags は見ずに、256 色の恒等なグレーのパレットならそのまま使う（D10）
            if (pixelFormat == PixelFormat.Format8bppIndexed)
            {
                var entries = bmp.Palette.Entries;
                var ok = entries.Length == 256;
                for (var i = 0; i < entries.Length; i++)
                {
                    var c = entries[i];
                    if (c.A != 255 || c.R != i || c.G != i || c.B != i)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return bmp;
            }
            if (!(
                pixelFormat == PixelFormat.Format24bppRgb ||
                pixelFormat == PixelFormat.Format32bppRgb ||
                pixelFormat == PixelFormat.Format32bppArgb ||
                pixelFormat == PixelFormat.Format32bppPArgb ||
                pixelFormat == PixelFormat.Format16bppArgb1555 ||
                pixelFormat == PixelFormat.Format16bppGrayScale ||
                pixelFormat == PixelFormat.Format16bppRgb555 ||
                pixelFormat == PixelFormat.Format16bppRgb565 ||
                pixelFormat == PixelFormat.Format1bppIndexed ||
                pixelFormat == PixelFormat.Format48bppRgb ||
                pixelFormat == PixelFormat.Format4bppIndexed ||
                pixelFormat == PixelFormat.Format64bppArgb ||
                pixelFormat == PixelFormat.Format64bppPArgb ||
                pixelFormat == PixelFormat.Format8bppIndexed //||
                                                             //pixelFormat == PixelFormat_Format32bppCMYK
               ))
            {
                // これでパフォーマンスが上がるわけでもなく、失敗することが増えるだけ
                // pixelFormat = PixelFormat.Format24bppRgb;


                return ToBitmap24Unsafe(bmp, leaveOpenOriginal); // 8 が無理なら 24 に変換

            }
            var w = bmp.Width;
            var h = bmp.Height;
            var rect = new Rectangle(0, 0, w, h);
            var dst = new Bitmap(w, h, PixelFormat.Format8bppIndexed);
            try
            {
                var dstPallete = dst.Palette;
                var entries = dstPallete.Entries;
                for (var i = 0; i < entries.Length; i++)
                {
                    entries[i] = Color.FromArgb(i, i, i);
                }
                dst.Palette = dstPallete;

                var dstData = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                try
                {
                    var dstScan0 = dstData.Scan0;
                    var dstStride = dstData.Stride;
                    var buffOutSize = w;

                    var srcData = bmp.LockBits(rect, ImageLockMode.ReadOnly, pixelFormat);
                    try
                    {
                        var srcScan0 = srcData.Scan0;
                        var srcStride = srcData.Stride;
                        var bpp = Image.GetPixelFormatSize(pixelFormat);
                        var buffInSize = (bpp * w + 7) / 8;

                        var palette = null as byte[,];
                        if ((pixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
                        {
                            var indexSize = 1 << bpp;
                            var bmpPalette = bmp.Palette.Entries;
                            var stop = Math.Min(indexSize, bmpPalette.Length);
                            palette = new byte[indexSize, 3];
                            for (var i = 0; i < stop; i++)
                            {
                                var c = bmpPalette[i];
                                var a = c.A;
                                var ar = (255 - a) * 255 + 127;
                                palette[i, 0] = (byte)((a * c.B + ar) / 255);
                                palette[i, 1] = (byte)((a * c.G + ar) / 255);
                                palette[i, 2] = (byte)((a * c.R + ar) / 255);
                            }
                        }

                        // 非線形変換（ガンマ 1 / 2.2 に近いが異なる）
                        // 使いまわしているが、毎回生成してもほとんどパフォーマンスに影響はない
                        if (ToBitmap24_RGBLookUp == null && bpp > 32)
                        {
                            using (var sample = new Bitmap(8193 / 3, 1, PixelFormat.Format48bppRgb)) //  8bppGrayScale がないのでカラーを利用
                            {
                                var sampleRect = new Rectangle(0, 0, 8193 / 3, 1);
                                var buff = new short[8193]; for (var i = 0; i < 8193; i++) buff[i] = (short)i;
                                var data = sample.LockBits(sampleRect, ImageLockMode.WriteOnly, PixelFormat.Format48bppRgb);
                                try
                                {
                                    //Marshal.Copy(buff, 0, data.Scan0, 8193);
                                    fixed (short* buffAdr = buff)
                                    {
                                        CopyMemory(data.Scan0, (IntPtr)buffAdr, 8193 * sizeof(short));
                                    }
                                }
                                finally
                                {
                                    sample.UnlockBits(data);
                                }
                                ToBitmap24_RGBLookUp = new byte[8193];
                                data = sample.LockBits(sampleRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                                try
                                {
                                    //Marshal.Copy(data.Scan0, ToBitmap24_RGBLookUp, 0, 8193);
                                    fixed (byte* ToBitmap24_RGBLookUpAdr = ToBitmap24_RGBLookUp)
                                    {
                                        CopyMemory((IntPtr)ToBitmap24_RGBLookUpAdr, data.Scan0, 8193 * sizeof(byte));
                                    }
                                }
                                finally
                                {
                                    sample.UnlockBits(data);
                                }
                            }
                        }

                        var srcByteAdr = (byte*)srcScan0;
                        var srcUShortAdr = (ushort*)srcScan0;
                        var srcStrideForUShort = srcStride / 2;
                        var srcUIntAdr = (uint*)srcScan0;
                        var srcStrideForUInt = srcStride / 4;
                        var dstByteAdr = (byte*)dstScan0;
                        int buffInLength;
                        switch (pixelFormat)
                        {
                            case PixelFormat.Format24bppRgb:
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcByteAdr + j * srcStride;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        int u = 0, v = 0;
                                        while (v < buffInSize)
                                        {
                                            buffOut[u++] = buffIn[v]; v += 3;
                                        }
                                    });
                                break;
                            case PixelFormat.Format32bppRgb:
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcByteAdr + j * srcStride;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        int u = 0, v = 0;
                                        while (v < buffInSize)
                                        {
                                            buffOut[u++] = buffIn[v]; v += 4;
                                        }
                                    });
                                break;
                            case PixelFormat.Format32bppArgb:
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcByteAdr + j * srcStride;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        int u = 0, v = 0;
                                        while (v < buffInSize)
                                        {
                                            var a = (int)buffIn[v + 3];
                                            var ar = (255 - a) * 255 + 127;
                                            buffOut[u++] = (byte)((a * buffIn[v] + ar) / 255); v += 4;

                                        }
                                    });
                                break;
                            case PixelFormat.Format32bppPArgb:
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcByteAdr + j * srcStride;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        int u = 0, v = 0;
                                        while (v < buffInSize)
                                        {
                                            var ar = 255 - buffIn[v + 3];
                                            int value;
                                            value = buffIn[v] + ar; v += 4;
                                            buffOut[u++] = (byte)(value > 255 ? 255 : value);
                                        }
                                    });
                                break;
                            case PixelFormat.Format16bppArgb1555:
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        var u = 0;
                                        for (var v = 0; v < w; v++)
                                        {
                                            var s = buffIn[v];
                                            if ((s & 0x8000) != 0)
                                            {
                                                buffOut[u++] = (byte)(s << 3 | 4);
                                            }
                                            else
                                            {
                                                buffOut[u++] = 255;
                                            }
                                        }
                                    });
                                break;
                            case PixelFormat.Format16bppGrayScale:
                                fixed (byte* l = ToBitmap24_RGBLookUp)
                                {
                                    var lookup = l;
                                    Parallel.For(0, h,
                                        j =>
                                        {
                                            var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                            var buffOut = dstByteAdr + j * dstStride;
                                            var u = 0;
                                            for (var v = 0; v < w; v++)
                                            {
                                                var s = (int)(ushort)buffIn[v];
                                                var value = s <= 0 ? (byte)0 : s >= 8192 ? (byte)255 : lookup[s];
                                                buffOut[u++] = value;
                                            }
                                        });
                                }
                                break;
                            case PixelFormat.Format16bppRgb555:
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        var u = 0;
                                        for (var v = 0; v < w; v++)
                                        {
                                            var s = buffIn[v];
                                            buffOut[u++] = (byte)(s << 3 | 4);
                                        }
                                    });
                                break;
                            case PixelFormat.Format16bppRgb565:
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        var u = 0;
                                        for (var v = 0; v < w; v++)
                                        {
                                            var s = buffIn[v];
                                            buffOut[u++] = (byte)((0xFFC & (s >> 3)) | 2); // ビット数の多い G を採用
                                        }
                                    });
                                break;
                            case PixelFormat.Format1bppIndexed:
                                fixed (byte* p0 = palette)
                                {
                                    var pallet0 = p0;
                                    var remainder = (w & 7);
                                    var eightOdd = remainder != 0;
                                    var stop = w >> 3;
                                    Parallel.For(0, h, j =>
                                    {
                                        var buffIn = srcByteAdr + j * srcStride;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        var u = 0;
                                        for (var v = 0; v < stop; v++)
                                        {
                                            var b = buffIn[v];
                                            var c = pallet0 + 3 * (b >> 7);
                                            buffOut[u++] = c[0];
                                            c = pallet0 + 3 * ((b >> 6) & 1);
                                            buffOut[u++] = c[0];
                                            c = pallet0 + 3 * ((b >> 5) & 1);
                                            buffOut[u++] = c[0];
                                            c = pallet0 + 3 * ((b >> 4) & 1);
                                            buffOut[u++] = c[0];
                                            c = pallet0 + 3 * ((b >> 3) & 1);
                                            buffOut[u++] = c[0];
                                            c = pallet0 + 3 * ((b >> 2) & 1);
                                            buffOut[u++] = c[0];
                                            c = pallet0 + 3 * ((b >> 1) & 1);
                                            buffOut[u++] = c[0];
                                            c = pallet0 + 3 * (b & 1);
                                            buffOut[u++] = c[0];
                                        }
                                        if (eightOdd)
                                        {
                                            var b = buffIn[stop];
                                            var shift = 7;
                                            for (var i = 0; i < remainder; i++)
                                            {
                                                var c = pallet0 + 3 * ((b >> shift--) & 1);
                                                buffOut[u++] = c[0];
                                            }
                                        }
                                    });
                                }
                                break;
                            case PixelFormat.Format48bppRgb:
                                buffInLength = buffInSize >> 1;
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        int u = 0, v = 0;
                                        while (u < buffInLength)
                                        {
                                            int value;
                                            value = buffIn[v]; v += 3;
                                            buffOut[u++] = value <= 0 ? (byte)0 : value >= 8192 ? (byte)255 : ToBitmap24_RGBLookUp[value];
                                        }
                                    });
                                break;
                            case PixelFormat.Format4bppIndexed:
                                fixed (byte* p0 = palette)
                                {
                                    var pallet0 = p0;
                                    var odd = (w & 1) == 1;
                                    var stop = w >> 1;
                                    Parallel.For(0, h, j =>
                                    {
                                        var buffIn = srcByteAdr + j * srcStride;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        var u = 0;
                                        for (var v = 0; v < stop; v++)
                                        {
                                            var b = buffIn[v];
                                            var c = pallet0 + 3 * (b >> 4);
                                            buffOut[u++] = c[0];
                                            c = pallet0 + 3 * (b & 0xF);
                                            buffOut[u++] = c[0];
                                        }
                                        if (odd)
                                        {
                                            var b = buffIn[stop];
                                            var c = pallet0 + 3 * (b >> 4);
                                            buffOut[u++] = c[0];
                                        }
                                    });
                                }
                                break;
                            case PixelFormat.Format64bppArgb:
                                buffInLength = buffInSize >> 1;
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        int u = 0, v = 0;
                                        while (v < buffInLength)
                                        {
                                            var a = (buffIn[v + 3] + 16) >> 5;
                                            var ar = (255 - a) * 255 + 127;
                                            int value;
                                            value = buffIn[v]; v += 4;
                                            value = value <= 0 ? 0 : value >= 8192 ? 255 : ToBitmap24_RGBLookUp[value];
                                            value = (a * value + ar) / 255; // a < 0 になりうるので value も範囲外の可能性がある
                                            buffOut[u++] = (byte)(value <= 0 ? 0 : value >= 255 ? 255 : value);
                                        }
                                    });
                                break;
                            case PixelFormat.Format64bppPArgb:
                                buffInLength = buffInSize >> 1;
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcUShortAdr + j * srcStrideForUShort;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        int u = 0, v = 0;
                                        while (v < buffInLength)
                                        {
                                            var ar = 255 - ((buffIn[v + 3] + 16) >> 5);
                                            int value;
                                            value = buffIn[v]; v += 4;
                                            value = value <= 0 ? 0 : value >= 8192 ? 255 : ToBitmap24_RGBLookUp[value];
                                            value = value + ar; // 過多のチェックは当然。a < 0 の可能性より過小のチェックも必要
                                            buffOut[u++] = (byte)(value <= 0 ? 0 : value >= 255 ? 255 : value);
                                        }
                                    });
                                break;
                            case PixelFormat.Format8bppIndexed:
                                fixed (byte* p0 = palette)
                                {
                                    var pallet0 = p0;
                                    Parallel.For(0, h, j =>
                                    {
                                        var buffIn = srcByteAdr + j * srcStride;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        var u = 0;
                                        for (var v = 0; v < w; v++)
                                        {
                                            var c = pallet0 + 3 * buffIn[v];
                                            buffOut[u++] = c[0];
                                        }
                                    });
                                }
                                break;
                            case PixelFormat_Format32bppCMYK:
                                Parallel.For(0, h,
                                    j =>
                                    {
                                        var buffIn = srcByteAdr + j * srcStride;
                                        var buffOut = dstByteAdr + j * dstStride;
                                        int u = 0, v = 0;
                                        while (v < buffInSize)
                                        {
                                            var c = (int)buffIn[v]; v += 3;
                                            var k = (int)buffIn[v++];

                                            var kr = 255 - k;
                                            buffOut[u++] = (byte)(255 - ((c * kr + 127) / 255 + k));
                                        }
                                    });
                                break;
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(srcData);
                    }
                    if (!leaveOpenOriginal) bmp.Dispose();
                    return dst;
                }
                finally
                {
                    dst.UnlockBits(dstData);
                }
            }
            catch
            {
                dst.Dispose();
                throw;
            }
        }
    }
}
