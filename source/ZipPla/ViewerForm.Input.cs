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
                movePageToMinimulForward();
            }
            else if (delta == 1)
            {
                movePageToMinimulBack();
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
    }
}
