using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ZipPla
{
    public delegate bool TryGetWeightDelegate1(double distance, out double weight);
    public delegate bool TryGetWeightDelegate2(double distanceAsSrc, double power, out double weight);

    public enum GammaConversion { Value1_0, Value2_2 }

    public static partial class LongVectorImageResizer
    {
        // 返却値が 1 よりも小さい最大の値を返す入力値の中央は (1/π)√(6ε * n^2 / (1+n^2))
        // 四捨五入を想定すると (1/π)√(3ε * n^2 / (1+n^2))
        // n を変化させて最小値をとると n = 1 で (1/π)√((3/2)ε)
        // なお、double.Epsilon は計算機イプシロンではなく正の最小の数なので注意
        private const double tryGetLanczosNWeight_ZeroThreshold = 5.4102333809763210874651254858134827373479997412536e-18;
        private static bool tryGetLanczosNWeight(double distance, out double weight, int n)
        {
            if (distance < 0) distance = -distance;
            if (distance < tryGetLanczosNWeight_ZeroThreshold)
            {
                weight = 1.0;
                return true;
            }
            else if (distance < n)
            {
                // 三角関数の計算時間がテーブル作成処理の半分を占める

                // 三角関数の計算時間は無視できる、精度は 7 bit 程度

                // 三角関数の計算時間はテーブル作成処理の 1 割程度、ほぼ 17 bit 分の精度（±2^(-17-1) 以内の誤差）を保証
                var pi_x = Math.PI * distance;
                var pi_x_n = pi_x / n;
                weight = (fastSin(pi_x) * fastSin(pi_x_n)) / (pi_x * pi_x_n);
                
                return true;
            }
            else
            {
                weight = 0;
                return false;
            }
        }

        // sin をテーブルを基準に 3 次のテイラー展開で近似
        // 誤差の主要項の係数が sin になるため相対誤差が保証される
        // 係数を全て直書きして switch で分岐すると逆に遅くなった
        // つまり線形に検索する switch は数がかなり少なくても配列へのアクセスより遅いということか
        private const int fastSinTableDigits = 3; // 下記の fastSinTableCount = 6 で最大誤差 0.0000122333 ≒ 0.0001220703125 ＝ 1 / 2^(-13)
        private const int fastSinTableCount = 1 << fastSinTableDigits;
        private const double fastSinTableDx = (Math.PI / 2) / fastSinTableCount;
        private const double fastSinTableDxInv = fastSinTableCount / (Math.PI / 2);
        private static readonly double[] fastSinTable = Enumerable.Range(0, fastSinTableCount + 1).Select(n => Math.Sin(n * fastSinTableDx)).ToArray();
        private static double fastSin(double positiveX)
        {
            var index = (long)(positiveX * fastSinTableDxInv + 0.5);
            var i = index & ~(~0 << fastSinTableDigits);
            var v0 = fastSinTable[i];
            var v1 = fastSinTable[fastSinTableCount - i];
            var dx = positiveX - index * fastSinTableDx;
            switch (index & 3 << fastSinTableDigits)
            {
                case 0 << fastSinTableDigits: return v0 + dx * (v1 - dx * ((1.0 / 2.0) * v0 + dx * (1.0 / 6.0) * v1));
                case 1 << fastSinTableDigits: return v1 - dx * (v0 + dx * ((1.0 / 2.0) * v1 - dx * (1.0 / 6.0) * v0));
                case 2 << fastSinTableDigits: return -v0 - dx * (v1 - dx * ((1.0 / 2.0) * v0 + dx * (1.0 / 6.0) * v1));
                default: return dx * (v0 + dx * ((1.0 / 2.0) * v1 - dx * (1.0 / 6.0) * v0)) - v1;
            }
        }

        
        public static Bitmap Lanczos(int n, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, Func<bool> cancel = null)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, (double distance, out double weight) =>
                tryGetLanczosNWeight(distance, out weight, n), parallel, cancel);
        }

        public static Bitmap Lanczos(int n, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, BackgroundWorker backgroundWorker)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, (double distance, out double weight) =>
                tryGetLanczosNWeight(distance, out weight, n), parallel, backgroundWorker);
        }

        public static void Lanczos(int n, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Bitmap dst, Rectangle dstRect, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            Draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, (double distance, out double weight) => tryGetLanczosNWeight(distance, out weight, n),
                parallel, backgroundWorker);
        }

#if !AUTOBUILD
        
        private static bool tryGetFewAliasingLanczosNWeight(double distanceAsSrc, double power, out double weight, int n)
        {
            double distance;
            if (power >= 1)
            {
                distance = distanceAsSrc;
            }
            else
            {
                var x = (double)n / (n + 1);
                power *= power > x ? power : x;
                distance = distanceAsSrc * power;
            }
            if (distance < 0) distance = -distance;
            if (distance < tryGetLanczosNWeight_ZeroThreshold)
            {
                weight = 1.0;
                return true;
            }
            else if (distance < n)
            {
                var pi_x = Math.PI * distance;
                var pi_x_n = pi_x / n;
                weight = (fastSin(pi_x) * fastSin(pi_x_n)) / (pi_x * pi_x_n);

                return true;
            }
            else
            {
                weight = 0;
                return false;
            }
        }
        
        public static Bitmap FewAliasingLanczos(int n, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, Func<bool> cancel = null)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, (double distanceAsSrc, double power, out double weight) =>
                tryGetFewAliasingLanczosNWeight(distanceAsSrc, power, out weight, n), parallel, cancel);
        }

        public static void FewAliasingLanczos(int n, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Bitmap dst, Rectangle dstRect, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            Draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, (double distanceAsSrc, double power, out double weight) =>
                tryGetFewAliasingLanczosNWeight(distanceAsSrc, power, out weight, n), parallel, backgroundWorker);
        }

#endif

        public static Bitmap Spline4(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, Func<bool> cancel = null)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, TryGetSpline4Weight, parallel, cancel);
        }

        public static Bitmap Spline4(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, BackgroundWorker backgroundWorker)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, TryGetSpline4Weight, parallel, backgroundWorker);
        }

        public static void Spline4(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Bitmap dst, Rectangle dstRect, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            Draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, TryGetSpline4Weight, parallel, backgroundWorker);
        }

        public static Bitmap Spline16(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, Func<bool> cancel = null)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, TryGetSpline16Weight, parallel, cancel);
        }

        public static Bitmap Spline16(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, BackgroundWorker backgroundWorker)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, TryGetSpline16Weight, parallel, backgroundWorker);
        }

        public static void Spline16(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Bitmap dst, Rectangle dstRect, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            Draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, TryGetSpline16Weight, parallel, backgroundWorker);
        }

        public static Bitmap Spline36(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, Func<bool> cancel = null)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, TryGetSpline36Weight, parallel, cancel);
        }

        public static Bitmap Spline36(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, BackgroundWorker backgroundWorker)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, TryGetSpline36Weight, parallel, backgroundWorker);
        }

        public static void Spline36(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Bitmap dst, Rectangle dstRect, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            Draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, TryGetSpline36Weight, parallel, backgroundWorker);
        }
        
        public static Bitmap Spline64(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, Func<bool> cancel = null)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, TryGetSpline64Weight, parallel, cancel);
        }

        public static Bitmap Spline64(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, BackgroundWorker backgroundWorker)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, TryGetSpline64Weight, parallel, backgroundWorker);
        }

        public static void Spline64(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Bitmap dst, Rectangle dstRect, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            Draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, TryGetSpline64Weight, parallel, backgroundWorker);
        }

        public static Bitmap AreaAverage(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            if (size.Width % src.Width == 0 && size.Height % src.Height == 0)
            {
                return nearestNeighbor(src, size, parallel, backgroundWorker, accurate: false);
            }
            else
            {
                return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, TryGetAreaAverageWeight, parallel, backgroundWorker);
            }
        }

        public static void AreaAverage(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Bitmap dst, Rectangle dstRect, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            if (dstRect.Width % src.Width == 0 && dstRect.Height % src.Height == 0)
            {
                DrawResizedBitmapByNearestNeighbor(src, 0, 0, src.Width, src.Height, dst,
                    dstRect.X, dstRect.Y, dstRect.Width, dstRect.Height,
                    parallel, backgroundWorker, accurate: false);
            }
            else
            {
                Draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, TryGetAreaAverageWeight, parallel, backgroundWorker);
            }
        }

        private static bool TryGetSpline4Weight(double distance, out double weight)
        {
            var absX = Math.Abs(distance);
            if (absX < 1)
            {
                weight = 1.0 - absX;
                return true;
            }
            weight = 0;
            return false;
        }
        private static bool TryGetSpline16Weight(double distance, out double weight)
        {
            var absX = Math.Abs(distance);
            if (absX <= 1)
            {
                weight = ((absX - 9.0 / 5.0) * absX - 1.0 / 5.0) * absX + 1.0;
                return true;
            }
            if (absX < 2)
            {
                weight = ((-1.0 / 3.0 * absX + 9.0 / 5.0) * absX - 46.0 / 15.0) * absX + 8.0 / 5.0;
                return true;
            }
            weight = 0;
            return false;
        }

        private static bool TryGetSpline36Weight(double distance, out double weight)
        {
            var absX = Math.Abs(distance);
            if (absX <= 1)
            {
                weight = ((13.0 / 11.0 * absX - 453.0 / 209.0) * absX - 3.0 / 209.0) * absX + 1.0;
                return true;
            }
            if (absX <= 2)
            {
                weight = ((-6.0 / 11.0 * absX + 612.0 / 209.0) * absX - 1038.0 / 209.0) * absX + 540.0 / 209.0;
                return true;
            }
            if (absX < 3)
            {
                weight = ((1.0 / 11.0 * absX - 159.0 / 209.0) * absX + 434.0 / 209.0) * absX - 384.0 / 209.0;
                return true;
            }

            weight = 0;
            return false;
        }

        private static bool TryGetSpline64Weight(double distance, out double weight)
        {
            var absX = Math.Abs(distance);
            if (absX <= 1)
            {
                weight = ((49.0 / 41.0 * absX - 6387.0 / 2911.0) * absX - 3.0 / 2911.0) * absX + 1.0;
                return true;
            }
            if (absX <= 2)
            {
                weight = ((-24.0 / 41.0 * absX + 9144.0 / 2911.0) * absX - 15504.0 / 2911.0) * absX + 8064.0 / 2911.0;
                return true;
            }
            if (absX <= 3)
            {
                weight = ((6.0 / 41.0 * absX - 3564.0 / 2911.0) * absX + 9726.0 / 2911.0) * absX - 8604.0 / 2911.0;
                return true;
            }
            if (absX < 4)
            {
                weight = ((-1.0 / 41.0 * absX + 807.0 / 2911.0) * absX - 3022.0 / 2911.0) * absX + 3720.0 / 2911.0;
                return true;
            }

            weight = 0;
            return false;
        }

        private static bool TryGetAreaAverageWeight(double distanceAsSrc, double power, out double weight)
        {
            weight = 0.5 + (0.5 - Math.Abs(distanceAsSrc)) * power;
            if (power > 1) power = 1;
            if (weight >= power)
            {
                weight = power;
                return true;
            }
            else
            {
                return weight > 0;
            }
        }

        public static Bitmap CreateNew(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, TryGetWeightDelegate1 tryGet, bool parallel, BackgroundWorker backgroundWorker)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, tryGet, null, parallel, backgroundWorker == null ? null as Func<bool> : () => backgroundWorker.CancellationPending);
        }
        public static Bitmap CreateNew(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, TryGetWeightDelegate2 tryGet, bool parallel, BackgroundWorker backgroundWorker)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, null, tryGet, parallel, backgroundWorker == null ? null as Func<bool> : () => backgroundWorker.CancellationPending);
        }
        public static Bitmap CreateNew(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, TryGetWeightDelegate1 tryGet, bool parallel, Func<bool> cancel)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, tryGet, null, parallel, cancel);
        }
        public static Bitmap CreateNew(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, TryGetWeightDelegate2 tryGet, bool parallel, Func<bool> cancel)
        {
            return CreateNew(src, pixelsPerPixelToBeLeft, gamma, size, null, tryGet, parallel, cancel);
        }
        private static Bitmap CreateNew(Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, Size size, TryGetWeightDelegate1 tryGet1, TryGetWeightDelegate2 tryGet2, bool parallel, Func<bool> cancel)
        {
            var pixelFormat = src.PixelFormat;
            var is8bpp = pixelFormat == PixelFormat.Format8bppIndexed;
            if (!is8bpp && pixelFormat != PixelFormat.Format24bppRgb)
            {
                throw new ArgumentException(null, "src");
            }
            var result = new Bitmap(size.Width, size.Height, pixelFormat);
            try
            {
                if (is8bpp) result.Palette = src.Palette;
                if (tryGet1 != null)
                {
                    Draw(result, src, pixelsPerPixelToBeLeft, gamma, tryGet1, parallel, cancel);
                }
                else
                {
                    Draw(result, src, pixelsPerPixelToBeLeft, gamma, tryGet2, parallel, cancel);
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static void Draw(Bitmap dst, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, TryGetWeightDelegate1 tryGetWeight, bool parallel, Func<bool> cancel)
        {
            draw(dst, new Rectangle(Point.Empty, dst.Size), src, pixelsPerPixelToBeLeft, gamma, tryGetWeight, parallel, cancel);
        }
        private static void Draw(Bitmap dst, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, TryGetWeightDelegate2 tryGetWeight, bool parallel, Func<bool> cancel)
        {
            draw(dst, new Rectangle(Point.Empty, dst.Size), src, pixelsPerPixelToBeLeft, gamma, tryGetWeight, parallel, cancel);
        }
        private static void Draw(Bitmap dst, Rectangle dstRect, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, TryGetWeightDelegate1 tryGetWeight, bool parallel, BackgroundWorker backgroundWorker)
        {
            draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, tryGetWeight, parallel, backgroundWorker == null ? null as Func<bool> : () => backgroundWorker.CancellationPending);
        }
        private static void Draw(Bitmap dst, Rectangle dstRect, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, TryGetWeightDelegate1 tryGetWeight, bool parallel, Func<bool> cancel)
        {
            draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, tryGetWeight, parallel, cancel);
        }
        private static void Draw(Bitmap dst, Rectangle dstRect, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, TryGetWeightDelegate2 tryGetWeight, bool parallel, BackgroundWorker backgroundWorker)
        {
            draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, tryGetWeight, parallel, backgroundWorker == null ? null as Func<bool> : () => backgroundWorker.CancellationPending);
        }
        private static void Draw(Bitmap dst, Rectangle dstRect, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, TryGetWeightDelegate2 tryGetWeight, bool parallel, Func<bool> cancel)
        {
            draw(dst, dstRect, src, pixelsPerPixelToBeLeft, gamma, tryGetWeight, parallel, cancel);
        }

        private static void draw(Bitmap dst, Rectangle dstRect, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, TryGetWeightDelegate1 tryGetWeight, bool parallel, Func<bool> cancel)
        {
            draw(dst, dstRect, src, new Rectangle(Point.Empty, src.Size), GetSuitableStep(src.Size, dstRect.Size, pixelsPerPixelToBeLeft), gamma, tryGetWeight, parallel, cancel: cancel);
        }

        private static void draw(Bitmap dst, Rectangle dstRect, Bitmap src, int pixelsPerPixelToBeLeft, GammaConversion gamma, TryGetWeightDelegate2 tryGetWeight, bool parallel, Func<bool> cancel)
        {
            draw(dst, dstRect, src, new Rectangle(Point.Empty, src.Size), GetSuitableStep(src.Size, dstRect.Size, pixelsPerPixelToBeLeft), gamma, tryGetWeight, parallel, cancel: cancel);
        }

        public static Size GetSuitableStep(Size srcSize, Size dstSize, int pixelsPerPixelToBeLeft)
        {
            return new Size(GetSuitableStep(srcSize.Width, dstSize.Width, pixelsPerPixelToBeLeft), GetSuitableStep(srcSize.Height, dstSize.Height, pixelsPerPixelToBeLeft));
        }

        public static int GetSuitableStep(int srcLength, int dstLength, int pixelsPerPixelToBeLeft)
        {
            if (pixelsPerPixelToBeLeft <= 0 || pixelsPerPixelToBeLeft > int.MaxValue / dstLength) return 1;
            var step = srcLength / (pixelsPerPixelToBeLeft * dstLength);
            return step <= 1 ? 1 : step;
        }

        private static void draw(Bitmap dst, Rectangle dstRect, Bitmap src, Rectangle srcRect, Size srcStep, GammaConversion gamma, TryGetWeightDelegate1 tryGetWeight, bool parallel, Func<bool> cancel)
        {
            draw(dst, dstRect, src, srcRect, srcStep, gamma, tryGetWeight, null, parallel, cancel);
        }

        private static void draw(Bitmap dst, Rectangle dstRect, Bitmap src, Rectangle srcRect, Size srcStep, GammaConversion gamma, TryGetWeightDelegate2 tryGetWeight, bool parallel, Func<bool> cancel)
        {
            draw(dst, dstRect, src, srcRect, srcStep, gamma, null, tryGetWeight, parallel, cancel);
        }

        const int grayLinearDigits = 18;

        private static void draw(Bitmap dst, Rectangle dstRect, Bitmap src, Rectangle srcRect, Size srcStep, GammaConversion gamma, TryGetWeightDelegate1 tryGetWeight1, TryGetWeightDelegate2 tryGetWeight2, bool parallel, Func<bool> cancel)
        {
            var srcPixelFormat = src.PixelFormat;
            var dstPixelFormat = dst.PixelFormat;
            var dstBytesPerPixel = dstPixelFormat == PixelFormat.Format8bppIndexed ? 1 : 3;
            
            ulong[] hWeights = null;
            ulong[] vWeights = null;
            int srcBytesPerPixel;
            int dstRightBound;
            int dstBottomBound;
            int rx2 = 0, ry2 = 0;

            bool gammaIs1 = gamma == GammaConversion.Value1_0;
            IntPtr toLinearPointer = IntPtr.Zero;
            if (srcPixelFormat == PixelFormat.Format24bppRgb)
            {
                if (gammaIs1)
                {
                    if (parallel)
                    {
                        Parallel.Invoke(
                            () => hWeights = getWeightArray(out rx2, false, tryGetWeight1, tryGetWeight2, srcRect.Width, dstRect.Width, srcStep.Width, 12),
                            () => vWeights = getWeightArray(out ry2, false, tryGetWeight1, tryGetWeight2, srcRect.Height, dstRect.Height, srcStep.Height, 0));
                    }
                    else
                    {
                        hWeights = getWeightArray(out rx2, false, tryGetWeight1, tryGetWeight2, srcRect.Width, dstRect.Width, srcStep.Width, 12);
                        vWeights = getWeightArray(out ry2, false, tryGetWeight1, tryGetWeight2, srcRect.Height, dstRect.Height, srcStep.Height, 0);
                    }
                }
                else
                {
                    if (parallel)
                    {
                        Parallel.Invoke(
                            () => hWeights = getWeightArray(out rx2, false, tryGetWeight1, tryGetWeight2, srcRect.Width, dstRect.Width, srcStep.Width, 7),
                            () => vWeights = getWeightArray(out ry2, false, tryGetWeight1, tryGetWeight2, srcRect.Height, dstRect.Height, srcStep.Height, 7));
                    }
                    else
                    {
                        hWeights = getWeightArray(out rx2, false, tryGetWeight1, tryGetWeight2, srcRect.Width, dstRect.Width, srcStep.Width, 7);
                        vWeights = getWeightArray(out ry2, false, tryGetWeight1, tryGetWeight2, srcRect.Height, dstRect.Height, srcStep.Height, 7);
                    }
                    toLinearPointer = toLinear131413Array2_2;
                }
                srcBytesPerPixel = 3;
                dstRightBound = dstRect.Width;
                dstBottomBound = dstRect.Height;
            }
            else if (srcPixelFormat == PixelFormat.Format8bppIndexed)
            {
                if (gammaIs1)
                {
                    if (parallel)
                    {
                        Parallel.Invoke(
                            () => hWeights = getWeightArray(out rx2, true, tryGetWeight1, tryGetWeight2, srcRect.Width, dstRect.Width, srcStep.Width, 7),
                            () => vWeights = getWeightArray(out ry2, true, tryGetWeight1, tryGetWeight2, srcRect.Height, dstRect.Height, srcStep.Height, 7));
                    }
                    else
                    {
                        hWeights = getWeightArray(out rx2, true, tryGetWeight1, tryGetWeight2, srcRect.Width, dstRect.Width, srcStep.Width, 7);
                        vWeights = getWeightArray(out ry2, true, tryGetWeight1, tryGetWeight2, srcRect.Height, dstRect.Height, srcStep.Height, 7);
                    }
                    dstBottomBound = (dstRect.Height + 1) / 2;
                }
                else
                {
                    if (parallel)
                    {
                        Parallel.Invoke(
                            () => hWeights = getWeightArray(out rx2, true, tryGetWeight1, tryGetWeight2, srcRect.Width, dstRect.Width, srcStep.Width, 31 - grayLinearDigits),
                            () => vWeights = getWeightArray(out ry2, false, tryGetWeight1, tryGetWeight2, srcRect.Height, dstRect.Height, srcStep.Height, 31 - grayLinearDigits));
                    }
                    else
                    {
                        hWeights = getWeightArray(out rx2, true, tryGetWeight1, tryGetWeight2, srcRect.Width, dstRect.Width, srcStep.Width, 31 - grayLinearDigits);
                        vWeights = getWeightArray(out ry2, false, tryGetWeight1, tryGetWeight2, srcRect.Height, dstRect.Height, srcStep.Height, 31 - grayLinearDigits);
                    }
                    dstBottomBound = dstRect.Height;
                    toLinearPointer = toLinear18Array2_2;
                }
                srcBytesPerPixel = 1;
                dstRightBound = (dstRect.Width + 1) / 2;
            }
            else throw new ArgumentException(null, "src");

            long txx, tx0, txd, tyy, ty0, tyd;
            getTransform(out txx, out tx0, out txd, dstRect.Width, srcRect.Width, srcStep.Width);
            getTransform(out tyy, out ty0, out tyd, dstRect.Height, srcRect.Height, srcStep.Height);

            var rx = rx2 / 2;
            var ry = ry2 / 2;

            int hStep = 0, vStep = 0;
            if (parallel)
            {
                var pc = Environment.ProcessorCount;
                if (pc <= 1)
                {
                    parallel = false;
                }
                else
                {
                    int srcRightBound, srcBottomBound;
                    if (srcPixelFormat == PixelFormat.Format8bppIndexed)
                    {
                        srcRightBound = (srcRect.Width + 1) / 2;
                        if (gammaIs1)
                        {
                            srcBottomBound = (srcRect.Height + 1) / 2;
                        }
                        else
                        {
                            srcBottomBound = srcRect.Height;
                        }
                    }
                    else
                    {
                        srcRightBound = srcRect.Width;
                        srcBottomBound = srcRect.Height;
                    }
                    long sampleW = (srcRightBound - 1) / srcStep.Width + 1;
                    long sampleH = (srcBottomBound - 1) / srcStep.Height + 1;
                    var c = Math.Pow((double)(sampleW * sampleH) / (rx2 * ry2 * (pc - 1)), 1.0 / 3.0);
                    hStep = Math.Max((int)(c * rx2 * dstRightBound / sampleW + 0.5), 1);
                    vStep = Math.Max((int)(c * ry2 * dstBottomBound / sampleH + 0.5), 1);
                    parallel = hStep * 2 <= dstRightBound || vStep * 2 <= dstBottomBound;
                }
            }

            var srcData = src.LockBits(srcRect, ImageLockMode.ReadOnly, srcPixelFormat);
            try
            {
                var dstData = dst.LockBits(dstRect, ImageLockMode.WriteOnly, dstPixelFormat);
                try
                {
                    int buffer1dSize, buffer2dSize;
                    unsafe
                    {
                        fixed (ulong* cx0 = hWeights)
                        fixed (ulong* cy0 = vWeights)
                        {
                            var toLinear = toLinearPointer == IntPtr.Zero ? null : (ulong*) toLinearPointer;

                            if (parallel)
                            {
                                getBufferSize(out buffer1dSize, out buffer2dSize, hStep, txx, txd, rx, ry);
                                var bufferSize = buffer1dSize + buffer2dSize;
                                var buuferByteSize = sizeof(ulong) * bufferSize;
                                var hCount = (dstRightBound - 1) / hStep + 1;
                                var vCount = (dstBottomBound - 1) / vStep + 1;
                                var cx0_ = cx0;
                                var cy0_ = cy0;
                                var cancelExists = cancel != null;
                                Parallel.For(0, hCount * vCount,
                                    () => Marshal.AllocCoTaskMem(buuferByteSize),
                                (ij, loop, pointer) =>
                                {
                                    if (cancelExists && cancel())
                                    {
                                        loop.Break();
                                        return pointer;
                                    }
                                    
                                    var buff1d = (ulong*)pointer;
                                    var buff2d = buff1d + buffer1dSize;

                                    var i = ij % hCount;
                                    var j = ij / hCount;

                                    var x0 = i * hStep;
                                    var x1 = Math.Min(x0 + hStep, dstRightBound);
                                    var y0 = j * vStep;
                                    var y1 = Math.Min(y0 + vStep, dstBottomBound);

                                    // ZipPla CE: ガンマ 1.0 で lvresize.dll があれば、ネイティブ版（AVX-512・AVX2）で描く。結果は同じ
                                    if (toLinear != null || !NativeResizer.TryCore(
                                        dstData.Scan0, x0, x1, y0, y1, dstData.Stride, dstBytesPerPixel, dstRect.Width, dstRect.Height,
                                        srcData.Scan0, txx, tx0, txd, srcStep.Width, tyy, ty0, tyd, srcStep.Height,
                                        srcData.Stride, srcBytesPerPixel, srcRect.Width, srcRect.Height,
                                        rx, ry, cx0_, cy0_, buff1d, buff2d, getSuitabgleDigits(ry * 2), cancel: null))
                                    core(
                                        dstData.Scan0, x0, x1, y0, y1, dstData.Stride, dstBytesPerPixel, dstRect.Width, dstRect.Height,
                                        srcData.Scan0, txx, tx0, txd, srcStep.Width, tyy, ty0, tyd, srcStep.Height,
                                        srcData.Stride, srcBytesPerPixel, srcRect.Width, srcRect.Height,
                                        toLinear, rx, ry, cx0_, cy0_, buff1d, buff2d, cancel: null);

                                    return pointer;
                                }, pointer => Marshal.FreeCoTaskMem(pointer));
                            }
                            else
                            {
                                getBufferSize(out buffer1dSize, out buffer2dSize, dstRightBound, txx, txd, rx, ry);
                                fixed (ulong* buff1d = new ulong[buffer1dSize])
                                fixed (ulong* buff2d = new ulong[buffer2dSize])
                                {
                                    if (toLinear != null || !NativeResizer.TryCore(
                                        dstData.Scan0, 0, dstRightBound, 0, dstBottomBound, dstData.Stride, dstBytesPerPixel, dstRect.Width, dstRect.Height,
                                        srcData.Scan0, txx, tx0, txd, srcStep.Width, tyy, ty0, tyd, srcStep.Height,
                                        srcData.Stride, srcBytesPerPixel, srcRect.Width, srcRect.Height,
                                        rx, ry, cx0, cy0, buff1d, buff2d, getSuitabgleDigits(ry * 2), cancel))
                                    core(
                                        dstData.Scan0, 0, dstRightBound, 0, dstBottomBound, dstData.Stride, dstBytesPerPixel, dstRect.Width, dstRect.Height,
                                        srcData.Scan0, txx, tx0, txd, srcStep.Width, tyy, ty0, tyd, srcStep.Height,
                                        srcData.Stride, srcBytesPerPixel, srcRect.Width, srcRect.Height,
                                        toLinear, rx, ry, cx0, cy0, buff1d, buff2d, cancel);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    dst.UnlockBits(dstData);
                }
            }
            finally
            {
                src.UnlockBits(srcData);
            }
        }
        
        private static void getTransform(out long t, out long t0, out long td, int dst, int src, int step)
        {
            t = src * 2;
            t0 = src - dst + 2 * dst * step - 1;
            td = dst * step * 2;
        }

        private static void getBufferSize(out int buffer1dSize, out int buffer2dSize,
            int dstWidth,
            long txx, long txd,
            int rx, int ry
            )
        {
            var srcWidthUBound = (int)((txx * (dstWidth - 1) - 1) / txd + 2);
            buffer1dSize = srcWidthUBound + rx * 2;
            buffer2dSize = dstWidth * ry * 2;
        }

        private static unsafe void core(
            IntPtr dstScan0,
            long x0, long x1, long y0, long y1, long dstStride, long dstBytesPerPixel, long dstDataWidth, long dstDataHeight,
            IntPtr srcScan0,
            long txx, long tx0, long txd, long srcXStep,
            long tyy, long ty0, long tyd, long srcYStep,
            long srcStride, long srcBytesPerPixel, long srcDataWidth, long srcDataHeight,
            ulong* toLinear,
            int rx, int ry, ulong* cx0, ulong* cy0,
            ulong* buff1d, ulong* buff2d,
            Func<bool> cancel)
        {
            var dstScan0_ = (byte*)dstScan0;
            var dstX0Bytes = x0 * dstBytesPerPixel;
            var dstScan0X0 = dstScan0_ + dstX0Bytes;
            var dstX1Bytes = (dstDataWidth - x0) * dstBytesPerPixel - 1;
            var dstScan0X0Mirror01 = dstScan0_ + dstX1Bytes;
            var dstScan0Mirror = dstScan0_ + (dstDataHeight - 1) * dstStride;
            var dstScan0X0Mirror10 = dstScan0Mirror + dstX0Bytes;// + dstBytesPerPixel - 1;
            var dstScan0X0Mirror11 = dstScan0Mirror + dstX1Bytes;
            long dstWidth = x1 - x0;
            var dst24bpp = dstBytesPerPixel == 3;
            long rx2 = rx * 2;
            long ry2 = ry * 2;
            var srcScan0_ = (byte*)srcScan0;
            var srcX0 = (x0 * txx + tx0) / txd;
            var srcX0rx = srcX0 - rx;
            const long srcXL = 0;
            var srcXStart = Math.Max(srcX0rx, srcXL);
            var srcBytesPerXSample = srcBytesPerPixel * srcXStep;
            var srcBytesPerXSample11 = srcBytesPerXSample - srcBytesPerPixel + 1;
            var srcXStepIsOne = srcXStep == 1;
            var srcXStartBytes = srcXStart * srcBytesPerXSample;
            var srcX1 = ((x1 - 1) * txx + tx0) / txd + 1;
            var srcX1rx = srcX1 + rx;
            var srcStrideStep = srcStride * srcYStep;
            var srcXU = (srcDataWidth - 1) / srcXStep + 1;
            //var srcXStartBytesMirror = (srcXU - srcXStart) * srcBytesPerXSample - 1; // ミラーは逆からステップを数えないとピクセルがズレる
            var srcXStartBytesMirror = srcDataWidth * srcBytesPerPixel - srcXStart * srcBytesPerXSample - 1;
            var srcXStop = Math.Min(srcX1rx, srcXU);
            var srcY0 = (y0 * tyy + ty0) / tyd;
            var srcY0ry = srcY0 - ry;
            var srcY0ry2 = srcY0 - ry2;
            var src24bpp = srcBytesPerPixel == 3;
            const long srcYL = 0;
            var srcYStart = Math.Max(srcY0ry, srcYL);
            var srcY1 = ((y1 - 1) * tyy + ty0) / tyd + 1;
            var srcY1ry = srcY1 + ry;
            var srcYU = (srcDataHeight - 1) / srcYStep + 1;
            //var srcScan0Mirror = srcScan0_ + (srcYU - 1) * srcStrideStep; // ミラーは逆からステップを数えないとピクセルがズレる
            var srcScan0Mirror = srcScan0_ + srcDataHeight * srcStride - srcStrideStep; // カラーでミラーリングするなら→を追加 + srcBytesPerPixel - 1;
            var srcYStop = Math.Min(srcY1ry, srcYU);
            var srcYStop1 = srcYStop - 1;
            long dstY = y0;
            var buff2dSize = dstWidth * ry2;
            var iy0 = (2 - ry) * tyd - ty0 + tyy - 1;
            var cancelExists = cancel != null;
            const ulong unt = 1L | 1L << 21 | 1L << 43; // G だけ精度を高めるので RGB 一致のモノクロ画像入力には不向き
            const ulong unt2 = 1L | 1L << 22 | 1L << 43;
            var linearize = toLinear != null;
            int interShift = linearize ? 7 : getSuitabgleDigits(ry2); // 24bit の場合のみ使用
            int interBits = 20 - interShift;
            ulong interDesignRoundOffset = unt2 << 19 | unt << (interShift - 1);
            ulong interBitsMask = ~(~0LU << (interBits + 1));
            ulong interMask = unt * interBitsMask | unt2 * interBitsMask;
            ulong interSignOffset = unt2 << (interBits - 1);
            var buff1d_srcX0rx = buff1d - srcX0rx;
            var buff1d_srcX0 = buff1d - srcX0;
            var buff2d_x0Ry2 = buff2d - x0 * ry2;
            var yUpScaling = tyy < ty0;
            var toLinear1 = linearize ? toLinear + 256 : null;
            var toLinear2 = linearize && src24bpp ? toLinear1 + 256 : null;
            for (var srcY = srcYStart; srcY < srcYStop; srcY++)
            {
                if (cancelExists && cancel()) return;
                var srcYStrideStep = srcY * srcStrideStep;
                var srcScanL = srcScan0_ + srcYStrideStep;
                var srcAdr = srcScanL + srcXStartBytes;
                var buff2dHori = buff2d_x0Ry2 + ((srcY - srcY0ry2) % ry2); // こちらのほうが呼び出し回数が多いところで空間的局所性が高い
                if (src24bpp)
                {
                    if (linearize)
                    {
                        if (srcXStepIsOne)
                        {
                            for (var srcX = srcXStart; srcX < srcXStop; srcX++)
                            {
                                buff1d_srcX0rx[srcX] = toLinear[*srcAdr++] | toLinear1[*srcAdr++] | toLinear2[*srcAdr++];
                            }
                        }
                        else
                        {
                            for (var srcX = srcXStart; srcX < srcXStop; srcX++)
                            {
                                buff1d_srcX0rx[srcX] = toLinear[*srcAdr++] | toLinear1[*srcAdr++] | toLinear2[*srcAdr];
                                srcAdr += srcBytesPerXSample11;
                            }
                        }

                        for (var dstX = x0; dstX < x1; dstX++)
                        {
                            var srcX = (dstX * txx + tx0) / txd;
                            var cx = cx0 + dstX * rx2;
                            var buff1dLocal = buff1d_srcX0 + srcX;
                            var v = interDesignRoundOffset;
                            
                            for (long i = 0; i < rx2; i++) v += cx[i] * buff1dLocal[i];
                            buff2dHori[dstX * ry2] = ((v >> interShift) & interMask) - interSignOffset;
                        }
                    }
                    else
                    {
                        if (srcXStepIsOne)
                        {
                            for (var srcX = srcXStart; srcX < srcXStop; srcX++)
                            {
                                // ここでは *srcAdr++ は効果がある
                                buff1d_srcX0rx[srcX] = *srcAdr++ | (ulong)*srcAdr++ << 22 | (ulong)*srcAdr++ << 43; // G に 0.5 のオフセットをつけるのは誤り
                            }
                        }
                        else
                        {
                            for (var srcX = srcXStart; srcX < srcXStop; srcX++)
                            {
                                buff1d_srcX0rx[srcX] = *srcAdr++ | (ulong)*srcAdr++ << 22 | (ulong)*srcAdr << 43;
                                srcAdr += srcBytesPerXSample11;
                            }
                        }

                        for (var dstX = x0; dstX < x1; dstX++)
                        {
                            var srcX = (dstX * txx + tx0) / txd;
                            var cx = cx0 + dstX * rx2;
                            var buff1dLocal = buff1d_srcX0 + srcX;
                            var v = interDesignRoundOffset;

                            // ここで 6 割の時間を消費
                            // x64 環境では long を使ったほうが高速
                            // ulong は long よりわずかに劣る
                            for (long i = 0; i < rx2; i++) v += cx[i] * buff1dLocal[i];
                            //for (long i = 0; i < rx2long; i++) v += *cx++ * *buff1dLocal++; // こちらのほうが低速

                            buff2dHori[dstX * ry2] = ((v >> interShift) & interMask) - interSignOffset;
                        }
                    }
                }
                else
                {
                    var srcAdr01 = srcScanL + srcXStartBytesMirror;
                    var srcScanL1 = srcScan0Mirror - srcYStrideStep;
                    if (linearize)
                    {
                        if (srcXStepIsOne)
                        {
                            for (var srcX = srcXStart; srcX < srcXStop; srcX++)
                            {
                                buff1d_srcX0rx[srcX] = toLinear[*srcAdr++] | toLinear1[*srcAdr01--];
                            }
                        }
                        else
                        {
                            for (var srcX = srcXStart; srcX < srcXStop; srcX++)
                            {
                                buff1d_srcX0rx[srcX] = toLinear[*srcAdr] | toLinear1[*srcAdr01];
                                srcAdr += srcBytesPerXSample;
                                srcAdr01 -= srcBytesPerXSample;
                            }
                        }

                        for (var dstX = x0; dstX < x1; dstX++)
                        {
                            var srcX = (dstX * txx + tx0) / txd;
                            var cx = cx0 + dstX * rx2;
                            var buff1dLocal = buff1d_srcX0 + srcX;
                            const ulong s0 = 1UL << (31 - 1); // シフト前符号オフセット
                            const int shift = 31 - grayLinearDigits;
                            const ulong s1 = 1UL << (grayLinearDigits - 1); // シフト後符号オフセット
                            const ulong r0 = 1UL << (shift - 1); // 丸めオフセット
                            const ulong v0 = s0 | r0;
                            const ulong m0 = ~(~0 << (grayLinearDigits + 1));
                            var v = v0 | v0 << 32;

                            for (long i = 0; i < rx2; i++) v += cx[i] * buff1dLocal[i];

                            buff2dHori[dstX * ry2] = ((v >> shift) & (m0 | m0 << 32)) - (s1 | s1 << 32);
                        }
                    }
                    else
                    {
                        var srcAdr10 = srcScanL1 + srcXStartBytes;
                        var srcAdr11 = srcScanL1 + srcXStartBytesMirror;
                        if (srcXStepIsOne)
                        {
                            for (var srcX = srcXStart; srcX < srcXStop; srcX++)
                            {
                                buff1d_srcX0rx[srcX] = *srcAdr++ | (ulong)*srcAdr01-- << 16 | (ulong)*srcAdr10++ << 32 | (ulong)*srcAdr11-- << 48;
                            }
                        }
                        else
                        {
                            for (var srcX = srcXStart; srcX < srcXStop; srcX++)
                            {
                                buff1d_srcX0rx[srcX] = *srcAdr | (ulong)*srcAdr01 << 16 | (ulong)*srcAdr10 << 32 | (ulong)*srcAdr11 << 48;
                                srcAdr += srcBytesPerXSample;
                                srcAdr01 -= srcBytesPerXSample;
                                srcAdr10 += srcBytesPerXSample;
                                srcAdr11 -= srcBytesPerXSample;
                            }
                        }

                        for (var dstX = x0; dstX < x1; dstX++)
                        {
                            var srcX = (dstX * txx + tx0) / txd;
                            var cx = cx0 + dstX * rx2;
                            var buff1dLocal = buff1d_srcX0 + srcX;
                            var v = 0x4040404040404040UL;

                            for (long i = 0; i < rx2; i++) v += cx[i] * buff1dLocal[i];

                            buff2dHori[dstX * ry2] = ((v >> 7) & 0x01ff01ff01ff01ff) - 0x0080008000800080;
                        }
                    }
                }

                // (srcY * tyd + iy0) / tyy で（リングバッファならぬ）ロールバッファの読み込み状態を基準に読める所まで進めることができるが、
                // 縮小時は srcY == srcYStep1 になっても最後まで到達しないので場合分けが必要
                // 拡大時は更に先まで行けてしまうので場合分けが必要 → 拡大時はソースがまばらなので到達しないこともある
                //var dstYBound = yUpScaling ? Math.Min((srcY * tyd + iy0) / tyy, y1) : srcY < srcYStop1 ? (srcY * tyd + iy0) / tyy : y1;
                var dstYBound = srcY < srcYStop1 ? Math.Min((srcY * tyd + iy0) / tyy, y1) : y1;
                for (; dstY < dstYBound; dstY++)
                {
                    var dstYStride = dstY * dstStride;
                    var dstAdr = dstScan0X0 + dstYStride;
                    var dstAdr01 = dstScan0X0Mirror01 + dstYStride;
                    var dstAdr10 = dstScan0X0Mirror10 - dstYStride;
                    var dstAdr11 = dstScan0X0Mirror11 - dstYStride;
                    var cy = cy0 + dstY * ry2;
                    var srcYsMod = ((dstY * tyy + ty0) / tyd - srcY0ry) % ry2;
                    var buff2dLeft = buff2d_x0Ry2 + srcYsMod;
                    var jLoop = ry2 - srcYsMod;
                    if (src24bpp)
                    {
                        if (linearize)
                        {
                            for (long dstX = x0; dstX < x1; dstX++)
                            {
                                var buff2dVert = buff2dLeft + dstX * ry2;
                                var v = unt2 << 19;

                                for (long j = 0; j < jLoop; j++) v += cy[j] * buff2dVert[j];
                                buff2dVert -= ry2;
                                for (long j = jLoop; j < ry2; j++) v += cy[j] * buff2dVert[j];

                                *dstAdr++ = fromLinearIn13Out20_gamma2_2(v & 0x1FFFFF);
                                *dstAdr++ = fromLinearIn14Out21_gamma2_2((v >> 21) & 0x3FFFFF);
                                *dstAdr++ = fromLinearIn13Out20_gamma2_2(v >> (21 + 22));
                            }
                        }
                        else
                        {
                            for (long dstX = x0; dstX < x1; dstX++)
                            {
                                var buff2dVert = buff2dLeft + dstX * ry2;
                                var v = ((unt2 << 11) + (unt2 << 19));

                                // ここで 1 割弱の時間を消費
                                for (long j = 0; j < jLoop; j++) v += cy[j] * buff2dVert[j];
                                buff2dVert -= ry2;
                                for (long j = jLoop; j < ry2; j++) v += cy[j] * buff2dVert[j];

                                ulong s;
                                s = v & (3UL << 19);
                                *dstAdr++ = (byte)(s == 0 ? 0 : s == (3UL << 19) ? 255 : ((v >> 12) - 128));
                                s = v & (3UL << 41);
                                *dstAdr++ = (byte)(s == 0 ? 0 : s == (3UL << 41) ? 255 : ((v >> 34) - 128));
                                s = v & (3UL << 62);
                                *dstAdr++ = (byte)(s == 0 ? 0 : s == (3UL << 62) ? 255 : ((v >> 55) - 128));
                            }
                        }
                    }
                    else if (dst24bpp)
                    {
                        if (linearize)
                        {
                            for (long dstX = x0; dstX < x1; dstX++)
                            {
                                var buff2dVert = buff2dLeft + dstX * ry2;
                                var v = 0x4000000040000000UL;

                                for (long j = 0; j < jLoop; j++) v += cy[j] * buff2dVert[j];
                                buff2dVert -= ry2;
                                for (long j = jLoop; j < ry2; j++) v += cy[j] * buff2dVert[j];
                                
                                byte vb;
                                vb = fromLinearIn18Out31_gamma2_2(v & 0xFFFFFFFFUL);
                                *dstAdr++ = vb; *dstAdr++ = vb; *dstAdr++ = vb;
                                vb = fromLinearIn18Out31_gamma2_2(v >> 32);
                                *dstAdr01-- = vb; *dstAdr01-- = vb; *dstAdr01-- = vb;
                            }
                        }
                        else
                        {
                            for (long dstX = x0; dstX < x1; dstX++)
                            {
                                var buff2dVert = buff2dLeft + dstX * ry2;
                                var v = 0x4040404040404040UL;

                                for (long j = 0; j < jLoop; j++) v += cy[j] * buff2dVert[j];
                                buff2dVert -= ry2;
                                for (long j = jLoop; j < ry2; j++) v += cy[j] * buff2dVert[j];

                                ulong s; byte vb;
                                s = v & 3UL << 14;
                                vb = (byte)(s == 0 ? 0 : s == (3UL << 14) ? 255 : ((v >> 7) - 128));
                                *dstAdr++ = vb; *dstAdr++ = vb; *dstAdr++ = vb;
                                s = v & 3UL << 30;
                                vb = (byte)(s == 0 ? 0 : s == (3UL << 30) ? 255 : ((v >> 23) - 128));
                                *dstAdr01-- = vb; *dstAdr01-- = vb; *dstAdr01-- = vb;
                                s = v & 3UL << 46;
                                vb = (byte)(s == 0 ? 0 : s == (3UL << 46) ? 255 : ((v >> 39) - 128));
                                *dstAdr10++ = vb; *dstAdr10++ = vb; *dstAdr10++ = vb;
                                s = v & 3UL << 62;
                                vb = (byte)(s == 0 ? 0 : s == (3UL << 62) ? 255 : ((v >> 55) - 128));
                                *dstAdr11-- = vb; *dstAdr11-- = vb; *dstAdr11-- = vb;
                            }
                        }
                    }
                    else
                    {
                        if (linearize)
                        {
                            for (long dstX = x0; dstX < x1; dstX++)
                            {
                                var buff2dVert = buff2dLeft + dstX * ry2;
                                var v = 0x4000000040000000UL;

                                for (long j = 0; j < jLoop; j++) v += cy[j] * buff2dVert[j];
                                buff2dVert -= ry2;
                                for (long j = jLoop; j < ry2; j++) v += cy[j] * buff2dVert[j];
                                
                                *dstAdr++ = fromLinearIn18Out31_gamma2_2(v & 0xFFFFFFFFUL);
                                *dstAdr01-- = fromLinearIn18Out31_gamma2_2(v >> 32);
                            }
                        }
                        else
                        {
                            for (long dstX = x0; dstX < x1; dstX++)
                            {
                                var buff2dVert = buff2dLeft + dstX * ry2;
                                var v = 0x4040404040404040UL;

                                for (long j = 0; j < jLoop; j++) v += cy[j] * buff2dVert[j];
                                buff2dVert -= ry2;
                                for (long j = jLoop; j < ry2; j++) v += cy[j] * buff2dVert[j];

                                ulong s;
                                s = v & 3UL << 14;
                                *dstAdr++ = (byte)(s == 0 ? 0 : s == (3UL << 14) ? 255 : ((v >> 7) - 128));
                                s = v & 3UL << 30;
                                *dstAdr01-- = (byte)(s == 0 ? 0 : s == (3UL << 30) ? 255 : ((v >> 23) - 128));
                                s = v & 3UL << 46;
                                *dstAdr10++ = (byte)(s == 0 ? 0 : s == (3UL << 46) ? 255 : ((v >> 39) - 128));
                                s = v & 3UL << 62;
                                *dstAdr11-- = (byte)(s == 0 ? 0 : s == (3UL << 62) ? 255 : ((v >> 55) - 128));
                            }
                        }
                    }
                }
            }
        }

        private static readonly IntPtr toLinear131413Array2_2 = getToLinear131413Array2_2();
        private static IntPtr getToLinear131413Array2_2()
        {
            var result = Marshal.AllocCoTaskMem(3 * 256 * sizeof(ulong));
            unsafe
            {
                var adr = (ulong*)result;
                for (var i = 0; i < 256; i++)
                {
                    var v = (ulong)(toLinear13_gamma2_2(i) + 0.5);
                    adr[i] = v;
                    adr[i + 256] = (ulong)(toLinear14_gamma2_2(i) + 0.5) << 21;
                    adr[i + 512] = v << (21 + 22);
                }
            }
            return result;
        }

        private static readonly IntPtr toLinear18Array2_2 = getToLinear18Array2_2();
        private static IntPtr getToLinear18Array2_2()
        {
            var result = Marshal.AllocCoTaskMem(2 * 256 * sizeof(ulong));
            unsafe
            {
                var adr = (ulong*)result;
                for (var i = 0; i < 256; i++)
                {
                    var v = (ulong)(toLinear18_gamma2_2(i) + 0.5);
                    adr[i] = v;
                    adr[i + 256] = v << 32;
                }
            }
            return result;
        }

#if DEBUG
        public static double toLinear13_gamma2_2(double b)
#else
        private static double toLinear13_gamma2_2(double b)
#endif
        {
            const double gamma = 2.2;
            const int inBits = 13;
            const double bt = 7.340082380389742;
            const double t = 0.0004887902940293499;

            if (b < bt)
            {
                return b;
            }
            else
            {
                return ((1UL << inBits) - 1) * ((1 - t) * Math.Pow(b / 255, gamma) + t);
            }
        }


#if DEBUG
        public static double toLinear14_gamma2_2(double b)
#else
        private static double toLinear14_gamma2_2(double b)
#endif
        {
            const double gamma = 2.2;
            const int inBits = 14;
            const double bt = 4.118064975095984;
            const double t = 0.00013710658970538143;

            if (b < bt)
            {
                return b;
            }
            else
            {
                return ((1UL << inBits) - 1) * ((1 - t) * Math.Pow(b / 255, gamma) + t);
            }
        }

#if DEBUG
        public static double toLinear18_gamma2_2(double b)
#else
        private static double toLinear18_gamma2_2(double b)
#endif
        {
            const double gamma = 2.2;
            return ((1UL << 18) - 1) * Math.Pow(b / 255, gamma);
        }

#if DEBUG
        const string returnCode = "\r\n"; 
        public static string CreateFromLinear(int bitsDiff, string name, Func<double,double> toLinear, ulong offset)
        {
            return
                $"private static byte {name}(ulong linear){returnCode}" +
                "{" + returnCode +
                CreateFromLinear("    ", 255, 0, 1UL << bitsDiff, toLinear, offset)
                + "}" + returnCode;
        }
        private static string CreateFromLinear(string indent, int upper, int lower, double valueDif, Func<double, double> toLinear, ulong offset)
        {
            if (upper > lower + 1)
            {
                var m0 = (upper + lower) / 2;
                var m1 = m0 + 1;
                var newIndent = indent + "    ";
                return
                    $"{indent}if (linear <= {(ulong)(valueDif * toLinear(Math.Sqrt(m0 * m1)) + 0.5) + offset}){returnCode}" +
                    CreateFromLinear(newIndent, m0, lower, valueDif, toLinear, offset) +
                    $"{indent}else{returnCode}" +
                    CreateFromLinear(newIndent, upper, m1, valueDif, toLinear, offset);
            }
            else
            {
                return
                    $"{indent}if (linear <= {(ulong)(valueDif * toLinear(Math.Sqrt(upper * lower)) + 0.5) + offset}){returnCode}" +
                    $"{indent}    return {lower};{returnCode}" +
                    $"{indent}else{returnCode}" +
                    $"{indent}    return {upper};{returnCode}";
            }
        }
#endif

        //private const double doubleEpsilon = 2.220446049250313080847263336181640625e-16; // 2^(1-53), 厳密値

        private static int getSuitabgleDigits(long r2)
        {
            if (r2 < 4) return 10;
            if (r2 < 64) return 11;
            return 12;
        }

        private static ulong[] getWeightArray(out int count, bool half, TryGetWeightDelegate1 tryGetWeight1, TryGetWeightDelegate2 tryGetWeight2, int srcLength, int dstLength, int step, int digitsWithoutSignBit)
        {
            if (dstLength <= 0 || srcLength <= 0)
            {
                count = 0;
                return new ulong[0];
            }

            var radius = getRadius(tryGetWeight1, tryGetWeight2, srcLength, dstLength, step);
            var minusRadius = -radius;
            count = radius * 2;

            var dstLength_ = (long)dstLength;
            var srcLength_ = (long)srcLength;

            long dstLength2 = dstLength_ * 2;
            long dstLengthStep = dstLength_ * step;
            long dstLength2Step = dstLength2 * step;
            double power = (double)dstLengthStep / srcLength_;
            var srcLengthPerStepCeiling = (srcLength_ + step - 1) / step;

            var resultLength = half ? (dstLength_ + 1) / 2 : dstLength_;
            var useTryGetWeight1 = tryGetWeight1 != null;
            var useDistanceAsDst = power < 1;

            var dstLength2Step1 = dstLength2Step - 1;
            
            var countl = (long)count;
            var result = new ulong[countl * resultLength];
            var resultI = new double[countl];

            if (digitsWithoutSignBit <= 0) digitsWithoutSignBit = getSuitabgleDigits(countl);
            var shift = 1 << digitsWithoutSignBit;

            // 1次元直流保証
            //var allowance = (1 << Math.Max(0, digitsWithoutSignBit - 9)) - 1;

            // 2次元直流保証
            //var allowance = (1 << Math.Max(0, digitsWithoutSignBit - 10)) - 1;

            // 直流優先
            const int allowance = 0;
            
            var bufferSize = countl / 2 - allowance; // 全ての数の端数が 0.5 だった場合に -count / 2 小さくなる
            var doubleBuffer = bufferSize <= 0 ? null : new double[bufferSize];
            var intBuffer = bufferSize <= 0 ? null : new int[bufferSize];

            // ZipPla CE: seek（と distance）は step ごとに間引いた後の単位なので、右端・下端も間引いた後の数（core の srcXU・srcYU と同じ）で判定する。
            // 以前は間引く前の srcLength で判定していたので、step が 2 以上のとき端からはみ出した重みが端に寄せられず、
            // core が書いていないバッファの外（並列では前のタイルの残り、並列でなければ 0）を読み、右端・下端の数列がおかしな値になっていた
            var srcLengthMinusCount = srcLengthPerStepCeiling - countl;
            var srcLengthMinus1 = srcLengthPerStepCeiling - 1;

            for (long i = 0; i < resultLength; i++)
            {
                var iCount = i * countl;

                var iAsSrcTimesDstLength2 = (2 * i + 1) * srcLength_ - dstLength_;
                var iCeilingAsSrcPerStepMinusRadius = (iAsSrcTimesDstLength2 + dstLength2Step1) / dstLength2Step - radius; // 分子は負の数にできないので radius は通分してはいけない
                var iAsSrcPerStep = (double)iAsSrcTimesDstLength2 / dstLength2Step;
                
                if (0 <= iCeilingAsSrcPerStepMinusRadius)
                {
                    if (iCeilingAsSrcPerStepMinusRadius <= srcLengthMinusCount)
                    {
                        var jToDistanceOffset = iCeilingAsSrcPerStepMinusRadius - iAsSrcPerStep;

                        for (long j = 0; j < countl; j++)
                        {
                            double distance = jToDistanceOffset + j;
                            double weight;
                            if (useTryGetWeight1 ? tryGetWeight1(useDistanceAsDst ? power * distance : distance, out weight) : tryGetWeight2(distance, power, out weight))
                            {
                                resultI[j] = weight;
                            }
                            else
                            {
                                resultI[j] = 0;
                            }
                        }
                    }
                    else
                    {
                        Array.Clear(resultI, 0, count);
                        for (long j = 0; j < countl; j++)
                        {
                            var seek = iCeilingAsSrcPerStepMinusRadius + j;
                            double distance = (seek - iAsSrcPerStep);
                            double weight;
                            if (useTryGetWeight1 ? tryGetWeight1(useDistanceAsDst ? power * distance : distance, out weight) : tryGetWeight2(distance, power, out weight))
                            {
                                if (seek > srcLengthMinus1)
                                {
                                    resultI[j - (seek - srcLengthMinus1)] += weight;
                                }
                                else
                                {
                                    resultI[j] += weight;
                                }
                            }
                        }
                    }
                }
                else
                {
                    Array.Clear(resultI, 0, count);
                    if (iCeilingAsSrcPerStepMinusRadius <= srcLengthMinusCount)
                    {
                        for (long j = 0; j < countl; j++)
                        {
                            var seek = iCeilingAsSrcPerStepMinusRadius + j;
                            double distance = (seek - iAsSrcPerStep);
                            double weight;
                            if (useTryGetWeight1 ? tryGetWeight1(useDistanceAsDst ? power * distance : distance, out weight) : tryGetWeight2(distance, power, out weight))
                            {
                                if (seek < 0)
                                {
                                    resultI[j - seek] += weight;
                                }
                                else
                                {
                                    resultI[j] += weight;
                                }
                            }
                        }
                    }
                    else
                    {
                        for (long j = 0; j < countl; j++)
                        {
                            var seek = iCeilingAsSrcPerStepMinusRadius + j;
                            double distance = (seek - iAsSrcPerStep);
                            double weight;
                            if (useTryGetWeight1 ? tryGetWeight1(useDistanceAsDst ? power * distance : distance, out weight) : tryGetWeight2(distance, power, out weight))
                            {
                                if (seek < 0)
                                {
                                    resultI[j - seek] += weight;
                                }
                                else if (seek > srcLengthMinus1)
                                {
                                    resultI[j - (seek - srcLengthMinus1)] += weight;
                                }
                                else
                                {
                                    resultI[j] += weight;
                                }
                            }
                        }
                    }
                }

                Array.Copy(rigorousNormalization(shift, allowance, resultI, doubleBuffer, intBuffer), 0, result, i * countl, countl);
            }

            return result;
        }
        
        private static int getRadius(TryGetWeightDelegate1 tryGetWeight1, TryGetWeightDelegate2 tryGetWeight2, int srcLength, int dstLength, int step)
        {
            var result = 1;
            long dstLengthStep = dstLength * step;
            double weight;
            const double errorGuarantee = (1 + 1e-8);
            if (tryGetWeight1 != null)
            {
                if (dstLengthStep > srcLength)
                {
                    while (tryGetWeight1((result + 1) * errorGuarantee, out weight)) result++;
                }
                else
                {
                    var power = errorGuarantee * dstLengthStep / srcLength;
                    while (tryGetWeight1((result + 1) * power, out weight)) result++;
                }
            }
            else
            {
                var power = (double)dstLengthStep / srcLength;
                while (tryGetWeight2((result + 1) * errorGuarantee, power, out weight)) result++;
            }

            return result;
        }

        
        private static ulong[] rigorousNormalization(long total, long allowance, double[] input, double[] doubleBuffer, int[] intBuffer)
        {
            if (total == 0) return new ulong[input.Length];
            var sum = input.Sum() / total;
            if (sum == 0) return new ulong[input.Length];
            var result = new long[input.Length];
            for (var k = 0; k < input.Length; k++)
            {
                var v = input[k] / sum;
                input[k] = v;
                var i = (long)Math.Round(v);
                result[k] = i;
                total -= i;
            }

            if (allowance < 0) allowance = 0;

            var over = Math.Abs(total) - allowance;
            if (over > 0)
            {
                var bound = 0.0;
                var tail = over - 1;
                var over1 = over - 1;
                var targetValues = doubleBuffer;
                var targetIndices = intBuffer;

                if (total > 0)
                {
                    for (var k = 0; k < input.Length; k++)
                    {
                        var sub = input[k] - result[k];
                        if (sub > bound)
                        {
                            var insert = tail;
                            for (; insert < over1; insert++)
                            {
                                var insert1 = insert + 1;
                                var t = targetValues[insert1];
                                if (sub > t)
                                {
                                    targetIndices[insert] = targetIndices[insert1];
                                    targetValues[insert] = t;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            targetIndices[insert] = k;
                            targetValues[insert] = sub;
                            if (tail > 0) tail--;
                            else bound = targetValues[0];
                        }
                    }

                    for (var i = 0; i < over; i++)
                    {
                        result[targetIndices[i]]++;
                    }
                }
                else
                {
                    for (var k = 0; k < input.Length; k++)
                    {
                        var sub = input[k] - result[k];
                        if (sub < bound)
                        {
                            var insert = tail;
                            for (; insert < over1; insert++)
                            {
                                var insert1 = insert + 1;
                                var t = targetValues[insert1];
                                if (sub < t)
                                {
                                    targetIndices[insert] = targetIndices[insert1];
                                    targetValues[insert] = t;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            targetIndices[insert] = k;
                            targetValues[insert] = sub;
                            if (tail > 0) tail--;
                            else bound = targetValues[0];
                        }
                    }

                    for (var i = 0; i < over; i++)
                    {
                        result[targetIndices[i]]--;
                    }
                }
            }

            return result.Select(i =>(ulong)i).ToArray();
        }
        
#region 最近傍法の個別実装
        public static Bitmap NearestNeighbor(Bitmap src, Size newSize, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            return nearestNeighbor(src, newSize, parallel, backgroundWorker,
                accurate: newSize.Width % src.Width != 0 || newSize.Height % src.Height != 0);
        }
        public static void NearestNeighbor(Bitmap src, Bitmap dst, Rectangle dstRectangle, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            DrawResizedBitmapByNearestNeighbor(src, 0, 0, src.Width, src.Height, dst,
                dstRectangle.X, dstRectangle.Y, dstRectangle.Width, dstRectangle.Height,
                parallel, backgroundWorker, accurate: dstRectangle.Width % src.Width != 0 || dstRectangle.Height % src.Height != 0);
        }
        public static void NearestNeighbor(BitmapData src, Rectangle srcRectangle, BitmapData dst, Rectangle dstRectangle, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            DrawResizedBitmapByNearestNeighbor(src, srcRectangle.X, srcRectangle.Y, srcRectangle.Width, srcRectangle.Height, dst,
                dstRectangle.X, dstRectangle.Y, dstRectangle.Width, dstRectangle.Height,
                parallel, backgroundWorker, accurate: dstRectangle.Width % srcRectangle.Width != 0 || dstRectangle.Height % srcRectangle.Height != 0);
        }

        private static Bitmap nearestNeighbor(Bitmap src, Size newSize, bool parallel, BackgroundWorker backgroundWorker, bool accurate, bool convertTo24bpp = false)
        {
            var result = new Bitmap(newSize.Width, newSize.Height,
                !convertTo24bpp && src.PixelFormat == PixelFormat.Format8bppIndexed ?
                PixelFormat.Format8bppIndexed : PixelFormat.Format24bppRgb);
            try
            {
                DrawResizedBitmapByNearestNeighbor(src, 0, 0, src.Width, src.Height, result, 0, 0, newSize.Width, newSize.Height,
                    parallel, backgroundWorker, accurate);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static void DrawResizedBitmapByNearestNeighbor(
            Bitmap src, int srcX, int srcY, int srcWidth, int srcHeight,
            Bitmap dst, int dstX, int dstY, int dstWidth, int dstHeight,
            bool parallel, BackgroundWorker backgroundWorker, bool accurate)
        {
            var backgroundWorkerExists = backgroundWorker != null;
            if (backgroundWorkerExists && backgroundWorker.CancellationPending) return;

            if (src.PixelFormat == PixelFormat.Format8bppIndexed)
            {
                if (dst.PixelFormat == PixelFormat.Format8bppIndexed)
                {
                    dst.Palette = src.Palette;
                    var dstData = dst.LockBits(new Rectangle(dstX, dstY, dstWidth, dstHeight), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                    try
                    {
                        DrawResizedBitmapByNearestNeighbor(src, srcX, srcY, srcWidth, srcHeight, dstData, 0, 0, dstWidth, dstHeight, parallel, backgroundWorker, accurate);
                    }
                    finally
                    {
                        dst.UnlockBits(dstData);
                    }
                }
                else
                {
                    var dstData = dst.LockBits(new Rectangle(dstX, dstY, dstWidth, dstHeight), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        DrawResizedBitmapByNearestNeighbor(src, srcX, srcY, srcWidth, srcHeight, dstData, 0, 0, dstWidth, dstHeight, parallel, backgroundWorker, accurate);
                    }
                    finally
                    {
                        dst.UnlockBits(dstData);
                    }
                }
            }
            else
            {
                var dstData = dst.LockBits(new Rectangle(dstX, dstY, dstWidth, dstHeight), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try
                {
                    DrawResizedBitmapByNearestNeighbor(src, srcX, srcY, srcWidth, srcHeight, dstData, 0, 0, dstWidth, dstHeight, parallel, backgroundWorker, accurate);
                }
                finally
                {
                    dst.UnlockBits(dstData);
                }
            }
        }

        private static void myFor(bool parallel, int fromInclusive, int toExclusive, Func<int, bool> body)
        {
            if (parallel)
            {
                Parallel.For(fromInclusive, toExclusive, (i, loop) =>
                {
                    if (!body(i)) loop.Break();
                });
            }
            else
            {
                for (var i = fromInclusive; i < toExclusive; i++)
                {
                    if (!body(i)) break;
                }
            }
        }

        private static void DrawResizedBitmapByNearestNeighbor(
            Bitmap src, int srcX, int srcY, int srcWidth, int srcHeight,
            BitmapData dstData, int dstX, int dstY, int dstWidth, int dstHeight,
            bool parallel, BackgroundWorker backgroundWorker, bool accurate)
        {
            var backgroundWorkerExists = backgroundWorker != null;
            if (backgroundWorkerExists && backgroundWorker.CancellationPending) return;

            if (src.PixelFormat == PixelFormat.Format8bppIndexed)
            {
                var srcData = src.LockBits(new Rectangle(srcX, srcY, srcWidth, srcHeight), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
                try
                {
                    DrawResizedBitmapByNearestNeighbor(srcData, 0, 0, srcWidth, srcHeight, dstData, dstX, dstY, dstWidth, dstHeight, parallel, backgroundWorker, accurate);
                }
                finally
                {
                    src.UnlockBits(srcData);
                }
            }
            else
            {
                var srcData = src.LockBits(new Rectangle(srcX, srcY, srcWidth, srcHeight), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    DrawResizedBitmapByNearestNeighbor(srcData, 0, 0, srcWidth, srcHeight, dstData, dstX, dstY, dstWidth, dstHeight, parallel, backgroundWorker, accurate);
                }
                finally
                {
                    src.UnlockBits(srcData);
                }
            }
        }

        private static void DrawResizedBitmapByNearestNeighbor(
            BitmapData srcData, int srcX, int srcY, int srcWidth, int srcHeight,
            BitmapData dstData, int dstX, int dstY, int dstWidth, int dstHeight,
            bool parallel, BackgroundWorker backgroundWorker, bool accurate)
        {
            var backgroundWorkerExists = backgroundWorker != null;
            if (backgroundWorkerExists && backgroundWorker.CancellationPending) return;

            int dstXStop = dstX + dstWidth;
            int dstYStop = dstY + dstHeight;

            if (srcData.PixelFormat == PixelFormat.Format8bppIndexed)
            {
                if (dstData.PixelFormat == PixelFormat.Format8bppIndexed)
                {
                    unsafe
                    {
                        var srcStride = srcData.Stride;
                        var src0 = (byte*)srcData.Scan0 + srcX + srcY * srcStride;
                        var dst0 = (byte*)dstData.Scan0;
                        var dstStride = dstData.Stride;

                        if (accurate)
                        {
                            myFor(parallel, dstY, dstYStop, y =>
                            {
                                if (backgroundWorkerExists && backgroundWorker.CancellationPending)
                                {
                                    return false;
                                }
                                var dstLeft = dst0 + y * dstStride;
                                var ySrc = ((2 * (y - dstY) + 1) * srcHeight) / (2 * dstHeight);
                                var srcLeft = src0 + ySrc * srcStride;
                                for (var x = dstX; x < dstXStop; x++)
                                {
                                    var xSrc = ((2 * (x - dstX) + 1) * srcWidth) / (2 * dstWidth);
                                    var adrDst = dstLeft + x;
                                    var adrSrc = srcLeft + xSrc;
                                    var v = adrSrc[0];
                                    adrDst[0] = v;
                                }
                                return true;
                            });
                        }
                        else
                        {
                            myFor(parallel, dstY, dstYStop, y =>
                            {
                                if (backgroundWorkerExists && backgroundWorker.CancellationPending)
                                {
                                    return false;
                                }
                                var dstLeft = dst0 + y * dstStride;
                                var ySrc = ((y - dstY) * srcHeight) / dstHeight;
                                var srcLeft = src0 + ySrc * srcStride;
                                for (var x = dstX; x < dstXStop; x++)
                                {
                                    var xSrc = ((x - dstX) * srcWidth) / dstWidth;
                                    var adrDst = dstLeft + x;
                                    var adrSrc = srcLeft + xSrc;
                                    var v = adrSrc[0];
                                    adrDst[0] = v;
                                }
                                return true;
                            });
                        }
                    }
                }
                else
                {
                    unsafe
                    {
                        var srcStride = srcData.Stride;
                        var src0 = (byte*)srcData.Scan0 + srcX + srcY * srcStride;
                        var dst0 = (byte*)dstData.Scan0;
                        var dstStride = dstData.Stride;

                        if (accurate)
                        {
                            myFor(parallel, dstY, dstYStop, y =>
                            {
                                if (backgroundWorkerExists && backgroundWorker.CancellationPending)
                                {
                                    return false;
                                }
                                var dstLeft = dst0 + y * dstStride;
                                var ySrc = ((2 * (y - dstY) + 1) * srcHeight) / (2 * dstHeight);
                                var srcLeft = src0 + ySrc * srcStride;
                                for (var x = dstX; x < dstXStop; x++)
                                {
                                    var xSrc = ((2 * (x - dstX) + 1) * srcWidth) / (2 * dstWidth);
                                    var adrDst = dstLeft + x * 3;
                                    var adrSrc = srcLeft + xSrc;
                                    var v = adrSrc[0];
                                    adrDst[0] = v;
                                    adrDst[1] = v;
                                    adrDst[2] = v;
                                }
                                return true;
                            });
                        }
                        else
                        {
                            myFor(parallel, dstY, dstYStop, y =>
                            {
                                if (backgroundWorkerExists && backgroundWorker.CancellationPending)
                                {
                                    return false;
                                }
                                var dstLeft = dst0 + y * dstStride;
                                var ySrc = ((y - dstY) * srcHeight) / dstHeight;
                                var srcLeft = src0 + ySrc * srcStride;
                                for (var x = dstX; x < dstXStop; x++)
                                {
                                    var xSrc = ((x - dstX) * srcWidth) / dstWidth;
                                    var adrDst = dstLeft + x * 3;
                                    var adrSrc = srcLeft + xSrc;
                                    var v = adrSrc[0];
                                    adrDst[0] = v;
                                    adrDst[1] = v;
                                    adrDst[2] = v;
                                }
                                return true;
                            });
                        }
                    }
                }
            }
            else
            {
                unsafe
                {
                    var srcStride = srcData.Stride;
                    var src0 = (byte*)srcData.Scan0 + srcX * 3 + srcY * srcStride;
                    var dst0 = (byte*)dstData.Scan0;
                    var dstStride = dstData.Stride;

                    if (accurate)
                    {
                        myFor(parallel, dstY, dstYStop, y =>
                        {
                            if (backgroundWorkerExists && backgroundWorker.CancellationPending)
                            {
                                return false;
                            }
                            var dstLeft = dst0 + y * dstStride;
                            var ySrc = ((2 * (y - dstY) + 1) * srcHeight) / (2 * dstHeight);
                            var srcLeft = src0 + ySrc * srcStride;
                            for (var x = dstX; x < dstXStop; x++)
                            {
                                var xSrc = ((2 * (x - dstX) + 1) * srcWidth) / (2 * dstWidth);
                                var adrDst = dstLeft + x * 3;
                                var adrSrc = srcLeft + xSrc * 3;
                                adrDst[0] = adrSrc[0];
                                adrDst[1] = adrSrc[1];
                                adrDst[2] = adrSrc[2];
                            }
                            return true;
                        });
                    }
                    else
                    {
                        myFor(parallel, dstY, dstYStop, y =>
                        {
                            if (backgroundWorkerExists && backgroundWorker.CancellationPending)
                            {
                                return false;
                            }
                            var dstLeft = dst0 + y * dstStride;
                            var ySrc = ((y - dstY) * srcHeight) / dstHeight;
                            var srcLeft = src0 + ySrc * srcStride;
                            for (var x = dstX; x < dstXStop; x++)
                            {
                                var xSrc = ((x - dstX) * srcWidth) / dstWidth;
                                var adrDst = dstLeft + x * 3;
                                var adrSrc = srcLeft + xSrc * 3;
                                adrDst[0] = adrSrc[0];
                                adrDst[1] = adrSrc[1];
                                adrDst[2] = adrSrc[2];
                            }
                            return true;
                        });
                    }
                }
            }
        }
        
        #endregion
    }


}
