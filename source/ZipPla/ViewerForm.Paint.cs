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
        private Size getCurrentPbViewSize()
        {
            if (FullScreen_pseudoFullScreen)
            {
                return Screen.FromControl(this).Bounds.Size;
            }
            else if (PseudoMaximized)
            {
                return PseudoMaximizedSize;
            }
            else
            {
                return ViewerForm_SizeChanged_pbBoundsMemoryForMinimize.Size; //pbView.Size;
            }
        }

        /// <summary>
        /// ViewerMode == Normal の場合のサイズを返す
        /// </summary>
        /// <returns></returns>
        private Size? getCurrentVisibleImageSize(Size pbViewSize)
        {
            var totalWidth = 0;
            var startPage = currentPage;
            var stopPage = NextPage;
            var totalHeight = 0;
            for (var i = startPage; i < stopPage; i++)
            {
                var size = ResizedImageArray[i].Size;
                totalWidth += size.Width;
                if (size.Height > totalHeight)
                {
                    totalHeight = size.Height;
                }
            }

            var divisions = 1;
            var left = 0;
            var right = totalWidth;


            if (totalWidth <= 0)
            {
                if (MaxDivision > 1 && 0 <= startPage && startPage == stopPage && ResizedImageArray != null && ResizedImageArray.Length > startPage && getCurrentPageDivison() > 1)
                {
                    var img = ResizedImageArray[startPage];
                    if (img == null) return null;
                    var imageWidth = img.Width;
                    divisions = (imageWidth - 1) / pbViewSize.Width + 1;
                    if (divisions > MaxDivision) return null;
                    if (CurrentDividedPosition < 0 || CurrentDividedPosition >= divisions) return null;
                    stopPage = startPage + 1;
                    var p = BindingMode != BindingMode.RightToLeft ? CurrentDividedPosition : divisions - CurrentDividedPosition - 1;
                    left = (imageWidth * p + divisions / 2) / divisions;
                    right = (imageWidth * (p + 1) + divisions / 2) / divisions;
                    totalWidth = right - left;
                }
            }

            if (totalWidth <= 0 || totalHeight <= 0) return null;

            return new Size(totalWidth, totalHeight);
        }

        private double? getCurrentMagnifierPower()
        {
            var result = MagnifyingPower.GetValue();
            if (result != null) return result;

            var pbViewSize = getCurrentPbViewSize();
            var visibleSize = getCurrentVisibleImageSize(pbViewSize);
            if (visibleSize == null) return null;

            return magnifyingPower.GetPower(pbViewSize,(Size) visibleSize);
        }

        private void resetMagnifier()
        {
            var cPage = this.currentPage;
            var resizedImageArray = this.ResizedImageArray;
            var resizedSizeArray = this.ResizedSizeArray;
            var preFilteredImageArray = this.PreFilteredImageArray;
            if (cPage < 0 || resizedImageArray == null || resizedSizeArray == null || preFilteredImageArray == null) return;
            var arrayLength = resizedImageArray.Length;
            if (resizedSizeArray.Length != arrayLength || preFilteredImageArray.Length != arrayLength) return;
            if (cPage >= arrayLength || resizedImageArray[cPage] == null) return;
            var nextPage = NextPage;
            if (nextPage < cPage || nextPage == cPage && (MaxDivision <= 1 || getCurrentPageDivison() <= 1)) return;

            if (nextPage == cPage) nextPage++;
            // 原寸を読み直している途中なら、終わってから作る（読み直しの完了時に showCurrentPage からもう一度呼ばれる）（C1）
            if (IsCurrentOriginalReloading(cPage, nextPage)) return;
             var totalWidth = 0;
            var pageCount = nextPage - cPage;
            var zoomedSizeArray = new Size[pageCount];

            var pbViewHeight = FullScreen_pseudoFullScreen ? Screen.FromControl(this).Bounds.Height : PseudoMaximized ? PseudoMaximizedSize.Height : pbView.Height;
            var zoomedHeight = 0;
            var magPower_ = getCurrentMagnifierPower();
            if (magPower_ == null) return;
            var magPower = (double)magPower_;
            for (var i = cPage; i < nextPage; i++)
            {
                var img = preFilteredImageArray[i];
                var resizedSize = resizedSizeArray[i];
                var zoomedHeightI = (int)Math.Round(magPower * resizedSize.Height);
                if (zoomedHeightI > zoomedHeight) zoomedHeight = zoomedHeightI;
                //orgSize = new Size(Math.Abs(orgSize.Width), Math.Abs(orgSize.Height));
                if (img == null)
                {
                    //if (orgSize.Width >= 0 || orgSize.Height >= 0)
                    if (resizedSize.IsEmpty) // エラーで img == null の可能性を除外
                    {
                        pbView_Paint_Rectangle = Rectangle.Empty;
                        ViewerMode = ViewerModeEnum.Normal;
                        return;
                    }
                }

                // 拡大画像生成待ちで待たされるので不適切

                //var resizedSize = GetResizedSize(orgSize);
                //totalWidth += zoomedWidthArray[i - currentPage] = (int)Math.Round(zoomedHeight * size.Width / (double)size.Height);
                var zoomedWidth = (int)Math.Round(resizedSize.Width * magPower);
                totalWidth += zoomedWidth;
                zoomedSizeArray[i - cPage] = new Size(zoomedWidth, zoomedHeightI);
            }
            //Magnifier_VisiblePage = Tuple.Create(currentPage, nextPage);
            SetMagnifierCanvasInfo(cPage, nextPage);
            //bmwLoadEachPage.ThreadCount = 0;
            if (Magnifier_ZoomedPage != null) Magnifier_ZoomedPage.Dispose();
            Magnifier_ZoomedPage = null;

            if (bwMagnifierMaker.IsBusy)
            {
                bwMagnifierMaker.CancelAsync();
                bwMagnifierMaker_Q = Tuple.Create(totalWidth, zoomedHeight, nextPage, cPage, zoomedSizeArray);
            }
            else
            {
                bwMagnifierMaker.RunWorkerAsync(Tuple.Create(totalWidth, zoomedHeight, nextPage, cPage, zoomedSizeArray));
            }
        }

        private void pbPaintInvalidate()
        {
            pbView_Paint_OnlyDrawMeasure = false;
            pbView.Invalidate(false);

        }

        private void pbPaintDrawOnlyMeasure()
        {
            pbView_Paint_OnlyDrawMeasure = true;
            pbView.Invalidate(false);

        }

        public static void lockBits(BitmapEx bmp, Rectangle rect, ImageLockMode flags, string purpose, Action<BitmapData> body)
        {
            lockBits(bmp, bmp, rect, flags, purpose, body);
        }

        public static void lockBits(Bitmap bmp, BitmapEx locker, Rectangle rect, ImageLockMode flags, string purpose, Action<BitmapData> body)
        {
            lockBits(bmp, locker, rect, flags, PixelFormat.Format24bppRgb, purpose, body);
        }

        public static void lockBits(Bitmap bmp, BitmapEx locker, Rectangle rect, ImageLockMode flags, PixelFormat format, string purpose, Action<BitmapData> body)
        {
            if (rect.IsEmpty) return;
            if (!new Rectangle(Point.Empty, bmp.Size).Contains(rect)) return;
            lock (locker)
            {
                BitmapData data;
                try
                {
                    data = bmp.LockBits(rect, flags, format);
                }
                catch (Exception e)
                {
                    string size;
                    try
                    {
                        size = $"({bmp.Width}, {bmp.Height})";
                    }
                    catch
                    {
                        size = "<error>";
                    }
                    throw new Exception($"Exception is thrown by lockBits\nbmp.Size = {size}\nrect = {rect}\npurpose = {purpose}", e);
                }
                try
                {
                    body(data);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
            }
        }

        Rectangle pbView_Paint_Rectangle = Rectangle.Empty;

        string pbView_Paint_RectangleMessage = null;

        readonly static Brush pbView_Paint_Brush = new SolidBrush(Color.FromArgb(160, 128, 128, 128));

        //readonly static Brush pbView_Paint_Brush_WithoutAlpha = new SolidBrush(Color.FromArgb(255, 128, 128, 128));
        private static Color HalfGrayBrusher(Color c) { return Color.FromArgb(
            ((160 * 128 + (255 - 160) * c.R + 127) / 255), ((160 * 128 + (255 - 160) * c.G + 127) / 255), ((160 * 128 + (255 - 160) * c.B + 127) / 255)); }

        private static unsafe void HalfGrayBrusher(IntPtr dataAdr, int size)
        {
#if DEBUG
            var data = (byte*)dataAdr; var stop = size / 3 * 3; for (var i = 0; i < stop; i++) data[i] = i % 3 == 2 ? (byte)255 : (byte)0;
#else
            var table = HalfGrayTable;
            var data = (byte*)dataAdr; var stop = size / 3 * 3; for (var i = 0; i < stop; i++) data[i] = table[data[i]];
#endif
        }

        // HalfGrayBrusher の 1 バイトごとの整数除算を表にしたもの（V19）
        private static readonly byte[] HalfGrayTable = Enumerable.Range(0, 256).Select(v => (byte)((160 * 128 + (255 - 160) * v + 127) / 255)).ToArray();

        bool pbView_Paint_Rectangle_FillAround = true;

        BitmapEx pbView_Paint_Canvas;

        bool drawLoading_ShowingMessage = false;

        int pbView_Paint_PrevWidth = 0;

        int pbView_Paint_PrevHeight = 0;

        bool pbView_Paint_OnlyDrawMeasure = false;

        //readonly object pbView_Paint_Canvas_Locker = new object();
#if PERFLOG
        // 計測用（PERFLOG 定数を付けたビルドのみ）: ビューアの描画1回あたりの時間を exe のフォルダの viewer-paint.log に書く
        private static long perfPaints, perfPaintTicks, perfPaintMax;
        private void pbView_Paint(object sender, PaintEventArgs e)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            pbView_PaintCore(sender, e);
            var t = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            perfPaints++; perfPaintTicks += t; if (t > perfPaintMax) perfPaintMax = t;
            if (perfPaints % 10 == 0)
            {
                double f = System.Diagnostics.Stopwatch.Frequency;
                try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "viewer-paint.log"), $"paints={perfPaints} avg={perfPaintTicks * 1000.0 / f / perfPaints:F2}ms max={perfPaintMax * 1000.0 / f:F2}ms"); } catch { }
            }
        }
        private void pbView_PaintCore(object sender, PaintEventArgs e)
#else
        private void pbView_Paint(object sender, PaintEventArgs e)
#endif
        {
            if (FullScreen_pseudoFullScreen) return;

            Rectangle pbViewRect;

            //lock (pbView_Paint_Canvas_Locker)
            {
                var pbViewSize = pbView.Size;
                if (pbViewSize.Width <= 0 || pbViewSize.Height <= 0) return;

                if (pbView_Paint_Canvas == null)
                {
                    pbView_Paint_Canvas = BitmapEx.ConvertToBitmapEx(new Bitmap(pbViewSize.Width, pbViewSize.Height, PixelFormat.Format24bppRgb));
                    pbView_Paint_PrevWidth = pbView_Paint_PrevHeight = 0; // 新しいキャンバスは黒なので、下で表示の全体を背景色で塗る（Y22）
                    pbView_Paint_OnlyDrawMeasure = false;
                }
                else if (/*pbView_Paint_Canvas == null ||*/ pbView_Paint_Canvas.Width < pbViewSize.Width || pbView_Paint_Canvas.Height < pbViewSize.Height)
                {
                    //var width = pbView_Paint_Canvas == null ? pbViewSize.Width : Math.Max(pbView_Paint_Canvas.Width, pbViewSize.Width);
                    //var height = pbView_Paint_Canvas == null ? pbViewSize.Height : Math.Max(pbView_Paint_Canvas.Height, pbViewSize.Height);
                    var width = Math.Max(pbView_Paint_Canvas.Width, pbViewSize.Width);
                    var height = Math.Max(pbView_Paint_Canvas.Height, pbViewSize.Height);
                    pbView_Paint_Canvas.Dispose();
                    pbView_Paint_Canvas = BitmapEx.ConvertToBitmapEx(new Bitmap(width, height, PixelFormat.Format24bppRgb));
                    pbView_Paint_PrevWidth = pbView_Paint_PrevHeight = 0; // 新しいキャンバスは黒なので、下で表示の全体を背景色で塗る（Y22）
                    pbView_Paint_OnlyDrawMeasure = false;
                }
                else if (!pbView_SizeChanged_SizeChanging &&
                    (long)pbView_Paint_Canvas.Width * pbView_Paint_Canvas.Height > 2L * pbViewSize.Width * pbViewSize.Height)
                {
                    // 大きくするだけで縮めなかったので、全画面（4K で約 25MB）から戻しても持ち続けていた。
                    // 表示の 2 倍より大きければ作り直す（大きさを変えている途中は除く）（V19 の残り）
                    pbView_Paint_Canvas.Dispose();
                    pbView_Paint_Canvas = BitmapEx.ConvertToBitmapEx(new Bitmap(pbViewSize.Width, pbViewSize.Height, PixelFormat.Format24bppRgb));
                    pbView_Paint_PrevWidth = pbView_Paint_PrevHeight = 0; // 新しいキャンバスは黒なので、下で表示の全体を背景色で塗る（Y22）
                    pbView_Paint_OnlyDrawMeasure = false;
                }

                if (pbViewSize.Width > pbView_Paint_PrevWidth || pbViewSize.Height > pbView_Paint_PrevHeight)
                {
                    var rect = new Rectangle(0, 0, pbViewSize.Width, pbViewSize.Height);
                    lockBits(pbView_Paint_Canvas, rect, ImageLockMode.WriteOnly, "Fill background of new canvas", data =>
                    {
                    //MessageBox.Show("X");
                    int drawX = pbViewSize.Width;
                        if (pbViewSize.Width > pbView_Paint_PrevWidth)
                        {
                            FillRectangle(data, pbView_Paint_PrevWidth, 0, pbView.BackColor,
                             pbViewSize.Width - pbView_Paint_PrevWidth, pbViewSize.Height, ParallelMode.ParallelForUIThread);
                            drawX = pbView_Paint_PrevWidth;
                        }
                        if (pbViewSize.Height > pbView_Paint_PrevHeight && drawX > 0) FillRectangle(data, 0, pbView_Paint_PrevHeight, pbView.BackColor,
                             drawX, pbViewSize.Height - pbView_Paint_PrevHeight, ParallelMode.ParallelForUIThread);
                    });
                    pbView_Paint_OnlyDrawMeasure = false;
                }
                pbView_Paint_PrevWidth = pbViewSize.Width;
                pbView_Paint_PrevHeight = pbViewSize.Height;

                //var sw = new System.Diagnostics.Stopwatch(); var s = ""; sw.Start();

                if (!pbView_Paint_OnlyDrawMeasure)
                {
                    //MessageBox.Show("A");
                    if (!pbView_PaintToCanvas(pbView_Paint_Canvas, ParallelMode.ParallelForUIThread))
                    {
                        if (!drawLoading_ShowingMessage)
                        {
                            var rect = new Rectangle(0, 0, pbViewSize.Width, pbViewSize.Height);
                            lockBits(pbView_Paint_Canvas, rect, ImageLockMode.ReadWrite, "Draw loading mask", data =>
                            {
                                FillRectangle(data, HalfGrayBrusher, rect, ParallelMode.ParallelForUIThread);
                            });
                            drawLoading_ShowingMessage = true;
                            //sw.Stop(); s += sw.Elapsed + "\n"; sw.Restart();
                        }
                    }
                    else
                    {
                        drawLoading_ShowingMessage = false;
                    }
                }
                //MessageBox.Show("B");

                //sw.Stop(); s += sw.Elapsed + "\n"; sw.Restart();

                pbViewRect = pbView.ClientRectangle;

                // ZipPla CE: 重ねて描くもの（目盛り・ルーペの開閉の文字・端の線）がなければ、キャンバスの再描画の範囲を
                // SetDIBitsToDevice でウィンドウに直接出す。以前は ViewControl のダブルバッファに DrawImageUnscaled で
                // 24bpp から 32bpp に変換しながら全体を描き、それを BitBlt でウィンドウに出していた（ページ送り・アニメーションのコマごとに、
                // 4K で約 33MB の書き込みが 2 回）。重ねて描くものがあるときは、従来と同じ順番で一時のバッファに描いてから出す
                if (pbView_Paint_NeedsOverlay() || !presentCanvasDirect(e, pbViewRect))
                {
                    using (var buffered = BufferedGraphicsManager.Current.Allocate(e.Graphics, pbViewRect))
                    {
                        var g = buffered.Graphics;
                        g.DrawImageUnscaled(pbView_Paint_Canvas, pbViewRect);

                        if (DrawMeasure)
                        {
                            drawMeasure(g, pbView_Paint_Canvas);
                        }

                        drawMessageOnRectangle(g); //, pbView_Paint_Canvas);

                        if (drowSideLine_Draw)
                        {
                            var rect = pbViewRect;
                            if (!drowSideLine_DrawLeft)
                            {
                                rect.X = rect.Right - drowSideLine_DrawWidth;
                            }
                            rect.Width = drowSideLine_DrawWidth;
                            using (var b = new SolidBrush(drowSideLine_DrawColor))
                            {
                                g.FillRectangle(b, rect);
                            }
                        }

                        buffered.Render(e.Graphics);
                    }
                }
            }

            //sw.Stop(); s += sw.Elapsed + "\n"; MessageBox.Show(s);
        }

        /// <summary>
        /// キャンバスの上に重ねて描くものがあるか。drawMeasure・drawMessageOnRectangle・端の線の条件と合わせること
        /// </summary>
        private bool pbView_Paint_NeedsOverlay()
        {
            if (DrawMeasure || drowSideLine_Draw) return true;
            var vm = ViewerMode;
            return (vm == ViewerModeEnum.MagnifierOpening || vm == ViewerModeEnum.MagnifierClosing) && pbView_Paint_RectangleMessage != null;
        }

        /// <summary>
        /// キャンバス（24bpp）の再描画の範囲を、SetDIBitsToDevice でウィンドウの DC に直接出す。GDI がその場で画面の形式に変換する。
        /// 出せなければ false（呼び出し側が従来の方法で描く）
        /// </summary>
        private bool presentCanvasDirect(PaintEventArgs e, Rectangle pbViewRect)
        {
            var canvas = pbView_Paint_Canvas;
            var clip = Rectangle.Intersect(e.ClipRectangle, pbViewRect);
            if (clip.Width <= 0 || clip.Height <= 0) return true;
            if (clip.Right > canvas.Width || clip.Bottom > canvas.Height) return false;
            var done = false;
            try
            {
                // 行の途中から始めると Scan0 の位置が変わるだけで stride は同じなので、左端から右端までの行をロックし、横は xSrc で選ぶ
                lockBits(canvas, new Rectangle(0, clip.Y, canvas.Width, clip.Height), ImageLockMode.ReadOnly, "Present canvas", data =>
                {
                    if (data.Stride != ((canvas.Width * 3 + 3) & ~3)) return; // DIB の行の長さと違えば従来の方法で
                    var header = new CanvasBlit.BITMAPINFOHEADER
                    {
                        biSize = Marshal.SizeOf(typeof(CanvasBlit.BITMAPINFOHEADER)),
                        biWidth = canvas.Width,
                        biHeight = -clip.Height, // 上から下
                        biPlanes = 1,
                        biBitCount = 24,
                        biCompression = 0, // BI_RGB
                    };
                    var hdc = e.Graphics.GetHdc();
                    try
                    {
                        var lines = CanvasBlit.SetDIBitsToDevice(hdc, clip.X, clip.Y, (uint)clip.Width, (uint)clip.Height,
                            clip.X, 0, 0, (uint)clip.Height, data.Scan0, ref header, 0 /* DIB_RGB_COLORS */);
                        done = lines == clip.Height;
                    }
                    finally
                    {
                        e.Graphics.ReleaseHdc(hdc);
                    }
                });
            }
            catch
            {
                done = false;
            }
            return done;
        }

        private static class CanvasBlit
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct BITMAPINFOHEADER
            {
                public int biSize;
                public int biWidth;
                public int biHeight;
                public short biPlanes;
                public short biBitCount;
                public int biCompression;
                public int biSizeImage;
                public int biXPelsPerMeter;
                public int biYPelsPerMeter;
                public int biClrUsed;
                public int biClrImportant;
            }

            [DllImport("gdi32.dll")]
            public static extern int SetDIBitsToDevice(IntPtr hdc, int xDest, int yDest, uint w, uint h, int xSrc, int ySrc,
                uint startScan, uint cLines, IntPtr lpvBits, ref BITMAPINFOHEADER lpbmi, uint colorUse);
        }

        private Font magnifierMessageFont, magnifierMessageFontBase;

        private void drawMessageOnRectangle(Graphics g)//, Image baseImage)
        {
            string mess;
            var vm = ViewerMode;
            bool opening;
            if (((opening = vm == ViewerModeEnum.MagnifierOpening) || vm == ViewerModeEnum.MagnifierClosing) && (mess = pbView_Paint_RectangleMessage) != null)
            {
                var baseFont = pbView.Font ?? DefaultFont;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                // ルーペの開閉のアニメーション中は毎回呼ばれるので、フォントを使い回す（V32）
                if (magnifierMessageFont == null || magnifierMessageFontBase != baseFont)
                {
                    magnifierMessageFont?.Dispose();
                    magnifierMessageFontBase = baseFont;
                    magnifierMessageFont = new Font(baseFont.FontFamily, baseFont.Size * 2);
                }
                var font = magnifierMessageFont;
                {
                    var p = pbView_Paint_Rectangle.Location;
                    if (opening)
                    {
                        var size = g.MeasureString(mess, font);
                        p.Y -= (int)Math.Ceiling(size.Height);
                    }
                    else
                    {
                        p.Y += (int)(font.Size / 4);
                    }
                    g.DrawString(mess, font, Brushes.White, p);

                }
            }
        }

        private void drawMeasure(Graphics g, Bitmap backGround)
        {
            var w = pbView.Width;
            var h = pbView.Height;
            if (w <= 0 || h <= 0 || backGround.Width < w || backGround.Height < h) return;
            var p = pbView.PointToClient(Cursor.Position);
            var rect = new Rectangle(0, 0, w, h);
            if (!rect.Contains(p)) return;

            if (ZipTag.NearToWhite(backGround.GetPixel(p.X, p.Y)))
            {
                g.DrawLine(Pens.Black, new Point(p.X, 0), new Point(p.X, h));
                g.DrawLine(Pens.Black, new Point(0, p.Y), new Point(w, p.Y));
            }
            else
            {
                g.DrawLine(Pens.White, new Point(p.X, 0), new Point(p.X, h));
                g.DrawLine(Pens.White, new Point(0, p.Y), new Point(w, p.Y));
            }

        }

        private bool pbView_PaintToCanvas(BitmapEx canvas, ParallelMode parallelMode)
        {
            var pbViewSize = pbView.Size;

            if(string.IsNullOrEmpty(currentArchiveFilePath))
            {
                lockBits(canvas, new Rectangle(0, 0, pbViewSize.Width, pbViewSize.Height),
                    ImageLockMode.WriteOnly, "Fill background for blank data", dst =>
                {
                    FillRectangle(dst, 0, 0, pbView.BackColor, pbViewSize.Width, pbViewSize.Height, ParallelMode.Single);
                });
                return true;
            }

            var sizeChanged = showCurrentPage_PrevDrawSize != pbViewSize;
            var returnValueForNoDraw = !sizeChanged;
            var ret = sizeChanged && !showCurrentPage_PagePrepared; 
            showCurrentPage_PagePrepared = false;
            if (ret)
            {
                switch (ViewerMode)
                {
                    case ViewerModeEnum.Normal: break;
                    case ViewerModeEnum.MagnifierOpening:
                    case ViewerModeEnum.MagnifierClosing:
                        pbView_Paint_Rectangle = Rectangle.Empty;
                        ViewerMode = ViewerModeEnum.Normal;
                        break;
                    case ViewerModeEnum.Magnifier:
                        break;
                }
                return sizeChanged;
            }
            showCurrentPage_PrevDrawSize = pbViewSize;
            //if (sizeChanged) return false;
            
            ShowCurrentPageToStatusBar();
            var stopPage = NextPage;
            if (ViewerMode == ViewerModeEnum.Magnifier && 
                MagnifierCanvasPrepared() && currentPage >= 0 && Magnifier_ZoomedPage != null)
            {
                Rectangle mr;
                if (TouchListener_Pan_Delta == null)
                {
                    mr = GetMagnifierRectangle(false, null);
                }
                else
                {
                    mr = GetMagnifierRectangle(false, TouchListener_Pan_Delta);
                    TouchListener_Pan_Delta = VectorD.Zero;
                }
                var dr = new Rectangle(new Point((pbViewSize.Width - mr.Width) / 2, (pbViewSize.Height - mr.Height) / 2), mr.Size);
                // 拡大画像の背景はあらかじめ設定してある
                //g.DrawImage(Magnifier_ZoomedPage, dr, mr, GraphicsUnit.Pixel);
                //g.DrawImageUnscaled(Magnifier_ZoomedPage, dr.X - mr.X, dr.Y - mr.Y); // DebugMode では OutOfMemoryException が投げられることがある

                var mathX = dr.X - mr.X;
                var mathY = dr.Y - mr.Y;

                DateTime now;
                var magBitmaps = Magnifier_ZoomedPage.Bitmaps;
                var magOffsets = Magnifier_ZoomedPage.Offsets;
                ReserveNextInvadiateForAnimation(Magnifier_ZoomedPage, out now);

                var imageCount = Magnifier_ZoomedPage.ImageCount;

                lockBits(canvas, new Rectangle(0, 0, pbViewSize.Width, pbViewSize.Height),
                    ImageLockMode.WriteOnly, "zoomed image (canvas)", dst =>
                    {
                        var leftBound = Math.Max(0, mathX);
                        var topBound = Math.Max(0, mathY);
                        var rightBound = Math.Min(pbViewSize.Width, leftBound + mr.Width);
                        var bottomBound = Math.Min(pbViewSize.Height, topBound + mr.Height);
                        var imageHeight = bottomBound - topBound;

                        var color = pbView.BackColor;

                        for (var i = 0; i < imageCount; i++)
                        {
                            var offset = magOffsets[i];
                            var localMathX = mathX + offset.X;
                            var localMathY = mathY + offset.Y;
                            var magBitI = magBitmaps[i];
                            var localRect = new Rectangle(Math.Max(0, -localMathX), Math.Max(0, -localMathY), mr.Width, mr.Height);
                            localRect.Intersect(new Rectangle(0, 0, magBitI.Width, magBitI.Height));
                            var localLeftBound = Math.Max(0, localMathX);
                            var localTopBound = Math.Max(0, localMathY);
                            magBitI.DrawFrame(dst, localLeftBound, localTopBound, localRect, now, canUseParallel: true);

                            if (localTopBound > topBound)
                            {
                                FillRectangle(dst, localLeftBound, topBound, color, localRect.Width, localTopBound - topBound, ParallelMode.Single);
                            }
                            var localBottomBound = Math.Min(pbViewSize.Height, localTopBound + localRect.Height);
                            if (localBottomBound < bottomBound)
                            {
                                FillRectangle(dst, localLeftBound, localBottomBound, color, localRect.Width, bottomBound - localBottomBound, ParallelMode.Single);
                            }

                        }

                        if (topBound > 0) FillRectangle(dst, 0, 0, color, pbViewSize.Width, topBound, ParallelMode.Single);
                        if (leftBound > 0) FillRectangle(dst, 0, topBound, color, leftBound, imageHeight, ParallelMode.Single);
                        if (rightBound < pbViewSize.Width) FillRectangle(dst, rightBound, topBound, color,
                      pbViewSize.Width - rightBound, imageHeight, ParallelMode.Single);
                        if (bottomBound < pbViewSize.Height) FillRectangle(dst, 0, bottomBound, color,
                     pbViewSize.Width, pbViewSize.Height - bottomBound, ParallelMode.Single);
                    });
                return true;
            }
            else
            {
                var totalWidth = 0;
                var startPage = currentPage;
                //var imageHeight =　-1;
                for (var i = startPage; i < stopPage; i++)
                {
                    totalWidth += ResizedImageArray[i].Width;
                }

                var divisions = 1;
                var left = 0;
                var right = totalWidth;

                if (totalWidth <= 0)
                {
                    if (MaxDivision > 1 && 0 <= startPage && startPage == stopPage && ResizedImageArray != null && ResizedImageArray.Length > startPage && getCurrentPageDivison() > 1)
                    {
                        var img = ResizedImageArray[startPage];
                        if (img == null) return sizeChanged;
                        var imageWidth = img.Width;
                        //imageHeight = img.Height;
                        divisions = (imageWidth - 1) / pbViewSize.Width + 1;
                        if (divisions > MaxDivision) return sizeChanged;
                        if (CurrentDividedPosition < 0 || CurrentDividedPosition >= divisions) return sizeChanged;
                        stopPage = startPage + 1;
                        var p = BindingMode != BindingMode.RightToLeft ? CurrentDividedPosition : divisions - CurrentDividedPosition - 1;
                        left = (imageWidth * p + divisions / 2) / divisions;
                        right = (imageWidth * (p + 1) + divisions / 2) / divisions;
                        totalWidth = right - left;
                    }
                }

                if (totalWidth <= 0) return sizeChanged;

                bool pageLeftToRight = BindingMode == BindingMode.LeftToRight && divisions <= 1; // 分割表示の場合の方向指定は left と right で指定
                int leftBound, rightBound;
                if (divisions == 1 && stopPage > startPage + 1 || !(startPage == 0 || stopPage == ResizedImageArray.Length) ||
                   !(BindingMode == BindingMode.LeftToRight || BindingMode == BindingMode.RightToLeft) || totalWidth > pbViewSize.Width / 2)
                {
                    if (stopPage == startPage + 2 && ResizedImageArray[startPage].Width <= pbViewSize.Width / 2 && ResizedImageArray[startPage + 1].Width <= pbViewSize.Width / 2)
                    {
                        leftBound = pbViewSize.Width / 2 - ResizedImageArray[BindingMode == BindingMode.LeftToRight ? startPage : (startPage + 1)].Width;
                    }
                    else
                    {
                        leftBound = (pbViewSize.Width - totalWidth) / 2;
                    }
                }
                else
                {
                    if (BindingMode == BindingMode.LeftToRight ^ startPage == 0)
                    {
                        leftBound = pbViewSize.Width / 2 - totalWidth;
                    }
                    else
                    {
                        leftBound = pbViewSize.Width / 2;
                    }
                }
                rightBound = leftBound + totalWidth;
                var offsetX = pageLeftToRight ? rightBound : leftBound;

                var minOffetY = pbViewSize.Height;
                var maxBottomBound = 0;
                for (var i = startPage; i < stopPage; i++)
                {
                    var resizedSize = ResizedSizeArray[i];
                    var imageHeight = resizedSize.Height;
                    int offsetY = (pbViewSize.Height - imageHeight) / 2;
                    int bottomBound = (pbViewSize.Height + imageHeight) / 2;
                    if (offsetY < minOffetY) minOffetY = offsetY;
                    if (bottomBound > maxBottomBound) maxBottomBound = bottomBound;
                }

                DateTime now;
                ReserveNextInvadiateForAnimation(currentPage, stopPage, out now);

                var bColor = pbView.BackColor;
                var color = pbView_Paint_Rectangle.IsEmpty || !pbView_Paint_Rectangle_FillAround ? bColor : HalfGrayBrusher(pbView.BackColor);

               // var totalWidthMemory = totalWidth;
                totalWidth = 0;
                var start = pageDirection > 0 ? currentPage : stopPage - 1;
                var stop = pageDirection > 0 ? stopPage : currentPage - 1;
                lockBits(canvas, new Rectangle(0, 0, pbViewSize.Width, pbViewSize.Height),
                    ImageLockMode.ReadWrite, "normal image (canvas)", dst =>
                {
                    for (var i = start; i != stop; i += pageDirection)
                    {
                        var resizedSize = ResizedSizeArray[i];
                        var imageHeight = resizedSize.Height;
                        int offsetY = (pbViewSize.Height - imageHeight) / 2;
                        int bottomBound = (pbViewSize.Height + imageHeight) / 2;

                        var imgEx = ResizedImageArray[i];


                        var x = offsetX + (pageLeftToRight ? -totalWidth - imgEx.Width : totalWidth);
                        if (divisions == 1)
                        {
                            //DrawBitmapDataUnscaled(dst, x, offsetY, src);
                            imgEx.DrawFrame(dst, x, offsetY, now);

                            var imageWidth = resizedSize.Width;
                            if (offsetY > minOffetY) FillRectangle(dst, x, minOffetY, bColor, imageWidth, offsetY - minOffetY, ParallelMode.Single);
                            if (bottomBound < maxBottomBound) FillRectangle(dst, x, bottomBound, bColor,
                                imageWidth, maxBottomBound - bottomBound, ParallelMode.Single);

                        }
                        else
                        {
                            var imageWidth = right - left;
                            //DrawBitmapDataUnscaled(dst, x, offsetY, src, left, 0, imageWidth, imgEx.Height);
                            imgEx.DrawFrame(dst, x, offsetY, new Rectangle(left, 0, imageWidth, imgEx.Height), now, canUseParallel: true);

                            if (offsetY > minOffetY) FillRectangle(dst, x, minOffetY, bColor, imageWidth, offsetY - minOffetY, ParallelMode.Single);
                            if (bottomBound < maxBottomBound) FillRectangle(dst, x, bottomBound, bColor,
                                imageWidth, maxBottomBound - bottomBound, ParallelMode.Single);
                        }


                                totalWidth += imgEx.Width;
                    }

                    
                    if (minOffetY > 0) FillRectangle(dst, 0, 0, color, pbViewSize.Width, minOffetY, ParallelMode.Single);
                    if (leftBound > 0) FillRectangle(dst, 0, minOffetY, color, leftBound, maxBottomBound - minOffetY, ParallelMode.Single);
                    if (rightBound < pbViewSize.Width) FillRectangle(dst, rightBound, minOffetY, color,
                         pbViewSize.Width - rightBound, maxBottomBound - minOffetY, ParallelMode.Single);
                    if (maxBottomBound < pbViewSize.Height) FillRectangle(dst, 0, maxBottomBound, color,
                        pbViewSize.Width, pbViewSize.Height - maxBottomBound, ParallelMode.Single);

                    if (!pbView_Paint_Rectangle.IsEmpty)
                    {
                        if (pbView_Paint_Rectangle_FillAround)
                        {
                            FillRectangle(dst, HalfGrayBrusher, new Rectangle(leftBound, minOffetY, pbView_Paint_Rectangle.X - leftBound, maxBottomBound - minOffetY), parallelMode);
                            FillRectangle(dst, HalfGrayBrusher, new Rectangle(pbView_Paint_Rectangle.Right, minOffetY, rightBound - pbView_Paint_Rectangle.Right, maxBottomBound - minOffetY), parallelMode);
                            FillRectangle(dst, HalfGrayBrusher, new Rectangle(pbView_Paint_Rectangle.X, minOffetY, pbView_Paint_Rectangle.Width, pbView_Paint_Rectangle.Y - minOffetY), parallelMode);
                            FillRectangle(dst, HalfGrayBrusher, new Rectangle(pbView_Paint_Rectangle.X, pbView_Paint_Rectangle.Bottom, pbView_Paint_Rectangle.Width, maxBottomBound - pbView_Paint_Rectangle.Bottom), parallelMode);
                        }
                        else
                        {
                            FillRectangle(dst, HalfGrayBrusher, pbView_Paint_Rectangle, parallelMode);
                            //g.FillRectangle(pbView_Paint_Brush, pbView_Paint_Rectangle);
                        }

                        if (ViewerMode == ViewerModeEnum.MagnifierOpening || ViewerMode == ViewerModeEnum.MagnifierClosing)
                        {
                            int borderWidth = Program.DpiScaling(5);
                            var bHalf = borderWidth / 2;
                            var bHalf2 = borderWidth - bHalf; // 奇数を想定
                            //var borderColor = Color.Chartreuse
                            var borderColor = Color.Yellow;

                            if (magnifyingPower == MagnifyingInfo.FitHorizontal)
                            {
                                FillRectangle(dst, pbView_Paint_Rectangle.X - bHalf2, 0, borderColor, borderWidth, pbViewSize.Height, ParallelMode.Single);
                                FillRectangle(dst, pbView_Paint_Rectangle.Right - bHalf, 0, borderColor, borderWidth, pbViewSize.Height, ParallelMode.Single);
                            }
                            if (magnifyingPower == MagnifyingInfo.FitVertical)
                            {
                                FillRectangle(dst, 0, pbView_Paint_Rectangle.Y - bHalf2, borderColor, pbViewSize.Width, borderWidth, ParallelMode.Single);
                                FillRectangle(dst, 0, pbView_Paint_Rectangle.Bottom - bHalf, borderColor, pbViewSize.Width, borderWidth, ParallelMode.Single);
                            }
                        }

                    }

                });
                return true;
            }
        }

        DateTime ReserveNextInvadiateForAnimation_ReservedTime = DateTime.MaxValue;

        readonly HashSet<BitmapEx> ReserveNextInvadiateForAnimation_LastVisible = new HashSet<BitmapEx>();

        private void ReserveNextInvadiateForAnimation(MagnifierCanvas magCanvas, out DateTime now) // start < stop であること
        {
            var targets = (from t in magCanvas.Bitmaps where t.FrameCount > 1 select t.GetSourceForLock()).ToArray();
            ReserveNextInvadiateForAnimation(targets, out now);
        }

        private void ReserveNextInvadiateForAnimation(int start, int stop, out DateTime now) // start < stop であること
        {
            // 描くたびに呼ばれるので、アニメーションがない（ほとんどの）場合は配列を作らない（V32）
            var resized = ResizedImageArray;
            List<BitmapEx> list = null;
            for (var i = start; i < stop; i++)
            {
                var t = resized[i];
                if (t.FrameCount > 1) (list ?? (list = new List<BitmapEx>())).Add(t.GetSourceForLock());
            }
            ReserveNextInvadiateForAnimation(list == null ? NoAnimationTargets : list.ToArray(), out now);
        }

        private static readonly BitmapEx[] NoAnimationTargets = new BitmapEx[0];

        private void ReserveNextInvadiateForAnimation(BitmapEx[] targets, out DateTime now)
        {
            if (targets.Length <= 0)
            {
                TimerResolution.SetHighResolution(false);
                ReserveNextInvadiateForAnimation_LastVisible.Clear();
                now = default(DateTime);
                ReserveNextInvadiateForAnimation_ReservedTime = default(DateTime);
                return;
            }
            now = DateTime.Now;
            foreach (var t in targets)
            {
                if (!ReserveNextInvadiateForAnimation_LastVisible.Contains(t)) t.Start(now);
            }
            ReserveNextInvadiateForAnimation_LastVisible.Clear();
            foreach (var t in targets) ReserveNextInvadiateForAnimation_LastVisible.Add(t);
            var now_ = now;
            var reserveTime = (from t in targets select t.GetNextChangeTime(now_)).Min();
            if (reserveTime > now)
            {
                // 次のコマを既定の約 15.6ms 単位に切り上げずに待てるよう、アニメーションの間だけタイマーの分解能を上げる
                TimerResolution.SetHighResolution(true);
                ReserveNextInvadiateForAnimation_AsyncPart(reserveTime, now, targets);
            }
            else
            {
                TimerResolution.SetHighResolution(false);
                ReserveNextInvadiateForAnimation_LastVisible.Clear();
                now = default(DateTime);
                ReserveNextInvadiateForAnimation_ReservedTime = default(DateTime);
                return;
            }
        }

        private async void ReserveNextInvadiateForAnimation_AsyncPart(DateTime reserveTime, DateTime now, BitmapEx[] targets)
        {
            ReserveNextInvadiateForAnimation_ReservedTime = reserveTime;
            await Task.Delay(reserveTime - now);
            if (ReserveNextInvadiateForAnimation_ReservedTime != reserveTime
                || !ReserveNextInvadiateForAnimation_LastVisible.SetEquals(targets)) return;
            // 隠れている・最小化しているときは描かれず次の予約も起きないので、分解能を戻しておく（また描かれたときに上げ直す）
            if (!Visible || WindowState == FormWindowState.Minimized) TimerResolution.SetHighResolution(false);
            pbView.Invalidate();
        }

        public static void DrawBitmapDataUnscaled(BitmapData dst, BitmapData src)
        {
            DrawBitmapDataUnscaled(dst, 0, 0, src);
        }

        /// <summary>
        /// 8bpp インデックスの src を、パレットで引いて 24bpp の dst に描く（クリップは DrawBitmapDataUnscaled と同じ）。
        /// 8bpp のページを 24bpp で LockBits すると、GDI+ がロックした範囲全体を一時のバッファに変換してからコピーすることになるので、
        /// 見えている範囲だけを 1 回で展開する。palette は不透明の色だけであること（呼び出し側で確かめる）
        /// </summary>
        public static unsafe void DrawIndexedBitmapDataUnscaled(BitmapData dst, int xDst, int yDst, BitmapData src, Color[] palette)
        {
            if (src.PixelFormat != PixelFormat.Format8bppIndexed || dst.PixelFormat != PixelFormat.Format24bppRgb) throw new InvalidEnumArgumentException();
            var rect = new Rectangle(-xDst, -yDst, dst.Width, dst.Height);
            rect.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            if (rect.Width <= 0 || rect.Height <= 0) return;
            // BGR を下位 3 バイトに入れた表。4 バイトまとめて書き、4 バイト目は次の画素で上書きされる（行の最後の画素だけは 3 バイトずつ書く）
            var lut = stackalloc uint[256];
            for (var i = 0; i < 256 && i < palette.Length; i++)
            {
                var c = palette[i];
                lut[i] = (uint)(c.B | c.G << 8 | c.R << 16);
            }
            var width = rect.Width;
            var height = rect.Height;
            var dstStride = dst.Stride;
            var srcStride = src.Stride;
            var dstLeft = (byte*)dst.Scan0 + (yDst + rect.Y) * dstStride + (xDst + rect.X) * 3;
            var srcLeft = (byte*)src.Scan0 + rect.Y * srcStride + rect.X;
            for (var j = 0; j < height; j++)
            {
                var d = dstLeft;
                var sp = srcLeft;
                var last = srcLeft + width - 1;
                while (sp < last)
                {
                    *(uint*)d = lut[*sp++];
                    d += 3;
                }
                var v = lut[*sp];
                d[0] = (byte)v;
                d[1] = (byte)(v >> 8);
                d[2] = (byte)(v >> 16);
                dstLeft += dstStride;
                srcLeft += srcStride;
            }
        }

        /// <summary>
        /// パレットがすべて不透明なら、その色の配列を返す。そうでなければ null（GDI+ の変換に任せる）
        /// </summary>
        public static Color[] GetOpaquePaletteEntries(Bitmap bmp)
        {
            var entries = bmp.Palette.Entries;
            foreach (var c in entries) if (c.A != 255) return null;
            return entries;
        }

        public static void DrawBitmapDataUnscaled(BitmapData dst, int xDst, int yDst, BitmapData src)
        {
            DrawBitmapDataUnscaled(dst, xDst, yDst, src, 0, 0, src.Width, src.Height);
        }

        private static void DrawBitmapDataUnscaled(BitmapData dst, int xDst, int yDst, BitmapData src, int xSrc, int ySrc, int width, int height)
        {
            var rect = new Rectangle(-xDst, -yDst, dst.Width, dst.Height);
            rect.Intersect(new Rectangle(-xSrc, -ySrc, src.Width, src.Height));
            rect.Intersect(new Rectangle(0, 0, width, height));
            xDst += rect.X;
            yDst += rect.Y;
            xSrc += rect.X;
            ySrc += rect.Y;
            width = rect.Width;
            height = rect.Height;
            
            var pixelFormat = dst.PixelFormat;
            if (src.PixelFormat != pixelFormat) throw new InvalidEnumArgumentException();
            var bytePerPixel = Image.GetPixelFormatSize(pixelFormat) >> 3;
            var dstStride = dst.Stride;
            var srcStride = src.Stride;
            var dstLeft = dst.Scan0 + yDst * dstStride + xDst * bytePerPixel;
            var srcLeft = src.Scan0 + ySrc * srcStride + xSrc * bytePerPixel;
            var buffSize = width * bytePerPixel;
            //var buff = new byte[buffSize];
            for (var j = 0; j < height; j++)
            {
                //System.Runtime.InteropServices.Marshal.Copy(srcLeft, buff, 0, buffSize);
                //System.Runtime.InteropServices.Marshal.Copy(buff, 0, dstLeft, buffSize);
                CopyMemory(dstLeft, srcLeft, buffSize);
                dstLeft += dstStride;
                srcLeft += srcStride;
            }
        }

        private void FillRectangle(BitmapData dst, Action<IntPtr, int> editor, Rectangle rect, ParallelMode parallelMode)
        {
            FillRectangle(dst, rect.X, rect.Y, editor, rect.Width, rect.Height, parallelMode);
        }

        private unsafe void FillRectangle(BitmapData dst, int xDst, int yDst, Action<IntPtr, int> editor, int width, int height, ParallelMode parallelMode)
        {
            var rec = new Rectangle(0, 0, dst.Width, dst.Height);
            rec.Intersect(new Rectangle(xDst, yDst, width, height));
            xDst = rec.X;
            yDst = rec.Y;
            width = rec.Width;
            height = rec.Height;

            var pixelFormat = dst.PixelFormat;
            var bytePerPixel = Image.GetPixelFormatSize(pixelFormat) >> 3;
            var dstStride = dst.Stride;
            var dstLeft = dst.Scan0 + yDst * dstStride + xDst * bytePerPixel;
            var buffSize = width * bytePerPixel;


            ParallelFor(parallelMode, 0, height, j =>
            {
                var currentDstLeft = dstLeft + (j * dstStride);
                editor(currentDstLeft, buffSize);
            });
        }

        [ThreadStatic] private static byte[] fillRectangleBuff;
        [ThreadStatic] private static int fillRectangleBuffColor;
        private void FillRectangle(BitmapData dst, Color color, Rectangle rect, ParallelMode parallelMode)
        {
            FillRectangle(dst, rect.X, rect.Y, color, rect.Width, rect.Height, parallelMode);
        }

        private unsafe void FillRectangle(BitmapData dst, int xDst, int yDst, Color color, int width, int height, ParallelMode parallelMode)
        {
            var rec = new Rectangle(0, 0, dst.Width, dst.Height);
            rec.Intersect(new Rectangle(xDst, yDst, width, height));
            xDst = rec.X;
            yDst = rec.Y;
            width = rec.Width;
            height = rec.Height;

            var pixelFormat = dst.PixelFormat;
            var bytePerPixel = Image.GetPixelFormatSize(pixelFormat) >> 3;
            var dstStride = dst.Stride;
            var dstLeft = dst.Scan0 + yDst * dstStride + xDst * bytePerPixel;
            var buffSize = width * bytePerPixel;
            if (bytePerPixel != 3) throw new NotImplementedException();
            // 描くたびに 1 行分の配列を作らない。色が同じで長さが足りれば使い回す（V32）
            var buff = fillRectangleBuff;
            if (buff == null || buff.Length < buffSize || fillRectangleBuffColor != color.ToArgb())
            {
                buff = new byte[Math.Max(buffSize, buff?.Length ?? 0)];
                var b = color.B;
                var g = color.G;
                var r = color.R;
                var i = 0;
                while (i + 2 < buff.Length)
                {
                    buff[i++] = b;
                    buff[i++] = g;
                    buff[i++] = r;
                }
                fillRectangleBuff = buff;
                fillRectangleBuffColor = color.ToArgb();
            }

            fixed (byte* a = buff)
            {
                var buffAdr = (IntPtr)a;

                ParallelFor(parallelMode, 0, height, j =>
                {
                    var currentDstLeft = dstLeft + (j * dstStride);
                    //System.Runtime.InteropServices.Marshal.Copy(buff, 0, currentDstLeft, buffSize);
                    CopyMemory(currentDstLeft, buffAdr, buffSize);
                });
            }
        }

        // UI スレッド上で Parallel は使えない（この例では待機を放棄している）
        // http://dalmore.blog7.fc2.com/blog-entry-35.html
        private enum ParallelMode { Single, Parallel, ParallelForUIThread }

        private static void ParallelFor<T>(ParallelMode parallelMode, int fromInclusive, int toExclusive, Func<T> localInit, Action<int, T> body, Action<T> localFinally = null)
        {
            switch(parallelMode)
            {
                case ParallelMode.Single:
                case ParallelMode.ParallelForUIThread:
                    {
                        var local = localInit();
                        for (var i = fromInclusive; i < toExclusive; i++) body(i, local);
                        localFinally?.Invoke(local);
                        break;
                    }
                case ParallelMode.Parallel:
                    {
                        Parallel.For(fromInclusive, toExclusive, localInit, (i, state, local) => { body(i, local); return local; }, localFinally);
                        break;
                    }
            }
        }

        private static void ParallelFor(ParallelMode parallelMode, int fromInclusive, int toExclusive, Action<int> body)
        {
            switch (parallelMode)
            {
                case ParallelMode.Single:
                case ParallelMode.ParallelForUIThread:
                    {
                        for (var i = fromInclusive; i < toExclusive; i++) body(i);
                        break;
                    }
                case ParallelMode.Parallel:
                    {
                        Parallel.For(fromInclusive, toExclusive, body);
                        break;
                    }
            }
        }

        private enum ViewerModeEnum { Normal, MagnifierOpening, Magnifier, MagnifierClosing, CoverSetting }

        private ViewerModeEnum viewerMode = ViewerModeEnum.Normal;

        private ViewerModeEnum ViewerMode
        {
            get
            {
                return viewerMode;
            }
            set
            {
                var preCover = viewerMode == ViewerModeEnum.CoverSetting;
                viewerMode = value;
                var cover = value == ViewerModeEnum.CoverSetting;
                magnifierToolStripMenuItem.Checked = viewerMode == ViewerModeEnum.Magnifier;
                if (preCover != cover)
                {
                    if(cover)
                    {
                        ksViewForm.LButtonAcceptControls = new Control[0];
                    }
                    else
                    {
                        ksViewForm.LButtonAcceptControls = new Control[] { pbView };
                    }
                }
            }
        }
    }
}
