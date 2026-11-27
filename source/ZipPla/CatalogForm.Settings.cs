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
        bool CatalogForm_FormClosing_FormClosed = false;

#if AUTOBUILD
        private async void CatalogForm_FormClosing(object sender, FormClosingEventArgs e)
#else
        private void CatalogForm_FormClosing(object sender, FormClosingEventArgs e)
#endif
        {

            if (CatalogForm_FormClosing_FormClosed) return;

            CatalogForm_FormClosing_FormClosed = true;
            bmwMakePreview.CancelAsync();

            setCurrentFilterToHistory(bringToTop: true);
            SaveSettings();

            // 一定確率でエラー
            //Environment.Exit(0);
            //Application.Exit();

            // bmw を使う方法
            bool realTime;
            bmwMakePreview.ExitingByReservation += (s, ee) => CleanupOnClose(); // 予約した終了は Environment.Exit で FormClosed を通らない（X3）
            bmwMakePreview.ReserveExit(0, out realTime);
            if (!realTime)
            {
                Visible = false;
                e.Cancel = true;
#if AUTOBUILD
                await Task.Delay(10 * 1000);
                Application.Exit();
#endif
            }
        }

        private ToolStripMenuItem SelectedThumbnailSize
        {
            get
            {
                foreach (object itemObject in thumbnailSizeToolStripMenuItem.DropDownItems)
                {
                    if (itemObject.GetType() != typeof(ToolStripMenuItem)) continue;
                    var item = (ToolStripMenuItem)itemObject;
                    if (item.Checked)
                    {
                        return item;
                    }
                }
                return normalToolStripMenuItem;
            }
        }

        private bool SaveSettings()
        {
            try
            {
                var windowState = WindowState;
                var Maximized = windowState == FormWindowState.Maximized || windowState == FormWindowState.Minimized && CatalogForm_SizeChanged_BeforeMinimizedState == FormWindowState.Maximized;

                var config = new CatalogFormConfig();
                config.CurrentProfileColor = currentProfileColor;
                config.Maximized = Maximized;
                if (windowState == FormWindowState.Normal)
                {
                    config.Size = Size;
                    config.Point = Location;
                }
                else
                {

                    var bounds = /*WindowState == FormWindowState.Normal ? Bounds :*/ RestoreBounds;
                    config.Size = bounds.Size;
                    config.Point = bounds.Location;

                }
                config.Location = !string.IsNullOrEmpty(preCurrentLocation) ? preCurrentLocation : zabLocation.Text;
                config.ThumbnailScrollbar = tvCatalog.ScrollBarPercentage;
                config.FileListScrollbar = dgvFileList.FirstDisplayedScrollingRowIndex;
                var selectedIndex = tvCatalog.SelectedIndex;
                if (0 <= selectedIndex)
                {
                    lock (ZipPathArrayLocker)
                    {
                        if (selectedIndex < ZipNameArray.Length)
                        {
                            config.SelectedFileName = ZipNameArray[selectedIndex];
                        }
                        else
                        {
                            config.SelectedFileName = "";
                        }
                    }
                }
                else
                {
                    config.SelectedFileName = "";
                }
                config.LastSortMode = prevSortMode;
                config.SortMode = sortMode;
                config.RandomSeed = sortMode == SortMode.Random ? GetSortArray_RandomSeed : null;
                config.Filter = cbFilter.Text;
                config.ThumbnailSize = thumbnailSizeToolStripMenuItem.DropDownItems.IndexOf(SelectedThumbnailSize) - ChangeThumbnailSize_MenuItemOffset;
                config.AspectRatio = aspectRatio;
                config.ShowIcon = showIcon;
                config.ShowRating = showRating;
                config.ShowTags = showTags;
                config.ShowFilename = tvCatalog.ShowText;
                config.NumberOfLinesOfName = NumberOfLinesOfName;
                config.ClipMode = clipModeByControl;
                config.AligningMode = currentAligning;
                config.MinimumFrameThickness = currentMinimumFrameThickness;
                config.ThumbnailMouseWheelScrollAmount = tvCatalog.MouseWheelScrollAmount;
                config.ThumbnailMouseWheelScrollUnit = tvCatalog.MouseWheelScrollUnit;
                config.BookmarkVisible = bookmarkToolStripMenuItem.Checked;
                config.BookmarkWidth = pnlLeft.Width;
                config.BookmarkHeight = pnlBookmark.Dock == DockStyle.Fill ? pnlBookmarkNoFillHeight : pnlBookmark.Height;//pnlBookmark.Height;
                config.FolderTreeVisible = folderTreeToolStripMenuItem.Checked;
                config.ThumbnailListVisible = thumbnailListToolStripMenuItem.Checked;
                config.FileListVisible = fileListToolStripMenuItem.Checked;
                config.FileListDock = getFileListDock();
                config.FileListWidth = pnlRight_NormalWidth;
                config.FileListOrder = Program.EncodeDisplayIndices(dgvFileList.Columns);
                config.IconInFileList = tbcIcon.Visible;
                config.PageInFileList = tbcPage.Visible;
                config.RatingInFileList = tbcRating.Visible;
                config.DateCreatedInFileList = tbcCreateTime.Visible;
                config.DateAccessedInFileList = tbcAccessTime.Visible;
                config.DateModifiedInFileList = tbcWriteTime.Visible;
                config.SizeInFileList = tbcSize.Visible;
                config.ResolutionInFileList = tbcResolution.Visible;
                config.RatioInFileList = tbcRatio.Visible;
                config.WidthInFileList = tbcWidth.Visible;
                config.HeightInFileList = tbcHeight.Visible;
                config.LengthInFileList = tbcLength.Visible;
                config.FpsInFileList = tbcFPS.Visible;
                config.TinyOnScreenKeyboardVisible = TinyOnScreenKeyboardVisible;
                config.SelectionHighlightMode = SelectionHilightMode;
                config.FilterRemovingMode = FilterRemovingMode;
                config.LoadLastViewedPageFromHistory = LoadLastViewedPageFromHistory;
                config.DoubleClickFileOpenMode = DoubleClickFileOpenMode;
                config.RightClickContextMenu = RightClickContextMenu;
                config.MiddleContextMenu = MiddleClickContextMenu;
                config.ControlRightClickContextMenu = ControlRightClickContextMenu;
                config.ItemSelectionFeedback = ItemSelectionFeedback;
                config.SynchroFromTreeToBar = SynchroFromTreeToBar;
                config.SynchroFromBarToTree = SynchroFromBarToTree;
                config.CollapseOthersWhenSynchronizing = CollapseOthersWhenSynchronizing;
                config.FolderSortMode = FolderSortMode;
                config.ArchivesInArchiveMode = ArchivesInArchiveMode;
                config.ReadOnMemoryMode = ReadOnMemoryMode;
                config.OpenInPreviousImageFilterSetting = OpenInPreviousImageFilterSetting;
                config.DefaultBindingMode = bindingModeByControl;
                config.CoverBindingMode = CoverBindingMode;
                config.SortSyncMode = SortSyncMode;
                config.AlwaysHideUI = AlwaysHideUI;
                config.AllowReadAheadProcess = AllowReadAheadProcess;
                config.BackColorOfBuiltInViewer = BackColorOfBuiltInViewer;
                config.SelectedColorForBackColorOfBuiltInViewer = SelectedColorForBackColorOfBuiltInViewer;
                config.BuiltInViewerInitialFullscreenMode = BuiltInViewerInitialFullscreenMode;
                config.MaxCaptchaCountInOneVideo = MaxCaptchaCountInAVideo;
                config.DisplayArchiveFiles = ArchiveLoadLevel.ToString(); // archivesFilesPDFFielsToolStripMenuItem.Checked;
                config.DisplayImageFiles = ImageLoadLevel.ToString(); //imageFilesToolStripMenuItem.Checked;
                config.DisplayVideoFiles = VideoLoadLevel.ToString(); //videoFilesToolStripMenuItem.Checked;
                config.DisplayOtherFiles = OtherLoadLevel.ToString(); //otherFilesToolStripMenuItem.Checked;
                config.ThumbnailCacheRoot = thumbnailCache == null ? CatalogFormConfig.NOCACHE : thumbnailCache;
                config.SubfolderMode = SubfolderMode;
                config.LocationHistory = (from object item in cbLocation.Items select item.ToString()).ToArray();
                config.FilterHistory = (from object item in cbFilter.Items where !string.IsNullOrEmpty(item?.ToString()) select item.ToString()).ToArray();
                config.RatingReference = ratingReferenceValue;
                if (aliasesToFilteringStrings != null) config.FilterAliasToString = (from kav in aliasesToFilteringStrings select new KeyAndValue<string, string>(kav)).ToArray();
                config.InvariantFilter = invariantFilteringList;
                config.MouseGestureEnabled = mgCatalog.Enabled;
                config.MouseGestureLineWidth = mgCatalog.GetWidth();
                config.MouseGestureLineColor = mgCatalog.GetColor();
                config.MouseGestureCommands = mgCatalog.Actions != null && mgCatalog.Actions.Length > 0 ?
                    (from a in mgCatalog.Actions select new MouseGestureCommand() { Gesture = a.Gesture, Command = (Command)a.Key }).ToArray() : null;
                config.Save();

                return true;
            }
            catch (Exception error)
            {
                Program.AlertError(error);
                return false;
            }
        }

        private bool checkNgen = false;

        private bool firstCatalogFormRun = false;

        private bool emptyStartLocation = false;

        private enum LoadSettings_Result { INIFileNotFound, INILoadError, Completed }

        private LoadSettings_Result LoadSettings(string startDirectory = null, string selectedFileName = null,
            SortMode? sortModeOption = null, SortMode? preSortModeOption = null, string randomSeedOption = null)
        {
            // コマンドラインから指定された場合キーボードから実行されたとみなす
            var selectFirst = startDirectory != null && selectedFileName == null;


            Color color;
            bool maximized;
            Size size;
            Point point;
            string location;
            double tScrollbar;
            int fScrollbar;
            SortMode preSort;
            SortMode sort;
            string seed;
            string filter;
            int thumbnailsize;
            double aspect;
            bool showIcon, showRating, showTags, showFilename;
            NumberOfLinesOfName numOfLines;
            ClipMode clipMode;
            ThumbViewerAligningMode aligning;
            double minimumFrameThickness;
            int scrollA;
            ThumbViewerScrollUnit scrollU;
            bool bookmark;
            int bookmarkWidth, bookmarkHeight;
            bool folderTree;
            CoverBindingMode coverBinding;
            SortSyncMode sortSync;
            bool alwaysHide, readAhead;
            bool onScreenKeyboard;
            SelectionHighlightMode highlight;
            FilterRemovingMode? filterRemoving;
            bool loadPage;
            DoubleClickMode? doubleClick;
            CatalogFormContextMenu right, middle, control;
            ItemSelectionFeedback feedback;
            bool treeToBar, barToTree, collap;
            FolderSortMode folderSortMode;
            ArchivesInArchiveMode aia;
            ReadOnMemoryMode readOnMemoryMode;
            bool prevImageFilter;
            BindingMode? binding;
            Color viewrColor;
            Color selectedViewerColor;
            InitialFullscreenMode initialFullscreen;
            int maxCapcha;
            bool fileList, thumbnailList, iconInFileList, pageInFileList, ratingInFileList, createTimeInFileList, accessTimeInFileList, writeTimeInFileList, sizeInFileList,
                resolutionInFileList, ratioInFileList, widthInFileList, heightInFileList, lengthInFileList, fpsInFileList;
            ItemLoadLevel archives, images, videos, others;
            string tCache;
            SubfolderMode subfolderMode;
            DockStyle fileListDock;
            int fileListWidth;
            string[] fileListOrder;
            int ratingReference;
            KeyAndValue<string, string>[] alias;
            bool invariantFilter;
            bool mouseGestureEnabled;
            double mouseGestureLineWidth;
            Color mouseGestureLineColor;
            MouseGestureCommand[] mgCommands;
            string[] locationHistory, filterHistory;
            try
            {
                var config = new CatalogFormConfig();

                color = config.CurrentProfileColor;
                maximized = config.Maximized;
                size = config.Size; if (size == Size.Empty) size = Size;
                if (config.Point != null) point = (Point)config.Point; else point = Location;
                firstCatalogFormRun = config.Location == null;

                if (string.IsNullOrEmpty(startDirectory))
                {
                    location = config.Location;
                    tScrollbar = config.ThumbnailScrollbar;
                    fScrollbar = config.FileListScrollbar;
                    if (string.IsNullOrEmpty(selectedFileName))
                    {
                        selectedFileName = config.SelectedFileName;
                    }
                }
                else
                {
                    location = startDirectory;
                    if (string.IsNullOrEmpty(selectedFileName))
                    {
                        tScrollbar = 0;
                        fScrollbar = 0;
                    }
                    else
                    {
                        tScrollbar = -1;
                        fScrollbar = -1;
                    }
                }
                if (sortModeOption == null)
                {
                    preSort = config.LastSortMode;
                    sort = config.SortMode;
                    seed = config.RandomSeed;
                }
                else
                {
                    if (preSortModeOption != null)
                    {
                        preSort = (SortMode)preSortModeOption;
                    }
                    else
                    {
                        preSort = config.SortMode;
                    }
                    sort = (SortMode)sortModeOption;
                    if (sort == SortMode.Random && randomSeedOption != null)
                    {
                        seed = randomSeedOption;
                    }
                    else
                    {
                        seed = config.RandomSeed;
                    }
                }
                filter = config.Filter;
                thumbnailsize = config.ThumbnailSize;
                aspect = config.AspectRatio;
                showIcon = config.ShowIcon;
                showRating = config.ShowRating;
                showTags = config.ShowTags;
                showFilename = config.ShowFilename;
                numOfLines = config.NumberOfLinesOfName;
                clipMode = config.ClipMode;
                aligning = config.AligningMode;
                minimumFrameThickness = config.MinimumFrameThickness;
                scrollA = config.ThumbnailMouseWheelScrollAmount;
                scrollU = config.ThumbnailMouseWheelScrollUnit;
                bookmark = config.BookmarkVisible;
                bookmarkWidth = config.BookmarkWidth; if (bookmarkWidth <= 0) bookmarkWidth = pnlLeft.Width; // まずゼロなら初期値に
                bookmarkHeight = config.BookmarkHeight; if (bookmarkHeight <= 0) bookmarkHeight = pnlBookmark.Height;
                folderTree = config.FolderTreeVisible;
                thumbnailList = config.ThumbnailListVisible;
                fileList = config.FileListVisible;
                fileListDock = config.FileListDock;
                fileListWidth = config.FileListWidth;
                fileListOrder = config.FileListOrder;
                iconInFileList = config.IconInFileList;
                pageInFileList = config.PageInFileList;
                ratingInFileList = config.RatingInFileList;
                createTimeInFileList = config.DateCreatedInFileList;
                accessTimeInFileList = config.DateAccessedInFileList;
                writeTimeInFileList = config.DateModifiedInFileList;
                sizeInFileList = config.SizeInFileList;
                resolutionInFileList = config.ResolutionInFileList;
                ratioInFileList = config.RatioInFileList;
                widthInFileList = config.WidthInFileList;
                heightInFileList = config.HeightInFileList;
                lengthInFileList = config.LengthInFileList;
                fpsInFileList = config.FpsInFileList;
                coverBinding = config.CoverBindingMode;
                sortSync = config.SortSyncMode;
                alwaysHide = config.AlwaysHideUI;
                readAhead = config.AllowReadAheadProcess;
                onScreenKeyboard = config.TinyOnScreenKeyboardVisible;
                highlight = config.SelectionHighlightMode;
                filterRemoving = config.FilterRemovingMode;
                loadPage = config.LoadLastViewedPageFromHistory;
                doubleClick = config.DoubleClickFileOpenMode;
                right = config.RightClickContextMenu;
                middle = config.MiddleContextMenu;
                control = config.ControlRightClickContextMenu;
                feedback = config.ItemSelectionFeedback;
                treeToBar = config.SynchroFromTreeToBar;
                barToTree = config.SynchroFromBarToTree;
                collap = config.CollapseOthersWhenSynchronizing;
                folderSortMode = config.FolderSortMode;
                aia = config.ArchivesInArchiveMode;
                readOnMemoryMode = config.ReadOnMemoryMode;
                prevImageFilter = config.OpenInPreviousImageFilterSetting;
                binding = config.DefaultBindingMode;
                viewrColor = config.BackColorOfBuiltInViewer;
                selectedViewerColor = config.SelectedColorForBackColorOfBuiltInViewer;
                initialFullscreen = config.BuiltInViewerInitialFullscreenMode;
                maxCapcha = config.MaxCaptchaCountInOneVideo;
                archives = ParseItemLoadLevel(config.DisplayArchiveFiles);
                images = ParseItemLoadLevel(config.DisplayImageFiles);
                videos = ParseItemLoadLevel(config.DisplayVideoFiles);
                others = ParseItemLoadLevel(config.DisplayOtherFiles);
                tCache = config.ThumbnailCacheRoot;
                subfolderMode = config.SubfolderMode;
                ratingReference = config.RatingReference;
                alias = config.FilterAliasToString;
                invariantFilter = config.InvariantFilter;
                mouseGestureEnabled = config.MouseGestureEnabled;
                mouseGestureLineWidth = config.MouseGestureLineWidth;
                mouseGestureLineColor = config.MouseGestureLineColor;
                mgCommands = config.MouseGestureCommands;

                locationHistory = config.LocationHistory; if (locationHistory == null) locationHistory = new string[0];
                filterHistory = config.FilterHistory; if (filterHistory == null) filterHistory = new string[0];
            }
            catch (Exception error)
            {
                Program.AlertError(error);
                return LoadSettings_Result.INILoadError;
            }
            

            // 最も近いスクリーンにパックする
            var settingRect = new Rectangle(point, size);
            var nearestRect = Screen.FromRectangle(settingRect).WorkingArea;
            settingRect = BetterFormRestoreBounds.Pack(settingRect, nearestRect);
            if (IsLoaded)
            {
                Bounds = settingRect;

                if (maximized) WindowState = FormWindowState.Maximized; // 最大化はサイズと場所決定後に行わないと復元されるときに不自然な表示

                // if (!maximized) SaveSettings_WindowLocationWhenNotMaximized = Location = point; else SaveSettings_WindowLocationWhenNotMaximized = Location;// 最大化時はサイズ変更後ウインドウ位置が復元されない
            }
            else
            {
                
                var evh = null as EventHandler;
                evh = (sender, e) =>
                {
                    Bounds = settingRect;
                    if (maximized) WindowState = FormWindowState.Maximized; // 最大化はサイズと場所決定後に行わないと復元されるときに不自然な表示

                    // if (!maximized) SaveSettings_WindowLocationWhenNotMaximized = Location = point; else SaveSettings_WindowLocationWhenNotMaximized = Location;// 最大化時はサイズ変更後ウインドウ位置が復元されない
                    Load -= evh;
                };
                Load += evh;
            }

            cbLocation.Items.Clear();
            {
                // 消すのは、つながっている固定ドライブ上でなくなったものだけにする。外したドライブ・ネットワークの場所は
                // 一時的に見えないだけのことがあるので残す（X6）。ネットワークの場所は存在確認もしない（切断中だと 1 件ごとに長く止まる）
                var keptHistory = new List<object>(locationHistory.Length);
                var fixedReadyRoots = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in locationHistory)
                {
                    if (keptHistory.Count >= LocationHistoryLimit) break; // 上限を超えた古いものは読まない（S1）
                    try
                    {
                        if (string.IsNullOrEmpty(h)) continue;
                        var root = Path.GetPathRoot(h);
                        if (string.IsNullOrEmpty(root)) continue;
                        if (!fixedReadyRoots.TryGetValue(root, out var fixedReady))
                        {
                            try
                            {
                                if (root.StartsWith(@"\\"))
                                {
                                    fixedReady = false;
                                }
                                else
                                {
                                    var drive = new DriveInfo(root);
                                    fixedReady = drive.DriveType == DriveType.Fixed && drive.IsReady;
                                }
                            }
                            catch { fixedReady = false; }
                            fixedReadyRoots.Add(root, fixedReady);
                        }
                        if (!fixedReady || VirtualFolder.DirectoryOrBookmarkExists(h))
                        {
                            keptHistory.Add(h);
                        }
                    }
                    catch { }
                }
                cbLocation.Items.AddRange(keptHistory.ToArray());
            }
            //zabLocation.Text = location; // setMainPanelWidth より後で

            prevSortMode = preSort;// GetSortIndex(preSort);
            GetSortArray_RandomSeed = seed;
            cbSortBy.SelectedIndex = (int)sort;

            cbFilter.Items.Clear();
            cbFilter.Items.Add("");
            // ユーザーが作った固定のリストは削らない。履歴は上限を超えた古いものを読まない（S1）
            cbFilter.Items.AddRange((invariantFilter ? filterHistory : filterHistory.Take(FilterHistoryLimit)).Cast<object>().ToArray());

            aliasesToFilteringStrings = new Dictionary<string, string>();
            foreach (var kav in alias) aliasesToFilteringStrings.Add(kav.Key, kav.Value);
            cbFilter.Text = filter;

            if (thumbnailsize < 0 || 5 <= thumbnailsize)
            {
                thumbnailsize = 2;
            }

            NumberOfLinesOfName = numOfLines;
            clipModeByControl = clipMode;
            aspectRatio = aspect;
            setAspectRatioCheckState();

            var temp = tvCatalog.StopUpdateRowsCols;
            tvCatalog.StopUpdateRowsCols = true;
            currentAligning = aligning;
            currentMinimumFrameThickness = minimumFrameThickness;
            tvCatalog.StopUpdateRowsCols = temp;

            setMouseWheelScrollAmount(scrollA, scrollU);

            ChangeThumbnailSize((ToolStripMenuItem)thumbnailSizeToolStripMenuItem.DropDownItems[thumbnailsize + ChangeThumbnailSize_MenuItemOffset]);
            this.showIcon = showIconToolStripMenuItem.Checked = showIcon;
            this.showRating = showRatingToolStripMenuItem.Checked = showRating;
            this.showTags = showTagsToolStripMenuItem.Checked = showTags;
            setShowFileName(showFilename);

            Program.DecodeDisplayIndices(dgvFileList.Columns, fileListOrder);

            if (fileListWidth <= 0) fileListWidth = getFileListWidth();

            if (bookmarkWidth < SidePanellMinWidth) bookmarkWidth = SidePanellMinWidth;
            if (bookmarkHeight < splBookmark.MinSize) bookmarkHeight = splBookmark.MinSize;
            if (fileListWidth < SidePanellMinWidth) fileListWidth = SidePanellMinWidth;

            pnlBookmarkNoFillHeight = bookmarkHeight;
            bookmarkToolStripMenuItem.Checked = bookmark;
            pnlLeft.Width = bookmarkWidth;
            if (folderTree)
            {
                folderTreeToolStripMenuItem.Checked = true;
            }
            else
            {
                pnlBookmark.Dock = DockStyle.Fill;
            }
            setFileListDock(fileListDock, keepWidth: false, fixWidth: false);

            pnlRight_NormalWidth = fileListWidth;

            // 最大化でない＋横並びの場合 Load 前だと意味のない 幅変更が起こる
            //setFileListWidthWithoutSettingWidth(fileListWidth);
            EventHandler setFileListWidthWithoutSettingWidth_fileListWidth = null;
            setFileListWidthWithoutSettingWidth_fileListWidth = (sender, e) =>
            {
                setFileListWidthWithoutSettingWidth(fileListWidth);
                Load -= setFileListWidthWithoutSettingWidth_fileListWidth;
                setFileListWidthWithoutSettingWidth_fileListWidth = null;
            };
            Load += setFileListWidthWithoutSettingWidth_fileListWidth;
            
            if (thumbnailList || fileList)
            {
                thumbnailListToolStripMenuItem.Checked = thumbnailList;
                fileListToolStripMenuItem.Checked = fileList;
            }
            else
            {
                fileListToolStripMenuItem.Checked = false;
            }
            tbcIcon.Visible = iconInFileList;
            tbcPage.Visible = pageInFileList;
            tbcRating.Visible = ratingInFileList;
            tbcCreateTime.Visible = createTimeInFileList;
            tbcAccessTime.Visible = accessTimeInFileList;
            tbcWriteTime.Visible = writeTimeInFileList;
            tbcSize.Visible = sizeInFileList;
            tbcResolution.Visible = resolutionInFileList;
            tbcRatio.Visible = ratioInFileList;
            tbcWidth.Visible = widthInFileList;
            tbcHeight.Visible = heightInFileList;
            tbcLength.Visible = lengthInFileList;
            tbcFPS.Visible = fpsInFileList;
            ratingReferenceValue = ratingReference;
            invariantFilteringList = invariantFilter;

            mgCatalog.Enabled = mgFileList.Enabled = mouseGestureEnabled;
            mgCatalog.Width = mgFileList.Width = mouseGestureLineWidth;
            mgCatalog.Color = mgFileList.Color = mouseGestureLineColor;
            if (mgCommands != null)
            {
                var actions = new List<MouseGestureAction>();
                var template = getMouseGestureSettingTemplate();
                foreach (var c in mgCommands)
                {
                    var gesture = c.Gesture;
                    if (gesture == null || gesture.Length == 0) continue;
                    var key = (int)c.Command;
                    var action = template.FirstOrDefault(t => t.Key == key);
                    if (action == default(MouseGestureSettingTemplate)) continue;
                    actions.Add(new MouseGestureAction(key, gesture, action.Action));
                }
                mgCatalog.Actions = mgFileList.Actions = actions.ToArray();
            }
            else
            {
                mgCatalog.Actions = mgFileList.Actions = null;
            }

            if (archives == ItemLoadLevel.Ignore && images == ItemLoadLevel.Ignore && videos == ItemLoadLevel.Ignore && others == ItemLoadLevel.Ignore && !IsDisplay(subfolderMode))
            {
                archives = ItemLoadLevel.Full;
            }

            SelectionHilightMode = highlight;
            if (filterRemoving != null)
            {
                FilterRemovingMode = (FilterRemovingMode)filterRemoving;
            }
            else
            {
                FilterRemovingMode = firstCatalogFormRun ? FilterRemovingMode.WhenMovingLocation : FilterRemovingMode.Never;
            }

            LoadLastViewedPageFromHistory = loadPage;

            // 移行完了後（nullable を外す必要がある）
            // DoubleClickFileOpenMode = doubleClick;

            // 移行用
            if (doubleClick == null)
            {
                DoubleClickFileOpenMode = !firstCatalogFormRun ? DoubleClickMode.SelectAndClick : DoubleClickMode.IgnoreMouseMove;
            }
            else
            {
                DoubleClickFileOpenMode = (DoubleClickMode)doubleClick;
            }

            RightClickContextMenu = right;
            MiddleClickContextMenu = middle;
            ControlRightClickContextMenu = control;

            ItemSelectionFeedback = feedback;

            setSynchroBetweenTreeAndBar(treeToBar, barToTree);
            CollapseOthersWhenSynchronizing = collap;

            SetFolderSortMode(folderSortMode, reSort: false);


            ArchivesInArchiveMode = aia;
            if (simplifiedKeyBoard == null)
            {
                tinyOnScreenKeyboardVisible = onScreenKeyboard;
            }
            else
            {
                TinyOnScreenKeyboardVisible = onScreenKeyboard;
            }
            OpenInPreviousImageFilterSetting = prevImageFilter;

            ReadOnMemoryMode = readOnMemoryMode;

            CoverBindingMode = coverBinding;
            BindingMode binding2;
            if (binding == null)
            {
                binding2 = Program.GetBindingModeFromCulture(Message.CurrentLanguage);
            }
            else
            {
                binding2 = (BindingMode)binding;
            }
            SortSyncMode = sortSync;
            SetAlwaysHideUI(alwaysHide, silent: true);
            SetAllowReadAheadProcess(readAhead, silent: true);
            BackColorOfBuiltInViewer = viewrColor;
            SelectedColorForBackColorOfBuiltInViewer = selectedViewerColor;
            SetBuiltInViewerInitialFullscreenMode(initialFullscreen, silent: true);
            MaxCaptchaCountInAVideo = maxCapcha;
            if (tCache != null)
            {
                thumbnailCache = tCache == CatalogFormConfig.NOCACHE ? null : tCache;
            }
            else
            {
                try
                {
                    thumbnailCache = GPSizeThumbnail.SupportADSSize(Application.ExecutablePath, checkNew: true) > 0 ? GPSizeThumbnail.AlternateDataStream :
                        DefaultThumbnailCache;
                }
                catch
                {
                    thumbnailCache = DefaultThumbnailCache;
                }
            }
            ArchiveLoadLevel = archives;
            bindingModeByControl = binding2;
            ImageLoadLevel = images;
            VideoLoadLevel = videos;
            OtherLoadLevel = others;

            SubfolderMode = subfolderMode;

            var generalConfig = new GeneralConfig();

            ApplicationArray = (from info in generalConfig.Applications select info.GetApplicationProvider(this)).ToArray();
            var dynamicStringSelections = generalConfig.DynamicStringSelections;
            var dynamicStringSelectionsEnabled = generalConfig.DynamicStringSelectionsEnabled;
            allowRenameByCmsRightClick = generalConfig.DynamicStringSelectionsAllowUserToRenameItem;
            dragAndDropAction = generalConfig.DragAndDropAction;
            checkNgen = generalConfig.CheckNgen;

            loadBookmarkFromConfig();

            var zipTagConfig = new ZipTagConfig();// 準備しているため不要でも読み込まないとエラーになる
            if (string.IsNullOrEmpty(startDirectory))
            {
                currentProfileColor = color;
            }
            else
            {
                var startDirectoryInLower = startDirectory.ToLower();
                var colorCands = new List<Color>();
                foreach (DataGridViewRow row in dgvDirectoryList.Rows)
                {
                    var bm = row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark;
                    if (bm != null && bm.SimpleBookmark.SpecialRole == SimpleBookmarkSpecialRole.None) // セパレーターなら無視される
                    {
                        var sb = bm.SimpleBookmark;
                        if (sb?.Location?.ToLower() == startDirectoryInLower)
                        {
                            colorCands.Add(bm.Color);
                        }
                    }
                }
                var candCount = colorCands.Count;
                if (candCount == 1 || candCount > 1 && new HashSet<Color>(colorCands).Count == 1)
                {

                    currentProfileColor = colorCands[0];
                }
                else
                {
                    var lastColor = zipTagConfig.ProfileColor; // プログラムを終了しなくても保存されるプロファイルカラー
                    if (colorCands.Count == 0 || colorCands.Contains(lastColor))
                    {
                        currentProfileColor = lastColor;
                    }
                    else
                    {
                        currentProfileColor = colorCands[0];
                    }
                }
            }


            Shown += (sender, e) =>
            {
                if (WindowState != FormWindowState.Minimized)
                {
                    setMainPanelWidth();
                }
            };
            var esl = string.IsNullOrEmpty(location);
            emptyStartLocation = esl;
            EventHandler shown = null; // 言語仕様上、先に適当な値を代入しておく必要がある
            shown = new EventHandler((sender, e) =>
            {
                // 先にテキストボックスを設定する必要があるので
                setNewDynamicStringSelectionToolStripMenuItems(dynamicStringSelectionsEnabled, dynamicStringSelections);

                var nullLocation = esl;

                if (!nullLocation)
                {
                    zabLocation.Text = location; // setMainPanelWidth より後で
                }

                try
                {
                    if (IsSupportedByCatalogFormAndExists(location))
                    {
                        if (selectFirst)
                        {
                            // MakePreview の selectedIndex は dataIndex なので不適切
                            bmwMakePreviewRunWorkerStartingFinalAction = () =>
                            {
                                firstShownItemSelect();
                                bmwMakePreviewRunWorkerStartingFinalAction = null;
                            };
                        }

                        MakePreview_StopResetFilter = true;
                        MakePreview(tScrollbar: tScrollbar, selectedIndex: -1, selectedFileName: selectedFileName, fScrollbar: fScrollbar);
                    }
                    else
                    {
                        if (!nullLocation) MessageBox.Show(this, Message._1CanNotBeOpend.Replace("$1", zabLocation.Text), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        tvCatalog.ScrollBarPercentage = Math.Max(0, tScrollbar);
                        if (fScrollbar >= 0 && fScrollbar < dgvFileList.Rows.Count) dgvFileList.FirstDisplayedScrollingRowIndex = fScrollbar;
                    }
                }
                catch (UriFormatException error)
                {
                    MessageBox.Show(this, error.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    tvCatalog.ScrollBarPercentage = Math.Max(0, tScrollbar);
                    if (fScrollbar >= 0 && fScrollbar < dgvFileList.Rows.Count) dgvFileList.FirstDisplayedScrollingRowIndex = fScrollbar;
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, error.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    tvCatalog.ScrollBarPercentage = Math.Max(0, tScrollbar);
                    if (fScrollbar >= 0 && fScrollbar < dgvFileList.Rows.Count) dgvFileList.FirstDisplayedScrollingRowIndex = fScrollbar;
                }

                // ヒントのため静的な Shown に移動
                //foreach (DataGridViewRow row in dgvDirectoryList.Rows) row.Selected = false;

                Shown -= shown;
            });
            Shown += shown;

            return LoadSettings_Result.Completed;
        }

        public static readonly string DefaultThumbnailCache = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "thumbnails");

        private void preferenceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AllowFullpower = false;
            bool dssEnabled;
            DynamicStringSelectionInfo[] dss;
            var preDDA = dragAndDropAction;
            Program.ShowSetting(this, out ApplicationArray, /*out startApplicationWithAccessKey,*/ out dssEnabled,
                out allowRenameByCmsRightClick, out dss, out dragAndDropAction, out var historyTrimmed);
            setNewDynamicStringSelectionToolStripMenuItems(dssEnabled, dss);
            if (historyTrimmed && string.Compare(currentLocation, Program.HistorySorPath, ignoreCase: true) == 0)
            {
                UpdatePreview();
            }
            if (showStartHelpMessage && preDDA != dragAndDropAction)
            {
                tvCatalog.Invalidate();
            }
            AllowFullpower = true;
        }
    }
}
