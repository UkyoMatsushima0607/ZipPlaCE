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
        bool ContextMenuForMenuStrip_Opend = false;

        private void ContextMenuForMenuStripPostProcessing2()
        {
            if (MouseButtons == MouseButtons.Left && pbView.ClientRectangle.Contains(pbView.PointToClient(Cursor.Position)))
            {
                ksViewForm.IgnoreAnyMouseEventOnce = MouseButtons.Left;
            }

            if (menuStrip.Visible && menuStrip_Bottom == 0 && HideUI)
            {
                if (!menuStrip.ClientRectangle.Contains(menuStrip.PointToClient(Cursor.Position)))
                {
                    menuStrip_Bottom = menuStrip.Bounds.Bottom;
                    menuStrip.Visible = false;
                }

                // ESC の機能の発動を抑制しつつ
                // ESC で閉じたときに MouseMove が動かなくなる問題を解消
                if (KeyboardShortcut.GetKeyState(Keys.Escape))
                {
                    KeyboardShortcut.SendEscapeKey();
                }
            }
            ContextMenuForMenuStrip_Opend = false;

            if (DrawMeasure) pbPaintDrawOnlyMeasure();
            
            SetMessageForwarderStop(false);
        }

        private void startToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            var c = currentPage;
            var ea = EntryLongPathArray;
            if (ea != null && c >= 0 && c < ea.Length)
            {
                openNewThumbnailWindowToolStripMenuItem.Text = Message.SelectCurrentImageInThumbnailWindow;
            }
            else
            {
                openNewThumbnailWindowToolStripMenuItem.Text = Message.OpenThumbnailWindow;
            }
            
            var fullscreen = FullScreen;

            // 有効無効で表現
            //fullScreenInStartToolStripMenuItem.Enabled = !fullscreen;
            //windowToolStripMenuItem.Enabled = fullscreen;

            // アイコンで表現
            fullScreenInStartToolStripMenuItem.Checked = fullscreen;
            windowToolStripMenuItem.Checked = !fullscreen;

            SetMessageForwarderStop(true);
        }

        private void startToolStripMenuItem_DropDownOpened(object sender, EventArgs e)
        {
            ContextMenuForMenuStrip_Opend = true;
            fixDropDownLocation(sender);
        }

        private void startToolStripMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            ContextMenuForMenuStripPostProcessing2();
        }

        private void viewToolStripMenuItem_DropDownOpened(object sender, EventArgs e)
        {
            ContextMenuForMenuStrip_Opend = true;
            fixDropDownLocation(sender);
        }

        private void viewToolStripMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            ContextMenuForMenuStripPostProcessing2();
        }

        private ToolStripMenuItem getParcentageToolStripMenuItem(int i)
        {
            switch(i)
            {
                case 0: return percentage000YoolStripMenuItem;
                case 1: return percentage010YoolStripMenuItem;
                case 2: return percentage020YoolStripMenuItem;
                case 3: return percentage030YoolStripMenuItem;
                case 4: return percentage040YoolStripMenuItem;
                case 5: return percentage050YoolStripMenuItem;
                case 6: return percentage060YoolStripMenuItem;
                case 7: return percentage070YoolStripMenuItem;
                case 8: return percentage080YoolStripMenuItem;
                case 9: return percentage090YoolStripMenuItem;
                case 10: return percentage100YoolStripMenuItem;
                default: throw new ArgumentOutOfRangeException("i");
            }
        }

        private void moveToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            var nextEnabled = isMovablePageNatural(1);
            var prevEnabled = isMovablePageNatural(-1);
            var l2r = BindingMode != BindingMode.RightToLeft;

            nextPageToolStripMenuItem.Enabled = nextEnabled;
            previousPageToolStripMenuItem.Enabled = prevEnabled;
            rightPageToolStripMenuItem.Enabled = l2r ? nextEnabled : prevEnabled;
            leftPageToolStripMenuItem.Enabled = l2r ? prevEnabled : nextEnabled;

            var next1Enabled = isMovablePageToMinimulForward(); // isMovablePage(1);
            var prev1Enabled = isMovablePageToMinimulBack(); // isMovablePage(-1);

            moveForward1PageToolStripMenuItem.Enabled = next1Enabled;
            moveBack1PageToolStripMenuItem.Enabled = prev1Enabled;
            moveRight1PageToolStripMenuItem.Enabled = l2r ? next1Enabled : prev1Enabled;
            moveLeft1PageToolStripMenuItem.Enabled = l2r ? prev1Enabled : next1Enabled;

            var openNeighborEnabled = getCurrentParent() != null && !ExitAfterCoverSetting;
            openNextToolStripMenuItem.Enabled = openNeighborEnabled;
            openPreviousToolStripMenuItem.Enabled = openNeighborEnabled;
            openRightToolStripMenuItem.Enabled = openNeighborEnabled;
            openLeftToolStripMenuItem.Enabled = openNeighborEnabled;

            var currentIndex = (int)Math.Round(10*getRatioByPage()); // ページ数が 10 よりも十分に大きい場合に高い妥当性
            var enabledExists = false;
            for (var i = 0; i <= 10; i++)
            {
                var item = getParcentageToolStripMenuItem(i);
                var enabled = isMovablePageByRatio(i / 10.0);
                item.Enabled = enabled;
                enabledExists = enabledExists || enabled;
                item.Checked = i == currentIndex;
            }

            // ショートカットキー確認のため無効化しない
            // ratioToolStripMenuItem.Enabled = enabledExists;
            
            SetMessageForwarderStop(true);
        }

        private string getCurrentParent()
        {
            if (string.IsNullOrEmpty(currentArchiveFilePath)) return null;
            try
            {
                return Path.GetDirectoryName(currentArchiveFilePath);
            }
            catch
            {
            }
            return null;
        }

        private void moveToolStripMenuItem_DropDownOpened(object sender, EventArgs e)
        {
            ContextMenuForMenuStrip_Opend = true;
            fixDropDownLocation(sender);
        }

        private void moveToolStripMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            ContextMenuForMenuStripPostProcessing2();
        }

        private void slideshowToolStripMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            ContextMenuForMenuStripPostProcessing2();
        }

        private void slideshowToolStripMenuItem_DropDownOpened(object sender, EventArgs e)
        {
            ContextMenuForMenuStrip_Opend = true;
            fixDropDownLocation(sender);
        }

        private void slideshowToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            var opend = PreFilteredImageArray != null;
            var enabled = opend && getSlideshowEnabled();
            startSlideshowToolStripMenuItem.Enabled = opend && !enabled;
            stopSlideshowToolStripMenuItem.Enabled = enabled;
            toggleSlideshowToolStripMenuItem.Enabled = opend;

            _1SecIntervalsToolStripMenuItem.Checked = tmSlideshow.Interval == 1 * 1000;
            _2SecIntervalsToolStripMenuItem.Checked = tmSlideshow.Interval == 2 * 1000;
            _3SecIntervalsToolStripMenuItem.Checked = tmSlideshow.Interval == 3 * 1000;
            _5SecIntervalsToolStripMenuItem.Checked = tmSlideshow.Interval == 5 * 1000;
            _10SecIntervalsToolStripMenuItem.Checked = tmSlideshow.Interval == 10 * 1000;
            _20SecIntervalsToolStripMenuItem.Checked = tmSlideshow.Interval == 20 * 1000;
            _30SecIntervalsToolStripMenuItem.Checked = tmSlideshow.Interval == 30 * 1000;
            _60SecIntervalsToolStripMenuItem.Checked = tmSlideshow.Interval == 60 * 1000;

            repeatToolStripMenuItem.Checked = SlideshowRepeat;
            openNextOnTerminalToolStripMenuItem.Checked = SlideshowGlobal;

            SetMessageForwarderStop(true);
        }

        private CheckState[] editToolStripMenuItem_TagInitialCheckState;

        private bool editToolStripMenuItem_newTagExists;

        private bool editToolStripMenuItem_pathHasPageDirectionInfo; 

        private bool editToolStripMenuItem_pathHasThumbnailInfo;

        private bool editToolStripMenuItem_initialTagEmpty;

        private void editToolStripMenuItem_DropDownOpening(object sender, EventArgs e) { editToolStripMenuItem_DropDownOpening(sender, e, null); }

        private void editToolStripMenuItem_DropDownOpening(object sender, EventArgs e, ContextMenuStrip contextMenuStrip)
        {
            var items = editToolStripMenuItem.DropDownItems;
            var tagStartIndex = items.IndexOf(uncheckAllToolStripMenuItem) + 1;
            var i = tagStartIndex;
            while (true)
            {
                var item = items[i];
                if (item == tagEditorToolStripMenuItem) break;
                items.RemoveAt(i);
                item.Dispose(); // 開くたびに作り直すタグの項目（X19）
            }

            if(ViewerMode == ViewerModeEnum.CoverSetting)
            {
                setThumbnailManuallyToolStripMenuItem.Text = Message.AbortSettingCover;
                setThumbnailManuallyToolStripMenuItem.ShortcutKeyDisplayString = Message.RightClick;
            }
            else
            {
                setThumbnailManuallyToolStripMenuItem.Text = Message.SetCoverManually;
                setThumbnailManuallyToolStripMenuItem.ShortcutKeyDisplayString = null;
            }

            var currentTarget = getCurrentRenameTarget();
            var currentExists = !string.IsNullOrEmpty(currentTarget);
            var thumbnailSetting = ViewerMode == ViewerModeEnum.CoverSetting;
            if (!currentExists || thumbnailSetting)
            {
                noneToolStripMenuItem.Enabled = false;
                rating1ToolStripMenuItem.Enabled = false;
                rating2ToolStripMenuItem.Enabled = false;
                rating3ToolStripMenuItem.Enabled = false;
                rating4ToolStripMenuItem.Enabled = false;
                rating5ToolStripMenuItem.Enabled = false;
                noneToolStripMenuItem.Checked = true;
                rating1ToolStripMenuItem.Checked = false;
                rating2ToolStripMenuItem.Checked = false;
                rating3ToolStripMenuItem.Checked = false;
                rating4ToolStripMenuItem.Checked = false;
                rating5ToolStripMenuItem.Checked = false;

                uncheckAllToolStripMenuItem.Enabled = false;
                tagEditorToolStripMenuItem.Enabled = false;
                addTagsFromTheFileNameToolStripMenuItem.Enabled = false;

                clearPageSequenceSettingToolStripMenuItem.Enabled = false;
                saveCurrentPageSequenceToolStripMenuItem.Enabled = false;
                clearThumbnailSettingToolStripMenuItem.Enabled = false;

                if (currentExists)
                {
                    setThumbnailManuallyToolStripMenuItem.Enabled = true;

                    ZipPlaInfo zipPlaInfo;
                    zipPlaInfo = new ZipPlaInfo(currentTarget);

                    ToolStripMenuItem[] partialItems;
                    CheckState[] initialCheckStates;
                    var tags = (new ZipTagConfig()).Tags;
                    if (contextMenuStrip == null)
                    {
                        Program.SetTagsToToolStripMenuItems(editToolStripMenuItem, out partialItems, out initialCheckStates, tagStartIndex, tags, zipPlaInfo);
                    }
                    else
                    {
                        Program.SetTagsToToolStripMenuItems(editToolStripMenuItem, contextMenuStrip, out partialItems, out initialCheckStates, tagStartIndex, tags, zipPlaInfo);
                    }
                    foreach (var item in partialItems) item.Enabled = false;
                }
                else
                {
                    setThumbnailManuallyToolStripMenuItem.Enabled = false;
                }
            }
            else
            {
                ZipPlaInfo zipPlaInfo;
                zipPlaInfo = new ZipPlaInfo(currentTarget);
                noneToolStripMenuItem.Enabled = true;
                rating1ToolStripMenuItem.Enabled = true;
                rating2ToolStripMenuItem.Enabled = true;
                rating3ToolStripMenuItem.Enabled = true;
                rating4ToolStripMenuItem.Enabled = true;
                rating5ToolStripMenuItem.Enabled = true;
                noneToolStripMenuItem.Checked = zipPlaInfo.Rating != 1 && zipPlaInfo.Rating != 2 && zipPlaInfo.Rating != 3 && zipPlaInfo.Rating != 4 && zipPlaInfo.Rating != 5;
                rating1ToolStripMenuItem.Checked = zipPlaInfo.Rating == 1;
                rating2ToolStripMenuItem.Checked = zipPlaInfo.Rating == 2;
                rating3ToolStripMenuItem.Checked = zipPlaInfo.Rating == 3;
                rating4ToolStripMenuItem.Checked = zipPlaInfo.Rating == 4;
                rating5ToolStripMenuItem.Checked = zipPlaInfo.Rating == 5;
                
                ToolStripMenuItem[] partialItems;
                CheckState[] initialCheckStates;
                var tags = (new ZipTagConfig()).Tags;
                if (contextMenuStrip == null)
                {
                    Program.SetTagsToToolStripMenuItems(editToolStripMenuItem, out partialItems, out initialCheckStates, tagStartIndex, tags, zipPlaInfo);
                }
                else
                {
                    Program.SetTagsToToolStripMenuItems(editToolStripMenuItem, contextMenuStrip, out partialItems, out initialCheckStates, tagStartIndex, tags, zipPlaInfo);
                }

                editToolStripMenuItem_TagInitialCheckState = initialCheckStates;

                if (partialItems.Length > 0)
                {
                    partialItems[0].ShortcutKeyDisplayString = Message.RightClickNotToClose;
                    if(partialItems.Length > 1)
                    {
                        partialItems[1].ShortcutKeyDisplayString = Message.RightFlickIsAlsoOk;
                        if (partialItems.Length > 2)
                        {
                            partialItems[2].ShortcutKeyDisplayString = Message.SameAsAbove;
                        }
                    }
                }

                EventHandler eh1 = (sender2, e2) =>
                {
                    if (unckeckAllTags_Changing) return;
                    uncheckAllToolStripMenuItem.Enabled = partialItems.Any(it => it.CheckState != CheckState.Unchecked);
                    (sender2 as ToolStripMenuItem).Invalidate();
                };

                editToolStripMenuItem_newTagExists = getNewTagsSet(zipPlaInfo).Length > 0;
                editToolStripMenuItem_pathHasPageDirectionInfo = zipPlaInfo.BindingModeForSet != null;
                editToolStripMenuItem_pathHasThumbnailInfo = zipPlaInfo.ThumbnailInfo != null;
                editToolStripMenuItem_initialTagEmpty = !initialCheckStates.Any(item => item != CheckState.Unchecked);
                
                uncheckAllToolStripMenuItem.Enabled = !editToolStripMenuItem_initialTagEmpty;
                tagEditorToolStripMenuItem.Enabled = true;

                var isBook = currentTarget == currentArchiveFilePath;
                var pageSequensCanBeSaved = isBook && NoScaling(BindingMode);
                //var isBookAndNormal = isBook && ViewerMode == ViewerModeEnum.Normal;

                var itemsForEventHandler = contextMenuStrip == null ? editToolStripMenuItem.DropDownItems : contextMenuStrip.Items;

                EventHandler eh2 = (sender2, e2) =>
                {
                    if (unckeckAllTags_Changing) return;
                    if (getTagChanged(itemsForEventHandler.Count > 0 ? itemsForEventHandler : items)) // 外側が閉じた後に呼び出されることがあり、その場合 items の方に入っている
                    {
                        noneToolStripMenuItem.Enabled = false;
                        rating1ToolStripMenuItem.Enabled = false;
                        rating2ToolStripMenuItem.Enabled = false;
                        rating3ToolStripMenuItem.Enabled = false;
                        rating4ToolStripMenuItem.Enabled = false;
                        rating5ToolStripMenuItem.Enabled = false;

                        tagEditorToolStripMenuItem.Enabled = false;
                        addTagsFromTheFileNameToolStripMenuItem.Enabled = false;

                        clearPageSequenceSettingToolStripMenuItem.Enabled = false;
                        saveCurrentPageSequenceToolStripMenuItem.Enabled = false;
                        clearThumbnailSettingToolStripMenuItem.Enabled = false;
                        setThumbnailManuallyToolStripMenuItem.Enabled = false;
                    }
                    else
                    {
                        noneToolStripMenuItem.Enabled = true;
                        rating1ToolStripMenuItem.Enabled = true;
                        rating2ToolStripMenuItem.Enabled = true;
                        rating3ToolStripMenuItem.Enabled = true;
                        rating4ToolStripMenuItem.Enabled = true;
                        rating5ToolStripMenuItem.Enabled = true;

                        tagEditorToolStripMenuItem.Enabled = true;
                        addTagsFromTheFileNameToolStripMenuItem.Enabled = editToolStripMenuItem_newTagExists;

                        clearPageSequenceSettingToolStripMenuItem.Enabled = editToolStripMenuItem_pathHasPageDirectionInfo;
                        saveCurrentPageSequenceToolStripMenuItem.Enabled = pageSequensCanBeSaved;
                        clearThumbnailSettingToolStripMenuItem.Enabled = editToolStripMenuItem_pathHasThumbnailInfo;
                        //setThumbnailManuallyToolStripMenuItem.Enabled = isBookAndNormal;
                        setThumbnailManuallyToolStripMenuItem.Enabled = isBook;
                    }
                };

                foreach (var item in partialItems)
                {
                    //item.MouseUp += eh1;
                    //item.MouseUp += eh2;
                    item.CheckStateChanged += eh1;
                    item.CheckStateChanged += eh2;
                }

                //tagEditorToolStripMenuItem.Enabled = true;


                addTagsFromTheFileNameToolStripMenuItem.Enabled = editToolStripMenuItem_newTagExists;

                clearPageSequenceSettingToolStripMenuItem.Enabled = editToolStripMenuItem_pathHasPageDirectionInfo;
                saveCurrentPageSequenceToolStripMenuItem.Enabled = pageSequensCanBeSaved;
                clearThumbnailSettingToolStripMenuItem.Enabled = editToolStripMenuItem_pathHasThumbnailInfo;
                //setThumbnailManuallyToolStripMenuItem.Enabled = isBookAndNormal;
                setThumbnailManuallyToolStripMenuItem.Enabled = isBook;
            }

            ResetToolStripMenuItem(sender);
            SetMessageForwarderStop(true);
        }

        private bool getTagChanged(ToolStripItemCollection items)
        {
            if (editToolStripMenuItem_TagInitialCheckState == null) return false;
            //var items = editToolStripMenuItem.DropDownItems;
            var tagStartIndex = items.IndexOf(uncheckAllToolStripMenuItem) + 1;
            var i = tagStartIndex;
            var partialItems = GetTagToolStripMenuItems(items);
            var count = partialItems.Length;
            if (editToolStripMenuItem_TagInitialCheckState.Length != count) return false;
            for(var j = 0; j < count; j++)
            {
                if (editToolStripMenuItem_TagInitialCheckState[j] != partialItems[j].CheckState) return true;
            }
            return false;
        }

        private PrefixEscapedToolStripMenuItem[] GetTagToolStripMenuItems(ToolStripItemCollection items)
        {
            var tagStartIndex = items.IndexOf(uncheckAllToolStripMenuItem) + 1;
            var i = tagStartIndex;
            var partialItems = new List<PrefixEscapedToolStripMenuItem>();
            while (true)
            {
                var item = items[i++] as PrefixEscapedToolStripMenuItem;
                if (item == null) break;
                partialItems.Add(item);
            }
            return partialItems.ToArray(); ;
        }

        private void optionToolStripMenuItem_DropDownOpened(object sender, EventArgs e)
        {
            ContextMenuForMenuStrip_Opend = true;
            fixDropDownLocation(sender);
        }

        private void optionToolStripMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            ContextMenuForMenuStripPostProcessing2();
        }

        private void tagEditorToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (Program.ShowTagEditor(this))
            {
                RequestToUpdateTags();
            }
        }

        private void preferenceToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            var pre = BuiltInViewerMemoryLimit;
            Program.ShowSetting(this, out BuiltInViewerMemoryLimit);
            if (pre > BuiltInViewerMemoryLimit)
            {
                SetMemoryUBound();
                ReduceUsingMemory(usedMemoryUBound);
            }
        }

        private void editToolStripMenuItem_DropDownOpened(object sender, EventArgs e)
        {
            ContextMenuForMenuStrip_Opend = true;
            fixDropDownLocation(sender);
        }

        private void editToolStripMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            ContextMenuForMenuStripPostProcessing2();
            editToolStripMenuItem_DropDownClosed_CommonPart(sender, e);
        }

        private void editToolStripMenuItem_DropDownClosed_CommonPart(object sender, EventArgs e)
        {
            if (getTagChanged(editToolStripMenuItem.DropDownItems))
            {
                Task.Run(() => Invoke((MethodInvoker)(() => SetTagToFileName())));
            }
        }

        private void SetTagToFileName()
        {
            //if (string.IsNullOrEmpty(currentArchiveFilePath)) return;

            var prevArchiveFilePath = getCurrentRenameTarget();
            if (string.IsNullOrEmpty(prevArchiveFilePath)) return;
            var zipPlaInfo = new ZipPlaInfo(prevArchiveFilePath);
            
            var checkedItems = GetTagToolStripMenuItems(editToolStripMenuItem.DropDownItems);
            var tagCount = checkedItems.Length;
            var tags = zipPlaInfo.TagArray?.ToList();
            if (tags == null) tags = new List<string>();
            for (var j = 0; j < tagCount; j++)
            {
                var checkedItem = checkedItems[j];
                var state = checkedItem.CheckState;
                if (state != CheckState.Indeterminate)
                {
                    var tagName = checkedItem.PlainText;
                    if (tags.Contains(tagName))
                    {
                        if (state == CheckState.Unchecked)
                        {
                            tags.Remove(tagName);
                        }
                    }
                    else
                    {
                        if (state == CheckState.Checked)
                        {
                            tags.Add(tagName);
                        }
                    }
                }
            }
            zipPlaInfo.TagArray = tags.Count == 0 ? null : tags.ToArray();

            var newArchiveFilePath = zipPlaInfo.GetPathOfCurrentInfo(isDirSafety(prevArchiveFilePath));

            Program.ShowCursor();
            if (MessageForm.Show(this,
                Message.RenameForTags + "\r\n\r\n" +
                Message.BeforeRename + ":\r\n  " + Path.GetFileName(prevArchiveFilePath) + "\r\n" +
                Message.AfterRename + ":\r\n  " + Path.GetFileName(newArchiveFilePath) + "\r\n", Message.Question, Message.OK, Message.Cancel, MessageBoxIcon.Question)
                != 0)
            {
                return;
            }

            RenameCurrentArchiveAndReopen(prevArchiveFilePath, newArchiveFilePath);
        }

        private void noneToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetRatingToFileName(0);
        }

        private void rating1ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetRatingToFileName(1);
        }

        private void rating2ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetRatingToFileName(2);
        }

        private void rating3ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetRatingToFileName(3);
        }

        private void rating4ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetRatingToFileName(4);
        }

        private void rating5ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetRatingToFileName(5);
        }

        private string getCurrentRenameTarget()
        {
            if (currentArchiveFilePath == null) return null;
            if (currentStartingFilePath == currentArchiveFilePath) return currentArchiveFilePath;
            try
            {
                var loaderExists = loader != null;

                //if (Directory.Exists(currentArchiveFilePath))
                if (loaderExists ? loader.Type == PackedImageLoader.PackType.Directory : Directory.Exists(currentArchiveFilePath))
                {
                    var path = EntryLongPathArray[mtbPage.Value].Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    if (!File.Exists(path)) return null;
                    return path;
                }
                else if (loaderExists || File.Exists(currentArchiveFilePath))// if (File.Exists(currentArchiveFilePath))
                {
                    return currentArchiveFilePath;
                }
                else return null;
            }
            catch
            {
                return null;
            }

        }

        private void SetRatingToFileName(int rating)
        {
            //if (string.IsNullOrEmpty(currentArchiveFilePath)) return;

            var prevArchiveFilePath = getCurrentRenameTarget();
            if (preferenceToolStripMenuItem1 == null) return;
            var zipPlaInfo = new ZipPlaInfo(prevArchiveFilePath);
            zipPlaInfo.Rating = (1 <= rating && rating <= 5) ? rating : null as int?;
            var newArchiveFilePath = zipPlaInfo.GetPathOfCurrentInfo(isDirSafety(prevArchiveFilePath));

            Program.ShowCursor();
            if (MessageForm.Show(this,
                Message.RenameForRating + "\r\n\r\n" +
                Message.BeforeRename + ":\r\n  " + Path.GetFileName(prevArchiveFilePath) + "\r\n" +
                Message.AfterRename + ":\r\n  " + Path.GetFileName(newArchiveFilePath) + "\r\n", Message.Question, Message.OK, Message.Cancel, MessageBoxIcon.Question)
                != 0)
            {
                return;
            }

            RenameCurrentArchiveAndReopen(prevArchiveFilePath, newArchiveFilePath);
        }

        private bool unckeckAllTags_Changing = false;

        private void unckeckAllTags()
        {
            var dropDownItems = uncheckAllToolStripMenuItem.Owner.Items;
            //var dropDownItems = editToolStripMenuItem.DropDownItems;
            var i = dropDownItems.IndexOf(uncheckAllToolStripMenuItem);
            var count = dropDownItems.Count;
            unckeckAllTags_Changing = true;
            while (i < count)
            {
                var item = dropDownItems[i++] as ToolStripMenuItem;
                if (item == tagEditorToolStripMenuItem) break;
                item.Checked = false;
            }
            unckeckAllTags_Changing = false;

            if (!editToolStripMenuItem_initialTagEmpty)
            {
                noneToolStripMenuItem.Enabled = false;
                rating1ToolStripMenuItem.Enabled = false;
                rating2ToolStripMenuItem.Enabled = false;
                rating3ToolStripMenuItem.Enabled = false;
                rating4ToolStripMenuItem.Enabled = false;
                rating5ToolStripMenuItem.Enabled = false;

                tagEditorToolStripMenuItem.Enabled = false;
                addTagsFromTheFileNameToolStripMenuItem.Enabled = false;

                clearPageSequenceSettingToolStripMenuItem.Enabled = false;
                saveCurrentPageSequenceToolStripMenuItem.Enabled = false;
                clearThumbnailSettingToolStripMenuItem.Enabled = false;
                setThumbnailManuallyToolStripMenuItem.Enabled = false;
            }
            else
            {
                noneToolStripMenuItem.Enabled = true;
                rating1ToolStripMenuItem.Enabled = true;
                rating2ToolStripMenuItem.Enabled = true;
                rating3ToolStripMenuItem.Enabled = true;
                rating4ToolStripMenuItem.Enabled = true;
                rating5ToolStripMenuItem.Enabled = true;

                tagEditorToolStripMenuItem.Enabled = true;
                addTagsFromTheFileNameToolStripMenuItem.Enabled = editToolStripMenuItem_newTagExists;

                clearPageSequenceSettingToolStripMenuItem.Enabled = editToolStripMenuItem_pathHasPageDirectionInfo;
                saveCurrentPageSequenceToolStripMenuItem.Enabled = true;
                clearThumbnailSettingToolStripMenuItem.Enabled = editToolStripMenuItem_pathHasThumbnailInfo;
                setThumbnailManuallyToolStripMenuItem.Enabled = true;
            }
        }

        private void uncheckAllToolStripMenuItem_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                //editToolStripMenuItem.DropDown.AutoClose = false;
                (uncheckAllToolStripMenuItem.Owner as ToolStripDropDown).AutoClose = false;
            }
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Left)
            {
                unckeckAllTags();
            }
        }

        private void uncheckAllToolStripMenuItem_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                //unckeckAllTags();
                //editToolStripMenuItem.DropDown.AutoClose = true;
                (uncheckAllToolStripMenuItem.Owner as ToolStripDropDown).AutoClose = true;
            }
            //uncheckAllToolStripMenuItem.Enabled = false;
            DisableUncheckAllToolStripMenuItemIfNeeded();
        }

        private void uncheckAllToolStripMenuItem_MouseLeave(object sender, EventArgs e)
        {
            (uncheckAllToolStripMenuItem.Owner as ToolStripDropDown).AutoClose = true;
            DisableUncheckAllToolStripMenuItemIfNeeded();
        }

        private void DisableUncheckAllToolStripMenuItemIfNeeded()
        {
            if (!uncheckAllToolStripMenuItem.Enabled) return;
            var dropDownItems = (uncheckAllToolStripMenuItem.Owner as ToolStripDropDown)?.Items;
            if (dropDownItems == null) return;
            var start = dropDownItems.IndexOf(uncheckAllToolStripMenuItem) + 1;
            if (start <= 0) return;
            var stop = dropDownItems.IndexOf(tagEditorToolStripMenuItem);
            if (stop < 0) return;
            for (var i = start; i < stop; i++)
            {
                var item = dropDownItems[i] as ToolStripMenuItem;
                if (item == null || item.CheckState != CheckState.Unchecked) return;
            }
            uncheckAllToolStripMenuItem.Enabled = false;
        }

        private void tagEditorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.ShowTagEditor(this))
            {
                RequestToUpdateTags();
            }
        }

        private void addTagsFromTheFileNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (currentArchiveFilePath == null) return;
            var info = new ZipPlaInfo(currentArchiveFilePath);
            var newTagsSet = getNewTagsSet(info);
            if(newTagsSet != null && newTagsSet.Length > 0)
            {
                if (Program.ShowTagEditor(this, newTagsSet))
                {
                    RequestToUpdateTags();
                }
            }
        }

        private string[] getNewTagsSet(ZipPlaInfo info)
        {
            if (info == null || info.TagArray == null) return new string[0];
            var tagConfig = new ZipTagConfig();
            var tags = tagConfig.Tags;
            if (tags == null || tags.Length == 0) return new string[0];
            var newTagsSet = new HashSet<string>();
            var tagNames = (from tag in tags select tag.Name).ToArray();
            if (info.TagArray != null) foreach (var tag in info.TagArray) if (!tagNames.Contains(tag)) newTagsSet.Add(tag);
            return newTagsSet.ToArray();
        }

        private void clearPageSequenceSettingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetPageDirectionToFileName(true);
        }

        private void saveCurrentPageSequenceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetPageDirectionToFileName();
        }

        private void clearThumbnailSettingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetCoverForThumbnail(true);
        }

        private void setThumbnailManuallyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var vm = ViewerMode;
            if (vm != ViewerModeEnum.CoverSetting)
            {
                //if (vm == ViewerModeEnum.Normal)
                {
                    if (imageFilter.PreFilterExists() ||
                        (BindingMode != BindingMode.SinglePage && BindingMode != BindingMode.SinglePageWithoutScalingUp && MaxDivision > 1))
                    {
                        reloadForChangeFilter(forOnlyPostFilter: false);
                    }
                    else if (imageFilter.PostFilterExists())
                    {
                        reloadForChangeFilter(forOnlyPostFilter: true);
                    }

                    pbView_Paint_RectangleMessage = null;
                    ViewerMode = ViewerModeEnum.CoverSetting;
                    ksViewForm.Enabled = true;
                    pbView_Paint_Rectangle_FillAround = true;
                    ShwoCoverRectangle();
                }
            }
            else
            {
                ExitCoverSetting();
            }
        }

        // トラックバーの上でカーソルを動かすたびに SetBackgroundMode（全ページの走査・ソート・離れたページの破棄）を呼んでいた（V8）。
        // カーソルの下のページが変わらず、ほかで SetBackgroundMode も呼ばれていなければ何もしない。呼ぶのは 50ms に 1 回まで（最後の位置は必ず反映する）
        private const int mtbPage_MouseMove_Interval = 50;

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (
                KeyboardShortcut.GetKeyState((int)Keys.D) < 0 &&
                KeyboardShortcut.GetKeyState((int)Keys.E) < 0 &&
                KeyboardShortcut.GetKeyState((int)Keys.B) < 0 &&
                KeyboardShortcut.GetKeyState((int)Keys.U) < 0 &&
                KeyboardShortcut.GetKeyState((int)Keys.G) < 0)
            {
                var i = bmwLoadEachPage_DoWork_DEBUG_WorkNumber;
                var index = i >= 0 ? (i + 1).ToString() : "<EMPTY>";
                Program.ShowCursor();
                MessageBox.Show(this,
                    $"This is \"Debug information\" for the developer.\n" +
                    $"Loading index = {index}\n" +
                    $"Phase number = {bmwLoadEachPage_DoWork_DEBUG_Phase}\n" +
                    $"\n" +
                    $"Hold down '7' key and 'Z' key, and click \"OK\" to prioritize 7z.dll."
                    ,
                    "Debug information", MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (
                KeyboardShortcut.GetKeyState((int)Keys.D7) < 0 &&
                KeyboardShortcut.GetKeyState((int)Keys.Z) < 0)
                {
                    var sevenZipPath = PackedImageLoader.GetSevenZipPath();
                    if (sevenZipPath != null)
                    {
                        PackedImageLoader.PrioritizeSevenZip = true;
                        Program.ShowCursor();
                        MessageBox.Show(this,
                            $"Following dll will be preferentially used until the window is closed.\n" +
                            $"\n" +
                            $"{sevenZipPath}."
                            ,
                            "Debug information", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        var prevArchiveFilePath = getCurrentRenameTarget();
                        DisposeLoader(); // 読み込みジョブが取り出し中でないときに（V1）

                        OpenFile(prevArchiveFilePath, currentPage, currentSortModeDetails);
                    }
                    else
                    {
                        Program.ShowCursor();
                        MessageBox.Show(this,
                            $"7z.dll is not found."
                            ,
                            "Debug information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            else
            {
                Program.ShowAbout(this);
            }
        }

        private void openFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileWithDialog();
        }

        private void openFileWithDialog()
        {
            if (ExitAfterCoverSetting) return;
            var path = Program.ItsSelfOrExistingParentDirectory(currentArchiveFilePath);
            if (!string.IsNullOrEmpty(path))
            {
                openFileDialog.InitialDirectory = path;
            }
            openFileDialog.FileName = "";
            var supportedImageFileFilter = ImageLoader.GetSupportedFileFilter();
            PackedImageLoader.CheckSevenZipExistence(this);
            var supportedArchiveFileFilter = PackedImageLoader.GetSupportedArchiveFileFilter();
            openFileDialog.Filter = Message.AllSupportedFiles + "|" + supportedArchiveFileFilter + ";" + supportedImageFileFilter + "|"
                + Message.ArchiveFilesPdfFiles + "|" + supportedArchiveFileFilter + "|"
                + Message.ImageFiles + "|" + supportedImageFileFilter;

            if (openFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                removeHiddenConfigFromCatalogForm();
                OpenFile(openFileDialog.FileName);
            }
        }

        private void openNewThumbnailWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openThumbnailWindow();
        }

        private void switchToThumbnailWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            switchToThumbnailWindow();
        }

        private void switchToThumbnailWindow()
        {
            SaveSettings();
            openThumbnailWindow(cancelFullscreen: false);
            CloseWithoutSave();
        }

        private void openThumbnailWindow() { openThumbnailWindow(cancelFullscreen: true); }

        private void openThumbnailWindow(bool cancelFullscreen)
        {
            if (ExitAfterCoverSetting) return;
            try
            {
                var c = currentPage;
                var ea = EntryLongPathArray;
                if (ea != null && c >= 0 && c < ea.Length)
                {
                    if (currentSortModeDetails != null)
                    {
                        var sort = currentSortModeDetails.SortMode;
                        if (sort == SortMode.NameInAsc || sort == SortMode.NameInDesc)
                        {
                            System.Diagnostics.Process.Start(Application.ExecutablePath, $"-c \"{ea[c]}\" -{sort}");
                        }
                        else if(sort == SortMode.Random && !string.IsNullOrEmpty(currentSortModeDetails.RandomSeed))
                        {
                            System.Diagnostics.Process.Start(Application.ExecutablePath, $"-c \"{ea[c]}\" -{sort} \"{currentSortModeDetails.RandomSeed}\"");
                        }
                        else
                        {
                            System.Diagnostics.Process.Start(Application.ExecutablePath, $"-c \"{ea[c]}\" -{currentSortModeDetails.PreSortMode} -{sort}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Process.Start(Application.ExecutablePath, $"-c \"{ea[c]}\"");
                    }
                }
                else if (string.IsNullOrEmpty(currentArchiveFilePath))
                {
                    System.Diagnostics.Process.Start(Application.ExecutablePath, "-c");
                }
                else
                {
                    System.Diagnostics.Process.Start(Application.ExecutablePath, "-c \"" + currentArchiveFilePath + "\"");
                }
                if (cancelFullscreen && FullScreen)
                {
                    fullScreenModeChange(false);
                }
            }
            catch (Exception error)
            {
                Program.AlertError(error);
            }
        }

        private void cloneTheWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cloneWindow();
        }

        private void cloneWindow()
        {
            if (ExitAfterCoverSetting) return;
            try
            {
                var longPath = null as string;
                var value = mtbPage.Value;
                if (value >= 0)
                {
                    var elpa = EntryLongPathArray;
                    if (elpa != null && value < elpa.Length)
                    {
                        longPath = elpa[value];
                        if (longPath == "") longPath = null;
                    }
                }

                var info = CreateCommandLineOptionInfo();
                info.OpenInPreviousImageFilterSetting = true;
                info.InitialFullscreenMode = InitialFullscreenMode.Default;
                var option = GetCommandLineOption(info, minimize: true);
                if (longPath != null) option += $" \"{longPath}\"";

                SaveSettings();

                Process.Start(Application.ExecutablePath, option);
            }
            catch (Exception error)
            {
                Program.AlertError(error);
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void mouseGestureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Program.OpenMouseGestureSetting(Icon, mgView, getMouseGestureSettingTemplate(CommandSender.MouseGesture), this, widthZoom: 1.4);
        }

        private void keyboardShortcutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(Program.OpenKeyboardShortcutSetting(Icon, ksViewForm, getMouseGestureSettingTemplate(CommandSender.KeyboardShortcut), ContinuousExecutionCommands.Select(enm => (int)enm).ToArray(),
                ViewerFormConfig.DefaultKeyboardShortcutCommands.Select(d => Tuple.Create(d.GetShortcut().Select(s => new HashSet<Keys>(s)).ToArray(), (int)d.Command)).ToArray(),
                useLButton: true, useMButton: true, useRButton: true, useX1Button: true, useX2Button:true,
                useLButtonWarningMessage: Message.WhenClickOnAndClickConflictTheFormerTakesPrecedence,
                useMButtonWarningMessage: null, useRButtonWarningMessage: null, owner: this))
            {
                setShortcutKeyDisplayStrings(setKeyNames: false);
            }
        }
    }
}
