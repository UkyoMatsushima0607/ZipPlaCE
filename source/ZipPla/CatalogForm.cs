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
    public partial class CatalogForm : Form, IMultipleLanguages
    {
        private const bool IgnoreFileError = true;

        private bool AllowFullpower = true;

        ZipPlaAddressBar zabLocation;

        const string ZipPlaCoverImageFileWithoutExtension = "{ZipPlaCoverFile}";
        const string ZipPlaCoverImageFileExtension = ".jpg";
        const string ZipPlaCoverImageFileWithoutExtensionInLower = "{zipplacoverfile}";
        const string ZipPlaCoverImageFileWithExtension = ZipPlaCoverImageFileWithoutExtension + ZipPlaCoverImageFileExtension;
        const string ZipPlaCoverImageFileWithExtensionInLower = ZipPlaCoverImageFileWithoutExtensionInLower + ZipPlaCoverImageFileExtension;

        private bool HideZipPlaCoverImageFile = true;

        bool isZipPlaCoverImageFile(string path)
        {
            if (path == null || path.Length < ZipPlaCoverImageFileWithExtensionInLower.Length) return false;
            if (path == ZipPlaCoverImageFileWithExtensionInLower) return true;
            var pos = path.Length - ZipPlaCoverImageFileWithExtensionInLower.Length - 1;
            if (pos < 0) return false;
            var c = path[pos];
            return (c == '\\' || c == '/') && path.Substring(pos).ToLower() == ZipPlaCoverImageFileWithExtensionInLower;
        }
        bool isZipPlaCoverImageType(string path)
        {
            return path != null && path.EndsWith(ZipPlaCoverImageFileExtension, StringComparison.OrdinalIgnoreCase);
        }

        private DataGridViewSorter dgvSorter;
        private MouseGesture mgCatalog, mgFileList;

        private Dictionary<string, string> aliasesToFilteringStrings;
        private bool invariantFilteringList;

        private string thumbnailCache = GPSizeThumbnail.AlternateDataStream;

        private bool allowRenameByCmsRightClick = true;

        private DragAndDropAction dragAndDropAction;

        private const bool startApplicationWithAccessKey = true;

        private bool AllowRightDragFromThumbnail = true;
        private bool AllowRightDragFromDetail = true;

        ItemLoadLevel archiveLoadLevel = ItemLoadLevel.Full;
        ItemLoadLevel ArchiveLoadLevel { get { return archiveLoadLevel; } set { SetItemLoadLevel(ref archiveLoadLevel, archivesFilesPDFFielsToolStripMenuItem, value, checkAllLoadLevel: false); } }
        ItemLoadLevel imageLoadLevel = ItemLoadLevel.Full;
        ItemLoadLevel ImageLoadLevel { get { return imageLoadLevel; } set { SetItemLoadLevel(ref imageLoadLevel, imageFilesToolStripMenuItem, value, checkAllLoadLevel: false); } }
        ItemLoadLevel videoLoadLevel = ItemLoadLevel.Full;
        ItemLoadLevel VideoLoadLevel { get { return videoLoadLevel; } set { SetItemLoadLevel(ref videoLoadLevel, videoFilesToolStripMenuItem, value, checkAllLoadLevel: false); } }
        ItemLoadLevel otherLoadLevel = ItemLoadLevel.Full;
        ItemLoadLevel OtherLoadLevel { get { return otherLoadLevel; } set { SetItemLoadLevel(ref otherLoadLevel, otherFilesToolStripMenuItem, value, checkAllLoadLevel: false); } }
        bool SetItemLoadLevel(ref ItemLoadLevel itemLoadLevel, ToolStripMenuItem toolStripMenuItem, ItemLoadLevel value, bool checkAllLoadLevel)
        {
            if (itemLoadLevel != value)
            {
                if (checkAllLoadLevel && value == ItemLoadLevel.Ignore)
                {
                    var temp = itemLoadLevel;
                    itemLoadLevel = value;
                    if (archiveLoadLevel == ItemLoadLevel.Ignore && imageLoadLevel == ItemLoadLevel.Ignore && videoLoadLevel == ItemLoadLevel.Ignore && otherLoadLevel == ItemLoadLevel.Ignore && !IsDisplay(SubfolderMode))
                    {
                        // ignore 以外でトグルする実装
                        itemLoadLevel = temp == ItemLoadLevel.Header ? ItemLoadLevel.Full : ItemLoadLevel.Header;


                        // 変更しない実装
                    }
                }
                else
                {
                    itemLoadLevel = value;
                }
                toolStripMenuItem.CheckState = itemLoadLevel == ItemLoadLevel.Ignore ? CheckState.Unchecked : itemLoadLevel == ItemLoadLevel.Full ? CheckState.Checked : CheckState.Indeterminate;
                return true;
            }
            return false;
        }


        SortSyncMode sortSyncMode = SortSyncMode.Directory;
        public SortSyncMode SortSyncMode
        {
            get { return sortSyncMode; }
            private set
            {
                if (value != sortSyncMode)
                {
                    sortSyncMode = value;
                    sortFilesWithTheSameOrderAsThumbnailWindowToolStripMenuItem.Checked = value != SortSyncMode.None;
                }
            }
        }

        bool synchroFromTreeToBar = true;
        bool synchroFromBarToTree = false;
        bool SynchroFromTreeToBar
        {
            get { return synchroFromTreeToBar; }
            set { setSynchroBetweenTreeAndBar(value, synchroFromBarToTree); }
        }
        bool SynchroFromBarToTree
        {
            get { return synchroFromBarToTree; }
            set { setSynchroBetweenTreeAndBar(synchroFromTreeToBar, value); }
        }

        bool collapseOthersWhenSynchronizing = false;
        bool CollapseOthersWhenSynchronizing
        {
            get { return collapseOthersWhenSynchronizing; }
            set
            {
                if (value != collapseOthersWhenSynchronizing)
                {
                    collapseOthersWhenSynchronizing = value;
                    collapseOthersWhenSynchronizingToolStripMenuItem.Checked = value;
                }
            }
        }

        void setSynchroBetweenTreeAndBar(bool treeToBar, bool barToTree)
        {
            if (treeToBar != synchroFromTreeToBar || barToTree != synchroFromBarToTree)
            {
                synchroFromTreeToBar = treeToBar;
                synchroFromBarToTree = barToTree;
                noneFolderTreeSynchronizingToolStripMenuItem.Checked = !(treeToBar || barToTree);
                folderTreeToAddressbarToolStripMenuItem.Checked = treeToBar && !barToTree;
                addressBarToFolderTreeToolStripMenuItem.Checked = !treeToBar && barToTree;
                twowayToolStripMenuItem.Checked = treeToBar && barToTree;
            }
        }

        ItemSelectionFeedback itemSelectionFeedback = ItemSelectionFeedback.IfSortMayBeSame;
        ItemSelectionFeedback ItemSelectionFeedback
        {
            get { return itemSelectionFeedback; }
            set
            {
                if (value != itemSelectionFeedback)
                {
                    itemSelectionFeedback = value;
                    noneItemSelectionFeedbackToolStripMenuItem.Checked = value == ItemSelectionFeedback.None;
                    inAppropriateCasesToolStripMenuItem.Checked = value == ItemSelectionFeedback.IfSortMayBeSame;
                    alwaysItemSelectionFeedbackToolStripMenuItem.Checked = value == ItemSelectionFeedback.Always;
                }
            }
        }

        private bool alwaysHideUI = false;
        private bool AlwaysHideUI
        {
            get { return alwaysHideUI; }
            set
            {
                SetAlwaysHideUI(value, silent: false);
            }
        }

        private void SetAlwaysHideUI(bool value, bool silent)
        {
            if (alwaysHideUI != value)
            {
                alwaysHideUI = value;
                alwaysAutomaticallyHideUIToolStripMenuItem.Checked = value;


            }
        }

        CatalogFormConetxtMenu rightClickContextMenu = CatalogFormConetxtMenu.ZipPla;
        CatalogFormConetxtMenu middleClickContextMenu = CatalogFormConetxtMenu.Tag;
        CatalogFormConetxtMenu controlRightClickContextMenu = CatalogFormConetxtMenu.Explorer;
        CatalogFormConetxtMenu RightClickContextMenu
        {
            get { return rightClickContextMenu; }
            set
            {
                if (value != rightClickContextMenu)
                {
                    if (middleClickContextMenu == value) middleClickContextMenu = rightClickContextMenu;
                    else controlRightClickContextMenu = rightClickContextMenu;
                    rightClickContextMenu = value;

                    setContextMenuCheckMark();
                }
            }
        }
        CatalogFormConetxtMenu MiddleClickContextMenu
        {
            get { return middleClickContextMenu; }
            set
            {
                if (value != middleClickContextMenu)
                {
                    if (rightClickContextMenu == value) rightClickContextMenu = middleClickContextMenu;
                    else controlRightClickContextMenu = middleClickContextMenu;
                    middleClickContextMenu = value;

                    setContextMenuCheckMark();
                }
            }
        }
        CatalogFormConetxtMenu ControlRightClickContextMenu
        {
            get { return controlRightClickContextMenu; }
            set
            {
                if (value != controlRightClickContextMenu)
                {
                    if (rightClickContextMenu == value) rightClickContextMenu = controlRightClickContextMenu;
                    else middleClickContextMenu = controlRightClickContextMenu;
                    controlRightClickContextMenu = value;

                    setContextMenuCheckMark();
                }
            }
        }

        void setContextMenuCheckMark()
        {
            standardRightclickToolStripMenuItem.Checked = CatalogFormConetxtMenu.ZipPla == rightClickContextMenu;
            standardMiddleclickToolStripMenuItem.Checked = CatalogFormConetxtMenu.ZipPla == middleClickContextMenu;
            standardCtrlRightclickToolStripMenuItem.Checked = CatalogFormConetxtMenu.ZipPla == controlRightClickContextMenu;

            OnlyTagRightclickToolStripMenuItem.Checked = CatalogFormConetxtMenu.Tag == rightClickContextMenu;
            OnlyTagMiddleclickToolStripMenuItem.Checked = CatalogFormConetxtMenu.Tag == middleClickContextMenu;
            onlyTagCtrlRightclickToolStripMenuItem.Checked = CatalogFormConetxtMenu.Tag == controlRightClickContextMenu;

            explorerRightclickToolStripMenuItem.Checked = CatalogFormConetxtMenu.Explorer == rightClickContextMenu;
            explorerMiddleclickToolStripMenuItem.Checked = CatalogFormConetxtMenu.Explorer == middleClickContextMenu;
            explorerCtrlRightclickToolStripMenuItem.Checked = CatalogFormConetxtMenu.Explorer == controlRightClickContextMenu;
        }

        private bool allowReadAheadProcess = true;
        private bool AllowReadAheadProcess
        {
            get { return allowReadAheadProcess; }
            set { SetAllowReadAheadProcess(value, silent: false); }
        }
        private void SetAllowReadAheadProcess(bool value, bool silent)
        {
            if (value != allowReadAheadProcess)
            {
                allowReadaheadProcessToolStripMenuItem.Checked = value;
                allowReadAheadProcess = value;
                if (!silent)
                {
                    if (value)
                    {
                        PrepareLookAheadProcess();
                    }
                    else
                    {
                        StopLookAheadProcess();
                    }
                }
            }
        }

        private bool loadLastViewedPageFromHistory = false;
        public bool LoadLastViewedPageFromHistory
        {
            get { return loadLastViewedPageFromHistory; }
            private set
            {
                if (value != loadLastViewedPageFromHistory)
                {
                    loadLastViewedPageFromHistory = value;
                    loadLastViewedPageFromHistoryToolStripMenuItem.Checked = value;
                }
            }
        }

        FolderSortMode folderSortMode = FolderSortMode.None;
        FolderSortMode FolderSortMode
        {
            get { return folderSortMode; }
            set { SetFolderSortMode(value, reSort: false); }
        }
        void SetFolderSortMode(FolderSortMode value, bool reSort)
        {
            if (value != folderSortMode)
            {
                reSort = reSort && ResolveFitToDirectionOfSort(value, sortMode) != ResolveFitToDirectionOfSort(folderSortMode, sortMode);
                folderSortMode = value;
                noneFolderArrangeToolStripMenuItem.Checked = value == FolderSortMode.None;
                displayFirstToolStripMenuItem.Checked = value == FolderSortMode.Ahead;
                displayLastToolStripMenuItem.Checked = value == FolderSortMode.Behind;
                fitToDirectionOfSortToolStripMenuItem.Checked = value == FolderSortMode.FitToDirectionOfSort;
                if (reSort)
                {
                    cbSortBy_SelectedValueChanged(null, null);
                }
            }
        }
        FolderSortMode ResolveFitToDirectionOfSort(FolderSortMode folderSortMode, SortMode sortMode)
        {
            if (folderSortMode == FolderSortMode.FitToDirectionOfSort)
            {
                switch (sortMode)
                {
                    case SortMode.AccessedInAsc:
                    case SortMode.CreatedInAsc:
                    case SortMode.ModifiedInAsc:
                    case SortMode.NameInAsc:
                    case SortMode.RatingInAsc:
                    case SortMode.SizeInAsc:
                    case SortMode.TypeInAsc:
                    case SortMode.VFPSInAsc:
                    case SortMode.VHeightInAsc:
                    case SortMode.VLengthInAsc:
                    case SortMode.VPageInAsc:
                    case SortMode.VRatioInAsc:
                    case SortMode.VResolutionInAsc:
                    case SortMode.VWidthInAsc:
                    case SortMode.Random:
                        return FolderSortMode.Ahead;
                    default: return FolderSortMode.Behind;
                }
            }
            else return folderSortMode;
        }

        NumberOfLinesOfName numberOfLinesOfName = NumberOfLinesOfName.Full;
        NumberOfLinesOfName NumberOfLinesOfName
        {
            get { return numberOfLinesOfName; }
            set
            {
                SetNumberOfLinesOfName(value, refresh: false);
            }
        }

        private void SetNumberOfLinesOfName(NumberOfLinesOfName value, bool refresh)
        {
            if (value == NumberOfLinesOfName.Full)
            {
                if (numberOfLinesOfName != value)
                {
                    numberOfLinesOfName = value;
                    fromTheLongestNameToolStripMenuItem.Checked = value == NumberOfLinesOfName.Full;
                    automaticlessToolStripMenuItem.Checked = value == NumberOfLinesOfName.Automatic1;
                    automaticgreaterToolStripMenuItem.Checked = value == NumberOfLinesOfName.Automatic2;
                    NumOfLineOfName1toolStripMenuItem.Checked = value == NumberOfLinesOfName.One;
                    NumOfLineOfName2toolStripMenuItem.Checked = value == NumberOfLinesOfName.Two;
                    NumOfLineOfName3toolStripMenuItem.Checked = value == NumberOfLinesOfName.Three;
                }

                if (refresh)
                {
                    tvCatalog.SetTextLinesFromText(getMaxLengthString(ZipNameArray), true);
                }
            }
            else
            {
                if (numberOfLinesOfName != value)
                {
                    numberOfLinesOfName = value;
                    fromTheLongestNameToolStripMenuItem.Checked = value == NumberOfLinesOfName.Full;
                    automaticlessToolStripMenuItem.Checked = value == NumberOfLinesOfName.Automatic1;
                    automaticgreaterToolStripMenuItem.Checked = value == NumberOfLinesOfName.Automatic2;
                    NumOfLineOfName1toolStripMenuItem.Checked = value == NumberOfLinesOfName.One;
                    NumOfLineOfName2toolStripMenuItem.Checked = value == NumberOfLinesOfName.Two;
                    NumOfLineOfName3toolStripMenuItem.Checked = value == NumberOfLinesOfName.Three;

                    int len, lin;
                    GetNameLengthAndNameLines(out len, out lin);
                    if (len > 0)
                    {
                        tvCatalog.SetTextLength(len, refresh);
                    }
                    else
                    {
                        tvCatalog.SetTextLines(lin, refresh);
                    }
                }
            }
        }

        private string getMaxLengthString(string[] array)
        {
            var maxLengthIndex = getMaxLengthIndex(array);
            if (maxLengthIndex >= 0) return array[maxLengthIndex];
            return null;
        }


        private int getMaxLengthIndex(string[] array)
        {
            if (array == null) return -1;
            float maxLength = -1;
            var maxLengthIndex = -1;
            using (var g = CreateGraphics())
            {
                for (var i = 0; i < array.Length; i++)
                {
                    var s = array[i];
                    if (s == null) continue;

                    var length = g.MeasureString(s, DefaultFont).Width; // TextRenderer.MeasureText より 10 倍程度高速

                    if (length > maxLength)
                    {
                        maxLength = length;
                        maxLengthIndex = i;
                    }
                }
            }

            return maxLengthIndex;
        }

        private void GetNameLengthAndNameLines(out int nameLength, out int nameLines)
        {
            switch (numberOfLinesOfName)
            {
                case NumberOfLinesOfName.Automatic1:
                    nameLength = 16;
                    nameLines = 0;
                    return;
                case NumberOfLinesOfName.Automatic2:
                    nameLength = 24;
                    nameLines = 0;
                    return;
                case NumberOfLinesOfName.One:
                    nameLength = 0;
                    nameLines = 1;
                    return;
                case NumberOfLinesOfName.Two:
                    nameLength = 0;
                    nameLines = 2;
                    return;
                case NumberOfLinesOfName.Three:
                    nameLength = 0;
                    nameLines = 3;
                    return;
                default: throw new Exception();
            }
        }

        private CoverBindingMode coverBindingMode = CoverBindingMode.Default;
        private CoverBindingMode CoverBindingMode
        {
            get
            {
                return coverBindingMode;
            }
            set
            {
                if (value != coverBindingMode)
                {
                    coverBindingMode = value;
                    forceFirstPageToBeSingleToolStripMenuItem.Checked = coverBindingMode == CoverBindingMode.ForceSingle;
                }
            }
        }

        private FilterRemovingMode filterRemovingMode;
        private FilterRemovingMode FilterRemovingMode
        {
            get { return filterRemovingMode; }
            set
            {
                if (value != filterRemovingMode)
                {
                    clearFilterWhenMovingLocationToolStripMenuItem.Checked = value == FilterRemovingMode.WhenMovingLocation;
                    filterRemovingMode = value;
                }
            }
        }

        // もはやこれでよい
        // ツリービューも追加
        private void explorerTreeView_TreeViewWnd_GotFocus(object sender, EventArgs e) { latestControledControl = explorerTreeView.TreeViewWnd; }
        private void dgvFileList_GotFocus(object sender, EventArgs e) { latestControledControl = dgvFileList; }
        private void dgvDirectoryList_GotFocus(object sender, EventArgs e) { latestControledControl = dgvDirectoryList; }
        private void tvCatalog_GotFocus(object sender, EventArgs e) { latestControledControl = tvCatalog; }
        private Control latestControledControl = null;
        private Control LatestControledControl
        {
            get
            {
                if (latestControledControl != null) return latestControledControl;
                if (thumbnailListToolStripMenuItem.Checked) return tvCatalog;
                return dgvFileList;
            }
            set
            {
                // 互換性用に残す
            }
        }
        // マウスホイールで直下を動かす動作を MouseEnter + Focus で実装していたとき

        public int packedImageLoaderLeftHierarchies = 0;
        public PackedImageLoaderSearchMode packedImageLoaderSearchMode = PackedImageLoaderSearchMode.Full;
        ArchivesInArchiveMode archivesInArchiveMode = ZipPla.ArchivesInArchiveMode.Ignore;
        private ArchivesInArchiveMode ArchivesInArchiveMode
        {
            get
            {
                return archivesInArchiveMode;
            }
            set
            {
                if (value != archivesInArchiveMode)
                {
                    archivesInArchiveMode = value;
                    switch (value)
                    {
                        case ArchivesInArchiveMode.Ignore:
                            packedImageLoaderLeftHierarchies = 0;
                            //packedImageLoaderAlwaysLoadLowerHierarchies = false;
                            packedImageLoaderSearchMode = PackedImageLoaderSearchMode.Full;
                            break;
                        case ArchivesInArchiveMode.IfNoOther1Level:
                            packedImageLoaderLeftHierarchies = 1;
                            //packedImageLoaderAlwaysLoadLowerHierarchies = false;
                            packedImageLoaderSearchMode = PackedImageLoaderSearchMode.UntilFoundLayer;
                            break;
                        case ArchivesInArchiveMode.UntilFound2Level:
                            packedImageLoaderLeftHierarchies = 2;
                            packedImageLoaderSearchMode = PackedImageLoaderSearchMode.UntilFoundItem;
                            break;
                        case ArchivesInArchiveMode.Always1Level:
                            packedImageLoaderLeftHierarchies = 1;
                            //packedImageLoaderAlwaysLoadLowerHierarchies = true;
                            packedImageLoaderSearchMode = PackedImageLoaderSearchMode.Full;
                            break;
                    }
                    ignoreArchivesInArchiveToolStripMenuItem.Checked = value == ArchivesInArchiveMode.Ignore;
                    onlyIfThereIsNoOtherImageToolStripMenuItem.Checked = value == ArchivesInArchiveMode.IfNoOther1Level;
                    untilFoundToolStripMenuItem.Checked = value == ArchivesInArchiveMode.UntilFound2Level;
                    alwaysLoadArchivesInArchiveToolStripMenuItem.Checked = value == ArchivesInArchiveMode.Always1Level;
                }
            }
        }

        private ReadOnMemoryMode readOnMemoryMode = ReadOnMemoryMode.None;
        private ReadOnMemoryMode ReadOnMemoryMode
        {
            get => readOnMemoryMode;
            set
            {
                if (value != readOnMemoryMode)
                {
                    readOnMemoryMode = value;
                    switch (value)
                    {
                        case ReadOnMemoryMode.None:
                            loadWholeArchiveIntoMemoryToolStripMenuItem.Checked = false;
                            exceptReadAheadProcessToolStripMenuItem.Enabled = false;
                            break;
                        case ReadOnMemoryMode.ExceptLookAhead:
                            loadWholeArchiveIntoMemoryToolStripMenuItem.Checked = true;
                            exceptReadAheadProcessToolStripMenuItem.Enabled = true;
                            exceptReadAheadProcessToolStripMenuItem.Checked = true;
                            break;
                        case ReadOnMemoryMode.Always:
                            loadWholeArchiveIntoMemoryToolStripMenuItem.Checked = true;
                            exceptReadAheadProcessToolStripMenuItem.Enabled = true;
                            exceptReadAheadProcessToolStripMenuItem.Checked = false;
                            break;
                    }
                }
            }
        }

        private bool openInPreviousImageFilterSetting = true;
        private bool OpenInPreviousImageFilterSetting
        {
            get
            {
                return openInPreviousImageFilterSetting;
            }
            set
            {
                if (value != openInPreviousImageFilterSetting)
                {
                    openInPreviousImageFilterSetting = value;
                    startInPreviousImageFilterSettingToolStripMenuItem.Checked = value;
                }
            }
        }


        private InitialFullscreenMode builtInViewerInitialFullscreenMode = InitialFullscreenMode.Default;
        private InitialFullscreenMode BuiltInViewerInitialFullscreenMode
        {
            get
            {
                return builtInViewerInitialFullscreenMode;
            }
            set
            {
                SetBuiltInViewerInitialFullscreenMode(value, silent: false);
            }
        }

        private void SetBuiltInViewerInitialFullscreenMode(InitialFullscreenMode value, bool silent)
        {
            if (builtInViewerInitialFullscreenMode != value)
            {
                builtInViewerInitialFullscreenMode = value;
                startInPreviousFullscreenModeToolStripMenuItem.Checked = value == InitialFullscreenMode.Default;
                alwaysStartInWindowModeToolStripMenuItem.Checked = value == InitialFullscreenMode.ForceWindow;
                openInFullscreenToolStripMenuItem.Checked = value == InitialFullscreenMode.ForceFullscreen;

            }
        }

        private SelectionHighlightMode selectionHilightMode = SelectionHighlightMode.WhenMouseLeaves;
        private SelectionHighlightMode SelectionHilightMode
        {
            get
            {
                return selectionHilightMode;
            }
            set
            {
                if (value != selectionHilightMode)
                {
                    selectionHilightMode = value;
                    hilightSelectedItemsWhenMouseLeavaesToolStripMenuItem.Checked = value == SelectionHighlightMode.WhenMouseLeaves;
                    if (value == SelectionHighlightMode.None) unselectedItemMaskOff();
                }
            }
        }

        private DoubleClickMode doubleClickFileOpenMode = DoubleClickMode.IgnoreMouseMove;
        private DoubleClickMode DoubleClickFileOpenMode
        {
            get
            {
                return doubleClickFileOpenMode;
            }
            set
            {
                if (value != doubleClickFileOpenMode)
                {
                    doubleClickFileOpenMode = value;
                    standardToolStripMenuItem.Checked = value == DoubleClickMode.Standard;
                    ignoreMouseMovementToolStripMenuItem.Checked = value == DoubleClickMode.IgnoreMouseMove;
                    selectAndClickItemToolStripMenuItem.Checked = value == DoubleClickMode.SelectAndClick;
                }
            }
        }

        private bool sendFilterStringToBuiltInViewer = true;
        private bool SendFilterStringToBuiltInViewer
        {
            get
            {
                return sendFilterStringToBuiltInViewer;
            }
        }

        private bool tinyOnScreenKeyboardVisible = false;
        private bool TinyOnScreenKeyboardVisible
        {
            get
            {
                return tinyOnScreenKeyboardVisible;
            }
            set
            {
                if (tinyOnScreenKeyboardVisible != value)
                {
                    setTinyOnScreenKeyboardVisible(value);
                }
            }
        }

        private void setTinyOnScreenKeyboardVisible(bool visible)
        {
            tinyOnScreenKeyboardVisible = visible;
            tinyOnscreenKeyboardToolStripMenuItem.Checked = visible;
            simplifiedKeyBoard.SetVisible(visible);
        }

        int undoBufferIndex = -1;
        readonly List<UndoBufferClass> undoBuffer = new List<UndoBufferClass>();
        class UndoBufferClass
        {
            public string Location = null;
            public double ThumbnailPosition = -1;
            public int FileListPosition = -1;
            public string SelectedFileName = null;
            public Color ProfileColor;
            public CatalogProfile Profile;
        }

        int maxCaptchaCountInAVideo = 0;
        int MaxCaptchaCountInAVideo
        {
            get { return maxCaptchaCountInAVideo; }
            set
            {
                if (value != maxCaptchaCountInAVideo)
                {
                    maxCaptchaCountInAVideo = value;
                    fitOnTheScreenToolStripMenuItem.Checked = maxCaptchaCountInAVideo == 0;
                    thumb6ToolStripMenuItem.Checked = maxCaptchaCountInAVideo == 6;
                    thumb12ToolStripMenuItem.Checked = maxCaptchaCountInAVideo == 12;
                    thumb24ToolStripMenuItem.Checked = maxCaptchaCountInAVideo == 24;
                    thumb60ToolStripMenuItem.Checked = maxCaptchaCountInAVideo == 60;
                    thumb120ToolStripMenuItem.Checked = maxCaptchaCountInAVideo == 120;
                    thumb240ToolStripMenuItem.Checked = maxCaptchaCountInAVideo == 240;
                    thumb600ToolStripMenuItem.Checked = maxCaptchaCountInAVideo == 600;
                    thumb1200ToolStripMenuItem.Checked = maxCaptchaCountInAVideo == 1200;
                    thumb2400ToolStripMenuItem.Checked = maxCaptchaCountInAVideo == 2400;
                    everySecondsToolStripMenuItem.Checked = maxCaptchaCountInAVideo == int.MaxValue;
                }
            }
        }

        private Color currentProfileColor_BackColor;
        private Color currentProfileColor_ForeColor;
        private Brush currentProfileColor_BackBrush = null;
        private Brush currentProfileColor_ForeBrush = null;
        private Color currentProfileColor
        {
            get
            {
                return currentProfileColor_BackColor;
            }

            set
            {
                currentProfileColor_BackColor = value;
                currentProfileColor_ForeColor = ZipTag.GetForeColor(currentProfileColor_BackColor);
                if (currentProfileColor_BackBrush != null) currentProfileColor_BackBrush.Dispose();
                if (currentProfileColor_ForeBrush != null) currentProfileColor_ForeBrush.Dispose();
                currentProfileColor_BackBrush = new SolidBrush(currentProfileColor_BackColor);
                currentProfileColor_ForeBrush = new SolidBrush(currentProfileColor_ForeColor);

                //setMenuItemColor();
                menuStrip.BackColor = currentProfileColor_BackColor;
                menuStripForTagFilter.BackColor = currentProfileColor_BackColor;
                //menuStripLeft.BackColor = currentProfileColor_BackColor;
            }
        }

        private double currentMinimumFrameThickness
        {
            get
            {
                if (zeroToolStripMenuItem.Checked) return 0;
                if (veryThinToolStripMenuItem.Checked) return 1;
                if (thinToolStripMenuItem.Checked) return 2;
                if (thickToolStripMenuItem.Checked) return 8;
                if (veryThickToolStripMenuItem.Checked) return 16;
                return 4;
            }
            set
            {
                ToolStripMenuItem tsmi;
                double selectedValue;
                if (value < 0.5)
                {
                    tsmi = zeroToolStripMenuItem;
                    selectedValue = 0;
                }
                else if (value < 1.5)
                {
                    tsmi = veryThinToolStripMenuItem;
                    selectedValue = 1;
                }
                else if (value < 3)
                {
                    tsmi = thinToolStripMenuItem;
                    selectedValue = 2;
                }
                else if (value < 6)
                {
                    tsmi = normalThicknessToolStripMenuItem;
                    selectedValue = 4;
                }
                else if (value < 12)
                {
                    tsmi = thickToolStripMenuItem;
                    selectedValue = 8;
                }
                else
                {
                    tsmi = veryThickToolStripMenuItem;
                    selectedValue = 16;
                }

                zeroToolStripMenuItem.Checked = zeroToolStripMenuItem == tsmi;
                veryThinToolStripMenuItem.Checked = veryThinToolStripMenuItem == tsmi;
                thinToolStripMenuItem.Checked = thinToolStripMenuItem == tsmi;
                normalThicknessToolStripMenuItem.Checked = normalThicknessToolStripMenuItem == tsmi;
                thickToolStripMenuItem.Checked = thickToolStripMenuItem == tsmi;
                veryThickToolStripMenuItem.Checked = veryThickToolStripMenuItem == tsmi;
                tvCatalog.ThumbnailPadding = Program.DpiScaling(ChangeThumbnailSize_Padding * selectedValue);
            }
        }

        // Color の == は同じ RGB でも名前が違うと別のものになってしまうため
        private static bool ColorEquals(Color a, Color b)
        {
            return a.R == b.R && a.G == b.G && a.B == b.B;
        }

        private Color backColorOfBuiltInViewer = Color.Black; // Name の変更
        private Color BackColorOfBuiltInViewer
        {
            get
            {
                return backColorOfBuiltInViewer;
            }
            set
            {
                if (!ColorEquals(value, backColorOfBuiltInViewer))
                {
                    if (ColorEquals(value, Color.Black))
                    {
                        backColorOfBuiltInViewer = Color.Black;
                        blackToolStripMenuItem.Checked = true;
                        grayToolStripMenuItem.Checked = false;
                        whiteToolStripMenuItem.Checked = false;
                        selectColorToolStripMenuItem.Checked = false;
                    }
                    else if (ColorEquals(value, Color.Gray))
                    {
                        backColorOfBuiltInViewer = Color.Gray;
                        blackToolStripMenuItem.Checked = false;
                        grayToolStripMenuItem.Checked = true;
                        whiteToolStripMenuItem.Checked = false;
                        selectColorToolStripMenuItem.Checked = false;
                    }
                    else if (ColorEquals(value, Color.White))
                    {
                        backColorOfBuiltInViewer = Color.White;
                        blackToolStripMenuItem.Checked = false;
                        grayToolStripMenuItem.Checked = false;
                        whiteToolStripMenuItem.Checked = true;
                        selectColorToolStripMenuItem.Checked = false;
                    }
                    else
                    {
                        backColorOfBuiltInViewer = value;
                        SelectedColorForBackColorOfBuiltInViewer = value;
                        blackToolStripMenuItem.Checked = false;
                        grayToolStripMenuItem.Checked = false;
                        whiteToolStripMenuItem.Checked = false;
                        selectColorToolStripMenuItem.Checked = true;
                    }
                }
            }
        }
        private Color selectedColorForBackColorOfBuiltInViewer = Color.LightSalmon;
        private Color SelectedColorForBackColorOfBuiltInViewer
        {
            get
            {
                return selectedColorForBackColorOfBuiltInViewer;
            }
            set
            {
                if (!ColorEquals(value, selectedColorForBackColorOfBuiltInViewer))
                {
                    selectedColorForBackColorOfBuiltInViewer = value;
                    selectColorToolStripMenuItem.BackColor = value;
                }
            }
        }


        private void setCurrentMinimumFrameThickness(ToolStripMenuItem tsmi)
        {
            zeroToolStripMenuItem.Checked = zeroToolStripMenuItem == tsmi;
            veryThinToolStripMenuItem.Checked = veryThinToolStripMenuItem == tsmi;
            thinToolStripMenuItem.Checked = thinToolStripMenuItem == tsmi;
            normalThicknessToolStripMenuItem.Checked = normalThicknessToolStripMenuItem == tsmi;
            thickToolStripMenuItem.Checked = thickToolStripMenuItem == tsmi;
            veryThickToolStripMenuItem.Checked = veryThickToolStripMenuItem == tsmi;

            tvCatalog.ThumbnailPadding = Program.DpiScaling(ChangeThumbnailSize_Padding * currentMinimumFrameThickness);
        }


        private ThumbViewerAligningMode currentAligning
        {
            get
            {
                return tvCatalog.LayoutMode;
            }
            set
            {
                totallyUniformedToolStripMenuItem.Checked = value == ThumbViewerAligningMode.TotallyUniformedGrid;
                horizontallyUniformedToolStripMenuItem.Checked = value == ThumbViewerAligningMode.HorizontallyUniformedGrid;
                alignedLeftToolStripMenuItem.Checked = value == ThumbViewerAligningMode.LeftAlignedGrid;
                alignedOnCenterToolStripMenuItem.Checked = value == ThumbViewerAligningMode.CenteredGrid;
                tvCatalog.LayoutMode = value;
            }
        }

        private readonly Dictionary<object, HashSet<string>> OpendByExternalApplication_Dictionary = new Dictionary<object, HashSet<string>>();
        private bool IsOpendByExternalApplication(string fullPath)
        {
            if (fullPath == null) return false;
            fullPath = fullPath.ToLower();
            return OpendByExternalApplication_Dictionary.Any(pair => pair.Value.Contains(fullPath));
        }

        public async void SetOpendByExternalApplication(Process p, IEnumerable<string> fullPathSet, bool selectionMirror)
        {
            if (p == null || p.HasExited || fullPathSet == null)
            {
                p?.Dispose();
                return;
            }
            var set = new HashSet<string>(from path in fullPathSet where !string.IsNullOrEmpty(path) select path.ToLower());
            if (!set.Any())
            {
                p.Dispose();
                return;
            }
            OpendByExternalApplication_Dictionary[p] = set;

            if (selectionMirror) selectionMirror = set.Count == 1;
            string fullPath = null;
            string name = null;
            string parent = null;
            if (selectionMirror)
            {
                fullPath = fullPathSet.First(path => !string.IsNullOrEmpty(path));
                if (!fullPath.Contains(Path.AltDirectorySeparatorChar))
                {
                    var i = fullPath.LastIndexOf(Path.DirectorySeparatorChar);
                    if (0 < i && i < fullPath.Length - 1)
                    {
                        parent = fullPath.Substring(0, i + 1);
                        name = fullPath.Substring(i + 1);
                    }
                    else selectionMirror = false;
                }
                else selectionMirror = false;
            }
            if (selectionMirror)
            {
                string prevTitle = null;
                string prevSelect = null;
                string prePath = null, postPath = null, preName = null, postName = null;
                while (!await Task.Run(() => p.WaitForExit(100)))
                {
                    string title = null;
                    bool active = false;
                    try
                    {
                        var p2 = Process.GetProcessById(p.Id);
                        if (p2 != null)
                        {
                            using (p2)
                            {
                                active = p2.MainWindowHandle == GetForegroundWindow();
                                title = p2.MainWindowTitle;
                            }
                        }
                    }
                    catch
                    {
                    }
                    var changed = title != null && title != prevTitle;
                    prevTitle = title;
                    if (changed)
                    {
                        string current = null;
                        var doneSomething = false;
                        if (prePath == null)
                        {
                            var pos = title.IndexOf(fullPath);
                            if (pos >= 0)
                            {
                                prePath = title.Substring(0, pos);
                                postPath = title.Substring(pos + fullPath.Length);
                                doneSomething = true;
                            }
                        }
                        else if (title.Length > prePath.Length + postPath.Length && title.StartsWith(prePath) && title.EndsWith(postPath))
                        {
                            current = title.Substring(prePath.Length, title.Length - prePath.Length - postPath.Length);
                            doneSomething = true;
                        }

                        if (!doneSomething)
                        {
                            if (preName == null)
                            {
                                var pos = title.IndexOf(name);
                                if (pos >= 0)
                                {
                                    preName = title.Substring(0, pos);
                                    postName = title.Substring(pos + name.Length);
                                }
                            }
                            else if (title.Length > preName.Length + postName.Length && title.StartsWith(preName) && title.EndsWith(postName))
                            {
                                current = parent + title.Substring(preName.Length, title.Length - preName.Length - postName.Length);
                            }
                        }

                        var selectChange = active && current != null && current != prevSelect;
                        prevSelect = current;
                        if (selectChange && (ItemSelectionFeedback == ItemSelectionFeedback.Always ||
                            ItemSelectionFeedback == ItemSelectionFeedback.IfSortMayBeSame && sortMode == SortMode.NameInAsc && IsNonvolatileSort()))
                        {
                            var zpa = ZipPathArray;
                            var s2d = tvCatalog.ShowIndexToDataIndex;
                            if (zpa != null && s2d != null)
                            {
                                // できるだけ丁寧に無駄な走査をしないようチェック
                                if (InMovie || InArchive)
                                {
                                    continue;
                                }
                                if (!(InSmartDirectory || InVirtualDirectory))
                                {
                                    var cd = currentLocation?.ToLower();
                                    if (string.IsNullOrEmpty(cd))
                                    {
                                        continue;
                                    }
                                    var last = cd.Last();
                                    if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
                                    {
                                        cd = cd.Substring(0, cd.Length - 1);
                                    }
                                    string currentParent;
                                    try
                                    {
                                        currentParent = Path.GetDirectoryName(current).ToLower();

                                        // ルートの場合に必要な処理
                                        if (currentParent[currentParent.Length - 1] == Path.DirectorySeparatorChar)
                                        {
                                            currentParent = currentParent.Substring(0, currentParent.Length - 1);
                                        }
                                    }
                                    catch
                                    {
                                        continue;
                                    }
                                    if (currentParent != cd)
                                    {
                                        continue;
                                    }
                                }

                                var count = zpa.Length;
                                var index = -1;
                                var showIndex = -1;
                                foreach (var i in s2d)
                                {
                                    showIndex++;
                                    if (i < 0 || i >= count) break;
                                    else if (current == zpa[i])
                                    {
                                        index = i;
                                        break;
                                    }
                                }
                                if (index >= 0)
                                {
                                    tvCatalog.SelectedIndex = index;
                                    tvCatalog.FocusedIndex = index;
                                    tvCatalog.ScrollBarToIndexWithMinimalMove(showIndex);
                                    // 詳細表示は自動的に連動する
                                }
                            }
                        }
                    }
                }
                //MessageBox.Show("exit");
                OpendByExternalApplication_Dictionary.Remove(p);
                p.Dispose();

                /*
                */
            }
            else
            {
                await Task.Run(() => p.WaitForExit()); // エクスプローラーを読んだ場合ここには到達するがすぐに終了する
                OpendByExternalApplication_Dictionary.Remove(p);
                p.Dispose();
            }
        }
        public void AddToOpendByExternalApplicationDictionary(object key, IEnumerable<string> path)
        {
            if (path == null) return;
            var set = new HashSet<string>(from p in path where !string.IsNullOrEmpty(p) select p.ToLower());
            if (!set.Any()) return;
            OpendByExternalApplication_Dictionary[key] = set;
        }
        public void RemoveFromOpendByExternalApplicationDictionary(object key)
        {
            OpendByExternalApplication_Dictionary.Remove(key);
        }

        private SortMode sortMode = 0;
        private SortMode prevSortMode = 0;

        // 外部プログラムの起動で使う
        public string GetSortModeString() { return IsNonvolatileSort() ? sortMode.ToString() : "Indeterminate"; }

        private bool IsNonvolatileSort()
        {
            var result = false;
            try
            {
                Invoke(((MethodInvoker)(() =>
                {
                    result = cbSortBy.SelectedIndex >= 0;
                })));
            }
            catch (ObjectDisposedException) { }
            return result;
        }

        private ZipTag[] Tags;

        private ApplicationProvider[] ApplicationArray;

        private double aspectRatio = 1 / Math.Sqrt(2);

        private MyClipboardViewer clipboardViewer;

        public CatalogForm()
        {
#if RUNTIME
            Program.RunTimeMeasure.Block("StartConstructor");
#endif
            Constructor();
#if RUNTIME
            Program.RunTimeMeasure.Block("StartLoadSettings");
#endif

            LoadSettings(null);
        }

        public CatalogForm(string path, SortMode? sortMode = null, SortMode? preSortMode = null, string randomSeed = null)
        {
            Constructor();
            try
            {
                path = Program.GetFullPath(path);

                if (IsSupportedByCatalogFormAndExists(path))
                {
                    LoadSettings(path, sortModeOption: sortMode);
                    return;
                }
                //MessageBox.Show("1");
                var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);

                if (altPos < 0 || altPos >= path.Length - 1)
                {
                    //LoadSettings(path, sortModeOption: sortMode);
                    LoadSettings(Path.GetDirectoryName(path), getFileName(path), sortModeOption: sortMode);
                    return;
                }
                //MessageBox.Show("2");

                var parent = path.Substring(0, altPos);
                if (IsSupportedByCatalogFormAndExists(parent))
                {
                    LoadSettings(parent, path.Substring(altPos + 1), sortMode);
                    return;
                }
                //MessageBox.Show("3");

                LoadSettings(path, sortModeOption: sortMode);
            }
            catch
            {
                LoadSettings(sortModeOption: sortMode, preSortModeOption: preSortMode, randomSeedOption: randomSeed);
            }

        }

        private bool showStartHelpMessage = false;
        private bool showStartHelpMessageOfDirectoryList = false;
        private bool IsShown = false;
        // エクスプローラーなどから開いた本を受け付ける（S7）
        private OpenRequestServer openRequestServer;
        private bool OpenFromOtherProcess(string path)
        {
            if (OpenWaitingProcess_Processing || IsDisposed) return false; // 別の本を開いている途中なら断る（依頼した側が自分で開く）
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            OpenWaitingProcess(path, -1, false, fromOtherProcess: true);
            return true;
        }

        private void CatalogForm_Shown(object sender, EventArgs e)
        {
            if (openRequestServer == null) openRequestServer = new OpenRequestServer(this, OpenFromOtherProcess);
#if RUNTIME
            Program.RunTimeMeasure?.Block("SetTagFilteringMenuItems");
#endif

            IsShown = true;
            thumbnailListToolStripMenuItem_CheckedChanged(null, null);
            //tbcIcon.Width = tbcIcon.HeaderCell.Size.Height; // tbcIcon.HeaderCell.Size.Height を使う場合ここで必要
            foreach (DataGridViewRow row in dgvDirectoryList.Rows) row.Selected = false;
            setTagFilteringMenuItems(needToRepairTagsFromBookmark: true, needToSaveCurrentTag: true, loadFromIni: true);

#if RUNTIME
            Program.RunTimeMeasure?.Block("SetEraserAndSearchIcon");
#endif

            // 高解像度環境でコンテクストメニューの高さを正しく得るためにはここで実行する必要がある
            setEraserAndSearchIcon();

            HintManager.ShowDialog(this, () =>
            {
                // ヒントを表示する場合のみ実行
                zabLocation.MakeButtons();

            }); // preAction は見栄えの問題

            if (firstCatalogFormRun)
            {
                using (var thumbnailCacheSettingForm = new ThumbnailCacheSettingForm(thumbnailCache, currentProfileColor, firstSetting: true))
                {
                    thumbnailCacheSettingForm.ShowDialog(this);
                    thumbnailCache = thumbnailCacheSettingForm.CacheRoot;
                }
            }

#if RUNTIME
            Program.RunTimeMeasure?.Block("SetCheckMarks");
#endif
            CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(howToDoubleClickToolStripMenuItem.DropDown, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(layoutToolStripMenuItem.DropDown, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(archivesInArchiveToolStripMenuItem.DropDown, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(defaultPageSequenceToolStripMenuItem.DropDown, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(arrangeFoldersToolStripMenuItem.DropDown, noneFolderArrangeToolStripMenuItem, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(arrangeFoldersToolStripMenuItem.DropDown, displayFirstToolStripMenuItem, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(arrangeFoldersToolStripMenuItem.DropDown, displayLastToolStripMenuItem, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(arrangeFoldersToolStripMenuItem.DropDown, fitToDirectionOfSortToolStripMenuItem, DisplayCheckMark.Select);
            //CheckMarkProvider.SetCheckMark(arrangeFoldersToolStripMenuItem.DropDown, getFolderSizeToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(itemSelectionFeedbackToolStripMenuItem.DropDown, DisplayCheckMark.Select);

            CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, archivesFilesPDFFielsToolStripMenuItem, DisplayCheckMark.CheckThreeState);
            CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, imageFilesToolStripMenuItem, DisplayCheckMark.CheckThreeState);
            CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, videoFilesToolStripMenuItem, DisplayCheckMark.CheckThreeState);
            CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, otherFilesToolStripMenuItem, DisplayCheckMark.CheckThreeState);
            CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, foldersToolStripMenuItem, DisplayCheckMark.CheckThreeState);

            CheckMarkProvider.SetCheckMark(folderTreeSettingsToolStripMenuItem.DropDown, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(folderTreeSettingsToolStripMenuItem.DropDown, collapseOthersWhenSynchronizingToolStripMenuItem, DisplayCheckMark.Check);

            CheckMarkProvider.SetCheckMark(backgroundColorOfBuiltinViewerToolStripMenuItem.DropDown, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(backgroundColorOfBuiltinViewerToolStripMenuItem.DropDown, forceFirstPageToBeSingleToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(backgroundColorOfBuiltinViewerToolStripMenuItem.DropDown, startInPreviousImageFilterSettingToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(backgroundColorOfBuiltinViewerToolStripMenuItem.DropDown, alwaysAutomaticallyHideUIToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(backgroundColorOfBuiltinViewerToolStripMenuItem.DropDown, sortFilesWithTheSameOrderAsThumbnailWindowToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(backgroundColorOfBuiltinViewerToolStripMenuItem.DropDown, allowReadaheadProcessToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(backgroundColorOfBuiltinViewerToolStripMenuItem.DropDown, loadWholeArchiveIntoMemoryToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(backgroundColorOfBuiltinViewerToolStripMenuItem.DropDown, exceptReadAheadProcessToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(contextMenuSelectToolStripMenuItem.DropDown, DisplayCheckMark.Select);
            //CheckMarkProvider.SetCheckMark(backgroundColorOfBuiltinViewerToolStripMenuItem.DropDown, alwaysStartInWindowModeToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(maxCaptchaCountInAVideoToolStripMenuItem.DropDown, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(thumbnailSizeToolStripMenuItem.DropDown, DisplayCheckMark.Select);
            CheckMarkProvider.SetCheckMark(thumbnailSizeToolStripMenuItem.DropDown, showRatingToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(thumbnailSizeToolStripMenuItem.DropDown, showIconToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(thumbnailSizeToolStripMenuItem.DropDown, showFilenameToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(thumbnailSizeToolStripMenuItem.DropDown, showTagsToolStripMenuItem, DisplayCheckMark.Check);
            //CheckMarkProvider.SetCheckMark(thumbnailSizeToolStripMenuItem.DropDown, createCacheInADSInNTFSToolStripMenuItem, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(cmsFileListHeader, DisplayCheckMark.Check);
            CheckMarkProvider.SetCheckMark(ratingToolStripMenuItem.DropDown, DisplayCheckMark.SelectThreeState);
            CheckMarkProvider.SetCheckMark(pageSequenceToolStripMenuItem.DropDown, DisplayCheckMark.SelectThreeState);


            if (emptyStartLocation)
            {
                showStartHelpMessage = true;
                tvCatalog.Invalidate();
            }

            if (dgvDirectoryList.RowCount == 0)
            {
                showStartHelpMessageOfDirectoryList = true;
                dgvDirectoryList.Invalidate();
            }

#if RUNTIME
            Program.RunTimeMeasure?.Block("StartCheckUpdate");
#endif

            Configuration.ClosePrepareMode();
            
            betterFormRestoreBounds = new BetterFormRestoreBounds(this);

            Program.StartCheckUpdateAndNgen(this, checkNgen);
            //PackedImageLoader.CheckSevenZipExistence(this); フォルダ読み込み時にチェックされるので不要


#if RUNTIME
            Program.RunTimeMeasure.Dispose();
            Program.RunTimeMeasure = null;
#endif
        }

        private ToolStripDropDown ratingFilterToolStripMenuItemDropDown;
        private ToolStripDropDown addToolStripMenuItemDropDown;

#if DEBUG
        // リリース版で有効にする場合リロードへの対処やエラー処理を記述すること
        private void backupFilepath_Click(object sender, EventArgs e)
        {
            if (ZipPathArray == null) return;
            var sizeToPath = new Dictionary<long, string>();
            for (var i = 0; i < ZipPathArray.Length; i++)
            {
                var path = ZipPathArray[i];
                var size = ZipSizeArray[i];
                if (!sizeToPath.ContainsKey(size))
                {
                    sizeToPath[size] = path;
                }
                else
                {
                    MessageBox.Show(this, $"File size duplicates.\n\n{ Program.GetFormatSizeString(size, "Bytes")}\n{sizeToPath[size]}\n{path}");
                    return;
                }
            }

            if (sizeToPath.Count == 0) return;

            string savePath = null;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "*.txt|*.txt";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    savePath = dialog.FileName;
                }
            }

            if (savePath != null)
            {
                using (var writer = new StreamWriter(savePath, append: false))
                {
                    foreach (var pair in sizeToPath)
                    {
                        writer.WriteLine(pair.Key + "\t" + pair.Value);
                    }
                }
            }
        }

        // （予定はないが）リリース版で有効にする場合リロードへの対処やエラー処理を記述すること
        private void saveIntensiveLines_Click(object sender, EventArgs e)
        {
            try
            {
                var path = ZipPathArray[tvCatalog.SelectedIndex];
                using (var target = new Bitmap(path))
                using (var intensiveLines = BitmapAnalyzer.GetIntensiveLines(target, pixelRatio: 1))
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Filter = "PNG files|*.png";
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        intensiveLines.Save(dialog.FileName);
                    }
                }
            }
            catch { }
        }

        private void filterPerformanceTest_Click(object sender, EventArgs e)
        {
            var s = cbFilter.Text;

            var re = new Regex(Regex.Escape(s), RegexOptions.IgnoreCase);

            var rec = new Regex(Regex.Escape(s), RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var names = ZipNameArray.Where(name => name != null).ToArray();

            var sw = new Stopwatch();
            var ss = "";
            var count = 0;
            sw.Start();

            count = 0;
            foreach (var name in names)
            {
                if (name.Contains(s)) count++;
            }
            sw.Stop(); ss += $"plain:\t{sw.Elapsed}\n"; sw.Restart();


            count = 0;
            foreach (var name in names)
            {
                if (re.IsMatch(name)) count++;
            }
            sw.Stop(); ss += $"notcmp:\t{sw.Elapsed}\n"; sw.Restart();

            count = 0;
            foreach (var name in names)
            {
                if (rec.IsMatch(name)) count++;
            }
            sw.Stop(); ss += $"cmp:\t{sw.Elapsed}\n"; MessageBox.Show(ss);
        }
#endif
        int tbcIconWidth, tbcMargin;

        Font MenuTextFont = null;


        public static bool MenuTextFontShouldBeReplaced()
        {
            // Windows 10 CreatorsUpdate で「テキストサイズのみを変更する」が廃止されたので仕様変更
            // OS のバージョンにかかわらず、一部の外部ツールにせていされたフォントを拾うことができる
            return false;

            // Windows 10 の「テキストサイズのみを変更する」の設定を拾うためのコード
        }

        private GestureListener gestureListener;
        private GestureListener gestureListenergCmsRightRoot;
        private GestureListener gestureListenergCmsRightTag;
        private MiniControlTouchGesture mctgCatalogForm;
        private MiniControlTouchGesture mctgCmsRightRoot;
        private MiniControlTouchGesture mctgCmsRightTag;

        private BetterFormRestoreBounds betterFormRestoreBounds;
        private new Rectangle RestoreBounds { get => betterFormRestoreBounds?.BetterRestoreBounds ?? base.RestoreBounds; }

        private void Constructor()
        {
            Configuration.PrepareXmlSerializer(
                typeof(CatalogFormConfig),
                typeof(ColoredBookmarkConfig),
                typeof(GeneralConfig),
                typeof(ZipTagConfig));


            if (Program.CheckConfigException())
            {
                CatalogForm_FormClosing_FormClosed = true;
                Close();
            }

#if RUNTIME
            Program.RunTimeMeasure.Block("InitializeComponent");
#endif
            InitializeComponent();
            fileSystemWatcher.Error += fileSystemWatcher_Error; // Y16


#if RUNTIME
            Program.RunTimeMeasure.Block("WaitPrepareTask");
#endif

            Configuration.WaitPrepareTask();

#if DEBUG
            // ファイル名のバックアップ
            var backupFilepath = new ToolStripMenuItem("Backup filepath");
            backupFilepath.Click += backupFilepath_Click;
            cmsRightClick.Items.Insert(cmsRightClick.Items.IndexOf(pasteToolStripMenuItem) + 1, backupFilepath);

            // 集中線の出力
            var saveIntensiveLines = new ToolStripMenuItem("Save intensive lines");
            saveIntensiveLines.Click += saveIntensiveLines_Click;
            cmsRightClick.Items.Insert(cmsRightClick.Items.IndexOf(backupFilepath) + 1, saveIntensiveLines);

            // フィルタ実験
            var filterPerformanceTest = new ToolStripMenuItem("Filter performance test");
            filterPerformanceTest.Click += filterPerformanceTest_Click;
            cmsRightClick.Items.Insert(cmsRightClick.Items.IndexOf(saveIntensiveLines) + 1, filterPerformanceTest);
#endif

#if RUNTIME
            Program.RunTimeMeasure.Block("PrepareTouch");
#endif
            try
            {
                gestureListener = new GestureListener(this, new GestureConfig[] {
                    new GestureConfig(3, 1, 0), // ズーム
                    new GestureConfig(4, 2 | 4 | 16 , 8 ), // パン、向き拘束なし
                    
                    new GestureConfig(5, 1, 0), // 回転
                    new GestureConfig(6, 1, 0), // ツーフィンガータップ
                    new GestureConfig(7, 1, 0), // プレスアンドタップ

                });

                new DataGridViewScrollBarTouchFixer(dgvFileList, gestureListener);
                new DataGridViewScrollBarTouchFixer(dgvDirectoryList, gestureListener);

                mctgCatalogForm = new MiniControlTouchGesture(this, gestureListener, onlyHorizontalStart: false);
                var target = mctgCatalogForm.Targets;
                target.Add(menuStripLeft);
                target.Add(menuStripForTagFilter);
                target.Add(cbFilter);
                target.Add(btnGoToBack);
                target.Add(btnGoToForward);
                target.Add(tvCatalog);
                target.Add(dgvFileList);
                mctgCatalogForm.TouchGestureStarting += mctgCatalogForm_TouchGestureStarting;
                mctgCatalogForm.TouchGestureCompleted += mctgCatalogForm_TouchGestureCompleted;

                gestureListener.Zoom += gestureListener_Zoom;
                gestureListener.Pan += gestureListener_Pan;
                gestureListener.Rotate += gestureListener_Rotate;
                gestureListener.TwoFingerTap += gestureListener_TwoFingerTap;
                gestureListener.PressAndTap += gestureListener_PressAndTap;

                gestureListenergCmsRightRoot = MiniControlTouchGesture.GetGestureListener(cmsRightClick);
                mctgCmsRightRoot = new MiniControlTouchGesture(cmsRightClick, gestureListenergCmsRightRoot, onlyHorizontalStart: true);
                mctgCmsRightRoot.Targets.Add(cmsRightClick);
                mctgCmsRightRoot.TouchGestureStarting += TouchGestureStartingForTagMenu;
                mctgCmsRightRoot.TouchGestureCompleted += TouchGestureCompletedForTagMenu;
                gestureListenergCmsRightTag = MiniControlTouchGesture.GetGestureListener(tagToolStripMenuItem.DropDown);
                mctgCmsRightTag = new MiniControlTouchGesture(tagToolStripMenuItem.DropDown, gestureListenergCmsRightTag, onlyHorizontalStart: true);
                mctgCmsRightTag.Targets.Add(tagToolStripMenuItem.DropDown);
                mctgCmsRightTag.TouchGestureStarting += TouchGestureStartingForTagMenu;
                mctgCmsRightTag.TouchGestureCompleted += TouchGestureCompletedForTagMenu;
            }
            catch { }

#if RUNTIME
            Program.RunTimeMeasure.Block("PrepareKeyMouseBinder");
#endif

            //ratingToolStripMenuItem.DropDown.KeyDown += ratingToolStripMenuItem_DropDown_KeyDown;
            new KeyMouseBinder(cmsRightClick, requiredKeyMouseBindingCmsRightRootOrTag);
            new KeyMouseBinder(tagToolStripMenuItem.DropDown, requiredKeyMouseBindingCmsRightRootOrTag);
            new KeyMouseBinder(ratingToolStripMenuItem.DropDown, requiredKeyMouseBindingCmsTrue);
            new KeyMouseBinder(pageSequenceToolStripMenuItem.DropDown, requiredKeyMouseBindingCmsTrue);


            tvCatalog.GotFocus += tvCatalog_GotFocus;
            dgvDirectoryList.GotFocus += dgvDirectoryList_GotFocus;
            dgvFileList.GotFocus += dgvFileList_GotFocus;
            explorerTreeView.TreeViewWnd.GotFocus += explorerTreeView_TreeViewWnd_GotFocus;

#if RUNTIME
            Program.RunTimeMeasure.Block("Replace font etc");
#endif

            if (MenuTextFontShouldBeReplaced())
            {
                MenuTextFont = new Font(menuStrip.Font.FontFamily, DefaultFont.SizeInPoints);
            }

            if (MenuTextFont != null)
            {
                menuStrip.Font = MenuTextFont;
                menuStripForTagFilter.Font = MenuTextFont;
                statusStrip.Font = MenuTextFont;
                menuStripLeft.Font = MenuTextFont;
            }

            ZipPlaAddressBar.AddDrawToolStripMenuItemTriangleEventHandler(menuStrip);
            ZipPlaAddressBar.AddDrawToolStripMenuItemTriangleEventHandler(cmsRightClick);
            tagToolStripMenuItem.Paint += ZipPlaAddressBar.DrawToolStripMenuItemTriangle; // この時点では子を持たないので自動的には追加されない
            allRatiosToolStripMenuItem.Paint += ZipPlaAddressBar.DrawToolStripMenuItemTriangle; // この時点では子を持たないので自動的には追加されない

            tvCatalog.DragOutSelection = true;
            tvCatalog.DragOutSingle = true;
            dgvFileList.DragOutSelection = true;
            dgvFileList.DragOutSingle = true;
            // K6: 仮想モード。行は共有され、値は CellValueNeeded で表示する行の分だけ作る
            // 独自のセルは行のテンプレートに入れる（tbcIcon は画像の列なので CellTemplate にはテキストのセルを入れられない）
            var fileListRowTemplate = new DataGridViewRow();
            fileListRowTemplate.CreateCells(dgvFileList);
            fileListRowTemplate.Cells[tbcFileName.Index] = new RainbowDataGridViewTextBoxCell();
            fileListRowTemplate.Cells[tbcRating.Index] = new RatingGridViewTextBoxCell();
            fileListRowTemplate.Cells[tbcIcon.Index] = new DataGridViewIconCell();
            // 画像の列の既定の NullValue は空の画像。値が null のとき（行がデータと対応していない間）に文字列のセルの書式の型と合わず、
            // 行を消すときの現在のセルの検証で DataError の既定のダイアログが出る（絞り込みで 0 件になったときなど）
            tbcIcon.DefaultCellStyle.NullValue = null;
            fileListRowTemplate.Height = dgvFileList.RowTemplate.Height;
            dgvFileList.RowTemplate = fileListRowTemplate;
            dgvFileList.VirtualMode = true;
            dgvFileList.CellValueNeeded += dgvFileList_CellValueNeeded;
            dgvFileList.CellToolTipTextNeeded += dgvFileList_CellToolTipTextNeeded;
            //dgvFileList.GetCurrentIndex = GetCurrentShowIndex;

            cmsRightClick.Opening += OpenInFileInClipboardProvider.Owner_Opening;
            cmsRightClick.Closed += OpenInFileInClipboardProvider.Owner_Closed;


            tvCatalog.bottomPadding = Program.DpiScaling(2);


            clipboardViewer = new MyClipboardViewer(this);
            clipboardViewer.ClipboardHandler += ClipboardViewer_ClipboardHandler;

            openbuiltinBookReaderToolStripMenuItem.Image = BuiltInViewerProvider.Icon;

#if RUNTIME
            Program.RunTimeMeasure.Block("Set explorer icon");
#endif

            openInExplorerInDirectoryListToolStripMenuItem.Image = ExplorerIcon;// ExplorerProvider.Icon;

#if RUNTIME
            Program.RunTimeMeasure.Block("Set UndoButton etc");
#endif

            setUndoButton(btnGoToBack, goBack: true);
            setUndoButton(btnGoToForward, goBack: false);

            //Program.SetDoubleBuffered(tvCatalog); // 初めから有効

            Program.SetDoubleBuffered(dgvFileList);
            Program.SetDoubleBuffered(dgvDirectoryList);


            setMenuItemEventHandler();

            //ApplicationArray = new ApplicationProvider[] { new BuiltInViewerProvider(this), new AssociationProvider(), new MoveLocationProvider(this) };

            tbcIcon.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcIconWidth = (int)Math.Round(Program.DisplayMagnificationX * 22); // 21 が限界値
            //tbcIconWidth = tbcIcon.HeaderCell.Size.Height - 1; // 100 % のときこれで 22 になる。23 が限界値
            tbcMargin = tbcIconWidth / 11;
            tbcIcon.Width = tbcIconWidth;
            tbcFileName.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcPage.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcRating.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcCreateTime.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcAccessTime.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcWriteTime.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcSize.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcResolution.HeaderCell.Style.WrapMode = DataGridViewTriState.False; ;
            tbcRatio.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcWidth.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcHeight.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcLength.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            tbcFPS.HeaderCell.Style.WrapMode = DataGridViewTriState.False;

            dgvFileList.MouseWheel += DgvFileList_MouseWheel;

            ratingFilterToolStripMenuItemDropDown = ratingFilterToolStripMenuItem.DropDown;
            ratingFilterToolStripMenuItem.DropDown = null;
            //menuStripForTagFilter.MouseWheel += MenuStripForTagFilter_MouseWheel;

            addToolStripMenuItemDropDown = addToolStripMenuItem.DropDown;
            addToolStripMenuItem.DropDown = null;

            ToolStripDropDownScroller.Enscrollable(tagToolStripMenuItem.DropDown, gestureListenergCmsRightTag);

            // 列の追加を DataGridViewRow を使って行えば必要ない
            //dgvDirectoryList.RowTemplate.Height = dgvFileList.RowTemplate.Height = Program.DpiScalingY(21);

            dgvSorter = new DataGridViewSorter(dgvDirectoryList);
            dgvSorter.CellClick += DgvSorter_CellClick;
            dgvSorter.RowsReleased += DgvSorter_RowsReleased;
            dgvSorter.UserSorted += DgvSorter_UserSorted;

            mgCatalog = new MouseGesture(tvCatalog);
            mgCatalog.MouseGestureStarting += MgCatalog_MouseGestureStarting;
            mgCatalog.MouseGestureCompleted += MgCatalog_MouseGestureCompleted;
            mgCatalog.Enabled = false; // 最初の Enabled = true で次のイベントが発動するように
            mgCatalog.EnabledChanged += mgCatalog_EnabledChanged;
            mgFileList = new MouseGesture(dgvFileList);
            mgFileList.MouseGestureStarting += mgFileList_MouseGestureStarting;
            mgFileList.MouseGestureCompleted += mgFileList_MouseGestureCompleted;
            mgFileList.Enabled = false; // mgCatalog との整合性のため

#if !DEBUG
            loadCountToolStripMenuItem.Visible = false;
#endif

            var nonvol = new string[(int)SortMode.Random + 1];
            for (var i = 0; i < (int)SortMode.Random + 1; i++) nonvol[i] = ((SortMode)i).ToString();
            cbSortBy.Items.AddRange(nonvol);
            cbSortBy.SelectedIndex = 0;


            // DPI 変更時用の処理
            var lineWidth = Program.DpiScaling(1);
            var boxWidth = cbLocation.Height + 2 * lineWidth;
            var boxWidth2 = (boxWidth * (2 * 12) + 11) / (2 * 11);
            MinimumSize = Size;
            //WindowMinWidth = Width;
            btnGoToBack.Location = new Point(btnGoToBack.Location.X + (btnGoToBack.Width - boxWidth2) / 2,
                cbLocation.Location.Y - lineWidth);
            btnGoToBack.Size = new Size(boxWidth2, boxWidth);
            btnGoToForward.Location = new Point(btnGoToForward.Location.X + (btnGoToForward.Width - boxWidth2) / 2,
                cbLocation.Location.Y - lineWidth);
            btnGoToForward.Size = new Size(boxWidth2, boxWidth);
            btnLocationCommands.Location = new Point(cbLocation.Right, cbLocation.Location.Y - lineWidth);
            //btnLocationCommands.Height = cbLocation.Height + 2;
            btnLocationCommands.Size = new Size(boxWidth, boxWidth);

            boxWidth = cbFilter.Height + 2 * lineWidth;
            btnFilterCommands.Location = new Point(cbFilter.Right, cbFilter.Location.Y - lineWidth);
            btnFilterCommands.Size = new Size(boxWidth, boxWidth);

#if RUNTIME
            Program.RunTimeMeasure.Block("SetMessagesWithoutTagFilter");
#endif

            //Text = Program.Name;
            SetMessagesWithoutTagFilter();

            // 静的な Shown に移動
            //Shown += (sender, e) => setTagFilteringMenuItems();

#if RUNTIME
            Program.RunTimeMeasure.Block("SetProcessEtc");
#endif

            bmwMakePreview.ThreadCount = Math.Max(1, Environment.ProcessorCount / 2);
            // ジョブ番号は 2*i がヘッダ取得、2*i+1 がサムネイル生成。サムネイルは ThumbViewerItem.LoadAsync が
            // 表示中のアイテムだけ読み込むので、奇数番のジョブはスレッドを起動せず完了扱いにする（A1）。
            bmwMakePreview.WorkSkipper = workNumber => (workNumber & 1) == 1;
            Program.SetPriority(ProcessPriorityClass.Normal);
            // ビューア用の待機プロセスは、カタログの表示が終わってから起動する（起動中に 2 つのプロセスが同時に JIT して CPU を取り合うのを避ける）。
            // それまでに必要になった場合は CheckAndRecoverWaitingProcess が起動する。
            Shown += (sender, e) => BeginInvoke((Action)(() =>
            {
                if (waitingProcess == null && !IsDisposed) PrepareLookAheadProcess(/*Configuration.GetLastWriteTime()*/);
            }));
            //menuStripForTagFilter.Location = new Point(menuStrip.Right - menuStripForTagFilter.Width, menuStrip.Top);
            menuStripForTagFilter.Top = menuStrip.Top;
            menuStripForTagFilter.Height = menuStrip.Size.Height;

#if RUNTIME
            Program.RunTimeMeasure.Block("CorrectPanelLayout");
#endif

            CenterPanellMinWidth = splRight.MinExtra = Program.DpiScalingX(105) + 2 * Program.DpiScaling(2);
            Shown += (sender, e) => pnlRight_SizeChanged(null, null);
            //tbcFileName.MinimumWidth = Program.DpiScalingX(70);
            var seventy = Program.DpiScalingX(70);
            splLeft.MinSize = seventy;
            splRight.MinSize = seventy;
            SidePanellMinWidth = seventy;

            splBookmark.MinSize = Program.DpiScalingY(50);
            splBookmark.MinExtra = Program.DpiScalingY(35);

            //zabLocation = new ZipPlaAddressBar(this, cbLocation, tvCatalog); // otherControl は非表示になるものは好ましくない
            zabLocation = new ZipPlaAddressBar(this, cbLocation, focusRobber: focusVisibleMainList);

            // 安全のため（v1.5.3.3 の不具合修正とともに）
            zabLocation.Text = "";

            zabLocation.MouseEnter += zabLocation_MouseEnter;
            zabLocation.MouseLeave += zabLocation_MouseLeave;
            zabLocation.DirectoryExistsOverrider = s => Directory.Exists(s) || IsSupportedByCatalogFormExceptFolder(s); // VirtualFolder.DirectoryOrBookmarkExists;
            //zabLocation.GetDirectoriesOverrider = VirtualFolder.GetDirectoriesAndBookmarks;

            zabLocation.DragDropStarting += zabLocation_DragDropStarting;
            zabLocation.DragDropFinished += zabLocation_DragDropFinished;

            zabLocation.ZipPlaAddressBarMakeButtonStarting += zabLocation_ZipPlaAddressBarMakeButtonStarting;

#if SCREENCAPTCHA
            zabLocation.GetDisplayNameOverrider = path =>
            {
                var result = SimpleBookmark.GetDisplayName(path);
                if (Message.CurrentLanguage.ToString() == "en-US")
                {
                    var m = Regex.Match(result, @"^ローカル ディスク \(([A-Z]):\)$");
                    if (m.Success)
                    {
                        return $"Local Disk ({m.Groups[1].Value}:)";
                    }
                }
                return result;
            };
#else
            zabLocation.GetDisplayNameOverrider = SimpleBookmark.GetDisplayName;
#endif
            zabLocation.TextChangedByButton += zabLocation_TextChangedByButton;
            zabLocation.TextChangedByBaseControl += zabLocation_TextChangedByBaseControl;
            zabLocation.CurrentLocationButtonClick += zabLocation_CurrentLocationButtonClick;
            zabLocation.DirectoryButtonContextMenuStripOpening += zabLocation_DirectoryButtonContextMenuStripOpening;

            zabLocation.AllowDrop = true;
            zabLocation.SubControlDragEnter += zabLocation_SubControlDragEnter;
            zabLocation.SubControlDragDrop += zabLocation_SubControlDragDrop;

            cbLocationMinimumWidth = cbLocation.Width;


#if RUNTIME
            Program.RunTimeMeasure.Block("SetAspectRatioOption1");
#endif

            //var screenSize = Screen.FromControl(this).Bounds;
            var screenSize = Screen.PrimaryScreen.Bounds;

#if RUNTIME
            Program.RunTimeMeasure.Block("SetAspectRatioOption2");
#endif
            int w = screenSize.Width;
            int h = screenSize.Height;
            while (w > 0)
            {
                var c = h % w;
                h = w;
                w = c;
            }
            w = screenSize.Width / h;
            h = screenSize.Height / h;

            if (w == 8 && h == 5 || w == 5 && h == 8)
            {
                w *= 2;
                h *= 2;
            }
            setAspectRatioOption((new Tuple<string, double>[]
            {
                Tuple.Create("1 : √2", 1.0 / Math.Sqrt(2.0)),
                Tuple.Create("1 : 1", 1.0 / 1.0),
                Tuple.Create("√2 : 1", Math.Sqrt(2.0) / 1),
                Tuple.Create($"{w} : {h}", (double)w / h),
            }).OrderBy(x => x.Item2).ToArray(),
            new Tuple<string, double>[]
            {
                Tuple.Create("1 : 5", 1.0 / 5.0),
                Tuple.Create("1 : 4", 1.0 / 4.0),
                Tuple.Create("1 : 3", 1.0 / 3.0),
                Tuple.Create("1 : 2", 1.0 / 2.0),
                Tuple.Create("9 : 16", 9.0 / 16.0),
                Tuple.Create("10 : 16", 10.0 / 16.0),
                Tuple.Create("2 : 3", 2.0 / 3.0),
                Tuple.Create("1 : √2", 1.0 / Math.Sqrt(2.0)),
                Tuple.Create("3 : 4", 3.0 / 4.0),
                Tuple.Create("4 : 5", 4.0 / 5.0),
                Tuple.Create("1 : 1", 1.0 / 1.0),
                Tuple.Create("5 : 4", 5.0 / 4.0),
                Tuple.Create("4 : 3", 4.0 / 3.0),
                Tuple.Create("√2 : 1", Math.Sqrt(2.0) / 1.0),
                Tuple.Create("3 : 2", 3.0 / 2.0),
                Tuple.Create("16 : 10", 16.0 / 10.0),
                Tuple.Create("16 : 9", 16.0 / 9.0),
                Tuple.Create("2 : 1", 2.0 / 1.0),
                Tuple.Create("3 : 1", 3.0 / 1.0),
                Tuple.Create("4 : 1", 4.0 / 1.0),
                Tuple.Create("5 : 1", 5.0 / 1.0),

            }.OrderBy(x => x.Item2).ToArray());

#if RUNTIME
            Program.RunTimeMeasure.Block("SetKeepOpen");
#endif

            SetKeepOpen(viewToolStripMenuItem);
            //SetKeepOpen(thumbnailSizeToolStripMenuItem, thumbnailCacheToolStripMenuItem, createViewerShortcutFromCurrentSettingsToolStripMenuItem);
            SetKeepOpen(thumbnailSizeToolStripMenuItem);

            Shown += (sender, e) =>
            {
                ratingFilterToolStripMenuItem.AutoSize = false;
                ratingFilterToolStripMenuItem.Text = "";
            };
        }

        private static bool requiredKeyMouseBindingCmsRightRootOrTag(ToolStripItem toolStripItem)
        {
            if (toolStripItem is PrefixEscapedToolStripMenuItem) return true;
            var items = toolStripItem.Owner.Items;
            return items.Count >= 3 && items[0] == toolStripItem && items[2] is PrefixEscapedToolStripMenuItem;
        }
        private static bool requiredKeyMouseBindingCmsTrue(ToolStripItem toolStripItem)
        {
            return true;
        }

        private void setFont(ToolStripItem tsi, bool? bold = null)
        {
            if (bold != null)
            {
                var current = tsi.Font;
                if ((bool)bold)
                {
                    tsi.Font = Program.StyledFont(current, current.Style | FontStyle.Bold);
                }
                else
                {
                    tsi.Font = Program.StyledFont(current, current.Style & ~FontStyle.Bold);
                }
            }
        }


        Graphics cbFilter_Graphic = null;
        private int cbLocationMinimumWidth;
        private void setCbFilterWidth()
        {
            // 縮小中にフィルタが書き換わる実装に備えて
            if (WindowState == FormWindowState.Minimized) return;

            // 長さの調整
            if (cbFilter_Graphic == null) cbFilter_Graphic = cbFilter.CreateGraphics();

            var text = cbFilter.Text;
            var start = cbFilter.SelectionStart;
            var length = cbFilter.SelectionLength;

            var textWidth = TextRenderer.MeasureText(cbFilter_Graphic, text, cbFilter.Font, new Size(int.MaxValue, int.MinValue),
                TextFormatFlags.NoPrefix).Width + cbFilter.Height * 3 / 2;
            //cbFilter_Graphic.MeasureString(cbFilter.Text, cbFilter.Font).Width + cbFilter.Height

            var widthFromText = Math.Min(cbFilter.Width + cbLocation.Width - cbLocationMinimumWidth, Math.Max(cbFilter.MinimumSize.Width, textWidth));
            var delta = widthFromText - cbFilter.Width;
            if (delta != 0)
            {
                // サイズ変更時にオートコンプリートが働いて意図しない動作をするのを回避
                // AutoCompleteMode を None にするのでは効果がない
                var cbFilterItems = (from object item in cbFilter.Items select item).ToArray();
                var cbLocationItems = (from object item in cbLocation.Items select item).ToArray();
                cbFilter.Items.Clear();
                cbLocation.Items.Clear();

                cbLocation.Width -= delta;
                btnLocationCommands.Left -= delta;
                cbSortBy.Left -= delta;
                //pbSearch.Left -= delta;
                cbFilter.Left -= delta;
                cbFilter.Width = widthFromText;

                // クリアした Items の復元
                cbFilter.Items.AddRange(cbFilterItems);
                cbLocation.Items.AddRange(cbLocationItems);

                // 連続実行時に一部のcontrolの表示が遅れて不格好になるのを回避
                cbLocation.Update();
                btnLocationCommands.Update();
                cbSortBy.Update(); ;
                cbFilter.Update();
                //Refresh();

                if (cbFilter.SelectionStart != start) cbFilter.SelectionStart = start;
                if (cbFilter.SelectionLength != length) cbFilter.SelectionLength = length;

                // オートコンプリート問題の旧対策法 
                //cbFilter.AutoCompleteMode = cbFilterACM;
                //cbLocation.AutoCompleteMode = cbLocationACM;
            }
        }

        private SearchManager cbFilter_SearchManager;
        private static readonly Color WindowColor = SystemColors.Window;// Color.FromKnownColor(KnownColor.Window);
        private static readonly Color InvalidColor = SystemColorsWindowIsNearToWhite ? Color.LightGray : Color.Gray;
        private static readonly Color ErrorColor = SystemColorsWindowIsNearToWhite ? Color.Pink : Color.DarkRed;
        private static readonly Color AliasColor = SystemColorsWindowIsNearToWhite ? Color.FromArgb(192, 255, 255) : Color.FromArgb(0, 64, 64);
        private bool cbFilter_TextChanged_StopResort = false;
        private string cbFilter_TextChanged_PrevText = null;
        private bool cbFilter_Valid = true;
        private void cbFilter_TextChanged(object sender, EventArgs e)
        {
            var text = cbFilter.Text;

            // TextChanged は過剰に呼び出される事がある
            if (cbFilter_TextChanged_PrevText == text) return;
            cbFilter_TextChanged_PrevText = text;

            //cbFilter_PasteMode = false;

            setCbFilterWidth();

            if (!cbFilter_Refresh(text)) return;

            if (cbFilter_TextChanged_StopResort) return; // ブックマーク読み込み用

            // ユーザーが入力しているあいだ（欄にフォーカスがある）は、最後の入力から少し待ってまとめて 1 回だけ絞り込む（K3）。
            // 以前は 1 文字ごとに全件の並べ替えと一覧の作り直しをしていた（3 万件で 1 文字 100〜180 ms）。
            // プログラムから文字列を設定した場合（タグのクリックなど）は従来どおりすぐ
            if (cbFilter.Focused)
            {
                if (cbFilter_TextChanged_Timer == null)
                {
                    cbFilter_TextChanged_Timer = new System.Windows.Forms.Timer { Interval = 150 };
                    cbFilter_TextChanged_Timer.Tick += (s2, e2) =>
                    {
                        cbFilter_TextChanged_Timer.Stop();
                        ResortForFilter();
                    };
                }
                cbFilter_TextChanged_Timer.Stop();
                cbFilter_TextChanged_Timer.Start();
                return;
            }
            ResortForFilter();
        }

        private System.Windows.Forms.Timer cbFilter_TextChanged_Timer;
        private void ResortForFilter()
        {
            cbFilter_TextChanged_Timer?.Stop();
            lock (ZipPathArrayLocker)
            {
                if (ZipPathArray != null && ZipPathArray.Length > 0)
                {
                    //using (var tm = new TimeMeasure())
                    {
                        tvCatalog.ShowIndexToDataIndex = GetSortArray(preSortArray: tvCatalog.ShowIndexToDataIndex);
                    }

                    setFileListFromThumbnailViewer();
                    var si = tvCatalog.SelectedIndex;
                    if (si >= 0)
                    {
                        // スクロール量の変化を伴う（誤：伴ない）ので MinimalMove は使わない
                        tvCatalog.ScrollBarToIndex(si);
                        //tvCatalog.ScrollBarToIndexWithMinimalMove(tvCatalog.DataIndexToShowIndex[si]);
                    }
                }
            }
        }

        private bool cbFilter_Refresh(string filter)
        {
            var invalid = InArchive || InMovie; // フィルタが無効の場合でもエラーの有無はチェックする
            if (aliasesToFilteringStrings != null && aliasesToFilteringStrings.ContainsKey(filter))
            {
                try
                {
                    cbFilter_SearchManager = new SearchManager(aliasesToFilteringStrings[filter]);
                }
                catch
                {
                    cbFilter_SearchManager = null;
                    cbFilter.BackColor = invalid ? InvalidColor : ErrorColor;
                    cbFilter_Valid = false;
                    return false;
                }
                cbFilter.BackColor = invalid ? InvalidColor : AliasColor;
                cbFilter_Valid = true;
                return true;
            }
            else
            {
                try
                {
                    cbFilter_SearchManager = new SearchManager(filter);
                }
                catch
                {
                    cbFilter_SearchManager = null;
                    cbFilter.BackColor = invalid ? InvalidColor : ErrorColor;
                    cbFilter_Valid = false;
                    return false;
                }
                cbFilter.BackColor = invalid ? InvalidColor : WindowColor;
                cbFilter_Valid = true;
                return true;
            }
        }

        private void cbFilter_Leave(object sender, EventArgs e)
        {
            if (cbFilter_TextChanged_Timer?.Enabled == true) ResortForFilter(); // 入力中で待っていた絞り込みを済ませる（K3）
            SetBackgroundMode(false, 0);
            setCurrentFilterToHistory(bringToTop: true);
        }

        private void setCurrentFilterToHistory(bool bringToTop)
        {
            if (invariantFilteringList) return;
            var text = cbFilter.Text;
            if (!string.IsNullOrEmpty(text) && filterValid())
            {
                if (cbFilter.Items.Contains(text))
                {
                    if (!bringToTop) return;
                    cbFilter.Items.Remove(text);
                    cbFilter.Text = text;
                }
                cbFilter.Items.Insert(1, text);
                TrimHistoryItems(cbFilter.Items, FilterHistoryLimit + 1); // 先頭の空欄のぶん +1。古いものから捨てる（S1）
            }
        }

        // 場所・絞り込みの履歴の上限（S1）。以前は無制限で、使うほど config.xml が大きくなり起動も遅くなっていた
        private const int LocationHistoryLimit = 200;
        private const int FilterHistoryLimit = 200;

        private static void TrimHistoryItems(ComboBox.ObjectCollection items, int limit)
        {
            var count = items.Count;
            if (count <= limit) return;
            for (var i = count - 1; i >= limit; i--) items.RemoveAt(i);
        }

        private void btnLocationCommands_Click(object sender, EventArgs e)
        {
            clearAllHistoryOfLocationsToolStripMenuItem.Enabled = cbLocation.Items.Count > 0;
            deleteCurrentPathFromHistoryToolStripMenuItem.Enabled = zabLocation.Text != null && cbLocation.Items.Contains(zabLocation.Text);
            cmsLocationCommands.Show(Cursor.Position);
        }

        private void btnLocationCommands_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                btnLocationCommands_Click(null, null);
            }
        }

        private void deleteCurrentPathFromHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbLocation.Items.Remove(zabLocation.Text);
            zabLocation.Text = "";
        }

        private void clearAllHistoryOfLocationsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Message.DoYouClearLocationHistory, Message.Information, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
            {
                cbLocation.Items.Clear();
            }
        }

        private string cbLocation_DropDown_TextBeforeDropDown = null;
        private bool cbLocation_DropDowning = false;
        private void cbLocation_DropDown(object sender, EventArgs e)
        {
            cbLocation_DropDown_TextBeforeDropDown = zabLocation.Text;
            cbLocation_DropDowning = true;
        }

        private void cbLocation_DropDownClosed(object sender, EventArgs e)
        {
            cbLocation_DropDowning = false;
            var cb = (ComboBox)sender;

            // この時点で cb.Text を明示的に変更しないと、cb.Text の値の変更のタイミングがこれに続く if 文よりも後になってしまうばかりか、
            // これに続くイベントハンドラで行われる読み込みパスの決定よりも後にさえなってしまう。
            var si = cb.SelectedIndex;
            if (si < 0)
            {
                // → KeyDown 側で対処
                return;
            }

            zabLocation.Text = cb.Items[si].ToString();

            if (zabLocation.Text != cbLocation_DropDown_TextBeforeDropDown)
            {
                MakePreview();
            }
        }


        private void btnFilterCommands_Click(object sender, EventArgs e)
        {
            clearAllHistoryOfFiltersToolStripMenuItem.Enabled = !invariantFilteringList && cbFilter.Items.Count > 1;
            deleteCurrentFilterFromHistoryToolStripMenuItem.Enabled = !invariantFilteringList && !string.IsNullOrEmpty(cbFilter.Text) && cbFilter.Items.Contains(cbFilter.Text);
            cmsFilterCommands.Show(Cursor.Position);
        }

        private void btnFilterCommands_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                btnFilterCommands_Click(null, null);
            }
            else if (e.Button == MouseButtons.Middle)
            {
                // enabled でないと使えない
                //deleteCurrentFilterFromHistoryToolStripMenuItem.PerformClick();

                deleteCurrentFilterFromHistoryToolStripMenuItem_Click(null, null);
            }
        }

        private void deleteCurrentFilterFromHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (invariantFilteringList) return;
            if (!string.IsNullOrEmpty(cbFilter.Text))
            {
                cbFilter.Items.Remove(cbFilter.Text);
                cbFilter.Text = "";
            }
        }

        private void clearAllHistoryOfFiltersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (invariantFilteringList) return;
            if (MessageBox.Show(Message.DoYouClearFilterHistory, Message.Information, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
            {
                cbFilter.Items.Clear();
                cbFilter.Items.Add("");
            }
        }

        private bool CursolIsOnControll()
        {
            return CursorIsOnControllField;
            //return UnderCursorSet.Count > 0;
        }

        // 復元する場合 RunWorkerStarting 内のコメントアウトを外すこと

        #region ポインタ位置処理

        bool CursorIsOnControllField = false;
        private void GeneralMouseLeave(object something)
        {
        }
        private void GeneralMouseEnter(object something)
        {
#if !AUTOBUILD
            //if (something == null) throw new ArgumentNullException();
#endif

            //CursorIsOnControllField = true;

        }

        private void tvCatalog_MouseEnter(object sender, EventArgs e) { focusIfRightness(sender as Control); GeneralMouseEnter(tvCatalog); }
        private void tvCatalog_MouseLeave(object sender, EventArgs e) { tvCatalog_MouseMove_RatingChangePart(); GeneralMouseLeave(tvCatalog); }
        private void cbLocation_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(cbLocation); }
        private void cbLocation_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(cbLocation); }
        private void btnLocationCommands_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(btnLocationCommands); }
        private void btnLocationCommands_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(btnLocationCommands); }
        private void cbSortBy_MouseEnter(object sender, EventArgs e) { focusIfRightness(sender as Control); GeneralMouseEnter(cbSortBy); }
        private void cbSortBy_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(cbSortBy); }
        private void cbFilter_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(cbFilter);
            showToolTip_MouseEnter(sender as Control, Message.MiddleClickToClearPaste + "\n" + Message.LeftFlickToClear + "\n" + Message.RightFlickToPaste,
                avoidCursorIcon: false, avoidTarget: true); }
        private void cbFilter_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(cbFilter); showToolTip_MouseLeave(sender as Control); }
        private void btnFilterCommands_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(btnFilterCommands); }
        private void btnFilterCommands_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(btnFilterCommands); }
        private void menuStrip_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(menuStrip); }
        private void menuStrip_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(menuStrip); }
        private void CatalogForm_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(this); }
        private void CatalogForm_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(this); }
        private void dgvFileList_MouseEnter(object sender, EventArgs e) { focusIfRightness(sender as Control); GeneralMouseEnter(dgvFileList); }
        private void dgvFileList_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(dgvFileList); }
        private void splRight_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(splRight); }
        private void splRight_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(splRight); }
        private void zabLocation_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(zabLocation); }
        private void zabLocation_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(zabLocation); }
        private void btnGoToBack_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(btnGoToBack); }
        private void btnGoToBack_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(btnGoToBack); }
        private void btnGoToForward_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(btnGoToForward); }
        private void btnGoToForward_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(btnGoToForward); }

        private void menuStripLeft_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(sender); }
        private void menuStripLeft_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(sender); }
        private void addToolStripMenuItem_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(sender); }
        private void addToolStripMenuItem_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(sender); }
        private void dgvDirectoryList_MouseEnter(object sender, EventArgs e)
        {
            focusIfRightness(sender as Control); GeneralMouseEnter(sender);
            dgvDirectoryList.ShowCellToolTips = true; // ドラッグアウト時に false になるため
        }
        private void dgvDirectoryList_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(sender); }
        private void splLeft_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(sender); }
        private void splLeft_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(sender); }
        private void cmsDirectoryList_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(sender); }
        private void cmsDirectoryList_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(sender); }

        private void SelectedFileNameToolStripTextBox_MouseEnter(object sender, EventArgs e)
        {
            //SelectedFileNameToolStripTextBox_MouseEnter_UnselectedItemMaskPart();
            GeneralMouseEnter(selectedFileNameToolStripTextBox);
        }
        private void SelectedFileNameToolStripTextBox_MouseLeave(object sender, EventArgs e)
        {
            //SelectedFileNameToolStripTextBox_MouseLeave_UnselectedItemMaskPart();
            GeneralMouseLeave(selectedFileNameToolStripTextBox);
        }

        private void startToolStripMenuItem_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(startToolStripMenuItem); }
        private void startToolStripMenuItem_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(startToolStripMenuItem); }
        private void viewToolStripMenuItem_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(viewToolStripMenuItem); }
        private void viewToolStripMenuItem_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(viewToolStripMenuItem); }
        private void thumbnailSizeToolStripMenuItem_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(thumbnailSizeToolStripMenuItem); }
        private void thumbnailSizeToolStripMenuItem_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(thumbnailSizeToolStripMenuItem); }
        private void optionToolStripMenuItem1_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(optionToolStripMenuItem); }
        private void optionToolStripMenuItem1_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(optionToolStripMenuItem); }

        private void cmsRightClick_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(cmsRightClick); }
        private void cmsRightClick_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(cmsRightClick); }


        private void cmsRightClickOnBackground_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(cmsRightClickOnBackground); }

        private void cmsRightClickOnBackground_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(cmsRightClickOnBackground); }

        private void ratingFilterToolStripMenuItem_MouseEnter(object sender, EventArgs e) { GeneralMouseEnter(ratingFilterToolStripMenuItem); }
        private void ratingFilterToolStripMenuItem_MouseLeave(object sender, EventArgs e) { GeneralMouseLeave(ratingFilterToolStripMenuItem); }

        #endregion

        private readonly HashSet<Control> showToolTipWaitingControls = new HashSet<Control>();
        private async void showToolTip_MouseEnter(Control target, string message, bool avoidCursorIcon, bool avoidTarget)
        {
            if (target != null && !string.IsNullOrEmpty(message) && !showToolTipWaitingControls.Contains(target))
            {
                showToolTipWaitingControls.Add(target);
                await Task.Delay(toolTip.InitialDelay);
                if (showToolTipWaitingControls.Contains(target))
                {
                    var rectangle = target.ClientRectangle;
                    var point = target.PointToClient(Cursor.Position);
                    // if (target.Cursor is Cursor cursor) // HotSopt が正しく取得できない
                    if (avoidCursorIcon && Cursor.Current is Cursor cursor)
                    {
                        point.Y += cursor.Size.Height - cursor.HotSpot.Y;
                    }
                    if (avoidTarget && rectangle.Right > point.X && rectangle.Bottom > point.Y) point.Y = rectangle.Bottom;
                    toolTip.Show(message, target, point);
                    await Task.Delay(toolTip.AutoPopDelay);
                    if (showToolTipWaitingControls.Contains(target))
                    {
                        toolTip.Hide(target);
                        showToolTipWaitingControls.Remove(target);
                    }
                }
            }
        }
        private void showToolTip_MouseLeave(Control target)
        {
            if (target != null)
            {
                toolTip.Hide(target);
                showToolTipWaitingControls.Remove(target);
            }
        }


        private void globalMouseEnter(Point screenLocation)
        {
            unselectedItemMaskOff();
            setWaitingProcessPriority(ProcessPriorityClass.Normal);
            CursorIsOnControllField = true;
        }

        private void globalMouseLeave(Point screenLocation)
        {
            bool? thisIsActiveNullable = null;
            if (SelectionHilightMode == SelectionHighlightMode.WhenMouseLeaves && !dragOuting && (bool)(thisIsActiveNullable = ActiveForm == this))
            {
                unselectedItemMaskOn();
            }

            setWaitingProcessPriority(ProcessPriorityClass.BelowNormal);

            if (!RenameShownArchive_MakePreviewSleepRequest && (!ClientRectangle.Contains(PointToClient(MousePosition))))
            {
                if (!(((MouseButtons & MouseButtons.Left) == MouseButtons.Left || cbFilter.Focused) && (bool)(
                     thisIsActiveNullable = thisIsActiveNullable ?? ActiveForm == this
                    )) && AllowFullpower)
                {
                    bmwMakePreview.ThreadCount = Math.Max(2, Environment.ProcessorCount - 1);
                    Program.SetPriority(ProcessPriorityClass.BelowNormal);
                }
            }
            
            if (!(bool)(thisIsActiveNullable = thisIsActiveNullable ?? ActiveForm == this))
            {
                stopLookAheadOnDeactivate();
            }

            CursorIsOnControllField = false;
        }

        //private static readonly Color unselectedItemMaskColor = Color.FromArgb(224, 255, 255, 255);
        private void unselectedItemMaskOn()
        {
            //if(tvCatalog.SelectedIndex >= 0)
            {
                tvCatalog.UnselectedItemAlphaChannel = 255 - MaskAlpha;
            }
        }

        private void unselectedItemMaskOff()
        {
            tvCatalog.UnselectedItemAlphaChannel = 255;
        }

        private bool NotInEditiongAnything()
        {
            return dgvFileList.ReadOnly && dgvDirectoryList.ReadOnly && !cbLocation.Focused && !cbFilter.Focused && !selectedFileNameToolStripTextBox.Focused;
        }

        private void focusIfRightness(Control control)
        {
            //if (NotInEditiongAnything()) control.Focus();
        }

        private void CatalogForm_Deactivate(object sender, EventArgs e)
        {
            SetBackgroundMode(false, 0);
            unselectedItemMaskOff();

            stopLookAheadOnDeactivate();
        }

        /// <summary>
        /// カタログが非アクティブになったとき、先読みした本を持つ待機ビューアを止め（本のファイルを掴んだままにしないため）、
        /// 何も読んでいない待機ビューアを低い優先度で用意しておく（Y8）。
        /// 以前は何も読んでいない待機ビューアまで殺していた。本を開いた直後に作った次の待機ビューアも、開いたビューアが前面に出た
        /// ときに殺され（開いている途中なら作られず）、カタログに戻って項目にマウスを乗せるまで作られなかった。
        /// エクスプローラーなどから開く本（S7）もそのたびに待機ビューアの起動からになっていた
        /// </summary>
        private void stopLookAheadOnDeactivate()
        {
            // 開いている途中の待機ビューアは、開く本を表示するので止めない。開き終わると次の待機ビューアが作られる
            if (OpenWaitingProcess_Processing) return;
            var hasLookAhead = lookAheadInfo?.Path != null; // 先読みの依頼を送ると Path が入る。新しく作った待機ビューアでは null
            if (hasLookAhead || waitingProcess == null || waitingProcess.HasExited)
            {
                StopLookAheadProcess();
                SetLookAheadArchive_PrevDataIndex = -1;
                // 表示前（Shown で作る）と終了中は作らない
                if (AllowReadAheadProcess && IsShown && !IsDisposed && !CatalogForm_FormClosing_FormClosed) PrepareLookAheadProcess();
            }
            setWaitingProcessPriority(ProcessPriorityClass.BelowNormal);
        }

        private void setWaitingProcessPriority(ProcessPriorityClass priority)
        {
            if (waitingProcess == null || waitingProcess.HasExited) return;
            try
            {
                waitingProcess.PriorityClass = priority;
            }
            catch (InvalidEnumArgumentException)
            {
                throw;
            }
            catch
            {
            }
        }

        private void cbLocation_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (cbLocation_DropDowning)
                {
                    // ComboBox は Enter キーで閉じると Text が Items に含まれない場合に空白に置き換える
                    // 以下の記述で Text をそのままにできる
                    e.SuppressKeyPress = true;
                    var text = cbLocation.Text;
                    cbLocation.DroppedDown = false;
                    cbLocation.Text = text;
                }
                //else // エンターでドロップダウンを閉じた場合もそのまま移動
                {
                    e.SuppressKeyPress = true;
                    MakePreview();
                }
            }
            else if (e.KeyCode == Keys.Escape && !cbLocation_DropDowning)
            {
                e.SuppressKeyPress = true;
                FocusLatestControledControl();
            }
        }

        private void cbFilter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !cbFilter.DroppedDown)
            {
                e.SuppressKeyPress = true;
                FocusLatestControledControl();
            }
        }

        private void cbLocation_MouseDown(object sender, MouseEventArgs e)
        {
            var cb = (ComboBox)sender;
            if (!cbLocation_Focused_for_MouseDown && e.Button == MouseButtons.Left && !cb.DroppedDown)
            {
                cbLocation.DroppedDown = true;
            }
            cbLocation_Focused_for_MouseDown = true;
        }

        bool cbLocation_Focused_for_MouseDown = false;
        private void cbLocation_Leave(object sender, EventArgs e)
        {
            cbLocation_Focused_for_MouseDown = false;
        }

        private void tvCatalog_KeyDown(object sender, KeyEventArgs e)
        {
            var mk = ModifierKeys;
            if (e.KeyCode == Keys.Enter)
            {
                // これを有効にする場合 Alt キーでメニューにフォーカスが移る動作を無効にする必要がある
                {
                    OpenSelectedArchiveInDefaultViewer(ApplicationProviderUser.EnterKey, null);
                }
                LatestControledControl = sender as Control;
            }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                LatestControledControl = sender as Control;
            }
            else if (commonKeyAction(e, mk))
            {
                return;
            }
        }

        private bool commonKeyAction(KeyEventArgs e, Keys modifierKeys)
        {
            if (e.KeyCode == Keys.Delete)
            {
                deleteKeyAction(modifierKeys); return true;
            }
            else if (e.Control && e.KeyCode == Keys.X)
            {
                CutKeyAction(modifierKeys); return true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopyKeyAction(modifierKeys); return true;
            }
            else if (e.Control && e.KeyCode == Keys.V)
            {
                PasteKeyAction(modifierKeys); return true;
            }
            else if (IsApps(e.KeyCode, modifierKeys))
            {
                AppsKeyAction(modifierKeys); return true;
            }
            else if (applicationShortcutKeyAction(e, modifierKeys) != applicationShortcutKeyAction_result.SomeException)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool IsApps(Keys keys, Keys modifierKeys)
        {
            return keys == Keys.Apps || keys == Keys.F10 && (modifierKeys & Keys.Shift) == Keys.Shift;
        }

        enum applicationShortcutKeyAction_result { Application, Bookmark, SomeException, NotFound }
        private applicationShortcutKeyAction_result applicationShortcutKeyAction(KeyEventArgs e, Keys modifierKeys)
        {
            var startApplicationWithAccessKeyLocal = startApplicationWithAccessKey;
            var bookmarkWithAccessKeyLocal = startApplicationWithAccessKeyLocal;
            if (!startApplicationWithAccessKey && !bookmarkWithAccessKeyLocal) return applicationShortcutKeyAction_result.NotFound;
            ApplicationProvider[] aa = null;
            if (startApplicationWithAccessKeyLocal)
            {
                var onlyApp = !bookmarkWithAccessKeyLocal;
                aa = ApplicationArray;
                if (aa == null)
                {
                    if (onlyApp) return applicationShortcutKeyAction_result.NotFound;
                    startApplicationWithAccessKeyLocal = false;
                }
                else if (!aa.Any())
                {
                    if (onlyApp) return applicationShortcutKeyAction_result.NotFound;
                    startApplicationWithAccessKeyLocal = false;
                }
                if (!startApplicationWithAccessKeyLocal)
                {
                    var lcc = LatestControledControl;
                    if (lcc != dgvFileList && lcc != tvCatalog)
                    {
                        if (onlyApp) return applicationShortcutKeyAction_result.NotFound;
                        startApplicationWithAccessKeyLocal = false;
                    }
                }
            }
            
            if (modifierKeys != Keys.None) return applicationShortcutKeyAction_result.SomeException;
            var kc = e.KeyCode;
            if (kc < Keys.D0 || kc > Keys.Z) return applicationShortcutKeyAction_result.SomeException; // D0, D1, ..., D9, A, B, ..., Z の順に並んでいる

            try
            {
                // フォーカスによってはアプリケーションは無視されることがあると考えるとこちら優先が妥当
                if (bookmarkWithAccessKeyLocal)
                {
                    var rows = (from DataGridViewRow row in dgvDirectoryList.Rows select row).ToArray();
                    foreach (var row in rows)
                    {
                        if (row.Cells[tbcDirectoryName.Index].Value is ColoredBookmark bookmark && bookmark.SimpleBookmark.SpecialRole == SimpleBookmarkSpecialRole.None) // セパレーターなら無視される
                        {
                            var sb = bookmark.SimpleBookmark;

                            if (bookmark.SimpleBookmark.Alias is string alias && alias != "" && kc == ExternalApplicationProvider.GetAccessKey(alias))
                            {
                                /*if (dgvDirectoryList.Visible)*/ foreach (var row2 in rows) row2.Selected = row2 == row;
                                moveToBookmark(row.Index, ApplicationProviderUser.EnterKey); // AccessKey も EnterKey と同様に振る舞う
                                return applicationShortcutKeyAction_result.Bookmark;
                            }
                        }
                    }
                }

                if (startApplicationWithAccessKeyLocal)
                {
                    string[] selectedPaths;
                    bool[] isDirs;
                    getSelectedPathArray(-1, out selectedPaths, out isDirs);
                    if (selectedPaths == null || selectedPaths.Length <= 0) return applicationShortcutKeyAction_result.SomeException;
                    var zpa = ZipPathArray;
                    if (zpa == null || zpa.Length != tvCatalog.Count) return applicationShortcutKeyAction_result.SomeException;
                    var zna = ZipNameArray;
                    if (zna == null || zna.Length != zpa.Length) return applicationShortcutKeyAction_result.SomeException;
                    var infos = new FileInfoForApplicationProvider[selectedPaths.Length];
                    for (var i = 0; i < selectedPaths.Length; i++)
                    {
                        infos[i] = new FileInfoForApplicationProvider { Path = selectedPaths[i], IsDir = isDirs[i] };
                    }
                    RecentFfmpegExists = MovieThumbnailLoader.ffmpegExists();
                    foreach (var app in aa)
                    {
                        if (kc == app.GetAccessKey())
                        {
                            if (app.Supports(infos))
                            {
                                app.Exec(ApplicationProviderUser.AccessKey, selectedPaths);
                                return applicationShortcutKeyAction_result.Application;
                            }
                        }
                    }
                    //throw new Exception(Message.ApplicationIsNotSpecified);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Task.Run(() => Invoke((MethodInvoker)(() => tvCatalog.DoMouseUp())));
            }

            // プログラムが見つかったかどうか、エラーが出たかどうかで動作は変更しない
            return applicationShortcutKeyAction_result.NotFound;
        }

        private void cmsRightClickPrepareAndShowForKeyOnTvCatalog(bool onlyTag, Keys modifierKeys)
        {
            var mk = modifierKeys;
            //var mk = ModifierKeys;
            if (tvCatalog.SelectedIndicesCount > 0)
            {
                int showIndex = tvCatalog.FirstDisplayedSelectedShowIndex;
                if (showIndex < 0) return;
                var p = tvCatalog.GetSemiCenterClientcoordinateOf(showIndex);
                p = tvCatalog.PointToScreen(p);
                if (onlyTag)
                {
                    cmsRightClickPrepareAndShow_(p, true);
                }
                else
                {
                    rightClickContextMenuPrepareAndShow(p, mk);
                }
            }
            else if (!onlyTag && tvCatalog.Visible && (RightClickContextMenu != CatalogFormConetxtMenu.Tag))
            {
                var p = tvCatalog.PointToScreen(tvCatalog.Location);

                //cmsRightClickPrepareAndShow(p);

                rightClickContextMenuPrepareAndShow(p, mk);
                //cmsRightClickPrepareAndShow_(p, false);
            }
        }

        private void ThumbnailSize_Click(ToolStripMenuItem toolStripMenuItem)
        {
            if (!toolStripMenuItem.Checked)
            {
                ChangeThumbnailSize(toolStripMenuItem);
                UpdatePreview();
            }
        }

        int ChangeThumbnailSize_Width = 210;
        int ChangeThumbnailSize_Height = 297;
        double ChangeThumbnailSize_Padding = 2;
        private static int ChangeThumbnailSize_MenuItemOffset = 2;
        static readonly double ChangeThumbnailSize_NeutralLength = Math.Sqrt(210 * 297);
        private void ChangeThumbnailSize(ToolStripMenuItem toolStripMenuItem)
        {
            var items = thumbnailSizeToolStripMenuItem.DropDownItems;

            //var isPlaClip = clipModeByControl == ClipMode.PlaClip;

            var inSizeBlock = false;
            foreach (object itemObject in items)
            {
                if (!inSizeBlock)
                {
                    inSizeBlock = itemObject == thumbnailSizeTopSeparator;
                    continue;
                }
                if (itemObject == thumbnailSizeBottomSeparator) break;
                if (itemObject.GetType() != typeof(ToolStripMenuItem)) continue;
                var item = (ToolStripMenuItem)itemObject;
                if (toolStripMenuItem != null)
                {
                    item.Checked = item == toolStripMenuItem;
                }
                else if (item.Checked)
                {
                    toolStripMenuItem = item;
                }
            }

        }

        private bool ThumbnailSettingToCatalog_CoverSettingModeAtOnce = false;
        private void ThumbnailSettingToCatalog()
        {
            int size;
            if (tinyToolStripMenuItem.Checked) size = -2;
            else if (smallToolStripMenuItem.Checked) size = -1;
            else if (largeToolStripMenuItem.Checked) size = 1;
            else if (hugeToolStripMenuItem.Checked) size = 2;
            else size = 0;


            double localAspectRatio = aspectRatio;
            if (ThumbnailSettingToCatalog_CoverSettingModeAtOnce)
            {
                ThumbnailSettingToCatalog_CoverSettingModeAtOnce = false;
            }
            else if (InMovie)
            {
                var mia = MovieInfoArray;
                if (mia != null && mia.Length > 0)
                {
                    var info = mia[0];
                    var movieSize = info.SizeByPixel;
                    var sar = info.SAR;
                    if (sar != 0) movieSize = sar * (Fraction)movieSize;
                    if (movieSize.Width > 0 && movieSize.Height > 0)
                    {
                        localAspectRatio = Math.Max(0.2, Math.Min(5.0, (double)movieSize.Width / movieSize.Height));
                    }
                }
            }

            var padding = size * 2; if (padding == 2) padding = 3; else if (padding < 2) padding = 2;
            var ratioSqrt = Math.Sqrt(localAspectRatio);
            var scale = Math.Pow(2, size / 2.0);
            ChangeThumbnailSize_Width = Program.DpiScalingX(ChangeThumbnailSize_NeutralLength * scale * ratioSqrt);
            ChangeThumbnailSize_Height = Program.DpiScalingX(ChangeThumbnailSize_NeutralLength * scale / ratioSqrt);
            ChangeThumbnailSize_Padding = padding;
        }

        private int GetThumbnailCountInTvCatalogOrFileList(MovieInfo info)
        {
            if (thumbnailListToolStripMenuItem.Checked)
            {
                int size;
                if (tinyToolStripMenuItem.Checked) size = -2;
                else if (smallToolStripMenuItem.Checked) size = -1;
                else if (largeToolStripMenuItem.Checked) size = 1;
                else if (hugeToolStripMenuItem.Checked) size = 2;
                else size = 0;

                double localAspectRatio = aspectRatio;
                if (!ThumbnailSettingToCatalog_CoverSettingModeAtOnce && info != null)
                {
                    var movieSize = info.SizeByPixel;
                    var sar = info.SAR;
                    if (sar != 0) movieSize = sar * (Fraction)movieSize;
                    if (movieSize.Width > 0 && movieSize.Height > 0)
                    {
                        localAspectRatio = Math.Max(0.2, Math.Min(5.0, (double)movieSize.Width / movieSize.Height));
                    }
                }

                var padding = size * 2; if (padding == 2) padding = 3; else if (padding < 2) padding = 2;
                var ratioSqrt = Math.Sqrt(localAspectRatio);
                var scale = Math.Pow(2, size / 2.0);
                var w = Program.DpiScalingX(ChangeThumbnailSize_NeutralLength * scale * ratioSqrt);
                var h = Program.DpiScalingX(ChangeThumbnailSize_NeutralLength * scale / ratioSqrt);
                padding = Program.DpiScaling(padding * currentMinimumFrameThickness);
                var ChangeThumbnailSize_Padding = padding;

                // スクロールバーは無視するべきなので tvCatalog.ClientSize は不適切
                int textLength, textLines;
                if (numberOfLinesOfName != NumberOfLinesOfName.Full)
                {
                    GetNameLengthAndNameLines(out textLength, out textLines);
                }
                else
                {
                    textLength = 0;
                    textLines = ThumbViewer.GetTextLines(default(TimeSpan).ToString(), new Size(w, h), padding, tvCatalog.bottomPadding, currentAligning, tvCatalog.Size);
                }
                return ThumbViewer.GetThumbnailCountInScreen(new Size(w, h), padding, tvCatalog.bottomPadding, tvCatalog.ShowText, tvCatalog.GetCharHeight(), textLength, textLines, currentAligning, tvCatalog.Size);
            }
            else if (fileListToolStripMenuItem.Checked)
            {
                return (dgvFileList.ClientRectangle.Bottom - dgvFileList.ColumnHeadersHeight) / GetCellDisplayHeight(dgvFileList);
            }
            else
            {
                return 0;
            }
        }
        /// <summary>
        /// DPI も考慮してセルの標準の高さを取得
        /// </summary>
        private static int GetCellDisplayHeight(DataGridView dataGridView)
        {
            var i = dataGridView.FirstDisplayedScrollingRowIndex;
            int result;
            if (i >= 0)
            {
                result = dataGridView.GetRowDisplayRectangle(i, false).Height;
            }
            else
            {
                result = dataGridView.ColumnHeadersHeight + 1;
                // 2016/12 Windows10 100%, 125%, 150%, 175%, 200%, 225% で完全一致を確認
            }
            return result > 0 ? result : dataGridView.RowTemplate.Height;
        }

        // サムネイルの大きさのメニュー項目
        private void thumbnailSizeMenuItem_Click(object sender, EventArgs e)
        {
            ThumbnailSize_Click((ToolStripMenuItem)sender);
        }

        private void showIconToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showIcon = showIconToolStripMenuItem.Checked = !showIcon;
            tvCatalog.Invalidate();
        }

        private void showRatingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showRating = showRatingToolStripMenuItem.Checked = !showRating;
            setTagfilterLocationIfRatingFilterVisibleChanged();
            //UpdatePreview();
            tvCatalog.Invalidate();
        }

        private void showTagsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showTags = showTagsToolStripMenuItem.Checked = !showTags;
            tvCatalog.Invalidate();
        }

        private void showFilenameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setShowFileName(!showFilenameToolStripMenuItem.Checked);
        }

        private void setShowFileName(bool show)
        {
            showFilenameToolStripMenuItem.Checked = show;
            tvCatalog.ShowText = show;
            theNumberOfLinesOfNameToolStripMenuItem.Enabled = show;
        }

        private IpcLookAheadInfo lookAheadInfo = null;
        private Process waitingProcess = null;
        private CatalogIpcServer currentIpcChanne = null; // 名前付きパイプのサーバー（P3。以前は .NET Remoting の IpcServerChannel）
        private IpcReportFromViewerToCatalog reportFromViewerToCatalog = null;
        private string currentIpcGUID = null; // チャンネル自身の名前及び ViewerForm 用プロセスからの連絡用オブジェクト名

        public void PrepareLookAheadProcess()
        {
            if (!AllowReadAheadProcess) return;
            PrepareLookAheadProcess(/*Configuration.GetLastWriteTime(),*/ forcePrepare: false);
        }
        void PrepareLookAheadProcess(/*DateTime iniModified,*/ bool forcePrepare = false)
        {
            if (!forcePrepare && !AllowReadAheadProcess) return;

            // チャンネル登録及び ViewerForm 用プロセスからの連絡用オブジェクト登録
            if (currentIpcChanne == null)
            {
                currentIpcGUID = Guid.NewGuid().ToString();
                currentIpcChanne = new CatalogIpcServer(currentIpcGUID);

                reportFromViewerToCatalog = new IpcReportFromViewerToCatalog();
                currentIpcChanne.Reports = reportFromViewerToCatalog;

                tmWatchIpc.Start();
            }

            // リモートオブジェクトを生成して公開
            if (lookAheadInfo != null)
            {
                currentIpcChanne?.Unregister(lookAheadInfo);
                lookAheadInfo = null;
            }
            lookAheadInfo = new IpcLookAheadInfo();

            var guid = Guid.NewGuid().ToString();
            currentIpcChanne.Register(lookAheadInfo, guid); // 依頼を書いて Accept を false にすると待機ビューアへ送る（P3）

            //CheckAndRecoverWaitingProcess_INIModified = iniModified;

            var psi = new ProcessStartInfo();
            psi.FileName = Application.ExecutablePath;

            psi.WindowStyle = ProcessWindowStyle.Hidden;
            const string command = "-LookAheadMode";
            psi.Arguments = command + " " + currentIpcGUID + " " + guid;
            waitingProcess = Process.Start(psi);
            // 本を開いた直後に起動されると、CLR の起動・JIT・フォームの構築（0.3〜0.6 秒の CPU）が開いたばかりのビューアの読み込みと
            // CPU を取り合う。カーソルがカタログの上になければ低い優先度で起動しておく（S2。カタログに戻れば globalMouseEnter で通常に戻る）
            if (!CursorIsOnControllField) setWaitingProcessPriority(ProcessPriorityClass.BelowNormal);
        }

        private bool mouseOnWindow;


        // ハンドルからウインドウの位置を取得。
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;

            public static implicit operator Rectangle(RECT rect)
            {
                return new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
            }
        }

        private bool CursorIsJustOnWindow(Point screenLocation)
        {
            if (ActiveForm == this) return false; // 極力 API は使わない

            var handle = GetForegroundWindow();
            if (handle == Handle) return false;
            RECT otherRectangle0;
            if (!GetWindowRect(handle, out otherRectangle0)) return false;
            var otherRectangle = (Rectangle)otherRectangle0;
            return otherRectangle.Contains(screenLocation);
        }

        // 64bit では動かない
        // http://pieceofnostalgy.blogspot.jp/2011/11/net-framework.html
        //[System.Runtime.InteropServices.DllImport("user32.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
        //public static extern IntPtr WindowFromPoint(int x, int y);

        void ReloadOneThumbnailForSubThread(int index, Guid startingGuid)
        {
            if (index >= 0)
            {
                lock (ZipPathArrayLocker)
                {
                    if (ZipPathArray != null && index < ZipPathArray.Length)
                    {
                        ReloadOneThumbnailForSubThread(index, ZipPathArray[index], startingGuid);
                    }
                }
            }
        }

        void ReloadOneThumbnailForSubThread(string oldPath, string newPath, Guid startingGuid)
        {
            if (!string.IsNullOrEmpty(oldPath) && !string.IsNullOrEmpty(newPath))
            {
                lock (ZipPathArrayLocker)
                {
                    if (ZipPathArray != null)
                    {
                        ReloadOneThumbnailForSubThread(Array.IndexOf(ZipPathArray, oldPath), newPath, startingGuid);
                    }
                }
            }
        }

        enum SilentMode { Always, Never, OnlyIfNotSuccess }
        void ReloadOneThumbnailForSubThread(int index, string newPath, Guid startingGuid, string oldPath = null, SilentMode silentMode = SilentMode.Never, bool reloadPage = false)
        {
            if (index >= 0 && !string.IsNullOrEmpty(newPath))
            {
                if (startingGuid != loadingGuid) return;
                var enterIf = false;
                Bitmap thumbnail = null;
                int filecount = -1;
                LoadResult loadResult = LoadResult.NotYet;
                long fileSize = -1;
                ImageInfo imageInfo;
                MovieInfo movieInfo;
                string zipName = null;
                var path = newPath;
                var isDir = false;
                lock (ZipPathArrayLocker)
                {
                    if (CreatingZipPathArray) return;

                    if (ZipPathArray != null && index < ZipPathArray.Length)
                    {
                        enterIf = true;
                        try
                        {
                            if (oldPath != null && oldPath != ZipPathArray[index]) return;
                            ZipPathArray[index] = path;
                            //var name = path.Substring(dirPathFullPathLengthPlusOne);

                            //isDir = oldPath != null ? ZipNameArray[index].EndsWith(Path.DirectorySeparatorChar.ToString()) : Directory.Exists(newPath);
                            isDir = Directory.Exists(newPath);
                            ZipNameArray[index] = zipName = getRelativePath(path, isDir);// name + Path.DirectorySeparatorChar;

                            FileSystemInfo info;
                            if (isDir)
                            {
                                info = new DirectoryInfo(path);
                            }
                            else
                            {
                                info = new FileInfo(path);
                            }
                            CreateTimeArray[index] = Program.GetCreateTime(info);
                            LastAccessTimeArray[index] = Program.GetLastAccessTime(info); // info.LastAccessTime;
                            ModifiedTimeArray[index] = Program.GetLastWriteTime(info);// .LastWriteTime;
                            FileCountArray[index] = -1;
                            ImageInfoArray[index] = null;// Size.Empty;
                            MovieInfoArray[index] = null;

                            if (reloadPage && LoadLastViewedPageFromHistory && File.Exists(Program.HistorySorPath))
                            {
                                ZipPageArray[index] = VirtualFolder.GetPageFromBookmark(path, isDir, Program.HistorySorPath);
                            }

                        }
                        catch (Exception error)
                        {
                            Program.AlertError(error);
                            return;
                        }
                    }
                }
                if (enterIf)
                {
                    // サムネイルは作らず、ヘッダ（ページ数・解像度・動画情報など）だけを取る。サムネイルは、表示範囲に入っている項目だけ
                    // ThumbViewerItem.LoadAsync が作る（K21。以前は監視イベントのたびに、見えていない項目のサムネイルまで作ってキャッシュに書いていた）
                    GetHeader(path, isDir, out _, out filecount, out loadResult, out fileSize, out imageInfo, out movieInfo);
                    if (fileSize < 0 && !isDir) // フォルダの場合サイズは取得済みまたはサイズは表示しない設定
                    {
                        try
                        {
                            fileSize = (new FileInfo(path)).Length;
                        }
                        catch { }
                    }
                    //if (fileSize >= 0)
                    {
                        if (startingGuid != loadingGuid)
                        {
                            thumbnail?.Dispose();
                            return;
                        }
                        try
                        {
                            Invoke((MethodInvoker)(() =>
                            {
                                if (CreatingZipPathArray || startingGuid != loadingGuid)
                                {
                                    thumbnail?.Dispose();
                                    return;
                                }
                                try
                                {
                                    if (index >= tvCatalog.Count)
                                    {
                                        thumbnail?.Dispose();
                                        return;
                                    }


                                    lock (ZipPathArrayLocker)
                                    {
                                        if (CreatingZipPathArray || index >= FileCountArray.Length)
                                        {
                                            thumbnail?.Dispose();
                                            return;
                                        }

                                        //NameInAsc, NameInDesc, RatingInAsc, RatingInDesc, AccessedInAsc, AccessedInDesc, ModifiedInAsc, ModifiedInDesc, SizeInAsc, SizeInDesc

                                        if (sortMode == SortMode.NameInAsc || sortMode == SortMode.NameInDesc)
                                        {
                                            var s2d = tvCatalog.ShowIndexToDataIndex;
                                            var sign = sortMode == SortMode.NameInAsc ? 1 : -1;
                                            var showIndex = tvCatalog.DataIndexToShowIndex[index];
                                            if (showIndex >= 0)
                                            {
                                                var clearSortOrder = false;
                                                var s2dLength = s2d.Length;
                                                var showIndexMinus1 = showIndex - 1;
                                                var ZipNameArrayLength = ZipNameArray.Length;
                                                if (showIndexMinus1 >= 0 && showIndexMinus1 < s2dLength)
                                                {
                                                    var preIndex = s2d[showIndexMinus1];
                                                    if (preIndex >= 0 && preIndex < ZipNameArrayLength)
                                                    {
                                                        var preName = ZipNameArray[preIndex];
                                                        if (preName != null && zipName != null && sign * GetSortArray_NaturalSort.Compare(preName, zipName) > 0)
                                                        {
                                                            clearSortOrder = true;
                                                        }
                                                    }
                                                }
                                                if (!clearSortOrder)
                                                {
                                                    var showIndexPlus1 = showIndex + 1;
                                                    if (showIndexPlus1 >= 0 && showIndexPlus1 < s2dLength)
                                                    {
                                                        var nexIndex = s2d[showIndexPlus1];
                                                        if (nexIndex >= 0 && nexIndex < ZipNameArrayLength)
                                                        {
                                                            var nexName = ZipNameArray[nexIndex];
                                                            if (nexName != null && zipName != null && sign * GetSortArray_NaturalSort.Compare(zipName, nexName) > 0)
                                                            {
                                                                clearSortOrder = true;
                                                            }
                                                        }

                                                    }
                                                }

                                                if (clearSortOrder)
                                                {
                                                    tbcFileName.HeaderCell.SortGlyphDirection = SortOrder.None;
                                                    cbSortBy.SelectedIndex = -1;
                                                }
                                            }
                                        }


                                        FileCountArray[index] = filecount;
                                        LoadResultArray[index] = loadResult;
                                        ZipSizeArray[index] = fileSize;
                                        ImageInfoArray[index] = imageInfo;
                                        MovieInfoArray[index] = movieInfo;

                                        setFileListRowInLock(index, dynamicOnly: false, localOnly: false, cultureForOnlyStatic: Message.CurrentLanguage, volatilitySortCancelIfChanged: true);

                                    }

                                    // 最後にやらないと描画時に Success 等が正しく参照されない
                                    var silent = silentMode == SilentMode.Always || silentMode == SilentMode.OnlyIfNotSuccess && loadResult != LoadResult.Success;
                                    if (tvCatalog[index] == null)
                                    {
                                        var item = new ThumbViewerItem()
                                        {
                                            OwnerCatalog = this,
                                            FilePath = path,
                                            Image = thumbnail,
                                            Text = zipName,
                                        };
                                        if (silent)
                                        {
                                            tvCatalog.SilentSet(index, item);
                                        }
                                        else
                                        {
                                            tvCatalog[index] = item;
                                        }
                                    }
                                    else
                                    {
                                        if (silent)
                                        {
                                            tvCatalog[index].SilentSet(zipName); // サムネイルは変わっていないので今の画像のまま（K21）
                                        }
                                        else
                                        {
                                            tvCatalog[index].ResetForReload(zipName);
                                        }
                                    }
                                    // 表示範囲に入っている項目のサムネイルを作らせる（描画のときに LoadAsync が呼ばれる。K21）
                                    tvCatalog.Invalidate();
                                }
                                catch (Exception error)
                                {
                                    Program.AlertError(error);
                                }
                                if (tvCatalog.IsSelected(index))
                                {
                                    ShowStatusBarAfterItemLoaded();
                                }
                            }));
                        }
                        catch (ObjectDisposedException) { }
                    }
                }
            }
        }

        private void resetVolatilitySort()
        {
            foreach (DataGridViewColumn c in dgvFileList.Columns)
            {
                if (
                    c != tbcFileName &&
                    c != tbcRating &&
                    c != tbcCreateTime &&
                    c != tbcAccessTime &&
                    c != tbcWriteTime &&
                    c != tbcSize)
                {
                    c.HeaderCell.SortGlyphDirection = SortOrder.None;
                }
            }
        }
        private void setNonVolatilitySort()
        {
            resetVolatilitySort();
            dgvFileList.Columns[tbcIcon.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.TypeInAsc ? SortOrder.Ascending : sortMode == SortMode.TypeInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcFileName.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.NameInAsc ? SortOrder.Ascending : sortMode == SortMode.NameInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcRating.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.RatingInAsc ? SortOrder.Ascending : sortMode == SortMode.RatingInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcCreateTime.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.CreatedInAsc ? SortOrder.Ascending : sortMode == SortMode.CreatedInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcAccessTime.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.AccessedInAsc ? SortOrder.Ascending : sortMode == SortMode.AccessedInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcWriteTime.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.ModifiedInAsc ? SortOrder.Ascending : sortMode == SortMode.ModifiedInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcSize.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.SizeInAsc ? SortOrder.Ascending : sortMode == SortMode.SizeInDesc ? SortOrder.Descending : SortOrder.None;
            var temp = cbSortBy_SelectedValueChanged_Stop;
            cbSortBy_SelectedValueChanged_Stop = true;
            //while (cbSortBy.Items.Count > (int)SortMode.Random + 1) cbSortBy.Items.RemoveAt(cbSortBy.Items.Count - 1);
            cbSortBy.SelectedIndex = (int)sortMode;
            cbSortBy_SelectedValueChanged_Stop = temp;
        }

        private long ShowStatusBar_NextID = 0;
        private long ShowStatusBar_CurrentID = 0;
        private void ShowStatusBar() { ShowStatusBar(tvCatalog.SelectedIndices); }

        // 項目の読み込みが終わるたびに呼ぶ。複数選択中の ShowStatusBar は選択項目をすべて走査し、フォルダのサイズの集計もやり直すので、
        // 全選択したまま読み込みが進むと O(n^2) になる。複数選択中はまとめて、更新にかかった時間の 4 倍（100ms〜1 秒）に 1 回にする（K4）。
        // UI スレッドから呼ぶこと
        private System.Windows.Forms.Timer ShowStatusBarAfterItemLoaded_Timer;
        private void ShowStatusBarAfterItemLoaded()
        {
            if (tvCatalog.SelectedIndicesCount <= 1)
            {
                ShowStatusBarAfterItemLoaded_Timer?.Stop();
                ShowStatusBar();
                return;
            }
            if (ShowStatusBarAfterItemLoaded_Timer == null)
            {
                ShowStatusBarAfterItemLoaded_Timer = new System.Windows.Forms.Timer { Interval = 100 };
                ShowStatusBarAfterItemLoaded_Timer.Tick += (sender, e) =>
                {
                    ShowStatusBarAfterItemLoaded_Timer.Stop();
                    if (IsDisposed) return;
                    var start = Environment.TickCount;
                    ShowStatusBar();
                    var elapsed = unchecked(Environment.TickCount - start);
                    ShowStatusBarAfterItemLoaded_Timer.Interval = Math.Max(100, Math.Min(1000, elapsed * 4));
                };
            }
            if (!ShowStatusBarAfterItemLoaded_Timer.Enabled) ShowStatusBarAfterItemLoaded_Timer.Start();
        }
        private void ShowStatusBar(IEnumerable<int> selectedIndices)
        {
            var id = ShowStatusBar_NextID;
            ShowStatusBar_CurrentID = id;
            ShowStatusBar_NextID = ShowStatusBar_NextID < long.MaxValue ? ShowStatusBar_NextID + 1 : 0;

            var itemsCount = -1;
            var selectedCount = 0;
            long fileSize = 0;
            bool? ffmpegExists = null;
            //var atLeastOneDir = false;
            List<string> dirPathList = null;
            if (selectedIndices != null && selectedIndices.Any(i => i >= 0))
            {
                lock (ZipPathArrayLocker)
                {
                    if (ZipPathArray != null)
                    {
                        itemsCount = ZipPathArray.Length;
                        foreach (var selectedIndex in selectedIndices)
                        {
                            if (0 <= selectedIndex && selectedIndex < itemsCount)
                            {
                                selectedCount++;
                                var name = ZipNameArray[selectedIndex];
                                var isDir = name.EndsWith(Path.DirectorySeparatorChar.ToString());
                                //if (isDir) atLeastOneDir = true;
                                if (selectedCount == 1)
                                {
                                    var path = ZipPathArray[selectedIndex];
                                    if (selectedFileNameToolStripTextBox.Text != path) selectedFileNameToolStripTextBox.Text = path;
                                    if (ImageLoader.SupportsGetImageInfo(path, ref ffmpegExists))
                                    {
                                        var info = ImageInfoArray[selectedIndex];
                                        if (info == null)
                                        {
                                            selectedFileContainsCountToolStripStatusLabel.Text = "?x?";
                                        }
                                        else
                                        {
                                            var size = info.Size;
                                            selectedFileContainsCountToolStripStatusLabel.Text = $"{size.Width}x{size.Height}";
                                        }
                                    }
                                    else if (isDir || PackedImageLoader.Supports(path))
                                    {
                                        selectedFileContainsCountToolStripStatusLabel.Text = Program.GetNPageString(IfNegativeThenQuestion(FileCountArray[selectedIndex]));
                                    }
                                    else if (MovieThumbnailLoader.Supports(path))
                                    {
                                        var info = MovieInfoArray[selectedIndex];
                                        if (info == null)
                                        {
                                            selectedFileContainsCountToolStripStatusLabel.Text = "";
                                        }
                                        else
                                        {
                                            selectedFileContainsCountToolStripStatusLabel.Text = info.ToShortString();
                                        }
                                    }
                                    else
                                    {
                                        selectedFileContainsCountToolStripStatusLabel.Text = "";
                                    }
                                    if (ffmpegExists != null) RecentFfmpegExists = (bool)ffmpegExists;
                                }

                                if (fileSize >= 0)
                                {
                                    var thisFileSize = ZipSizeArray[selectedIndex];
                                    if (thisFileSize >= 0)
                                    {
                                        fileSize += thisFileSize;
                                    }
                                    else if (isDir)
                                    {
                                        if (dirPathList == null) dirPathList = new List<string>();
                                        dirPathList.Add(ZipPathArray[selectedIndex]);
                                    }
                                    else
                                    {
                                        fileSize = -1;
                                        dirPathList = null;
                                    }
                                }

                            }
                        }
                    }
                }
            }
            //var needToCheckDirSize = false;
            {
                if (fileSize >= 0)
                {
                    if (dirPathList == null)
                    {
                        fileSizeToolStripStatusLabel.Text = Program.GetFormatSizeString(fileSize, Message.Bytes, simpleForm: true);
                    }
                    else
                    {
                        fileSizeToolStripStatusLabel.Text = Program.GetFormatSizeString(fileSize, Message.Bytes, simpleForm: true) + "+";
                    }
                }
                else
                {
                    fileSizeToolStripStatusLabel.Text = $"? {Message.Bytes}";
                }
            }
            tvCatalog_ShowIndexToDataIndexChanged(null, null);

            if (selectedCount != 1)
            {
                var selectedIndicesCount = tvCatalog.SelectedIndicesCount;
                if (selectedIndicesCount > 1)
                {
                    selectedFileNameToolStripTextBox.Text = Message._1ItemsAreSelected.Replace("$1", selectedIndicesCount.ToString());
                }
                else
                {
                    selectedFileNameToolStripTextBox.Text = "";
                }
                selectedFileContainsCountToolStripStatusLabel.Text = "";
                if (selectedCount < 1) fileSizeToolStripStatusLabel.Text = "";
            }

            if (fileSize >= 0 && dirPathList != null)
            {
                var lastCheck = (Environment.TickCount & int.MaxValue); // 最も高速だがループで大小関係が狂うので注意 http://d.hatena.ne.jp/saiya_moebius/20100819/1282201466
                Task.Run(() =>
                {
                    foreach (var dirPath in dirPathList)
                    {
                        try
                        {
                            foreach (var file in new DirectoryInfo(dirPath).EnumerateFiles("*", SearchOption.AllDirectories))
                            {
                                fileSize += file.Length;
                                const int span = 50;
                                if (id != ShowStatusBar_CurrentID) return;
                                var now = (Environment.TickCount & int.MaxValue);
                                bool over;
                                if (lastCheck <= int.MaxValue - span)
                                {
                                    over = now >= lastCheck + span || now < lastCheck;
                                }
                                else
                                {
                                    over = now >= lastCheck - (int.MaxValue - (span - 1)) && now < lastCheck;
                                }
                                if (over)
                                {
                                    lastCheck = now;
                                    Invoke(((MethodInvoker)(() =>
                                    {
                                        if (id != ShowStatusBar_CurrentID) return;
                                        fileSizeToolStripStatusLabel.Text = Program.GetFormatSizeString(fileSize, Message.Bytes, simpleForm: true) + "+";
                                    })));
                                }
                                if (id != ShowStatusBar_CurrentID) return;
                            }
                            Invoke(((MethodInvoker)(() =>
                            {
                                if (id != ShowStatusBar_CurrentID) return;
                                fileSizeToolStripStatusLabel.Text = Program.GetFormatSizeString(fileSize, Message.Bytes, simpleForm: true);
                            })));
                        }
                        catch
                        {
                            Invoke(((MethodInvoker)(() =>
                            {
                                if (id != ShowStatusBar_CurrentID) return;
                                fileSizeToolStripStatusLabel.Text = fileSizeToolStripStatusLabel.Text = $"? {Message.Bytes}";
                            })));
                            return;
                        }
                    }
                });

            }
        }


        private void setThumbnailManuallyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenSelectedArchiveCoverSetting();
        }

        private void OpenSelectedArchiveCoverSetting()
        {
            var selectedIndex = tvCatalog.SelectedIndex;
            if (selectedIndex >= 0)
            {
                string selectedPath = null;
                lock (ZipPathArrayLocker)
                {
                    if (ZipPathArray != null && selectedIndex < ZipPathArray.Length)
                    {
                        if (LoadResultArray == null || selectedIndex >= LoadResultArray.Length) return;
                        // LoadResult.NotYet は読み込む
                        switch (LoadResultArray[selectedIndex])
                        {
                            case LoadResult.LoadError:
                                MessageBox.Show(Message.FailedToLoadFile, null, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                                return;
                            case LoadResult.FileNotFound:

                                MessageBox.Show(Message.NoReadableFile, null, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                                return;
                        }
                        try
                        {
                            selectedPath = ZipPathArray[selectedIndex];
                        }
                        catch (Exception error)
                        {
                            Program.AlertError(error);
                        }
                    }
                }
                if (selectedPath != null)
                {
                    try
                    {
                        if (PackedImageLoader.Supports(selectedPath))
                        {
                            OpenWaitingProcess(selectedPath, -1, true);
                        }
                        else if (InArchive)
                        {
                            OpenWaitingProcess(selectedPath, page: -1, coverSettingMode: true); // selectedPath が / を含むパスの場合 page は -1 でよい
                        }
                        else if (MovieThumbnailLoader.Supports(selectedPath) && MovieThumbnailLoader.ffmpegExists())
                        {
                            zabLocation.Text = selectedPath;
                            ThumbnailSettingToCatalog_CoverSettingModeAtOnce = true;
                            MakePreview();
                        }
                    }
                    catch (Exception error)
                    {
                        Program.AlertError(error);
                    }
                }
            }
        }

        private void RenameShownArchive(int selectedIndex, string path, string newPath, bool atFirstArrayChange = true, bool? needToReload = null)
        {
            RenameShownArchive(new int[] { selectedIndex }, new string[] { path }, new string[] { newPath }, atFirstArrayChange, needToReload);
        }

        private async Task<bool> showshowConfirmationDialogDialogAlways(string message, IReadOnlyList<string> paths)
        {
            return await showConfirmationDialog(message, paths, 1, getFileName: true);
        }

        private async Task<bool> showshowConfirmationDialogDialogIfIndicesAreMany(string message, IReadOnlyList<string> paths)
        {
            return await showConfirmationDialog(message, paths, 10, getFileName: true);
        }

        private async Task<bool> showConfirmationDialog(string message, IReadOnlyList<string> paths, int dialogLBound, bool getFileName)
        {
            var count = paths.Count;
            if (count >= dialogLBound)
            {
                const int dialogDisplayUBound = 13;
                const int dialogDisplayTail = 6;
                var fullMessage = new StringBuilder(message.Replace("$1", count.ToString())).AppendLine();
                try
                {
                    void appendPath(int i)
                    {
                        var s = paths[i];
                        fullMessage.AppendLine().Append(getFileName ? Path.GetFileName(s) : s);
                    }

                    if (count <= dialogDisplayUBound)
                    {
                        for (var i = 0; i < count; i++) appendPath(i);
                    }
                    else
                    {
                        for (var i = 0; i < dialogDisplayUBound - dialogDisplayTail - 1; i++) appendPath(i);
                        fullMessage.AppendLine().Append("...");
                        for (var i = -dialogDisplayTail; i < 0; i++) appendPath(count + i);
                    }
                }
                catch { }

                await Task.Run(() => Invoke((MethodInvoker)(() => CloseVisibleContextMenuStrips())));
                //var result = MessageBox.Show(this, message, Message.Question, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                var result = MessageForm.Show(this, fullMessage.ToString(), Message.Question, Message._Yes, Message._No, MessageBoxIcon.Question);
                await Task.Run(() => Invoke((MethodInvoker)(() => tvCatalog.DoMouseUp())));
                //return result == DialogResult.OK;
                return result == 0;
            }
            else
            {
                return true;
            }
        }

        private static void Arrange<T>(T[] target, int[] sorter)
        {
            var temp = target.Clone() as T[];
            for (var i = 0; i < target.Length; i++) target[i] = temp[sorter[i]];
        }

        bool RenameShownArchive_MakePreviewSleepRequest = false;
        /// <summary>
        /// needToReloadEvenIfFileNameIsNotChanged は atFirstArrayChange == false のときは未実装
        /// </summary>
        /// <param name="selectedIndices"></param>
        /// <param name="paths"></param>
        /// <param name="newPaths"></param>
        /// <param name="atFirstArrayChange"></param>
        /// <param name="needToReload"></param>
        /// <param name="needToReloadEvenIfFileNameIsNotChanged"></param>
        private async void RenameShownArchive(int[] selectedIndices, string[] paths, string[] newPaths, bool atFirstArrayChange = true, bool? needToReload = null, bool[] needToReloadEvenIfFileNameIsNotChanged = null)// bool atFirstArrayChange = false)
        {
            var startingGuid = loadingGuid;


            var count = selectedIndices.Length;

            var d2s = tvCatalog.DataIndexToShowIndex;
            if (d2s == null) return;
            int[] sorter;
            try
            {
                sorter = Enumerable.Range(0, count).OrderBy(i => d2s[selectedIndices[i]]).ToArray();
            }
            catch // IndexOutOfRangeException
            {
                return;
            }
            Arrange(selectedIndices, sorter);
            Arrange(paths, sorter);
            Arrange(newPaths, sorter);
            if (needToReloadEvenIfFileNameIsNotChanged != null)
            {
                if (needToReloadEvenIfFileNameIsNotChanged.Length == count) Arrange(needToReloadEvenIfFileNameIsNotChanged, sorter);
                else needToReloadEvenIfFileNameIsNotChanged = null;
            }

            if (!await showshowConfirmationDialogDialogIfIndicesAreMany(Message.DoYouRenameFollowing1Items, paths)) return;

            bool[] changes;
            if (needToReloadEvenIfFileNameIsNotChanged == null)
            {
                changes = new bool[count];
                for (var i = 0; i < count; i++)
                {
                    changes[i] = paths[i].ToLower() != newPaths[i].ToLower();
                }
            }
            else
            {
                changes = needToReloadEvenIfFileNameIsNotChanged;
                for (var i = 0; i < count; i++)
                {
                    //var nameChange = paths[i].ToLower() != newPaths[i].ToLower();
                    changes[i] = changes[i] || paths[i].ToLower() != newPaths[i].ToLower();
                }
            }

            if (changes.All(v => !v)) return;

            var lra = LoadResultArray;
            int lraLen;
            if (lra == null || (lraLen = lra.Length) == 0) return;
            var allTargetsAreLoaded = true;
            var tempThreadCount = 0;
            var priority = ProcessPriorityClass.Normal;
            if (bmwMakePreview.IsBusy && (tempThreadCount = bmwMakePreview.ThreadCount) > 0)
            {
                priority = Program.GetPriority();
                for (var i = 0; i < count; i++)
                {
                    var idx = selectedIndices[i];
                    if (idx < 0 || idx >= lraLen) return;
                    if (lra[idx] == LoadResult.NotYet)
                    {
                        allTargetsAreLoaded = false;
                        break;
                    }
                }
            }

            Action renameAction = async () =>
            {
                try
                {
                    StopLookAheadProcess();

                    var needToReloadArray = new bool[count];

                    if (atFirstArrayChange)
                    {
                        var ntr = needToReload != false;
                        lock (ZipPathArrayLocker)
                        {
                            for (var i = 0; i < count; i++)
                            {
                                if (!changes[i]) continue;

                                var index = selectedIndices[i];
                                var path = newPaths[i];
                                var oldPath = paths[i];


                                if (oldPath.ToLower() != path.ToLower()) Program.FileOrDirectoryMove(oldPath, path); // ここで例外が発生した場合 ZipPathArray 等の書き換えは行われない

                                ZipPathArray[index] = path;
                                //var name = path.Substring(dirPathFullPathLengthPlusOne);

                                var name = getRelativePath(path, ZipNameArray[index]);// name + (isDir ? Path.DirectorySeparatorChar.ToString() : "");
                                ZipNameArray[index] = name;

                                setFileListRowInLock(index, dynamicOnly: false, localOnly: false, cultureForOnlyStatic: Message.CurrentLanguage, volatilitySortCancelIfChanged: true);

                                var item = tvCatalog[index];
                                item.SilentSet(name);
                                item.DrawItem(onlyFrame: false); // レートなどの再描画が必要なので

                                if (needToReload != null) needToReloadArray[i] = ntr;
                                else needToReloadArray[i] = LoadResultArray[index] == LoadResult.NotYet;
                            }
                        }
                    }
                    else
                    {
                        for (var i = 0; i < count; i++)
                        {
                            Program.FileOrDirectoryMove(paths[i], newPaths[i]);
                            needToReloadArray[i] = true;
                        }
                    }

                    // InDirectory に限りフォルダ監視でも実行されるが、イベントを落とすリスクに備え常に実行
                    // この関数は二重呼び出しに対する制御は実装済み
                    swapPathAndNameInSorBookmarkUndoBuff(paths, newPaths, needToSaveBookmark: true);

                    PrepareLookAheadProcess();

                    if (needToReloadArray.Any(n => n))
                    {
                        await Task.Run(() =>
                        {
                            for (var i = 0; i < count; i++)
                            {
                                if (needToReloadArray[i])
                                {
                                    ReloadOneThumbnailForSubThread(selectedIndices[i], newPaths[i], startingGuid, atFirstArrayChange ? newPaths[i] : paths[i]);
                                }
                            }
                        });
                    }

                }
                catch (Exception error)
                {
                    await Task.Run(() => Invoke((MethodInvoker)(() => CloseVisibleContextMenuStrips())));
                    MessageBox.Show(this, error.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    await Task.Run(() => Invoke((MethodInvoker)(() => tvCatalog.DoMouseUp())));
                }
            };

            if (allTargetsAreLoaded)
            {
                //await Task.Run(renameAction);
                renameAction();
            }
            else
            {

                RenameShownArchive_MakePreviewSleepRequest = true;
                bmwMakePreview.ThreadCount = 0;
                Program.SetPriority(ProcessPriorityClass.Normal);
                BackgroundMultiWorker.EachRunWorkerCompletedEventHandler eh = null;

                eh = (sender, e) =>
                {
                    if (bmwMakePreview.CurrentThreadCount <= 1) // 自分はカウントされていて良い
                    {
                        //await Task.Run(renameAction);
                        renameAction();

                        bmwMakePreview.ThreadCount = tempThreadCount;
                        Program.SetPriority(priority);
                        RenameShownArchive_MakePreviewSleepRequest = false;
                        bmwMakePreview.EachRunWorkerCompleted -= eh;
                    }
                };

                bmwMakePreview.EachRunWorkerCompleted += eh;
            }
        }

        private void swapPathAndNameInSorBookmarkUndoBuff(string oldPath, string newPath, bool needToSaveBookmark)
        {
            swapPathAndNameInSorBookmarkUndoBuff(new string[1] { oldPath }, new string[1] { newPath }, needToSaveBookmark);
        }
        private void swapPathAndNameInSorBookmarkUndoBuff(string[] oldPathArray, string[] newPathArray, bool needToSaveBookmark)
        {
            int count;
            if (oldPathArray == null || newPathArray == null || (count = oldPathArray.Length) != newPathArray.Length) return;

            Exception innerCurrentEditException = null;

            try
            {
                Invoke(((MethodInvoker)(() =>
                {

                    var current = currentLocation;

                    // 現在の場所
                    var indexOfCurrentLocation = Array.IndexOf(oldPathArray, current);
                    if (indexOfCurrentLocation >= 0)
                    {
                        current = currentLocation = newPathArray[indexOfCurrentLocation];
                    }

                    // キャッシュ
                    var tc = thumbnailCache;
                    if (tc != null && tc != GPSizeThumbnail.AlternateDataStream)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            GPSizeThumbnail.TryMove(tc, oldPathArray[i], newPathArray[i]);
                        }
                    }

                    // ブックマーク
                    var changedBookmark = false;
                    var changedBookmarkDisplayedName = false;
                    foreach (DataGridViewRow row in dgvDirectoryList.Rows)
                    {
                        var bookmark = row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark;
                        if (bookmark != null && bookmark.SimpleBookmark.SpecialRole == SimpleBookmarkSpecialRole.None) // セパレーターなら無視される
                        {
                            var sb = bookmark.SimpleBookmark;
                            var i = Array.IndexOf(oldPathArray, sb.Location);
                            if (i >= 0)
                            {
                                changedBookmark = true;
                                sb.Location = newPathArray[i];
                                if (bookmark.SimpleBookmark.Alias == null) changedBookmarkDisplayedName = true;
                            }
                        }
                    }
                    if (changedBookmark)
                    {
                        if (changedBookmarkDisplayedName)
                        {
                            dgvDirectoryList.Invalidate();
                        }
                        if (needToSaveBookmark)
                        {
                            saveBookmarkToIni_bookmarkChanged = true;
                            saveBookmarkToConfig();
                        }
                    }


                    // ブックマーク内と現在開いている SOR 及び履歴の中身
                    var renameTargetsSorSet = new HashSet<string>(from bmk in getVirtualFoldersInBookmarks() select bmk.SimpleBookmark.Location); // getVirtual は null を含むものは返さない
                    renameTargetsSorSet.Add(Program.HistorySorPath);
                    if (!CreatingZipPathArray && InVirtualDirectory && (current?.ToUpper().EndsWith(".SOR") == true)) // Virtual の中でも特に SOR のみ
                    {
                        renameTargetsSorSet.Add(current);
                    }
                    foreach (var renameTarget in renameTargetsSorSet)
                    {
                        // 開いている .sor は結果（例外）をすぐに使うので同期で、ほかの .sor と履歴は背景で直列に書き換える（K23。
                        // 以前は評価・タグ・名前を変えるたびに UI スレッドで全部の .sor を読み書きしていた）
                        if (current != renameTarget)
                        {
                            VirtualFolder.ReplaceBookmarkDataInBackground(renameTarget, oldPathArray, newPathArray);
                            continue;
                        }
                        try
                        {
                            VirtualFolder.ReplaceBookmarkData(renameTarget, oldPathArray, newPathArray);
                        }
                        catch (Exception e)
                        {
                            if (current == renameTarget && innerCurrentEditException == null) innerCurrentEditException = e;
                        }
                    }

                    // ブックマーク内と現在開いている KDD の中身
                    var renameTargetsKdkSet = new HashSet<string>(from bmk in getSmartFolderInBookmarks() select bmk.SimpleBookmark.Location); // getVirtual は null を含むものは返さない
                    if (!CreatingZipPathArray && InSmartDirectory && current != null && SmartFolder.IsSmartFolderPath(current))
                    {
                        renameTargetsKdkSet.Add(current);
                    }
                    var oldPathInLower = (from path in oldPathArray select path.ToLower()).ToArray();
                    foreach (var renameTarget in renameTargetsKdkSet)
                    {
                        try
                        {
                            var smartFolder = new SmartFolder(renameTarget);
                            var items = smartFolder.Items;
                            if (items == null) continue;
                            var changed = false;
                            foreach (var item in smartFolder.Items)
                            {
                                var path = item.Path;
                                if (path == null) continue;
                                var idx = Array.IndexOf(oldPathInLower, path.ToLower());
                                if (idx < 0) continue;
                                changed = true;
                                item.Path = newPathArray[idx];
                            }
                            if (changed)
                            {
                                smartFolder.SaveToFile(renameTarget);
                            }
                        }
                        catch (Exception e)
                        {
                            if (current == renameTarget && innerCurrentEditException == null) innerCurrentEditException = e;
                        }
                    }

                    // UndoBuffer
                    foreach (var b in undoBuffer)
                    {
                        var i = Array.IndexOf(oldPathArray, b.Location);
                        if (i >= 0) b.Location = newPathArray[i];

                        var bSelectedName = b.SelectedFileName;
                        if (!string.IsNullOrEmpty(bSelectedName))
                        {
                            for (var j = 0; j < count; j++)
                            {
                                if (oldPathArray[j].EndsWith(bSelectedName))
                                {
                                    b.SelectedFileName = newPathArray[j].Substring(oldPathArray[j].Length - bSelectedName.Length);
                                    break;
                                }
                            }
                        }
                    }
                })));
            }
            catch (ObjectDisposedException) { }


            if (innerCurrentEditException != null) throw innerCurrentEditException;
        }


        private string getRelativePath(string fullPath, string oldRelativePath)
        {
            return getRelativePath(fullPath, oldRelativePath.EndsWith(Path.DirectorySeparatorChar.ToString()));
        }

        private string getRelativePath(string fullPath, bool isDir)
        {
            string result = null;
            var cl = currentLocation;
            if (fullPath.Length >= cl.Length + 2 && fullPath.StartsWith(cl))
            {
                var sp = fullPath[cl.Length];
                if (sp == Path.DirectorySeparatorChar || sp == Path.AltDirectorySeparatorChar)
                {
                    result = fullPath.Substring(cl.Length + 1);
                }
            }
            if (result == null) result = fullPath.Substring(fullPath.LastIndexOfAny(new char[2] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) + 1);
            return result + (isDir ? Path.DirectorySeparatorChar.ToString() : "");
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
            //renameForTextBoxRatingTagPageSequence(noop: KeyboardShortcut.GetKeyState(Keys.Escape));
            renameForTextBoxRatingTagPageSequence(noop: cmsRightClick_Closing_CloseReason == ToolStripDropDownCloseReason.Keyboard);
            //MessageBox.Show(cmsRightClick_Closing_CloseReason.ToString());

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
                //OpenInDefaultViewer(ApplicationProviderUser.OpenFileDialog, openFileDialog.FileName);
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

            //pnlRight.Dock = ckd ? DockStyle.Right : DockStyle.Fill;

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

        private void dgvFileList_KeyDown(object sender, KeyEventArgs e)
        {
            var mk = ModifierKeys;
            if (e.KeyCode == Keys.Enter)
            {
                OpenSelectedArchiveInDefaultViewer(ApplicationProviderUser.EnterKey, null);
                e.Handled = true;
                LatestControledControl = sender as Control;
            }
            else if (e.KeyCode == Keys.F2)
            {
                renameToolStripMenuItem_Click(sender, e);
                LatestControledControl = sender as Control;
            }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                LatestControledControl = sender as Control;
            }
            else if (e.KeyCode == Keys.Home)
            {
                LatestControledControl = dgvFileList;
                if (SelectItem(0, dgvFileList.CurrentRow?.Index ?? -1, mk))
                {
                    e.Handled = true;
                    if (tvCatalog.SelectedIndicesCount > 1) dgvFileList.FirstDisplayedScrollingRowIndex = 0; // 単数選択なら自動的にスクロールされる
                    else tvCatalog.ScrollBarPercentage = 0; // 単数の場合のみサムネイル側もスクロール
                }
            }
            else if (e.KeyCode == Keys.End)
            {
                LatestControledControl = dgvFileList;
                if (SelectItem(-1, dgvFileList.CurrentRow?.Index ?? -1, mk))
                {
                    e.Handled = true;
                    if (tvCatalog.SelectedIndicesCount > 1) dgvFileList.FirstDisplayedScrollingRowIndex = dgvFileList.Rows.Count - 1;
                    else tvCatalog.ScrollBarPercentage = 100;
                }
            }
            else if (commonKeyAction(e, mk))
            {
                return;
            }
            else if (e.Alt)
            {
                e.Handled = true;
            }
        }

        private bool SelectItem(int showIndex, int currentShowIndex, Keys modifiedKeys)
        {
            if (CreatingZipPathArray) return false;
            var s2d = tvCatalog.ShowIndexToDataIndex;
            if (s2d == null) return false;
            var count = s2d.Length;
            if (count <= 0) return false;
            if (showIndex < 0) showIndex += count;
            if (showIndex < 0 || showIndex >= count) return false;
            if ((modifiedKeys & Keys.Shift) != Keys.Shift) currentShowIndex = -1;
            if (currentShowIndex < 0 || currentShowIndex >= count || currentShowIndex == showIndex)
            {
                tvCatalog.SelectedIndex = s2d[showIndex];
            }
            else
            {
                var selectedShowTable = new bool[count];
                var alreadySelected = tvCatalog.SelectedIndices;
                var d2s = tvCatalog.DataIndexToShowIndex;
                var selectedCount = alreadySelected.Length;
                for (var i = 0; i < alreadySelected.Length; i++) selectedShowTable[d2s[alreadySelected[i]]] = true;
                int start, stop;
                if (showIndex < currentShowIndex)
                {
                    start = showIndex;
                    stop = currentShowIndex + 1;
                }
                else
                {
                    start = currentShowIndex;
                    stop = showIndex + 1;
                }
                for (var i = start; i < stop; i++)
                {
                    if (!selectedShowTable[i])
                    {
                        selectedShowTable[i] = true;
                        selectedCount++;
                    }
                }
                var selectedIndices = new int[selectedCount];
                var k = 0;
                for (var i = 0; i < selectedShowTable.Length; i++)
                {
                    if (selectedShowTable[i])
                    {
                        selectedIndices[k++] = s2d[i];
                    }
                }
                tvCatalog.SelectedIndices = selectedIndices;
            }
            return true;
        }

        private void cmsRightClickPrepareAndShowForKeyOnFileList(bool onlyTag, Keys modifierKeys)
        {
            var mk = modifierKeys;
            //var mk = ModifierKeys;
            if (tvCatalog.SelectedIndicesCount > 0)
            {
                var selectedRow = dgvFileList_FirstDisplayedSelectedRow();
                if (selectedRow == null) return;
                var rect = dgvFileList.GetRowDisplayRectangle(selectedRow.Index, cutOverflow: true);
                var m2 = Math.Min(rect.Height, rect.Width) / 2;
                var p = dgvFileList.PointToScreen(new Point(rect.X + m2, rect.Y + m2));

                //cmsRightClickPrepareAndShow(p, onlyTag);
                if (onlyTag)
                {
                    cmsRightClickPrepareAndShow_(p, true);
                }
                else
                {
                    rightClickContextMenuPrepareAndShow(p, mk);
                }
            }
            else if (!onlyTag && dgvFileList.Visible && (RightClickContextMenu != CatalogFormConetxtMenu.Tag))
            {
                var p = dgvFileList.PointToScreen(dgvFileList.Location);
                p.Y += dgvFileList.ColumnHeadersHeight;
                //cmsRightClickPrepareAndShow_(p);

                rightClickContextMenuPrepareAndShow(p, mk);
            }
        }

        private DataGridViewRow dgvFileList_FirstDisplayedSelectedRow()
        {
            return getFirstDisplayedSelectedRow(dgvFileList);
        }

        private static DataGridViewRow getFirstDisplayedSelectedRow(DataGridView dataGridView)
        {
            try
            {
                var start = dataGridView.FirstDisplayedScrollingRowIndex;
                var stop = start + dataGridView.DisplayedRowCount(includePartialRow: true);
                for (var i = start; i < stop; i++)
                {
                    if ((dataGridView.Rows.GetRowState(i) & DataGridViewElementStates.Selected) != 0) return dataGridView.Rows[i];
                }
            }
            catch
            {
            }
            return null;
        }

        string renameToolStripMenuItem_Click_OldName = null;
        string renameToolStripMenuItem_Click_OldPath = null;
        int renameToolStripMenuItem_Click_Index = -1;
        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!dgvFileList.Focused) return;
            if (!fileListToolStripMenuItem.Checked || !dgvFileList.Visible) return;

            if (renameToolStripMenuItem_Click_OldName != null) return;
            if (CreatingZipPathArray) return;

            try
            {
                if (!InDirectoryVirtualDirectoryOrSmartDirectory) return;
            }
            catch
            {
                return;
            }

            if (!dgvFileList.Focused) dgvFileList.Focus();

            var selectedIndex = tvCatalog.SelectedIndex;
            if (selectedIndex < 0) return;

            string currentPath;
            bool isDirectory;
            lock (ZipPathArrayLocker)
            {
                if (ZipPathArray == null || ZipPathArray.Length <= selectedIndex) return;
                currentPath = ZipPathArray[selectedIndex];
                isDirectory = ZipNameArray[selectedIndex].EndsWith(Path.DirectorySeparatorChar.ToString());
            }
            if (isDirectory && !Directory.Exists(currentPath) || !isDirectory && !File.Exists(currentPath))
            {
                var exception = new FileNotFoundException(null, currentPath);
                MessageBox.Show(this, exception.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var d2s = tvCatalog.DataIndexToShowIndex;
            if (selectedIndex >= d2s.Length) return;
            var showIndex = d2s[selectedIndex];
            if (showIndex < 0 || showIndex >= dgvFileList.RowCount) return;
            var info = getFileListRowInfo(showIndex);
            if (info == null) return;
            var cell = dgvFileList[tbcFileName.Index, showIndex];

            // 仮想モードなのでセルに値を入れられない。編集中は CellValueNeeded で拡張子を除いた名前を返す
            var name = info.Texts[tbcFileName.Index] ?? "";
            fileListEditingShowIndex = showIndex;
            fileListEditingText = getBaseName(name);
            dgvFileList.InvalidateCell(tbcFileName.Index, showIndex);

            renameToolStripMenuItem_Click_OldName = name;
            renameToolStripMenuItem_Click_Index = selectedIndex;
            renameToolStripMenuItem_Click_OldPath = currentPath;

            dgvFileList.CurrentCell = cell;
            dgvFileList.ReadOnly = false;
            dgvFileList.BeginEdit(true);
        }

        private void tvCatalog_ShowIndexToDataIndexChanged(object sender, EventArgs e)
        {
            var itemsCount = tvCatalog.ShowIndexToDataIndex.Length;
            itemsCountToolStripStatusLabel.Text = itemsCount >= 0 ? Message._1Items.Replace("$1", itemsCount.ToString()) : "";
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

        // private string cbFilter_DropDown_Text = null;
        private void cbFilter_DropDown(object sender, EventArgs e)
        {
            setCurrentFilterToHistory(bringToTop: false);
        }

        private readonly Dictionary<string, SizeF> cbFilter_DrawItem_SizeDictionary = new Dictionary<string, SizeF>();
        private void cbFilter_DrawItem(object sender, DrawItemEventArgs e)
        {
            var index = e.Index;
            if (index < 0) return;
            var items = cbFilter.Items;
            if (index >= items.Count) return;
            var text = items[index].ToString();
            var size = MeasureStringSizeByGraphics(e.Graphics, text, cbFilter.Font, cbFilter_DrawItem_SizeDictionary); //e.Graphics.MeasureString(text, cbFilter.Font);
            var rect = e.Bounds;
            Color color;
            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            if (selected)
            {
                color = SystemColors.Highlight;
            }
            else if (aliasesToFilteringStrings != null && aliasesToFilteringStrings.ContainsKey(text))
            {
                color = AliasColor;
            }
            else
            {
                color = SystemColors.Window;
            }
            using (var b = new SolidBrush(color))
            {
                e.Graphics.FillRectangle(b, rect);
            }
            rect.Y += (int)Math.Ceiling((rect.Height - size.Height) / 2);
            e.Graphics.DrawString(text, cbFilter.Font, selected ? SystemBrushes.HighlightText : SystemBrushes.WindowText, rect);
        }


        private void btnGoToBack_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                //showUndoBufferList(0, undoBufferIndex + 1);
                showUndoBufferList(0, undoBuffer.Count);
            }
        }

        private void btnGoToForward_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                //showUndoBufferList(undoBufferIndex, undoBuffer.Count);
                showUndoBufferList(0, undoBuffer.Count);
            }
        }

        private ContextMenuStrip showUndoBufferList_ContextMenuStrip = null;
        private void showUndoBufferList(int fromInclusive, int toExclusive)
        {
            if (fromInclusive < 0 || fromInclusive >= toExclusive || toExclusive > undoBuffer.Count) return;

            ContextMenuStrip cms;
            if (showUndoBufferList_ContextMenuStrip != null)
            {
                cms = showUndoBufferList_ContextMenuStrip;
                foreach (var item in (from ToolStripMenuItem item in cms.Items select item).ToArray())
                {
                    item.Dispose();
                }
            }
            else
            {
                cms = new ContextMenuStrip();
                cms.Closed += (sender2, e2) =>
                {
                    AllowFullpower = true;
                };
                ToolStripDropDownScroller.Enscrollable(cms, ToolStripDropDownScroller.GetGestureListener(cms));
                showUndoBufferList_ContextMenuStrip = cms;
            }
            var items = new ToolStripMenuItem[toExclusive - fromInclusive];
            for (var i = fromInclusive; i < toExclusive; i++)
            {
                var buf = undoBuffer[i];
                var path = buf.Location;
                var item = new ToolStripMenuItem(SimpleBookmark.GetDisplayName(path));

                if (i == undoBufferIndex)
                {
                    item.Font = Program.StyledFont(item.Font, FontStyle.Bold);
                    item.CheckState = CheckState.Indeterminate;
                }
                else
                {
                    item.Font = Program.StyledFont(item.Font, FontStyle.Regular);
                    var ii = i;
                    item.Click += (sender2, e2) =>
                    {
                        gotToUnderBuffer(ii, ApplicationProviderUser.MouseClick);
                    };
                }

                var backColor = buf.ProfileColor;
                var foreColor = ZipTag.GetForeColor(backColor);
                item.BackColor = backColor;
                item.ForeColor = foreColor;

                if (ColorEquals(foreColor, Color.White))
                {
                    item.ForeColor = foreColor;
                    ForeColorManager.Set(item);
                }

                CheckMarkProvider.SetCheckMark(cms, item, DisplayCheckMark.Select);

                items[toExclusive - i - 1] = item;
            }

            cms.Items.AddRange(items);


            var maxSize = cms.MaximumSize;
            var area = Screen.FromControl(btnGoToBack).WorkingArea;
            var buttonBound = RectangleToScreen(btnGoToBack.Bounds);
            cms.MaximumSize = new Size(maxSize.Width, Math.Max(buttonBound.Height * 2 * SystemInformation.MouseWheelScrollLines, area.Bottom - (buttonBound.Bottom - 1)));

            AllowFullpower = false;
            if (this != ActiveForm) Activate();
            cms.Show(new Point(buttonBound.Left, buttonBound.Bottom - 1));
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


        private void changeCurrentProfileColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AllowFullpower = false;

            var bookmarksWithCurrentColor = GetBookmarksStringColoredBy(currentProfileColor);
            var message = bookmarksWithCurrentColor != null ?
                $"{Message.WillChangeCurrentProfileColor}\n{Message.ThisChangeAlsoAffectsFollowingBookmarks}\n\n{bookmarksWithCurrentColor}\n\n{Message.HowToCreateNewColorDescription}" :
                $"{Message.WillChangeCurrentProfileColor}\n\n{Message.HowToCreateNewColorDescription}";


            var result = MessageBox.Show(this, message, Message.ChangeCurrentProfileColor, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (result == DialogResult.OK)
            {
                AllowFullpower = false;
            }
            else
            {
                AllowFullpower = true;
                return;
            }
            
            var newColor = getNewProfileColorFromDialog(useCurrentAsDefault: true);
            if (!newColor.IsEmpty)
            {
                foreach (DataGridViewRow row in dgvDirectoryList.Rows)
                {
                    var bookmark = row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark;
                    if (bookmark != null && bookmark.SimpleBookmark.SpecialRole == SimpleBookmarkSpecialRole.None && bookmark.Color == currentProfileColor)// セパレーターなら無視される
                    {
                        bookmark.Color = newColor;
                    }
                }

                foreach (var buf in undoBuffer)
                {
                    if (buf.ProfileColor == currentProfileColor)
                    {
                        buf.ProfileColor = newColor;
                    }
                }

                saveBookmarkToIni_bookmarkChanged = true;
                saveBookmarkToConfig();

                currentProfileColor = newColor;

                dgvDirectoryList.Invalidate();
            }

            AllowFullpower = true;
        }

        private Color getNewProfileColorFromDialog(bool useCurrentAsDefault)
        {
            using (var cd = new ColorDialog())
            {
                Color dc;
                bool defaultExists = useCurrentAsDefault;
                if (defaultExists)
                {
                    dc = currentProfileColor;
                    var cc = cd.CustomColors;
                    cc[cc.Length - 1] = dc.R | dc.G << 8 | dc.B << 16;
                    cd.CustomColors = cc;
                    cd.Color = dc;
                }
                else
                {
                    dc = Color.Empty;
                }

                cd.AllowFullOpen = true;
                cd.SolidColorOnly = false;
                AllowFullpower = false;
                while (cd.ShowDialog(this) == DialogResult.OK)
                {
                    var newColor = cd.Color;

                    if (defaultExists && ColorEquals(dc, newColor))
                    {
                        AllowFullpower = false;
                        return Color.Empty;
                    }

                    var sameColorBookmarks = GetBookmarksStringColoredBy(newColor);

                    if (!string.IsNullOrEmpty(sameColorBookmarks))
                    {
                        // ウインドウが切り替わると AllowFullpower が復帰する
                        AllowFullpower = false;
                        if (MessageBox.Show(this, Message.SelectedColorIsUsedByFollowingBookmarks + "\n\n" + sameColorBookmarks
                            + (useCurrentAsDefault ? "" : ("\n\n" + Message.HowToAddUsedColorDescription)), null, MessageBoxButtons.OKCancel, MessageBoxIcon.Error) == DialogResult.OK)
                        {
                            AllowFullpower = false;
                            continue;
                        }
                        else
                        {
                            AllowFullpower = false;
                            return Color.Empty;
                        }
                    }

                    return (SerializableRGB)newColor;
                }
                return Color.Empty;
            }
        }

        private string GetBookmarksStringColoredBy(Color color)
        {
            var sb = new StringBuilder();
            string lastLine = null;
            const int showLimit = 10;
            var lineCount = 0;
            foreach (DataGridViewRow row in dgvDirectoryList.Rows)
            {
                var bookmark = row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark;
                if (bookmark == null || bookmark.SimpleBookmark.SpecialRole != SimpleBookmarkSpecialRole.None) continue; // セパレーターなら無視される
                if (ColorEquals(bookmark.Color, color))
                {
                    var line = bookmark.SimpleBookmark?.GetDisplayString() ?? "";
                    if (lineCount == 0)
                    {
                        sb.Append(line);
                    }
                    else if (lineCount < showLimit - 1)
                    {
                        sb.AppendLine().Append(line);
                    }
                    else if (lastLine == null)
                    {
                        lastLine = line;
                    }
                    else
                    {
                        sb.AppendLine().Append("...");
                        lastLine = null;
                        break;
                    }
                    lineCount++;
                }
            }
            if (lineCount == 0) return null;
            if (lastLine != null) sb.AppendLine().Append(lastLine);
            return sb.ToString();
        }

        private void newSmartFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openSmartFolderSettingForm(null); // AllowFullpower 込み
        }

        private void openSmartFolderSettingForm(string smartFolderPath)
        {
            AllowFullpower = false;

            string output;
            using (var smartFolderSettingForm = new SmartFolderSettingForm(smartFolderPath))
            {
                smartFolderSettingForm.ShowDialog(this);
                output = smartFolderSettingForm.Edited ? smartFolderSettingForm.TargetSmartFolderPath : null;
            }
            if (output != null)
            {
                zabLocation.Text = output;
                MakePreview();
            }

            AllowFullpower = true;
        }

        private void setEraserAndSearchIcon()
        {
            var iconSize = cmsRightClick.ImageScalingSize;
            SelectionToFilterToolStripMenuItem.Icon = getIcon(drawInvertSearchIcon, iconSize.Width, iconSize.Height);

            SelectionToClipBoardToolStripMenuItem.Icon = getIcon(drawClipboardIcon, iconSize.Width, iconSize.Height);

            btnLocationCommands.FlatStyle = FlatStyle.Flat;
            btnLocationCommands.FlatAppearance.BorderColor = BackColor;
            btnLocationCommands.Text = null;
            btnLocationCommands.Image = getIcon(drawEraserIcon, btnLocationCommands.ClientRectangle.Width, btnLocationCommands.ClientRectangle.Height);

            btnFilterCommands.FlatStyle = FlatStyle.Flat;
            btnFilterCommands.FlatAppearance.BorderColor = BackColor;
            btnFilterCommands.Text = null;
            //btnFilterCommands.Paint += btnFilterCommands_Paint;
            btnFilterCommands.Image = getIcon(drawInvertSearchIcon, btnFilterCommands.ClientRectangle.Width, btnFilterCommands.ClientRectangle.Height);

            //pbSearch.Image = getSearchIcon(pbSearch.ClientRectangle.Width, pbSearch.ClientRectangle.Height);
        }


        private Bitmap getIcon(Action<Graphics, RectangleF> drawer, int width, int height)
        {
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                drawer(g, new Rectangle(0, 0, width, height));
            }
            return result;
        }

        private void drawEraserIcon(Graphics g, RectangleF rect)
        {
            var x = rect.X;
            var y = rect.Y;
            var width = rect.Width;
            var height = rect.Height;

            const double upperTop = 0.15;
            const double upperBottom = 0.35;
            const double upperWidth = 0.45;

            const double lowerTop = 0.45;
            const double lowerBottom = 0.85;
            const double lowerWidth = 0.55;

            var ut = (int)Math.Round(y + lowerTop * height);
            var ub = (int)Math.Round(y + lowerBottom * height);
            var ul = (int)Math.Round(x + (1 - lowerWidth) * height / 2);
            var ur = (int)Math.Round(rect.Right - (ul - rect.Left));

            var lt = (int)Math.Round(y + upperTop * height);
            var lb = (int)Math.Round(y + upperBottom * height);
            var ll = (int)Math.Round(x + (1 - upperWidth) * height / 2);
            var lr = (int)Math.Round(rect.Right - (ll - rect.Left));

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.FillRectangle(Brushes.Black, ul - 1, ut - 1, ur - ul, ub - ut);
            g.FillRectangle(Brushes.Black, ll - 1, lt - 1, lr - ll, lb - lt);

        }

        private void drawInvertSearchIcon(Graphics g, RectangleF rect)
        {
            drawSearchIcon(g, new RectangleF(rect.Right, rect.Y, -rect.Width, rect.Height));
        }

        private void drawSearchIcon(Graphics g, RectangleF rect)
        {
            var x = rect.X;
            var y = rect.Y;
            var width = rect.Width;
            var height = rect.Height;

            const double thk = 0.15;

            const double rectX0 = 0.20;
            const double rectX1 = 0.65;

            const double lineX1 = 0.8;
            const double lineY1 = 0.8;

            const double rectY0 = rectX0;
            const double rectY1 = rectX1;
            const double lineX0 = (rectX0 + rectX1) / 2 + (rectX1 - rectX0) / (2 * 1.41421356237309504880168872421);
            const double lineY0 = lineX0;

            using (var p = new Pen(Color.Black, (float)((thk / 2) * (Math.Abs(width) + Math.Abs(height)))))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                g.DrawEllipse(p, new RectangleF((float)(x + rectX0 * width) - 1, (float)(y + rectY0 * height) - 1, (float)((rectX1 - rectX0) * width), (float)((rectY1 - rectY0) * height)));

                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawLine(p, new PointF((float)(x + lineX0 * width) - 1, (float)(y + lineY0 * height) - 1), new PointF((float)(x + lineX1 * width) - 1, (float)(y + lineY1 * height) - 1));
            }
        }

        private void drawClipboardIcon(Graphics g, RectangleF rect)
        {
            var x = rect.X;
            var y = rect.Y;
            var width = rect.Width;
            var height = rect.Height;

            const double thk = 0.1;
            const double thk2 = thk / 2;

            const double rectX = 0.20;
            const double rectY = 0.10;
            const double strnX = 0.35;
            const double strnY = 0.32;

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            using (var p = new Pen(Color.Black, (float)((thk / 2) * (Math.Abs(width) + Math.Abs(height)))))
            {
                g.DrawRectangle(p, (float)(x + rectX * width) - 1, (float)(y + rectY * height) - 1, (float)((1 - 2 * rectX) * width), (float)((1 - 2 * rectY) * height));
            }

            using (var p = new Pen(Color.Black, (float)((thk2 / 2) * (Math.Abs(width) + Math.Abs(height)))))
            {
                var px1 = (float)(x + strnX * width) - 1;
                var px2 = (float)(x + (1 - strnX) * width) - 1;
                float py;

                py = (float)(y + strnY * height) - 1;
                g.DrawLine(p, new PointF(px1, py), new PointF(px2, py));

                py = (float)(y + 0.5 * height) - 1;
                g.DrawLine(p, new PointF(px1, py), new PointF(px2, py));

                py = (float)(y + (1 - strnY) * height) - 1;
                g.DrawLine(p, new PointF(px1, py), new PointF(px2, py));
            }
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
            RightClickContextMenu = CatalogFormConetxtMenu.ZipPla;
        }

        private void standardMiddleclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MiddleClickContextMenu = CatalogFormConetxtMenu.ZipPla;
        }

        private void standardCtrlRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ControlRightClickContextMenu = CatalogFormConetxtMenu.ZipPla;
        }

        private void OnlyTagRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RightClickContextMenu = CatalogFormConetxtMenu.Tag;
        }

        private void OnlyTagMiddleclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MiddleClickContextMenu = CatalogFormConetxtMenu.Tag;
        }

        private void onlyTagCtrlRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ControlRightClickContextMenu = CatalogFormConetxtMenu.Tag;
        }

        private void explorerRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RightClickContextMenu = CatalogFormConetxtMenu.Explorer;
        }

        private void explorerMiddleclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MiddleClickContextMenu = CatalogFormConetxtMenu.Explorer;
        }

        private void explorerCtrlRightclickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ControlRightClickContextMenu = CatalogFormConetxtMenu.Explorer;
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

        private void tvCatalog_Paint(object sender, PaintEventArgs e)
        {
            if (showStartHelpMessage && tvCatalog.Visible)
            {
                drawInitialMessage(e, tvCatalog.ClientRectangle, tvCatalog.BackColor);
            }
        }

        Font drawInitialMessageFont = null;
        private Font GetDrawInitialMessageFont()
        {
            if (drawInitialMessageFont == null)
            {
                drawInitialMessageFont = new Font(DefaultFont.FontFamily, DefaultFont.SizeInPoints * 1.7f);
            }
            return drawInitialMessageFont;
        }

        private void drawInitialMessage(PaintEventArgs e, Rectangle rect, Color backColor)
        {
            // ファイルリストにも表示させる場合共通設定終了時に描画を追加すること
            var drawInitialMessageFont = GetDrawInitialMessageFont();

            if (dragAndDropAction == DragAndDropAction.ShowThumbnails)
            {
                // アンチエイリアスが効かない
                //TextRenderer.DrawText(e.Graphics, Message.FolderOpenDescription.Replace("\\n", "\n"), drawInitialMessageFont, rect, SystemColors.GrayText, backColor,
                //    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

                var preMode = e.Graphics.TextRenderingHint;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                var message = Message.FolderOpenDescription.Replace("\\n", "\n");
                var size = e.Graphics.MeasureString(message, drawInitialMessageFont, rect.Size);
                var rect2 = new RectangleF(rect.X + (rect.Width - size.Width) / 2, rect.Y + (rect.Height - size.Height) / 2, size.Width, size.Height);

                e.Graphics.DrawString(message, drawInitialMessageFont, SystemBrushes.GrayText, rect2);

                e.Graphics.TextRenderingHint = preMode;
            }
            else
            {

            }

        }

        private void mouseGestureSettingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AllowFullpower = false;

            var edited = Program.OpenMouseGestureSetting(Icon, mgCatalog, getMouseGestureSettingTemplate(), this);

            if (edited)
            {
                mgFileList.Actions = mgCatalog.Actions;
                mgFileList.Enabled = mgCatalog.Enabled;
                mgFileList.Width = mgCatalog.Width;
                mgFileList.Color = mgCatalog.Color;
            }

            AllowFullpower = true;
        }


        public enum Command { GoBack, GoForward, GoUp, ScrollToTop, ResetFilter, ScrollToBottom, ScrollToSelection, Reload, Exit }
        public class MouseGestureCommand { public MouseGestureDirection[] Gesture; public Command Command; }
        private MouseGestureSettingTemplate[] getMouseGestureSettingTemplate()
        {
            return new MouseGestureSettingTemplate[]
            {
                new MouseGestureSettingTemplate( (int)Command.GoBack, Message.GoBack,
                   btnGoToBack.PerformClick),
                new MouseGestureSettingTemplate( (int)Command.GoForward, Message.GoForward,
                   btnGoToForward.PerformClick),
                new MouseGestureSettingTemplate( (int)Command.GoUp, Message.GoUp,
                   goUpFromCurrentLocation),
                new MouseGestureSettingTemplate( (int)Command.ResetFilter, Message.ResetFilter,
                   () => cbFilter.Text = ""),
                new MouseGestureSettingTemplate( (int)Command.ScrollToTop, Message.ScrollToTop,
                   () => {
                       if(tvCatalog.Focused)
                       {
                           tvCatalog.ScrollBarPercentage = 0;
                       }
                       else if(dgvFileList.Focused && dgvFileList.FirstDisplayedScrollingRowIndex > 0)
                       {
                           dgvFileList.FirstDisplayedScrollingRowIndex = 0;
                       }
                   }),
                new MouseGestureSettingTemplate( (int)Command.ScrollToBottom, Message.ScrollToBottom,
                   () => {
                       if(tvCatalog.Focused)
                       {
                           tvCatalog.ScrollBarPercentage = 100;
                       }
                       else if(dgvFileList.Focused && dgvFileList.FirstDisplayedScrollingRowIndex >= 0)
                       {
                           dgvFileList.FirstDisplayedScrollingRowIndex = dgvFileList.Rows.Count - 1;
                       }
                   }),
                new MouseGestureSettingTemplate( (int)Command.ScrollToSelection, Message.ScrollToSelection,
                   () => {
                       if(tvCatalog.Focused)
                       {
                           var index = tvCatalog.SelectedIndex;
                           if(index >= 0)
                           {
                                tvCatalog.ScrollBarToIndexWithMinimalMove(tvCatalog.DataIndexToShowIndex[index]);
                           }
                       }
                       else if(dgvFileList.Focused)
                       {
                           selectionSynchronizingFromThumbnailToFileList(changeScroll: true);
                       }
                   }),
                new MouseGestureSettingTemplate( (int)Command.Reload, Message.Reload,
                   userReload),

                new MouseGestureSettingTemplate( (int)Command.Exit, Message.Exit,
                   Close),
            };
        }


        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            ActivateManager.WndProcOverrider(base.WndProc, ref m);
        }
    }

}
