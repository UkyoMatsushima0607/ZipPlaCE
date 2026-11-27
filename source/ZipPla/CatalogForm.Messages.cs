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
        public void SetMessages()
        {
            SetMessagesWithoutTagFilter();

            // メニュの幅に依存して動作が決まるので必ず SetMessagesWithoutTagFilter よりも後で
            setTagFilteringMenuItems(needToRepairTagsFromBookmark: false, needToSaveCurrentTag: false);

            // コンストラクタからは呼ばれないようにここに書く
            if (showStartHelpMessage)
            {
                tvCatalog.Invalidate();
            }
        }

        private Control[] SetMessages_LayoutControls;

        public void SetMessagesWithoutTagFilter()
        {

            startToolStripMenuItem.Text = Message._Start;
            if (SetMessages_LayoutControls == null)
            {
                SetMessages_LayoutControls = new Control[]
                {
                    menuStrip,
                    startToolStripMenuItem.DropDown,
                    viewToolStripMenuItem.DropDown,
                    layoutToolStripMenuItem.DropDown,
                    howToDoubleClickToolStripMenuItem.DropDown,
                    contextMenuSelectToolStripMenuItem.DropDown,
                    arrangeFoldersToolStripMenuItem.DropDown,
                    itemSelectionFeedbackToolStripMenuItem.DropDown,
                    folderTreeSettingsToolStripMenuItem.DropDown,
                    archivesInArchiveToolStripMenuItem.DropDown,
                    defaultPageSequenceToolStripMenuItem.DropDown,
                    backgroundColorOfBuiltinViewerToolStripMenuItem.DropDown,
                    maxCaptchaCountInAVideoToolStripMenuItem.DropDown,
                    thumbnailSizeToolStripMenuItem.DropDown,
                    theNumberOfLinesOfNameToolStripMenuItem.DropDown,
                    minimumFrameThicknessToolStripMenuItem.DropDown,
                    aligningToolStripMenuItem.DropDown,
                    mouseWheelScrollAmountToolStripMenuItem.DropDown,
                    optionToolStripMenuItem.DropDown,
                    addToolStripMenuItem.DropDown,
                    cmsFileListHeader,
                    cmsRightClick,
                    cmsLocationCommands,
                    cmsFilterCommands,
                    cmsRightClickOnBackground,
                    cmsDirectoryList,
                    cmsRightClickDrop,
                    cmsDirectoryListRightClickDrop,
                };
            }

            foreach (var control in SetMessages_LayoutControls) control.SuspendLayout();

            openHistoryToolStripMenuItem.Text = Message.OpenHistory;
            //openListFileToolStripMenuItem.Text = Message.OpenVirtualFolder + "...";
            openDirectoryToolStripMenuItem.Text = Message.OpenFolder + "...";
            openFileToolStripMenuItem.Text = Message.OpenFile + "...";
            openbuiltinBookReaderToolStripMenuItem.Text = Message.OpenBuiltInViewer;
            newVirtualFolderToolStripMenuItem.Text = Message.NewVirtualFolder + "...";
            newSmartFolderToolStripMenuItem.Text = Message.NewSmartFolder + "...";
            reloadAllItemsToolStripMenuItem.Text = Message.Reload;
            newFolderToolStripMenuItem.Text = Message.NewFolder;
            editLocationToolStripMenuItem.Text = Message.EditAddressBar;
            editFilterToolStripMenuItem.Text = Message.EditFilter;
            exitToolStripMenuItem.Text = Message.Exit;
            viewToolStripMenuItem.Text = Message._Behavior;// Message._View;
            bookmarkToolStripMenuItem.Text = Message.Bookmark;
            folderTreeToolStripMenuItem.Text = Message.FolderTree;
            thumbnailListToolStripMenuItem.Text = Message.Thumbnails;
            fileListToolStripMenuItem.Text = Message.Details;
            layoutToolStripMenuItem.Text = Message.Layout;
            verticalToolStripMenuItem.Text = Message.VerticalLayout;
            alternativeVerticalToolStripMenuItem.Text = Message.AlternativeVerticalLayout;
            horizontalToolStripMenuItem.Text = Message.HorizontalLayout;
            alternativeHorizontalToolStripMenuItem.Text = Message.AlternativeHorizontalLayout;
            tinyOnscreenKeyboardToolStripMenuItem.Text = Message.TinyOnScreenKeyboard;
            hilightSelectedItemsWhenMouseLeavaesToolStripMenuItem.Text = Message.HighlightSelectionWhenMouseLeaves;
            loadLastViewedPageFromHistoryToolStripMenuItem.Text = Message.LoadLastPageViewed;
            clearFilterWhenMovingLocationToolStripMenuItem.Text = Message.ClearFilterWhenMovingLocation;
            howToDoubleClickToolStripMenuItem.Text = Message.HowToOpenFile;
            standardToolStripMenuItem.Text = Message.ClickSamePointTwiceQuickly;
            ignoreMouseMovementToolStripMenuItem.Text = Message.ClickSameItemTwiceQuickly;
            selectAndClickItemToolStripMenuItem.Text = Message.ClickSelectedItem;

            contextMenuSelectToolStripMenuItem.Text = Message.ContextMenu;
            contextMenuStandardToolStripMenuItem.Text = Message.Standard;
            contextMenuOnlyTagsToolStripMenuItem.Text = Message.OnlyTags;
            contextMenuExplorerToolStripMenuItem.Text = Message.Explorer;

            standardRightclickToolStripMenuItem.Text = Message.RightClickPressAndTap;// Message.RightClick;
            standardMiddleclickToolStripMenuItem.Text = Message.MiddleClickTwoFingerTap;
            standardCtrlRightclickToolStripMenuItem.Text = "Ctrl+" + Message.RightClick;
            OnlyTagRightclickToolStripMenuItem.Text = Message.RightClickPressAndTap;// Message.RightClick;
            OnlyTagMiddleclickToolStripMenuItem.Text = Message.MiddleClickTwoFingerTap;
            onlyTagCtrlRightclickToolStripMenuItem.Text = "Ctrl+" + Message.RightClick;
            explorerRightclickToolStripMenuItem.Text = Message.RightClickPressAndTap;// Message.RightClick;
            explorerMiddleclickToolStripMenuItem.Text = Message.MiddleClickTwoFingerTap;
            explorerCtrlRightclickToolStripMenuItem.Text = "Ctrl+" + Message.RightClick;

            arrangeFoldersToolStripMenuItem.Text = Message.FolderViewSettings;
            noneFolderArrangeToolStripMenuItem.Text = Message.DisplayMixingWithFiles;
            displayFirstToolStripMenuItem.Text = Message.DisplayFirst;
            displayLastToolStripMenuItem.Text = Message.DisplayLast;
            fitToDirectionOfSortToolStripMenuItem.Text = Message.DisplayRespectingToSortOfDirection;
            //getFolderSizeToolStripMenuItem.Text = Message.ShowFolderSizeOnStatusBar;


            itemSelectionFeedbackToolStripMenuItem.Text = Message.AutomaticSelectionSwitching;// Message.ItemSelectionFeedback;
            noneItemSelectionFeedbackToolStripMenuItem.Text = Message.None;
            inAppropriateCasesToolStripMenuItem.Text = Message.InAppropriateCases;
            alwaysItemSelectionFeedbackToolStripMenuItem.Text = Message.Always;

            folderTreeSettingsToolStripMenuItem.Text = Message.FolderTreeSynchronizing;
            noneFolderTreeSynchronizingToolStripMenuItem.Text = Message.None;
            folderTreeToAddressbarToolStripMenuItem.Text = Message.FolderTreeToAddresBar;
            addressBarToFolderTreeToolStripMenuItem.Text = Message.AddresBarToFolderTree;
            twowayToolStripMenuItem.Text = Message.TwoWay;
            collapseOthersWhenSynchronizingToolStripMenuItem.Text = Message.CollapseOthersWhenSynchronizing;

            backgroundColorOfBuiltinViewerToolStripMenuItem.Text = Message.BuiltInViewerSettings;
            forceFirstPageToBeSingleToolStripMenuItem.Text = Message.ForceFirstPageToBeSingle;
            startInPreviousImageFilterSettingToolStripMenuItem.Text = Message.OpenInPreviousRotationToneCurveSetting;
            alwaysAutomaticallyHideUIToolStripMenuItem.Text = Message.AlwaysAutomaticallyHideUI;
            sortFilesWithTheSameOrderAsThumbnailWindowToolStripMenuItem.Text = Message.SortFilesWithSameOrderAsThumbnailWindow;
            allowReadaheadProcessToolStripMenuItem.Text = Message.AllowReadAheadProcess;
            loadWholeArchiveIntoMemoryToolStripMenuItem.Text = Message.LoadWholeArchiveIntoMemory;
            exceptReadAheadProcessToolStripMenuItem.Text = "    " + Message.ExceptReadAheadProcess;
            blackToolStripMenuItem.Text = Message.BlackBackground;
            grayToolStripMenuItem.Text = Message.GrayBackground;
            whiteToolStripMenuItem.Text = Message.WhiteBackground;
            selectColorToolStripMenuItem.Text = Message.SelectBackgroundColor + "...";
            createViewerShortcutFromCurrentSettingsToolStripMenuItem.Text = Message.CreateShortcutFromCurrentSettings + "...";
            maxCaptchaCountInAVideoToolStripMenuItem.Text = Message.MaxCountCaptchaInAVideo;
            fitOnTheScreenToolStripMenuItem.Text = Message.FitOnScreen;
            everySecondsToolStripMenuItem.Text = Message.EverySecond;
            thumbnailSizeToolStripMenuItem.Text = Message._Thumbnail;
            archivesInArchiveToolStripMenuItem.Text = Message.LoadArchivesInArchive;
            ignoreArchivesInArchiveToolStripMenuItem.Text = Message.NoneRecommended;
            //onlyIfThereIsNoOtherImageToolStripMenuItem.Text = Message.OnlyIfThereIsNoOtherImage;
            untilFoundToolStripMenuItem.Text = Message.TwoLevelUntilFound;
            alwaysLoadArchivesInArchiveToolStripMenuItem.Text = Message.OneLevelCompletelyNotRecommended;// Message.Always;
            defaultPageSequenceToolStripMenuItem.Text = Message.DefaultPageSequence;
            defaultLeftToRightToolStripMenuItem.Text = Message.LeftToRight;
            defaultRightToLeftToolStripMenuItem1.Text = Message.RightToLeft;
            defaultSinglePageToolStripMenuItem.Text = Message.SinglePage;
            singlePageWithoutScalingUpToolStripMenuItem.Text = Message.SinglePageWithoutScalingUp;
            startInPreviousFullscreenModeToolStripMenuItem.Text = Message.OpenInPreviousFullscreenMode;
            alwaysStartInWindowModeToolStripMenuItem.Text = Message.OpenInWindow;
            openInFullscreenToolStripMenuItem.Text = Message.OpenInFullscreen;
            archivesFilesPDFFielsToolStripMenuItem.Text = Message.ArchiveFilesPdfFiles;
            imageFilesToolStripMenuItem.Text = Message.ImageFiles;
            videoFilesToolStripMenuItem.Text = Message.VideoFiles;
            otherFilesToolStripMenuItem.Text = Message.OtherFiles;
            foldersToolStripMenuItem.Text = Message.Folders;
            //ignoreFoldersToolStripMenuItem.Text = Message.IgnoreFolders;
            searchSubfoldersAsWellToolStripMenuItem.Text = Message.SearchSubfoldersAsWell;
            //reloadAllThumbnailsToolStripMenuItem.Text = Message.ReloadAllThumbnails;
            tinyToolStripMenuItem.Text = Message.Tiny;
            smallToolStripMenuItem.Text = Message.Small;
            normalToolStripMenuItem.Text = Message.Normal;
            largeToolStripMenuItem.Text = Message.Large;
            hugeToolStripMenuItem.Text = Message.Huge;
            allRatiosToolStripMenuItem.Text = Message.AllRatios;
            showIconToolStripMenuItem.Text = Message.ShowIcon;
            showRatingToolStripMenuItem.Text = Message.ShowRating;
            showTagsToolStripMenuItem.Text = Message.ShowTags;
            showFilenameToolStripMenuItem.Text = Message.ShowName;
            theNumberOfLinesOfNameToolStripMenuItem.Text = Message.NumberOfLinesOfName;
            fromTheLongestNameToolStripMenuItem.Text = Message.Full;
            automaticlessToolStripMenuItem.Text = Message.AutomaticLess;
            automaticgreaterToolStripMenuItem.Text = Message.AutomaticGreater;
            NumOfLineOfName1toolStripMenuItem.Text = Message.OneLine;
            NumOfLineOfName2toolStripMenuItem.Text = Message._1Lines.Replace("$1", "2");
            NumOfLineOfName3toolStripMenuItem.Text = Message._1Lines.Replace("$1", "3");
            minimumFrameThicknessToolStripMenuItem.Text = Message.MinimumFrameThickness;
            veryThinToolStripMenuItem.Text = Message.VeryThin;
            thinToolStripMenuItem.Text = Message.Thin;
            normalThicknessToolStripMenuItem.Text = Message.NormalThickness;
            thickToolStripMenuItem.Text = Message.Thick;
            veryThickToolStripMenuItem.Text = Message.VeryThick;
            aligningToolStripMenuItem.Text = Message.Aligning;
            totallyUniformedToolStripMenuItem.Text = Message.TotallyUniformed;
            horizontallyUniformedToolStripMenuItem.Text = Message.HorizontallyUniformed;
            alignedLeftToolStripMenuItem.Text = Message.AlignedLeft;
            alignedOnCenterToolStripMenuItem.Text = Message.AlignedOnCenter;
            mouseWheelScrollAmountToolStripMenuItem.Text = Message.MouseWheelScrollAmount;
            tinyScrollToolStripMenuItem.Text = Message.Tiny;
            smallScrollToolStripMenuItem.Text = Message.Small;
            normalScrollToolStripMenuItem.Text = Message.Normal;
            largeScrollToolStripMenuItem.Text = Message.Large;
            hugeScrollToolStripMenuItem.Text = Message.Huge;
            _1lineScrollToolStripMenuItem.Text = Message.OneLine;
            _2lineScrollToolStripMenuItem.Text = Message._1Lines.Replace("$1", "2");
            _3lineScrollToolStripMenuItem.Text = Message._1Lines.Replace("$1", "3");
            thumbnailCacheToolStripMenuItem.Text = Message.ThumbnailCache + "...";
            //createCacheInADSInNTFSToolStripMenuItem.Text = Message.CreateThumbnailCache;
            //aboutThumbnailCacheToolStripMenuItem.Text = Message.AboutThumbnailCache + "...";
            letterboxToolStripMenuItem.Text = Message.Letterbox;
            panAndScanToolStripMenuItem.Text = Message.PanAndScan;
            plaClipToolStripMenuItem.Text = Message.SmartClip;
            cutToolStripMenuItem.Text = Message.Cut;
            //copyToolStripMenuItem.Text = Message.Copy;
            pasteToolStripMenuItem.Text = Message.Paste;
            renameToolStripMenuItem.Text = Message.Rename;
            //deleteonlyForListFileToolStripMenuItem.Text = Message.DeleteOnlyForVirtualFolder;
            //deleteonlyForListFileToolStripMenuItem.Text = Message.Delete;
            optionToolStripMenuItem.Text = Message._Others;
            tagEditorToolStripMenuItem.Text = Message.Edit_Tags + "...";
            mouseGestureSettingToolStripMenuItem.Text = Message._MouseTouchGestures + "...";
            changeCurrentProfileColorToolStripMenuItem.Text = Message._ChangeCurrentProfileColor + "...";
            preferenceToolStripMenuItem.Text = Message.Common_Settings + "...";

            moveHereToolStripMenuItem.Text = Message.MoveHere;
            copyHereToolStripMenuItem.Text = Message.CopyHere;

            pasteOnBackGroundToolStripMenuItem.Text = Message.Paste;

            toolTip.SetToolTip(btnLocationCommands, $"{Message.ClearMenuForAddressBarHistory}");
            toolTip.SetToolTip(btnFilterCommands, $"{Message.EditMenuForFilter}");

            // MessageFoworder と競合、またツールチップが表示領域を隠すので自前で実装する
            //toolTip.SetToolTip(cbFilter, $"{Message.MiddleClickToClearPaste}");

            toolTip.SetToolTip(btnGoToBack, $"{Message.GoBack} (Alt+←, {Message.X1Button})\n{Message.RightClickDownFlickToShowHistory}");
            toolTip.SetToolTip(btnGoToForward, $"{Message.GoForward} (Alt+→, {Message.X2Button})\n{Message.RightClickDownFlickToShowHistory}");


            deleteCurrentPathFromHistoryToolStripMenuItem.Text = Message.DeleteCurrentPathFromHistory;
            clearAllHistoryOfLocationsToolStripMenuItem.Text = Message.ClearAllHistoryOfLocations;

            iconToolStripMenuItem.Text = Message.Icon;
            fileNameToolStripMenuItem.Text = Message.Name;
            ratingHeaderRightClickToolStripMenuItem.Text = Message.Rating;
            pageToolStripMenuItem.Text = Message.Page;
            dateCreatedToolStripMenuItem.Text = Message.DateCreated;
            accessTimeToolStripMenuItem.Text = Message.DateAccessed;
            writeTimeToolStripMenuItem.Text = Message.DateModified;
            sizeToolStripMenuItem.Text = Message.Size;
            resolutionToolStripMenuItem.Text = Message.Resolution;
            ratioToolStripMenuItem.Text = Message.AspectRatio;
            widthToolStripMenuItem.Text = Message.Width;
            heightToolStripMenuItem.Text = Message.Height;
            lengthToolStripMenuItem.Text = Message.Length;
            fPSToolStripMenuItem.Text = Message.FPS;

            deleteCurrentFilterFromHistoryToolStripMenuItem.Text = Message.DeleteCurrentFilterFromDropdown;
            deleteCurrentFilterFromHistoryToolStripMenuItem.ShortcutKeyDisplayString = Message.MiddleClick;
            clearAllHistoryOfFiltersToolStripMenuItem.Text = Message.ClearAllDropdownItems;
            editDropdownOfFiltersToolStripMenuItem.Text = Message.EditDropdownOfFilters + "...";

            addToolStripMenuItem.Text = Message._Add;
            addToolStripMenuItem.ToolTipText = Message.RightClickDownFlickToOpenContextMenu;
            currentLocationToolStripMenuItem.Text = Message.BookmarkWithCurrentProfileColor;
            currentLocationToolStripMenuItem.ShortcutKeyDisplayString = Message.LeftClick;
            currentLocationAndFilterToolStripMenuItem.Text = Message.BookmarkWithNewProfileColor;
            AddOnlyTheCurrentProfileColorToolStripMenuItem.Text = Message.OnlyCurrentProfileColor;
            addOnlyANewProfileColorToolStripMenuItem.Text = Message.OnlyNewProfileColor;
            separatorToolStripMenuItem.Text = Message.Separator;
            aboutAccessKeyToolStripMenuItem.Text = Message.AboutAccessKey + "...";

            addToBookmarkWithCurrentProfileColorRightClickDropToolStripMenuItem.Text = Message.AddWithCurrentProfileColor;
            addToBookmarkWithNewProfileColorRightClickDropToolStripMenuItem.Text = Message.AddWithNewProfileColor;

            deleteToolStripMenuItem.Text = Message.Delete;
            //aliasToolStripMenuItem.Text = Message.EditAlias;
            realNameToolStripMenuItem.Text = Message.RealName;
            addSeparatorToolStripMenuItem.Text = Message.AddSeparator;
            //setCurrentLocationToolStripMenuItem.Text = Message.SetToCurrentLocation;
            //setCurrentLocationAndStateToolStripMenuItem.Text = Message.SetToCurrentLocationAndState;
            openInExplorerInDirectoryListToolStripMenuItem.Text = Message.OpenInExplorer;
            editSmartFolderToolStripMenuItem.Text = Message.EditSmartFolder;
            //loadOnlyProfileColorToolStripMenuItem.Text = Message.LoadOnlyProfileColor;

            tbcFileName.HeaderText = Message.Name;
            tbcFileName.MinimumWidth = 2;
            tbcFileName.Width = 0;
            //var temp = tbcFileName.HeaderCell.SortGlyphDirection;
            //if (temp == SortOrder.None) tbcFileName.HeaderCell.SortGlyphDirection = SortOrder.Ascending;
            tbcFileName.AutoSizeMode = DataGridViewAutoSizeColumnMode.ColumnHeader;
            tbcFileName.MinimumWidth = tbcFileName.Width;
            //if (temp == SortOrder.None) tbcFileName.HeaderCell.SortGlyphDirection = temp;
            tbcFileName.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;


            tbcPage.HeaderText = Message.Page;
            tbcRating.HeaderText = Message.Rating;
            tbcCreateTime.HeaderText = Message.DateCreated;
            tbcAccessTime.HeaderText = Message.DateAccessed;
            tbcWriteTime.HeaderText = Message.DateModified;
            tbcSize.HeaderText = Message.Size;
            tbcResolution.HeaderText = Message.Resolution;
            tbcRatio.HeaderText = Message.AspectRatio;
            tbcWidth.HeaderText = Message.Width;
            tbcHeight.HeaderText = Message.Height;
            tbcLength.HeaderText = Message.Length;
            tbcFPS.HeaderText = Message.FPS;

            tagToolStripMenuItem.Text = Message.Tag;
            listFileToolStripMenuItem.Text = Message.VirtualFolder;
            addToNewListToolStripMenuItem.Text = Message.AddToNewVirtualFolder + "...";
            removeFromHistoryToolStripMenuItem.Text = Message.RemoveFromHistory;
            ratingToolStripMenuItem.Text = Message.Rating;
            noneToolStripMenuItem.Text = Message.None;
            //noneToolStripMenuItem.ShortcutKeyDisplayString = Message.MiddleClickOnStars;
            thumbnailToolStripMenuItem.Text = Message.Cover;
            setThumbnailManuallyToolStripMenuItem.Text = Message.SetCoverManually + "...";
            setCoverToThisToolStripMenuItem.Text = Message.SetCoverToThis;
            //setCoverOfParentFolderToThisToolStripMenuItem.Text = Message.SetCoverOfParentFolderToThis;
            clearThumbnailSettingToolStripMenuItem.Text = Message.ClearCoverSetting;
            pageSequenceToolStripMenuItem.Text = Message.PageSequence;
            defaultToolStripMenuItem.Text = Message.Default;
            leftToRightToolStripMenuItem.Text = Message.LeftToRight;
            rightToLeftToolStripMenuItem.Text = Message.RightToLeft;
            singlePageToolStripMenuItem.Text = Message.SinglePage;
            propertiesToolStripMenuItem.Text = Message.Properties + "...";

            addToNewListSaveFileDialog.Title = Message.AddToNewVirtualFolder;
            addToNewListSaveFileDialog.Filter = $"{Message.VirtualFolder}|*.sor";
            //openListFileDialog.Title = Message.OpenVirtualFolder;
            //openListFileDialog.Filter = $"{Message.VirtualFolder}|*.sor";


            ratingFilterToolStripMenuItem.ToolTipText = Message.RightClickDownFlickToOpenContextMenu;
            increaseReferenceValueToolStripMenuItem.Text = Message.IncreaseReferenceValue;
            decreaseReferenceValueToolStripMenuItem.Text = Message.DecreaseReferenceValue;
            //increaseReferenceValueToolStripMenuItem.ShortcutKeyDisplayString = Message.WheelUp;
            //decreaseReferenceValueToolStripMenuItem.ShortcutKeyDisplayString = Message.WheelDown;


            if (simplifiedKeyBoard != null)
            {
                foreach (var key in simplifiedKeyBoard.Keys)
                {
                    switch (key.KeyCode)
                    {
                        case Keys.None: key.Text = Message.Tag; break;
                    }
                }
            }

            Text = string.IsNullOrEmpty(currentLocation) ? Program.Name : $"{SimpleBookmark.GetDisplayName(currentLocation)} - {Program.Name}";

            ShowStatusBar();

            dgvDirectoryList.Refresh();

            var culture = Message.CurrentLanguage;
            lock (ZipPathArrayLocker)
            {
                if (ZipPathArray != null)
                {
                    // 行の情報は表示するときに今の言語で作り直す
                    clearFileListRowInfos();
                    dgvFileList.Invalidate();
                }
            }

            if ((InMovie || InArchive) && LocationZipPlaInfo != null && LocationZipPlaInfo.ThumbnailInfo != null)
            {
                tvCatalog.Refresh();
            }

            // ファイルリストのヘッダの設定よりも後で
            SetFileListHeaderWidth(culture, Message.Bytes);

            SetStatusBarWidth();

            // 先にすると変更がその時点で描画されて見栄えが良くない
            cbSortBy_SelectedValueChanged_Stop = true;
            cbSortBy.Items[(int)SortMode.TypeInAsc] = Message.TypeInAsc;
            cbSortBy.Items[(int)SortMode.TypeInDesc] = Message.TypeInDesc;
            cbSortBy.Items[(int)SortMode.NameInAsc] = Message.NameInAsc;
            cbSortBy.Items[(int)SortMode.NameInDesc] = Message.NameInDesc;
            cbSortBy.Items[(int)SortMode.RatingInAsc] = Message.RatingInAsc;
            cbSortBy.Items[(int)SortMode.RatingInDesc] = Message.RatingInDesc;
            cbSortBy.Items[(int)SortMode.CreatedInAsc] = Message.CreatedInAsc;
            cbSortBy.Items[(int)SortMode.CreatedInDesc] = Message.CreatedInDesc;
            cbSortBy.Items[(int)SortMode.AccessedInAsc] = Message.AccessedInAsc;
            cbSortBy.Items[(int)SortMode.AccessedInDesc] = Message.AccessedInDesc;
            cbSortBy.Items[(int)SortMode.ModifiedInAsc] = Message.ModifiedInAsc;
            cbSortBy.Items[(int)SortMode.ModifiedInDesc] = Message.ModifiedInDesc;
            cbSortBy.Items[(int)SortMode.SizeInAsc] = Message.SizeInAsc;
            cbSortBy.Items[(int)SortMode.SizeInDesc] = Message.SizeInDesc;
            cbSortBy.Items[(int)SortMode.Random] = Message.Random;
            cbSortBy_SelectedValueChanged_Stop = false;


            for (var i = SetMessages_LayoutControls.Length - 1; i >= 0; i--)
            {
                var control = SetMessages_LayoutControls[i];
                control.ResumeLayout(performLayout: false);
                control.PerformLayout();
            }

        }

        private void SetStatusBarWidth()
        {
            var font = fileSizeToolStripStatusLabel.Font; // Dispose してはならない
            Func<string, int> measure = text => TextRenderer.MeasureText(text, font).Width; // font が null でも動作するが戻り値が決まった値に成らないので注意
            var digitWithMaximumSize = GetDigitWithMaximumSize(measure);

            itemsCountToolStripStatusLabel.Width = GetMaxWidthWithTemplates(measure, digitWithMaximumSize, Message._1Items.Replace("$1", "000000"));

            //var fileSizeMaxWidth = Enumerable.Range(0, 5) // 999 Bytes, 999.0 KB, 999.0 MB, 999.0 GB, 999.0 TB まで
            //    .Select(i => GetMaxWidthWithTemplates(measure, digitWithMaximumSize, Program.GetFormatSizeString(999L << (10*i), Message.Bytes, simpleForm: true) + "+")).Max();
            var fileSizeMaxWidth = fileSizeToolStripStatusLabel.Width = GetMaxWidthWithTemplates(measure, digitWithMaximumSize,
                Program.GetFormatSizeStringTemplates(Message.Bytes).Select(s => s + "+"));

            var movieTemplate = MovieInfo.ToShortStringTemplate;
            var pageTemplage = Program.GetNPageString(99999);
            var sizeTemplate = "00000x00000";

            selectedFileContainsCountToolStripStatusLabel.Width = Math.Max(Math.Max(
                GetMaxWidthWithTemplates(measure, digitWithMaximumSize, movieTemplate),
                GetMaxWidthWithTemplates(measure, digitWithMaximumSize, pageTemplage)),
                GetMaxWidthWithTemplates(measure, digitWithMaximumSize, sizeTemplate));
        }

        private void SetFileListHeaderWidth(CultureInfo culture, string bytesInString)
        {
            // ページ・FPSはヘッダのみから設定されるように AutoSize が設定されている

            var font = dgvFileList.DefaultCellStyle.Font; // Dispose してはならない
            Func<string, int> measure = text => TextRenderer.MeasureText(text, font).Width; // font が null でも動作するが戻り値が決まった値に成らないので注意
            var digitWithMaximumSize = GetDigitWithMaximumSize(measure);

            var dateTimeWidth = GetMaxWidthOfDateTime(measure, digitWithMaximumSize, culture);
            setColumnWidth(tbcCreateTime, dateTimeWidth, measure);
            setColumnWidth(tbcAccessTime, dateTimeWidth, measure);
            setColumnWidth(tbcWriteTime, dateTimeWidth, measure);

            var sizeWidth = GetMaxWidthWithTemplates(measure, digitWithMaximumSize,
                Program.GetFormatSizeStringTemplates(bytesInString));
            setColumnWidth(tbcSize, sizeWidth, measure);

            var rosolusionSize = GetMaxWidthWithTemplates(measure, digitWithMaximumSize, "11111x11111");
            setColumnWidth(tbcResolution, rosolusionSize, measure);

            var ratioSize = GetMaxWidthWithTemplates(measure, digitWithMaximumSize, getRatioString_MaxWidthTemplate);
            setColumnWidth(tbcRatio, ratioSize, measure);

            var fiveDigitSize = GetMaxWidthWithTemplates(measure, digitWithMaximumSize, "11111");
            setColumnWidth(tbcWidth, fiveDigitSize, measure);
            setColumnWidth(tbcHeight, fiveDigitSize, measure);

            // 10 時間超えを考慮しない選択肢も考えられるが、ヘッダが短い日本語でも 10 時間超えを想定してもなおヘッダによって幅が決まるので重要でない
            var lengthSize = GetMaxWidthWithTemplates(measure, digitWithMaximumSize, new TimeSpan(11, 11, 11).ToString(setFileListRoDynamicPart_LengthFormat));
            setColumnWidth(tbcLength, lengthSize, measure);

            SetFileListPageHeaderWidth_Fraction = getPageIsFraction();
            SetFileListPageHeaderWidth(measure, digitWithMaximumSize);

            var fpsSize = GetMaxWidthWithTemplates(measure, digitWithMaximumSize, "29.97");
            setColumnWidth(tbcFPS, fpsSize, measure);
        }

        private void SetFileListPageHeaderWidth()
        {
            var pageFraction = getPageIsFraction();
            if (pageFraction != SetFileListPageHeaderWidth_Fraction)
            {
                var font = dgvFileList.DefaultCellStyle.Font; // Dispose してはならない
                Func<string, int> measure = text => TextRenderer.MeasureText(text, font).Width; // font が null でも動作するが戻り値が決まった値に成らないので注意
                var digitWithMaximumSize = GetDigitWithMaximumSize(measure);

                SetFileListPageHeaderWidth_Fraction = getPageIsFraction();
                SetFileListPageHeaderWidth(measure, digitWithMaximumSize);
            }
        }

        bool SetFileListPageHeaderWidth_Fraction = false;

        private void SetFileListPageHeaderWidth(Func<string, int> measure, string digitWithMaximumSize)
        {
            var pageSize = GetMaxWidthWithTemplates(measure, digitWithMaximumSize,
                SetFileListPageHeaderWidth_Fraction ? getPageString(11110, 11111) : getPageString(-1, 11111));
            setColumnWidth(tbcPage, pageSize, measure);
        }

        private bool getPageIsFraction()
        {
            return LoadLastViewedPageFromHistory || (InVirtualDirectory && currentLocation == Program.HistorySorPath);
            // InVirtualDirectory が無いと、Program.HistorySorPath が無くて同名のフォルダが存在してそれを開いている時に期待通りの結果にならない
        }

        private void setColumnWidth(DataGridViewColumn column, int cellMaximumWidth, Func<string, int> measure)
        {

            //column.Width = Math.Max(cellMaximumWidth, headerWidth);

            var dgv = column.DataGridView;
            dgv.AutoResizeColumn(column.Index, DataGridViewAutoSizeColumnMode.ColumnHeader);
            var plus = tbcIconWidth - 22;
            cellMaximumWidth = Math.Max(column.Width + plus, cellMaximumWidth);
            //cellMaximumWidth = 200;
            if (column.Width < cellMaximumWidth) column.Width = cellMaximumWidth;
        }

        public static T GetMaxWidthWithTemplates<T>(Func<string, T> measure,
             string digitWithMaximumSize, params string[] templates) where T : IComparable<T>
        {
            return GetMaxWidthWithTemplates(measure, digitWithMaximumSize, templates as IEnumerable<string>);
        }

        private static T GetMaxWidthWithTemplates<T>(Func<string, T> measure,
             string digitWithMaximumSize, IEnumerable<string> templates) where T : IComparable<T>
        {
            return templates.Max(template => measure(Regex.Replace(template, @"\d", digitWithMaximumSize)));
        }

        private T GetMaxWidthOfDateTime<T>(Func<string, T> measure, string digitWithMaximumSize, CultureInfo culture) where T : IComparable<T>
        {
            // 午前／午後、月、曜日それぞれの最大幅を取得してそれらを繋げた日付の幅を返す
            T maxSize, size;

            const int testYear = 1999;

            var month = new DateTime(testYear, 1, 11, 11, 11, 11);
            var monthWithMaximumSize = month;
            maxSize = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize));
            month = month.AddDays(31); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; monthWithMaximumSize = month; }
            month = month.AddDays(29); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; monthWithMaximumSize = month; } // うるう年以外はずれるが問題ない
            month = month.AddDays(31); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; monthWithMaximumSize = month; }
            month = month.AddDays(30); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; monthWithMaximumSize = month; }
            month = month.AddDays(31); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; monthWithMaximumSize = month; }
            month = month.AddDays(30); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; monthWithMaximumSize = month; }
            month = month.AddDays(31); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; monthWithMaximumSize = month; }
            month = month.AddDays(31); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; monthWithMaximumSize = month; }
            month = month.AddDays(30); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; monthWithMaximumSize = month; }
            month = month.AddDays(31); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; monthWithMaximumSize = month; }
            month = month.AddDays(30); size = measure(Regex.Replace(getShortTimeString(month, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { /*maxSize = size;*/ monthWithMaximumSize = month; }

            var ampm = new DateTime(testYear, 1, 11, 11, 11, 11);
            var ampmWithMaximumSize = ampm;
            maxSize = measure(Regex.Replace(getShortTimeString(ampm, culture), @"\d", digitWithMaximumSize));
            ampm = ampm.AddHours(12); size = measure(Regex.Replace(getShortTimeString(ampm, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { /*maxSize = size;*/ ampmWithMaximumSize = ampm; }

            var day = new DateTime(testYear, 1, 11, 11, 11, 11);
            var dayWithMaximumSize = day;
            maxSize = measure(Regex.Replace(getShortTimeString(day, culture), @"\d", digitWithMaximumSize));
            day = day.AddDays(1); size = measure(Regex.Replace(getShortTimeString(day, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; dayWithMaximumSize = day; }
            day = day.AddDays(1); size = measure(Regex.Replace(getShortTimeString(day, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; dayWithMaximumSize = day; }
            day = day.AddDays(1); size = measure(Regex.Replace(getShortTimeString(day, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; dayWithMaximumSize = day; }
            day = day.AddDays(1); size = measure(Regex.Replace(getShortTimeString(day, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; dayWithMaximumSize = day; }
            day = day.AddDays(1); size = measure(Regex.Replace(getShortTimeString(day, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; dayWithMaximumSize = day; }
            day = day.AddDays(1); size = measure(Regex.Replace(getShortTimeString(day, culture), @"\d", digitWithMaximumSize)); if (size.CompareTo(maxSize) > 0) { maxSize = size; dayWithMaximumSize = day; }

            var dateWithMaximumSize = new DateTime(testYear, monthWithMaximumSize.Month, 11, ampmWithMaximumSize.Hour, 11, 11);
            dateWithMaximumSize.AddDays((dayWithMaximumSize.DayOfWeek - dateWithMaximumSize.DayOfWeek + 7) % 7);

            return measure(Regex.Replace(getShortTimeString(dateWithMaximumSize, culture), @"\d", digitWithMaximumSize));
        }

        public static string GetDigitWithMaximumSize<T>(Func<string, T> measure) where T : IComparable<T>
        {
            var max = measure("0");
            var maxDigit = "0";
            T size;
            size = measure("1"); if (size.CompareTo(max) > 0) { max = size; maxDigit = "1"; }
            size = measure("2"); if (size.CompareTo(max) > 0) { max = size; maxDigit = "2"; }
            size = measure("3"); if (size.CompareTo(max) > 0) { max = size; maxDigit = "3"; }
            size = measure("4"); if (size.CompareTo(max) > 0) { max = size; maxDigit = "4"; }
            size = measure("5"); if (size.CompareTo(max) > 0) { max = size; maxDigit = "5"; }
            size = measure("6"); if (size.CompareTo(max) > 0) { max = size; maxDigit = "6"; }
            size = measure("7"); if (size.CompareTo(max) > 0) { max = size; maxDigit = "7"; }
            size = measure("8"); if (size.CompareTo(max) > 0) { max = size; maxDigit = "8"; }
            size = measure("9"); if (size.CompareTo(max) > 0) { /*max = size;*/ maxDigit = "9"; }
            return maxDigit;
        }

        private bool showIcon = true;

        private bool showRating = true;

        private bool showTags = true;

        private SubfolderMode subfolderMode = SubfolderMode.Display;

        private SubfolderMode SubfolderMode
        {
            get
            {
                return subfolderMode;
            }
            set
            {
                SetSubfolderMode(value, checkAllLoadLevel: false);
            }
        }

        bool SetSubfolderMode(SubfolderMode value, bool checkAllLoadLevel)
        {
            if (subfolderMode != value)
            {
                if (checkAllLoadLevel && IsDisplay(subfolderMode) && !IsDisplay(value))
                {
                    if (archiveLoadLevel == ItemLoadLevel.Ignore && imageLoadLevel == ItemLoadLevel.Ignore && videoLoadLevel == ItemLoadLevel.Ignore && otherLoadLevel == ItemLoadLevel.Ignore)
                    {
                        // Search 如何が変化する場合そこだけ変更
                        // Search 如何が変化しない場合 Display と DisplayHeader をトグル
                        subfolderMode =
                            subfolderMode == SubfolderMode.Display ?
                                value == SubfolderMode.Ignore ?
                                    SubfolderMode.DisplayHeader :
                                    SubfolderMode.DisplayAndSearch :
                            subfolderMode == SubfolderMode.DisplayHeader ?
                                value == SubfolderMode.Ignore ?
                                    SubfolderMode.Display :
                                    SubfolderMode.DisplayHeaderAndSearch :
                            subfolderMode == SubfolderMode.DisplayAndSearch ?
                                value == SubfolderMode.Ignore ?
                                    SubfolderMode.Display :
                                    SubfolderMode.DisplayHeaderAndSearch :
                                value == SubfolderMode.Ignore ?
                                    SubfolderMode.DisplayHeader :
                                    SubfolderMode.DisplayAndSearch;
                        
                    }
                    else
                    {
                        subfolderMode = value;
                    }
                }
                else
                {
                    subfolderMode = value;
                }
                switch (subfolderMode)
                {
                    case SubfolderMode.Display:
                        foldersToolStripMenuItem.CheckState = CheckState.Checked;
                        searchSubfoldersAsWellToolStripMenuItem.Checked = false;
                        break;
                    case SubfolderMode.DisplayHeader:
                        foldersToolStripMenuItem.CheckState = CheckState.Indeterminate;
                        searchSubfoldersAsWellToolStripMenuItem.Checked = false;
                        break;
                    case SubfolderMode.Ignore:
                        foldersToolStripMenuItem.CheckState = CheckState.Unchecked;
                        searchSubfoldersAsWellToolStripMenuItem.Checked = false;
                        break;
                    case SubfolderMode.Search:
                        foldersToolStripMenuItem.CheckState = CheckState.Unchecked;
                        searchSubfoldersAsWellToolStripMenuItem.Checked = true;
                        break;
                    case SubfolderMode.DisplayAndSearch:
                        foldersToolStripMenuItem.CheckState = CheckState.Checked;
                        searchSubfoldersAsWellToolStripMenuItem.Checked = true;
                        break;
                    case SubfolderMode.DisplayHeaderAndSearch:
                        foldersToolStripMenuItem.CheckState = CheckState.Indeterminate;
                        searchSubfoldersAsWellToolStripMenuItem.Checked = true;
                        break;
                }
                return true;
            }
            return false;
        }

        private ClipMode clipModeByControl
        {
            get
            {
                if (letterboxToolStripMenuItem.Checked) return ClipMode.Letterbox;
                else if (panAndScanToolStripMenuItem.Checked) return ClipMode.PanAndScan;
                else return ClipMode.PlaClip;
            }
            set
            {
                letterboxToolStripMenuItem.Checked = value == ClipMode.Letterbox;
                panAndScanToolStripMenuItem.Checked = value == ClipMode.PanAndScan;
                plaClipToolStripMenuItem.Checked = value == ClipMode.PlaClip;
            }
        }

        private BindingMode bindingModeByControl
        {
            get
            {
                if (defaultSinglePageToolStripMenuItem.Checked) return BindingMode.SinglePage;
                if (singlePageWithoutScalingUpToolStripMenuItem.Checked) return BindingMode.SinglePageWithoutScalingUp;
                if (defaultLeftToRightToolStripMenuItem.Checked) return BindingMode.LeftToRight;
                return BindingMode.RightToLeft;
            }
            set
            {
                defaultSinglePageToolStripMenuItem.Checked = value == BindingMode.SinglePage;
                singlePageWithoutScalingUpToolStripMenuItem.Checked = value == BindingMode.SinglePageWithoutScalingUp;
                defaultLeftToRightToolStripMenuItem.Checked = value == BindingMode.LeftToRight;
                defaultRightToLeftToolStripMenuItem1.Checked = value == BindingMode.RightToLeft;
            }
        }

        const int DragEventKeyState_LeftButton = 1;
    }
}
