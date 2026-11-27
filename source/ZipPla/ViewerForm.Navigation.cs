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
        private const double SquareRootOf2 = 1.4142135623730950488016887242096980785696718753769;

        private void pbView_MouseWheel(object sender, MouseEventArgs e)
        {
            if (statusStrip.Visible && statusStrip.ClientRectangle.Contains(statusStrip.PointToClient(pbView.PointToScreen(e.Location))))
            {
                // テキストボックス上でのホイール操作が statusStrip に伝わらないため
                // e の座標を使う場合補正が必要
                aroundSeekBar_MouseWheel(statusStrip, e);
                return;
            }
            if (e.Delta != 0) changeMagnifyingPowerInMagnifierOpeningOrClosing(e.Delta > 0);
        }

        private void changeMagnifyingPowerInMagnifierOpeningOrClosing(bool zoomUp)
        {
            if (UserMagnifierOpeningOrClosing && (ViewerMode == ViewerModeEnum.MagnifierOpening || ViewerMode == ViewerModeEnum.MagnifierClosing))
            {
                var currentPbViewSize = getCurrentPbViewSize();
                var currentImageSize_ = getCurrentVisibleImageSize(currentPbViewSize);
                if (currentImageSize_ == null) return;
                var currentImageSize = (Size)currentImageSize_;

                var p0 = magnifyingPower.GetPower(currentPbViewSize, currentImageSize);
                var mp = Math.Log(p0, SquareRootOf2);
                var p1 = Math.Pow(SquareRootOf2, zoomUp ? mp + 1 : mp - 1);

                const double LBound = 1.1; // 16:10 で √2:1 を拡大するには 4√2 / 5 = 1.131 必要。2^(1/4) =1.189 では不十分

                var currentHFit = MagnifyingInfo.FitHorizontal.GetPower(currentPbViewSize, currentImageSize);
                var currentVFit = MagnifyingInfo.FitVertical.GetPower(currentPbViewSize, currentImageSize);
                const double aff = 1 + 1e-14; //double の計算機イプシロンは 1.220e-16 程度
                if (zoomUp) // 拡大
                {
                    if (currentHFit <= p0) currentHFit = double.PositiveInfinity;
                    if (currentVFit <= p0) currentVFit = double.PositiveInfinity;
                    currentHFit /= aff;
                    currentVFit /= aff;
                    p1 = Math.Min(Math.Min(currentHFit, currentVFit), p1);
                }
                else // 縮小
                {
                    if (currentHFit >= p0) currentHFit = 0;
                    if (currentVFit >= p0) currentVFit = 0;
                    currentHFit *= aff;
                    currentVFit *= aff;
                    p1 = Math.Max(Math.Max(currentHFit, currentVFit), p1);
                }

                if (p1 < LBound) // 
                {
                    MagnifyingPower = MagnifyingInfo.FromValue(LBound);
                }
                else if (p1 > 4)
                {
                    MagnifyingPower = MagnifyingInfo.FromValue(4);
                }
                else
                {
                    if (p1 == currentHFit) MagnifyingPower = MagnifyingInfo.FitHorizontal;
                    else if (p1 == currentVFit) MagnifyingPower = MagnifyingInfo.FitVertical;
                    else
                    {
                        MagnifyingPower = MagnifyingInfo.FromValue(p1);
                    }
                }
            }
        }

        private bool IsResizedForDivide(int page)
        {
            if (page < 0) return false;
            var resizedSizes = ResizedSizeArray;
            if (resizedSizes == null || resizedSizes.Length <= page) return false;
            return resizedSizes[page].Width > getCurrentPbViewSize().Width;
        }

        /// <summary>
        /// n はページ送り回数（≠ページの変化量）、負の数可
        /// </summary>
        private bool movePageNatural(int n)
        {
            // ナチュラル移動のみ
            if (n == 1)
            {
                IfTouchModifyingThenMoveOrigin(inverse: false);
            }
            else if (n == -1)
            {
                IfTouchModifyingThenMoveOrigin(inverse: true);
            }

            // n < -1 が必要な場合別に実装すること
            if (n == -1 && CurrentDividedPosition > 0)
            {
                return moveDividedPage(-1);
            }

            var oldCurrent = currentPage;
            var oldMode = onePageModeForNext;
            var elderPageIsOverwrap = false;
            if (n > 0)
            {
                for (var i = 0; i < n; i++)
                {
                    var nextPage = NextPage;
                    var cPage = currentPage;
                    if (nextPage <= cPage)
                    {
                        if (i == 0 && nextPage == cPage)
                        {
                            var currentPageDivision = getCurrentPageDivison();
                            if (CurrentDividedPosition < currentPageDivision - 1 || CurrentDividedPosition == currentPageDivision - 1 && IsResizedForDivide(cPage))
                            {
                                return moveDividedPage(n, currentPageDivision);
                            }
                        }
                        break;
                    }
                    currentPage = nextPage;
                }
            }
            else
            {
                var nextCurrent = int.MaxValue;
                for (var i = 0; i < -n; i++)
                {
                    nextCurrent = currentPage;
                    var prevPage = PreviousPage;
                    var cPage = currentPage;
                    if (prevPage >= cPage)
                    {
                        if (i == 0 && prevPage == cPage)
                        {
                            if (CurrentDividedPosition > 0 || CurrentDividedPosition == 0 && IsResizedForDivide(cPage - 1))
                            {
                                return moveDividedPage(n);
                            }
                        }
                        break;
                    }
                    currentPage = prevPage;
                    if (currentPage < nextCurrent && nextCurrent < NextPage) elderPageIsOverwrap = true;
                }
            }
            if (currentPage != oldCurrent && currentPage >= 0 && ResizedImageArray != null && currentPage < ResizedImageArray.Length)
            {
                if (elderPageIsOverwrap) onePageModeForNext = true;
                showCurrentPage();
                mtbPage.Value = currentPage;
                return true;
            }
            else
            {
                currentPage = oldCurrent;
                onePageModeForNext = oldMode; // currentPage の変更で false になったものを元に戻す

                return false;
            }
        }

        /// <summary>
        /// n はページ送り回数（≠ページの変化量）、負の数可
        /// </summary>
        private bool isMovablePageNatural(int n, bool checkLoaded = false)
        {
            // n < -1 が必要な場合別に実装すること
            if (n == -1 && CurrentDividedPosition > 0)
            {
                return isMovableDividedPage(-1, checkLoaded);
            }

            var oldCurrent = currentPage;
            var oldMode = onePageModeForNext;
            var nextPage = currentPage;
            if (n > 0)
            {
                for (var i = 0; i < n; i++)
                {
                    var nextPage0 = NextPage;
                    var cPage = currentPage;
                    if (nextPage0 <= cPage)
                    {
                        if (i == 0 && nextPage0 == cPage)
                        {
                            var currentPageDivision = getCurrentPageDivison();
                            if (CurrentDividedPosition < currentPageDivision - 1 || CurrentDividedPosition == currentPageDivision - 1 && IsResizedForDivide(cPage))
                            {
                                return isMovableDividedPage(n, checkLoaded, currentPageDivision);
                            }
                        }
                        break;
                    }
                    currentPage = nextPage0;
                }
            }
            else
            {
                for (var i = 0; i < -n; i++)
                {
                    var prevPage = PreviousPage;
                    var cPage = currentPage;
                    if (prevPage >= cPage)
                    {
                        if (i == 0 && prevPage == cPage)
                        {
                            if (CurrentDividedPosition > 0 || CurrentDividedPosition == 0 && IsResizedForDivide(cPage - 1))
                            {
                                return isMovableDividedPage(n, checkLoaded);
                            }
                        }
                        break;
                    }
                    currentPage = prevPage;
                }
            }
            var result = currentPage != oldCurrent && currentPage >= 0 && ResizedImageArray != null && currentPage < ResizedImageArray.Length;
            if (!checkLoaded || !result)
            {
                currentPage = oldCurrent;
                onePageModeForNext = oldMode; // currentPage の変更で false になったものを元に戻す

                return result;
            }
            else
            {
                var checkStop = NextPage;
                if (checkStop == currentPage) result = false;
                else
                {
                    for (var i = currentPage; i < checkStop; i++)
                    {
                        if (ResizedImageArray[i] == null)
                        {
                            result = false;
                            break;
                        }
                    }
                }
                currentPage = oldCurrent;
                onePageModeForNext = oldMode; // currentPage の変更で false になったものを元に戻す

                return result;
            }
        }

        /// <summary>
        /// 1ページ分戻る。ただし極力左右の位置関係が movePageNatural(-1) とは逆になるようにする
        /// </summary>
        private void movePageToMinimalBack()
        {
            if (CurrentDividedPosition > 0)
            {
                moveDividedPage(-1);
                return;
            }

            var naturalNext = NextPage;
            if(naturalNext <= currentPage + 1)
            {
                if (isMovablePage(-1, checkLoaded: true))
                {
                    moveDividedPage(-1);
                    return;
                }
            }
            else
            {
                var oldCurrent = currentPage;
                var nextPage = currentPage - 1;
                if (nextPage >= -1 && ResizedImageArray != null && Math.Max(nextPage, 0) < ResizedImageArray.Length)
                {
                    if (nextPage == -1)
                    {
                        onePageModeForNext = true;
                    }
                    else
                    {
                        currentPage = nextPage;
                        if (oldCurrent >= NextPage)
                        {
                            currentPage = oldCurrent;
                            onePageModeForNext = true;
                        }
                    }
                    showCurrentPage();
                    mtbPage.Value = currentPage;
                    return;
                }
            }

            moveDividedPage(-1);
        }

        private bool isMovablePageToMinimalBack(bool checkLoaded = false)
        {

            if (isMovableDividedPage(-1, checkLoaded)) return true;
            if (currentPage != 0) return false;
            var naturalNext = NextPage;
            if (naturalNext <= currentPage + 1) return false;
            var nextPage = currentPage - 1;
            if (ResizedImageArray == null || ResizedImageArray.Length == 0) return false;
            
            return !checkLoaded || ResizedImageArray[0] != null;

        }

        /// <summary>
        /// 1ページ分進める。ただし極力左右の位置関係が movePageNatural(1) とは逆になるようにする
        /// </summary>
        private void movePageToMinimalForward()
        {
            if (onePageModeForNext)
            {
                var nextPageInOmePageMode = NextPage;
                if (nextPageInOmePageMode <= currentPage)
                {
                    moveDividedPage(1);
                    return;
                }
                onePageModeForNext = false;
                var nextPageInNormalMode = NextPage;
                if(nextPageInNormalMode > nextPageInOmePageMode)
                {
                    // currentPage は変更せず表示を追加
                    showCurrentPage();
                }
                else
                {
                    movePageToMinimalForward();
                }
                return;
            }
            else
            {
                var naturalNext = NextPage;
                if (naturalNext <= currentPage)
                {
                    moveDividedPage(1);
                    return;
                }
                if (naturalNext > currentPage + 1)
                {
                    if (isMovablePage(1, checkLoaded: true))
                    {
                        movePage(1);
                        return;
                    }
                }
                else
                {
                    var nextPage = currentPage + 1;
                    if (nextPage != currentPage && nextPage >= 0 && ResizedImageArray != null && nextPage < ResizedImageArray.Length)
                    {
                        currentPage = nextPage;
                        onePageModeForNext = true;
                        showCurrentPage();
                        mtbPage.Value = currentPage;
                        return;
                    }
                }
            }
            moveDividedPage(1);
        }

        private bool isMovablePageToMinimalForward(bool checkLoaded = false)
        {
            if (onePageModeForNext)
            {
                var nextPageInOmePageMode = NextPage;
                if (nextPageInOmePageMode <= currentPage) return isMovableDividedPage(1, checkLoaded);
                onePageModeForNext = false;
                var nextPageInNormalMode = NextPage;
                if (nextPageInNormalMode > nextPageInOmePageMode)
                {
                    onePageModeForNext = true;
                    if (!checkLoaded)
                    {
                        return true;
                    }
                    else
                    {
                        for(var i = nextPageInOmePageMode; i < nextPageInNormalMode; i++)
                        {
                            if (!(0 <= i && ResizedImageArray != null &&
                                i < ResizedImageArray.Length && ResizedImageArray[i] != null))
                                return isMovableDividedPage(1, checkLoaded);
                        }
                        return true;
                    }
                }
                else
                {
                    var result = isMovablePageToMinimalForward(checkLoaded);
                    onePageModeForNext = true;
                    return result || isMovableDividedPage(1, checkLoaded);
                }
            }
            else
            {
                return isMovablePage(1, checkLoaded) || isMovableDividedPage(1, checkLoaded);
            }
        }

        private bool moveDividedPage(int n, int? currentPageDivision = null)
        {
            if (Math.Abs(n) == 1)
            {
                if (MaxDivision == 1)
                {
                    if (isMovablePage(n))
                    {
                        movePage(n);
                        return true;
                    }
                    else return false;
                }
                if (n == -1)
                {
                    var division = currentPageDivision ?? getCurrentPageDivison();
                    if (division < 1) return false;
                    if (Math.Min(CurrentDividedPosition, division - 1) == 0)
                    {
                        var prevDivision = getPageDivison(currentPage - 1);
                        if (prevDivision > 0)
                        {
                            if (isMovablePage(-1, checkLoaded: true))
                            {
                                movePage(-1, prevDivision - 1);
                                return true;
                            }
                        }
                    }
                    else
                    {
                        if (currentPage >= 0 && ResizedImageArray != null &&
                            currentPage < ResizedImageArray.Length && ResizedImageArray[currentPage] != null)
                        {
                            CurrentDividedPosition--;
                            showCurrentPage();
                            return true;
                        }
                    }
                }
                else
                {
                    var division = currentPageDivision ?? getCurrentPageDivison();
                    if (CurrentDividedPosition >= division - 1)
                    {
                        if (isMovablePage(1, checkLoaded: true))
                        {
                            movePage(1, 0);
                            return true;
                        }
                    }
                    else
                    {
                        if (currentPage >= 0 && ResizedImageArray != null &&
                            currentPage < ResizedImageArray.Length && ResizedImageArray[currentPage] != null)
                        {
                            CurrentDividedPosition++;
                            showCurrentPage();
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool isMovableDividedPage(int n, bool checkLoaded, int? currentPageDivision = null)
        {
            if (Math.Abs(n) == 1)
            {
                if (MaxDivision == 1) return isMovablePage(n, checkLoaded);

                if (n == -1)
                {
                    var division = currentPageDivision ?? getCurrentPageDivison();
                    if (division < 1) return false;

                    if (Math.Min(CurrentDividedPosition, division - 1) == 0)
                    {
                        var prevDivision = getPageDivison(currentPage - 1);
                        if (prevDivision > 0)
                        {
                            return isMovablePage(-1);
                        }
                    }
                    else
                    {
                        return !checkLoaded || currentPage >= 0 && ResizedImageArray != null &&
                            currentPage < ResizedImageArray.Length && ResizedImageArray[currentPage] != null;
                    }
                }
                else
                {
                    var division = currentPageDivision ?? getCurrentPageDivison();
                    if (CurrentDividedPosition >= division - 1)
                    {
                        return isMovablePage(1);
                    }
                    else
                    {
                        return !checkLoaded || currentPage >= 0 && ResizedImageArray != null &&
                            currentPage < ResizedImageArray.Length && ResizedImageArray[currentPage] != null;
                    }
                }
            }
            return false;
        }

        private int getCurrentPageDivison()
        {
            return getPageDivison(currentPage);
        }

        private int getPageDivison(int page)
        {
            if (page >= 0 && ResizedSizeArray != null && page < ResizedSizeArray.Length)
            {
                var w = Math.Abs(ResizedSizeArray[page].Width);
                if (w <= 0) return 0;
                var result = (w - 1) / pbView.Width + 1;
                if (result > MaxDivision) return 0;
                return result;
            }
            return 0;
        }

        /// <summary>
        /// n は単純なページの変化量、負の数可
        /// </summary>
        private void movePage(int n, int dividedPosition = -1)
        {
            var nextPage = currentPage + n;
            if (nextPage != currentPage && nextPage >= 0 && ResizedImageArray != null && nextPage < ResizedImageArray.Length)
            {
                currentPage = nextPage;
                if (dividedPosition >= 0 && dividedPosition < getCurrentPageDivison())
                {
                    CurrentDividedPosition = dividedPosition;
                }
                showCurrentPage();
                mtbPage.Value = currentPage;
            }
        }

        /// <summary>
        /// n は単純なページの変化量、負の数可
        /// </summary>
        private bool isMovablePage(int n, bool checkLoaded = false)
        {
            var nextPage = currentPage + n;
            var result = nextPage != currentPage && nextPage >= 0 && ResizedImageArray != null && nextPage < ResizedImageArray.Length;
            if (!checkLoaded || !result)
            {
                return result;
            }
            else
            {
                for (var i = currentPage; i < nextPage; i++)
                {
                    if (ResizedImageArray[i] == null) return false;
                }
                return true;
            }
        }

        private bool SetCurrentImageLongPathRequired = false;

        private string shownImegesPath = null;

        private string requestedImagePath = null;

        private int requestedImagePage;

        private BindingMode? requestedBindingMode = null;

        private bool? requestedIgnoreSavedFilerSetting = null;

        private SortModeDetails requestedSortModeDetails;

        private string requestedFilterString;

        private string requestedFilterStringWithoutAlias;

        private bool ExitAfterCoverSetting = false;

        private int NextPage
        {
            get
            {
                // 読み込みジョブ（ワーカースレッド）からも呼ばれる（CanDecodeReduced）。Reload の配列のクリアと重なっても
                // 例外にならないよう、配列と currentPage は一度だけ読む（TD-7）
                var resizedImages = ResizedImageArray;
                var resizedSizes = ResizedSizeArray;
                var cPage = currentPage;
                if (resizedImages == null || resizedSizes == null)
                {
                    return 0;
                }
                if (cPage < 0) return cPage;

                var totalWidth = 0;
                var stopPage = cPage;
                var MaxPageCountInWindowForNext = onePageModeForNext ? 1 : MaxPageCountInWindow;
                var screenWidth = null as int?;
                for (var i = cPage; i < cPage + MaxPageCountInWindowForNext && i < resizedImages.Length && i < resizedSizes.Length; i++)
                {
                    var img = resizedImages[i];
                    if (img == null)
                    {
                        var imgSize = resizedSizes[i];
                        if (imgSize.IsEmpty)
                        {
                            return cPage;
                        }
                        totalWidth += imgSize.Width;
                    }
                    else
                    {
                        totalWidth += img.Width;
                    }
                    stopPage++;
                    var pbViewWidth = FullScreen_pseudoFullScreen ? screenWidth ?? (int)(screenWidth = GetScreenBoundsThreadSafe().Width) :
                        PseudoMaximized ? PseudoMaximizedSize.Width : ViewerForm_SizeChanged_pbBoundsMemoryForMinimize.Width; // pbView.Width;
                    if (totalWidth >= pbViewWidth)
                    {
                        if (totalWidth != pbViewWidth)
                        {
                            stopPage--;
                            break; // img == null でも stopPage を返す唯一のパターン
                        }
                        if (img == null) return cPage;
                        break;
                    }
                    if (img == null) return cPage;
                }
                return stopPage;
            }
        }

        // ワーカースレッドからはウィンドウハンドルに触らない（Screen.FromControl は Handle を使う。TD-7）。Bounds はフィールドの読み取りだけ
        private Rectangle GetScreenBoundsThreadSafe()
        {
            return (InvokeRequired ? Screen.FromRectangle(Bounds) : Screen.FromControl(this)).Bounds;
        }

        private int PreviousPage
        {
            get
            {
                if (ResizedImageArray == null)
                {
                    return -1;
                }
                var totalWidth = 0;
                var stopPage = currentPage;
                var screenWidth = null as int?;
                for (var i = currentPage - 1; i >= currentPage - MaxPageCountInWindow && i >= 0; i--)
                {
                    var img = ResizedImageArray[i];
                    if (img == null)
                    {
                        var imgSize = ResizedSizeArray[i];
                        if (imgSize.IsEmpty)
                        {
                            if (MaxPageCountInWindow > 1)
                            {
                                return currentPage;
                            }
                            else
                            {
                                stopPage--;
                                break;
                            }
                        }
                        totalWidth += imgSize.Width;
                    }
                    else
                    {
                        totalWidth += img.Width;
                    }
                    stopPage--;
                    var pbViewWidth = FullScreen_pseudoFullScreen ? screenWidth ?? (int)(screenWidth = Screen.FromControl(this).Bounds.Width) :
                        PseudoMaximized ? PseudoMaximizedSize.Width : ViewerForm_SizeChanged_pbBoundsMemoryForMinimize.Width;
                    if (totalWidth >= pbViewWidth)
                    {
                        if (totalWidth != pbViewWidth)
                        {
                            stopPage++;
                            break; // img == null でも stopPage を返す唯一のパターン
                        }
                        if (img == null) return currentPage;
                        break;
                    }
                    if (img == null) return currentPage;
                }
                return stopPage;
            }
        }

        bool mtbPage_ValueChanged_SendRequestToChangeSelection_Stop = false;

        private void mtbPage_ValueChanged(object sender, EventArgs e)
        {
            if (mtbPage.Value != currentPage)
            {
                currentPage = mtbPage.Value;
                if (!mtbPage_ValueChanged_SendRequestToChangeSelection_Stop) showCurrentPage();
            }

            if (!mtbPage_ValueChanged_SendRequestToChangeSelection_Stop)
            {
                if (SeekBarDragging) mtbPage_DragDeferred = true; // カタログへの選択の依頼はドラッグを終えたときに 1 回だけ（V24）
                else SendRequestToChangeSelectionForCurrentPage();
            }
            
            if (ipcLookAheadInfo == null) SetCurrentImageLongPath(); // ページ番号の描画よりも後で呼び出す
        }

        private void SendRequestToChangeSelectionForCurrentPage()
        {
            {
                var elp = EntryLongPathArray;
                var ld = loader;
                if (elp != null && currentPage >= 0 && currentPage < elp.Length && ld != null)
                {
                    var path = elp[currentPage];
                    if (ld.Type == PackedImageLoader.PackType.Directory)
                    {
                        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    }

                    if (!string.IsNullOrEmpty(path))
                    {
                        SendRequestToChangeSelection(path);
                    }
                }
            }
        }

        // シークバーをドラッグしている間は、1 ページ変わるたびの先読みの組み直し・原寸の読み直しと破棄・
        // カタログへの選択の依頼をやめ、離したときに 1 回だけ行う（V24）。ドラッグ中の先読みは、トラックバーの上のカーソルに合わせる
        // 処理（mtbPage_MouseMove、50ms に 1 回まで）が組み直す
        private bool mtbPage_Dragging = false;

        private bool mtbPage_DragDeferred = false;

        // マウスを捕まえている間だけドラッグ中とみなす。離したことが届かなかった場合も、後回しにした処理を必ず行う
        private bool SeekBarDragging
        {
            get
            {
                if (!mtbPage_Dragging) return false;
                if (mtbPage.Capture) return true;
                BeginInvoke((MethodInvoker)EndSeekBarDrag);
                return false;
            }
        }

        private void EndSeekBarDrag()
        {
            if (!mtbPage_Dragging) return;
            mtbPage_Dragging = false;
            if (!mtbPage_DragDeferred) return;
            mtbPage_DragDeferred = false;
            if (ResizedImageArray == null) return;
            prefetchOrderPage = int.MinValue; // 先読みの範囲を必ず作り直させる
            showCurrentPage();
            if (!mtbPage_ValueChanged_SendRequestToChangeSelection_Stop) SendRequestToChangeSelectionForCurrentPage();
        }

        private void ChangePageDirection(BindingMode binding)
        {
            if(BindingMode != binding)
            {
                var sizeChanged = getNeedToResizeImage(binding, bindingModeField);
                ChangeBindingModeCore(binding, redraw: !sizeChanged);
                if (sizeChanged) Reload(force: true);
                ShowViewSettingHint();
            }
            
        }

        private bool getNeedToResizeImage(BindingMode a, BindingMode b)
        {
            var aIsSpread = a == BindingMode.LeftToRight || a == BindingMode.RightToLeft;
            var bIsSpread = b == BindingMode.LeftToRight || b == BindingMode.RightToLeft;
            if (aIsSpread && bIsSpread) return false;
            if (MaxDivision > 1 || MinPageCountInWindow > 1) return true;
            return !((aIsSpread || a == BindingMode.SinglePage) && (bIsSpread || b == BindingMode.SinglePage));
        }

        private void rightToLeftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ChangePageDirection(BindingMode.RightToLeft);
        }

        private void leftToRightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ChangePageDirection(BindingMode.LeftToRight);
        }

        private void singlePageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ChangePageDirection(BindingMode.SinglePage);
        }

        private void singlePageWithoutScalingUpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ChangePageDirection(BindingMode.SinglePageWithoutScalingUp);
        }

        private void forceTwoPageSpreadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleMinPageCountInWindow();
        }

        private void ToggleMinPageCountInWindow()
        {
            MinPageCountInWindow = 3 - MinPageCountInWindow;
            //if (MinPageCountInWindow > 1) MaxDivision = 1; // 強制見開きページの場合必要、見開き優先なら不要
            if (BindingMode == BindingMode.SinglePage || BindingMode == BindingMode.SinglePageWithoutScalingUp)
            {
                showCurrentPage();
            }
            else
            {
                Reload(force: true);
            }
        }

        private void allowPageDivisionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleMaxDivision();
        }

        private void ToggleMaxDivision()
        {
            MaxDivision = 3 - MaxDivision;
            //if (MaxDivision > 1) MinPageCountInWindow = 1; // 強制見開きページの場合必要、見開き優先なら不要
            if (BindingMode == BindingMode.SinglePage || BindingMode == BindingMode.SinglePageWithoutScalingUp)
            {
                showCurrentPage();
            }
            else
            {
                Reload(force: true);
            }
        }

        private void nextPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            moveToNextPage();
        }

        private void moveToNextPage()
        {
            if (ResizedImageArray == null) return;
            var count = ResizedImageArray.Length;
            if (count == 0) return;
            var nextPage = NextPage;
            if (nextPage < count && (nextPage > currentPage || currentPage == nextPage && CurrentDividedPosition >= 0 && MaxDivision > 1))
            {
                movePageNatural(1);
            }
            else if (nextPage == count && CurrentDividedPosition + 1 == getCurrentPageDivison())
            {
                ksViewForm?.InvokeCommand(ExtendedKeys.NextPageAtLastPage);
            }
        }

        private void previousPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MoveToPreviousPage();
        }

        private void MoveToPreviousPage()
        {
            if (ResizedImageArray == null) return;
            var count = ResizedImageArray.Length;
            if (count == 0) return;
            if (currentPage > 0 || currentPage == 0 && CurrentDividedPosition > 0 && MaxDivision > 1)
            {
                movePageNatural(-1);
            }
            else if (currentPage == 0 && CurrentDividedPosition == 0)
            {
                ksViewForm?.InvokeCommand(ExtendedKeys.PreviousPageAtFirstPage);
            }
        }

        private void rightPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            moveToRightPage();
        }

        private void moveToRightPage()
        {
            if (BindingMode != BindingMode.RightToLeft)
            {
                moveToNextPage();
            }
            else
            {
                MoveToPreviousPage();
            }
        }

        private void leftPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            moveToLeftPage();
        }

        private void moveToLeftPage()
        {
            if(BindingMode != BindingMode.RightToLeft)
            {
                MoveToPreviousPage();
            }
            else
            {
                moveToNextPage();
            }
            
        }

        private void moveForward1PageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            movePageToMinimalForward();
        }

        private void moveBack1PageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            movePageToMinimalBack();
        }

        private void moveRight1PageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            movePageToMinimalRight();
        }

        private void movePageToMinimalRight()
        {
            if (BindingMode != BindingMode.RightToLeft) movePageToMinimalForward(); else movePageToMinimalBack();
        }

        private void moveLeft1PageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            movePageToMinimalLeft();
        }

        private void movePageToMinimalLeft()
        {
            if (BindingMode != BindingMode.RightToLeft) movePageToMinimalBack(); else movePageToMinimalForward();
        }

        private void percentage000ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.0); }

        private void percentage010ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.1); }

        private void percentage020ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.2); }

        private void percentage030ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.3); }

        private void percentage040ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.4); }

        private void percentage050ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.5); }

        private void percentage060ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.6); }

        private void percentage070ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.7); }

        private void percentage080ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.8); }

        private void percentage090ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.9); }

        private void percentage100ToolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(1.0); }

        private double getRatioByPage(int p = -1)
        {
            if (p < 0) p = currentPage;
            if (ResizedImageArray == null) return 0;
            var pageCount = ResizedImageArray.Length;
            return Math.Max(0, Math.Min(1, (double)p / pageCount));
        }

        private int getPageByRatio(double r)
        {
            if (ResizedImageArray == null) return -1;
            var pageCount = ResizedImageArray.Length;
            return Math.Max(0, Math.Min(pageCount - 1, (int)(r * pageCount)));
        }

        private void movePageByRatio000() { movePageByRatio(0.0); }

        private void movePageByRatio010() { movePageByRatio(0.1); }

        private void movePageByRatio020() { movePageByRatio(0.2); }

        private void movePageByRatio030() { movePageByRatio(0.3); }

        private void movePageByRatio040() { movePageByRatio(0.4); }

        private void movePageByRatio050() { movePageByRatio(0.5); }

        private void movePageByRatio060() { movePageByRatio(0.6); }

        private void movePageByRatio070() { movePageByRatio(0.7); }

        private void movePageByRatio080() { movePageByRatio(0.8); }

        private void movePageByRatio090() { movePageByRatio(0.9); }

        private void movePageByRatio100() { movePageByRatio(1.0); }

        private void movePageByRatio(double r)
        {
            var nextPage = getPageByRatio(r);
            if (nextPage < 0) return;
            currentPage = nextPage;
            if (nextPage == 0)
            {
                // seemToBook は廃止
                onePageModeForNext = CoverBindingMode == CoverBindingMode.ForceSingle;
            }
            showCurrentPage();
            mtbPage.Value = currentPage;
        }

        private bool isMovablePageByRatio(double r)
        {
            var nextPage = getPageByRatio(r);
            return nextPage >= 0 && nextPage != currentPage;
        }
    }
}
