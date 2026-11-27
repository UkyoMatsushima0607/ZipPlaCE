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
                //var mpInt = (int)Math.Round(Math.Log(p0, SquareRootOf2));
                var mp = Math.Log(p0, SquareRootOf2);
                var p1 = Math.Pow(SquareRootOf2, zoomUp ? mp + 1 : mp - 1);

                const double LBound = 1.1; // 16:10 で √2:1 を拡大するには 4√2 / 5 = 1.131 必要。2^(1/4) =1.189 では不十分

                var currentHFit = MagnifyingInfo.FitHorizontal.GetPower(currentPbViewSize, currentImageSize);
                var currentVFit = MagnifyingInfo.FitVertical.GetPower(currentPbViewSize, currentImageSize);
                //double p2;
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
                //currentPage = nextPage;
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
                //return moveDividedPage(n);
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

                //return result || isMovableDividedPage(n, checkLoaded);
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
                //MessageBox.Show($"{currentPage}, {checkStop}, {result}");
                currentPage = oldCurrent;
                onePageModeForNext = oldMode; // currentPage の変更で false になったものを元に戻す

                //return result || isMovableDividedPage(n, checkLoaded);
                return result;
            }
        }

        /// <summary>
        /// 1ページ分戻る。ただし極力左右の位置関係が movePageNatural(-1) とは逆になるようにする
        /// </summary>
        private void movePageToMinimulBack()
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
                    //movePage(-1);
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

        private bool isMovablePageToMinimulBack(bool checkLoaded = false)
        {
            //return isMovablePage(-1, checkLoaded;
            //return isMovableDividedPage(-1, checkLoaded);

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
        private void movePageToMinimulForward()
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
                    movePageToMinimulForward();
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

        private bool isMovablePageToMinimulForward(bool checkLoaded = false)
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
                    var result = isMovablePageToMinimulForward(checkLoaded);
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

        //private bool ShowIconRequired = false;
        private bool SetCurrentImageLongPathRequired = false;

        private string shownImegesPath = null;

        private string requestedImagePath = null;

        private int requestedImagePage;

        //private Color requestedImageColor;
        //private ArchivesInArchiveMode? requestedArchivesInArchiveMode = null;
        private BindingMode? requestedBindingMode = null;

        //private CoverBindingMode? requestedCoverBindingMode = null;
        private bool? requestedIgnoreSavedFilerSetting = null;

        private SortModeDetails requestedSortModeDetails;

        private string requestedFilterString;

        private string requestedFilterStringWithoutAlias;

        private bool ExitAfterCoverSetting = false;
    }
}
