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
        private CatalogProfile synchronizeCurrentProfile()
        {
            var currentProfile = currentConditionToProfile();
            var cc = currentProfileColor;
            foreach (DataGridViewRow row in dgvDirectoryList.Rows)
            {
                var bookmark = row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark;
                if (bookmark != null && bookmark.SimpleBookmark.SpecialRole == SimpleBookmarkSpecialRole.None && bookmark.Color == cc) // セパレーターなら無視される
                {
                    bookmark.Profile = currentProfile;
                }
            }
            foreach (var buf in undoBuffer)
            {
                if (buf.ProfileColor == cc)
                {
                    buf.Profile = currentProfile;
                }
            }
            return currentProfile;
        }

        private void addCatalogBookmarkToList(ColoredBookmark bookmark, bool insertToSelected = false)
        { addCatalogBookmarkToList(new ColoredBookmark[] { bookmark }, insertToSelected); }

        private void addCatalogBookmarkToList(IReadOnlyList<ColoredBookmark> bookmarks, bool insertToSelected = false)
        {
            var sr = dgvDirectoryList.SelectedRows;
            if (sr != null && sr.Count > 0)
            {
                addCatalogBookmarkToList(bookmarks, (from DataGridViewRow r in sr select r.Index).Min());
            }
            else
            {
                addCatalogBookmarkToList(bookmarks, int.MaxValue);
            }
        }

        private void addCatalogBookmarkToList(IReadOnlyList<ColoredBookmark> bookmarks, int insertIndex)
        {
            if (bookmarks == null) return;
            var count = bookmarks.Count;
            if (count == 0) return;
            var newRows = new List<DataGridViewRow>();
            var colorToProfile = new Dictionary<Color, CatalogProfile>();
            foreach (var bookmark in bookmarks)
            {
                var role = bookmark.SimpleBookmark.SpecialRole;
                if (role == 0)
                {
                    if (bookmark.Profile == null) continue; // フォールトトレランス

                    if (colorToProfile.ContainsKey(bookmark.Color))
                    {
                        bookmark.Profile = colorToProfile[bookmark.Color];
                    }
                    else
                    {
                        colorToProfile[bookmark.Color] = bookmark.Profile;
                    }

                    var row = new DataGridViewRow();
                    row.CreateCells(dgvDirectoryList);
                    row.Cells[tbcDirectoryName.Index].Value = bookmark;
                    newRows.Add(row);
                }
                else
                {
                    var row = new DataGridViewRow();
                    row.CreateCells(dgvDirectoryList);
                    var spBookmark = getColoredBookmarkWithSpecialRole(role);
                    spBookmark.Color = dgvDirectoryList.BackgroundColor;
                    spBookmark.SimpleBookmark.SetOrResetAlias(bookmark.SimpleBookmark.Alias);
                    row.Cells[tbcDirectoryName.Index].Value = spBookmark;
                    newRows.Add(row);
                }
            }
            foreach (DataGridViewRow row in dgvDirectoryList.Rows)
            {
                var bookmark = row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark;
                if (bookmark != null && bookmark.SimpleBookmark.SpecialRole == SimpleBookmarkSpecialRole.None && colorToProfile.ContainsKey(bookmark.Color)) // セパレーターなら無視される
                {
                    bookmark.Profile = colorToProfile[bookmark.Color];
                }
            }
            var rows = dgvDirectoryList.Rows;
            int addTop = Math.Max(0, Math.Min(rows.Count, insertIndex));
            rows.InsertRange(addTop, newRows.ToArray());
            var rowsCount = rows.Count;
            for (var i = 0; i < rowsCount; i++)
            {
                rows[i].Selected = false; // i >= start;
            }
        }

        private void moveToBookmark(int bookmarkRowIndex, ApplicationProviderUser user, bool loadOnlyProfileColor = false)
        {
            if (!dgvDirectoryList.ReadOnly) return;
            if (bookmarkRowIndex < 0) return;
            var rows = dgvDirectoryList.Rows;
            if (rows.Count > bookmarkRowIndex)
            {
                var bookmark = rows[bookmarkRowIndex].Cells[tbcDirectoryName.Index].Value as ColoredBookmark;
                if (bookmark == null || bookmark.SimpleBookmark.SpecialRole != SimpleBookmarkSpecialRole.None) return;// セパレーターなら無視される
                var profile = bookmark.Profile;
                var text = loadOnlyProfileColor ? null : bookmark?.SimpleBookmark?.Location;

                //if (text == null || profile == null) return; // フォールトトレランス
                if (profile == null) return; // フォールトトレランス

                var prevColor = currentProfileColor;
                var prevProfile = prepareMoveLocationWithChangingProfile(text, bookmark.Color, profile, user, out var prepareToReload);

                if (!string.IsNullOrEmpty(text))
                {
                    MakePreview(resetBookmark: false, prevProfileInfo: Tuple.Create(prevColor, prevProfile));
                }
                else if (prepareToReload)
                {
                    UpdatePreview();
                }
            }
        }

        private CatalogProfile prepareMoveLocationWithChangingProfile(string location, Color color, CatalogProfile profile, ApplicationProviderUser user)
            => prepareMoveLocationWithChangingProfile(location, color, profile, user, out var prepareToReload);

        private CatalogProfile prepareMoveLocationWithChangingProfile(string location, Color color, CatalogProfile profile, ApplicationProviderUser user, out bool prepareToReload)
        {
            var textExists = !string.IsNullOrEmpty(location);
            if (textExists) zabLocation.Text = location;

            prepareToReload = false;

            if (color != currentProfileColor)
            {
                // リロード時にブックマークのセーブが行われるのでこのシンクロのあとに明示的にセーブする必要はない
                var prevProfile = synchronizeCurrentProfile();

                currentProfileColor = color;

                if (profile.ClipMode is ClipMode clipMode && clipMode != clipModeByControl)
                {
                    prepareToReload = true;
                    clipModeByControl = clipMode;
                }

                if (profile.AligningMode != null || profile.MinimumFrameThickness != null)
                {
                    var temp = tvCatalog.StopUpdateRowsCols;
                    tvCatalog.StopUpdateRowsCols = true;
                    if (profile.AligningMode != null) currentAligning = (ThumbViewerAligningMode)profile.AligningMode;
                    if (profile.MinimumFrameThickness != null) currentMinimumFrameThickness = (double)profile.MinimumFrameThickness;
                    tvCatalog.StopUpdateRowsCols = temp;
                }

                if (profile.ThumbnailMouseWheelScrollAmount != null && profile.ThumbnailMouseWheelScrollUnit != null)
                {
                    setMouseWheelScrollAmount((int)profile.ThumbnailMouseWheelScrollAmount, (ThumbViewerScrollUnit)profile.ThumbnailMouseWheelScrollUnit);
                }

                if (profile.AspectRatio is double aspectRatioLocal && aspectRatioLocal != aspectRatio)
                {
                    prepareToReload = true;
                    aspectRatio = aspectRatioLocal;
                    setAspectRatioCheckState();
                }

                if (profile.ThumbnailSize is int size && 0 <= size && size < 5)
                {
                    var tsmi = (ToolStripMenuItem)thumbnailSizeToolStripMenuItem.DropDownItems[size + ChangeThumbnailSize_MenuItemOffset];
                    if (!tsmi.Checked)
                    {
                        prepareToReload = true;
                        ChangeThumbnailSize(tsmi);
                    }
                }

                var filterHistory = profile.FilterHistory;
                if (filterHistory != null)
                {
                    cbFilter.Items.Clear();
                    cbFilter.Items.Add("");
                    foreach (var h in filterHistory)
                    {
                        cbFilter.Items.Add(h);
                    }
                }

                var alias = profile.FilterAliasToString;
                if (alias != null)
                {
                    aliasesToFilteringStrings = new Dictionary<string, string>();
                    foreach (var kav in alias) aliasesToFilteringStrings.Add(kav.Key, kav.Value);
                }

                if (profile.InvariantFilter != null)
                {
                    invariantFilteringList = (bool)profile.InvariantFilter;
                }

                var tags = profile.Tags;
                if (tags != null)
                {
                    // runworker starting で行われるが早めに更新することを目的に実行。読み込み待ち中に他のウインドウをアクティブにすることも想定して needToSaveCurrentTag も true に
                    setTagFilteringMenuItems(needToRepairTagsFromBookmark: true, needToSaveCurrentTag: true);

                }

                if (profile.TinyOnScreenKeyboardVisible != null)
                {
                    TinyOnScreenKeyboardVisible = (bool)profile.TinyOnScreenKeyboardVisible;
                }
                if (profile.SelectionHighlightMode != null)
                {
                    SelectionHilightMode = (SelectionHighlightMode)profile.SelectionHighlightMode;
                }
                bool removeFilter = false;
                if (profile.FilterRemovingMode != null)
                {
                    var frm = (FilterRemovingMode)profile.FilterRemovingMode;
                    FilterRemovingMode = frm;
                    removeFilter = frm == FilterRemovingMode.WhenMovingLocation;
                }
                if (profile.LoadLastViewedPageFromHistory != null)
                {
                    LoadLastViewedPageFromHistory = (bool)profile.LoadLastViewedPageFromHistory;
                }
                if (profile.DoubleClickFileOpenMode != null)
                {
                    DoubleClickFileOpenMode = (DoubleClickMode)profile.DoubleClickFileOpenMode;
                }
                if (profile.RightClickContextMenu != null)
                {
                    RightClickContextMenu = (CatalogFormConetxtMenu)profile.RightClickContextMenu;
                }
                if (profile.MiddleClickContextMenu != null)
                {
                    MiddleClickContextMenu = (CatalogFormConetxtMenu)profile.MiddleClickContextMenu;
                }
                if (profile.ControlRightClickContextMenu != null)
                {
                    ControlRightClickContextMenu = (CatalogFormConetxtMenu)profile.ControlRightClickContextMenu;
                }
                if (profile.ItemSelectionFeedback != null)
                {
                    ItemSelectionFeedback = (ItemSelectionFeedback)profile.ItemSelectionFeedback;
                }
                if (profile.SynchroFromTreeToBar != null && profile.SynchroFromBarToTree != null)
                {
                    setSynchroBetweenTreeAndBar((bool)profile.SynchroFromTreeToBar, (bool)profile.SynchroFromBarToTree);
                }
                if (profile.CollapseOthersWhenSynchronizing != null)
                {
                    CollapseOthersWhenSynchronizing = (bool)profile.CollapseOthersWhenSynchronizing;
                }
                if (profile.ArchivesInArchiveMode is ArchivesInArchiveMode aiam && aiam != ArchivesInArchiveMode )
                {
                    prepareToReload = true;
                    ArchivesInArchiveMode = aiam;
                }
                if (profile.ReadOnMemoryMode is ReadOnMemoryMode romm)
                {
                    ReadOnMemoryMode = romm;
                }
                if (profile.OpenInPreviousImageFilterSetting != null)
                {
                    OpenInPreviousImageFilterSetting = (bool)profile.OpenInPreviousImageFilterSetting;
                }
                if (profile.DefaultBindingMode != null)
                {
                    bindingModeByControl = (BindingMode)profile.DefaultBindingMode;
                }
                if (profile.CoverBindingMode != null)
                {
                    CoverBindingMode = (CoverBindingMode)profile.CoverBindingMode;
                }
                if (profile.SortSyncMode is SortSyncMode sortSyncMode)
                {
                    SortSyncMode = sortSyncMode;
                }
                if (profile.AlwaysHideUI != null)
                {
                    SetAlwaysHideUI((bool)profile.AlwaysHideUI, silent: true);
                }
                if (profile.AllowReadAheadProcess != null)
                {
                    SetAllowReadAheadProcess((bool)profile.AllowReadAheadProcess, silent: true);
                }
                if (profile.BackColorOfBuiltInViewer != null)
                {
                    BackColorOfBuiltInViewer = (Color)profile.BackColorOfBuiltInViewer;
                }
                if (profile.SelectedColorForBackColorOfBuiltInViewer != null)
                {
                    SelectedColorForBackColorOfBuiltInViewer = (Color)profile.SelectedColorForBackColorOfBuiltInViewer;
                }
                if (profile.BuiltInViewerInitialFullscreenMode != null)
                {
                    BuiltInViewerInitialFullscreenMode = (InitialFullscreenMode)profile.BuiltInViewerInitialFullscreenMode;
                }

                if (profile.DisplayArchiveFiles != null && profile.DisplayImageFiles != null && profile.DisplayVideoFiles != null && profile.DisplayOtherFiles != null && profile.SubfolderMode != null)
                {
                    var a = ParseItemLoadLevel(profile.DisplayArchiveFiles);
                    var i = ParseItemLoadLevel(profile.DisplayImageFiles);
                    var v = ParseItemLoadLevel(profile.DisplayVideoFiles);
                    var o = ParseItemLoadLevel(profile.DisplayOtherFiles);
                    var s = (SubfolderMode)profile.SubfolderMode;

                    if (a == ItemLoadLevel.Ignore && i == ItemLoadLevel.Ignore && v == ItemLoadLevel.Ignore && o == ItemLoadLevel.Ignore && !IsDisplay(s)) a = ItemLoadLevel.Full;

                    if (!textExists && !prepareToReload) prepareToReload = ArchiveLoadLevel != a || ImageLoadLevel != i || VideoLoadLevel != v || OtherLoadLevel != o || SubfolderMode != s;

                    ArchiveLoadLevel = a;
                    ImageLoadLevel = i;
                    VideoLoadLevel = v;
                    OtherLoadLevel = o;
                    SubfolderMode = s;

                }
                if (profile.ThumbnailCacheRoot != null)
                {
                    if (profile.ThumbnailCacheRoot == CatalogFormConfig.NOCACHE)
                    {
                        thumbnailCache = null;
                    }
                    else
                    {
                        thumbnailCache = profile.ThumbnailCacheRoot;
                    }
                }

                if (profile.ShowIcon != null) showIcon = showIconToolStripMenuItem.Checked = (bool)profile.ShowIcon;
                if (profile.ShowRating != null) showRating = showRatingToolStripMenuItem.Checked = (bool)profile.ShowRating;
                if (profile.ShowTags != null) showTags = showTagsToolStripMenuItem.Checked = (bool)profile.ShowTags;
                //if (profile.ShowFilename != null) tvCatalog.ShowText = showFilenameToolStripMenuItem.Checked = (bool)profile.ShowFilename;
                if (profile.ShowFilename != null) setShowFileName((bool)profile.ShowFilename);
                
                var noReload = !locationValid() || user == ApplicationProviderUser.Error;

                // changeMaxCaptchaCount のコストが高いのでできるだけ後で行う
                if (profile.MaxCaptchaCountInOneVideo is int maxCaptchaCountInOneVideo)
                {
                    if (noReload || textExists || prepareToReload)
                    {
                        MaxCaptchaCountInAVideo = maxCaptchaCountInOneVideo;
                    }
                    else
                    {
                        changeMaxCaptchaCount(maxCaptchaCountInOneVideo, out prepareToReload);
                    }
                }

                if (noReload) prepareToReload = false;

                // リロードするかどうかが確定したあとの処理
                var makePreviewOrUpdate = textExists || prepareToReload;

                if (!removeFilter || !textExists) // さもなければ makePreview で消される。prepareToReload 後の Update では消されないので注意。
                {
                    cbFilter_TextChanged_StopResort = true;
                    if (profile.Filter != null) cbFilter.Text = profile.Filter;
                    cbFilter_TextChanged_StopResort = false;
                }

                if (profile.NumberOfLinesOfName is NumberOfLinesOfName numberOfLinesOfName && numberOfLinesOfName != NumberOfLinesOfName)
                {
                    SetNumberOfLinesOfName(numberOfLinesOfName, refresh: !makePreviewOrUpdate); // 読み込みの Starting でリフレッシュされるので Update の場合もここではリフレッシュしない

                    //NumberOfLinesOfName = (NumberOfLinesOfName)profile.NumberOfLinesOfName;
                }

                var needToResortForFolderSortMode = false;
                FolderSortMode needToResortForFolderSortMode_Mode = default(FolderSortMode);
                if (profile.FolderSortMode is FolderSortMode folderSortMode && folderSortMode != FolderSortMode)
                {
                    if (makePreviewOrUpdate)
                    {
                        SetFolderSortMode(folderSortMode, reSort: false);
                    }
                    else
                    {
                        needToResortForFolderSortMode_Mode = folderSortMode;
                        needToResortForFolderSortMode = true;
                    }
                }

                if (profile.LastSortMode is SortMode lastSortModeLocal && profile.SortMode is SortMode sortModeLocal)
                {
                    var temp = cbSortBy_SelectedValueChanged_Stop;
                    if (makePreviewOrUpdate) cbSortBy_SelectedValueChanged_Stop = true;
                    prevSortMode = (SortMode)profile.LastSortMode;
                    cbSortBy.SelectedIndex = (int)profile.SortMode;
                    sortMode = (SortMode)profile.SortMode;
                    GetSortArray_RandomSeed = profile.RandomSeed;
                    if (makePreviewOrUpdate) cbSortBy_SelectedValueChanged_Stop = temp;
                }

                if (needToResortForFolderSortMode) SetFolderSortMode(needToResortForFolderSortMode_Mode, reSort: true);

                if (makePreviewOrUpdate)
                {
                    //BackgroundMultiWorker.RunWorkerStartingEventHandler eh = null;
                    //eh = (sender2, e2) =>
                    bmwMakePreviewRunWorkerStartingFinalAction = () =>
                    {
                        prepareMoveLocationWithChangingProfile_SetProfileAfterReloaded(profile);

                        if (textExists && IsKeyboard(user))
                        {
                            firstShownItemSelect();
                        }

                        //bmwMakePreview.RunWorkerStarting -= eh;
                        bmwMakePreviewRunWorkerStartingFinalAction = null;
                    };
                    //bmwMakePreview.RunWorkerStarting += eh;
                }
                else
                {
                    prepareMoveLocationWithChangingProfile_SetProfileAfterReloaded(profile);

                    if (undoBufferIndex >= 0 && undoBufferIndex < undoBuffer.Count)
                    {
                        var data = undoBuffer[undoBufferIndex];
                        data.ProfileColor = color;
                    }
                }

                return prevProfile;
            }
            else if (textExists)
            {
                if (IsKeyboard(user))
                {
                    bmwMakePreviewRunWorkerStartingFinalAction = () =>
                    {
                        firstShownItemSelect();

                        //bmwMakePreview.RunWorkerStarting -= eh;
                        bmwMakePreviewRunWorkerStartingFinalAction = null;
                    };
                }

                return null;
            }
            else return null;
        }

        private void prepareMoveLocationWithChangingProfile_SetProfileAfterReloaded(CatalogProfile profile)
        {
            if (profile.ThumbnailListVisible != null) thumbnailListToolStripMenuItem.Checked = (bool)profile.ThumbnailListVisible;
            if (profile.FileListVisible != null) fileListToolStripMenuItem.Checked = (bool)profile.FileListVisible;
            if (profile.FileListDock != null) setFileListDock((DockStyle)profile.FileListDock, keepWidth: false, fixWidth: false);
            if (profile.FileListWidth != null)
            {
                pnlRight_NormalWidth = (int)profile.FileListWidth;
                setFileListWidthWithoutSettingWidth(pnlRight_NormalWidth);
            }
            if (profile.FileListOrder != null) Program.DecodeDisplayIndices(dgvFileList.Columns, (string[])profile.FileListOrder);
            if (profile.IconInFileList != null) tbcIcon.Visible = (bool)profile.IconInFileList;
            if (profile.PageInFileList != null) tbcPage.Visible = (bool)profile.PageInFileList;
            if (profile.RatingInFileList != null) tbcRating.Visible = (bool)profile.RatingInFileList;
            if (profile.DateCreatedInFileList != null) tbcCreateTime.Visible = (bool)profile.DateCreatedInFileList;
            if (profile.DateAccessedInFileList != null) tbcAccessTime.Visible = (bool)profile.DateAccessedInFileList;
            if (profile.DateModifiedInFileList != null) tbcWriteTime.Visible = (bool)profile.DateModifiedInFileList;
            if (profile.SizeInFileList != null) tbcSize.Visible = (bool)profile.SizeInFileList;
            if (profile.ResolutionInFileList != null) tbcResolution.Visible = (bool)profile.ResolutionInFileList;
            if (profile.RatioInFileList != null) tbcRatio.Visible = (bool)profile.RatioInFileList;
            if (profile.WidthInFileList != null) tbcWidth.Visible = (bool)profile.WidthInFileList;
            if (profile.HeightInFileList != null) tbcHeight.Visible = (bool)profile.HeightInFileList;
            if (profile.LengthInFileList != null) tbcLength.Visible = (bool)profile.LengthInFileList;
            if (profile.FpsInFileList != null) tbcFPS.Visible = (bool)profile.FpsInFileList;

            if (WindowState != FormWindowState.Minimized) setMainPanelWidth();
        }

        private static ItemLoadLevel ParseItemLoadLevel(string str)
        {
            if (str == null) return ItemLoadLevel.Full;
            if (!Enum.TryParse(str, out ItemLoadLevel result))
            {
                result = bool.TryParse(str, out bool strIsTrue) && !strIsTrue ? ItemLoadLevel.Ignore : ItemLoadLevel.Full;
            }
            return result;
        }

        private static bool IsKeyboard(ApplicationProviderUser user)
        {
            return user == ApplicationProviderUser.EnterKey || user == ApplicationProviderUser.AccessKey;
        }

        private void firstShownItemSelect()
        {
            if (tvCatalog.Count > 0)
            {
                var s2d = tvCatalog.ShowIndexToDataIndex;
                tvCatalog.SelectedIndex = s2d[0];

                if (pnlCenter.Visible) tvCatalog.Focus();
                else if (pnlRight.Visible) dgvFileList.Focus();
            }
        }

        private void DgvSorter_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            moveToBookmark(e.RowIndex, ApplicationProviderUser.MouseClick);
        }

        private void DgvSorter_RowsReleased(DataGridView sender, DataGridViewSorterRowsReleasedEventArgs e)
        {
            if (e.Operation == DataGridViewSorterRowsReleasedOperation.NotMoved)
            {
                moveToBookmark(e.RowIndex, ApplicationProviderUser.MouseClick);
            }
        }

        private Action bmwMakePreviewRunWorkerStartingFinalAction = null;

        DateTime BookmarkConfigModifiedDateTime = DateTime.MinValue;

        private bool SetBookmarkConfigModifiedDateTimeFromFile()
        {
            try
            {
                //BookmarkConfigModifiedDateTime = File.GetLastWriteTime(Configuration.XmlPath);
                BookmarkConfigModifiedDateTime = Configuration.GetLastWriteTimeWithoutFileCheckWithException();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void loadBookmarkFromConfig(bool reload = false)
        {
            var config = new ColoredBookmarkConfig();

            var ex = config.GetLastException();
            if (ex != null)
            {
                MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!SetBookmarkConfigModifiedDateTimeFromFile()) return;

            var bookmarks = config.Bookmarks;
            var bookmarkColors = config.BookmarkColors;
            var profiles = config.Profiles;
            var profileColors = config.ProfileColors;
            if (bookmarks != null && bookmarks.Length == bookmarkColors?.Length && profiles != null && profiles.Length == profileColors?.Length)
            {
                var coloredBookmarks = new ColoredBookmark[bookmarks.Length];
                for (var i = 0; i < bookmarks.Length; i++)
                {
                    var bookmark = bookmarks[i];
                    var color = (Color)bookmarkColors[i];
                    CatalogProfile profile = null;
                    for (var j = 0; j < profiles.Length; j++)
                    {
                        if (profileColors[j] == color)
                        {
                            profile = profiles[j];
                            break;
                        }
                    }
                    if (profile != null)
                    {
                        coloredBookmarks[i] = new ColoredBookmark(color, bookmark, profile);
                    }
                    else
                    {
                        coloredBookmarks[i] = new ColoredBookmark(color, bookmark, new CatalogProfile());
                    }
                }

                if (reload) dgvDirectoryList.Rows.Clear();

                addCatalogBookmarkToList(coloredBookmarks);
            }
        }

        bool saveBookmarkToIni_bookmarkChanged = false;

        private void saveBookmarkToConfig()
        {
            if (saveBookmarkToIni_bookmarkChanged)
            {
                saveBookmarkToIni_bookmarkChanged = false;
                var config = new ColoredBookmarkConfig();
                var bookmarks = (from DataGridViewRow row in dgvDirectoryList.Rows select row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark).ToArray();
                if (bookmarks != null)
                {
                    var simpleBookmarks = new SimpleBookmark[bookmarks.Length];
                    var bookmarkColors = new SerializableRGB[bookmarks.Length];
                    var profiles = new List<CatalogProfile>();
                    var profileColors = new List<SerializableRGB>();
                    for (var i = 0; i < bookmarks.Length; i++)
                    {
                        var bookmark = bookmarks[i];
                        var color = (SerializableRGB)bookmark.Color;
                        simpleBookmarks[i] = bookmark.SimpleBookmark;
                        bookmarkColors[i] = color;
                        var profile = bookmark.Profile;
                        if (profile != null && !profileColors.Contains(color))
                        {
                            profileColors.Add(color);
                            profiles.Add(profile);
                        }
                    }
                    config.Bookmarks = simpleBookmarks;
                    config.BookmarkColors = bookmarkColors;
                    config.Profiles = profiles.ToArray();
                    config.ProfileColors = profileColors.ToArray();
                }
                else
                {
                    config.Bookmarks = null;
                    config.BookmarkColors = null;
                    config.Profiles = null;
                    config.ProfileColors = null;
                }
                config.Save();
                SetBookmarkConfigModifiedDateTimeFromFile();
            }
        }

        private void dgvDirectoryList_UserDeletedRow(object sender, DataGridViewRowEventArgs e)
        {
            saveBookmarkToIni_bookmarkChanged = true;
            saveBookmarkToConfig();
        }

        private ColoredBookmark currentConditionToColoredBookmark(Color color, string location)
        {
            var simpleBookmark = new SimpleBookmark(null, location);
            if (string.IsNullOrEmpty(location))
            {
                var cl = currentLocation;
                simpleBookmark.Alias = string.IsNullOrEmpty(cl) ? Message.ProfileColor : Message.ProfileColorOf1.Replace("$1", SimpleBookmark.GetDisplayName(cl).Replace("&", "&&"));
            }
            return new ColoredBookmark(color, simpleBookmark, currentConditionToProfile());
        }

        private ColoredBookmark getColoredBookmarkWithSpecialRole(SimpleBookmarkSpecialRole specialRole)
        {
            var result = new ColoredBookmark(default(Color), new SimpleBookmark(specialRole), null);
            return result;
        }

        private void separatorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            addSeparatorToBookmarkList(insertToSelected: false);
        }

        private void addSeparatorToBookmarkList(bool insertToSelected)
        {
            //if (!locationValid()) return;

            var selectedRows = (from DataGridViewRow row in dgvDirectoryList.SelectedRows select row).ToArray();
            if (selectedRows.Length == 0) insertToSelected = false;
            int addTop;
            if (insertToSelected)
            {
                addTop = (from row in selectedRows select row.Index).Min();
            }
            else
            {
                addTop = dgvDirectoryList.RowCount;
            }

            var separator = getColoredBookmarkWithSpecialRole(SimpleBookmark.Separator);
            separator.SimpleBookmark.SetOrResetAlias(Message.Separator);
            addCatalogBookmarkToList(separator, insertToSelected);
            saveBookmarkToIni_bookmarkChanged = true;
            saveBookmarkToConfig();

            editBookmarkName(dgvDirectoryList.Rows[addTop]);
        }

        private CatalogProfile currentConditionToProfile()
        {
            if (Tags == null) setTagFilteringMenuItems(needToRepairTagsFromBookmark: false, needToSaveCurrentTag: false);

            return new CatalogProfile(
                prevSortMode,
                sortMode,
                sortMode == SortMode.Random ? GetSortArray_RandomSeed : null,
                cbFilter.Text,
                thumbnailSizeToolStripMenuItem.DropDownItems.IndexOf(SelectedThumbnailSize) - ChangeThumbnailSize_MenuItemOffset,
                aspectRatio,
                showIcon,
                showRating,
                showTags,
                tvCatalog.ShowText,
                clipModeByControl,
                currentAligning,
                NumberOfLinesOfName,
                currentMinimumFrameThickness,
                tvCatalog.MouseWheelScrollAmount,
                tvCatalog.MouseWheelScrollUnit,
                thumbnailListToolStripMenuItem.Checked,
                fileListToolStripMenuItem.Checked,
                getFileListDock(),
                pnlRight_NormalWidth,
                Program.EncodeDisplayIndices(dgvFileList.Columns),
                tbcIcon.Visible,
                tbcPage.Visible,
                tbcRating.Visible,
                tbcCreateTime.Visible,
                tbcAccessTime.Visible,
                tbcWriteTime.Visible,
                tbcSize.Visible,
                tbcResolution.Visible,
                tbcRatio.Visible,
                tbcWidth.Visible,
                tbcHeight.Visible,
                tbcLength.Visible,
                tbcFPS.Visible,
                TinyOnScreenKeyboardVisible,
                SelectionHilightMode,
                FilterRemovingMode,
                LoadLastViewedPageFromHistory,
                DoubleClickFileOpenMode,
                RightClickContextMenu,
                MiddleClickContextMenu,
                ControlRightClickContextMenu,
                ItemSelectionFeedback,
                SynchroFromTreeToBar,
                SynchroFromBarToTree,
                CollapseOthersWhenSynchronizing,
                FolderSortMode,
                //CheckDirectorySize,
                ArchivesInArchiveMode,
                ReadOnMemoryMode,
                OpenInPreviousImageFilterSetting,
                bindingModeByControl,
                CoverBindingMode,
                SortSyncMode,
                AlwaysHideUI,
                AllowReadAheadProcess,
                BackColorOfBuiltInViewer,
                SelectedColorForBackColorOfBuiltInViewer,
                BuiltInViewerInitialFullscreenMode,
                MaxCaptchaCountInAVideo,
                ArchiveLoadLevel,
                ImageLoadLevel,
                VideoLoadLevel,
                OtherLoadLevel,
                //archivesFilesPDFFielsToolStripMenuItem.Checked,
                //imageFilesToolStripMenuItem.Checked,
                //videoFilesToolStripMenuItem.Checked,
                //otherFilesToolStripMenuItem.Checked,
                thumbnailCache,
                SubfolderMode,
                (from object item in cbFilter.Items where !string.IsNullOrEmpty(item?.ToString()) select item.ToString()).ToArray(),
                invariantFilteringList,
                aliasesToFilteringStrings != null ? (from kav in aliasesToFilteringStrings select new KeyAndValue<string, string>(kav)).ToArray() : new KeyAndValue<string, string>[0], // null では意味が変わる
                Tags
                );
        }

        int dgvDirectoryList_MouseDown_RButtonDownIndex = -2;

        private void dgvDirectoryList_MouseDown(object sender, MouseEventArgs e)
        {
            simplifiedKeyBoard.UpHeldKey();
            LatestControledControl = sender as Control;
            //if (!dgvSorter.InRowsHolded) cmsDirectoryListPrepareAndShow(e);

            DataGridView.HitTestInfo hit = null;

            if (!dgvSorter.InRowsHolded && e.Button == MouseButtons.Right)
            {
                hit = dgvDirectoryList.HitTest(e.X, e.Y);
                var rowIndex = hit.RowIndex;
                dgvDirectoryList_MouseDown_RButtonDownIndex = rowIndex;
                if (!(from DataGridViewRow row in dgvDirectoryList.SelectedRows select row).Any(row => row.Index == rowIndex))
                {
                    foreach (DataGridViewRow row in dgvDirectoryList.Rows)
                    {
                        row.Selected = row.Index == rowIndex;
                    }
                    dgvDirectoryList.CurrentCell = rowIndex >= 0 ? dgvDirectoryList[0, rowIndex] : null;
                }
            }

            if (e.Button == MouseButtons.Left)
            {
                hit = hit ?? dgvDirectoryList.HitTest(e.X, e.Y);
                if (hit.RowIndex < 0 && hit.Type != DataGridViewHitTestType.ColumnHeader)
                {
                    foreach (DataGridViewRow row in (from DataGridViewRow r in dgvDirectoryList.SelectedRows select r).ToArray())
                    {
                        row.Selected = false;
                    }
                    dgvDirectoryList.EndEdit();
                }
            }
        }

        private void dgvDirectoryList_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && dgvDirectoryList_MouseDown_RButtonDownIndex >= -1)
            {
                cmsDirectoryListPrepareAndShow(e);
            }
            dgvDirectoryList_MouseDown_RButtonDownIndex = -2;
        }

        System.Drawing.Bitmap cmsDirectoryListPrepareAndShow_ProfileColorIcon;

        void cmsDirectoryListPrepareAndShow(MouseEventArgs e)
        {
            var hit = dgvDirectoryList.HitTest(e.X, e.Y);
            if (e.Button == MouseButtons.Right)
            {
                if (!dgvDirectoryList.ReadOnly && hit.RowIndex != dgvDirectoryList.CurrentCell.RowIndex)
                {
                    dgvDirectoryList.EndEdit();
                }

                //if (hit.RowIndex >= 0)
                if (hit.Type != DataGridViewHitTestType.ColumnHeader)
                {
                    if (hit.RowIndex >= 0)
                    {
                        var clickedRow = dgvDirectoryList.Rows[hit.RowIndex];
                        if (!clickedRow.Selected)
                        {
                            foreach (DataGridViewRow row in (from DataGridViewRow r in dgvDirectoryList.SelectedRows select r).ToArray())
                            {
                                row.Selected = false;
                            }
                            clickedRow.Selected = true;
                        }
                    }
                    else
                    {
                        foreach (DataGridViewRow row in (from DataGridViewRow r in dgvDirectoryList.SelectedRows select r).ToArray())
                        {
                            row.Selected = false;
                        }
                    }
                    var selectedRows = dgvDirectoryList.SelectedRows;
                    var selectedCount = selectedRows.Count;
                    deleteToolStripMenuItem.Enabled = selectedCount > 0;

                    bool specialRoleIncluded = false;
                    bool locationIncluded = false;
                    bool pureProfileColorIncluded = false;
                    bool aliasIncluded = false;
                    Color firstColor = Color.Empty ;
                    foreach (DataGridViewRow row in (from DataGridViewRow r in dgvDirectoryList.SelectedRows select r).ToArray())
                    {
                        var coloredBookmark = row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark;
                        if (coloredBookmark == null) continue;
                        var bookmark = coloredBookmark.SimpleBookmark;
                        if (bookmark == null) continue;
                        if (!specialRoleIncluded && bookmark.SpecialRole != SimpleBookmarkSpecialRole.None)
                        {
                            specialRoleIncluded = true;
                        }
                        else
                        {
                            if (firstColor.IsEmpty) firstColor = coloredBookmark.Color;
                        }
                        if (!pureProfileColorIncluded && string.IsNullOrEmpty(bookmark.Location)) pureProfileColorIncluded = true;
                        if (!aliasIncluded && bookmark.Alias != null) aliasIncluded = true;
                        if (!locationIncluded && bookmark.Location != null) locationIncluded = true;

                    }
                    realNameToolStripMenuItem.Enabled = aliasIncluded && !specialRoleIncluded && !pureProfileColorIncluded;
                    if (selectedCount == 1)
                    {
                        aliasToolStripMenuItem.Text = specialRoleIncluded || pureProfileColorIncluded ? Message.Rename : Message.EditAlias;
                        aliasToolStripMenuItem.Enabled = true;
                        openInExplorerInDirectoryListToolStripMenuItem.Enabled = !specialRoleIncluded;
                        loadOnlyProfileColorToolStripMenuItem.Enabled = !specialRoleIncluded;
                        loadOnlyProfileColorToolStripMenuItem.Text = specialRoleIncluded || locationIncluded ? Message.LoadOnlyProfileColor : Message.LoadProfileColor;


                    }
                    else
                    {
                        aliasToolStripMenuItem.Text = Message.EditAlias;
                        aliasToolStripMenuItem.Enabled = false;
                        openInExplorerInDirectoryListToolStripMenuItem.Enabled = false;
                        loadOnlyProfileColorToolStripMenuItem.Enabled = false;
                        loadOnlyProfileColorToolStripMenuItem.Text = Message.LoadOnlyProfileColor;
                    }
                    if (selectedCount == 1)
                    {
                        var path = (selectedRows[0].Cells[tbcDirectoryName.Index].Value as ColoredBookmark)?.SimpleBookmark?.Location;
                        editSmartFolderToolStripMenuItem.Visible = path?.ToLower()?.EndsWith("." + SmartFolder.ExtensionWithoutPeriodInLower) == true && FileExistsWithoutException(path);
                    }
                    else
                    {
                        editSmartFolderToolStripMenuItem.Visible = false;
                    }
                    if (selectedCount == 1 && !specialRoleIncluded && !firstColor.IsEmpty)
                    {
                        if (cmsDirectoryListPrepareAndShow_ProfileColorIcon == null)
                        {
                            var size = cmsDirectoryList.ImageScalingSize;
                            cmsDirectoryListPrepareAndShow_ProfileColorIcon = new System.Drawing.Bitmap(size.Width, size.Height);
                        }
                        loadOnlyProfileColorToolStripMenuItem.Image = cmsDirectoryListPrepareAndShow_ProfileColorIcon;
                        using (var g = Graphics.FromImage(cmsDirectoryListPrepareAndShow_ProfileColorIcon))
                        using (var b = new SolidBrush(firstColor))
                        {
                            g.FillRectangle(b, g.ClipBounds);
                        }
                    }
                    else
                    {
                        loadOnlyProfileColorToolStripMenuItem.Image = null;
                    }


                    dgvDirectoryList.Update();
                    //cmsDirectoryList.Show(Cursor.Position);
                    var p = e.X < 0 || e.Y < 0 ? Point.Empty : e.Location;

                    //cmsDirectoryList.Show(dgvDirectoryList.PointToScreen(p));
                    if (this != ActiveForm) Activate();
                    cmsDirectoryList.Show(dgvDirectoryList, p);
                }
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (hit.RowIndex < 0 && hit.Type != DataGridViewHitTestType.ColumnHeader)
                {
                    foreach (DataGridViewRow row in dgvDirectoryList.SelectedRows.Cast<DataGridViewRow>().ToArray())
                    {
                        row.Selected = false;
                    }
                    dgvDirectoryList.CurrentCell = null;
                    dgvDirectoryList.EndEdit();
                }
            }
        }

        private static bool FileExistsWithoutException(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path); }
            catch { return false; }
        }

        private async void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedRows = (from DataGridViewRow row in dgvDirectoryList.SelectedRows orderby row.Index select row).ToArray();
            if (selectedRows.Length > 1)
            {
                if (!await showConfirmationDialog(Message.DoYouDeleteFollowing1Items,
                    (from row in selectedRows let name = (row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark)?.SimpleBookmark?.ToString() where name != null select name).ToArray(),
                    dialogLBound: 2, getFileName: false)) return;
            }

            var deleted = false;
            var rows = dgvDirectoryList.Rows; // await を挟むので再度取得
            foreach (var row in selectedRows)
            {
                if (rows.Contains(row))
                {
                    deleted = true;
                    rows.Remove(row);
                }
            }
            if (deleted)
            {
                saveBookmarkToIni_bookmarkChanged = true;
                saveBookmarkToConfig();
            }
        }

        private void aliasToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedRows = dgvDirectoryList.SelectedRows;
            if (selectedRows.Count != 1) return;
            editBookmarkName(selectedRows[0]);
        }

        private void editBookmarkName(DataGridViewRow row)
        {
            var cell = row.Cells[tbcDirectoryName.Index];

            dgvDirectoryList.CurrentCell = cell;
            dgvDirectoryList.ReadOnly = false;
            dgvDirectoryList.BeginEdit(true);
        }

        private void realNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var renamed = false;
            foreach (DataGridViewRow row in dgvDirectoryList.SelectedRows)
            {
                var simple = (row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark)?.SimpleBookmark;
                if (simple?.SpecialRole == SimpleBookmarkSpecialRole.None && !string.IsNullOrEmpty(simple.Location))
                {
                    renamed = true;
                    simple?.SetOrResetAlias(null);
                }
            }
            if (renamed)
            {
                dgvDirectoryList.Refresh();
                saveBookmarkToIni_bookmarkChanged = true;
                saveBookmarkToConfig();
            }
        }

        private void dgvDirectoryList_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (!dgvDirectoryList.ReadOnly)
            {
                dgvDirectoryList.ReadOnly = true;
                saveBookmarkToConfig();
            }
        }

        private void dgvDirectoryList_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            if (e.RowIndex == dgv.NewRowIndex || !dgv.IsCurrentCellDirty)
            {
                return;
            }
            var alias = e.Value.ToString();
            var bookmark = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].Value as ColoredBookmark;
            if (!string.IsNullOrEmpty(alias) && alias.Length < 128)
            {
                bookmark?.SimpleBookmark?.SetOrResetAlias(alias);
            }
            e.Value = bookmark; // CellParsing 後 e.Value が対象のセルに代入されるので bookmark が参照型であることとは無関係にこの行は必要
            e.ParsingApplied = true;
            saveBookmarkToIni_bookmarkChanged = true;
        }

        private void dgvDirectoryList_KeyDown(object sender, KeyEventArgs e)
        {
            var mk = ModifierKeys;
            if (e.KeyCode == Keys.F2)
            {
                aliasToolStripMenuItem_Click(sender, e);
                LatestControledControl = sender as Control;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                var sr = dgvDirectoryList.SelectedRows;
                if (sr.Count == 1)
                {
                    moveToBookmark(sr[0].Index, ApplicationProviderUser.EnterKey);
                    LatestControledControl = sender as Control;
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

        private void deleteKeyAction(Keys modifierKeys)
        {
            var lcc = LatestControledControl;
            if (lcc == dgvDirectoryList)
            {
                deleteToolStripMenuItem_Click(null, null);
            }
            else if (lcc == tvCatalog || lcc == dgvFileList)
            {
                deleteSelectedItems(modifierKeys);
            }
        }

        private void CopyKeyAction(Keys modifierKeys)
        {
            var lcc = LatestControledControl;
            if (lcc == tvCatalog || lcc == dgvFileList)
            {
                copySelection(modifierKeys);
            }
        }

        private void PasteKeyAction(Keys modifierKeys)
        {
            var lcc = LatestControledControl;
            if (lcc == tvCatalog || lcc == dgvFileList)
            {
                pasteToCurrentLocation();
            }
        }

        private void CutKeyAction(Keys modifierKeys)
        {
            var lcc = LatestControledControl;
            if (lcc == tvCatalog || lcc == dgvFileList)
            {
                cutSelection(modifierKeys);
            }
        }

        private void AppsKeyAction(Keys modifierKeys)
        {
            var lcc = LatestControledControl;
            if (lcc == dgvDirectoryList)
            {
                var selectedRows = dgvDirectoryList.SelectedRows;
                if (selectedRows.Count > 0)
                {
                    var selectedRow = getFirstDisplayedSelectedRow(dgvDirectoryList);
                    if (selectedRow == null) return;
                    var rect = dgvDirectoryList.GetRowDisplayRectangle(selectedRow.Index, cutOverflow: true);
                    var m2 = Math.Min(rect.Height, rect.Width) / 2;
                    var p = new Point(rect.X + m2, rect.Y + m2);
                    cmsDirectoryListPrepareAndShow(new MouseEventArgs(MouseButtons.Right, 1, p.X, p.Y, 0));
                }
                else if (dgvDirectoryList.Visible)
                {
                    cmsDirectoryListPrepareAndShow(new MouseEventArgs(MouseButtons.Right, 1, -1, -1, 0));
                }
            }
            else if (lcc == tvCatalog)
            {
                cmsRightClickPrepareAndShowForKeyOnTvCatalog(onlyTag: false, modifierKeys: modifierKeys);
            }
            else if (lcc == dgvFileList)
            {
                cmsRightClickPrepareAndShowForKeyOnFileList(onlyTag: false, modifierKeys: modifierKeys);
            }
            else if (lcc == explorerTreeView.TreeViewWnd)
            {
                cmsRightClickPrepareAndShowForKeyOnTreeView();
            }
        }

        private void openInExplorerInDirectoryListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedRows = dgvDirectoryList.SelectedRows;
            if (selectedRows.Count == 1)
            {
                var path = (selectedRows[0].Cells[tbcDirectoryName.Index].Value as ColoredBookmark)?.SimpleBookmark?.Location;
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            Process.Start(path);
                        }
                        else
                        {
                            Program.OpenWithExplorer(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void dgvDirectoryList_MouseMove(object sender, MouseEventArgs e)
        {
            if (dgvDirectoryList_MouseDown_RButtonDownIndex >= -1)
            {
                if (dgvDirectoryList_MouseDown_RButtonDownIndex != dgvDirectoryList.HitTest(e.X, e.Y).RowIndex)
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        dgvDirectoryList_DragOutPart();
                    }
                    dgvDirectoryList_MouseDown_RButtonDownIndex = -2;
                }
            }
            else
            {
                dgvDirectoryList_MouseLeave_DragOutPart(e);
            }
        }
    }
}
