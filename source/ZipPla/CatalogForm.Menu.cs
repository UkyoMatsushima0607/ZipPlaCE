using Alteridem.WinTouch;
using Common;
using GenerarClasses;
using SharpCompress.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Permissions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WilsonProgramming;
using WindowsFormUtilities;

namespace ZipPla
{
    public partial class CatalogForm
    {
        private void setCoverOfParentFolderToThisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string[] paths;
            bool[] isDirs;
            if (CreatingZipPathArray) return;
            getSelectedPathArray(-1, out paths, out isDirs);
            if (paths == null || paths.Length != 1) return;
            var path = paths[0];
            var isDir = isDirs[0];

            try
            {
                var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
                string parent;
                if (altPos < 0) parent = Path.GetDirectoryName(path);
                else parent = Path.GetDirectoryName(path.Substring(0, altPos));

                var filePath = Path.Combine(parent, ZipPlaCoverImageFileWithExtension);



                if (isDir && altPos < 0)
                {
                    var oldFilePath = Path.Combine(path, ZipPlaCoverImageFileWithExtension);
                    File.Copy(oldFilePath, filePath);
                }
                else if (isZipPlaCoverImageType(path) && File.Exists(path) && new FileInfo(path).Length <= 5 * 1024 * 1024) // getFullSizeThumbnail 内での実験の結果より
                {
                    filePath = Path.ChangeExtension(filePath, Path.GetExtension(path));
                    File.Copy(path, filePath);
                }
                else
                {
                    using (var bmp = getFullSizeThumbnail(path)) bmp.Save(filePath);
                }

                File.SetAttributes(filePath, HideZipPlaCoverImageFile ? FileAttributes.Hidden : FileAttributes.Normal);

                if (thumbnailCache == GPSizeThumbnail.AlternateDataStream)
                {
                    GPSizeThumbnail.TryDeleteAlternateDataStream(parent);
                }
                else if (thumbnailCache != null)
                {
                    GPSizeThumbnail.TryDelete(thumbnailCache, parent);
                }

                if (undoBufferIndex > 0)
                {
                    var preloc = undoBuffer[undoBufferIndex - 1].Location + Path.DirectorySeparatorChar;
                    if (path.StartsWith(preloc) &&
                        parent.Length >= preloc.Length && parent.Substring(preloc.Length).ToLower() + Path.DirectorySeparatorChar == undoBuffer[undoBufferIndex - 1].SelectedFileName.ToLower())
                    {
                        btnGoToBack.PerformClick();
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                MessageBox.Show(this, ex.ToString(), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
            }
        }

        // 「GDI+ で一般的なエラーが発生しました」の回避
        private Bitmap bitmapRefresh(Bitmap bmp)
        {
            using (bmp) return new Bitmap(bmp);
        }

        private Bitmap getFullSizeThumbnail(string path)
        {
            const int maxWidth = 1280;
            const int maxHeight = 1280;
            var needToRefresh = false;
            Bitmap result = null;
            try
            {
                bool? ffmpegExists = null;
                if (ImageLoader.SupportsImageAtLeastThumbnailReading(path, ref ffmpegExists))
                {
                    ImageInfo info;
                    result = ImageLoader.GetAtLeastThumbnailBitmap(path, new Size(maxWidth, maxHeight), ref ffmpegExists, out info, out needToRefresh);
                }
                else if (PackedImageLoader.Supports(path))
                {
                    result = GetArchiveThumbnail(path, ratio: Math.Sqrt(0.5)); // このオーバーロードは例外を投げる
                }
                else if (MovieThumbnailLoader.Supports(path))
                {
                    MovieInfo dummy = null;
                    needToRefresh = true;
                    result = MovieThumbnailLoader.GetThumbnail(path, sizeProviderForOriginalSizeAsPossible, getpositionProvider(path), backgroundWorker: null, timeOut: TimeSpan.FromSeconds(10), movieInfo: ref dummy);
                }
                else
                {
                    var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
                    if (altPos < 0) throw new NotSupportedException();
                    var moviePath = path.Substring(0, altPos);

                    var innerName = path.Substring(altPos + 1);

                    var pos = TimeSpan.Parse(innerName);

                    MovieInfo dummy = null;
                    needToRefresh = true;
                    result = MovieThumbnailLoader.GetThumbnail(moviePath, sizeProviderForOriginalSizeAsPossible, t => pos, backgroundWorker: null, timeOut: TimeSpan.FromSeconds(10), movieInfo: ref dummy);
                }

                // ファイルサイズ確認
                // これで 4.8 MB 程度

                if (result.Width > maxWidth || result.Height > maxHeight)
                {
                    using (result)
                    {
                        var zoom = Math.Min((double)maxWidth / result.Width, (double)maxHeight / result.Height);
                        var width = (int)Math.Round(zoom * result.Width);
                        var height = (int)Math.Round(zoom * result.Height);

                        return BitmapResizer.CreateNew(result, width, height);
                    }
                }
                else
                {
                    return needToRefresh ? bitmapRefresh(result) : result;
                }
            }
            catch
            {
                result?.Dispose();
                throw;
            }
        }

        // 枠の太さのメニュー項目
        private void frameThicknessMenuItem_Click(object sender, EventArgs e)
        {
            setCurrentMinimumFrameThickness(sender as ToolStripMenuItem);
        }

        private void totallyUniformedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            currentAligning = ThumbViewerAligningMode.TotallyUniformedGrid;
        }

        private void horizontallyUniformedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            currentAligning = ThumbViewerAligningMode.HorizontallyUniformedGrid;
        }

        private void alignedLeftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            currentAligning = ThumbViewerAligningMode.LeftAlignedGrid;
        }

        private void alignedOnCenterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            currentAligning = ThumbViewerAligningMode.CenteredGrid;
        }

        private void defaultLeftToRightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender as ToolStripMenuItem).Checked)
            {
                currentBindingMode = bindingModeByControl = BindingMode.LeftToRight;
                if (SenseBindingMode()) UpdatePreview();
            }
        }

        private void defaultRightToLeftToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (!(sender as ToolStripMenuItem).Checked)
            {
                currentBindingMode = bindingModeByControl = BindingMode.RightToLeft;
                if (SenseBindingMode()) UpdatePreview();
            }
        }

        private void defaultSinglePageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender as ToolStripMenuItem).Checked)
            {
                var fromSingle = currentBindingMode == BindingMode.SinglePageWithoutScalingUp;

                currentBindingMode = bindingModeByControl = BindingMode.SinglePage;
                if (!fromSingle && SenseBindingMode()) UpdatePreview();
            }
        }

        private void singlePageWithoutScalingUpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender as ToolStripMenuItem).Checked)
            {
                var fromSingle = currentBindingMode == BindingMode.SinglePage;

                currentBindingMode = bindingModeByControl = BindingMode.SinglePageWithoutScalingUp;
                if (!fromSingle && SenseBindingMode()) UpdatePreview();
            }

        }

        private void thumbnailCacheToolStripMenuItem_Click(object sender, EventArgs e)
        {
            thumbnailToolStripMenuItem.DropDown.Close();
            if (CreatingZipPathArray) return;
            AllowFullpower = false;
            using (var thumbnailCacheSettingForm = new ThumbnailCacheSettingForm(thumbnailCache, currentProfileColor))
            {
                thumbnailCacheSettingForm.ShowDialog(this);
                thumbnailCache = thumbnailCacheSettingForm.CacheRoot;
            }
            AllowFullpower = true;
        }

        private void addSeparatorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            addSeparatorToBookmarkList(insertToSelected: true);
        }

        private void blackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BackColorOfBuiltInViewer = Color.Black;
        }

        private void grayToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BackColorOfBuiltInViewer = Color.Gray;
        }

        private void whiteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BackColorOfBuiltInViewer = Color.White;
        }

        private void selectColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AllowFullpower = false;

            using (var cd = new ColorDialog())
            {
                var cc = cd.CustomColors;
                var current = SelectedColorForBackColorOfBuiltInViewer;
                cc[cc.Length - 1] = current.R | current.G << 8 | current.B << 16;
                cd.CustomColors = cc;

                cd.Color = current;
                cd.AllowFullOpen = true;
                cd.SolidColorOnly = false;
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    BackColorOfBuiltInViewer = cd.Color;
                }
            }

            AllowFullpower = true;
        }

        private void changeMaxCaptchaCountByUser(int maxCaptcha)
        {
            changeMaxCaptchaCount(maxCaptcha, out var shouldBeUpdate);
            if (shouldBeUpdate) UpdatePreview();
        }

        private void changeMaxCaptchaCount(int maxCaptcha, out bool shouldBeUpdate)
        {
            shouldBeUpdate = false;

            if (MaxCaptchaCountInAVideo == maxCaptcha) return;

            if (!InMovie)
            {
                MaxCaptchaCountInAVideo = maxCaptcha;
                return;
            }

            int currentViewCount;
            var zpa = ZipPathArray;
            if (zpa == null)
            {
                MaxCaptchaCountInAVideo = maxCaptcha;
                return;
            }
            currentViewCount = zpa.Length;

            MovieInfo info;
            try
            {
                info = MovieInfo.FromFile(currentLocation);
            }
            catch
            {
                MaxCaptchaCountInAVideo = maxCaptcha;
                return;
            }

            int nextMovieLengthCount;
            if (maxCaptcha != 0 && currentViewCount >= maxCaptcha)
            {
                nextMovieLengthCount = currentViewCount;
            }
            else
            {
                var duration = info.Duration;
                if (duration == null)
                {
                    MaxCaptchaCountInAVideo = maxCaptcha;
                    return;
                }
                nextMovieLengthCount = (int)duration.TotalSeconds;
                if (nextMovieLengthCount == 0) nextMovieLengthCount = 1;
            }
            int nextBound;
            if (maxCaptcha > 0)
            {
                nextBound = maxCaptcha;
            }
            else
            {
                nextBound = GetThumbnailCountInTvCatalogOrFileList(info);
            }
            var nextViewCount = Math.Min(nextMovieLengthCount, nextBound);

            MaxCaptchaCountInAVideo = maxCaptcha;

            if (currentViewCount != nextViewCount) shouldBeUpdate = true;
        }

        private void fitOnTheScreenToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(0); }

        private void thumb6ToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(6); }

        private void thumb12ToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(12); }

        private void thumb24ToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(24); }

        private void thumb60ToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(60); }

        private void thumb120ToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(120); }

        private void thumb240ToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(240); }

        private void thumb600ToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(600); }

        private void thumb1200ToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(1200); }

        private void thumb2400ToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(2400); }

        private void everySecondsToolStripMenuItem_Click(object sender, EventArgs e) { changeMaxCaptchaCountByUser(int.MaxValue); }

        private void cmsRightClick_MouseDown(object sender, MouseEventArgs e)
        {
            cmsRightClick.Focus(); // テキストボックスに奪われたフォーカスを取り戻す
        }

        private void alwaysStartInWindowModeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BuiltInViewerInitialFullscreenMode = InitialFullscreenMode.ForceWindow;
        }

        private void startInPreviousFullscreenModeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BuiltInViewerInitialFullscreenMode = InitialFullscreenMode.Default;
        }

        private void openInFullscreenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BuiltInViewerInitialFullscreenMode = InitialFullscreenMode.ForceFullscreen;
        }

        private void forceFirstPageToBeSingleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CoverBindingMode = CoverBindingMode == CoverBindingMode.Default ? CoverBindingMode.ForceSingle : CoverBindingMode.Default;
        }

        private void hilightSelectedItemsWhenMouseLeavaesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SelectionHilightMode = SelectionHilightMode == SelectionHighlightMode.None ?
                SelectionHighlightMode.WhenMouseLeaves : SelectionHighlightMode.None;
        }

        private void tinyScrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMouseWheelScrollAmount(30, ThumbViewerScrollUnit.AbsolutePixel);
        }

        private void smallScrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMouseWheelScrollAmount(60, ThumbViewerScrollUnit.AbsolutePixel);
        }

        private void normalScrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMouseWheelScrollAmount(120, ThumbViewerScrollUnit.AbsolutePixel);
        }

        private void largeScrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMouseWheelScrollAmount(240, ThumbViewerScrollUnit.AbsolutePixel);
        }

        private void hugeScrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMouseWheelScrollAmount(480, ThumbViewerScrollUnit.AbsolutePixel);
        }

        private void _1lineScrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMouseWheelScrollAmount(1, ThumbViewerScrollUnit.GridCeiling);
        }

        private void _2lineScrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMouseWheelScrollAmount(2, ThumbViewerScrollUnit.GridCeiling);
        }

        private void _3lineScrollToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMouseWheelScrollAmount(3, ThumbViewerScrollUnit.GridCeiling);
        }

        private void setMouseWheelScrollAmount(int amount, ThumbViewerScrollUnit unit)
        {
            tvCatalog.MouseWheelScrollAmount = amount;
            tvCatalog.MouseWheelScrollUnit = unit;
            tinyScrollToolStripMenuItem.Checked = amount == 30 && unit == ThumbViewerScrollUnit.AbsolutePixel;
            smallScrollToolStripMenuItem.Checked = amount == 60 && unit == ThumbViewerScrollUnit.AbsolutePixel;
            normalScrollToolStripMenuItem.Checked = amount == 120 && unit == ThumbViewerScrollUnit.AbsolutePixel;
            largeScrollToolStripMenuItem.Checked = amount == 240 && unit == ThumbViewerScrollUnit.AbsolutePixel;
            hugeScrollToolStripMenuItem.Checked = amount == 480 && unit == ThumbViewerScrollUnit.AbsolutePixel;
            _1lineScrollToolStripMenuItem.Checked = amount == 1 && unit == ThumbViewerScrollUnit.GridCeiling;
            _2lineScrollToolStripMenuItem.Checked = amount == 2 && unit == ThumbViewerScrollUnit.GridCeiling;
            _3lineScrollToolStripMenuItem.Checked = amount == 3 && unit == ThumbViewerScrollUnit.GridCeiling;
        }

        private void startInPreviousImageFilterSettingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenInPreviousImageFilterSetting = !OpenInPreviousImageFilterSetting;
        }

        private void tinyOnscreenKeyboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TinyOnScreenKeyboardVisible = !TinyOnScreenKeyboardVisible;
        }

        private void ignoreArchivesInArchiveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            changeArchivesInArchiveMode(ArchivesInArchiveMode.Ignore);
        }

        private void onlyIfThereIsNoOtherImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            changeArchivesInArchiveMode(ArchivesInArchiveMode.IfNoOther1Level);
        }

        private void untilFoundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            changeArchivesInArchiveMode(ArchivesInArchiveMode.UntilFound2Level);
        }

        private void alwaysLoadArchivesInArchiveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            changeArchivesInArchiveMode(ArchivesInArchiveMode.Always1Level);
        }

        private void changeArchivesInArchiveMode(ArchivesInArchiveMode mode)
        {
            if (mode != ArchivesInArchiveMode)
            {
                ArchivesInArchiveMode = mode;
                if (InArchive || ArchiveLoadLevel != ItemLoadLevel.Ignore)
                {
                    UpdatePreview();
                }
            }
        }

        private void loadWholeArchiveIntoMemoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ReadOnMemoryMode != ReadOnMemoryMode.None)
            {
                ReadOnMemoryMode = ReadOnMemoryMode.None;
            }
            else if (exceptReadAheadProcessToolStripMenuItem.Checked)
            {
                ReadOnMemoryMode = ReadOnMemoryMode.ExceptLookAhead;
            }
            else
            {
                ReadOnMemoryMode = ReadOnMemoryMode.Always;
            }
        }

        private void exceptReadAheadProcessToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (exceptReadAheadProcessToolStripMenuItem.Checked)
            {
                ReadOnMemoryMode = ReadOnMemoryMode.Always;
            }
            else
            {
                ReadOnMemoryMode = ReadOnMemoryMode.ExceptLookAhead;
            }
        }

        private void standardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoubleClickFileOpenMode = DoubleClickMode.Standard;
        }

        private void ignoreMouseMovementToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoubleClickFileOpenMode = DoubleClickMode.IgnoreMouseMove;
        }

        private void selectAndClickItemToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoubleClickFileOpenMode = DoubleClickMode.SelectAndClick;
        }

        private void tvCatalog_DragAttempted(object sender, MouseEventArgs e)
        {
            var mk = simplifiedKeyBoard.GetModifierKeys(upHeldKeys: true); // mouseUp が後で呼び出されるが今は使用していない
            if ((e.Button & MouseButtons.Right) == MouseButtons.Right)
            {
                rightClickContextMenuPrepareAndShow(tvCatalog.PointToScreen(e.Location), mk);
            }
        }

        private void dgvFileList_DragAttempted(object sender, MouseEventArgs e)
        {
            var mk = simplifiedKeyBoard.GetModifierKeys(upHeldKeys: true); // mouseUp が後で呼び出されるが今は使用していない
            if ((e.Button & MouseButtons.Right) == MouseButtons.Right)
            {
                rightClickContextMenuPrepareAndShow(dgvFileList.PointToScreen(e.Location), mk);
            }
        }

        private bool dragOuting = false;

        private void DragReady(object sender, MouseEventArgs e)
        {
            try
            {
                DataObject dataObj;
                DragDropEffects effects;
                GetSelectedDataObject(out dataObj, out effects, forClipboard: false);
                if (dataObj != null)
                {
                    dragOuting = true;
                    bool isNormalDrag = false;
                    if (!(dataObj is DataObjectEx) && dataObj.GetDataPresent(DataFormats.FileDrop))
                    {
                        isNormalDrag = dataObj.GetData(DataFormats.FileDrop, false) is string[];
                    }
                    if (isNormalDrag)
                    {
                        StopLookAheadProcess();

                        // ドラッグは同期的なので不要
                        //dragGuid = new Guid();
                        //OpenedByExternalApplication_Dictionary[dragGuid] = files;
                    }

                    (sender as Control).DoDragDrop(dataObj, effects);

                    if (isNormalDrag)
                    {
                        PrepareLookAheadProcess();
                    }

                    dragOuting = false;
                }
                else
                {
                    dragOuting = true;
                    (sender as Control).DoDragDrop(new string[0], DragDropEffects.None);
                    dragOuting = false;
                }
            }
            catch
            {
            }
        }

        private void propertiesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenSelectedProperties();
        }

        private void OpenSelectedProperties()
        {
            var selectedIndex = tvCatalog.SelectedIndex;
            if (selectedIndex < 0 || ZipPathArray == null || ZipPathArray.Length <= selectedIndex)
            {
                return;
            }
            var path = ZipPathArray[selectedIndex];

            AllowFullpower = false;

            string newPath;
            using (var pform = new ArchivePropertiesForm(path, FileCountArray[selectedIndex], ArchivesInArchiveMode, ImageInfoArray[selectedIndex], MovieInfoArray[selectedIndex]))
            {
                pform.ShowDialog(this);
                newPath = pform.FilePath;
            }

            RenameShownArchive(selectedIndex, path, newPath);

            AllowFullpower = true;
        }

        private void startToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            try
            {
                openHistoryToolStripMenuItem.Enabled = File.Exists(Program.HistorySorPath);
            }
            catch
            {
                openHistoryToolStripMenuItem.Enabled = false;
            }
            newFolderToolStripMenuItem.Enabled = !CreatingZipPathArray && InRealDirectory;
            AllowFullpower = false;
        }

        private void startToolStripMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            // ショートカットキーで使用できるように
            openHistoryToolStripMenuItem.Enabled = true;
            newFolderToolStripMenuItem.Enabled = true;

            AllowFullpower = true;
        }

        // メニューを開いている間は全力の処理を控える
        private void menu_DropDownOpeningDisallowFullpower(object sender, EventArgs e)
        {
            AllowFullpower = false;
        }

        // メニューを閉じたら全力の処理を許す
        private void menu_DropDownClosedAllowFullpower(object sender, EventArgs e)
        {
            AllowFullpower = true;
        }

        private void cmsRightClick_Opening(object sender, CancelEventArgs e)
        {
            AllowFullpower = false;
            msSortBy.Stop = msFilter.Stop = msCatalog.Stop = msFileList.Stop = true;
        }

        private void cmsRightClick_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            renameForTextBoxRatingTagPageSequence(noop: cmsRightClick_Closing_CloseReason == ToolStripDropDownCloseReason.Keyboard);

            //msSortBy.Stop = msFilter.Stop = msCatalog.Stop = msFileList.Stop = false; // 直後の Opening と逆転することがあるので Closing で
            //AllowFullpower = true; // 直後の Opening と逆転することがあるので Closing で
        }

        // コンテキストメニューを開いている間は全力の処理を控える
        private void contextMenu_OpeningDisallowFullpower(object sender, CancelEventArgs e)
        {
            AllowFullpower = false;
        }

        private void cmsDirectoryList_Opening(object sender, CancelEventArgs e)
        {
            msDirectoryList.Stop = true;
            AllowFullpower = false;
        }

        private void cmsDirectoryList_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            msDirectoryList.Stop = false;
            AllowFullpower = true;
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AllowFullpower = false;
            Program.ShowAbout();
            AllowFullpower = true;
        }

        private void openDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AllowFullpower = false;

            var path = Program.ItsSelfOrExistingParentDirectory(zabLocation.Text);
            if (!string.IsNullOrEmpty(path))
            {
                folderBrowserDialog.SelectedPath = path;
            }
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
            {
                zabLocation.Text = folderBrowserDialog.SelectedPath;
                MakePreview();
            }

            AllowFullpower = true;
        }

        private void openFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AllowFullpower = false;

            var path = Program.ItsSelfOrExistingParentDirectory(zabLocation.Text);
            if (!string.IsNullOrEmpty(path))
            {
                openFileDialog.InitialDirectory = path;
            }
            openFileDialog.FileName = "";

            PackedImageLoader.CheckSevenZipExistence(this);
            var supportedArchiveFileFilter = PackedImageLoader.GetSupportedArchiveFileFilter();

            if (MovieThumbnailLoader.ffmpegExists())
            {
                openFileDialog.Filter =
                    Message.AllSupportedFiles + $"|*.sor;*.{SmartFolder.ExtensionWithoutPeriodInLower};" +
                        supportedArchiveFileFilter + ";" + MovieThumbnailLoader.SupportedVideoFileFilter + "|" +
                    Message.VirtualFolder + "|*.sor|" +
                    Message.SmartFolder + $"|*.{SmartFolder.ExtensionWithoutPeriodInLower}|" +
                    Message.ArchiveFilesPdfFiles + "|" + supportedArchiveFileFilter + "|" +
                    Message.VideoFiles + "|" + MovieThumbnailLoader.SupportedVideoFileFilter;
            }
            else
            {
                openFileDialog.Filter =
                    Message.AllSupportedFiles + $"|*.sor;*.{SmartFolder.ExtensionWithoutPeriodInLower};" + supportedArchiveFileFilter + "|" +
                    Message.VirtualFolder + "|*.sor|" +
                    Message.SmartFolder + $"|*.{SmartFolder.ExtensionWithoutPeriodInLower}|" +
                    Message.ArchiveFilesPdfFiles + "|" + supportedArchiveFileFilter + "|" +
                    Message.VideoFilesRequireFfmpeg + "|;"; // 最後の ; を省略するとすべてのファイルが表示されてしまう
            }

            if (openFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                zabLocation.Text = openFileDialog.FileName;
                MakePreview();
            }

            AllowFullpower = true;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void editLocationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbLocation.Focus();
            cbLocation.DroppedDown = true;
        }

        private void editFilterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbFilter.Focus();
            cbFilter.DroppedDown = true;
        }

        private void fileListToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            var ckd = fileListToolStripMenuItem.Checked;
            var ckd2 = thumbnailListToolStripMenuItem.Checked;
            var firstCkd = ckd && dgvFileList.FirstDisplayedScrollingRowIndex < 0;
            var temp = selectionSynchronizing;
            selectionSynchronizing = true;
            splRight.Visible = ckd && ckd2;
            pnlRight.Visible = ckd;
            selectionSynchronizing = temp;
            if (ckd)
            {
                dgvFileList.Focus();
            }

            if (ckd && ckd2)
            {
                pnlRight.Dock = getFileListDock();
            }

            if (firstCkd) // 現実装では分岐の必要なし
            {
                selectionSynchronizingFromThumbnailToFileList();
            }
            else
            {
                selectionSynchronizingFromThumbnailToFileList();
            }
            if (!ckd && !ckd2)
            {
                thumbnailListToolStripMenuItem.Checked = true;
            }
            else
            {
                setTagfilterLocationIfRatingFilterVisibleChanged();
            }
        }

        private void thumbnailListToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            var ckd = thumbnailListToolStripMenuItem.Checked;
            var ckd2 = fileListToolStripMenuItem.Checked;

            if (ckd) setFileListWidthWithoutSettingWidth(pnlRight_NormalWidth);

            pnlCenter.Visible = ckd;
            splRight.Visible = ckd && ckd2;


            if (ckd)
            {
                if (ckd2)
                {
                    pnlRight.Dock = getFileListDock();
                }
            }
            else
            {
                pnlRight.Dock = DockStyle.Fill;
            }


            if (WindowState != FormWindowState.Minimized) setMainPanelWidth();

            if (ckd)
            {
                tvCatalog.Focus();
            }

            if (!ckd && !ckd2)
            {
                fileListToolStripMenuItem.Checked = true;
            }
            else
            {
                setTagfilterLocationIfRatingFilterVisibleChanged();
            }
        }

        int pnlBookmarkNoFillHeight;

        private void bookmarkToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if (bookmarkToolStripMenuItem.Checked)
            {
                pnlBookmark.Visible = pnlLeft.Visible = splLeft.Visible = true;
                if (folderTreeToolStripMenuItem.Checked)
                {
                    pnlBookmark.Dock = DockStyle.Top;
                    splBookmark.Visible = true;
                    pnlBookmark.Height = pnlBookmarkNoFillHeight;
                }
                else
                {
                    splBookmark.Visible = false;
                    if (pnlBookmark.Dock != DockStyle.Fill)
                    {
                        pnlBookmarkNoFillHeight = pnlBookmark.Height;
                        pnlBookmark.Dock = DockStyle.Fill;
                    }
                }
                fixLeftSplitter();
                dgvDirectoryList.Focus();
            }
            else
            {
                pnlBookmark.Visible = splBookmark.Visible = false;
                pnlLeft.Visible = splLeft.Visible = folderTreeToolStripMenuItem.Checked;
            }
        }

        private void folderTreeToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            setFolderTreeVisible(silent: false);
            if (folderTreeToolStripMenuItem.Checked)
            {
                if (SynchroFromBarToTree)
                {
                    setToTree(currentLocation, CollapseOthersWhenSynchronizing);
                }
                explorerTreeView.TreeViewWnd.Focus();
            }
        }

        private void setFolderTreeVisible(bool silent)
        {
            if (folderTreeToolStripMenuItem.Checked)
            {
                pnlDirectoryTree.Visible = pnlLeft.Visible = splLeft.Visible = true;
                if (bookmarkToolStripMenuItem.Checked)
                {
                    pnlBookmark.Dock = DockStyle.Top;
                    splBookmark.Visible = true;
                    pnlBookmark.Height = pnlBookmarkNoFillHeight;
                }
                else
                {
                    splBookmark.Visible = false;
                }
                fixLeftSplitter();

                if (!explorerTreeView.Initialized)
                {
                    // MessageBox などでないと効果なし
                    //await Task.Run(() => Invoke((MethodInvoker)(() => explorerTreeView.Initialize())));

                    // 高速化前
                    //Application.DoEvents();
                    //explorerTreeView.Initialize(IsShown ? Message.NowLoading + "..." : null);

                    // 十分に高速化したので同期的で良い
                    explorerTreeView.Initialize(null);

                    //await Task.Delay(1);
                    //explorerTreeView.Initialize(); // await のコストを考えて一度しか呼び出さないが二回以上呼び出しても無視されるだけ
                }
            }
            else
            {
                pnlDirectoryTree.Visible = splBookmark.Visible = false;
                if (bookmarkToolStripMenuItem.Checked)
                {
                    if (pnlBookmark.Dock != DockStyle.Fill)
                    {
                        pnlBookmarkNoFillHeight = pnlBookmark.Height;
                        pnlBookmark.Dock = DockStyle.Fill;
                    }
                }
                else
                {
                    pnlLeft.Visible = splLeft.Visible = false;
                }
            }
        }

        private void pnlLeft_SizeChanged(object sender, EventArgs e)
        {
            fixLeftSplitter();
        }

        private void fixLeftSplitter()
        {
            if (WindowState != FormWindowState.Minimized && pnlBookmark.Visible &&
                pnlDirectoryTree.Visible && pnlDirectoryTree.Height < splBookmark.MinExtra)
            {
                pnlBookmark.Height = Math.Max(splBookmark.MinSize, pnlLeft.Height - splBookmark.MinExtra - splBookmark.Height);
            }
        }

        private void ratingHeaderRightClickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcRating.Visible = !tbcRating.Visible;
            setTagfilterLocationIfRatingFilterVisibleChanged();
        }

        private void iconToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcIcon.Visible = !tbcIcon.Visible;
        }

        private void pageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcPage.Visible = !tbcPage.Visible;
        }

        private void dateCreatedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcCreateTime.Visible = !tbcCreateTime.Visible;
        }

        private void accessTimeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcAccessTime.Visible = !tbcAccessTime.Visible;
        }

        private void writeTimeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcWriteTime.Visible = !tbcWriteTime.Visible;
        }

        private void sizeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcSize.Visible = !tbcSize.Visible;
        }

        private void resolutionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcResolution.Visible = !tbcResolution.Visible;
        }

        private void ratioToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcRatio.Visible = !tbcRatio.Visible;
        }

        private void widthToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcWidth.Visible = !tbcWidth.Visible;
        }

        private void heightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcHeight.Visible = !tbcHeight.Visible;
        }

        private void lengthToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcLength.Visible = !tbcLength.Visible;
        }

        private void fPSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tbcFPS.Visible = !tbcFPS.Visible;
        }

        private void letterboxToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender as ToolStripMenuItem).Checked)
            {
                clipModeByControl = ClipMode.Letterbox;
                ChangeThumbnailSize(null);
                UpdatePreview();
            }
        }

        private void panAndScanToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender as ToolStripMenuItem).Checked)
            {
                clipModeByControl = ClipMode.PanAndScan;
                ChangeThumbnailSize(null);
                UpdatePreview();
            }
        }

        private void plaClipToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender as ToolStripMenuItem).Checked)
            {
                clipModeByControl = ClipMode.PlaClip;
                ChangeThumbnailSize(null);
                UpdatePreview();
            }
        }

        private void verticalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setFileListDockForUser(DockStyle.Bottom);
        }

        private void alternativeVerticalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setFileListDockForUser(DockStyle.Top);
        }

        private void horizontalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setFileListDockForUser(DockStyle.Right);
        }

        private void alternativeHorizontalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setFileListDockForUser(DockStyle.Left);
        }

        private void reloadAllItemsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            userReload();
        }

        private void userReload()
        {
            SmartStaticIconProvider.ClearIconCache();
            UpdatePreview();
        }

        // マウスダウンイベントに InDirectoryOrVirtualDirectory のイベントが割り当ててある
        private void clearThumbnailSettingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (InMovie || InArchive) removeCoverSettingFromCurrentLocation();
        }

        private void addToolStripMenuItem_DropDownClosed(object sender, EventArgs e)
        {

            addToolStripMenuItem.DropDown = null;
            AllowFullpower = true;
        }

        private void editSmartFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedRows = dgvDirectoryList.SelectedRows;
            if (selectedRows.Count != 1) return;
            var path = (selectedRows[0].Cells[tbcDirectoryName.Index].Value as ColoredBookmark)?.SimpleBookmark?.Location;
            if (path == null) return;
            openSmartFolderSettingForm(path); // AllowFullpower 込み
        }

        private void cutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cutSelection(ModifierKeys);
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            copySelection(ModifierKeys);
        }

        private void pasteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var targetChildFolder = null as string;
            while (InRealDirectory)
            {
                var selectedIndex = tvCatalog.SelectedIndex; if (selectedIndex < 0) break;
                var zpa = ZipPathArray; if (zpa == null) break;
                var count = zpa.Length; if (count <= selectedIndex) break;
                var zna = ZipNameArray; if (zna == null || zna.Length != count) break;
                var name = zna[selectedIndex]; if (name == null || !name.Any() || name.Last() != Path.DirectorySeparatorChar) break;
                targetChildFolder = zpa[selectedIndex];
                if (targetChildFolder == "") targetChildFolder = null;
                break;
            }

            if (targetChildFolder == null)
            {
                pasteToCurrentLocation();
            }
            else
            {
                pasteToTarget(targetChildFolder);
            }
        }

        private void standardRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RightClickContextMenu = CatalogFormContextMenu.ZipPla;
        }

        private void standardMiddleclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MiddleClickContextMenu = CatalogFormContextMenu.ZipPla;
        }

        private void standardCtrlRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ControlRightClickContextMenu = CatalogFormContextMenu.ZipPla;
        }

        private void OnlyTagRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RightClickContextMenu = CatalogFormContextMenu.Tag;
        }

        private void OnlyTagMiddleclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MiddleClickContextMenu = CatalogFormContextMenu.Tag;
        }

        private void onlyTagCtrlRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ControlRightClickContextMenu = CatalogFormContextMenu.Tag;
        }

        private void explorerRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RightClickContextMenu = CatalogFormContextMenu.Explorer;
        }

        private void explorerMiddleclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MiddleClickContextMenu = CatalogFormContextMenu.Explorer;
        }

        private void explorerCtrlRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ControlRightClickContextMenu = CatalogFormContextMenu.Explorer;
        }

        private void alwaysAutomaticallyHideUIToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AlwaysHideUI = !AlwaysHideUI;
        }

        private void sortFilesWithTheSameOrderAsThumbnailWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SortSyncMode = SortSyncMode == SortSyncMode.None ? SortSyncMode.Directory : SortSyncMode.None;
        }

        private void noneItemSelectionFeedbackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ItemSelectionFeedback = ItemSelectionFeedback.None;
        }

        private void inAppropriateCasesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ItemSelectionFeedback = ItemSelectionFeedback.IfSortMayBeSame;
        }

        private void alwaysItemSelectionFeedbackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ItemSelectionFeedback = ItemSelectionFeedback.Always;
        }
    }
}
