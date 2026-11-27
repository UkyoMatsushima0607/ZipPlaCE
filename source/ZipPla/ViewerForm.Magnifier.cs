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
        class MagnifierCanvas : IDisposable
        {
            private VirtualBitmapEx[] virtualBitmapExArray;
            public VirtualBitmapEx[] Bitmaps { get { return virtualBitmapExArray; } }
            private Point[] offsetArray;
            public Point[] Offsets { get { return offsetArray; } }
            public int ImageCount { get { return virtualBitmapExArray.Length; } }

            private MagnifierCanvas() { }

            public MagnifierCanvas(Size[] zoomedSizeArray/*, BitmapEx[] originalBitmapExArray, bool isEntity*/)
            {
                virtualBitmapExArray = new VirtualBitmapEx[zoomedSizeArray.Length];
                offsetArray = new Point[zoomedSizeArray.Length];
                try
                {
                    for (var i = 0; i < zoomedSizeArray.Length; i++)
                    {
                        //var original = originalBitmapExArray[i];
                        //if (original == null) continue;
                        var size = zoomedSizeArray[i];
                        var width = size.Width;
                        var height = size.Height;
                    }
                }
                catch
                {
                    throw;
                }
            }

            public void Dispose()
            {
                for (var i = 0; i < virtualBitmapExArray.Length; i++)
                {
                    var bitmapEx = virtualBitmapExArray[i];
                    if (bitmapEx != null)
                    {
                        bitmapEx.Dispose();
                        virtualBitmapExArray[i] = null;
                    }
                }
            }

            public VirtualBitmapEx this[int i]
            {
                get { return virtualBitmapExArray[i]; }
                set
                {
                    virtualBitmapExArray[i] = value;
                }
            }

            public void SetErrorImage(int i, Size size)
            {
                var bitmapEx = virtualBitmapExArray[i];
                //var size = bitmapEx.Size;
                bitmapEx?.Dispose();
                // bitmapEx = new VirtualBitmapEx(BitmapEx.ConvertToBitmapEx(Program.GetErrorImage(size.Width, size.Height)));
                virtualBitmapExArray[i] = VirtualBitmapEx.GetErrorImage(size);
            }

            public static MagnifierCanvas GetErrorCanvas(Size size)
            {
                var result = new MagnifierCanvas();
                result.virtualBitmapExArray = new VirtualBitmapEx[1] { VirtualBitmapEx.GetErrorImage(size) };
                result.offsetArray = new Point[1] { new Point(0, 0) };
                return result;
            }

        }

        //private Tuple<int, int> Magnifier_VisiblePage = null;
        private int Magnifier_currentPageReady = -1;

        private MagnifierCanvas Magnifier_ZoomedPage = null;

        private int Magnifier_ZoomedPage_StartPage = -1;

        private int Magnifier_ZoomedPage_StopPage = -1;

        private void SetMagnifierCanvasInfo(int start, int stop)
        {
            Magnifier_ZoomedPage_StartPage = start;
            Magnifier_ZoomedPage_StopPage = stop;
        }

        private bool MagnifierCanvasPrepared()
        {
            if (Magnifier_ZoomedPage == null) return false;
            if (currentPage < 0) return false;
            if (currentPage != Magnifier_ZoomedPage_StartPage) return false;
            if (ResizedImageArray == null || currentPage >= ResizedImageArray.Length || ResizedImageArray[currentPage] == null) return false;
            var nextPage = NextPage;
            if (nextPage < currentPage || nextPage == currentPage && (MaxDivision <= 1 || getCurrentPageDivison() <= 1)) return false;
            if (nextPage == currentPage) nextPage++;
            return Magnifier_ZoomedPage_StopPage == nextPage;
        }

        private void bwMagnifierMaker_DoWork(object sender, DoWorkEventArgs e)
        {
            var tplArgs = (Tuple<int, int, int, int, Size[]>)e.Argument;
            var totalWidth = tplArgs.Item1;
            var zoomedHeight = tplArgs.Item2;
            var nextPage = tplArgs.Item3;
            var currentPage = tplArgs.Item4;
            var zoomedSizeArray = tplArgs.Item5;

            var arrangedZoomedSizeArray = new Size[nextPage - currentPage];
            var originalBitmapExArray = new BitmapEx[nextPage - currentPage];
            //var canvasIndex = 0;
            var nextPage1 = nextPage - 1;
            var preFiltered = PreFilteredImageArray;
            if (preFiltered == null || nextPage1 >= preFiltered.Length) return; // 本が切り替わった
            for (var i = nextPage1; i >= currentPage; i--)
            {
                var canvasIndex = nextPage1 - i;
                arrangedZoomedSizeArray[canvasIndex] = zoomedSizeArray[i - currentPage];
                lock (preFiltered) originalBitmapExArray[canvasIndex] = preFiltered[i]; // LockManager.ForIReadOnlyList と同じ手順
                canvasIndex++;
            }

            //BitmapEx canvasExArray = new Bitmap(totalWidth, zoomedHeight, PixelFormat.Format24bppRgb);
            var canvas = new MagnifierCanvas(arrangedZoomedSizeArray/*, originalBitmapExArray, isEntity*/);
            var offsets = canvas.Offsets;

            try
            {
                var cumulativeWidth = 0;
                var pageLeftToRight = BindingMode == BindingMode.LeftToRight;
                //Graphics graphics = null;
                //canvasIndex = 0;
                for (var i = nextPage1; i >= currentPage; i--)
                {
                    var canvasIndex = nextPage1 - i;
                    //var canvas1 = canvas[canvasIndex];
                    var imgEx = originalBitmapExArray[canvasIndex];
                    var zoomedSize = zoomedSizeArray[i - currentPage];
                    var zoomedWidth = zoomedSize.Width;
                    var zoomedHeightI = zoomedSize.Height;
                    var X = pageLeftToRight ? totalWidth - cumulativeWidth - zoomedWidth : cumulativeWidth;
                    offsets[canvasIndex] = new Point(X, (zoomedHeight - zoomedHeightI) / 2);
                    var size = new Size(zoomedWidth, zoomedHeightI);
                    if (imgEx != null)
                    {
                        lock (imgEx)
                        {
                            // ロックを待っている間に、原寸が破棄された（DiscardLoadedPage）か本が切り替わった。
                            // 破棄済みの画像は使えないので、拡大画像を作らずに抜ける（結果なし。必要になれば作り直される）
                            if (!ReferenceEquals(preFiltered, PreFilteredImageArray) || !ReferenceEquals(preFiltered[i], imgEx))
                            {
                                canvas.Dispose();
                                return;
                            }
                            var isEntity = imgEx.FrameCount == 1/* && BitmapEx.GetDataSizeInBytes(imgEx, zoomedSize) < GetUserMemoryUBound() / 2*/;
                            if (isEntity)
                            {
                                //var rect = new Rectangle(X, (zoomedHeight - zoomedHeightI) / 2, zoomedWidth, zoomedHeightI);
                                //var rect = new Rectangle(0, (zoomedHeight - zoomedHeightI) / 2, zoomedWidth, zoomedHeightI);


                                var gamma = ResizeGammaMagnifier;

                                // 0.1 秒、またキャンセルも可能
                                ScalingAlgorithm scalingAlgorithm;
                                if ((long)zoomedWidth * zoomedHeight > (long)imgEx.Width * imgEx.Height)
                                {
                                    scalingAlgorithm = MagnifierScalingAlgorithm.ScaleUp;
                                }
                                else
                                {
                                    scalingAlgorithm = MagnifierScalingAlgorithm.ScaleDown;
                                }
                                const int pixelsPerPixelToBeLeft = int.MaxValue;
                                const bool parallel = true;

                                
                                var canvas1 = imgEx.CreateNew(img =>
                                {
                                    if (bwMagnifierMaker.CancellationPending)
                                    {
                                        imgEx.CancelApply = true;
                                        return null;
                                    }

                                    switch (scalingAlgorithm)
                                    {
                                        case ScalingAlgorithm.HighSpeed: return QuickGraphic.CreateNew(img, size, parallel, bwMagnifierMaker);
                                        //case ScalingAlgorithm.HighSpeed: LongVectorImageResizer.Spline4(img, 2, canvas, rect, parallel, bwMagnifierMaker); break;
                                        case ScalingAlgorithm.NearestNeighbor: return LongVectorImageResizer.NearestNeighbor(img, size, parallel, bwMagnifierMaker);
                                        case ScalingAlgorithm.AreaAverage: return LongVectorImageResizer.AreaAverage(img, pixelsPerPixelToBeLeft, gamma, size, parallel, bwMagnifierMaker);
                                        case ScalingAlgorithm.Lanczos1: return LongVectorImageResizer.Lanczos(1, img, pixelsPerPixelToBeLeft, gamma, size, parallel, bwMagnifierMaker);
                                        case ScalingAlgorithm.Lanczos2: return LongVectorImageResizer.Lanczos(2, img, pixelsPerPixelToBeLeft, gamma, size, parallel, bwMagnifierMaker);
                                        case ScalingAlgorithm.Lanczos3: return LongVectorImageResizer.Lanczos(3, img, pixelsPerPixelToBeLeft, gamma, size, parallel, bwMagnifierMaker);
                                        case ScalingAlgorithm.Lanczos4: return LongVectorImageResizer.Lanczos(4, img, pixelsPerPixelToBeLeft, gamma, size, parallel, bwMagnifierMaker);
                                        case ScalingAlgorithm.Spline4: return LongVectorImageResizer.Spline4(img, pixelsPerPixelToBeLeft, gamma, size, parallel, bwMagnifierMaker);
                                        case ScalingAlgorithm.Spline16: return LongVectorImageResizer.Spline16(img, pixelsPerPixelToBeLeft, gamma, size, parallel, bwMagnifierMaker);
                                        case ScalingAlgorithm.Spline36: return LongVectorImageResizer.Spline36(img, pixelsPerPixelToBeLeft, gamma, size, parallel, bwMagnifierMaker);
                                        case ScalingAlgorithm.Spline64: return LongVectorImageResizer.Spline64(img, pixelsPerPixelToBeLeft, gamma, size, parallel, bwMagnifierMaker);
                                        //default: QuickGraphic.DrawImage(canvas, rect, img, bwMagnifierMaker); break;

                                        // 品質の良さを重視する場合
                                        // 他のオプションがあるので現在は不採用
                                        // BitmapResizer の使用を止めるならこちらを採用か
                                        //default: LongVectorImageResizer.Lanczos(3, img, 4, GammaConversion.Value1_0, canvas, rect, parallel, bwMagnifierMaker); break;

                                        // キャンセルが必要なので BitmapResizer は使えないがそれと同等の出力であることを重視
                                        // BitmapResizer は BitmapScalingMode.Unspecified を使用しており、これが Linear であることは以下に明記されている
                                        // https://msdn.microsoft.com/en-us/library/system.windows.media.bitmapscalingmode(v=vs.110).aspx
                                        default: return LongVectorImageResizer.Spline4(img, int.MaxValue, GammaConversion.Value1_0, size, parallel, bwMagnifierMaker);
                                    }
                                });

                                if (canvas1 == null)
                                {
                                    canvas.Dispose();
                                    return; // キャンセルがかかった場合
                                }


                                //sw.Stop(); MessageBox.Show($"{sw.Elapsed}");
                                try
                                {
                                    if (ViewerMode != ViewerModeEnum.CoverSetting)
                                    {
                                        ViewerFormImageInfo imageInfo;
                                        try
                                        {
                                            imageInfo = OriginalImageInfoArray[i];
                                        }
                                        catch
                                        {
                                            imageInfo = null;
                                        }
                                        if (canvas1.FrameCount == 1)
                                        {
                                            if (imageInfo != null)
                                            {
                                                imageFilter.PostFilter(canvas1, ref imageInfo.MaxColorDiff, bwMagnifierMaker);
                                            }
                                            else
                                            {
                                                int? dummy = null;
                                                imageFilter.PostFilter(canvas1, ref dummy, bwMagnifierMaker);
                                            }
                                        }
                                        else
                                        {
                                            int? dummy = imageInfo?.MaxColorDiff ?? 255;
                                            canvas1.ApplyToAll(img =>
                                            {
                                                if(bwMagnifierMaker.CancellationPending)
                                                {
                                                    canvas1.CancelApply = true;
                                                    return img; // Dispose が必要なので null は誤り
                                                }
                                                imageFilter.PostFilter(img, ref dummy, bwMagnifierMaker);
                                                return img;
                                            });
                                        }
                                    }
                                    if (bwMagnifierMaker.CancellationPending)
                                    {
                                        canvas1.Dispose();
                                        canvas.Dispose();
                                        return;
                                    }
                                }
                                catch
                                {
                                    canvas1.Dispose();
                                    throw;
                                }

                                canvas[canvasIndex] = new VirtualBitmapEx(canvas1, 0);
                            }
                            else
                            {
                                canvas[canvasIndex] = GetVirtualBitmapExForMagnifier(imgEx, size);
                            }
                        }
                    }
                    else
                    {
                        canvas.SetErrorImage(canvasIndex, size);
                    }
                    cumulativeWidth += zoomedWidth;
                }
                //graphics?.Dispose();
            }
            catch
            {
                //if(canvas != null) canvas.Dispose();
                canvas.Dispose();
                //canvas = MagnifierCanvas.FromBitmapEx(BitmapEx.ConvertToBitmapEx(Program.GetErrorImage(totalWidth, zoomedHeight)));
                canvas = MagnifierCanvas.GetErrorCanvas(new Size(totalWidth, zoomedHeight));
            }

            e.Result = Tuple.Create(canvas, currentPage);
        }

        private VirtualBitmapEx GetVirtualBitmapExForStandard(BitmapEx source, Size virtualSize)
        {
            return GetVirtualBitmapEx(source, virtualSize, StandardScalingAlgorithm);
        }

        private VirtualBitmapEx GetVirtualBitmapExForMagnifier(BitmapEx source, Size virtualSize)
        {
            return GetVirtualBitmapEx(source, virtualSize, MagnifierScalingAlgorithm);
        }

        private static VirtualBitmapEx GetVirtualBitmapEx(BitmapEx source, Size virtualSize, ScalingAlgorithmPair algorithm)
        {
            return new VirtualBitmapEx(source, virtualSize, algorithm.ScaleDown == ScalingAlgorithm.NearestNeighbor,
                algorithm.ScaleUp == ScalingAlgorithm.AreaAverage || algorithm.ScaleUp == ScalingAlgorithm.NearestNeighbor);
            //upScaleNearestNeighbor: true);
        }

        private void bwMagnifierMaker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            var t = e.Result as Tuple<MagnifierCanvas, int>;
            if (Magnifier_ZoomedPage != null) Magnifier_ZoomedPage.Dispose();
            Magnifier_ZoomedPage = t?.Item1;
            Magnifier_currentPageReady = t == null ? -1 : t.Item2;


            //optionToolStripMenuItem.Text = $"mag loaded: {ViewerMode} {bwMagnifierMaker_Q == null} {MagnifierPhase2_Q}";

            if (bwMagnifierMaker_Q == null)
            {
                //bmwLoadEachPage.ThreadCount = 1;
                if (MagnifierPhase2_Q)
                {
                    MagnifierPhase2_Q = false;
                    MagnifierPhase2();
                }
            }
            else
            {
                bwMagnifierMaker.RunWorkerAsync(bwMagnifierMaker_Q);
                bwMagnifierMaker_Q = null;
            }

            // 拡大画像の作成中は原寸を捨てずにおいたので、ここで捨てる（C1）
            TrimOriginals();
        }

        private void IfTouchModifyingThenMoveOrigin(bool inverse)
        {
            if (GetMagnifierRectangle_LastCursorPosition != null)
            {
                if (BindingMode != BindingMode.RightToLeft)
                {
                    if (!inverse)
                    {
                        GetMagnifierRectangle_LastCursorPosition = new Point(int.MinValue, int.MinValue);
                    }
                    else
                    {
                        GetMagnifierRectangle_LastCursorPosition = new Point(int.MaxValue, int.MaxValue);
                    }
                }
                else
                {
                    if (!inverse)
                    {
                        GetMagnifierRectangle_LastCursorPosition = new Point(int.MaxValue, int.MinValue);
                    }
                    else
                    {
                        GetMagnifierRectangle_LastCursorPosition = new Point(int.MinValue, int.MaxValue);
                    }
                }
            }
        }

        private bool MagnifierPhase1_Q = false;

        private Point? MagnifierPhase1_QValue = null;

        private void MagnifierPhase1(Point? initialCenterLocation, bool ifNoopThenQ = false)
        {
            var nextPage = NextPage;
            //if (currentPage < 0 || nextPage <= currentPage) return;
            if (currentPage < 0 || nextPage < currentPage || nextPage == currentPage && getCurrentPageDivison() <= 1 || ResizedImageArray == null ||
                ResizedImageArray.Length <= currentPage || ResizedImageArray[currentPage] == null)
            {
                if (ifNoopThenQ)
                {
                    MagnifierPhase1_Q = true;
                    MagnifierPhase1_QValue = initialCenterLocation;
                }
                return;
            }
            ViewerMode = ViewerModeEnum.MagnifierOpening;
            ksViewForm.Enabled = false;
            pbView_Paint_Rectangle_FillAround = true;
            GetMagnifierRectangle_LastCursorPosition = initialCenterLocation;
            ShwoMagnifierRectangle();
            Program.HideCursor();
            EnsureMagnifierCanvas();
        }

        // ルーペを開いたら true（V2）。true の間はページごとに拡大画像を先に作り、原寸を表示中＋前後1見開き分残す。
        // 別の本を開いたときと、ルーペを使わずに MagnifierUnusedPageTurnsLimit 回ページを送ったときに false に戻す（V25）
        private bool magnifierWanted = false;

        private int magnifierUnusedPageTurns = 0;

        private const int MagnifierUnusedPageTurnsLimit = 10;

        // ルーペを開くときに拡大画像がなければ作り始める（V2）。
        // 原寸がなければ先に読み直させる。読み直しが終わると showCurrentPage から resetMagnifier が呼ばれる
        private void EnsureMagnifierCanvas()
        {
            magnifierUnusedPageTurns = 0; // V25
            if (!magnifierWanted)
            {
                magnifierWanted = true;
                ReloadTrimmedOriginalsNearCurrent();
            }
            if (MagnifierCanvasPrepared()) return;
            if (bwMagnifierMaker.IsBusy && Magnifier_ZoomedPage_StartPage == currentPage) return; // 作成中
            resetMagnifier();
        }

        bool MagnifierPhase2_Q = false;

        private void MagnifierPhase2()
        {
            pbView_Paint_RectangleMessage = null;
            if (ViewerMode != ViewerModeEnum.MagnifierOpening)
            {
                ksViewForm.Enabled = true;
                return;
            }
            MagnifierPhase1_Q = false;
            if (Magnifier_ZoomedPage == null || Magnifier_currentPageReady != currentPage || bwMagnifierMaker.IsBusy)
            {
                MagnifierPhase2_Q = true;
            }
            if (MagnifierPhase2_Q) return;
            pbView_Paint_Rectangle = Rectangle.Empty;
            ViewerMode = ViewerModeEnum.Magnifier;
            ksViewForm.Enabled = true;
            //pbView.Invalidate(false); // 描画は Paint イベントハンドラで行われる
            //GetMagnifierRectangle_LastCursorPosition = null;
            pbPaintInvalidate();
        }

        private void MagnifierPhase3(bool toPhase4)
        {
            if (ViewerMode != ViewerModeEnum.Magnifier) return;
            ViewerMode = ViewerModeEnum.MagnifierClosing;
            ksViewForm.Enabled = false;
            pbView_Paint_Rectangle_FillAround = false;
            //GetMagnifierRectangle_LastCursorPosition = null;
            if (!toPhase4) ShwoMagnifierRectangle();
        }

        private void MagnifierPhase4(bool toPhase1)
        {
            pbView_Paint_RectangleMessage = null;
            if (ViewerMode != ViewerModeEnum.MagnifierClosing)
            {
                ksViewForm.Enabled = true;
                return;
            }
            ViewerMode = ViewerModeEnum.Normal;
            ksViewForm.Enabled = true;
            pbView_Paint_Rectangle = Rectangle.Empty;
            //pbView.Invalidate(false); // 描画は Paint イベントハンドラで行われる
            if (!toPhase1)
            {
                pbPaintInvalidate();
                Program.ShowCursor();
            }
        }

        private void ShwoMagnifierRectangle(bool invalidiate = true)
        {
            pbView_Paint_Rectangle = GetMagnifierRectangle(true, null);
            //pbView.Invalidate(false); // 描画は Paint イベントハンドラで行われる
            if (invalidiate) pbPaintInvalidate();
        }

        Point? GetMagnifierRectangle_LastCursorPosition = null;

        //VectorD GetMagnifierRectangle_LastCursorDelta;
        // bool GetMagnifierRectangle_LastSmooth;


        private Rectangle GetMagnifierRectangle(bool PreviewMode, VectorD? delta)
        {
            return GetMagnifierRectangle(PreviewMode, delta, pbView.Size, out var touchMode, out var imageRectangle, out var magPower_);
        }

        /// <summary>
        /// PreviewMode == true なら切り替え途中の長方形を取得、false なら拡大して描画すべき長方形を取得
        /// </summary>
        /// <param name="PreviewMode"></param>
        /// <returns></returns>
        private Rectangle GetMagnifierRectangle(bool PreviewMode, VectorD? delta, Size pbViewSize, out bool touchMode, out Rectangle imageRectangle, out double? magPower_)
        {
            var totalWidth = 0;
            var nextPage = NextPage;

            touchMode = false;


            magPower_ = getCurrentMagnifierPower();
            if (magPower_ == null || currentPage < 0 || nextPage < currentPage || MaxDivision <= 1 && nextPage == currentPage ||
                ResizedImageArray == null || currentPage >= ResizedImageArray.Length || ResizedImageArray[currentPage] == null)
            {
                imageRectangle = default(Rectangle);
                return Rectangle.Empty;
            }
            var magPower = (double)magPower_;
            var division = 1;
            if (currentPage == nextPage)
            {
                nextPage++;
                division = getCurrentPageDivison();
                if (division <= 1 || CurrentDividedPosition < 0 || CurrentDividedPosition >= division)
                {
                    imageRectangle = default(Rectangle);
                    return Rectangle.Empty;
                }
            }
            for (var i = currentPage; i < nextPage; i++)
            {
                var img = ResizedImageArray[i];
                if (img == null)
                {
                    imageRectangle = default(Rectangle);
                    return Rectangle.Empty;
                }
                totalWidth += img.Width;
            }

            var divisionOffset = 0;
            if (division > 1)
            {
                int p;
                if (BindingMode != BindingMode.RightToLeft)
                {
                    p = CurrentDividedPosition;
                }
                else
                {
                    p = division - 1 - CurrentDividedPosition;
                }
                var right = (totalWidth * (p + 1) + division / 2) / division;
                var left = (totalWidth * p + division / 2) / division;
                totalWidth = right - left;
                divisionOffset = left;
            }
            
            var totalHeight = 0;
            for (var i = currentPage; i < nextPage; i++)
            {
                var h = ResizedImageArray[i].Height;
                if (totalHeight < h) totalHeight = h;
            }

            int offsetX;
            if (division == 1 && nextPage > currentPage + 1 || !(currentPage == 0 || nextPage == ResizedImageArray.Length) ||
               !(BindingMode == BindingMode.LeftToRight || BindingMode == BindingMode.RightToLeft) || totalWidth > pbViewSize.Width / 2)
            {
                if (nextPage == currentPage + 2 && ResizedImageArray[currentPage].Width <= pbViewSize.Width / 2 && ResizedImageArray[currentPage + 1].Width <= pbViewSize.Width / 2)
                {
                    offsetX = pbViewSize.Width / 2 - ResizedImageArray[BindingMode == BindingMode.LeftToRight ? currentPage : (currentPage + 1)].Width;
                }
                else
                {
                    offsetX = (pbViewSize.Width - totalWidth) / 2;
                }
            }
            else
            {
                if (BindingMode == BindingMode.LeftToRight ^ currentPage == 0)
                {
                    offsetX = pbViewSize.Width / 2 - totalWidth;
                }
                else
                {
                    offsetX = pbViewSize.Width / 2;
                }
            }
            
            int offsetY = (pbViewSize.Height - totalHeight) / 2;

            var pointScreen = Cursor.Position;
            var pointClient = pbView.PointToClient(pointScreen);
            PointD pointAsLocation;

            //PointD GetMagnifierRectangle_LastRectanglePosition;

            //var size = new Size((int)Math.Round((2 * pbViewSize.Width + MagnifyingPower) / (2 * MagnifyingPower)), (int)Math.Round((2 * pbViewSize.Height + MagnifyingPower) / (2 * MagnifyingPower)));
            var size = GetPreviewModeMagnifierRectangleSize(pbViewSize, magPower);

            var whInt = size.Width / 2;
            var hhInt = size.Height / 2;
            var cursorXMin = offsetX + whInt;
            var corsorXMax = offsetX + totalWidth - size.Width + whInt;
            var corsorYMin = offsetY + hhInt;
            var corsorYMax = offsetY + totalHeight - size.Height + hhInt;

            //if (!PreviewMode && delta == null || PreviewMode && GetMagnifierRectangle_LastCursorPosition == null)
            if (delta == null && GetMagnifierRectangle_LastCursorPosition == null)
            {
                pointAsLocation = pointClient;
                
                var pointTruncated = pbView.PointToScreen(new Point(
                        size.Width < totalWidth ? Math.Max(cursorXMin, Math.Min(corsorXMax, pointClient.X)) : offsetX + totalWidth / 2,
                        size.Height < totalHeight ? Math.Max(corsorYMin, Math.Min(corsorYMax, pointClient.Y)) : offsetY + totalHeight / 2));

                if (pointScreen != pointTruncated)
                {
                    Cursor.Position = pointTruncated;
                }

                //GetMagnifierRectangle_LastRectanglePosition = new PointD(pointTruncated.X - size.Width / 2/0, pointTruncated.Y - size.Height / 2.0);
            }
            else
            {
                touchMode = true;

                var wh = size.Width / 2.0;
                //var whMinus = wh - 0.25;
                var hh = size.Height / 2.0;
                //var hhMinus = hh - 0.25;

                if (GetMagnifierRectangle_LastCursorPosition != null)
                {
                    //var delta = new VectorD(pointClient, (Point)GetMagnifierRectangle_LastCursorPosition);

                    pointAsLocation = (Point)GetMagnifierRectangle_LastCursorPosition;

                    // 差分に不自然さがなければ現在位置を更新
                    //var preDelta = GetMagnifierRectangle_LastCursorDelta;
                    //GetMagnifierRectangle_LastCursorDelta = (VectorD)delta;
                    //var smooth = sharpness(delta, preDelta) < 1;

                    //if (!GetMagnifierRectangle_LastSmooth) delta += preDelta;

                    //if (smooth)
                    {

                        if (delta != null)
                        {
                            // 移動前にも Truncate
                            pointAsLocation.X = Math.Max(cursorXMin, Math.Min(corsorXMax, pointAsLocation.X));
                            pointAsLocation.Y = Math.Max(corsorYMin, Math.Min(corsorYMax, pointAsLocation.Y));

                            var magDelta = (VectorD)delta / magPower;
                            pointAsLocation -= magDelta;

                            //pointAsLocation.X = Math.Max(offsetX + wh, Math.Min(offsetX + totalWidth - wh, pointAsLocation.X));
                            //pointAsLocation.Y = Math.Max(offsetY + hh, Math.Min(offsetY + totalHeight - hh, pointAsLocation.Y));
                        }
                        

                    }

                    //GetMagnifierRectangle_LastSmooth = smooth;

                    GetMagnifierRectangle_LastCursorPosition = new PointD(Math.Max(cursorXMin, Math.Min(corsorXMax, pointAsLocation.X)),
                        Math.Max(corsorYMin, Math.Min(corsorYMax, pointAsLocation.Y))).Round();
                    //GetMagnifierRectangle_LastCursorPosition = pointAsLocation.Round();// pointClient;
                }
                else
                {
                    GetMagnifierRectangle_LastCursorPosition = pointClient;

                    pointAsLocation = BindingMode != BindingMode.RightToLeft ?
                        new PointD(offsetX + wh, offsetY + hh) : new PointD(offsetX + totalWidth - wh, offsetY + hh);
                }

                //pointAsLocation = new PointD(GetMagnifierRectangle_LastRectanglePosition.X + size.Width / 2.0,
                //   GetMagnifierRectangle_LastRectanglePosition.Y + size.Height / 2.0);

            }

            if (PreviewMode)
            {
                var boundX = offsetX + totalWidth - size.Width;
                var boundY = offsetY + totalHeight - size.Height;

                imageRectangle = new Rectangle(offsetX, offsetY, totalWidth, totalHeight);

                return new Rectangle(
                    (int)Math.Round(Math.Max(offsetX, Math.Min(boundX, pointAsLocation.X - whInt/* size.Width / 2.0*/))),
                    (int)Math.Round(Math.Max(offsetY, Math.Min(boundY, pointAsLocation.Y - hhInt/*size.Height / 2.0*/))),
                    Math.Min(size.Width, totalWidth),
                    Math.Min(size.Height, totalHeight));
            }
            else
            {
                imageRectangle = default(Rectangle);
                return new Rectangle(
                    (int)Math.Round(Math.Max(0, Math.Min(totalWidth * magPower - pbViewSize.Width, (pointAsLocation.X - offsetX) * magPower - pbViewSize.Width / 2)
                        ) + divisionOffset * magPower),
                    (int)Math.Round(Math.Max(0, Math.Min(totalHeight * magPower - pbViewSize.Height, (pointAsLocation.Y - offsetY) * magPower - pbViewSize.Height / 2))),
                    (int)Math.Round(Math.Min(pbViewSize.Width, totalWidth * magPower)),
                    (int)Math.Round(Math.Min(pbViewSize.Height, totalHeight * magPower)));
            }
        }

        private bool CursorBounded()
        {
            return (viewerMode == ViewerModeEnum.Magnifier || viewerMode == ViewerModeEnum.MagnifierOpening || viewerMode == ViewerModeEnum.MagnifierClosing) && GetMagnifierRectangle_LastCursorPosition == null;
        }

        private enum ScrollResult { Scrolled, NotMoveAnymore, UnsupportedMode }

        private ScrollResult MagnifierAutoScroll(bool back, bool prioritizeHorizontal, double horizontalOverwrap, double verticalOverwrap)
        {
            if (horizontalOverwrap < 0) throw new ArgumentOutOfRangeException(nameof(horizontalOverwrap));
            if (verticalOverwrap < 0) throw new ArgumentOutOfRangeException(nameof(verticalOverwrap));

            if (ViewerMode != ViewerModeEnum.Magnifier) return ScrollResult.UnsupportedMode;

            if (GetMagnifierRectangle_LastCursorPosition == null)
            {
                GetMagnifierRectangle_LastCursorPosition = pbView.PointToClient(Cursor.Position);
            }

            var pbViewSize = pbView.Size;
            var magRectAsPreview = GetMagnifierRectangle(PreviewMode: true, delta: null, pbViewSize: pbViewSize, touchMode: out var touchMode, imageRectangle: out var imageRect, magPower_: out var magPower_);
            if (magPower_ == null) return ScrollResult.NotMoveAnymore;

            var shift = VectorD.Zero;

            for (var count = 0; count < 2; count++)
            {
                Interval interval;
                bool reverse;
                double overwrap;
                int bound;
                var first = count == 0;
                var vertical = first ^ prioritizeHorizontal;
                if (vertical)
                {
                    interval = Interval.FromVertical(magRectAsPreview);
                    interval.Start -= imageRect.Top;
                    reverse = back;
                    overwrap = verticalOverwrap;
                    bound = imageRect.Height;
                }
                else
                {
                    interval = Interval.FromHorizontal(magRectAsPreview);
                    interval.Start -= imageRect.Left;
                    reverse = BindingMode == BindingMode.RightToLeft ^ back;
                    overwrap = horizontalOverwrap;
                    bound = imageRect.Width;
                }
                var start0 = interval.Start;
                if (reverse) interval.Start = bound - interval.Stop;
                var notMoveAnymore = interval.Stop >= bound;
                if (notMoveAnymore)
                {
                    if (!first) return ScrollResult.NotMoveAnymore;
                    interval.Start = 0;
                }
                else
                {
                    var absOverwrap = overwrap < 1 ? overwrap * interval.Length : overwrap;

                    var shiftLocal = interval.Length - absOverwrap;

                    // interval.Length + n * fitted == bound なる整数 n が存在する shiftLocal 以下の fitted の中で最大のもの
                    var bl = bound - interval.Length;
                    if (bl > 0)
                    {
                        var n = Math.Ceiling(bl / shiftLocal);
                        //optionToolStripMenuItem.Text = n.ToString() + "  " + (xxxx++);
                        var fitted = bl / n;
                        shiftLocal = fitted;
                    }

                    interval.Start += Math.Max(1, (int)Math.Ceiling(shiftLocal));
                }
                if (reverse) interval.Start = bound - interval.Stop;
                var shift_ = interval.Start - start0;
                if (vertical) shift.Y = shift_; else shift.X = shift_;
                if (!notMoveAnymore) break;
            }
            if (shift.IsZero) return ScrollResult.NotMoveAnymore;
            TouchListener_Pan_Delta = -shift * (double)magPower_;
            pbPaintInvalidate();
            return ScrollResult.Scrolled;
        }

        //int xxxx = 0;

        // 16:10 で横が3段階にならないようにするには横のオーバーラップが 2 - 1.25√2 ≒ 0.232233 以下でなければならない
        // 縦を3段階にするには縦のオーバーラップが 0 より大きくなければならない
        // ルーペのオートスクロール（コマンドとして呼ばれる 16 個）。20% は横の重なり 0.2・縦 0.499999、0% は重なりなし。
        // Right / Left は綴じ方向に合わせて Forward / Back に読み替える。MoveXxxOr… はスクロールできなければページを送る
        private ScrollResult MagnifierAutoScrollStep(bool back, bool overwrap20)
        {
            return overwrap20 ?
                MagnifierAutoScroll(back: back, prioritizeHorizontal: false, horizontalOverwrap: 0.2, verticalOverwrap: 0.499999) :
                MagnifierAutoScroll(back: back, prioritizeHorizontal: false, horizontalOverwrap: 0, verticalOverwrap: 0);
        }

        private void MoveOrMagnifierAutoScroll(bool back, bool overwrap20)
        {
            if (MagnifierAutoScrollStep(back, overwrap20) == ScrollResult.Scrolled) return;
            if (back) MoveToPreviousPage();
            else moveToNextPage();
        }

        private bool RightIsForward => BindingMode != BindingMode.RightToLeft;

        private void MagnifierAutoScrollForwardWith20PercentOverwrap() => MagnifierAutoScrollStep(back: false, overwrap20: true);
        private void MagnifierAutoScrollBackWith20PercentOverwrap() => MagnifierAutoScrollStep(back: true, overwrap20: true);
        private void MagnifierAutoScrollForwardWith0PercentOverwrap() => MagnifierAutoScrollStep(back: false, overwrap20: false);
        private void MagnifierAutoScrollBackWith0PercentOverwrap() => MagnifierAutoScrollStep(back: true, overwrap20: false);
        private void MoveForwardOrMagnifierAutoScrollWith20PercentOverwrap() => MoveOrMagnifierAutoScroll(back: false, overwrap20: true);
        private void MoveBackOrMagnifierAutoScrollWith20PercentOverwrap() => MoveOrMagnifierAutoScroll(back: true, overwrap20: true);
        private void MoveForwardOrMagnifierAutoScrollWith0PercentOverwrap() => MoveOrMagnifierAutoScroll(back: false, overwrap20: false);
        private void MoveBackOrMagnifierAutoScrollWith0PercentOverwrap() => MoveOrMagnifierAutoScroll(back: true, overwrap20: false);
        private void MagnifierAutoScrollRightWith20PercentOverwrap() => MagnifierAutoScrollStep(back: !RightIsForward, overwrap20: true);
        private void MagnifierAutoScrollLeftWith20PercentOverwrap() => MagnifierAutoScrollStep(back: RightIsForward, overwrap20: true);
        private void MagnifierAutoScrollRightWith0PercentOverwrap() => MagnifierAutoScrollStep(back: !RightIsForward, overwrap20: false);
        private void MagnifierAutoScrollLeftWith0PercentOverwrap() => MagnifierAutoScrollStep(back: RightIsForward, overwrap20: false);
        private void MoveRightOrMagnifierAutoScrollWith20PercentOverwrap() => MoveOrMagnifierAutoScroll(back: !RightIsForward, overwrap20: true);
        private void MoveLeftOrMagnifierAutoScrollWith20PercentOverwrap() => MoveOrMagnifierAutoScroll(back: RightIsForward, overwrap20: true);
        private void MoveRightOrMagnifierAutoScrollWith0PercentOverwrap() => MoveOrMagnifierAutoScroll(back: !RightIsForward, overwrap20: false);
        private void MoveLeftOrMagnifierAutoScrollWith0PercentOverwrap() => MoveOrMagnifierAutoScroll(back: RightIsForward, overwrap20: false);

        private Size GetPreviewModeMagnifierRectangleSize(Size pbViewSize, double magPower)
        {
            // オートスクロールのオーバーラップ0での動作との兼ね合いで Ceiling
            return new Size((int)Math.Ceiling(pbViewSize.Width / magPower), (int)Math.Ceiling(pbViewSize.Height / magPower));
        }

        public struct PointD
        {
            public double X, Y;
            public PointD(double x, double y) { X = x; Y = y; }
            public static implicit operator PointD(Point point)
            {
                return new PointD(point.X, point.Y);
            }
            public Point Round()
            {
                return new Point(
                    (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(X))),
                    (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(Y))));
            }
        }

        public struct VectorD
        {
            public double X, Y;
            public VectorD(int x, int y) { X = x; Y = y; }
            public VectorD(Point point) { X = point.X; Y = point.Y; }
            public VectorD(Point point, Point origin) { X = point.X - origin.X; Y = point.Y - origin.Y; }

            public override string ToString()
            {
                return $"({X}, {Y})";
            }

            public static VectorD operator *(VectorD vector, double scalar)
            {
                vector.X *= scalar;
                vector.Y *= scalar;
                return vector;
            }

            public static VectorD operator *(double scalar, VectorD vector)
            {
                vector.X *= scalar;
                vector.Y *= scalar;
                return vector;
            }

            public static VectorD operator +(VectorD a, VectorD b)
            {
                a.X += b.X;
                a.Y += b.Y;
                return a;
            }

            public static PointD operator +(PointD a, VectorD b)
            {
                a.X += b.X;
                a.Y += b.Y;
                return a;
            }

            public static VectorD operator -(VectorD a)
            {
                a.X = -a.X;
                a.Y = -a.Y;
                return a;
            }

            public static PointD operator -(PointD a, VectorD b)
            {
                a.X -= b.X;
                a.Y -= b.Y;
                return a;
            }

            public static VectorD operator /(VectorD vector, double scalar)
            {
                vector.X /= scalar;
                vector.Y /= scalar;
                return vector;
            }

            public double Length2 { get { return X * X + Y * Y; } }

            public bool IsZero { get { return X == 0 && Y == 0; } }

            public static readonly VectorD Zero = new VectorD(0, 0);
        }

        private static double sharpness(VectorD delta, VectorD preDelta)
        {
            try
            {
                if (delta.IsZero)
                {
                    return preDelta.IsZero ? 0 : double.PositiveInfinity;
                }
                else if (preDelta.IsZero)
                {
                    return double.PositiveInfinity;
                }

                var del2 = delta.Length2;
                var pre2 = preDelta.Length2;
                if (del2 < pre2)
                {
                    var temp = delta;
                    delta = preDelta;
                    preDelta = temp;
                    pre2 = del2;
                }

                var r = (delta.X * preDelta.X + delta.Y * preDelta.Y) / pre2;
                var i = (delta.X * preDelta.Y - delta.Y * preDelta.X) / pre2;

                //var logR = Math.Log(r * r + i * i) / 2;
                //var logI = Math.Atan2(r, i);

                var logR = r - 1;
                var logI = i;

                return logR * logR + logI * logI;
            }
            catch
            {
                return double.PositiveInfinity;
            }
        }
    }
}
