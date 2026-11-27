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
    public class CatalogProfile
    {
        //public string Location;
        public SortMode? LastSortMode;
        public SortMode? SortMode;
        public string RandomSeed;
        public string Filter;
        public int? ThumbnailSize;
        public double? AspectRatio;
        public bool? ShowIcon;
        public bool? ShowRating;
        public bool? ShowTags;
        public bool? ShowFilename;
        public ClipMode? ClipMode;
        public ThumbViewerAligningMode? AligningMode;
        public NumberOfLinesOfName? NumberOfLinesOfName;
        public double? MinimumFrameThickness;
        public int? ThumbnailMouseWheelScrollAmount;
        public ThumbViewerScrollUnit? ThumbnailMouseWheelScrollUnit;
        public bool? ThumbnailListVisible;
        public bool? FileListVisible;
        public DockStyle? FileListDock;
        public int? FileListWidth;
        public string[] FileListOrder;
        public bool? IconInFileList;
        public bool? PageInFileList;
        public bool? RatingInFileList;
        public bool? DateCreatedInFileList;
        public bool? DateAccessedInFileList;
        public bool? DateModifiedInFileList;
        public bool? SizeInFileList;
        public bool? ResolutionInFileList;
        public bool? RatioInFileList;
        public bool? WidthInFileList;
        public bool? HeightInFileList;
        public bool? LengthInFileList;
        public bool? FpsInFileList;
        public bool? TinyOnScreenKeyboardVisible;
        public SelectionHighlightMode? SelectionHighlightMode;
        public FilterRemovingMode? FilterRemovingMode;
        public bool? LoadLastViewedPageFromHistory;
        public DoubleClickMode? DoubleClickFileOpenMode;
        public CatalogFormConetxtMenu? RightClickContextMenu;
        public CatalogFormConetxtMenu? MiddleClickContextMenu;
        public CatalogFormConetxtMenu? ControlRightClickContextMenu;
        public ItemSelectionFeedback? ItemSelectionFeedback;
        public bool? SynchroFromTreeToBar;
        public bool? SynchroFromBarToTree;
        public bool? CollapseOthersWhenSynchronizing;
        public FolderSortMode? FolderSortMode;
        // public bool? CheckDirectorySize;
        public ArchivesInArchiveMode? ArchivesInArchiveMode;
        public ReadOnMemoryMode? ReadOnMemoryMode;
        public bool? OpenInPreviousImageFilterSetting;
        public BindingMode? DefaultBindingMode;
        public CoverBindingMode? CoverBindingMode;
        public SortSyncMode? SortSyncMode;
        public bool? AlwaysHideUI;
        public bool? AllowReadAheadProcess;
        public SerializableRGB? BackColorOfBuiltInViewer;
        public SerializableRGB? SelectedColorForBackColorOfBuiltInViewer;
        public InitialFullscreenMode? BuiltInViewerInitialFullscreenMode;
        public int? MaxCaptchaCountInOneVideo;
        public string DisplayArchiveFiles;
        public string DisplayImageFiles;
        public string DisplayVideoFiles;
        public string DisplayOtherFiles;
        public string ThumbnailCacheRoot;
        public SubfolderMode? SubfolderMode;
        public string[] FilterHistory;
        public bool? InvariantFilter;
        public KeyAndValue<string, string>[] FilterAliasToString;

        public ZipTag[] Tags;

        // シリアル化のため
        public CatalogProfile()
        {
        }


        public CatalogProfile(
            SortMode? lastSortMode,
            SortMode? sortMode,
            string randomSeed,
            string filter,
            int? thumbnailSize,
            double? aspectRatio,
            bool? showIcon,
            bool? showRating,
            bool? showTags,
            bool? showFilename,
            ClipMode? clipMode,
            ThumbViewerAligningMode? aligningMode,
            NumberOfLinesOfName numberOfLinesOfName,
            double? minimumFrameThickness,
            int thumbnailMouseWheelScrollAmount,
            ThumbViewerScrollUnit thumbnailMouseWheelScrollUnit,
            bool? thumbnailListVisible,
            bool? fileListVisible,
            DockStyle? fileListDock,
            int? fileListWidth,
            string[] fileListOrder,
            bool? iconInFileList,
            bool? pageInFileList,
            bool? ratingInFileList,
            bool? dateCreatedInFileList,
            bool? dateAccessedInFileList,
            bool? dateModifiedInFileList,
            bool? sizeInFileList,
            bool? resolutionInFileList,
            bool? ratioInFileList,
            bool? widthInFileList,
            bool? heightInFileList,
            bool? lengthInFileList,
            bool? fpsInFileList,
            bool tinyOnScreenKeyboardVisible,
            SelectionHighlightMode selectionHighlightMode,
            FilterRemovingMode filterRemovingMode,
            bool loadLastViewedPageFromHistory,
            DoubleClickMode doubleClickFileOpenMode,
            CatalogFormConetxtMenu rightClickContextMenu,
            CatalogFormConetxtMenu middleClickContextMenu,
            CatalogFormConetxtMenu controlRightClickContextMenu,
            ItemSelectionFeedback itemSelectionFeedback,
            bool synchroFromTreeToBar,
            bool synchroFromBarToTree,
            bool collapseOthersWhenSynchronizing,
            FolderSortMode folderSortMode,
            // bool checkDirectorySize,
            ArchivesInArchiveMode archivesInArchiveMode,
            ReadOnMemoryMode readOnMemoryMode,
            bool openInPreviousImageFilterSetting,
            BindingMode defaultBindingMode,
            CoverBindingMode coverBindingMode,
            SortSyncMode sortSyncMode,
            bool alwaysHideUI,
            bool allowReadAheadProcess,
            Color backColorOfBuiltInViewer,
            Color selectedColorForBackColorOfBuiltInViewer,
            InitialFullscreenMode? builtInViewerInitialFullscreenMode,
            int? maxCaptchaCountInOneVideo,
            ItemLoadLevel archiveLoadLevel,
            ItemLoadLevel imageLoadLevel,
            ItemLoadLevel videoLoadLevel,
            ItemLoadLevel otherLoadLevel,
            string thumbnailCacheRoot,
            SubfolderMode? subfolderMode,
            string[] filterHistory,
            bool invariantFilter,
            KeyAndValue<string, string>[] filterAliasToString,
            ZipTag[] tags
            )
        {
            LastSortMode = lastSortMode;
            SortMode = sortMode;
            RandomSeed = randomSeed;
            Filter = filter;
            ThumbnailSize = thumbnailSize;
            AspectRatio = aspectRatio;
            ShowIcon = showIcon;
            ShowRating = showRating;
            ShowTags = showTags;
            ShowFilename = showFilename;
            ClipMode = clipMode;
            AligningMode = aligningMode;
            NumberOfLinesOfName = numberOfLinesOfName;
            MinimumFrameThickness = minimumFrameThickness;
            ThumbnailMouseWheelScrollAmount = thumbnailMouseWheelScrollAmount;
            ThumbnailMouseWheelScrollUnit = thumbnailMouseWheelScrollUnit;
            ThumbnailListVisible = thumbnailListVisible;
            FileListVisible = fileListVisible;
            //if (fileListVisible != false)
            {
                FileListDock = fileListDock;
                FileListWidth = fileListWidth;
                FileListOrder = fileListOrder;
                IconInFileList = iconInFileList;
                PageInFileList = pageInFileList;
                ratingInFileList = RatingInFileList;
                DateCreatedInFileList = dateCreatedInFileList;
                DateAccessedInFileList = dateAccessedInFileList;
                DateModifiedInFileList = dateModifiedInFileList;
                SizeInFileList = sizeInFileList;
                ResolutionInFileList = resolutionInFileList;
                RatioInFileList = ratioInFileList;
                WidthInFileList = widthInFileList;
                HeightInFileList = heightInFileList;
                LengthInFileList = lengthInFileList;
                FpsInFileList = fpsInFileList;
            }
            TinyOnScreenKeyboardVisible = tinyOnScreenKeyboardVisible;
            SelectionHighlightMode = selectionHighlightMode;
            FilterRemovingMode = filterRemovingMode;
            LoadLastViewedPageFromHistory = loadLastViewedPageFromHistory;
            DoubleClickFileOpenMode = doubleClickFileOpenMode;
            RightClickContextMenu = rightClickContextMenu;
            MiddleClickContextMenu = middleClickContextMenu;
            ControlRightClickContextMenu = controlRightClickContextMenu;
            ItemSelectionFeedback = itemSelectionFeedback;
            SynchroFromTreeToBar = synchroFromTreeToBar;
            SynchroFromBarToTree = synchroFromBarToTree;
            CollapseOthersWhenSynchronizing = collapseOthersWhenSynchronizing;
            FolderSortMode = folderSortMode;
            //CheckDirectorySize = checkDirectorySize;
            ArchivesInArchiveMode = archivesInArchiveMode;
            ReadOnMemoryMode = readOnMemoryMode;
            OpenInPreviousImageFilterSetting = openInPreviousImageFilterSetting;
            DefaultBindingMode = defaultBindingMode;
            BackColorOfBuiltInViewer = backColorOfBuiltInViewer;
            SelectedColorForBackColorOfBuiltInViewer = selectedColorForBackColorOfBuiltInViewer;
            CoverBindingMode = coverBindingMode;
            SortSyncMode = sortSyncMode;
            AlwaysHideUI = alwaysHideUI;
            AllowReadAheadProcess = allowReadAheadProcess;
            BuiltInViewerInitialFullscreenMode = builtInViewerInitialFullscreenMode;
            MaxCaptchaCountInOneVideo = maxCaptchaCountInOneVideo;
            DisplayArchiveFiles = archiveLoadLevel.ToString();
            DisplayImageFiles = imageLoadLevel.ToString();
            DisplayVideoFiles = videoLoadLevel.ToString();
            DisplayOtherFiles = otherLoadLevel.ToString();
            ThumbnailCacheRoot = thumbnailCacheRoot == null ? CatalogFormConfig.NOCACHE : thumbnailCacheRoot;
            SubfolderMode = subfolderMode;
            FilterHistory = filterHistory;
            InvariantFilter = invariantFilter;
            FilterAliasToString = filterAliasToString;
            Tags = tags;
        }
    }
}
