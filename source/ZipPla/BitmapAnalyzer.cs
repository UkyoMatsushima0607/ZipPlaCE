using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace ZipPla
{
    public static class BitmapAnalyzer
    {
        private const int maxLength = 128;

        public static Point GetFocus(Bitmap bmp, RectangleF rectangle, Fraction pixelRatio)
        {
            var rect = new Rectangle((int)Math.Round(rectangle.X), (int)Math.Round(rectangle.Y), (int)Math.Round(rectangle.Width), (int)Math.Round(rectangle.Height));
            return GetFocus(bmp, rect, pixelRatio);
        }
        public static Point GetFocus(Bitmap bmp, Fraction pixelRatio)
        {
            return GetFocus(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height), pixelRatio);
        }
        /// <summary>注目点の計算結果。作業用の縮小画像（長い辺 128）のどのマスか。元画像の解像度によらず、範囲の縦横比だけで作業用の大きさが決まる（P4）</summary>
        public struct FocusCell
        {
            public int WorkingWidth, WorkingHeight, X, Y;
        }

        public static Point GetFocus(Bitmap bmp, Rectangle rectangle, Fraction pixelRatio)
        {
            return FocusCellToPoint(GetFocusCell(bmp, rectangle, pixelRatio), rectangle);
        }

        /// <summary>範囲と画素の縦横比から決まる作業用の大きさ。保存してある FocusCell が使えるかの確認に使う</summary>
        public static Size GetFocusWorkingSize(Size rectangleSize, Fraction pixelRatio)
        {
            return GetWorkignSize(rectangleSize, pixelRatio);
        }

        public static Point FocusCellToPoint(FocusCell cell, Rectangle rectangle)
        {
            var orgW = rectangle.Width;
            var orgH = rectangle.Height;
            return new Point(((cell.X * 2 + 1) * orgW / (2 * cell.WorkingWidth)) + rectangle.X, ((cell.Y * 2 + 1) * orgH / (2 * cell.WorkingHeight)) + rectangle.Y);
        }

        public static FocusCell GetFocusCell(Bitmap bmp, Rectangle rectangle, Fraction pixelRatio)
        {
            var workingSize = GetWorkignSize(rectangle.Size, pixelRatio);
            var workingWidth = workingSize.Width;
            var workingHeight = workingSize.Height;

            var pointsLinearArray = GetLinearArrayForFocus(bmp, rectangle, workingWidth, workingHeight);

            var result = 0;
            int max = 0;
            var wh = workingWidth * workingHeight;
            for (var i = 0; i < wh; i++)
            {
                var v = pointsLinearArray[i];
                if(v > max)
                {
                    max = v;
                    result = i;
                }
            }
            
            return new FocusCell { WorkingWidth = workingWidth, WorkingHeight = workingHeight, X = result % workingWidth, Y = result / workingWidth };
        }

        private static Size GetWorkignSize(Size targetSize, Fraction pixelRatio)
        {
            return GetWorkignSize((Fraction)targetSize * pixelRatio);
        }
        private static Size GetWorkignSize(Fraction targetAspect)
        {
            var orgW = targetAspect.Numerator;
            var orgH = targetAspect.Denominator;
            var m = orgW > orgH ? orgW : orgH;
            var workingWidth = Math.Max(1, orgW * maxLength / m);
            var workingHeight = Math.Max(1, orgH * maxLength / m);
            return new Size(workingWidth, workingHeight);
        }

        private static int[] GetLinearArrayForFocus(Bitmap bmp, Rectangle rectangle, int workingWidth, int workingHeight)
        {
            var orgW = rectangle.Width;
            var orgH = rectangle.Height;
            int[] grayLinearArray;
            int wh;
            // 縮小は GDI+ を使わない面積平均で（GDI+ の DrawImage はスレッドをまたいで直列になる）。拡大・透明な画素があるときは従来どおり（P4）
            var workingBmp = new Bitmap(workingWidth, workingHeight, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                if (!AreaAverageResizer.TryDraw(bmp, rectangle, workingBmp, new Rectangle(0, 0, workingWidth, workingHeight)))
                {
                    workingBmp.Dispose();
                    workingBmp = new Bitmap(workingWidth, workingHeight);
                    using (var g = Graphics.FromImage(workingBmp))
                    {
                        g.DrawImage(bmp, new Rectangle(0, 0, workingWidth, workingHeight), rectangle, GraphicsUnit.Pixel);
                    }
                }

                wh = workingWidth * workingHeight;
                grayLinearArray = new int[wh];
                var format = workingBmp.PixelFormat;
                var bytePerPixel = Image.GetPixelFormatSize(format) / 8;
                var img = workingBmp.LockBits(new Rectangle(new Point(), workingBmp.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, format);
                try
                {
                    var stride = img.Stride;
                    unsafe
                    {
                        var adr = (byte*)img.Scan0;
                        for (var y = 0; y < workingHeight; y++)
                        {
                            var adrYStride = adr + y * stride;
                            var yW = y * workingWidth;
                            for (var x = 0; x < workingWidth; x++)
                            {
                                var bgr = adrYStride + bytePerPixel * x;
                                int b = bgr[0];
                                int g = bgr[1];
                                int r = bgr[2];
                                grayLinearArray[yW + x] = b + 6 * g + 3 * r; // 以前は緑の代わりに b を使っていた（X10）
                            }
                        }
                    }
                }
                finally
                {
                    workingBmp.UnlockBits(img);
                }
            }
            finally
            {
                workingBmp.Dispose();
            }
            var pointsLinearArray = new int[wh];
            var h1 = workingHeight - 1;
            var w1 = workingWidth - 1;
            for (var y = 0; y < h1; y++)
            {
                var yW = y * workingWidth;
                var y1W = (y + 1) * workingWidth;
                for (var x = 0; x < w1; x++)
                {
                    var x1 = x + 1;
                    var dp = grayLinearArray[y1W + x1] - grayLinearArray[yW + x];
                    var dn = grayLinearArray[y1W + x] - grayLinearArray[yW + x1];

                    var dy = dp + dn;
                    var dx = dp - dn;

                    var absDx = dx > 0 ? dx : -dx;
                    var absDy = dy > 0 ? dy : -dy;

                    var d2 = absDx + absDy; // 斜めが強まり帳尻が合う
                    if (d2 == 0) continue;


                    var nonInc = !(dx > 0 && dy > 0 || dx < 0 && dy < 0);
                    var nonDec = !(dx > 0 && dy < 0 || dx < 0 && dy > 0);

                    // 異符号
                    // 等符号
                    if (absDx > absDy)
                    {
                        for (var x2 = 0; x2 < workingWidth; x2++)
                        {
                            var y2 = Round(dy * (2 * (x2 - x) - 1) + dx, 2 * dx) + y;
                            if (y2 < 0)
                            {
                                if (nonInc) break;
                            }
                            else if (y2 >= workingHeight)
                            {
                                if (nonDec) break;
                            }
                            else
                            {
                                pointsLinearArray[x2 + y2 * workingWidth] += d2;
                            }
                        }
                    }
                    else
                    {
                        for (var y2 = 0; y2 < workingHeight; y2++)
                        {
                            var y2w = y2 * workingWidth;
                            var x2 = Round(dx * (2 * (y2 - y) - 1) + dy, 2 * dy) + x;
                            if (x2 < 0)
                            {
                                if (nonInc) break;
                            }
                            else if (x2 >= workingWidth)
                            {
                                if (nonDec) break;
                            }
                            else
                            {
                                pointsLinearArray[x2 + y2w] += d2;
                            }
                        }
                    }
                }
            }

            return pointsLinearArray;
        }

        private static int Round(int p, int q)
        {
            if(p > 0)
            {
                if(q > 0)
                {
                    return (2 * p + q) / (2 * q);
                }
                else
                {
                    return (2 * p - q) / (2 * q);
                }
            }
            else
            {
                if (q < 0)
                {
                    return (2 * p + q) / (2 * q);
                }
                else
                {
                    return (2 * p - q) / (2 * q);
                }
            }
        }
    }
}
