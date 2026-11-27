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

        CatalogFormContextMenu rightClickContextMenu = CatalogFormContextMenu.ZipPla;
        CatalogFormContextMenu middleClickContextMenu = CatalogFormContextMenu.Tag;
        CatalogFormContextMenu controlRightClickContextMenu = CatalogFormContextMenu.Explorer;
        CatalogFormContextMenu RightClickContextMenu
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
        CatalogFormContextMenu MiddleClickContextMenu
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
        CatalogFormContextMenu ControlRightClickContextMenu
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
            standardRightclickToolStripMenuItem.Checked = CatalogFormContextMenu.ZipPla == rightClickContextMenu;
            standardMiddleclickToolStripMenuItem.Checked = CatalogFormContextMenu.ZipPla == middleClickContextMenu;
            standardCtrlRightclickToolStripMenuItem.Checked = CatalogFormContextMenu.ZipPla == controlRightClickContextMenu;

            OnlyTagRightclickToolStripMenuItem.Checked = CatalogFormContextMenu.Tag == rightClickContextMenu;
            OnlyTagMiddleclickToolStripMenuItem.Checked = CatalogFormContextMenu.Tag == middleClickContextMenu;
            onlyTagCtrlRightclickToolStripMenuItem.Checked = CatalogFormContextMenu.Tag == controlRightClickContextMenu;

            explorerRightclickToolStripMenuItem.Checked = CatalogFormContextMenu.Explorer == rightClickContextMenu;
            explorerMiddleclickToolStripMenuItem.Checked = CatalogFormContextMenu.Explorer == middleClickContextMenu;
            explorerCtrlRightclickToolStripMenuItem.Checked = CatalogFormContextMenu.Explorer == controlRightClickContextMenu;
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

        private readonly Dictionary<object, HashSet<string>> OpenedByExternalApplication_Dictionary = new Dictionary<object, HashSet<string>>();
        private bool IsOpenedByExternalApplication(string fullPath)
        {
            if (fullPath == null) return false;
            fullPath = fullPath.ToLower();
            return OpenedByExternalApplication_Dictionary.Any(pair => pair.Value.Contains(fullPath));
        }

        public async void SetOpenedByExternalApplication(Process p, IEnumerable<string> fullPathSet, bool selectionMirror)
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
            OpenedByExternalApplication_Dictionary[p] = set;

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
                OpenedByExternalApplication_Dictionary.Remove(p);
                p.Dispose();

                /*
                */
            }
            else
            {
                await Task.Run(() => p.WaitForExit()); // エクスプローラーを読んだ場合ここには到達するがすぐに終了する
                OpenedByExternalApplication_Dictionary.Remove(p);
                p.Dispose();
            }
        }
        public void AddToOpenedByExternalApplicationDictionary(object key, IEnumerable<string> path)
        {
            if (path == null) return;
            var set = new HashSet<string>(from p in path where !string.IsNullOrEmpty(p) select p.ToLower());
            if (!set.Any()) return;
            OpenedByExternalApplication_Dictionary[key] = set;
        }
        public void RemoveFromOpenedByExternalApplicationDictionary(object key)
        {
            OpenedByExternalApplication_Dictionary.Remove(key);
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
            Constructor();

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

            IsShown = true;
            thumbnailListToolStripMenuItem_CheckedChanged(null, null);
            //tbcIcon.Width = tbcIcon.HeaderCell.Size.Height; // tbcIcon.HeaderCell.Size.Height を使う場合ここで必要
            foreach (DataGridViewRow row in dgvDirectoryList.Rows) row.Selected = false;
            setTagFilteringMenuItems(needToRepairTagsFromBookmark: true, needToSaveCurrentTag: true, loadFromIni: true);


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


            Configuration.ClosePrepareMode();
            
            betterFormRestoreBounds = new BetterFormRestoreBounds(this);

            Program.StartCheckUpdateAndNgen(this, checkNgen);
            //PackedImageLoader.CheckSevenZipExistence(this); フォルダ読み込み時にチェックされるので不要


        }

        private ToolStripDropDown ratingFilterToolStripMenuItemDropDown;
        private ToolStripDropDown addToolStripMenuItemDropDown;

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

            InitializeComponent();
            fileSystemWatcher.Error += fileSystemWatcher_Error; // Y16


            Configuration.WaitPrepareTask();

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


            //ratingToolStripMenuItem.DropDown.KeyDown += ratingToolStripMenuItem_DropDown_KeyDown;
            new KeyMouseBinder(cmsRightClick, requiredKeyMouseBindingCmsRightRootOrTag);
            new KeyMouseBinder(tagToolStripMenuItem.DropDown, requiredKeyMouseBindingCmsRightRootOrTag);
            new KeyMouseBinder(ratingToolStripMenuItem.DropDown, requiredKeyMouseBindingCmsTrue);
            new KeyMouseBinder(pageSequenceToolStripMenuItem.DropDown, requiredKeyMouseBindingCmsTrue);


            tvCatalog.GotFocus += tvCatalog_GotFocus;
            dgvDirectoryList.GotFocus += dgvDirectoryList_GotFocus;
            dgvFileList.GotFocus += dgvFileList_GotFocus;
            explorerTreeView.TreeViewWnd.GotFocus += explorerTreeView_TreeViewWnd_GotFocus;


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


            openInExplorerInDirectoryListToolStripMenuItem.Image = ExplorerIcon;// ExplorerProvider.Icon;


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


            //Text = Program.Name;
            SetMessagesWithoutTagFilter();

            // 静的な Shown に移動
            //Shown += (sender, e) => setTagFilteringMenuItems();


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

            zabLocation.GetDisplayNameOverrider = SimpleBookmark.GetDisplayName;
            zabLocation.TextChangedByButton += zabLocation_TextChangedByButton;
            zabLocation.TextChangedByBaseControl += zabLocation_TextChangedByBaseControl;
            zabLocation.CurrentLocationButtonClick += zabLocation_CurrentLocationButtonClick;
            zabLocation.DirectoryButtonContextMenuStripOpening += zabLocation_DirectoryButtonContextMenuStripOpening;

            zabLocation.AllowDrop = true;
            zabLocation.SubControlDragEnter += zabLocation_SubControlDragEnter;
            zabLocation.SubControlDragDrop += zabLocation_SubControlDragDrop;

            cbLocationMinimumWidth = cbLocation.Width;


            //var screenSize = Screen.FromControl(this).Bounds;
            var screenSize = Screen.PrimaryScreen.Bounds;

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


        private static readonly Color WindowColor = SystemColors.Window;// Color.FromKnownColor(KnownColor.Window);
        private static readonly Color InvalidColor = SystemColorsWindowIsNearToWhite ? Color.LightGray : Color.Gray;
        private static readonly Color ErrorColor = SystemColorsWindowIsNearToWhite ? Color.Pink : Color.DarkRed;
        private static readonly Color AliasColor = SystemColorsWindowIsNearToWhite ? Color.FromArgb(192, 255, 255) : Color.FromArgb(0, 64, 64);
        private static int ChangeThumbnailSize_MenuItemOffset = 2;
        static readonly double ChangeThumbnailSize_NeutralLength = Math.Sqrt(210 * 297);
        private void tvCatalog_ShowIndexToDataIndexChanged(object sender, EventArgs e)
        {
            var itemsCount = tvCatalog.ShowIndexToDataIndex.Length;
            itemsCountToolStripStatusLabel.Text = itemsCount >= 0 ? Message._1Items.Replace("$1", itemsCount.ToString()) : "";
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            ActivateManager.WndProcOverrider(base.WndProc, ref m);
        }
    }

}
