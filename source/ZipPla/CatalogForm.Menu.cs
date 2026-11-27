using Alteridem.WinTouch;
using Common;
using GenerarClasses;
using SharpCompress.Common;
//using SharpCompress.Reader;
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

                //var filePath = Path.Combine(parent, ZipPlaCoverImageFileWithoutExtension + ".jpg");
                var filePath = Path.Combine(parent, ZipPlaCoverImageFileWithExtension);

                //if (File.Exists(filePath)) File.Delete(filePath);


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
                    //string[] files = null;
                    bool isNormalDrag = false;
                    if (!(dataObj is DataObjectEx) && dataObj.GetDataPresent(DataFormats.FileDrop))
                    {
                        //files = dataObj.GetData(DataFormats.FileDrop, false) as string[];
                        isNormalDrag = dataObj.GetData(DataFormats.FileDrop, false) is string[];
                    }
                    //var dragGuid = Guid.Empty;
                    //if (files != null)
                    if (isNormalDrag)
                    {
                        StopLookAheadProcess();

                        // ドラッグは同期的なので不要
                        //dragGuid = new Guid();
                        //OpendByExternalApplication_Dictionary[dragGuid] = files;
                    }

                    (sender as Control).DoDragDrop(dataObj, effects);

                    //if (files != null)
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
    }
}
