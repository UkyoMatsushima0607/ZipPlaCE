using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;

namespace ZipPla
{
    public static class QuickGraphic
    {
        /// <summary>
        /// 2 倍のアンチエイリアスで、出力の列ごとに使う入力の 2 列。以前は画素ごとに整数の割り算 2 回で求め直していた（計算式は同じ）
        /// </summary>
        private sealed class AntiAliasing2xXTable
        {
            public readonly int[] Src0, Src1;
            public AntiAliasing2xXTable(int srcWidth, int dstWidth)
            {
                Src0 = new int[dstWidth];
                Src1 = new int[dstWidth];
                for (var xDst = 0; xDst < dstWidth; xDst++)
                {
                    Src0[xDst] = ((xDst << 2) + 1) * srcWidth / (dstWidth << 2);
                    Src1[xDst] = ((xDst << 2) + 3) * srcWidth / (dstWidth << 2);
                }
            }
        }

        /// <summary>
        /// 双線形補間で、出力の列ごとに使う入力の 2 列と重み。以前は画素ごとに浮動小数点の割り算と Floor で求め直していた（計算式は同じ）
        /// </summary>
        private sealed class BilinearXTable
        {
            public readonly int[] Src0, Src1;
            public readonly double[] T1;
            public BilinearXTable(int srcWidth, int dstWidth)
            {
                Src0 = new int[dstWidth];
                Src1 = new int[dstWidth];
                T1 = new double[dstWidth];
                for (var xDst = 0; xDst < dstWidth; xDst++)
                {
                    var xSrc = (xDst + 0.5) * srcWidth / dstWidth - 0.5;
                    var xSrc0d = Math.Floor(xSrc);
                    T1[xDst] = xSrc - xSrc0d;
                    var xSrc0 = (int)xSrc0d;
                    var xSrc1 = xSrc0 + 1;
                    if (xSrc0 < 0) xSrc0 = 0;
                    else if (xSrc1 >= srcWidth) xSrc1 = srcWidth - 1;
                    Src0[xDst] = xSrc0;
                    Src1[xDst] = xSrc1;
                }
            }
        }

        public static Bitmap CreateNew(Bitmap src, Size newSize, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            //var result = new Bitmap(newSize.Width, newSize.Height, PixelFormat.Format24bppRgb);
            var result = new Bitmap(newSize.Width, newSize.Height, src.PixelFormat);
            try
            {
                if ((src.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
                {
                    result.Palette = src.Palette;
                }
                DrawImage(result, new Rectangle(0, 0, newSize.Width, newSize.Height), src, parallel, backgroundWorker);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        public static void DrawImage(Bitmap dstBitmap, RectangleF dstRectangle, Bitmap srcImage, bool parallel, BackgroundWorker backgroundWorker)
        {
            DrawImage(dstBitmap, new Rectangle((int)Math.Round(dstRectangle.X), (int)Math.Round(dstRectangle.Y), (int)Math.Round(dstRectangle.Width), (int)Math.Round(dstRectangle.Height)), srcImage, parallel, backgroundWorker);
        }

        public static void DrawImage(BitmapData dstData, Rectangle dstRectangle, BitmapData srcData, bool parallel, BackgroundWorker backgroundWorker)
        {
            DrawBitmap(dstData, dstRectangle, srcData, parallel, backgroundWorker);
        }

        public static void DrawImage(Bitmap dstBitmap, Rectangle dstRectangle, Bitmap srcImage, bool parallel, BackgroundWorker backgroundWorker)
        {
            if (srcImage is Bitmap srcBitmap)
            {
                DrawBitmap(dstBitmap, dstRectangle, srcBitmap, parallel, backgroundWorker);
            }
            else
            {
                using (srcBitmap = new Bitmap(srcImage))
                {
                    DrawBitmap(dstBitmap, dstRectangle, srcBitmap, parallel, backgroundWorker);
                }
            }
        }

        public static PixelFormat FlattenFormat(PixelFormat pixelFormat)
        {
            if((pixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
            {
                return PixelFormat.Format24bppRgb;
            }
            else
            {
                return pixelFormat;
            }
            
        }

        private static void DrawBitmap(Bitmap dstBitmap, Rectangle dstRectangle, Bitmap srcBitmap, bool parallel, BackgroundWorker backgroundWorker)
        {
            var srcWidth = srcBitmap.Width;
            var srcHeight = srcBitmap.Height;
            //var pixelFormat = FlattenFormat(srcBitmap.PixelFormat);
            var srcPixelFormat = srcBitmap.PixelFormat;
            var dstPixelFormat = dstBitmap.PixelFormat;
            var srcBytesPerPixel = Image.GetPixelFormatSize(srcPixelFormat) >> 3;
            var dstBytesPerPixel = Image.GetPixelFormatSize(dstPixelFormat) >> 3;
            if (dstBytesPerPixel < srcBytesPerPixel) throw new ArgumentException(null, "dstBitmap");
            var dstImage = dstBitmap.LockBits(dstRectangle, System.Drawing.Imaging.ImageLockMode.WriteOnly, dstPixelFormat);
            try
            {
                var srcImage = srcBitmap.LockBits(new Rectangle(0, 0, srcWidth, srcHeight), System.Drawing.Imaging.ImageLockMode.ReadOnly, srcPixelFormat);
                try
                {
                    DrawBitmap(dstImage, new Rectangle(0,0, dstRectangle.Width, dstRectangle.Height), srcImage, parallel, backgroundWorker);
                }
                finally
                {
                    srcBitmap.UnlockBits(srcImage);
                }
            }
            finally
            {
                dstBitmap.UnlockBits(dstImage);
            }
        }

        private static void DrawBitmap(BitmapData dstImage, Rectangle dstRectangle, Bitmap srcBitmap, bool parallel, BackgroundWorker backgroundWorker)
        {
            var srcWidth = srcBitmap.Width;
            var srcHeight = srcBitmap.Height;
            //var pixelFormat = FlattenFormat(srcBitmap.PixelFormat);
            var srcPixelFormat = srcBitmap.PixelFormat;
            var dstPixelFormat = dstImage.PixelFormat;
            var srcBytesPerPixel = Image.GetPixelFormatSize(srcPixelFormat) >> 3;
            var dstBytesPerPixel = Image.GetPixelFormatSize(dstPixelFormat) >> 3;
            if (dstBytesPerPixel < srcBytesPerPixel) throw new ArgumentException(null, "dstBitmap");
            var srcImage = srcBitmap.LockBits(new Rectangle(0, 0, srcWidth, srcHeight), System.Drawing.Imaging.ImageLockMode.ReadOnly, srcPixelFormat);
            try
            {
                DrawBitmap(dstImage, dstRectangle, srcImage, parallel, backgroundWorker);
            }
            finally
            {
                srcBitmap.UnlockBits(srcImage);
            }
        }

        private static void DrawBitmap(BitmapData dstImage, Rectangle dstRectangle, BitmapData srcImage, bool parallel, BackgroundWorker backgroundWorker)
        {
            var srcWidth = srcImage.Width;
            var srcHeight = srcImage.Height;
            //var pixelFormat = FlattenFormat(srcBitmap.PixelFormat);
            var srcPixelFormat = srcImage.PixelFormat;
            var dstPixelFormat = dstImage.PixelFormat;
            var srcBytesPerPixel = Image.GetPixelFormatSize(srcPixelFormat) >> 3;
            var dstBytesPerPixel = Image.GetPixelFormatSize(dstPixelFormat) >> 3;
            if (dstBytesPerPixel < srcBytesPerPixel) throw new ArgumentException(null, "dstBitmap");

            var srcStride = srcImage.Stride;
            var dstStride = dstImage.Stride;
            var dstSize = dstRectangle.Size;
            unsafe
            {
                var srcAddress = (byte*)srcImage.Scan0;
                var dstAddress = (byte*)(dstImage.Scan0 + dstBytesPerPixel * dstRectangle.X + dstStride * dstRectangle.Y);
                if ((srcPixelFormat & PixelFormat.Alpha) == 0)
                {
                    if (srcWidth < (dstSize.Width << 1) || srcHeight < (dstSize.Height << 1))
                    {
                        Bilinear(dstAddress, dstSize.Width, dstSize.Height, dstStride, dstBytesPerPixel, srcAddress, srcWidth, srcHeight, srcStride, srcBytesPerPixel, parallel, backgroundWorker);
                    }
                    else
                    {
                        AntiAliasing2x(dstAddress, dstSize.Width, dstSize.Height, dstStride, dstBytesPerPixel, srcAddress, srcWidth, srcHeight, srcStride, srcBytesPerPixel, parallel, backgroundWorker);
                    }
                }
                else
                {
                    if (srcWidth < (dstSize.Width << 1) || srcHeight < (dstSize.Height << 1))
                    {
                        BilinearForAlpha(dstAddress, dstSize.Width, dstSize.Height, dstStride, dstBytesPerPixel, srcAddress, srcWidth, srcHeight, srcStride, srcBytesPerPixel, parallel, backgroundWorker);
                    }
                    else
                    {
                        AntiAliasing2xForAlpha(dstAddress, dstSize.Width, dstSize.Height, dstStride, dstBytesPerPixel, srcAddress, srcWidth, srcHeight, srcStride, srcBytesPerPixel, parallel, backgroundWorker);
                    }
                }
            }
        }


        /// <summary>
        /// 行ごとの処理 row を 0〜height-1 について呼ぶ。parallel なら並列に。backgroundWorker があれば、キャンセルされたら残りの行を処理しない
        /// </summary>
        private static void ForEachRow(int height, bool parallel, BackgroundWorker backgroundWorker, Action<int> row)
        {
            if (!parallel)
            {
                for (var y = 0; y < height; y++)
                {
                    if (backgroundWorker != null && backgroundWorker.CancellationPending) return;
                    row(y);
                }
            }
            else if (backgroundWorker == null)
            {
                Parallel.For(0, height, row);
            }
            else
            {
                Parallel.For(0, height, (y, state) =>
                {
                    if (backgroundWorker.CancellationPending)
                    {
                        state.Break();
                        return;
                    }
                    row(y);
                });
            }
        }

        private unsafe static void BilinearForAlpha(byte* dstAddress, int dstWidth, int dstHeight, int dstStride, int dstBytesPerPixel, byte* srcAddress, int srcWidth, int srcHeight, int srcStride, int srcBytesPerPixel, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            var xt = new BilinearXTable(srcWidth, dstWidth); // 列ごとの位置と重みは行によらないので 1 回だけ求める
            ForEachRow(dstHeight, parallel, backgroundWorker, yDst => Bilinear1DForAlpha(dstAddress, yDst, dstWidth, dstHeight, dstStride, dstBytesPerPixel, srcAddress, srcWidth, srcHeight, srcStride, srcBytesPerPixel, xt));
        }

        private unsafe static void Bilinear1DForAlpha(byte* dstAddress, int yDst, int dstWidth, int dstHeight, int dstStride, int dstBytesPerPixel, byte* srcAddress, int srcWidth, int srcHeight, int srcStride, int srcBytesPerPixel, BilinearXTable xt)
        {
            var ySrc = (yDst + 0.5) * srcHeight / dstHeight - 0.5;
            var ySrc0d = Math.Floor(ySrc);
            var yT1 = ySrc - ySrc0d;
            var yT0 = 1 - yT1;
            var ySrc0 = (int)ySrc0d;
            var ySrc1 = ySrc0 + 1;
            if (ySrc0 < 0) ySrc0 = 0;
            else if (ySrc1 >= srcHeight) ySrc1 = srcHeight - 1;
            var srcTop0 = srcAddress + ySrc0 * srcStride;
            var srcTop1 = srcAddress + ySrc1 * srcStride;
            var dstLocal = dstAddress + yDst * dstStride;
            var srcBytePerPixel1 = srcBytesPerPixel - 1;
            var dstBytePerPixel1 = dstBytesPerPixel - 1;
            for (var xDst = 0; xDst < dstWidth; xDst++)
            {
                var xT1 = xt.T1[xDst];
                var xT0 = 1 - xT1;
                var xSrc0 = xt.Src0[xDst];
                var xSrc1 = xt.Src1[xDst];
                var xSrc0BytePerPixel = xSrc0 * srcBytesPerPixel;
                var xSrc1BytePerPixel = xSrc1 * srcBytesPerPixel;
                var srcLocal00 = srcTop0 + xSrc0BytePerPixel;
                var srcLocal01 = srcTop0 + xSrc1BytePerPixel;
                var srcLocal10 = srcTop1 + xSrc0BytePerPixel;
                var srcLocal11 = srcTop1 + xSrc1BytePerPixel;
                var t00 = yT0 * xT0;
                var t01 = yT0 * xT1;
                var t10 = yT1 * xT0;
                var t11 = yT1 * xT1;
                var alpha = t00 * srcLocal00[srcBytePerPixel1] + t01 * srcLocal01[srcBytePerPixel1] + t10 * srcLocal10[srcBytePerPixel1] + t11 * srcLocal11[srcBytePerPixel1];
                var alphaC5 = (255 + 0.5)  - alpha;
                alpha /= 255;
                for (var i = 0; i < srcBytePerPixel1; i++)
                {
                    var dstLocali = (int)(alpha * (t00 * srcLocal00[i] + t01 * srcLocal01[i] + t10 * srcLocal10[i] + t11 * srcLocal11[i]) + alphaC5);
                    if (dstLocali <= 0) dstLocal[i] = 0;
                    else if (dstLocali >= 255) dstLocal[i] = 255;
                    else dstLocal[i] = (byte)dstLocali;
                }
                dstLocal[dstBytePerPixel1] = 255;
                dstLocal += dstBytesPerPixel;
            }
        }

        private unsafe static void Bilinear(byte* dstAddress, int dstWidth, int dstHeight, int dstStride, int dstBytesPerPixel, byte* srcAddress, int srcWidth, int srcHeight, int srcStride, int srcBytesPerPixel, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            var xt = new BilinearXTable(srcWidth, dstWidth); // 列ごとの位置と重みは行によらないので 1 回だけ求める
            ForEachRow(dstHeight, parallel, backgroundWorker, yDst => Bilinear1D(dstAddress, yDst, dstWidth, dstHeight, dstStride, dstBytesPerPixel, srcAddress, srcWidth, srcHeight, srcStride, srcBytesPerPixel, xt));
        }

        private unsafe static void Bilinear1D(byte* dstAddress, int yDst, int dstWidth, int dstHeight, int dstStride, int dstBytesPerPixel, byte* srcAddress, int srcWidth, int srcHeight, int srcStride, int srcBytesPerPixel, BilinearXTable xt)
        {
            var ySrc = (yDst + 0.5) * srcHeight / dstHeight - 0.5;
            var ySrc0d = Math.Floor(ySrc);
            var yT1 = ySrc - ySrc0d;
            var yT0 = 1 - yT1;
            var ySrc0 = (int)ySrc0d;
            var ySrc1 = ySrc0 + 1;
            if (ySrc0 < 0) ySrc0 = 0;
            else if (ySrc1 >= srcHeight) ySrc1 = srcHeight - 1;
            var srcTop0 = srcAddress + ySrc0 * srcStride;
            var srcTop1 = srcAddress + ySrc1 * srcStride;
            var dstLocal = dstAddress + yDst * dstStride;
            switch(dstBytesPerPixel << 16 | srcBytesPerPixel)
            {
                case 1 << 16 | 1:
                    for (var xDst = 0; xDst < dstWidth; xDst++)
                    {
                        var xT1 = xt.T1[xDst];
                        var xT0 = 1 - xT1;
                        var xSrc0 = xt.Src0[xDst];
                        var xSrc1 = xt.Src1[xDst];
                        var xSrc0BytePerPixel = xSrc0;
                        var xSrc1BytePerPixel = xSrc1;
                        var srcLocal00 = srcTop0 + xSrc0BytePerPixel;
                        var srcLocal01 = srcTop0 + xSrc1BytePerPixel;
                        var srcLocal10 = srcTop1 + xSrc0BytePerPixel;
                        var srcLocal11 = srcTop1 + xSrc1BytePerPixel;
                        var t00 = yT0 * xT0;
                        var t01 = yT0 * xT1;
                        var t10 = yT1 * xT0;
                        var t11 = yT1 * xT1;
                        //for (var i = 0; i < bytePerPixel; i++)
                        {
                            const int i = 0;
                            var dstLocali = (int)(t00 * srcLocal00[i] + t01 * srcLocal01[i] + t10 * srcLocal10[i] + t11 * srcLocal11[i] + 0.5);
                            if (dstLocali <= 0) dstLocal[i] = 0;
                            else if (dstLocali >= 255) dstLocal[i] = 255;
                            else dstLocal[i] = (byte)dstLocali;
                        }
                        dstLocal++;
                    }
                    break;
                case 3 << 16 | 1:
                    for (var xDst = 0; xDst < dstWidth; xDst++)
                    {
                        var xT1 = xt.T1[xDst];
                        var xT0 = 1 - xT1;
                        var xSrc0 = xt.Src0[xDst];
                        var xSrc1 = xt.Src1[xDst];
                        var xSrc0BytePerPixel = xSrc0;
                        var xSrc1BytePerPixel = xSrc1;
                        var srcLocal00 = srcTop0 + xSrc0BytePerPixel;
                        var srcLocal01 = srcTop0 + xSrc1BytePerPixel;
                        var srcLocal10 = srcTop1 + xSrc0BytePerPixel;
                        var srcLocal11 = srcTop1 + xSrc1BytePerPixel;
                        var t00 = yT0 * xT0;
                        var t01 = yT0 * xT1;
                        var t10 = yT1 * xT0;
                        var t11 = yT1 * xT1;
                        //for (var i = 0; i < bytePerPixel; i++)
                        {
                            const int i = 0;
                            var dstLocali = (int)(t00 * srcLocal00[i] + t01 * srcLocal01[i] + t10 * srcLocal10[i] + t11 * srcLocal11[i] + 0.5);
                            byte value;
                            if (dstLocali <= 0) value = 0;
                            else if (dstLocali >= 255) value = 255;
                            else value = (byte)dstLocali;
                            dstLocal[0] = value;
                            dstLocal[1] = value;
                            dstLocal[2] = value;
                        }
                        dstLocal += 3;
                    }
                    break;
                case 3 << 16 | 3:
                    for (var xDst = 0; xDst < dstWidth; xDst++)
                    {
                        var xT1 = xt.T1[xDst];
                        var xT0 = 1 - xT1;
                        var xSrc0 = xt.Src0[xDst];
                        var xSrc1 = xt.Src1[xDst];
                        var xSrc0BytePerPixel = xSrc0 * 3;
                        var xSrc1BytePerPixel = xSrc1 * 3;
                        var srcLocal00 = srcTop0 + xSrc0BytePerPixel;
                        var srcLocal01 = srcTop0 + xSrc1BytePerPixel;
                        var srcLocal10 = srcTop1 + xSrc0BytePerPixel;
                        var srcLocal11 = srcTop1 + xSrc1BytePerPixel;
                        var t00 = yT0 * xT0;
                        var t01 = yT0 * xT1;
                        var t10 = yT1 * xT0;
                        var t11 = yT1 * xT1;
                        //for (var i = 0; i < bytePerPixel; i++)
                        {
                            const int i = 0;
                            var dstLocali = (int)(t00 * srcLocal00[i] + t01 * srcLocal01[i] + t10 * srcLocal10[i] + t11 * srcLocal11[i] + 0.5);
                            if (dstLocali <= 0) dstLocal[i] = 0;
                            else if (dstLocali >= 255) dstLocal[i] = 255;
                            else dstLocal[i] = (byte)dstLocali;
                        }
                        {
                            const int i = 1;
                            var dstLocali = (int)(t00 * srcLocal00[i] + t01 * srcLocal01[i] + t10 * srcLocal10[i] + t11 * srcLocal11[i] + 0.5);
                            if (dstLocali <= 0) dstLocal[i] = 0;
                            else if (dstLocali >= 255) dstLocal[i] = 255;
                            else dstLocal[i] = (byte)dstLocali;
                        }
                        {
                            const int i = 2;
                            var dstLocali = (int)(t00 * srcLocal00[i] + t01 * srcLocal01[i] + t10 * srcLocal10[i] + t11 * srcLocal11[i] + 0.5);
                            if (dstLocali <= 0) dstLocal[i] = 0;
                            else if (dstLocali >= 255) dstLocal[i] = 255;
                            else dstLocal[i] = (byte)dstLocali;
                        }
                        dstLocal += 3;
                    }
                    break;
                default:
                    for (var xDst = 0; xDst < dstWidth; xDst++)
                    {
                        var xT1 = xt.T1[xDst];
                        var xT0 = 1 - xT1;
                        var xSrc0 = xt.Src0[xDst];
                        var xSrc1 = xt.Src1[xDst];
                        var xSrc0BytePerPixel = xSrc0 * srcBytesPerPixel;
                        var xSrc1BytePerPixel = xSrc1 * srcBytesPerPixel;
                        var srcLocal00 = srcTop0 + xSrc0BytePerPixel;
                        var srcLocal01 = srcTop0 + xSrc1BytePerPixel;
                        var srcLocal10 = srcTop1 + xSrc0BytePerPixel;
                        var srcLocal11 = srcTop1 + xSrc1BytePerPixel;
                        var t00 = yT0 * xT0;
                        var t01 = yT0 * xT1;
                        var t10 = yT1 * xT0;
                        var t11 = yT1 * xT1;
                        for (var i = 0; i < srcBytesPerPixel; i++)
                        {
                            var dstLocali = (int)(t00 * srcLocal00[i] + t01 * srcLocal01[i] + t10 * srcLocal10[i] + t11 * srcLocal11[i] + 0.5);
                            if (dstLocali <= 0) dstLocal[i] = 0;
                            else if (dstLocali >= 255) dstLocal[i] = 255;
                            else dstLocal[i] = (byte)dstLocali;
                        }
                        dstLocal += dstBytesPerPixel;
                    }
                    break;
                
            }
        }

        private unsafe static void AntiAliasing2xForAlpha(byte* dstAddress, int dstWidth, int dstHeight, int dstStride, int dstBytesPerPixel, byte* srcAddress, int srcWidth, int srcHeight, int srcStride, int srcBytesPerPixel, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            var xt = new AntiAliasing2xXTable(srcWidth, dstWidth); // 列ごとの位置は行によらないので 1 回だけ求める
            ForEachRow(dstHeight, parallel, backgroundWorker, yDst => AntiAliasing2x1DForAlpha(dstAddress, yDst, dstWidth, dstHeight, dstStride, dstBytesPerPixel, srcAddress, srcWidth, srcHeight, srcStride, srcBytesPerPixel, xt));
        }

        private unsafe static void AntiAliasing2x1DForAlpha(byte* dstAddress, int yDst, int dstWidth, int dstHeight, int dstStride, int dstBytesPerPixel, byte* srcAddress, int srcWidth, int srcHeight, int srcStride, int srcBytesPerPixel, AntiAliasing2xXTable xt)
        {
            var ySrc0 = ((yDst << 2) + 1) * srcHeight / (dstHeight << 2);
            var ySrc1 = ((yDst << 2) + 3) * srcHeight / (dstHeight << 2);
            var srcTop0 = srcAddress + ySrc0 * srcStride;
            var srcTop1 = srcAddress + ySrc1 * srcStride;
            var dstLocal = dstAddress + yDst * dstStride;
            var srcBytePerPixel1 = srcBytesPerPixel - 1;
            var dstBytePerPixel1 = dstBytesPerPixel - 1;
            for (var xDst = 0; xDst < dstWidth; xDst++)
            {
                var xSrc0 = xt.Src0[xDst];
                var xSrc1 = xt.Src1[xDst];
                var xSrc0BytePerPixel = xSrc0 * srcBytesPerPixel;
                var xSrc1BytePerPixel = xSrc1 * srcBytesPerPixel;
                var srcLocal00 = srcTop0 + xSrc0BytePerPixel;
                var srcLocal01 = srcTop0 + xSrc1BytePerPixel;
                var srcLocal10 = srcTop1 + xSrc0BytePerPixel;
                var srcLocal11 = srcTop1 + xSrc1BytePerPixel;
                var alpha4_255 = srcLocal00[srcBytePerPixel1] + srcLocal01[srcBytePerPixel1] + srcLocal10[srcBytePerPixel1] + srcLocal11[srcBytePerPixel1];
                var alpha4C48 = (((255 * 4) - alpha4_255) * (255 * 4)) + (255 * 8);
                for (var i = 0; i < srcBytePerPixel1; i++)
                {
                    var dstLocali = ((srcLocal00[i] + srcLocal01[i] + srcLocal10[i] + srcLocal11[i])* alpha4_255 + alpha4C48) / (255 * 16);
                    if (dstLocali <= 0) dstLocal[i] = 0;
                    else if (dstLocali >= 255) dstLocal[i] = 255;
                    else dstLocal[i] = (byte)dstLocali;
                }
                dstLocal[dstBytePerPixel1] = 255;
                dstLocal += dstBytesPerPixel;
            }
        }

        private unsafe static void AntiAliasing2x(byte* dstAddress, int dstWidth, int dstHeight, int dstStride, int dstBytesPerPixel, byte* srcAddress, int srcWidth, int srcHeight, int srcStride, int srcBytesPerPixel, bool parallel, BackgroundWorker backgroundWorker = null)
        {
            var xt = new AntiAliasing2xXTable(srcWidth, dstWidth); // 列ごとの位置は行によらないので 1 回だけ求める
            ForEachRow(dstHeight, parallel, backgroundWorker, yDst => AntiAliasing2x1D(dstAddress, yDst, dstWidth, dstHeight, dstStride, dstBytesPerPixel, srcAddress, srcWidth, srcHeight, srcStride, srcBytesPerPixel, xt));
        }

        private unsafe static void AntiAliasing2x1D(byte* dstAddress, int yDst, int dstWidth, int dstHeight, int dstStride, int dstBytesPerPixel, byte* srcAddress, int srcWidth, int srcHeight, int srcStride, int srcBytesPerPixel, AntiAliasing2xXTable xt)
        {
            var ySrc0 = ((yDst << 2) + 1) * srcHeight / (dstHeight << 2);
            var ySrc1 = ((yDst << 2) + 3) * srcHeight / (dstHeight << 2);
            var srcTop0 = srcAddress + ySrc0 * srcStride;
            var srcTop1 = srcAddress + ySrc1 * srcStride;
            var dstLocal = dstAddress + yDst * dstStride;
            switch (dstBytesPerPixel << 16 | srcBytesPerPixel)
            {
                case 1 << 16 | 1:
                    for (var xDst = 0; xDst < dstWidth; xDst++)
                    {
                        var xSrc0 = xt.Src0[xDst];
                        var xSrc1 = xt.Src1[xDst];
                        var xSrc0BytePerPixel = xSrc0;
                        var xSrc1BytePerPixel = xSrc1;
                        var srcLocal00 = srcTop0 + xSrc0BytePerPixel;
                        var srcLocal01 = srcTop0 + xSrc1BytePerPixel;
                        var srcLocal10 = srcTop1 + xSrc0BytePerPixel;
                        var srcLocal11 = srcTop1 + xSrc1BytePerPixel;
                        {
                            const int i = 0;
                            var dstLocali = (srcLocal00[i] + srcLocal01[i] + srcLocal10[i] + srcLocal11[i] + 2) >> 2;
                            if (dstLocali <= 0) dstLocal[i] = 0;
                            else if (dstLocali >= 255) dstLocal[i] = 255;
                            else dstLocal[i] = (byte)dstLocali;
                        }
                        dstLocal++;
                    }
                    break;
                case 3 << 16 | 1:
                    for (var xDst = 0; xDst < dstWidth; xDst++)
                    {
                        var xSrc0 = xt.Src0[xDst];
                        var xSrc1 = xt.Src1[xDst];
                        var xSrc0BytePerPixel = xSrc0;
                        var xSrc1BytePerPixel = xSrc1;
                        var srcLocal00 = srcTop0 + xSrc0BytePerPixel;
                        var srcLocal01 = srcTop0 + xSrc1BytePerPixel;
                        var srcLocal10 = srcTop1 + xSrc0BytePerPixel;
                        var srcLocal11 = srcTop1 + xSrc1BytePerPixel;
                        {
                            const int i = 0;
                            var dstLocali = (srcLocal00[i] + srcLocal01[i] + srcLocal10[i] + srcLocal11[i] + 2) >> 2;
                            byte value;
                            if (dstLocali <= 0) value = 0;
                            else if (dstLocali >= 255) value = 255;
                            else value = (byte)dstLocali;
                            dstLocal[0] = value;
                            dstLocal[1] = value;
                            dstLocal[2] = value;
                        }
                        dstLocal += 3;
                    }
                    break;
                case 3 << 16 | 3:
                    for (var xDst = 0; xDst < dstWidth; xDst++)
                    {
                        var xSrc0 = xt.Src0[xDst];
                        var xSrc1 = xt.Src1[xDst];
                        var xSrc0BytePerPixel = xSrc0 * 3;
                        var xSrc1BytePerPixel = xSrc1 * 3;
                        var srcLocal00 = srcTop0 + xSrc0BytePerPixel;
                        var srcLocal01 = srcTop0 + xSrc1BytePerPixel;
                        var srcLocal10 = srcTop1 + xSrc0BytePerPixel;
                        var srcLocal11 = srcTop1 + xSrc1BytePerPixel;
                        {
                            const int i = 0;
                            var dstLocali = (srcLocal00[i] + srcLocal01[i] + srcLocal10[i] + srcLocal11[i] + 2) >> 2;
                            if (dstLocali <= 0) dstLocal[i] = 0;
                            else if (dstLocali >= 255) dstLocal[i] = 255;
                            else dstLocal[i] = (byte)dstLocali;
                        }
                        {
                            const int i = 1;
                            var dstLocali = (srcLocal00[i] + srcLocal01[i] + srcLocal10[i] + srcLocal11[i] + 2) >> 2;
                            if (dstLocali <= 0) dstLocal[i] = 0;
                            else if (dstLocali >= 255) dstLocal[i] = 255;
                            else dstLocal[i] = (byte)dstLocali;
                        }
                        {
                            const int i = 2;
                            var dstLocali = (srcLocal00[i] + srcLocal01[i] + srcLocal10[i] + srcLocal11[i] + 2) >> 2;
                            if (dstLocali <= 0) dstLocal[i] = 0;
                            else if (dstLocali >= 255) dstLocal[i] = 255;
                            else dstLocal[i] = (byte)dstLocali;
                        }
                        dstLocal += 3;
                    }
                    break;
                default:
                    for (var xDst = 0; xDst < dstWidth; xDst++)
                    {
                        var xSrc0 = xt.Src0[xDst];
                        var xSrc1 = xt.Src1[xDst];
                        var xSrc0BytePerPixel = xSrc0 * srcBytesPerPixel;
                        var xSrc1BytePerPixel = xSrc1 * srcBytesPerPixel;
                        var srcLocal00 = srcTop0 + xSrc0BytePerPixel;
                        var srcLocal01 = srcTop0 + xSrc1BytePerPixel;
                        var srcLocal10 = srcTop1 + xSrc0BytePerPixel;
                        var srcLocal11 = srcTop1 + xSrc1BytePerPixel;
                        for (var i = 0; i < srcBytesPerPixel; i++)
                        {
                            var dstLocali = (srcLocal00[i] + srcLocal01[i] + srcLocal10[i] + srcLocal11[i] + 2) >> 2;
                            if (dstLocali <= 0) dstLocal[i] = 0;
                            else if (dstLocali >= 255) dstLocal[i] = 255;
                            else dstLocal[i] = (byte)dstLocali;
                        }
                        dstLocal += dstBytesPerPixel;
                    }
                    break;
            }
        }
    }
}
