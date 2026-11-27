using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZipPla
{
    public struct DihedralFraction
    {
        private int numerator;
        private int denominator;

        public int SignedDenominator { get { return denominator; } }

        public DihedralFraction(int numerator, int denominator)
        {
            this.numerator = numerator;
            this.denominator = denominator;
            reduction();
        }

        public DihedralFraction(int numerator, int denominator, bool reflection)
            : this(numerator, reflection ? -denominator : denominator) { }

        public static readonly DihedralFraction Identity = new DihedralFraction { numerator = 0, denominator = 1 };

        public static DihedralFraction operator *(DihedralFraction a, DihedralFraction b)
        {
            a.fix();
            b.fix();
            var result = new DihedralFraction
            {
                numerator = a.numerator * Math.Abs(b.denominator) + b.numerator * a.denominator,
                denominator = a.denominator * b.denominator
            };
            result.reduction();
            return result;
        }

        public static DihedralFraction operator /(DihedralFraction a, DihedralFraction b)
        {
            a.fix();
            b.fix();
            var result = new DihedralFraction
            {
                numerator = Math.Sign(b.denominator) * (a.numerator * b.denominator - b.numerator * a.denominator),
                denominator = a.denominator * b.denominator
            };
            result.reduction();
            return result;
        }

        public static bool operator ==(DihedralFraction a, DihedralFraction b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(DihedralFraction a, DihedralFraction b)
        {
            return !a.Equals(b);
        }

        public override bool Equals(object obj)
        {
            if (obj is DihedralFraction)
            {
                return Equals((DihedralFraction)obj);
            }
            else
            {
                return false;
            }
        }
        
        public bool Equals(DihedralFraction other)
        {
            fix();
            other.fix();
            return numerator == other.numerator && denominator == other.denominator;
        }

        public override int GetHashCode()
        {
            return numerator ^ denominator;
        }

        private void reduction()
        {
            var g = Math.Abs(gcd(numerator, denominator));
            numerator /= g;
            denominator /= g;
            numerator %= denominator;
            if (numerator < 0) numerator += denominator;
        }

        public override string ToString()
        {
            fix();
            return denominator > 0 ? $"R{numerator} / D{denominator}" : $"S{numerator} / D{-denominator}";
        }

        private void fix()
        {
            if (denominator == 0) denominator = 1;
        }

        private static int gcd(int a, int b)
        {
            while (a != 0)
            {
                var temp = a;
                a = b % a;
                b = temp;
            }
            return b;
        }
    }

    public class ViewerFormImageFilter
    {
        public double RotationAngle = 0;
        public bool ToneCurveEnabled = false;
        //public bool MoireRemoverEnabled = false;
        //public PixelValueConversion PixelValueConversion = PixelValueConversion.None;

        //private static string PseudoColoringToneCurvesFile = Path.Combine(Application.StartupPath, "PseudoColoring.cur");
        private static string PseudoColoringToneCurvesFile = Path.Combine(Application.StartupPath, "ToneCurve.cur");

        public bool PreFilterExists() // プロパティにすると Config に保存される可能性
        {
            return RotationAngle != 0;
            //return !PseudoColoringEnabled && RotationAngle == 0;// && PixelValueConversion == PixelValueConversion.None;
        }
        public bool PostFilterExists() // プロパティにすると Config に保存される可能性
        {
            return ToneCurveEnabled;// || MoireRemoverEnabled;
            //return !PseudoColoringEnabled && RotationAngle == 0;// && PixelValueConversion == PixelValueConversion.None;
        }


        public bool NeedToCheckWhetherColorOrNot()
        {
            return ToneCurveEnabled;// || MoireRemoverEnabled;
        }

        public void Set(ViewerFormImageFilter other)
        {
            if (other == null)
            {
                RotationAngle = 0;
                ToneCurveEnabled = false;
                //MoireRemoverEnabled = false;
               // PixelValueConversion = PixelValueConversion.None;
            }
            else
            {
                RotationAngle = other.RotationAngle;
                ToneCurveEnabled = other.ToneCurveEnabled;
                //MoireRemoverEnabled = other.MoireRemoverEnabled;
               // PixelValueConversion = other.PixelValueConversion;
            }
        }

        public static Bitmap Rotate(Bitmap bmp, DihedralFraction rotate)
        {
            if (rotate == new DihedralFraction())
            {
                return bmp;
            }
            var data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                rotate == new DihedralFraction(1, 4) || rotate == new DihedralFraction(3, 4) ? ImageLockMode.ReadOnly : ImageLockMode.ReadWrite,
                bmp.PixelFormat);
            try
            {
                Rotate(ref bmp, ref data, rotate);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        private static void Rotate(ref Bitmap bmp, ref BitmapData data, DihedralFraction rotate)
        {
            if (rotate == new DihedralFraction())
            {
                return;
            }
            bool is90;
            if ((is90 = rotate == new DihedralFraction(3, 4)) || rotate == new DihedralFraction(1, 4))
            {
                Bitmap outBitmap;
                BitmapData outData;
                Rotate(data, is90 ? 90 : 270, out outBitmap, out outData);
                if (data != outData)
                {
                    bmp.UnlockBits(data);
                    if (outBitmap.PixelFormat == PixelFormat.Format8bppIndexed)
                    {
                        outBitmap.Palette = bmp.Palette;
                    }
                    bmp.Dispose();
                    data = outData;
                    bmp = outBitmap;
                }
                return;
            }
            if (rotate == new DihedralFraction(0, -4))
            {
                VerticalReflection(data);
                return;
            }
            if (rotate == new DihedralFraction(2, 4))
            {
                Rotate180(data);
                return;
            }
            if (rotate == new DihedralFraction(1, -4))
            {
                VerticalReflection(data);
                Rotate(ref bmp, ref data, new DihedralFraction(3, 4));
                return;
            }
            if (rotate == new DihedralFraction(2, -4))
            {
                Rotate180(data);
                VerticalReflection(data);
                return;
            }
            if (rotate == new DihedralFraction(3, -4))
            {
                VerticalReflection(data);
                Rotate(ref bmp, ref data, new DihedralFraction(1, 4));
                return;
            }
        }
        
        public static DihedralFraction GetOrientation(int orientation)
        {
            switch (orientation)
            {
                case 2: return new DihedralFraction(0, -4); // S0
                case 3: return new DihedralFraction(2, 4); // R2
                case 4: return new DihedralFraction(2, -4); // S2
                case 5: return new DihedralFraction(3, -4); // S0 * R1 = S3
                case 6: return new DihedralFraction(3, 4); // R3
                case 7: return new DihedralFraction(1, -4); // S0 * R3 = S1
                case 8: return new DihedralFraction(1, 4); // R1
                default: return new DihedralFraction();
            }
        }


        private static DihedralFraction GetOrientation(double angle)
        {
            return
                angle == 90 ? new DihedralFraction(3, 4) :
                angle == 180 ? new DihedralFraction(2, 4) :
                angle == 270 ? new DihedralFraction(1, 4) :
                new DihedralFraction();
        }

        public Bitmap PreFilter(Bitmap src)//, bool? isColorImage)
        {
            var rotate = GetOrientation(RotationAngle);
            return Rotate(src, rotate);
                
            //}
            //return src;
        }

        /// <summary>
        /// PostFilter が画素を書き換えずパレットだけを変えるか（8bpp で色の差が分かっているグレー画像）。
        /// パレットは GDI+ のオブジェクトごとに持つので、そのときは原寸と画素を共有する複製で足りる（V13）
        /// </summary>
        public bool PostFilterChangesOnlyPalette(PixelFormat pixelFormat, int? maxColorDiff)
        {
            return ToneCurveEnabled && maxColorDiff != null && maxColorDiff <= GrayScaleThreshold && pixelFormat == PixelFormat.Format8bppIndexed;
        }

        public void PostFilter(Bitmap src, ref int? maxColorDiff, BackgroundWorker bw = null)
        {
            PostFilter(src, new Rectangle(0, 0, src.Width, src.Height), ref maxColorDiff, bw);
        }
        public void PostFilter(Bitmap src, Rectangle rect, ref int? maxColorDiff, BackgroundWorker bw = null)
        {
            if ((ToneCurveEnabled /*|| MoireRemoverEnabled*/) && (maxColorDiff == null || maxColorDiff <= GrayScaleThreshold))
            {
                if (maxColorDiff != null && src.PixelFormat == PixelFormat.Format8bppIndexed)
                {
                    ApplayColorTransformTo8bpp(src);
                }
                else
                {
                    var changePalette = false;
                    var data = src.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
                    try
                    {
                        // 他の PostFilter が追加されなければ if は不要
                        //if (PseudoColoringEnabled && (maxColorDiff == null || maxColorDiff <= GrayScaleThreshold))

                        if (maxColorDiff == null)
                        {
                            var result = GetMaxColorDiffProperly(data, bw);
                            if (result == null) return; // キャンセルがかかった場合
                            maxColorDiff = result;
                        }

                        if (maxColorDiff <= GrayScaleThreshold)
                        {

                            if (ToneCurveEnabled)
                            {
                                if (src.PixelFormat == PixelFormat.Format8bppIndexed)
                                {
                                    changePalette = true;
                                }
                                else
                                {
                                    var toneCurve = TryGetPseudoColoringToneCurves();
                                    using (var painter = toneCurve == null ? new PseudoColoringPainter() : new PseudoColoringPainter(toneCurve))
                                    {
                                        var transform = /*MoireRemoverEnabled ||*/ maxColorDiff == 0 ? painter.ColorTransformForGrayScale : painter.ColorTransform;
                                        if (bw == null)
                                        {
                                            ApplyColorTransform(transform, data);
                                        }
                                        else
                                        {
                                            ApplyColorTransform(transform, data, bw);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        src.UnlockBits(data);
                    }
                    if(changePalette)
                    {
                        ApplayColorTransformTo8bpp(src);
                    }
                }
            }
        }

        private static void ApplayColorTransformTo8bpp(Bitmap bmp)
        {
            var palette = bmp.Palette;
            var entries = palette.Entries;
            var toneCurve = TryGetPseudoColoringToneCurves();
            using (var painter = toneCurve == null ? new PseudoColoringPainter() : new PseudoColoringPainter(toneCurve))
            {
                var transform = painter.ColorTransformForGrayScale;
                var canvas = new byte[3];
                unsafe
                {
                    fixed (byte* p = canvas)
                    {
                        for (var i = 0; i < entries.Length; i++)
                        {
                            p[0] = (byte)i;
                            transform(p);
                            entries[i] = Color.FromArgb(p[2], p[1], p[0]);
                        }
                    }
                }
            }
            bmp.Palette = palette;
        }

        public static int? TryGetMaxColorDiff(Bitmap bmp)
        {
            var pixelFormat = bmp.PixelFormat;
            if (pixelFormat == PixelFormat.Format16bppGrayScale) return 0;

            if ((pixelFormat & PixelFormat.Indexed) != PixelFormat.Indexed) return null;

            const int Flag_IsGrayScale = 0x00000002;
            var palette = bmp.Palette;
            if ((palette.Flags & Flag_IsGrayScale) == Flag_IsGrayScale) return 0;
            
            var bpp = Image.GetPixelFormatSize(pixelFormat);
            var indexSize = 1 << bpp;
            var bmpPalette = palette.Entries;
            var stop = Math.Min(indexSize, bmpPalette.Length);
            var dMax = 0;
            for (var i = 0; i < stop; i++)
            {
                var c = bmpPalette[i];
                var d = getMaxDiff(c.R, c.G, c.B);
                if (d > GrayScaleThreshold)
                {
                    return d;
                }
                if (d > dMax)
                {
                    dMax = d;
                }
            }
            return dMax;
        }

        // トーンカーブのファイル（ToneCurve.cur）の解析結果を、更新日時とサイズをキーに使い回す（V12）。
        // 以前はページを読み込むたび・ルーペを作るたびにファイルを解析していた。ToneCurves は読み取り専用なので共有してよい
        private static readonly object TryGetPseudoColoringToneCurves_Locker = new object();
        private static ToneCurves TryGetPseudoColoringToneCurves_Cache = null;
        private static DateTime TryGetPseudoColoringToneCurves_CacheTime;
        private static long TryGetPseudoColoringToneCurves_CacheLength = -1; // -1: キャッシュなし
        private static ToneCurves TryGetPseudoColoringToneCurves()
        {
            try
            {
                var info = new FileInfo(PseudoColoringToneCurvesFile);
                if (!info.Exists) return null;
                var time = info.LastWriteTimeUtc;
                var length = info.Length;
                lock (TryGetPseudoColoringToneCurves_Locker)
                {
                    if (TryGetPseudoColoringToneCurves_CacheLength == length && TryGetPseudoColoringToneCurves_CacheTime == time)
                    {
                        return TryGetPseudoColoringToneCurves_Cache;
                    }
                    ToneCurves result = null;
                    try
                    {
                        result = ToneCurves.FromFile(PseudoColoringToneCurvesFile);
                    }
                    catch { } // 解析できないファイルも、変わるまでは読み直さない（null を覚える）
                    TryGetPseudoColoringToneCurves_Cache = result;
                    TryGetPseudoColoringToneCurves_CacheTime = time;
                    TryGetPseudoColoringToneCurves_CacheLength = length;
                    return result;
                }
            }
            catch { }
            return null;
        }

        public static bool TestGetToneCorveFile()
        {
            try
            {
                if (File.Exists(PseudoColoringToneCurvesFile))
                {
                    return ToneCurves.FromFileTest(PseudoColoringToneCurvesFile);
                }
            }
            catch { }
            return false;
        }
        
        private static void Rotate(BitmapData data, double angle, out Bitmap outBitmap, out BitmapData outData)
        {
            if (data.PixelFormat == PixelFormat.Format24bppRgb)
            {
                outBitmap = new Bitmap(data.Height, data.Width, PixelFormat.Format24bppRgb);
                try
                {
                    outData = outBitmap.LockBits(new Rectangle(Point.Empty, outBitmap.Size), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
                    try
                    {
                        unsafe
                        {
                            var inScan0 = (byte*)data.Scan0;
                            var inStride = data.Stride;
                            var outScan0 = (byte*)outData.Scan0;
                            var outStride = outData.Stride;
                            var w = outData.Width;
                            if (angle == 90)
                            {
                                var w1 = w - 1;
                                Parallel.For(0, outData.Height, y =>
                                {
                                    var outScan = outScan0 + y * outStride;
                                    var inScan = inScan0 + y * 3;
                                    for (var x = 0; x < w; x++)
                                    {
                                        var outP = outScan + x * 3;
                                        var inP = inScan + (w1 - x) * inStride;
                                        outP[0] = inP[0];
                                        outP[1] = inP[1];
                                        outP[2] = inP[2];
                                    }
                                });
                            }
                            else
                            {
                                var h1 = outData.Height - 1;
                                Parallel.For(0, outData.Height, y =>
                                {
                                    var outScan = outScan0 + y * outStride;
                                    var inScan = inScan0 + (h1 - y) * 3;
                                    for (var x = 0; x < w; x++)
                                    {
                                        var outP = outScan + x * 3;
                                        var inP = inScan + x * inStride;
                                        outP[0] = inP[0];
                                        outP[1] = inP[1];
                                        outP[2] = inP[2];
                                    }
                                });
                            }
                        }
                    }
                    catch
                    {
                        outBitmap.UnlockBits(outData);
                        throw;
                    }
                }
                catch
                {
                    outBitmap.Dispose();
                    throw;
                }
            }
            else if (data.PixelFormat == PixelFormat.Format8bppIndexed)
            {
                outBitmap = new Bitmap(data.Height, data.Width, PixelFormat.Format8bppIndexed);
                try
                {
                    outData = outBitmap.LockBits(new Rectangle(Point.Empty, outBitmap.Size), ImageLockMode.ReadWrite, PixelFormat.Format8bppIndexed);
                    try
                    {
                        unsafe
                        {
                            var inScan0 = (byte*)data.Scan0;
                            var inStride = data.Stride;
                            var outScan0 = (byte*)outData.Scan0;
                            var outStride = outData.Stride;
                            var w = outData.Width;
                            if (angle == 90)
                            {
                                var w1 = w - 1;
                                Parallel.For(0, outData.Height, y =>
                                {
                                    var outScan = outScan0 + y * outStride;
                                    var inScan = inScan0 + y;
                                    for (var x = 0; x < w; x++)
                                    {
                                        var outP = outScan + x;
                                        var inP = inScan + (w1 - x) * inStride;
                                        outP[0] = inP[0];
                                    }
                                });
                            }
                            else
                            {
                                var h1 = outData.Height - 1;
                                Parallel.For(0, outData.Height, y =>
                                {
                                    var outScan = outScan0 + y * outStride;
                                    var inScan = inScan0 + (h1 - y);
                                    for (var x = 0; x < w; x++)
                                    {
                                        var outP = outScan + x;
                                        var inP = inScan + x * inStride;
                                        outP[0] = inP[0];
                                    }
                                });
                            }
                        }
                    }
                    catch
                    {
                        outBitmap.UnlockBits(outData);
                        throw;
                    }
                }
                catch
                {
                    outBitmap.Dispose();
                    throw;
                }
            }
            else throw new ArgumentException();
        }


        private static void VerticalReflection(BitmapData data)
        {
            var scan0 = data.Scan0;
            var stride = data.Stride;
            var height = data.Height;
            var height1 = height - 1;
            Parallel.For(0, height1 / 2, () => Marshal.AllocCoTaskMem(stride), (y, loop, pointer) =>
            {
                var a = scan0 + y * stride;
                var b = scan0 + (height1 - y) * stride;
                NativeMethods.CopyMemory(pointer, a, stride);
                NativeMethods.CopyMemory(a, b, stride);
                NativeMethods.CopyMemory(b, pointer, stride);
                return pointer;
            }, pointer => Marshal.FreeCoTaskMem(pointer));

        }

        private static void Rotate180(BitmapData data)
        {
            unsafe
            {
                var scan0 = (byte*)data.Scan0;
                var stride = data.Stride;
                if (data.PixelFormat == PixelFormat.Format24bppRgb)
                {
                    var w13 = (data.Width - 1) * 3;
                    var h1Stride = (data.Height - 1) * stride;
                    var odd = (data.Width & 1) == 1;
                    var wHalf3 = (data.Width >> 1) * 3; // 切り下げ
                    var hHalf = (data.Height + 1) >> 1; // 切り上げ
                    Parallel.For(0, hHalf, y =>
                    {
                        var yStride = y * stride;
                        var scanU = scan0 + yStride;
                        var scanL = scan0 + h1Stride - yStride;
                        for (var x3 = 0; x3 < wHalf3; x3 += 3)
                        {
                            var xo = w13 - x3;
                            byte* pA, pB;
                            byte temp;

                            pA = scanU + x3;
                            pB = scanL + xo;
                            temp = pA[0]; pA[0] = pB[0]; pB[0] = temp;
                            temp = pA[1]; pA[1] = pB[1]; pB[1] = temp;
                            temp = pA[2]; pA[2] = pB[2]; pB[2] = temp;

                            pA = scanU + xo;
                            pB = scanL + x3;
                            temp = pA[0]; pA[0] = pB[0]; pB[0] = temp;
                            temp = pA[1]; pA[1] = pB[1]; pB[1] = temp;
                            temp = pA[2]; pA[2] = pB[2]; pB[2] = temp;
                        }
                        if (odd)
                        {
                            var pA = scanU + wHalf3;
                            var pB = scanL + wHalf3;
                            byte temp;
                            temp = pA[0]; pA[0] = pB[0]; pB[0] = temp;
                            temp = pA[1]; pA[1] = pB[1]; pB[1] = temp;
                            temp = pA[2]; pA[2] = pB[2]; pB[2] = temp;
                        }
                    });
                }
                else if (data.PixelFormat == PixelFormat.Format8bppIndexed)
                {
                    var w1 = data.Width - 1;
                    var h1Stride = (data.Height - 1) * stride;
                    var odd = (data.Width & 1) == 1;
                    var wHalf = data.Width >> 1; // 切り下げ
                    var hHalf = (data.Height + 1) >> 1; // 切り上げ
                    Parallel.For(0, hHalf, y =>
                    {
                        var yStride = y * stride;
                        var scanU = scan0 + yStride;
                        var scanL = scan0 + h1Stride - yStride;
                        for (var x = 0; x < wHalf; x++)
                        {
                            var xo = w1 - x;
                            byte* pA, pB;
                            byte temp;

                            pA = scanU + x;
                            pB = scanL + xo;
                            temp = pA[0]; pA[0] = pB[0]; pB[0] = temp;

                            pA = scanU + xo;
                            pB = scanL + x;
                            temp = pA[0]; pA[0] = pB[0]; pB[0] = temp;
                        }
                        if (odd)
                        {
                            var pA = scanU + wHalf;
                            var pB = scanL + wHalf;
                            byte temp;
                            temp = pA[0]; pA[0] = pB[0]; pB[0] = temp;
                        }
                    });
                }
                else
                {
                    throw new ArgumentException();
                }
            }

        }
        
        private static void ApplyColorTransform(ColorTransform transform, BitmapData data)
        {
            unsafe
            {
                var scan0 = (byte*)data.Scan0;
                var stride = data.Stride;
                var w3 = data.Width * 3;
                Parallel.For(0, data.Height, y =>
                //for (var y = 0; y < data.Height; y++)
                {
                    var left = scan0 + y * stride;
                    for (var x3 = 0; x3 < w3; x3 += 3)
                    {
                        transform(left + x3);
                    }
                }
                );
            }
        }

        private static void ApplyColorTransform(ColorTransform transform, BitmapData data, BackgroundWorker bw)
        {
            unsafe
            {
                var scan0 = (byte*)data.Scan0;
                var stride = data.Stride;
                var w3 = data.Width * 3;
                Parallel.For(0, data.Height, (y, state) =>
                //for (var y = 0; y < data.Height; y++)
                {
                    var left = scan0 + y * stride;
                    for (var x3 = 0; x3 < w3; x3 += 3)
                    {
                        transform(left + x3);
                    }
                    if (bw.CancellationPending)
                    {
                        state.Break();
                    }
                }
                );
            }
        }
        

        const int GrayScaleThreshold = 2;

#if DEBUG
        // これは真を返す
        public static bool ParallelBreakTestCode()
        {
            var success = false;
            Parallel.For(0, 1, () => false,
            (y, state, subResult) =>
            {
                state.Break();
                return true;
            },
            subResult =>
            {
                success = subResult;
            });
            return success;
        }
        // よって GetMaxColorDiffProperly でこの様な実装は不要
        public static bool ParallelBreakTestCode2()
        {
            var success = false;
            var successForOnlySuccess = false;
            Parallel.For(0, 1, () => false,
            (y, state, subResult) =>
            {
                successForOnlySuccess = true;
                state.Break();
                return true;
            },
            subResult =>
            {
                success = subResult;
            });
            return success || successForOnlySuccess;
        }
#endif

        // 正確に見る実装
        // 入力がカラーならすぐに終了する
        // bw があればキャンセルを見る（キャンセルされたら null）
        private static int? GetMaxColorDiffProperly(BitmapData data, BackgroundWorker bw = null)
        {
            var dMax = GetMaxColorDiff(data);
            if (dMax > GrayScaleThreshold) return dMax;
            var dMaxSync = new object();
            
            var w = data.Width;
            var h = data.Height;

            var canceled = false;

            unsafe
            {
                var scan0 = (byte*)data.Scan0;
                var stride = data.Stride;
                var w3 = data.Width * 3;
                Parallel.For(0, h, () => dMax,
                (y, state, localMax) =>
                {
                    if (bw != null && bw.CancellationPending)
                    {
                        canceled = true;
                        state.Stop();
                        return localMax;
                    }

                    var left = scan0 + y * stride;
                    for (var x3 = 0; x3 < w3; x3 += 3)
                    {
                        var v = left + x3;
                        var d = getMaxDiff(v[0], v[1], v[2]);
                        if (d > GrayScaleThreshold)
                        {
                            // 呼び出し側はしきい値を超えたかだけを見るので、残りの行は調べなくてよい
                            state.Stop();
                            return d;
                        }
                        if (d > localMax)
                        {
                            localMax = d;
                        }
                    }
                    return localMax;
                },
                localMax =>
                {
                    // localFinally は複数のスレッドから同時に呼ばれる
                    lock (dMaxSync)
                    {
                        if (localMax > dMax) dMax = localMax;
                    }
                });
            }

            if (canceled) return null;

            return dMax;
        }
        
        // 荒く見る実装
        private static int GetMaxColorDiff(BitmapData data)
        {
            const int xCount = 10;
            const int yCount = 10;
            var w = data.Width;
            var h = data.Height;
            var xStep3 = Math.Max(1, w / xCount) * 3;
            var yStep = Math.Max(1, h / yCount);

            var xStart3 = xStep3 / 3 / 2 * 3;
            var yStart = yStep / 2;

            var dMax = 0;
            unsafe
            {
                var scan0 = (byte*)data.Scan0;
                var stride = data.Stride;
                var w3 = data.Width * 3;
                for (var y = yStart; y < h; y += yStep)
                {
                    var left = scan0 + y * stride;
                    for (var x3 = xStart3; x3 < w3; x3 += xStep3)
                    {
                        var v = left + x3;
                        var d = getMaxDiff(v[0], v[1], v[2]);
                        if (d > GrayScaleThreshold)
                        {
                            return d;
                        }
                        if (d > dMax) dMax = d;
                    }
                }
            }

            return dMax;
        }

        private static int getMaxDiff(int r, int g, int b)
        {
            if (b > g)
            {
                if (r > b)
                {
                    return r - g;
                }
                else if (r > g)
                {
                    return b - g;
                }
                else
                {
                    return b - r;
                }
            }
            else
            {
                if (r > g)
                {
                    return r - b;
                }
                else if (r > b)
                {
                    return g - b;
                }
                else
                {
                    return g - r;
                }
            }
        }

        private static void Binarization(BitmapData data)
        {
            unsafe
            {
                var scan0 = (byte*)data.Scan0;
                var stride = data.Stride;
                var hist = new ulong[256];
                var w3 = data.Width * 3;
                var h = data.Height;
                Parallel.For(0, h, () =>
                {
                    // 一番内側のループで配列の境界チェックを行わないようにする
                    var handle = GCHandle.Alloc(new ulong[256], GCHandleType.Pinned);
                    var pointer = handle.AddrOfPinnedObject().ToPointer();
                    return Tuple.Create((IntPtr)pointer, handle);
                }
                , (y, state, resource) =>
                {
                    var localHist = (ulong*)resource.Item1;
                    var scan = scan0 + y * stride;
                    for (var x3 = 0; x3 < w3; x3 += 3)
                    {
                        var p = scan + x3;
                        var g = ToGrayScale(p);
                        p[0] = g;
                        localHist[g]++;
                    }
                    return resource;
                },
                resource =>
                {
                    // ZipPla CE: 確保したのは ulong[256] なので ulong* で数える（byte* では 256 で一周していた）。
                    // localFinally は複数のスレッドから同時に呼ばれるので、足し込みはロックの中で行う
                    var localHist = (ulong*)resource.Item1;
                    lock (hist) for (var i = 0; i < 256; i++) hist[i] += localHist[i];
                    resource.Item2.Free();
                });

                var t = DiscriminantAnalysisMethod(hist);

                Parallel.For(0, h, y =>
                {
                    var scan = scan0 + y * stride;
                    for (var x3 = 0; x3 < w3; x3 += 3)
                    {
                        var p = scan + x3;
                        var g = p[0];
                        var b = g < t ? (byte)0 : (byte)255;
                        p[0] = p[1] = p[2] = b;
                    }
                });
            }
        }

        private static unsafe byte ToGrayScale(byte* p)
        {
            return (byte)((114 * p[0] + 587 * p[1] + 299 * p[2] + 500) / 1000);
        }

        private static int DiscriminantAnalysisMethod(ulong[] hist)
        {

            double s0 = 0;
            ulong w0 = 0;
            double s0s1 = 0;
            ulong w0w1 = 0;
            for (int i = 0; i < hist.Length; i++)
            {
                var c = hist[i];
                s0s1 += ToLinear[i] * c;
                w0w1 += c;
            }
            double hMax = 0;
            int tMax = 0;
            for (var t = 1; t < 255; t++) // t == 0 と t == 255 の評価値は同じなので tMax == 255 にはならない
            {
                var t1 = t - 1;
                var c = hist[t1];
                w0 += c; if (w0 == 0) continue;
                var w1 = w0w1 - w0; if (w1 == 0) break;
                s0 += ToLinear[t1] * c;
                var s1 = s0s1 - s0;
                var m0m1 = s0 / w0 - s1 / w1;
                var h = (double)w0 * w1 * m0m1 * m0m1;
                if (h > hMax)
                {
                    hMax = h;
                    tMax = t;
                }
            }
            return tMax;
        }

        const double standardGamma = 2.2;

        private static readonly double[] ToLinear = getToLinear();
        private static double[] getToLinear()
        {
            var result = new double[256];
            for (var i = 1; i < 255; i++)
            {
                result[i] = Math.Pow(i / 255d, standardGamma);
            }
            result[255] = 1;
            return result;
        }


#if DEBUG
        public static void GetFromLinear()
        {
            System.Windows.Forms.Clipboard.SetText(GetFromLinear("", 0, 256));
            System.Windows.Forms.MessageBox.Show("success!");
        }
        private static string GetFromLinear(string indent, int fromInclusive, int toExclusive)
        {
            if (fromInclusive == toExclusive - 1)
            {
                return $"{indent}return {fromInclusive};\n";
            }
            else
            {
                var center = (fromInclusive + toExclusive) / 2;
                var newIndent = indent + "    ";
                var lCenter = Math.Sqrt(ToLinear[center - 1] * ToLinear[center]);
                return
                    $"{indent}if (x <= {lCenter})\n" + // もともと幾何平均なので切り上げを優遇する根拠はない
                    $"{indent}{'{'}\n" +
                    GetFromLinear(newIndent, fromInclusive, center) +
                    $"{indent}{'}'}\n" +
                    $"{indent}else\n" +
                    $"{indent}{'{'}\n" +
                    GetFromLinear(newIndent, center, toExclusive) +
                    $"{indent}{'}'}\n";
            }
        }
#endif

    }

    //enum TrimmingMode { None = 0, Left = 1 << 0, Right = 1 << 1, Top = 1 << 2, Bottom = 1 << 3,
    //    LeftRight = Left | Right, TopBottom = Top | Bottom }

    //enum PixelValueConversion { None, Binarization, AutoContrastControl }


    public unsafe delegate void ColorTransform(byte* bgr);
    public class PseudoColoringPainter : IDisposable
    {
        bool disposed = true;
        IntPtr pointer;
        unsafe byte* lookup = null;
        //GCHandle gch;

        public PseudoColoringPainter(ToneCurves toneCurve)
        {
            alloc(initialize: true);
            try
            {
                unsafe
                {
                    for (var i = 0; i < 256; i++) toneCurve.ApplyToValueForBrightness(lookup + i * 3);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public PseudoColoringPainter()
        {
            unsafe
            {
                lookup = DefaultLookup;
            }
        }

        private void alloc(bool initialize)
        {
            Dispose();
            //gch = GCHandle.Alloc(256 * 3, GCHandleType.Pinned);
            pointer = Marshal.AllocHGlobal(256 * 3);
            disposed = false;
            try
            {
                unsafe
                {
                    lookup = (byte*)pointer;
                    if (initialize)
                    {
                        for (var i = 0; i < 256; i++)
                        {
                            var a = lookup + i * 3;
                            a[0] = a[1] = a[2] = (byte)i;
                        }
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private unsafe readonly static byte* DefaultLookup = getDefaultTable();
        private unsafe static byte* getDefaultTable()
        {
            var table = new byte[256 * 3] {
0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 3, 4, 5, 4, 5, 6, 5, 7, 7, 6, 8,
8, 7, 9, 9, 8, 10, 10, 9, 11, 11, 10, 12, 12, 11, 13, 13, 12, 14, 14, 13, 16, 15, 13, 17,
16, 14, 18, 17, 15, 19, 18, 16, 20, 19, 17, 21, 20, 18, 22, 21, 19, 24, 22, 20, 25, 23, 21, 26,
24, 22, 27, 25, 22, 28, 26, 23, 30, 27, 24, 31, 28, 25, 32, 29, 26, 33, 30, 27, 34, 31, 28, 35,
32, 29, 37, 33, 30, 38, 34, 31, 39, 35, 32, 40, 36, 33, 41, 37, 33, 42, 38, 34, 43, 39, 35, 44,
40, 36, 46, 41, 37, 47, 42, 38, 48, 43, 39, 49, 44, 40, 51, 45, 41, 52, 46, 42, 53, 47, 43, 54,
48, 43, 55, 48, 44, 56, 49, 45, 58, 50, 46, 59, 51, 47, 60, 52, 48, 61, 53, 49, 63, 54, 50, 64,
55, 51, 65, 56, 52, 66, 57, 52, 68, 57, 53, 69, 58, 54, 70, 59, 55, 71, 60, 56, 73, 61, 57, 74,
62, 58, 75, 62, 59, 77, 63, 59, 78, 64, 60, 80, 65, 61, 81, 65, 62, 82, 66, 63, 84, 67, 64, 86,
68, 64, 87, 68, 65, 88, 69, 66, 90, 70, 67, 92, 70, 67, 94, 70, 68, 96, 72, 69, 97, 73, 70, 98,
74, 71, 99, 75, 72, 100, 76, 73, 101, 77, 74, 102, 78, 75, 104, 79, 76, 105, 80, 77, 106, 81, 78, 108,
81, 79, 109, 82, 80, 110, 83, 80, 111, 84, 81, 113, 85, 82, 114, 85, 83, 115, 86, 84, 117, 87, 85, 118,
88, 86, 119, 89, 87, 120, 90, 88, 121, 90, 89, 123, 91, 90, 124, 92, 91, 125, 93, 92, 126, 94, 93, 128,
95, 94, 129, 95, 95, 130, 96, 96, 131, 97, 96, 132, 98, 98, 133, 99, 98, 135, 100, 99, 136, 100, 101, 137,
102, 102, 138, 102, 103, 139, 103, 103, 140, 104, 104, 142, 105, 105, 143, 106, 106, 144, 107, 107, 145, 107, 108, 146,
108, 109, 148, 109, 110, 148, 110, 111, 150, 110, 112, 151, 111, 113, 153, 112, 114, 154, 113, 115, 155, 114, 116, 156,
114, 117, 157, 116, 118, 158, 116, 119, 159, 117, 120, 161, 118, 121, 162, 119, 122, 163, 120, 123, 164, 121, 124, 165,
121, 125, 166, 122, 126, 167, 123, 127, 169, 123, 128, 170, 124, 129, 171, 125, 129, 172, 126, 130, 174, 127, 131, 175,
128, 132, 176, 129, 133, 177, 129, 134, 178, 131, 136, 179, 132, 137, 180, 132, 138, 181, 133, 138, 183, 134, 139, 184,
134, 140, 185, 136, 141, 186, 136, 142, 188, 137, 143, 189, 137, 144, 190, 138, 145, 191, 138, 146, 192, 139, 147, 194,
140, 148, 195, 140, 149, 196, 141, 150, 197, 142, 151, 198, 143, 152, 200, 144, 153, 201, 144, 154, 202, 145, 155, 203,
146, 156, 205, 146, 157, 206, 146, 158, 207, 147, 159, 208, 148, 160, 209, 149, 161, 210, 150, 162, 211, 150, 163, 212,
151, 164, 214, 151, 165, 215, 152, 166, 216, 153, 167, 217, 154, 168, 218, 154, 169, 219, 155, 171, 220, 156, 172, 221,
157, 173, 222, 158, 174, 224, 158, 175, 224, 160, 176, 225, 160, 177, 226, 161, 178, 227, 162, 179, 228, 163, 180, 229,
163, 181, 230, 165, 182, 231, 165, 183, 231, 166, 184, 232, 167, 186, 233, 168, 187, 234, 169, 188, 234, 170, 189, 235,
171, 190, 235, 172, 191, 236, 173, 192, 237, 174, 194, 237, 175, 195, 238, 176, 196, 239, 177, 197, 239, 178, 198, 240,
179, 199, 240, 181, 201, 241, 182, 202, 241, 183, 203, 241, 184, 204, 242, 185, 206, 242, 186, 207, 243, 188, 208, 243,
189, 209, 244, 190, 211, 244, 192, 212, 244, 193, 213, 245, 195, 214, 245, 196, 216, 245, 198, 217, 245, 200, 218, 246,
201, 219, 246, 203, 221, 246, 204, 222, 247, 206, 223, 247, 207, 224, 247, 209, 226, 247, 211, 227, 247, 213, 228, 247,
214, 230, 248, 216, 231, 248, 218, 232, 248, 220, 233, 248, 222, 234, 248, 224, 236, 248, 226, 237, 248, 228, 238, 248,
231, 239, 248, 233, 240, 249, 235, 241, 249, 237, 243, 249, 239, 244, 249, 240, 245, 250, 242, 246, 250, 244, 247, 250,
245, 248, 251, 247, 249, 251, 249, 250, 252, 250, 251, 252, 252, 252, 253, 253, 253, 253, 254, 254, 254, 255, 255, 255,
            };

            var parray = Marshal.AllocHGlobal(256 * 3);
            Marshal.Copy(table, 0, parray, 256 * 3);
            table = null;
            return (byte*)parray;
        }

        public ColorTransform ColorTransform
        {
            get
            {
                unsafe
                {
                    return transform;
                }
            }
        }

        public ColorTransform ColorTransformForGrayScale
        {
            get
            {
                unsafe
                {
                    return transformForGrayScale;
                }
            }
        }

        private unsafe void transform(byte* bgr)
        {
            var a = lookup + 3 * median(bgr[0], bgr[1], bgr[2]);
            bgr[0] = a[0];
            bgr[1] = a[1];
            bgr[2] = a[2];
        }

        private unsafe void transformForGrayScale(byte* bgr)
        {
            var a = lookup + 3 * *bgr;
            bgr[0] = a[0];
            bgr[1] = a[1];
            bgr[2] = a[2];
        }

        private static byte median(byte r, byte g, byte b)
        {
            if (b > g)
            {
                if (r > b)
                {
                    return b;
                }
                else if (r > g)
                {
                    return r;
                }
                else
                {
                    return g;
                }
            }
            else
            {
                if (r > g)
                {
                    return g;
                }
                else if (r > b)
                {
                    return r;
                }
                else
                {
                    return b;
                }
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                //gch.Free();
                Marshal.FreeHGlobal(pointer);
                disposed = true;
            }
        }
    }
}
