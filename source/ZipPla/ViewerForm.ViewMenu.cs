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
        private void reloadForChangeFilter(bool forOnlyPostFilter)
        {
            drawLoading_ShowingMessage = true;
            Reload(force: true, updateOriginal: !forOnlyPostFilter);
        }

        private bool statusStrip_VisibleChanged_FirstVisible = true;

        private void statusStrip_VisibleChanged(object sender, EventArgs e)
        {
            if (statusStrip_VisibleChanged_FirstVisible && ipcLookAheadInfo == null && statusStrip.Visible)
            {
                statusStrip_VisibleChanged_FirstVisible = false;
                Program.TextBoxShowRight(currentImageLongPathToolStripTextBox); // statusStrip の位置調整を行ってもなお必要
            }
        }

        private void openStartMenu()
        {
            openToolStripMenuItem(startToolStripMenuItem, Cursor.Position, startToolStripMenuItem_DropDownOpening, null, scrollable: false, mouseGestureEnabled: false);
        }

        private void openMoveMenu()
        {
            openToolStripMenuItem(moveToolStripMenuItem, Cursor.Position, moveToolStripMenuItem_DropDownOpening, null, scrollable: false, mouseGestureEnabled: false);
        }

        private void openViewMenu()
        {
            openToolStripMenuItem(viewToolStripMenuItem, Cursor.Position, viewToolStripMenuItem_DropDownOpening, null, scrollable: false, mouseGestureEnabled: false);
        }

        private void openSlideshowMenu()
        {
            openToolStripMenuItem(slideshowToolStripMenuItem, Cursor.Position, slideshowToolStripMenuItem_DropDownOpening, null, scrollable: false, mouseGestureEnabled: false);
        }

        private void openVirtualFolderMenu()
        {
            openToolStripMenuItem(virtualFoldersToolStripMenuItem, Cursor.Position, virtualFoldersToolStripMenuItem_DropDownOpening, null, scrollable: true, mouseGestureEnabled: false);
        }

        private void openOthersMenu()
        {
            openToolStripMenuItem(optionToolStripMenuItem, Cursor.Position, null, null, scrollable: false, mouseGestureEnabled: false);
        }

        private void editToolStripMenuItem_DropDownOpeningForContextMenuStrip(object sender, EventArgs e) { editToolStripMenuItem_DropDownOpening(sender, e, openToolStripMenuItemAsContextMenuStrip_Frame); }

        private void openEditMenu()
        {
            openToolStripMenuItem(editToolStripMenuItem, Cursor.Position, editToolStripMenuItem_DropDownOpeningForContextMenuStrip, editToolStripMenuItem_DropDownClosed_CommonPart, scrollable: true, mouseGestureEnabled: true);
        }

        private void openToolStripMenuItemByKeyboard(ToolStripMenuItem toolStripMenuItem)
        {
            if (FullScreen)
            {
                menuStrip.Visible = true;
                menuStrip_Bottom = 0;
                hideUnderUI(null, null); // currentImageLongPathToolStripTextBox_MouseLeave の仕様が期待通りでないので
            }
            toolStripMenuItem.ShowDropDown();
        }

        private void openToolStripMenuItem(ToolStripMenuItem toolStripMenuItem, Point screenLocation, EventHandler dropDownOpening, EventHandler dropDownClosed, bool scrollable, bool mouseGestureEnabled)
        {
            if (ksViewForm.PureKeyboardShortcutExcuting())
            {
                openToolStripMenuItemByKeyboard(toolStripMenuItem);
            }
            else
            {
                openToolStripMenuItemAsContextMenuStrip(toolStripMenuItem, screenLocation, dropDownOpening, dropDownClosed, scrollable, mouseGestureEnabled);
            }
        }

        private ContextMenuStrip openToolStripMenuItemAsContextMenuStrip_Frame = null;

        private MiniControlTouchGesture openToolStripMenuItemAsContextMenuStrip_Frame_MiniControlTouchGesture;

        private GestureListener openToolStripMenuItemAsContextMenuStrip_Frame_GestureListener;

        private void openToolStripMenuItemAsContextMenuStrip(ToolStripMenuItem toolStripMenuItem, Point screenLocation, EventHandler dropDownOpening, EventHandler dropDownClosed, bool scrollable, bool mouseGestureEnabled)
        {
            if (openToolStripMenuItemAsContextMenuStrip_Frame == null)
            {
                openToolStripMenuItemAsContextMenuStrip_Frame = new ContextMenuStrip();
                openToolStripMenuItemAsContextMenuStrip_Frame.Closed += openToolStripMenuItemAsContextMenuStrip_Frame_Closed;

                new KeyMouseBinder(openToolStripMenuItemAsContextMenuStrip_Frame, requiredBindingEditMenu);

                try
                {
                    openToolStripMenuItemAsContextMenuStrip_Frame_GestureListener = MiniControlTouchGesture.GetGestureListener(openToolStripMenuItemAsContextMenuStrip_Frame);
                    openToolStripMenuItemAsContextMenuStrip_Frame_MiniControlTouchGesture = new MiniControlTouchGesture(openToolStripMenuItemAsContextMenuStrip_Frame, openToolStripMenuItemAsContextMenuStrip_Frame_GestureListener, onlyHorizontalStart: true);
                    openToolStripMenuItemAsContextMenuStrip_Frame_MiniControlTouchGesture.Targets.Add(openToolStripMenuItemAsContextMenuStrip_Frame);
                    openToolStripMenuItemAsContextMenuStrip_Frame_MiniControlTouchGesture.TouchGestureCompleted += CatalogForm.TouchGestureCompletedForTagMenu;
                    openToolStripMenuItemAsContextMenuStrip_Frame_MiniControlTouchGesture.TouchGestureStarting += CatalogForm.TouchGestureStartingForTagMenu;
                }
                catch { }
            }
            dropDownOpening(toolStripMenuItem, null);
            var items = toolStripMenuItem.DropDownItems;
            var copy = new ToolStripItem[items.Count];
            items.CopyTo(copy, 0);
            items.Clear(); 
            var contextMenuStrip = openToolStripMenuItemAsContextMenuStrip_Frame;
            var frameItems = contextMenuStrip.Items;
            frameItems.AddRange(copy);
            ToolStripDropDownClosedEventHandler eh = null;
            eh = (sender, e) =>
            {
                frameItems.Clear();
                items.AddRange(copy);
                dropDownClosed?.Invoke(toolStripMenuItem, null);
                contextMenuStrip.Closed -= eh;
            };
            contextMenuStrip.Closed += eh;
            if (scrollable)
            {
                ToolStripDropDownScroller.EnscrollableOneTime(contextMenuStrip, openToolStripMenuItemAsContextMenuStrip_Frame_GestureListener, enterFocus: true);
            }
            if (openToolStripMenuItemAsContextMenuStrip_Frame_MiniControlTouchGesture != null)
            {
                openToolStripMenuItemAsContextMenuStrip_Frame_MiniControlTouchGesture.Enabled = mouseGestureEnabled;
            }
            contextMenuStrip.Show(screenLocation);
            
        }

        private void openToolStripMenuItemAsContextMenuStrip_Frame_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            if (MouseButtons == MouseButtons.Left && pbView.ClientRectangle.Contains(pbView.PointToClient(Cursor.Position)))
            {
                ksViewForm.IgnoreAnyMouseEventOnce = MouseButtons.Left;
            }
            if (DrawMeasure) pbPaintDrawOnlyMeasure();
            SetMessageForwarderStop(false);
        }

        private static bool isIndividuallyImplemented(ScalingAlgorithm algorithm)
        {
            return algorithm == ScalingAlgorithm.Default || algorithm == ScalingAlgorithm.HighSpeed || algorithm == ScalingAlgorithm.NearestNeighbor;
        }

        private void scalingAlgorithmNormalToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            defaultScalingAlgorithmNormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.Default;
            default2ScalingAlgorithmNormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.HighSpeed;
            nearestNeighborNormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.NearestNeighbor;
            areaAverageNormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.AreaAverage;
            lanczos1NormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.Lanczos1;
            lanczos2NormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.Lanczos2;
            lanczos3NormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.Lanczos3;
            lanczos4NormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.Lanczos4;
            spline4NormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.Spline4;
            spline16NormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.Spline16;
            spline36NormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.Spline36;
            spline64NormalToolStripMenuItem.Checked = StandardScalingAlgorithm.ScaleDown == ScalingAlgorithm.Spline64;
        }

        int standardScalingAlgorithm_BenchRequest = -1;

        private void setStandardScalingAlgorithm(ScalingAlgorithm algorithm)
        {
            var MSR =
                KeyboardShortcut.GetKeyState(Keys.M) &&
                KeyboardShortcut.GetKeyState(Keys.S) &&
                KeyboardShortcut.GetKeyState(Keys.R);
            if (ResizeGammaNormal != GammaConversion.Value1_0 && isIndividuallyImplemented(algorithm) &&
                !isIndividuallyImplemented(StandardScalingAlgorithm.ScaleDown))
            {
                MessageBox.Show(this, Message.LinearizeColorSpaceDescription.Replace("\\n", "\n"),
                    Message.Information, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            if (MSR)
            {
                StandardScalingAlgorithm.ScaleDown = algorithm;
                if (!useAreaAverageWhenUpsizingNormalToolStripMenuItem.Checked)
                    StandardScalingAlgorithm.ScaleUp = algorithm;
                standardScalingAlgorithm_BenchRequest = currentPage;
                reloadForChangeFilter(forOnlyPostFilter: true);
            }
            else
            {
                if (StandardScalingAlgorithm.ScaleDown != algorithm)
                {
                    StandardScalingAlgorithm.ScaleDown = algorithm;
                    if (!useAreaAverageWhenUpsizingNormalToolStripMenuItem.Checked)
                        StandardScalingAlgorithm.ScaleUp = algorithm;
                    reloadForChangeFilter(forOnlyPostFilter: true);
                }
            }
        }

        private void defaultScalingAlgorithmNormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.Default);
        }

        private void default2ScalingAlgorithmNormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.HighSpeed);
        }

        private void nearestNeighborNormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.NearestNeighbor);
        }

        private void areaAverageNormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.AreaAverage);
        }

        private void lanczos1NormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.Lanczos1);
        }

        private void lanczos2NormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.Lanczos2);
        }

        private void lanczos3NormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.Lanczos3);
        }

        private void lanczos4NormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.Lanczos4);
        }

        private void spline4NormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.Spline4);
        }

        private void spline16NormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.Spline16);
        }

        private void spline36NormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.Spline36);
        }

        private void spline64NormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setStandardScalingAlgorithm(ScalingAlgorithm.Spline64);
        }

        private void linearizeColorSpaceforGamma22NormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (isIndividuallyImplemented(StandardScalingAlgorithm.ScaleDown))
            {
                if (ResizeGammaNormal == GammaConversion.Value2_2)
                {
                    ResizeGammaNormal = GammaConversion.Value1_0;
                }
                else
                {
                    ResizeGammaNormal = GammaConversion.Value2_2;
                    MessageBox.Show(this, Message.LinearizeColorSpaceDescription.Replace("\\n", "\n"),
                        Message.Information, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                if (useAreaAverageWhenUpsizingNormalToolStripMenuItem.Checked) reloadForChangeFilter(forOnlyPostFilter: true);
            }
            else
            {
                if (ResizeGammaNormal == GammaConversion.Value2_2)
                {
                    ResizeGammaNormal = GammaConversion.Value1_0;
                }
                else
                {
                    ResizeGammaNormal = GammaConversion.Value2_2;
                }
                reloadForChangeFilter(forOnlyPostFilter: true);
            }
        }

        private void useAreaAverageWhenUpsizingNormalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (useAreaAverageWhenUpsizingNormalToolStripMenuItem.Checked)
            {
                useAreaAverageWhenUpsizingNormalToolStripMenuItem.Checked = false;
                if (StandardScalingAlgorithm.ScaleUp != StandardScalingAlgorithm.ScaleDown)
                {
                    StandardScalingAlgorithm.ScaleUp = StandardScalingAlgorithm.ScaleDown;
                    reloadForChangeFilter(forOnlyPostFilter: true); // 以前は通常表示のときに 2 回呼んでいた
                }
            }
            else
            {
                useAreaAverageWhenUpsizingNormalToolStripMenuItem.Checked = true;
                if (StandardScalingAlgorithm.ScaleUp != ScalingAlgorithm.AreaAverage)
                {
                    StandardScalingAlgorithm.ScaleUp = ScalingAlgorithm.AreaAverage;
                    reloadForChangeFilter(forOnlyPostFilter: true);
                }
            }
        }

        private void scalingAlgorithmMagnifierToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            defaultMagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.HighSpeed;
            default0MagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.Default;
            nearestNeighborMagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.NearestNeighbor;
            areaAverageMagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.AreaAverage;
            lanczos1MagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.Lanczos1;
            lanczos2MagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.Lanczos2;
            lanczos3MagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.Lanczos3;
            lanczos4MagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.Lanczos4;
            spline4MagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.Spline4;
            spline16MagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.Spline16;
            spline36MagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.Spline36;
            spline64MagnifierToolStripMenuItem.Checked = MagnifierScalingAlgorithm.ScaleDown == ScalingAlgorithm.Spline64;
        }

        private void setMagnifierScalingAlgorithm(ScalingAlgorithm algorithm)
        {
            if (ResizeGammaMagnifier != GammaConversion.Value1_0 && isIndividuallyImplemented(algorithm) &&
                !isIndividuallyImplemented(MagnifierScalingAlgorithm.ScaleDown))
            {
                MessageBox.Show(this, Message.LinearizeColorSpaceDescription.Replace("\\n", "\n"),
                    Message.Information, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            if (MagnifierScalingAlgorithm.ScaleDown != algorithm)
            {
                MagnifierScalingAlgorithm.ScaleDown = algorithm;
                if (!useAreaAverageWhenUpsizingToolStripMenuItem.Checked) // ルーペの「拡大には面積平均」を見る（以前は通常表示の項目を見ていた）
                    MagnifierScalingAlgorithm.ScaleUp = algorithm;
                //reloadForChangeFilter(forOnlyPostFilter: true);
                resetMagnifyingPower(magnifyingPower);
            }
        }

        private void defaultMagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.HighSpeed);
        }

        private void default0MagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.Default);
        }

        private void nearestNeighborMagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.NearestNeighbor);
        }

        private void areaAverageMagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.AreaAverage);
        }

        private void lanczos1MagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.Lanczos1);
        }

        private void lanczos2MagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.Lanczos2);
        }

        private void lanczos3MagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.Lanczos3);
        }

        private void lanczos4MagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.Lanczos4);
        }

        private void spline4MagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.Spline4);
        }

        private void spline16MagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.Spline16);
        }

        private void spline36MagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.Spline36);
        }

        private void spline64MagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMagnifierScalingAlgorithm(ScalingAlgorithm.Spline64);
        }

        private void linearizeColorSpaceforGamma22MagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (isIndividuallyImplemented(MagnifierScalingAlgorithm.ScaleDown))
            {
                if (ResizeGammaMagnifier == GammaConversion.Value2_2)
                {
                    ResizeGammaMagnifier = GammaConversion.Value1_0;
                }
                else
                {
                    ResizeGammaMagnifier = GammaConversion.Value2_2;
                    MessageBox.Show(this, Message.LinearizeColorSpaceDescription.Replace("\\n", "\n"),
                        Message.Information, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                if (useAreaAverageWhenUpsizingToolStripMenuItem.Checked) resetMagnifyingPower(magnifyingPower);
            }
            else
            {
                if (ResizeGammaMagnifier == GammaConversion.Value2_2)
                {
                    ResizeGammaMagnifier = GammaConversion.Value1_0;
                }
                else
                {
                    ResizeGammaMagnifier = GammaConversion.Value2_2;
                }
                resetMagnifyingPower(magnifyingPower);
            }
        }

        private void useAreaAverageWhenUpsizingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (useAreaAverageWhenUpsizingToolStripMenuItem.Checked)
            {
                useAreaAverageWhenUpsizingToolStripMenuItem.Checked = false;
                if (MagnifierScalingAlgorithm.ScaleUp != MagnifierScalingAlgorithm.ScaleDown)
                {
                    MagnifierScalingAlgorithm.ScaleUp = MagnifierScalingAlgorithm.ScaleDown;
                    resetMagnifyingPower(magnifyingPower);
                }
            }
            else
            {
                useAreaAverageWhenUpsizingToolStripMenuItem.Checked = true;
                if (MagnifierScalingAlgorithm.ScaleUp != ScalingAlgorithm.AreaAverage)
                {
                    MagnifierScalingAlgorithm.ScaleUp = ScalingAlgorithm.AreaAverage;
                    resetMagnifyingPower(magnifyingPower);
                }
            }
        }

        Point? normalStateLocation = null;

        //FormWindowState ViewerForm_LocationChanged_LastWindowState = FormWindowState.Normal;
        //Point? ViewerForm_LocationChanged_LastScreenOrigin = null;
        private void ViewerForm_LocationChanged(object sender, EventArgs e)
        {
            var ws = WindowState;

            if (ws == FormWindowState.Normal)
            {
                normalStateLocation = Location;
            }
        }

        private void alwaysAutoHideUIToolStripMenuItem_Click(object sender, EventArgs e)
        {
            toggleAlwaysHideUI();
        }

        private void toggleAlwaysHideUI()
        {
            AlwaysHideUI = !AlwaysHideUI;
            ShowViewSettingHint();
        }

        class ToolStripMenuItemWithPath : ToolStripMenuItem
        {
            public ToolStripMenuItemWithPath(string text, string sorPath, string targetLongPath) : base(text.Replace("&", "&&"))
            {
                SorPath = sorPath;
                TargetLongPath = targetLongPath;
            }
            public string SorPath;
            public string TargetLongPath;
        }

        private void virtualFoldersToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            var items = virtualFoldersToolStripMenuItem.DropDownItems;
            items.Clear();

            var config = new ColoredBookmarkConfig();

            var ex = config.GetLastException();
            if (ex != null)
            {
                MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var bookmarks = config.Bookmarks;
            var bookmarkColors = config.BookmarkColors;
            List<ToolStripItem> newItems = new List<ToolStripItem>();
            try
            {
                if (bookmarks != null && bookmarks.Length == bookmarkColors?.Length)
                {
                    var c = currentPage;
                    var ea = EntryLongPathArray;
                    string targetLongPath = null;
                    if (ea != null && c >= 0 && c < ea.Length)
                    {
                        var isEntity = loader?.Type == PackedImageLoader.PackType.Directory;
                        targetLongPath = ea[c];
                        if (targetLongPath == "") targetLongPath = null;
                        if (targetLongPath != null && isEntity)
                        {
                            targetLongPath = targetLongPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                        }
                    }

                    for (var i = 0; i < bookmarks.Length; i++)
                    {
                        var bookmark = bookmarks[i];
                        if (bookmark.SpecialRole == SimpleBookmarkSpecialRole.None)
                        {
                            if (bookmark.Location is string path && Path.GetExtension(path).ToLower() == ".sor" && File.Exists(path))
                            {
                                if (targetLongPath != null)
                                {
                                    //var tsmi = new ToolStripMenuItemWithPath(Message.AddTo1.Replace("$1", Path.GetFileName(path)), path, targetLongPath);
                                    var tsmi = new ToolStripMenuItemWithPath(Message.AddTo1.Replace("$1", bookmark.ToString()), path, targetLongPath);
                                    tsmi.Click += virtualFolderSorToolStripMenuItem_Click;
                                    tsmi.BackColor = bookmarkColors[i];
                                    newItems.Add(tsmi);
                                }
                                else
                                {
                                    var tsmi = new ToolStripMenuItem(Message.AddTo1.Replace("$1", Path.GetFileName(path)));
                                    tsmi.Enabled = false;
                                    newItems.Add(tsmi);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex2)
            {
                items.Clear();
#if DEBUG
                MessageBox.Show(this, ex2.ToString(), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                MessageBox.Show(this, ex2.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
                return;
            }

            if (newItems.Count > 0)
            {
                var newItemsArray = newItems.ToArray();
                ForeColorManager.Set(newItemsArray);
                items.AddRange(newItemsArray);
            }
            else
            {
                var item = new ToolStripMenuItem(Message.NoVirtualFoldersInBookmark);
                item.Enabled = false;
                items.Add(item);
            }

            ResetToolStripMenuItem(sender);
            SetMessageForwarderStop(true);
        }

        private static void ResetToolStripMenuItem(object sender)
        {
            var tsmi = (ToolStripMenuItem)sender;
            var dropDown = tsmi.DropDown;
            var maxSize = dropDown.MaximumSize;
            var owner = tsmi.Owner;
            var area = Screen.FromControl(owner).WorkingArea;
            var buttonBound = owner.RectangleToScreen(tsmi.Bounds);
            dropDown.MaximumSize = new Size(maxSize.Width, Math.Max(buttonBound.Height * 2 * SystemInformation.MouseWheelScrollLines, area.Bottom - (buttonBound.Bottom - 1)));
        }

        private void virtualFolderSorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItemWithPath;
            var sorPath = item.SorPath;
            var targetLongPath = item.TargetLongPath;
            var altPos = targetLongPath.IndexOf(Path.AltDirectorySeparatorChar);

            try
            {
                // 実体を登録するパターン

                // ページ指定は LongPath を使うかページ番号を使うか検討中
                // ページ番号を指定してサムネイルは変更しないのが最もシンプルな方法
                // サムネイルを変更する場合必然的にキャッシュの検討が必要になる
                // すなわち、キャッシュは諦めるか、特別な方法を使うか、複数キャッシュが格納できるように拡張するか
                // しかし一ファイル一キャッシュを崩すことによるデメリットは多大
                if (altPos >= 0) targetLongPath = targetLongPath.Substring(0, altPos);
                VirtualFolder.AddBookmarkData(sorPath, targetLongPath, page: -1, limitOfItemsCount: int.MaxValue, deleteLostPath: false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void optionToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            SetMessageForwarderStop(true);
        }

        private void readOnMemoryNoneToolStripMenuItem_Click(object sender, EventArgs e) => SetReadOnMemoryModeExceptLookAhead();

        private void SetReadOnMemoryModeNone() => changeReadOnMemoryMode(ReadOnMemoryMode.None);

        private void readOnMemoryExceptLookAheadToolStripMenuItem_Click(object sender, EventArgs e) => SetReadOnMemoryModeExceptLookAhead();

        private void SetReadOnMemoryModeExceptLookAhead() => changeReadOnMemoryMode(ReadOnMemoryMode.ExceptLookAhead);

        private void readOnMemoryAlwaysToolStripMenuItem_Click(object sender, EventArgs e) => SetReadOnMemoryModeAlways();

        private void SetReadOnMemoryModeAlways() => changeReadOnMemoryMode(ReadOnMemoryMode.Always);

        private void changeReadOnMemoryMode(ReadOnMemoryMode mode)
        {
            if (mode != ReadOnMemoryMode)
            {
                ReadOnMemoryMode = mode;
                ResetAndShowHintForChangeReadingMode();
            }
        }

        private void nonerecommendedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetArchivesInArchiveModeIgnore();
        }

        private void SetArchivesInArchiveModeIgnore()
        {
            changeArchivesInArchiveMode(ArchivesInArchiveMode.Ignore);
        }

        private void untilFoundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetArchivesInArchiveModeUntilFound2Level();
        }

        private void SetArchivesInArchiveModeUntilFound2Level()
        {
            changeArchivesInArchiveMode(ArchivesInArchiveMode.UntilFound2Level);
        }

        private void alwaysLoadArchivesInArchiveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetArchivesInArchiveModeAlways1Level();
        }

        private void SetArchivesInArchiveModeAlways1Level()
        {
            changeArchivesInArchiveMode(ArchivesInArchiveMode.Always1Level);
        }

        private void changeArchivesInArchiveMode(ArchivesInArchiveMode mode)
        {
            if (mode != ArchivesInArchiveMode)
            {
                ArchivesInArchiveMode = mode;
                ResetAndShowHintForChangeReadingMode();
            }
        }

        private void ResetAndShowHintForChangeReadingMode()
        {
            if (loader != null)
            {
                lock (loaderLock) loader.Dispose();
                loader = null;
            }
            if (!string.IsNullOrEmpty(currentArchiveFilePath))
            {
                var ea = EntryArray;
                var cp = currentPage;
                var path = null as string;
                if (cp >= 0 && ea != null && ea.Length > cp)
                {
                    var e = ea[cp];
                    if (e != null)
                    {
                        path = e.Path;
                        if (path == "") path = null;
                    }
                }
                OpenFile(currentArchiveFilePath, 0, currentSortModeDetails, path);
            }
            ShowViewSettingHint();
        }

        private void selectBackgroundColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SelectBackgroundColor();
        }

        private void SelectBackgroundColor()
        {
            using (var cd = new ColorDialog())
            {
                var cc = cd.CustomColors;
                var current = pbView.BackColor;
                cc[cc.Length - 1] = current.R | current.G << 8 | current.B << 16;
                cd.CustomColors = cc;

                cd.Color = current;
                cd.AllowFullOpen = true;
                cd.SolidColorOnly = false;
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    var newColor = cd.Color;
                    if (current.R != newColor.R || current.G != newColor.G || current.B != newColor.B)
                    {
                        pbView.BackColor = newColor;
                        ShowViewSettingHint();
                    }
                }
            }
        }

        private void forceFirstPageToBeSingleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleForceFirstPageToBeSingle();
        }

        private int loadedPagesSinceGc = 0; // 前回の GC からの読み込み完了の数（V1）

        private int lastPageLoadedTick;

        // メモリの確認は 0.5 秒ごと。タイマーは通常 100ms ごと（下の UI の処理のため）で、待機ビューアでは 500ms ごと（V6）
        private int tiMemoryReducer_Tick_LastReduce = Environment.TickCount;

        private void tiMemoryReducer_Tick(object sender, EventArgs e)
        {
            if (!ViewerForm_Shown_Called) return;

            var now = Environment.TickCount;
            // 並列に読み込むと（V1）、デコードの一時的なメモリ（WIC のネイティブメモリや取り出したバイト列）が GC が回るまで残り、
            // 読み込みが終わって落ち着いたあとのワーキングセットが 1 本のときの 1.5〜2 倍になった。
            // 読み込みが 0.5 秒途切れたら（まとめて読んだ 8 ページ以上のとき。それ未満でも 1.5 秒途切れたら）GC を 1 回走らせて返す。
            // UI スレッドを止めないように、ブロックしない（バックグラウンドの）フル GC にする（V31。以前は 8 ページ未満だと走らず WS が下がらなかった）
            var sinceLoad = unchecked(now - lastPageLoadedTick);
            if (loadedPagesSinceGc > 0 && sinceLoad > (loadedPagesSinceGc >= 8 ? 500 : 1500))
            {
                loadedPagesSinceGc = 0;
                GC.Collect(2, GCCollectionMode.Forced, blocking: false);
            }
            // 前面になく読み込みも落ち着いているときは 500ms 間隔に（待機中は IPC の待ち受けの側で 500ms にしている）（V31）
            if (!tmWatchIpc_Waiting)
            {
                var interval = Form.ActiveForm != this && sinceLoad > 2000 ? 500 : 100;
                if (tmMemoryReducer.Interval != interval) tmMemoryReducer.Interval = interval;
            }
            var elapsed = unchecked(now - tiMemoryReducer_Tick_LastReduce);
            if (elapsed >= 450 || elapsed < 0)
            {
                tiMemoryReducer_Tick_LastReduce = now;
                SetMemoryUBound();
                ReduceUsingMemory(usedMemoryUBound);
            }

            // ToolStripTextBox の MouseEnter の隙間を埋める
            hideUnderUIForMouseLeave(sender, e);
        }

        private void createViewerShortcutFromCurrentSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CreateViewerShortcutFromCurrentSettings();
        }

        private void CreateViewerShortcutFromCurrentSettings()
        {
            string currentPath;
            try
            {
                var ld = loader;
                if (ld == null)
                {
                    currentPath = null;
                }
                else
                {
                    currentPath = currentArchiveFilePath;
                    if (!string.IsNullOrEmpty(currentPath) && ld.Type != PackedImageLoader.PackType.Directory)
                    {
                        currentPath = Path.GetDirectoryName(currentPath);
                    }
                }
            }
            catch
            {
                currentPath = null;
            }
            
            using (var makeShortcutForm = new ViewerFormMakeShortcutDialog(CreateCommandLineOptionInfo(), currentPath))
            {
                makeShortcutForm.ShowDialog(this);
            }
        }

        CommandLineOptionInfo CreateCommandLineOptionInfo() => new CommandLineOptionInfo
        {
            AlwaysHideUI = AlwaysHideUI,
            ArchivesInArchiveMode = ArchivesInArchiveMode,
            BackColor = pbView.BackColor,
            CoverBindingMode = CoverBindingMode,
            DefaultBindingMode = BindingMode,
            ReadOnMemoryMode = ReadOnMemoryMode,
            OpenInPreviousImageFilterSetting = false,
            InitialFullscreenMode = InitialFullscreenMode.Default
        };

        private void mtbPage_VisibleChanged(object sender, EventArgs e)
        {
            if (mtbPage.Visible)
            {
                mtbPage.ValueCanChangedByMouseButton = false;
            }
        }

        private void mtbPage_MouseDown(object sender, MouseEventArgs e)
        {
            mtbPage.ValueCanChangedByMouseButton = true;
            if (e.Button == MouseButtons.Left) mtbPage_Dragging = true; // V24
        }

        private void btnOpenLeftRight_MouseMove(object sender, MouseEventArgs e)
        {
            mtbPage.ValueCanChangedByMouseButton = true;
        }

        private void showAHintWhenChangingSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ViewSettingHintMode = ViewSettingHintMode == ViewSettingHintMode.ShowWhenChangingSettings ? ViewSettingHintMode.NeverShow : ViewSettingHintMode.ShowWhenChangingSettings;
        }

        private void ToggleForceFirstPageToBeSingle()
        {
            CoverBindingMode = CoverBindingMode == CoverBindingMode.Default ? CoverBindingMode.ForceSingle : CoverBindingMode.Default;

            if (currentPage == 0)
            {
                if (CoverBindingMode == CoverBindingMode.ForceSingle)
                {
                    movePageToMinimulBack();
                }
                else
                {
                    onePageModeForNext = false;
                    showCurrentPage();
                }
            }

            ShowViewSettingHint();
        }

        private void virtualFoldersToolStripMenuItem_DropDownOpened(object sender, EventArgs e)
        {
            ContextMenuForMenuStrip_Opend = true;
            fixDropDownLocation(sender);
        }

        private void virtualFoldersToolStripMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            ContextMenuForMenuStripPostProcessing2();
        }
    }
}
