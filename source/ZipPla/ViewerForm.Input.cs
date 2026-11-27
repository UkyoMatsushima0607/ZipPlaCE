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
        private VectorD? TouchListener_Pan_Delta = null;

        private bool TouchListener_Pan_HoldTrackBar = false;

        private void TouchListener_Pan(object sender, PanEventArgs e)
        {
            CloseAllMenu();
            if (gestureListener_Rotate_Degrees != null)
            {
                TouchListener_Pan_Delta = null;
                mgView.Clear();

                TouchListener_Pan_HoldTrackBar = false;

                return;
            }

            Point clientPoint;
            if (e.Begin && !TouchListener_Pan_HoldTrackBar && !mtbPage.Visible)
            {
                TouchListener_Pan_HoldTrackBar = showUnderUI(pbView.PointToClient(e.Location));
                if (TouchListener_Pan_HoldTrackBar) Cursor.Position = e.Location;
            }
            if (!e.End && !e.Inertia && mtbPage.Visible && (
                mtbPage.ClientRectangle.Contains(clientPoint = mtbPage.PointToClient(e.Location)) || TouchListener_Pan_HoldTrackBar))
            {
                mtbPage.Value = mtbPage.PointToValue(clientPoint);
                TouchListener_Pan_HoldTrackBar = true;
                return;
            }
            TouchListener_Pan_HoldTrackBar = false;

            switch (ViewerMode)
            {
                case ViewerModeEnum.Normal:
                    {
                        TouchListener_Pan_Delta = null;
                        var points = e.Info.location;
                        var point = pbView.PointToClient(new Point(points.x, points.y));
                        var contain = pbView.ClientRectangle.Contains(point);
                        
                        if (contain && e.Begin) mgView.GestureBegin(point);
                        else if (contain && !e.End && !e.Inertia) mgView.GestureContinue(point);
                        else mgView.GestureEnd(point, inNativeThread: true);
                    }
                    break;
                case ViewerModeEnum.MagnifierOpening:
                    TouchListener_Pan_Delta = null;
                    mgView.Clear();
                    break;
                case ViewerModeEnum.Magnifier:
                    mgView.Clear();
                    if (TouchListener_Pan_Delta == null)
                    {
                        TouchListener_Pan_Delta = new VectorD(e.PanOffset);
                    }
                    else
                    {
                        TouchListener_Pan_Delta += new VectorD(e.PanOffset);
                    }
                    pbPaintInvalidate();
                    //optionToolStripMenuItem.Text = $"{e.PanOffset.X} {e.PanOffset.Y}";
                    break;
                case ViewerModeEnum.MagnifierClosing:
                    TouchListener_Pan_Delta = null;
                    mgView.Clear();
                    break;
                case ViewerModeEnum.CoverSetting:
                    {
                        TouchListener_Pan_Delta = null;
                        mgView.Clear();
                        var points = e.Info.location;
                        var point = pbView.PointToClient(new Point(points.x, points.y));
                        pbView_Paint_Rectangle = GetCoverRectangleForDisplay(point);
                        pbPaintInvalidate();
                        if (e.End)
                        {
                            // TouchListener_Pan を抜けるまでタッチからクリックへの変換が行われない
                            Task.Run(() =>
                            {
                                try
                                {
                                    Invoke((MethodInvoker)(() =>
                                    {
                                        SetCoverForThumbnail(clientPoint: point);
                                    }));
                                }
                                catch (ObjectDisposedException) { }
                            });
                        }
                    }
                    break;
            }


            e.Handled = true;
        }

        private void ksViewForm_WheelAccepted(object sender, MouseAcceptedCancelEventArgs e)
        {
            // テキストボックス上でのホイール操作が statusStrip に伝わらないため
            e.Cancel = statusStrip.Visible && statusStrip.ClientRectangle.Contains(statusStrip.PointToClient(e.ScreenPoint));
        }

        private void ksViewForm_KeyboardShortcutStarting(KeyboardShortcut sender, KeyboardShortcutStartingEventArgs e)
        {
            if (e.ByProgram) return;
            if (e.StartingKey == Keys.RButton)
            {
                if (mgView != null && mgView.Enabled)
                {
                    e.Cancel = true;
                }
            }
            else if (ksViewForm != null && ksViewForm.Enabled && mgView.InGesturing && !mgView.InGesturing2)
            {
                mgView.Clear();
                e.InitialState = new Keys[1] { Keys.RButton };
            }
        }

        private void mgView_MouseGestureCompleted(MouseGesture sender, MouseGestureCompletedEventArgs e)
        {
            if (e.UserDirections.Length == 0 && !MouseDownForCoverSettingExit)
            {
                ksViewForm.InvokeMouseClick(e.Button);
            }
            MouseDownForCoverSettingExit = false;
        }

        private void mgView_MouseGestureStarting(MouseGesture sender, MouseGestureStartingEventArgs e)
        {
            if (ksViewForm != null && ksViewForm.InInput)
            {
                e.Cancel = true;
                //MessageBox.Show($"{ksViewForm.KeySequence.Count} {string.Join(", ", ksViewForm.KeySequence[0])}");
            }
        }

        private void aroundSeekBar_MouseWheel(object sender, MouseEventArgs e)
        {
            // pbView_MouseWheel 経由で呼ばれた場合座標が異なるので注意

            var delta = e.Delta / 120;
            if (delta == -1)
            {
                movePageToMinimalForward();
            }
            else if (delta == 1)
            {
                movePageToMinimalBack();
            }
            else
            {
                mtbPage.Value = Math.Max(mtbPage.Minimum, Math.Min(mtbPage.Maximum, mtbPage.Value - delta));
            }
        }

        MessageForwarder msView, msStatusStrip, msPage;

        ToolStripSpringTextBox currentImageLongPathToolStripTextBox = new ToolStripSpringTextBox();

        bool ViewerForm_Load_Called = false;

        //bool ViewerForm_Load_CallTick = false;
        private void ViewerForm_Load(object sender, EventArgs e)
        {

            var font = pageToolStripStatusLabel.Font; // Dispose してはならない
            Func<string, int> measure = text => TextRenderer.MeasureText(text, font).Width; // font が null でも動作するが戻り値が決まった値に成らないので注意
            var digitWithMaximumSize = CatalogForm.GetDigitWithMaximumSize(measure);

            //pageToolStripStatusLabel.Width = Program.DpiScalingX(70);// (70 * Program.DisplayMagnificationX4 + 2) / 4;
            pageToolStripStatusLabel.Width = CatalogForm.GetMaxWidthWithTemplates(measure, digitWithMaximumSize, " 00000 / 00000");
            imageSizeToolStripStatusLabel.Width = CatalogForm.GetMaxWidthWithTemplates(measure, digitWithMaximumSize, "00000x00000");

            currentImageLongPathToolStripTextBox.ReadOnly = true;
            if (MenuTextFont != null)
            {
                currentImageLongPathToolStripTextBox.Font = MenuTextFont;
            }
            currentImageLongPathToolStripTextBox.MouseLeave += currentImageLongPathToolStripTextBox_MouseLeave;
            statusStrip.Items.Insert(0, currentImageLongPathToolStripTextBox);
            //MessageBox.Show(statusStrip.Items.Count.ToString());

            msView = new MessageForwarder(pbView, ForwardedMessage.MouseWheel);
            msStatusStrip = new MessageForwarder(statusStrip, ForwardedMessage.MouseWheel);
            msPage = new MessageForwarder(mtbPage, ForwardedMessage.MouseWheel);


            if (LoadSettingToLoad_FormLocation != null)
            {
                Location = (Point)LoadSettingToLoad_FormLocation;
                LoadSettingToLoad_FormLocation = null;
            }

            // Location 変更後であること
            //if (ViewerForm_Load_CallTick) tmWatchIpc_Tick(sender, null);

            ViewerForm_Load_Called = true;
        }

        private void SetMessageForwarderStop(bool stop)
        {
            if (msView != null)
            {
                msView.Stop = stop;
                msStatusStrip.Stop = stop;
                msPage.Stop = stop;
            }
        }

        private Control[] SetMessages_LayoutControls;

        public void SetMessages()
        {
            if (SetMessages_LayoutControls == null)
            {
                SetMessages_LayoutControls = new Control[]
                {
                    menuStrip,
                    startToolStripMenuItem.DropDown,
                    loadArchivesInArchiveToolStripMenuItem.DropDown,
                    viewToolStripMenuItem.DropDown,
                    viewToolStripMenuItem.DropDown,
                    scalingAlgorithmNormalToolStripMenuItem.DropDown,
                    scalingAlgorithmMagnifierToolStripMenuItem.DropDown,
                    moveToolStripMenuItem.DropDown,
                    ratioToolStripMenuItem.DropDown,
                    slideshowToolStripMenuItem.DropDown,
                    editToolStripMenuItem.DropDown,
                    optionToolStripMenuItem.DropDown,
                };
            }

            foreach (var control in SetMessages_LayoutControls) control.SuspendLayout();

            startToolStripMenuItem.Text = Message._Start;
            viewToolStripMenuItem.Text = Message._View;
            //imageFilterToolStripMenuItem.Text = Message._Image;
            moveToolStripMenuItem.Text = Message._Move;
            editToolStripMenuItem.Text = Message._Edit;

            openFileToolStripMenuItem.Text = Message.OpenFile + " ...";
            //openNewThumbnailWindowToolStripMenuItem.Text = Message.OpenNewThumbnailWindow;
            switchToThumbnailWindowToolStripMenuItem.Text = Message.SwitchToThumbnailWindow;
            cloneTheWindowToolStripMenuItem.Text = Message.CloneWindow;
            loadWholeArchiveIntoMemoryToolStripMenuItem.Text = Message.LoadWholeArchiveIntoMemory;
            readOnMemoryNoneToolStripMenuItem.Text = Message.NoneRecommended;
            readOnMemoryExceptLookAheadToolStripMenuItem.Text = Message.ExceptReadAheadProcess;
            readOnMemoryAlwaysToolStripMenuItem.Text = Message.Always;
            loadArchivesInArchiveToolStripMenuItem.Text = Message.LoadArchivesInArchive;
            nonerecommendedToolStripMenuItem.Text = Message.NoneRecommended;
            untilFoundToolStripMenuItem.Text = Message.TwoLevelUntilFound;
            alwaysLoadArchivesInArchiveToolStripMenuItem.Text = Message.OneLevelCompletelyNotRecommended;
            createViewerShortcutFromCurrentSettingsToolStripMenuItem.Text = Message.CreateShortcutFromCurrentSettings + "...";
            fullScreenInStartToolStripMenuItem.Text = Message.Fullscreen;
            windowToolStripMenuItem.Text = Message.Window;
            toggleFullScreenModeToolStripMenuItem.Text = Message.ToggleFullscreenMode;
            exitToolStripMenuItem.Text = Message.Exit;

            alwaysAutoHideUIToolStripMenuItem.Text = Message.AlwaysAutomaticallyHideUI;

            magnifierToolStripMenuItem.Text = Message.ToggleMagnifier;
            //magnifierToolStripMenuItem.ShortcutKeyDisplayString = Message.MiddleClick + ", Z";
            enableMagnifierToolStripMenuItem.Text = Message.EnableMagnifier;
            disableMagnifierToolStripMenuItem.Text = Message.DisableMagnifier;

            scalingAlgorithmNormalToolStripMenuItem.Text = Message.ScalingAlgorithmNormal;
            scalingAlgorithmMagnifierToolStripMenuItem.Text = Message.ScalingAlgorithmMagnifier;
            defaultScalingAlgorithmNormalToolStripMenuItem.Text = Message.DefaultHighQuality;
            default2ScalingAlgorithmNormalToolStripMenuItem.Text = Message.DefaultHighSpeed;
            defaultMagnifierToolStripMenuItem.Text = Message.DefaultHighSpeed;
            default0MagnifierToolStripMenuItem.Text = Message.DefaultHighQuality;
            nearestNeighborMagnifierToolStripMenuItem.Text = Message.NearestNeighbor;
            nearestNeighborNormalToolStripMenuItem.Text = Message.NearestNeighbor;
            areaAverageMagnifierToolStripMenuItem.Text = Message.AreaAverage;
            areaAverageNormalToolStripMenuItem.Text = Message.AreaAverage;

            linearizeColorSpaceforGamma22NormalToolStripMenuItem.Text = Message.LinearizeColorSpace;
            linearizeColorSpaceforGamma22MagnifierToolStripMenuItem.Text = Message.LinearizeColorSpace;

            useAreaAverageWhenUpsizingNormalToolStripMenuItem.Text = Message.UseAreaAverageWhenUpsizing;
            useAreaAverageWhenUpsizingToolStripMenuItem.Text = Message.UseAreaAverageWhenUpsizing;

            leftToRightToolStripMenuItem.Text = Message.LeftToRight;
            rightToLeftToolStripMenuItem.Text = Message.RightToLeft;
            singlePageToolStripMenuItem.Text = Message.SinglePage;
            singlePageWithoutScalingUpToolStripMenuItem.Text = Message.SinglePageWithoutScalingUp;
            togglePageSequenceToolStripMenuItem.Text = Message.TogglePageSequence;
            //forceTwoPageSpreadToolStripMenuItem.Text = Message.ForcePageSpread;
            forceFirstPageToBeSingleToolStripMenuItem.Text = Message.ForceFirstPageToBeSingle;
            forceTwoPageSpreadToolStripMenuItem.Text = Message.PrioritizePageSpread;
            allowPageDivisionToolStripMenuItem.Text = Message.AllowPageDivision;

            orientSeekBarDirectionToPageSequenceToolStripMenuItem.Text = Message.OrientSeekBarDirectionToPageSequence;
            selectBackgroundColorToolStripMenuItem.Text = Message.SelectBackgroundColor + "...";
            showAHintWhenChangingSettingsToolStripMenuItem.Text = Message.ShowHintWhenChangingSettings;

            slideshowToolStripMenuItem.Text = Message.S_lideshow;
            startSlideshowToolStripMenuItem.Text = Message.StartSlideshow;
            stopSlideshowToolStripMenuItem.Text = Message.StopSlideshow;
            toggleSlideshowToolStripMenuItem.Text = Message.ToggleSlideshow;
            _1SecIntervalsToolStripMenuItem.Text = Message.OneSecondIntervals;
            _2SecIntervalsToolStripMenuItem.Text = Message._1SecondsIntervals.Replace("$1", "2");
            _3SecIntervalsToolStripMenuItem.Text = Message._1SecondsIntervals.Replace("$1", "3");
            _5SecIntervalsToolStripMenuItem.Text = Message._1SecondsIntervals.Replace("$1", "5");
            _10SecIntervalsToolStripMenuItem.Text = Message._1SecondsIntervals.Replace("$1", "10");
            _20SecIntervalsToolStripMenuItem.Text = Message._1SecondsIntervals.Replace("$1", "20");
            _30SecIntervalsToolStripMenuItem.Text = Message._1SecondsIntervals.Replace("$1", "30");
            _60SecIntervalsToolStripMenuItem.Text = Message._1SecondsIntervals.Replace("$1", "60");
            repeatToolStripMenuItem.Text = Message.Repeat;
            openNextOnTerminalToolStripMenuItem.Text = Message.AllowToOpenNextItem;

            virtualFoldersToolStripMenuItem.Text = Message.Virtual_Folder;

            rotateLeftToolStripMenuItem.Text = Message.RotateLeft;
            rotateRightToolStripMenuItem.Text = Message.RotateRight;
            cancelRotationSettingToolStripMenuItem.Text = Message.CancelRotationSetting;
            //binarizationToolStripMenuItem.Text = Message.Binarization;
            //autoContrastControlToolStripMenuItem.Text = Message.AutoContrastControl;

            //removeMoireToolStripMenuItem.Text = Message.RemoveMoire;

            nextPageToolStripMenuItem.Text = Message.NextPage;
            previousPageToolStripMenuItem.Text = Message.PreviousPage;
            rightPageToolStripMenuItem.Text = Message.RightPage;
            leftPageToolStripMenuItem.Text = Message.LeftPage;
            moveForward1PageToolStripMenuItem.Text = Message.MoveForwardOnePage;
            //moveForward1PageToolStripMenuItem.ShortcutKeyDisplayString = Message.WheelDownOnBar + ", " + Message.CursorDown;
            moveBack1PageToolStripMenuItem.Text = Message.MoveBackOnePage;
            //moveBack1PageToolStripMenuItem.ShortcutKeyDisplayString = Message.WheelUpOnBar + ", " + Message.CursorUp;
            moveRight1PageToolStripMenuItem.Text = Message.MoveRightOnePage;
            moveLeft1PageToolStripMenuItem.Text = Message.MoveLeftOnePage;

            ratioToolStripMenuItem.Text = Message.PositionRatio;

            openNextToolStripMenuItem.Text = Message.OpenNext;
            openPreviousToolStripMenuItem.Text = Message.OpenPrevious;
            openRightToolStripMenuItem.Text = Message.OpenRight;
            openLeftToolStripMenuItem.Text = Message.OpenLeft;

            //openNextToolStripMenuItem.ShortcutKeyDisplayString = Message.MiddleClickOnPosteriorHalfOfBar + ", Alt+" + Message.CursorRight; 
            //openPreviousToolStripMenuItem.ShortcutKeyDisplayString = Message.MiddleClickOnAnteriorHalfOfBar + ", Alt+" + Message.CursorLeft;


            noneToolStripMenuItem.Text = Message.None;
            tagEditorToolStripMenuItem.Text = Message.EditTags + "...";
            addTagsFromTheFileNameToolStripMenuItem.Text = Message.AddTagsFromName + "...";
            uncheckAllToolStripMenuItem.Text = Message.UncheckAll;
            //uncheckAllToolStripMenuItem.ShortcutKeyDisplayString = Message.RightClickNotToClose;
            clearPageSequenceSettingToolStripMenuItem.Text = Message.ClearPageSequenceSetting;
            saveCurrentPageSequenceToolStripMenuItem.Text = Message.SaveCurrentPageSequence;
            clearThumbnailSettingToolStripMenuItem.Text = Message.ClearCoverSetting;
            //setThumbnailManuallyToolStripMenuItem.Text = Message.SetThumbnailManually;

            optionToolStripMenuItem.Text = Message._Others;
            tagEditorToolStripMenuItem1.Text = Message.Edit_Tags + "...";
            preferenceToolStripMenuItem1.Text = Message.Common_Settings + "...";
            //mouseGestureToolStripMenuItem.Text = Message._MouseGestures + "...";
            mouseGestureToolStripMenuItem.Text = Message._MouseTouchGestures + "...";
            //keyboardShortcutToolStripMenuItem.Text = Message._KeysMouseButtons + "...";
            keyboardShortcutToolStripMenuItem.Text = Message._BasicOperationSettings + "...";

            fullScreenToolStripMenuItem.Text = FullScreen ? Message.Window : Message.Fullscreen;

            setShortcutKeyDisplayStrings(setKeyNames: true);

            for (var i = SetMessages_LayoutControls.Length - 1; i >= 0; i--)
            {
                var control = SetMessages_LayoutControls[i];
                control.ResumeLayout(performLayout: false);
                control.PerformLayout();
            }
        }

        double? gestureListener_Rotate_Degrees = null;

        private void gestureListener_Rotate(object sender, RotateEventArgs e)
        {
            CloseAllMenu();
            if (ViewerMode == ViewerModeEnum.Normal || ViewerMode == ViewerModeEnum.Magnifier)
            //if (ViewerMode == ViewerModeEnum.Normal || ViewerMode == ViewerModeEnum.Magnifier || ViewerMode == ViewerModeEnum.MagnifierOpening)
            {
                double deg;
                if (e.Begin)
                {
                    gestureListener_Rotate_Degrees = 0;
                    deg = 0;
                }
                else if (e.End)
                {
                    gestureListener_Rotate_Degrees = null;
                    return;
                }
                else
                {
                    if (gestureListener_Rotate_Degrees == null)
                    {
                        gestureListener_Rotate_Degrees = 0;
                        deg = e.Degrees;
                    }
                    else
                    {
                        deg = (double)gestureListener_Rotate_Degrees - e.Degrees;// フィルタと符号が逆
                        gestureListener_Rotate_Degrees = deg;
                    }
                }

                const double roteDegree = 45;
                var currentDegrees = imageFilter.RotationAngle;
                if (currentDegrees == 270 && deg < 0 || currentDegrees == 90 && deg > 0)
                {
                    gestureListener_Rotate_Degrees = 0;
                }
                else if (deg <= -roteDegree)
                {
                    imageFilter.RotationAngle = currentDegrees == 0 ? 270 : 0;
                    reloadForChangeFilter(forOnlyPostFilter: false);
                    gestureListener_Rotate_Degrees = deg + 90;
                }
                else if (gestureListener_Rotate_Degrees >= roteDegree)
                {
                    imageFilter.RotationAngle = currentDegrees == 0 ? 90 : 0;
                    reloadForChangeFilter(forOnlyPostFilter: false);
                    gestureListener_Rotate_Degrees = deg - 90;
                }
            }
            else
            {
                gestureListener_Rotate_Degrees = null;
            }
        }

        private void gestureListener_TwoFingerTap(object sender, TwoFingerTapEventArgs e)
        {
            CloseAllMenu();
            if (ViewerMode == ViewerModeEnum.Magnifier)
            {
                MagnifierPhase3(toPhase4: true);
                MagnifierPhase4(toPhase1: false);
            }
            else if (ViewerMode == ViewerModeEnum.Normal)
            {
                MagnifierPhase1(pbView.PointToClient(e.Location));
                MagnifierPhase2();
            }
            else if (ViewerMode == ViewerModeEnum.MagnifierOpening)
            {
                var value = magnifyingPower.GetValue();
                if (value == null || value > 1)
                {
                    MagnifierPhase2();
                }
                else
                {
                    MagnifierPhase2();
                    MagnifierPhase3(toPhase4: true);
                    MagnifierPhase4(toPhase1: false);
                }
            }
        }

        private void gestureListener_Zoom(object sender, ZoomEventArgs e)
        {
            CloseAllMenu();
            TouchListener_Pan_Delta = null;
            if (ViewerMode == ViewerModeEnum.CoverSetting) return;

            if (e.Begin)
            {
                if (gestureListener_Rotate_Degrees == null && (ViewerMode == ViewerModeEnum.Normal || ViewerMode == ViewerModeEnum.Magnifier))
                {
                    if (ViewerMode == ViewerModeEnum.Magnifier)
                    {
                        MagnifierPhase3(toPhase4: true);
                        MagnifierPhase4(toPhase1: false);
                    }
                    else
                    {
                        //resetMagnifyingPower(1, checkChange: true, redraw: false);
                    }

                    // 係数の設定
                    // 0.7 を基本とし、
                    // GetCenterOfMagnifierRectangle_DistanceCoef * longer / Math.Max(1, e.Distance) が 4 を上回るならそうならないように更に小さくする
                    var pbViewSize = pbView.Size;
                    var longer = Math.Max(1, Math.Max(pbViewSize.Width, pbViewSize.Height));
                    GetCenterOfMagnifierRectangle_DistanceCoef = Math.Min(0.7, 4.0 / longer * e.Distance);

                    MagnifyingInfo newMagPower;
                    MagnifierPhase1(GetCenterOfMagnifierRectangle(e, out newMagPower));

                    if (ViewerMode == ViewerModeEnum.MagnifierOpening)
                    {
                        resetMagnifyingPower(newMagPower, checkChange: true, redraw: true);
                    }

                }
            }
            else if (!e.End)
            {
                if (ViewerMode == ViewerModeEnum.MagnifierOpening)
                {
                    MagnifyingInfo newMagPower;
                    GetMagnifierRectangle_LastCursorPosition = GetCenterOfMagnifierRectangle(e, out newMagPower);
                    resetMagnifyingPower(newMagPower, checkChange: true, redraw: true);
                }
            }
            else
            {
                var value = magnifyingPower.GetValue();
                if (value == null || value > 1)
                {
                    MagnifierPhase2();
                }
                else
                {
                    MagnifierPhase2();
                    MagnifierPhase3(toPhase4: true);
                    MagnifierPhase4(toPhase1: false);
                }
            }
        }

        private double GetCenterOfMagnifierRectangle_DistanceCoef;

        private Point GetCenterOfMagnifierRectangle(ZoomEventArgs e, out MagnifyingInfo newMagnifyingPower)
        {
            var center = pbView.PointToClient(e.Location);
            var distance = e.Distance;
            var pbViewSize = getCurrentPbViewSize();
            var dicreasedLonger = GetCenterOfMagnifierRectangle_DistanceCoef * Math.Max(pbViewSize.Width, pbViewSize.Height);

            var imageSize_ = getCurrentVisibleImageSize(pbViewSize);

            var value = dicreasedLonger / Math.Max(1, distance);

            if (imageSize_ == null)
            {
                value = Math.Min(4, Math.Max(1, value));
                newMagnifyingPower = MagnifyingInfo.FromValue(value);

            }
            else
            {
                var imageSize = (Size)imageSize_;
                var fits = new List<double>();
                var mh = MagnifyingInfo.FitHorizontal.GetPower(pbViewSize, imageSize);
                if (mh < 4 && mh > 1) fits.Add(mh);
                var mv = MagnifyingInfo.FitVertical.GetPower(pbViewSize, imageSize);
                if (mv < 4 && mv > 1) fits.Add(mv);
                fits.Sort();
                var margin = 0.1;
                for (var i = fits.Count - 1; i >= 0; i--)
                {
                    var m = fits[i];
                    var mInv = 1 / m;
                    var vInv = 1 / value;
                    if (vInv <= mInv) break;
                    else if (vInv - margin <= mInv)
                    {
                        value = m;
                        break;
                    }
                    else
                    {
                        value = 1 / (vInv - margin);
                    }
                }

                // GetValue が 1 以下になる場合はその値で FromValue し直すことで拡大を行わないことを知らせる
                // 等倍の場合、他のページも等倍の可能性が高いのでフィットの必要性は低く、これを行う必要は認められない。
                if (value <= 1)
                {
                    newMagnifyingPower = MagnifyingInfo.FromValue(1);
                }
                else if (value == mh)
                {
                    newMagnifyingPower = MagnifyingInfo.FitHorizontal;
                }
                else if (value == mv)
                {
                    newMagnifyingPower = MagnifyingInfo.FitVertical;
                }
                else if (value < 4)
                {
                    newMagnifyingPower = MagnifyingInfo.FromValue(value);
                }
                else
                {
                    newMagnifyingPower = MagnifyingInfo.FromValue(4);
                }

            }

            // サイズの設定
            // 現在表示中の画像サイズが取得できなければ常に FromValue で。

            return center;
        }

        private void currentImageLongPathToolStripTextBox_MouseLeave(object sender, EventArgs e)
        {
            if (HideUI && mtbPageTop_As_Reference_pbViewBottom == 0)
            {
                var cursorPosition = Cursor.Position;
                
                // 境界上に乗っていた場合にその後 MouseLeave が発生しなくなる現象への対策
                // ポインタが境界上に飛び込んだ場合の対処はタイマーに任せる
                var positionOnClient = statusStrip.PointToClient(cursorPosition);
                var bounds = currentImageLongPathToolStripTextBox.Bounds;
                if (bounds.Contains(positionOnClient))
                {
                    var dl = positionOnClient.X - bounds.Left + 1;
                    var dr = bounds.Right - positionOnClient.X;
                    var dt = positionOnClient.Y - bounds.Top + 1;
                    var db = bounds.Bottom - positionOnClient.Y;
                    var dm = Math.Min(Math.Min(Math.Min(dl, dr), dt), db);
                    if (dm == db)
                    {
                        cursorPosition.Y += db;
                        Cursor.Position = cursorPosition;
                    }
                }

                if (!pbView.ClientRectangle.Contains(pbView.PointToClient(Cursor.Position)))
                {
                    hideUnderUI(null, null);
                }
            }
        }

        private void hideUnderUIForMouseLeave(object sender, EventArgs e)
        {
            if (HideUI && mtbPageTop_As_Reference_pbViewBottom == 0)
            {
                var cursorPosition = Cursor.Position;
                var clientPoint = PointToClient(cursorPosition);
                var x = pnlSeekbar.Left;
                var y = pnlSeekbar.Top;
                if (!new Rectangle(x, y, statusStrip.Right - x, statusStrip.Bottom - y).Contains(clientPoint) ||
                    !ActivateManager.InVisibleRegion(this, clientPoint, cursorPosition, checkParentsCount: 3))
                {
                    hideUnderUI(null, null);
                }
            }
        }

        // 上が期待通りに働かないので下で対処
        private bool MouseInPbView = false;

        private void pbView_MouseEnter(object sender, EventArgs e)
        {
            MouseInPbView = true;
            hideUnderUI(null, null);
            //pbView.Focus();

            if (ViewerMode == ViewerModeEnum.MagnifierOpening || ViewerMode == ViewerModeEnum.Magnifier || ViewerMode == ViewerModeEnum.MagnifierClosing)
            {
                Program.HideCursor();
            }
        }

        Point pbView_MouseMove_prevPos;

        Point? pbView_MouseMove_prevContPos = null;

        bool pbView_PreviewKeyDown_To_pbView_MouseMove_StopShwoMagnifierRectangle = false;

        private void pbView_MouseMove(object sender, MouseEventArgs e)
        {
            var stopShowMag = pbView_PreviewKeyDown_To_pbView_MouseMove_StopShwoMagnifierRectangle;
            pbView_PreviewKeyDown_To_pbView_MouseMove_StopShwoMagnifierRectangle = false;
            if (pbView_MouseMove_prevPos == e.Location) return;
            var afterEnter = pbView_MouseMove_prevContPos == null;
            pbView_MouseMove_prevContPos = pbView_MouseMove_prevPos = e.Location;
            TouchListener_Pan_Delta = null;

            if (!afterEnter) ksViewForm.IgnoreAnyMouseEventOnce = MouseButtons.None;

            if (!(ViewerMode == ViewerModeEnum.MagnifierOpening || ViewerMode == ViewerModeEnum.Magnifier || ViewerMode == ViewerModeEnum.MagnifierClosing))
            {
                Program.ShowCursor();
            }
            //setMouseButtonShape(GetPositionOnView(e.Location));

            //var y = pbView.PointToScreen(e.Location).Y;
            if (!CursorBounded())
            {
                var y = e.Location.Y;
                if (menuStrip_Bottom > 0 && y < menuStrip_Bottom)
                {
                    menuStrip.Visible = true;
                    menuStrip_Bottom = 0;

                    hideUnderUI(null, null); // currentImageLongPathToolStripTextBox_MouseLeave の仕様が期待通りでないので
                }
                else if (showUnderUI(e.Location)) { }
            }

            if (ViewerMode == ViewerModeEnum.MagnifierOpening || ViewerMode == ViewerModeEnum.MagnifierClosing)
            {
                ShwoMagnifierRectangle();
            }
            else if (ViewerMode == ViewerModeEnum.Magnifier)
            {
                var showMag = TouchListener_Pan_Delta == null && GetMagnifierRectangle_LastCursorPosition == null;

                if (showMag)
                {
                    //pbView.Invalidate(false); // 描画は Paint イベントハンドラで行われる
                    pbPaintInvalidate();
                }
            }
            else if (ViewerMode == ViewerModeEnum.CoverSetting)
            {
                if (!stopShowMag) ShwoCoverRectangle();
            }
            else if (DrawMeasure) pbPaintDrawOnlyMeasure();
        }

        private bool showUnderUI(Point pbViewClientPoint)
        {
            //if (mtbPage_Top > 0 && pbViewClientPoint.Y >= mtbPage_Top)
            if (mtbPageTop_As_Reference_pbViewBottom != 0 && pbViewClientPoint.Y >= pbView.Bottom + mtbPageTop_As_Reference_pbViewBottom)
            {
                pnlSeekbar.Visible = true;
                pnlSeekbar_PreStatusStripVisibleTrue();
                statusStrip.Visible = true;
                if (SetCurrentImageLongPathRequired) SetCurrentImageLongPath();
                //mtbPage_Top = 0;
                mtbPageTop_As_Reference_pbViewBottom = 0;
                return true;
            }
            return false;
        }

        // 画面のちらつきを回避する
        private void pnlSeekbar_PreStatusStripVisibleTrue()
        {
            if (statusStrip_VisibleChanged_FirstVisible)
            {
                var currentBound = statusStrip.Bounds;
                var statusStripBound = new Rectangle(pnlSeekbar.Left, statusStrip.Parent.Bottom - currentBound.Height, pnlSeekbar.Width, currentBound.Height); // 二回目以降と完全に一致
                statusStrip.Bounds = statusStripBound;
            }
        }

        private int mtbPage_MouseMove_LastCallCount = -1;

        private int mtbPage_MouseMove_LastTick;

        private System.Windows.Forms.Timer mtbPage_MouseMove_Timer;

        private void mtbPage_MouseMove(object sender, MouseEventArgs e)
        {
            mtbPage.ValueCanChangedByMouseButton = true;
            if (ResizedImageArray == null) return;
            if (mtbPage.PointToValue(e.Location) == mtbPage_MouseMove_LastValue && SetBackgroundMode_CallCount == mtbPage_MouseMove_LastCallCount) return;
            var elapsed = unchecked(Environment.TickCount - mtbPage_MouseMove_LastTick);
            if (0 <= elapsed && elapsed < mtbPage_MouseMove_Interval)
            {
                if (mtbPage_MouseMove_Timer == null)
                {
                    mtbPage_MouseMove_Timer = new System.Windows.Forms.Timer(components) { Interval = mtbPage_MouseMove_Interval };
                    mtbPage_MouseMove_Timer.Tick += (s, ev) => SetBackgroundModeForTrackbarHover();
                }
                if (!mtbPage_MouseMove_Timer.Enabled) mtbPage_MouseMove_Timer.Start();
                return;
            }
            SetBackgroundModeForTrackbarHover();
        }

        private void SetBackgroundModeForTrackbarHover()
        {
            mtbPage_MouseMove_Timer?.Stop();
            mtbPage_MouseMove_LastTick = Environment.TickCount;
            mtbPage_MouseMove_LastValue = mtbPage.PointToValue(mtbPage.PointToClient(Cursor.Position));
            SetBackgroundMode();
            mtbPage_MouseMove_LastCallCount = SetBackgroundMode_CallCount;
        }

        private bool MouseDownForCoverSettingExit = false;

        private void pbView_MouseDown(object sender, MouseEventArgs e)
        {
            // キーボードショートカットに置き換え
            if (e.Button == MouseButtons.Left)
            {
                if (ksViewForm.IgnoreAnyMouseEventOnce != MouseButtons.Left)
                {
                    if (ViewerMode == ViewerModeEnum.CoverSetting)
                    {
                        SetCoverForThumbnail();
                        ksViewForm.IgnoreMouseEventOnce = e;
                    }
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (ViewerMode == ViewerModeEnum.CoverSetting)
                {
                    ExitCoverSetting();
                    if (mgView.Enabled)
                    {
                        MouseDownForCoverSettingExit = true;
                    }
                    else
                    {
                        ksViewForm.IgnoreMouseEventOnce = e;
                    }
                }
            }
            //setMouseButtonShape(position);
        }

        private void closeWindowtoolStripMenuItem_MouseEnter(object sender, EventArgs e)
        {
            // メニューバー上のボタンの色の変化をマウスカーソルの位置と正確に一致させるための処理
            if (FullScreen)
            {
                var tsmi = (ToolStripMenuItem)sender;
                tsmi.Select();
            }
            
            // 古い方法
        }

        private bool KeysUpState = false;

        private bool KeysDownState = false;

        private bool KeysLeftState = false;

        private bool KeysRightState = false;

        private void Control_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                var preUp = KeysUpState;
                var preDown = KeysDownState;
                var preLeft = KeysLeftState;
                var preRight = KeysRightState;

                var up = e.KeyCode == Keys.Up; ;
                var down = e.KeyCode == Keys.Down;
                var left  = e.KeyCode == Keys.Left;
                var right = e.KeyCode == Keys.Right;

                KeysUpState = KeysUpState || up;
                KeysDownState = KeysDownState || down;
                KeysLeftState = KeysLeftState || left;
                KeysRightState = KeysRightState || right;

                var vm = ViewerMode;
                if (vm == ViewerModeEnum.MagnifierOpening || vm == ViewerModeEnum.MagnifierClosing)
                {
                    var r = BoolToInt(KeysRightState);
                    var l = BoolToInt(KeysLeftState);
                    var d = BoolToInt(KeysDownState);
                    var u = BoolToInt(KeysUpState);

                    var sum = r + l + d + u;

                    if (sum == 1 || sum == 2 && (KeysRightState ^ KeysLeftState))
                    {
                        var xu = r - l;
                        var yu = d - u;

                        var currentPosition = Cursor.Position;
                        Cursor.Position = new Point(currentPosition.X + Control_PreviewKeyDown_Speed * xu, currentPosition.Y + Control_PreviewKeyDown_Speed * yu);
                    }
                    else if (sum == 2)
                    {
                        // 直前の操作をキャンセル
                        var xu = !preRight && right ? +1 : !preLeft && left ? -1 : 0;
                        var yu = !preDown && down ? +1 : !preUp && up ? -1 : 0;
                        if (xu != 0 || yu != 0)
                        {
                            var currentPosition = Cursor.Position;
                            pbView_PreviewKeyDown_To_pbView_MouseMove_StopShwoMagnifierRectangle = true;
                            Cursor.Position = new Point(currentPosition.X + Control_PreviewKeyDown_Speed * xu, currentPosition.Y + Control_PreviewKeyDown_Speed * yu);
                        }

                        // 上下で拡大、左右で縮小
                        changeMagnifyingPowerInMagnifierOpeningOrClosing(zoomUp: KeysUpState);
                    }
                }
            }
            else if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey)
            {
                changeMagnifyingPowerInMagnifierOpeningOrClosing(zoomUp: true);
            }
            else if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey)
            {
                changeMagnifyingPowerInMagnifierOpeningOrClosing(zoomUp: false);
            }
        }

        private void ViewerForm_KeyUp(object sender, KeyEventArgs e)
        {
            KeysUpState = KeysUpState && e.KeyCode != Keys.Up;
            KeysDownState = KeysDownState && e.KeyCode != Keys.Down;
            KeysLeftState = KeysLeftState && e.KeyCode != Keys.Left;
            KeysRightState = KeysRightState && e.KeyCode != Keys.Right;
        }

        private static int BoolToInt(bool b)
        {
            return b ? 1 : 0;
        }
    }
}
