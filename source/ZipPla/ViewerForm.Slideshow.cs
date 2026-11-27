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
        private void btnOpenRight_Click(object sender, EventArgs e)
        {
            if (mtbPage.ValueCanChangedByMouseButton)
            {
                if (mtbPage.InversedSlider) OpenPrevious();
                else OpenNext();
            }
            else mtbPage.ValueCanChangedByMouseButton = true;
        }

        private void setSlideshow(int intervalMilliseconds)
        {
            if (intervalMilliseconds <= 0)
            {
                if (tmSlideshow_LoadWaiting)
                {
                    tmSlideshow_StopReserved = true;
                }
                else
                {
                    tmSlideshow_StopReserved = false;
                    tmSlideshow.Stop();
                }
            }
            else
            {
                tmSlideshow_LoadWaiting = false;
                tmSlideshow_StopReserved = false;
                tmSlideshow.Interval = intervalMilliseconds;
                tmSlideshow.Start();
            }
        }

        private bool tmSlideshow_LoadWaiting = false;

        private bool tmSlideshow_StopReserved = false;

        private void tmSlideshow_Tick(object sender, EventArgs e)
        {
            if (!ViewerForm_Shown_Called) return;

            if (PreFilteredImageArray == null || PreFilteredImageArray.Length <= 0)
            {
                //setSlideshow(0);
                slideshowStop();
                return;
            }
            Func<bool> isMovable;
            Action move;
            if (NextPage >= PreFilteredImageArray.Length)
            {
                if(SlideshowGlobal)
                {
                    if (SlideshowRepeat)
                    {
                        OpenNextWithLoop(notFoundAction: slideshowStop);
                    }
                    else
                    {
                        OpenNext(notFoundAction: slideshowStop);
                    }
                }
                else
                {
                    if (SlideshowRepeat)
                    {
                        // 読み込みの表示が出る可能性があるがシークバーを動かさないと読み込みが始まらない
                        // 修正するなら、先読みの優先順位を決める距離の定義を変えた上で
                        // isMovablePageNatural と movePageNatural に Loop のオーバーロードを加えて状況に応じてそれを呼び出し
                        // （ただし単純に先読みの優先順位を変えると初めから開いたときに無駄に後ろの方を読み込むことになるので
                        // 　スライドショーが動いているときのみスライドショーの方向に合わせて最小限の変更をするべき）
                        mtbPage.Value = 0;
                    }
                    else
                    {
                        //setSlideshow(0);
                        slideshowStop();
                    }
                }
                return;
            }
            if (SlideshowMove1Page)
            {
                isMovable = () => isMovablePageToMinimalForward(checkLoaded: true);
                move = movePageToMinimalForward;
            }
            else
            {
                isMovable = () => isMovablePageNatural(1, checkLoaded: true);
                move = () => movePageNatural(1);
            }

            if (isMovable())
            {
                move();
            }
            else
            {
                tmSlideshow_LoadWaiting = true;
                tmSlideshow.Stop();
                GenerarClasses.BackgroundMultiWorker.EachRunWorkerCompletedEventHandler eh = null;
                eh = (sender2, e2) =>
                {
                    if (tmSlideshow_StopReserved)
                    {
                        tmSlideshow_LoadWaiting = false;
                        tmSlideshow_StopReserved = false;
                    }
                    else
                    {
                        slideshowStart();
                        tmSlideshow_Tick(sender, e);
                    }
                    bmwLoadEachPage.EachRunWorkerCompleted -= eh;
                };
                bmwLoadEachPage.EachRunWorkerCompleted += eh;
            }
        }

        private void _1SecIntervalsToolStripMenuItem_Click(object sender, EventArgs e) { tmSlideshow.Interval = 1 * 1000; }

        private void _2SecIntervalsToolStripMenuItem_Click(object sender, EventArgs e) { tmSlideshow.Interval = 2 * 1000; }

        private void _3SecIntervalsToolStripMenuItem_Click(object sender, EventArgs e) { tmSlideshow.Interval = 3 * 1000; }

        private void _5SecIntervalsToolStripMenuItem_Click(object sender, EventArgs e) { tmSlideshow.Interval = 5 * 1000; }

        private void _10SecIntervalsToolStripMenuItem_Click(object sender, EventArgs e) { tmSlideshow.Interval = 10 * 1000; }

        private void _20SecIntervalsToolStripMenuItem_Click(object sender, EventArgs e) { tmSlideshow.Interval = 20 * 1000; }

        private void _30SecIntervalsToolStripMenuItem_Click(object sender, EventArgs e) { tmSlideshow.Interval = 30 * 1000; }

        private void _60SecIntervalsToolStripMenuItem_Click(object sender, EventArgs e) { tmSlideshow.Interval = 60 * 1000; }

        private void startSlideshowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            slideshowStart();
        }

        private void slideshowStart()
        {
            slideshowStart(force: false);
        }

        private void slideshowStart(bool force)
        {
            if (ExitAfterCoverSetting) return;

            var opend = PreFilteredImageArray != null;
            if (opend && (force || !getSlideshowEnabled()))
            {
                setSlideshow(tmSlideshow.Interval);
            }
            setTitleBar();
        }

        private void stopSlideshowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            slideshowStop();
        }

        private void slideshowStop()
        {
            if (ExitAfterCoverSetting) return;
            setSlideshow(0);
            setTitleBar();
        }

        private void toggleSlideshowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            slideshowToggle();
        }

        private void slideshowToggle()
        {
            if (ExitAfterCoverSetting) return;
            if (!getSlideshowEnabled()) slideshowStart(force: true);
            else slideshowStop();
        }

        private void repeatToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SlideshowRepeat = !SlideshowRepeat;
        }

        private bool getSlideshowEnabled()
        {
            return !tmSlideshow_StopReserved && (tmSlideshow_LoadWaiting || tmSlideshow.Enabled);
        }

        private void openNextOnTerminalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SlideshowGlobal = !SlideshowGlobal;
        }

        private void closeWindowtoolStripMenuItem_Paint(object sender, PaintEventArgs e)
        {
            if (closeWindowtoolStripMenuItem.Text != null)
            {
                closeWindowtoolStripMenuItem.AutoSize = false;
                closeWindowtoolStripMenuItem.Text = null;
                closeWindowtoolStripMenuItem.Width = closeWindowtoolStripMenuItem.Height;
            }
            ControlPainter.FillCross(sender as ToolStripItem, e);
        }

        private void rotateLeftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            toggleRotateLeft();
        }

        private void toggleRotateLeft()
        {
            if (ViewerMode != ViewerModeEnum.CoverSetting)
            {
                imageFilter.RotationAngle = imageFilter.RotationAngle == 270 ? 0 : 270;
                reloadForChangeFilter(forOnlyPostFilter: false);
            }
        }

        private void rotateRightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            toggleRotateRight();
        }

        private void toggleRotateRight()
        {
            if (ViewerMode != ViewerModeEnum.CoverSetting)
            {
                imageFilter.RotationAngle = imageFilter.RotationAngle == 90 ? 0 : 90;
                reloadForChangeFilter(forOnlyPostFilter: false);
            }
        }

        private void cancelRotationSettingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cancelRotationSetting();
        }

        private void cancelRotationSetting()
        {
            if (ViewerMode != ViewerModeEnum.CoverSetting && imageFilter.RotationAngle != 0)
            {
                imageFilter.RotationAngle = 0;
                reloadForChangeFilter(forOnlyPostFilter: false);
            }
        }

        private void pseudoColoringToolStripMenuItem_Click(object sender, EventArgs e)
        {
            togglePseudoColoring();
        }

        private void togglePseudoColoring()
        {
            if (ViewerMode != ViewerModeEnum.CoverSetting)
            {
                imageFilter.ToneCurveEnabled = !imageFilter.ToneCurveEnabled;
                reloadForChangeFilter(forOnlyPostFilter: true);
            }
        }
    }
}
