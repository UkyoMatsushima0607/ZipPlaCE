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
        string cmsRightClickPrepareAndShow_InitialTextBoxText;

        private void cmsRightClickPrepareAndShow_(Point point, bool onlyTag = false)
        {
            if (CreatingZipPathArray) return;

            AllowFullpower = false;

            if (cmsRightClick.Visible)
            {
                cmsRightClick.Close();
            }

            if (tvCatalog.SelectedIndicesCount == 0)
            {
                if (!onlyTag)
                {
                    pasteOnBackGroundToolStripMenuItem.Enabled = (InRealDirectory || InVirtualDirectory) && clipBoardHasFile(InRealDirectory);
                    if (this != ActiveForm) Activate();
                    AllowFullpower = false;
                    cmsRightClickOnBackground.Show(point);
                }
                else
                {
                    AllowFullpower = true;
                }
                return;
            }

            var clickedIndex = pointToIndex(point);
            if (clickedIndex < 0)
            {
                AllowFullpower = true;
                return;
            }

            bool openedInFileList = dgvFileList.RectangleToScreen(dgvFileList.ClientRectangle).Contains(point);

            var selectedIndex = tvCatalog.SelectedIndex;
            var singleSelected = selectedIndex >= 0;

            if (openedInFileList && !dgvFileList.ReadOnly)
            {
                var clientPosition = dgvFileList.PointToClient(point);
                var hit = dgvFileList.HitTest(clientPosition.X, clientPosition.Y);
                if (hit.RowIndex == dgvFileList.CurrentCell.RowIndex)
                {
                    AllowFullpower = true;
                    return;
                }
            }


            listFileToolStripMenuItem.Enabled = InDirectoryVirtualDirectoryOrSmartDirectory;

            try
            {
                deleteonlyForListFileToolStripMenuItem.Enabled = InDirectoryVirtualDirectoryOrSmartDirectory;
            }
            catch
            {
                deleteonlyForListFileToolStripMenuItem.Enabled = false;
            }

            if (InVirtualDirectory)
            {
                deleteonlyForListFileToolStripMenuItem.Text = Message.RemoveFromVirtualFolder;
            }
            else
            {
                deleteonlyForListFileToolStripMenuItem.Text = Message.Delete;
            }


            propertiesToolStripMenuItem.Enabled = singleSelected;

            rebuildAddToVirtualFolderMenuItems();

            // 新規作成が上（デザイナで移動すること、履歴から削除は未実装）

            ZipPlaInfo zipPlaInfo = null;
            ZipPlaInfo[] zipPlaInfos = null;
            var selectedIndices = tvCatalog.SelectedIndices; if (selectedIndices?.Contains(clickedIndex) != true)
            {
                AllowFullpower = true;
                return;
            }
            string[] selectedNameArray = new string[selectedIndices.Length];
            string[] selectedPathArray = new string[selectedIndices.Length];
            //bool allSelectionLoaded;
            //bool allSelectionArchive
            bool allSelectionArchiveOrDirectoryOrEmptyDirectory;
            bool atLeast1SelectionArchive;
            bool allSelectionEntity;
            var singlePath = "";
            var singleName = "";
            bool allSelectionFile;
            lock (ZipPathArrayLocker)
            {
                if (singleSelected)
                {
                    if (ZipPathArray == null || selectedIndex >= ZipPathArray.Length) selectedIndex = -1;
                    if (selectedIndex >= 0)
                    {
                        singlePath = ZipPathArray[selectedIndex];
                        singleName = ZipNameArray[selectedIndex];
                        zipPlaInfo = new ZipPlaInfo(singlePath);
                        zipPlaInfos = new ZipPlaInfo[1] { zipPlaInfo };
                    }
                }
                else if (selectedIndices.Length > 1 && ZipPathArray != null)
                {
                    zipPlaInfos = new ZipPlaInfo[selectedIndices.Length];
                    for (var i = 0; i < selectedIndices.Length; i++)
                    {
                        var index = selectedIndices[i];
                        if (index >= ZipPathArray.Length)
                        {
                            zipPlaInfos = null;
                            break;
                        }
                        zipPlaInfos[i] = new ZipPlaInfo(ZipPathArray[index]);
                    }
                }
                if (ZipNameArray == null)
                {
                    selectedNameArray = null;
                    selectedPathArray = null;
                    //allSelectionLoaded = false;
                    //allSelectionArchive = false;
                    allSelectionArchiveOrDirectoryOrEmptyDirectory = false;
                    atLeast1SelectionArchive = false;
                    allSelectionEntity = false;
                    //allSelectionCanBeCopied = false;
                    allSelectionFile = false;
                }
                else
                {
                    //allSelectionLoaded = true;
                    //allSelectionArchive = true;
                    allSelectionArchiveOrDirectoryOrEmptyDirectory = true;
                    atLeast1SelectionArchive = false;
                    allSelectionEntity = InDirectoryVirtualDirectoryOrSmartDirectory;
                    //allSelectionCanBeCopied = InDirectoryVirtualDirectoryOrSmartDirectory || InArchive;
                    allSelectionFile = !InArchive && !InMovie;
                    for (var i = 0; i < selectedIndices.Length; i++)
                    {
                        var index = selectedIndices[i];
                        if (index >= ZipNameArray.Length)
                        {
                            selectedNameArray = null;
                            selectedPathArray = null;
                            //allSelectionLoaded = false;
                            //allSelectionArchive = false;
                            allSelectionArchiveOrDirectoryOrEmptyDirectory = false;
                            atLeast1SelectionArchive = false;
                            allSelectionEntity = false;
                            //allSelectionCanBeCopied = false;
                            allSelectionFile = false;
                            break;
                        }
                        var selectedName = selectedNameArray[i] = ZipNameArray[index];
                        var path = ZipPathArray[index];
                        selectedPathArray[i] = path;
                        var loadResult = LoadResultArray[index];
                        //if (allSelectionLoaded && (loadResult == LoadResult.NotYet))// || loadResult == LoadResult.FileNotFound))
                        {
                            //    allSelectionLoaded = false;
                        }
                        var thisIsFolder = selectedName.Length > 0 && selectedName[selectedName.Length - 1] == Path.DirectorySeparatorChar;
                        if (allSelectionFile && thisIsFolder)
                        {
                            allSelectionFile = false;
                        }

                        if (allSelectionArchiveOrDirectoryOrEmptyDirectory && ((!thisIsFolder && (loadResult == LoadResult.LoadError || loadResult == LoadResult.FileNotFound)) ||
                                !(thisIsFolder || PackedImageLoader.Supports(path))))
                        {
                            allSelectionArchiveOrDirectoryOrEmptyDirectory = false;
                        }
                        if (!atLeast1SelectionArchive && !(loadResult == LoadResult.LoadError || loadResult == LoadResult.FileNotFound ||
                                !(thisIsFolder || PackedImageLoader.Supports(path))))
                        {
                            atLeast1SelectionArchive = true;
                        }
                        if (InVirtualDirectory)
                        {
                            var notEntity = true;
                            if (allSelectionEntity && (notEntity = path.Contains(Path.AltDirectorySeparatorChar)))
                            {
                                allSelectionEntity = false;
                            }
                        }
                    }

                }
            }

            cmsRightClickPrepareAndShow_SelectedPathList = selectedPathArray;

            //var isSingleDirectory = singleSelected && selectedNameArray != null && selectedNameArray.Length == 1 &&
            //    selectedNameArray[0].EndsWith(Path.DirectorySeparatorChar.ToString());


            var ffmpegExists = MovieThumbnailLoader.ffmpegExists();
            RecentFfmpegExists = ffmpegExists;
            AppInContextMenuEnabled = true;
            setAppInContextMenu(clickedIndex); // RecentFfmpegExists を使う


            renameToolStripMenuItem.Visible = openedInFileList;
            renameToolStripMenuItem.Enabled = false;

            var singleMovie = singleSelected && allSelectionFile && MovieThumbnailLoader.Supports(singleName);
            //var ffmpegExists = InArchive || (singleSelected && !InMovie && !singleMovie) || MovieThumbnailLoader.ffmpegExists(); // ffmpeg の存在が要求されない場合はチェックしない

            var singleImage = allSelectionFile && singleSelected && ImageLoader.SupportsImageAtLeastThumbnailReading(singlePath, ffmpegExists);

            setThumbnailManuallyToolStripMenuItem.Visible = !singleImage && !InMovie;
            setCoverToThisToolStripMenuItem.Visible = InMovie;

            var singleDir = !string.IsNullOrEmpty(singleName) && singleName[singleName.Length - 1] == Path.DirectorySeparatorChar;
            if (InMovie || (!(InSmartDirectory || InVirtualDirectory) && (allSelectionFile &&
                (singleImage ||
                //PackedImageLoader.Supports(singlePath) ||
                (singleSelected && atLeast1SelectionArchive) ||
                (ffmpegExists && MovieThumbnailLoader.Supports(singlePath))) || singleDir)))
            {
                setCoverOfParentFolderToThisToolStripMenuItem.Visible = true;
                if (isNoneRootItem(singlePath))
                {
                    if (InMovie)
                    {
                        setCoverOfParentFolderToThisToolStripMenuItem.Enabled = true;
                        setCoverOfParentFolderToThisToolStripMenuItem.Text = Message.SetCoverOfParentFolderToThis;
                    }
                    else if (isZipPlaCoverImageFile(singlePath))
                    {
                        setCoverOfParentFolderToThisToolStripMenuItem.Enabled = false;
                        setCoverOfParentFolderToThisToolStripMenuItem.Text = Message.SetCoverOfParentFolderToThis;
                    }
                    else
                    {
                        // 既に親フォルダの表紙が設定されているかどうかで表示内容を変更する場合

                        // 変更しない場合
                        if (singleDir)
                        {
                            // Enabled はサムネイルの存在チェック後
                            //setCoverOfParentFolderToThisToolStripMenuItem.Enabled = true;
                        }
                        else
                        {
                            setCoverOfParentFolderToThisToolStripMenuItem.Enabled = true;
                        }
                        setCoverOfParentFolderToThisToolStripMenuItem.Text = Message.SetCoverOfParentFolderToThis;
                    }
                }
                else
                {
                    setCoverOfParentFolderToThisToolStripMenuItem.Enabled = false;
                    setCoverOfParentFolderToThisToolStripMenuItem.Text = Message.SetCoverOfParentFolderToThis;
                }

            }
            else
            {
                setCoverOfParentFolderToThisToolStripMenuItem.Visible = false;
            }


            //copyToolStripMenuItem.Enabled = InDirectoryVirtualDirectoryOrSmartDirectory && allSelectionEntity;
            copyToolStripMenuItem.Enabled = (InDirectoryVirtualDirectoryOrSmartDirectory && allSelectionEntity) || InArchive || (InMovie && singleSelected);
            if (InMovie && singleSelected)
            {
                copyToolStripMenuItem.Text = Message.CopyCapturedImage;
            }
            else
            {
                copyToolStripMenuItem.Text = Message.Copy;
            }
            pasteToolStripMenuItem.Enabled = (InRealDirectory || InVirtualDirectory) && selectedIndices.Length <= 1 && clipBoardHasFile(InRealDirectory);

            var thumbnailCanBeSet = false;

            var renamable = false;
            cmsRightClick_RenamableByTextBox = false; // タグのメニューのイベントより前に下で書き換える
            if (zipPlaInfos != null)
            //if (zipPlaInfos != null && (allSelectionLoaded))
            //if(zipPlaInfos != null && (singleSelected || allSelectionLoaded)) // ユーザーが有効になるパターンを理解できない恐れ
            {
                cmsRightClickPrepareAndShow_ZipPlaInfoReady = true;

                var singleArchive = singleSelected && atLeast1SelectionArchive;// (singleName.EndsWith(Path.DirectorySeparatorChar.ToString()) || PackedImageLoader.SupportedArchivesPath.IsMatch(singlePath));
                var singleMovieOrSingleInMovie = singleMovie || singleSelected && InMovie; // ffmpeg が無くても削除のために表示する
                var singleInArchive = singleSelected && InArchive;
                //if (allSelectionLoaded)
                {
                    renamable = InDirectoryVirtualDirectoryOrSmartDirectory && allSelectionEntity;// !singlePath.Contains(Path.AltDirectorySeparatorChar);
                    ratingToolStripMenuItem.Enabled = renamable;
                    cutToolStripMenuItem.Enabled = InRealDirectory && allSelectionEntity;

                    // ページ方向はフォルダにも設定する
                    pageSequenceToolStripMenuItem.Visible = pageSequenceToolStripMenuItem.Enabled = allSelectionArchiveOrDirectoryOrEmptyDirectory;// allSelectionArchive;

                    // 表紙を番号で指定するためフォルダに適応するのは適切ではない
                    // 表紙をファイル名のハッシュ値で指定すればよいが、ファイル名が長くなってしまう。ハッシュ値の計算時間はランダムソートで使っているくらいなので問題にはならない
                    //thumbnailToolStripMenuItem.Visible = thumbnailToolStripMenuItem.Enabled = ( singleArchive && allSelectionFile) || singleMovieOrSingleInMovie || singleInArchive || singleImage;

                    // ↓ 削除はいつでも行えるように

                    thumbnailCanBeSet = (singleArchive && allSelectionFile) || singleMovieOrSingleInMovie || singleInArchive || singleImage;
                    thumbnailToolStripMenuItem.Visible = thumbnailToolStripMenuItem.Enabled = true;

                    if (!thumbnailCanBeSet)
                    {
                        if (!singleDir) setCoverOfParentFolderToThisToolStripMenuItem.Visible = false;
                        setThumbnailManuallyToolStripMenuItem.Visible = false;
                    }
                    setThumbnailManuallyToolStripMenuItem.Enabled = thumbnailCanBeSet && !singleImage && ((ffmpegExists || !singleMovie) && singleName?.EndsWith(Path.DirectorySeparatorChar.ToString()) != true); // フォルダに対する表紙設定は表紙を番号で指定する仕様上意味がなく、内蔵ビューアでも実装していない
                    tagToolStripMenuItem.Enabled = renamable;

                    renameToolStripMenuItem.Enabled = singleSelected && renamable;
                }

                var thumbnailHistogram = setRatingAndPageSequenceCheckStates(zipPlaInfos, selectedNameArray, selectedPathArray);

                if (singleDir)
                {
                    setCoverOfParentFolderToThisToolStripMenuItem.Enabled = thumbnailHistogram > 0;
                }

                if (singleImage)
                {
                    //clearThumbnailSettingToolStripMenuItem.Visible = false; // 一貫性のため表紙の削除は常に表示する
                    clearThumbnailSettingToolStripMenuItem.Visible = true;
                    clearThumbnailSettingToolStripMenuItem.Enabled = !(InMovie || InArchive) && thumbnailHistogram > 0;
                }
                else
                {
                    clearThumbnailSettingToolStripMenuItem.Visible = true;
                    if (InDirectoryVirtualDirectoryOrSmartDirectory)
                    {
                        clearThumbnailSettingToolStripMenuItem.Enabled = thumbnailHistogram > 0;
                    }
                    else if ((InMovie || InArchive) && LocationZipPlaInfo != null)
                    {
                        var hasThumbnailInfo = LocationZipPlaInfo.ThumbnailInfo != null;
                        clearThumbnailSettingToolStripMenuItem.Enabled = hasThumbnailInfo;
                        if (InMovie) // 違えば Visible でないので設定不要
                        {
                            setCoverToThisToolStripMenuItem.Enabled = thumbnailCanBeSet && singleSelected && (!hasThumbnailInfo || !LocationZipPlaInfo.ThumbnailInfo.Equals(getMoviePostionTuple(singleName)));
                        }
                    }
                    else
                    {
                        clearThumbnailSettingToolStripMenuItem.Enabled = false;
                    }
                }

                buildTagMenuItems(zipPlaInfos);
            }
            else
            {
                cmsRightClickPrepareAndShow_ZipPlaInfoReady = false;

                ratingToolStripMenuItem.Enabled = false;
                cutToolStripMenuItem.Enabled = false;
                pageSequenceToolStripMenuItem.Enabled = false;
                pageSequenceToolStripMenuItem.Visible = false;
                thumbnailToolStripMenuItem.Enabled = false;
                thumbnailToolStripMenuItem.Visible = false;

                Program.ClearAndDisposeItems(tagToolStripMenuItem.DropDownItems); // 開くたびに作り直す（X19）
                tagToolStripMenuItem.DropDownItems.Add("");
                tagToolStripMenuItem.Enabled = false;
                cmsRightClickPrepareAndShow_TagInitialCheckState = null;
                cmsRightClickPrepareAndShow_SelectedPathList = null;
                //cmsRightClickPrepareAndShow_TagNames = null;

                //tagToolStripMenuItem.Enabled = false;

                //clearThumbnailSettingToolStripMenuIteaaam.Enabled = false;
            }

            RightClickFileNameToolStripTextBox_TextChanged_SetDefaultValues = false;

            rightClickFileNameToolStripTextBox_SelectedText = "";
            var renamableByCmsTextBox = cmsRightClick_RenamableByTextBox = allowRenameByCmsRightClick && singleSelected && renamable;
            //selectionToFilterToolStripMenuItem.Enabled = false;
            //selectionToFilterToolStripMenuItem.Text = Message.FilterWithSelectedText;
            setRightClickFileNameTextBox(renamableByCmsTextBox ? getBaseName(singleName) : TextAnalyzer.TextToWildcard(selectedNameArray));
            rightClickFileNameToolStripTextBox.ReadOnly = !renamableByCmsTextBox;

            setRightClickMenuShortcutKeys(true);

            if (onlyTag && !tagToolStripMenuItem.Enabled)
            {
                cmsRightClickPrepareAndShow_ZipPlaInfoReady = false;
                AllowFullpower = true;
                return;
            }

            cmsRightClickPrepareAndShow_tagToolStripMenuItemDropDownItems = (from ToolStripItem item in tagToolStripMenuItem.DropDownItems select item).ToArray();

            if (onlyTag)
            {
                var items = cmsRightClick.Items;
                var tempItems = (from ToolStripItem item in items select item).ToArray();
                items.Clear();
                items.AddRange(cmsRightClickPrepareAndShow_tagToolStripMenuItemDropDownItems);

                ToolStripDropDownClosedEventHandler eh = null;
                eh += (sender, e) =>
                {
                    items.Clear();
                    items.AddRange(tempItems);
                    cmsRightClick.Closed -= eh;
                    tempItems = null;
                };
                cmsRightClick.Closed += eh;

                ToolStripDropDownScroller.EnscrollableOneTime(cmsRightClick, gestureListenergCmsRightRoot, enterFocus: true);
            }

            ratingChangeQue = -1;
            pageSequenceChangeQue = PageSequence.NotClicked;
            clearThumbnailSettingQue = false;

            //MessageBox.Show($"{cmsRightClick.ImageScalingSize}");

            //if (openedInFileList) dgvFileList.Update();
            if (this != ActiveForm) Activate();
            AllowFullpower = false;
            cmsRightClick.Show(point);

        }

        // 「リストに追加」のドロップダウンを、ブックマークにある仮想フォルダから作り直す
        private void rebuildAddToVirtualFolderMenuItems()
        {
            var vfBookmarks = getVirtualFoldersInBookmarks().ToArray();
            var spPos = listFileToolStripMenuItem.DropDownItems.IndexOf(tmsSeparatorForVirtualFolder);

            // 新規作成が下（デザイナで移動すること）
            while (spPos > 0)
            {
                listFileToolStripMenuItem.DropDownItems[--spPos].Dispose();
            }
            if (vfBookmarks.Length == 0)
            {
                tmsSeparatorForVirtualFolder.Visible = false;
            }
            else
            {
                tmsSeparatorForVirtualFolder.Visible = true;
                var tsmiList = new List<ToolStripMenuItem>();
                foreach (var bookmark in vfBookmarks)
                {
                    var tsmi = new ToolStripMenuItem();
                    tsmi.Text = Message.AddTo1.Replace("$1", bookmark.ToString());
                    var simpleBookmark = bookmark.SimpleBookmark;
                    tsmi.Enabled = simpleBookmark?.Location != currentLocation;
                    var vfPath = simpleBookmark?.Location;
                    tsmi.Click += (sender, e) => addSelectedToList(true, sorPath: vfPath);
                    tsmiList.Add(tsmi);
                }
                var ddi = listFileToolStripMenuItem.DropDownItems;
                ddi.Clear();
                if (string.Compare(currentLocation, Program.HistorySorPath, ignoreCase: true) == 0)
                {
                    ddi.AddRange(tsmiList.Concat(new ToolStripItem[2] { tmsSeparatorForVirtualFolder, addToNewListToolStripMenuItem }).ToArray());
                }
                else
                {
                    ddi.AddRange(tsmiList.Concat(new ToolStripItem[3] { tmsSeparatorForVirtualFolder, addToNewListToolStripMenuItem, removeFromHistoryToolStripMenuItem }).ToArray());
                }
            }
        }

        /// <summary>
        /// 選択中の項目の評価・綴じ方向を集計して、右クリックメニューのチェック（全部・一部・なし）を付ける。
        /// フォルダの表紙ファイルを含め、表紙が設定されている項目があれば正の値を返す（数は 1 で打ち切ることがある）
        /// </summary>
        private int setRatingAndPageSequenceCheckStates(ZipPlaInfo[] zipPlaInfos, string[] selectedNameArray, string[] selectedPathArray)
        {
            var ratingHistogram = new int[6];
            var pageSequenceHistogram = new int[4];
            var thumbnailHistogram = 0;
            if (zipPlaInfos != null && selectedNameArray != null && selectedPathArray != null &&
                zipPlaInfos.Length == selectedNameArray.Length && zipPlaInfos.Length == selectedPathArray.Length)
            {
                for (var i = 0; i < selectedNameArray.Length; i++)
                {
                    var name = selectedNameArray[i];
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name[name.Length - 1] == Path.DirectorySeparatorChar)
                    {
                        // thumbnailHistogram は 0 より大きいかしか使わないので、1 つ見つかったら残りのフォルダは調べない（K30）
                        if (thumbnailHistogram > 0) continue;
                        try
                        {
                            if (File.Exists(Path.Combine(selectedPathArray[i], ZipPlaCoverImageFileWithExtension)))
                            {
                                thumbnailHistogram++;
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        var info = zipPlaInfos[i];
                        var r = info.Rating;
                        if (r == null) ratingHistogram[0]++;
                        else if (0 < r && r <= 5) ratingHistogram[(int)r]++;

                        var p = info.BindingModeForSet;
                        if (p == null) pageSequenceHistogram[0]++;
                        else if (p == BindingMode.LeftToRight) pageSequenceHistogram[1]++;
                        else if (p == BindingMode.RightToLeft) pageSequenceHistogram[2]++;
                        else pageSequenceHistogram[3]++;

                        var t = info.ThumbnailInfo;
                        if (t != null) thumbnailHistogram++;
                    }
                }
            }

            var zipPlaInfosCount = zipPlaInfos.Length;
            CheckState checkState(int count) => count == 0 ? CheckState.Unchecked : count < zipPlaInfosCount ? CheckState.Indeterminate : CheckState.Checked;

            noneToolStripMenuItem.CheckState = checkState(ratingHistogram[0]);
            star1toolStripMenuItem.CheckState = checkState(ratingHistogram[1]);
            star2toolStripMenuItem.CheckState = checkState(ratingHistogram[2]);
            star3toolStripMenuItem.CheckState = checkState(ratingHistogram[3]);
            star4toolStripMenuItem.CheckState = checkState(ratingHistogram[4]);
            star5toolStripMenuItem.CheckState = checkState(ratingHistogram[5]);

            defaultToolStripMenuItem.CheckState = checkState(pageSequenceHistogram[0]);
            leftToRightToolStripMenuItem.CheckState = checkState(pageSequenceHistogram[1]);
            rightToLeftToolStripMenuItem.CheckState = checkState(pageSequenceHistogram[2]);
            singlePageToolStripMenuItem.CheckState = checkState(pageSequenceHistogram[3]);

            return thumbnailHistogram;
        }

        // 右クリックメニューの名前の欄で名前を変えられるか。タグのチェックを変えたときのイベントが、メニューを開いたあとの値を読む
        private bool cmsRightClick_RenamableByTextBox;

        /// <summary>右クリックメニューの「タグ」のドロップダウンを、選択中の項目のタグから作り直す（開くたびに作り直す。X19）</summary>
        private void buildTagMenuItems(ZipPlaInfo[] zipPlaInfos)
        {
            //tagToolStripMenuItem.Enabled = true;
            Program.ClearAndDisposeItems(tagToolStripMenuItem.DropDownItems); // 開くたびに作り直す（X19）


            ToolStripMenuItem[] items;
            CheckState[] initialCheckStates;
            var tags = Tags;
            if (tags == null) tags = new ZipTag[0];
            Program.SetTagsToToolStripMenuItems(tagToolStripMenuItem, out items, out initialCheckStates, 0, tags, zipPlaInfos);

            cmsRightClickPrepareAndShow_TagInitialCheckState = initialCheckStates;
            //cmsRightClickPrepareAndShow_TagNames = (from tag in tags select tag.Name).ToArray();

            if (items.Length > 0)
            {
                tagToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
                items[0].ShortcutKeyDisplayString = Message.RightClickNotToClose;
                if (items.Length > 1)
                {
                    items[1].ShortcutKeyDisplayString = Message.RightFlickIsAlsoOk;
                    if (items.Length > 2)
                    {
                        items[2].ShortcutKeyDisplayString = Message.SameAsAbove;
                    }
                }

            }

            var uncheckAll = new ToolStripMenuItem(Message.UncheckAll);
            uncheckAll.Enabled = initialCheckStates.Any(item => item != CheckState.Unchecked);
            //uncheckAll.ShortcutKeyDisplayString = Message.RightClickNotToClose;
            var checkedChangedByUncheckAll = false;
            foreach (var item in items)
            {
                item.CheckStateChanged += (sender2, e2) =>
                {
                    if (checkedChangedByUncheckAll) return;
                    uncheckAll.Enabled = items.Any(i => i.CheckState != CheckState.Unchecked);
                    //if (cmsRightClick_RenamableByTextBox)
                    {
                        for (var i = 0; i < initialCheckStates.Length; i++)
                        {
                            if (items[i].CheckState != initialCheckStates[i])
                            {
                                rightClickFileNameToolStripTextBox.ReadOnly = true;
                                SetAppInContextMenuEnabled(false);
                                return;
                            }
                        }
                        if (cmsRightClick_RenamableByTextBox) rightClickFileNameToolStripTextBox.ReadOnly = false;
                        SetAppInContextMenuEnabled(true);
                    }
                };
            }
            uncheckAll.MouseDown += (sender2, e2) =>
            {
                if (e2.Button == MouseButtons.Right)
                {
                    cmsRightClick.AutoClose = false;
                    tagToolStripMenuItem.DropDown.AutoClose = false;
                    //uncheckAll.Enabled = false; // ここで無効化すると MouseUp や MouseLeave が発生しない
                }

                if (e2.Button == MouseButtons.Right || e2.Button == MouseButtons.Left)
                {
                    checkedChangedByUncheckAll = true;
                    foreach (var item in items)
                    {
                        item.Checked = false;
                    }
                    checkedChangedByUncheckAll = false;
                    //if (cmsRightClick_RenamableByTextBox)
                    {
                        for (var i = 0; i < initialCheckStates.Length; i++)
                        {
                            if (items[i].CheckState != initialCheckStates[i])
                            {
                                rightClickFileNameToolStripTextBox.ReadOnly = true;
                                SetAppInContextMenuEnabled(false);
                                return;
                            }
                        }
                        if (cmsRightClick_RenamableByTextBox) rightClickFileNameToolStripTextBox.ReadOnly = false;
                        SetAppInContextMenuEnabled(true);
                    }
                }
            };
            uncheckAll.MouseUp += (sender2, e2) =>
            {
                if (e2.Button == MouseButtons.Right)
                {
                    cmsRightClick.AutoClose = true;
                    tagToolStripMenuItem.DropDown.AutoClose = true;
                    if (uncheckAll.Enabled && !items.Any(i => i.CheckState != CheckState.Unchecked)) uncheckAll.Enabled = false;
                }
            };
            uncheckAll.MouseLeave += (sender2, e2) =>
            {
                cmsRightClick.AutoClose = true;
                tagToolStripMenuItem.DropDown.AutoClose = true;
                if (uncheckAll.Enabled && !items.Any(i => i.CheckState != CheckState.Unchecked)) uncheckAll.Enabled = false;
            };
            tagToolStripMenuItem.DropDownItems.Insert(0, uncheckAll);
            tagToolStripMenuItem.DropDownItems.Insert(1, new ToolStripSeparator());

            var tagEditor = new ToolStripMenuItem(Message.EditTags + "...");
            tagEditor.Click += tagEditorToolStripMenuItem_Click;
            tagToolStripMenuItem.DropDownItems.Add(tagEditor);

            var addTagsFromFileName = new ToolStripMenuItem(Message.AddTagsFromName + "...");
            var newTagsSet = new HashSet<string>();
            var tagNames = (from tag in tags select tag.Name).ToArray();
            foreach (var info in zipPlaInfos) if (info.TagArray != null) foreach (var tag in info.TagArray) if (!tagNames.Contains(tag)) newTagsSet.Add(tag);
            if (newTagsSet.Count > 0)
            {
                var newTags = newTagsSet.ToArray();
                addTagsFromFileName.Click += (sender2, e2) =>
                {
                    showTagEditor(newTags);
                };
            }
            else
            {
                addTagsFromFileName.Enabled = false;
            }
            tagToolStripMenuItem.DropDownItems.Add(addTagsFromFileName);
        }

        // 右クリックメニューの名前の欄に text を入れ、幅を合わせ、大事な部分（TextAnalyzer.GetImportantRange）を選ぶ
        private void setRightClickFileNameTextBox(string text)
        {
            cmsRightClickPrepareAndShow_InitialTextBoxText = text;
            SizeF sizeF;
            using (var g = CreateGraphics())
            {
                sizeF = g.MeasureString(text, rightClickFileNameToolStripTextBox.Font);
            }
            var size = rightClickFileNameToolStripTextBox.Size;
            size.Width = (int)Math.Ceiling(sizeF.Width);
            rightClickFileNameToolStripTextBox.Size = size;
            RightClickFileNameToolStripTextBox_TextChanged_CheckFileNameChar = false;
            rightClickFileNameToolStripTextBox.Text = text;
            RightClickFileNameToolStripTextBox_TextChanged_CheckFileNameChar = true;
            var selected = TextAnalyzer.GetImportantRange(text);
            rightClickFileNameToolStripTextBox.Select(selected.Index, selected.Length);
            rightClickFileNameToolStripTextBox_SelectedText = selected.Value;
            foreach (var dss in getCurrentDynamicStringSelectionToolStripMenuItems()) dss.SelectedText = rightClickFileNameToolStripTextBox_SelectedText;
        }

        public void mctgCatalogForm_TouchGestureStarting(object sender, MiniControlTouchGestureStartingEventArgs e)
        {
            if (e.Control == tvCatalog)
            {
                e.Cancel = true;

                if (!showRating) return;
                if (!InDirectoryVirtualDirectoryOrSmartDirectory) return;
                if (CreatingZipPathArray) return;
                if (drawRating_Bound.IsEmpty) return;
                var clientStartPosition = tvCatalog.PointToClient(e.Location);
                var dataIndex = mgCatalog.InGesturing || cmsRightClick.Visible ? -1 : tvCatalog.PointToIndex(clientStartPosition);
                if (dataIndex < 0) return;

                var imageRectangle = tvCatalog.PointToContainerImageRectangle(clientStartPosition);
                var backRectangle = drawRating_Bound;
                backRectangle.X += imageRectangle.X;
                backRectangle.Y += imageRectangle.Y;
                if (backRectangle.Contains(clientStartPosition))
                {
                    e.Cancel = false;
                }
            }
            else if (e.Control == dgvFileList)
            {
                e.Cancel = true;

                if (!InDirectoryVirtualDirectoryOrSmartDirectory) return;
                if (CreatingZipPathArray) return;

                var clientStartPosition = dgvFileList.PointToClient(e.Location);
                var hit = dgvFileList.HitTest(clientStartPosition.X, clientStartPosition.Y);
                if (hit.RowIndex >= 0 && hit.ColumnIndex == tbcRating.Index)
                {
                    e.Cancel = false;
                }
            }
        }

        public void mctgCatalogForm_TouchGestureCompleted(object sender, MiniControlTouchGestureCompletedEventArgs e)
        {
            var ud = e.UserDirections; if (ud.Length < 1 || ud.Length > 2) return;
            if (e.StartingControl == cbFilter)
            {
                if (ud.Length != 1) return;
                if (ud[0] == MouseGestureDirection.Left)
                {
                    cbFilter.Text = "";
                }
                else if (ud[0] == MouseGestureDirection.Right)
                {
                    var s = GetClipBoardShortString();
                    if (!string.IsNullOrEmpty(s))
                    {
                        cbFilter.Text = s;
                        cbFilter.SelectAll();
                    }
                }
            }
            else if (e.StartingControl == btnGoToBack || e.StartingControl == btnGoToForward)
            {
                if (ud.Length == 1 && ud[0] == MouseGestureDirection.Down)
                {
                    showUndoBufferList(0, undoBuffer.Count);
                }
            }
            else if (e.StartingControl == tvCatalog)
            {
                if (ud.Length == 1 && ud[0] == MouseGestureDirection.Left)
                {
                    var orbit = e.MouseOrbit;
                    if (orbit == null || orbit.Length <= 0) return;
                    var onStars = !drawRating_Bound.IsEmpty && showRating && InDirectoryVirtualDirectoryOrSmartDirectory;
                    Rectangle imageRectangle = Rectangle.Empty, backRectangle = Rectangle.Empty;
                    if (onStars)
                    {
                        var clientPoint = orbit[0];
                        imageRectangle = tvCatalog.PointToContainerImageRectangle(clientPoint);
                        backRectangle = drawRating_Bound;
                        backRectangle.X += imageRectangle.X;
                        backRectangle.Y += imageRectangle.Y;
                        onStars = backRectangle.Contains(clientPoint);
                    }
                    if (onStars)
                    {
                        // カーソルの位置によっては星が期待通りに表示されない
                        Cursor.Position = tvCatalog.PointToScreen(new Point(backRectangle.Left - 1, (backRectangle.Top + backRectangle.Bottom) / 2));

                        SetRating(null, tvCatalog.PointToIndex(e.Location));
                    }
                }
            }
            else if (e.StartingControl == dgvFileList)
            {
                if (ud.Length == 1 && ud[0] == MouseGestureDirection.Left)
                {
                    var orbit = e.MouseOrbit;
                    if (orbit == null || orbit.Length <= 0) return;
                    var clientPoint = orbit[0];
                    var hit = dgvFileList.HitTest(clientPoint.X, clientPoint.Y);
                    if (hit.RowIndex >= 0 && hit.ColumnIndex == tbcRating.Index)
                    {
                        // カーソルの位置によっては星が期待通りに表示されない
                        var bound = dgvFileList.GetCellDisplayRectangle(hit.ColumnIndex, hit.RowIndex, cutOverflow: true);
                        Cursor.Position = dgvFileList.PointToScreen(new Point(bound.Left - 1, (bound.Top + bound.Bottom) / 2));

                        // 別の方法
                        //Cursor.Position = PointToScreen(e.Location);

                        var e2 = new RatingClickEventArgs();
                        e2.NewRating = 0;
                        e2.RowIndex = hit.RowIndex;
                        RatingGridViewTextBoxCell_RatingClick(null, e2);
                    }
                }
            }
            else
            {
                var menuStrip = e.StartingControl as MenuStrip; if (menuStrip == null) return;
                var orbit = e.MouseOrbit;
                if (orbit == null || orbit.Length <= 0) return;
                var toolStripItem = MiniControlTouchGesture.GetItem(menuStrip, orbit[0]) as ToolStripMenuItem;
                if (toolStripItem == null) return;

                if (ud.Length == 1 && ud[0] == MouseGestureDirection.Down)
                {
                    if (toolStripItem == addToolStripMenuItem)
                    {
                        PerformMouseDown(menuStrip, new MouseEventArgs(MouseButtons.Right, 1, orbit[0].X, orbit[0].Y, 0));
                        PerformMouseUp(menuStrip, new MouseEventArgs(MouseButtons.Right, 1, orbit[0].X, orbit[0].Y, 0));
                    }
                    else if (menuStrip == menuStripForTagFilter)
                    {
                        if (toolStripItem == ratingFilterToolStripMenuItem)
                        {
                            OpenRateButtonDropdown(byTouch: true);
                        }
                        else
                        {
                            OpenTagButtonDropdown(toolStripItem, byTouch: true);
                        }
                    }

                }
                else if (menuStrip == menuStripForTagFilter)
                {
                    if (toolStripItem == ratingFilterToolStripMenuItem)
                    {
                        if (ud.Length == 1)
                        {
                            if (ud[0] == MouseGestureDirection.Right)
                            {
                                andGreaterThanOrEqualToXToolStripMenuItem_Click(ratingFilterToolStripMenuItem, null);
                            }
                            else if (ud[0] == MouseGestureDirection.Left)
                            {
                                greaterThanOrEqualToXToolStripMenuItem_Click(ratingFilterToolStripMenuItem, null);
                            }
                            else if (ud[0] == MouseGestureDirection.Up)
                            {
                                andEqualToXToolStripMenuItem_Click(ratingFilterToolStripMenuItem, null);
                            }
                        }
                        else
                        {

                            if (ud[0] == MouseGestureDirection.Right && ud[1] == MouseGestureDirection.Up)
                            {
                                orEqualToXToolStripMenuItem_Click(ratingFilterToolStripMenuItem, null);
                            }
                            else if (ud[0] == MouseGestureDirection.Left)
                            {
                                equalToXToolStripMenuItem_Click(ratingFilterToolStripMenuItem, null);
                            }
                        }
                    }
                    else
                    {
                        if (ud.Length == 1)
                        {
                            if (ud[0] == MouseGestureDirection.Right)
                            {
                                tagOrInclude1_Click(toolStripItem, null);
                            }
                            else if (ud[0] == MouseGestureDirection.Left)
                            {
                                tagInclude1_Click(toolStripItem, null);
                            }
                            else if (ud[0] == MouseGestureDirection.Up)
                            {
                                tagAndExclude1_Click(toolStripItem, null);
                            }
                        }
                        else
                        {

                            if (ud[0] == MouseGestureDirection.Right && ud[1] == MouseGestureDirection.Up)
                            {
                                tagOrExclide1_Click(toolStripItem, null);
                            }
                            else if (ud[0] == MouseGestureDirection.Left)
                            {
                                tagExclude1_Click(toolStripItem, null);
                            }
                        }
                    }
                }
            }
        }

        public static void TouchGestureStartingForTagMenu(object sender, MiniControlTouchGestureStartingEventArgs e)
        {
            var dropDown = e.Control as ToolStripDropDown;
            if (dropDown == null)
            {
                e.Cancel = true;
                return;
            }
            var fullItems = dropDown.Items;
            var count = fullItems.Count;
            if (!(MiniControlTouchGesture.GetItem(dropDown, dropDown.PointToClient(e.Location)) is PrefixEscapedToolStripMenuItem))
            {
                e.Cancel = true;
            }
        }

        public static void TouchGestureCompletedForTagMenu(object sender, MiniControlTouchGestureCompletedEventArgs e)
        {
            if (!e.UserDirections.SequenceEqual(new MouseGestureDirection[] { MouseGestureDirection.Right })) return;
            var dropDown = e.StartingControl as ToolStripDropDown;
            if (dropDown == null) return;
            var fullItems = dropDown.Items;
            var count = fullItems.Count;
            var tags = (from ToolStripItem item in fullItems let p = item as PrefixEscapedToolStripMenuItem where p != null select p).ToArray();
            if (tags.Length == 0) return;
            var orbit = e.MouseOrbit;
            if (orbit == null || orbit.Length <= 0) return;
            var toolStripItem = MiniControlTouchGesture.GetItem(dropDown, orbit[0]) as ToolStripMenuItem;
            if (toolStripItem == null) return;
            var isTarget = tags.Contains(toolStripItem);
            if (!isTarget)
            {
                var index = fullItems.IndexOf(tags[0]);

                if (index < 0) return;
                if (index + 1 >= count) return;
                if (fullItems[index + 1] is PrefixEscapedToolStripMenuItem)
                {
                    isTarget = true;
                }
                else
                {
                    if (index + 2 >= count) return;
                    if (!(fullItems[index + 1] is ToolStripMenuItem) && fullItems[index + 2] is PrefixEscapedToolStripMenuItem)
                    {
                        isTarget = true;
                    }
                    else return;

                }
            }
            if (isTarget)
            {
                PerformMouseDown(dropDown, new MouseEventArgs(MouseButtons.Right, 1, orbit[0].X, orbit[0].Y, 0));
                PerformMouseUp(dropDown, new MouseEventArgs(MouseButtons.Right, 1, orbit[0].X, orbit[0].Y, 0));
            }
        }

        private static bool isNoneRootItem(string path)
        {
            if (path == null || path.Length <= 3) return false;

            var p = path.IndexOf(Path.DirectorySeparatorChar, 3);
            if (p < 0) return false;
            return path.IndexOf(Path.DirectorySeparatorChar, p + 2) >= 0;
        }

        private static bool clipBoardHasFile(bool allowVirtualFile)
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return false;

                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    var obj = data.GetData(DataFormats.FileDrop);
                    var files = obj as string[];
                    if (files != null) return files.Length > 0;
                }

                if (!allowVirtualFile) return false;

                if (data.GetDataPresent(NativeMethods.ZIPPLA_FILECONTENTS))
                {
                    return true;
                }

                //var iobj = obj as System.Runtime.InteropServices.ComTypes.IDataObject;
                //if (iobj != null) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        ToolStripDropDownCloseReason cmsRightClick_Closing_CloseReason;

        private void cmsRightClick_Closing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            cmsRightClickPrepareAndShow_ZipPlaInfoReady = false;

            msSortBy.Stop = msFilter.Stop = msCatalog.Stop = msFileList.Stop = false; // 直後の Opening と逆転することがあるので Closing で
            AllowFullpower = true; // 直後の Opening と逆転することがあるので Closing で

            cmsRightClick_Closing_CloseReason = e.CloseReason;

            // キーの処理
            /*
            if (cmsRightClick_Closing_CloseReason == ToolStripDropDownCloseReason.)

            ratingChangeQue =
                noneToolStripMenuItem.Selected ? 0 :
                star1toolStripMenuItem.Selected ? 1 :
                star2toolStripMenuItem.Selected ? 2 :
                star3toolStripMenuItem.Selected ? 3 :
                star4toolStripMenuItem.Selected ? 4 :
                star5toolStripMenuItem.Selected ? 5 : -1;!
                */
        }

        private void setNewDynamicStringSelectionToolStripMenuItems(bool enabled, DynamicStringSelectionInfo[] infos)
        {
            var currents = getCurrentDynamicStringSelectionToolStripMenuItems().ToArray();
            foreach (var current in currents)
            {
                current.Dispose();
            }

            rightClickFileNameToolStripTextBox.Visible = enabled;
            appStartSeparator.Visible = enabled;
            if (enabled)
            {
                var cmsItems = cmsRightClick.Items;
                var index = 1;
                foreach (var info in infos)
                {
#if SCREENCAPTCHA
                    if (info.DisplayName != null && Message.CurrentLanguage.ToString() == "en-US")
                    {
                        var m = Regex.Match(info.DisplayName, @"^""\$1"" を (.*) で検索$");
                        if (m.Success)
                        {
                            info.DisplayName = "Search \"$1\" on " + m.Groups[1].Value;
                        }
                    }
#endif
                    cmsItems.Insert(index++, info.GetDynamicStringSelectionToolStripMenuItem(this));
                }
            }
        }

        private IEnumerable<DynamicStringSelectionToolStripMenuItem> getCurrentDynamicStringSelectionToolStripMenuItems()
        {
            var items = cmsRightClick.Items;
            for (var i = 1; true; i++)
            {
                var result = items[i] as DynamicStringSelectionToolStripMenuItem;
                if (result != null) yield return result;
                else yield break;
            }
        }

        private void SetRating(int? rating, int index)
        {
            if (index < 0) return;
            if (CreatingZipPathArray) return;
            if (!InDirectoryVirtualDirectoryOrSmartDirectory) return;

            var apa = ZipPathArray;
            var zna = ZipNameArray;
            if (apa == null || zna == null) return;
            var count = apa.Length;
            if (zna.Length != count || count <= index) return;
            var path = apa[index];
            var name = zna[index];
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name)) return;
            var zipPlaInfo = new ZipPlaInfo(path);
            zipPlaInfo.Rating = rating;
            var newPath = zipPlaInfo.GetPathOfCurrentInfo(name[name.Length - 1] == Path.DirectorySeparatorChar);

            RenameShownArchive(index, path, newPath);
        }

        enum PageSequence { NotClicked, Cleared, LeftToRight, RightToLeft, SinglePage }

        PageSequence pageSequenceChangeQue = PageSequence.NotClicked;

        private void defaultToolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { pageSequenceChangeQue = PageSequence.Cleared; }

        private void leftToRightToolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { pageSequenceChangeQue = PageSequence.LeftToRight; }

        private void rightToLeftToolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { pageSequenceChangeQue = PageSequence.RightToLeft; }

        private void singlePageToolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { pageSequenceChangeQue = PageSequence.SinglePage; }

        private void defaultToolStripMenuItem_MouseLeave(object sender, EventArgs e) { pageSequenceChangeQue = PageSequence.NotClicked; }

        private void leftToRightToolStripMenuItem_MouseLeave(object sender, EventArgs e) { pageSequenceChangeQue = PageSequence.NotClicked; }

        private void rightToLeftToolStripMenuItem_MouseLeave(object sender, EventArgs e) { pageSequenceChangeQue = PageSequence.NotClicked; }

        private void singlePageToolStripMenuItem_MouseLeave(object sender, EventArgs e) { pageSequenceChangeQue = PageSequence.NotClicked; }

        // クリックイベントに動画内でのリネームが割り当ててある
        bool clearThumbnailSettingQue = false;

        private void clearThumbnailSettingToolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { if (InDirectoryVirtualDirectoryOrSmartDirectory) clearThumbnailSettingQue = true; }

        private int ratingChangeQue = -1;

        private void noneToolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { ratingChangeQue = 0; }

        private void star1toolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { ratingChangeQue = 1; }

        private void star2toolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { ratingChangeQue = 2; }

        private void star3toolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { ratingChangeQue = 3; }

        private void star4toolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { ratingChangeQue = 4; }

        private void star5toolStripMenuItem_MouseDown(object sender, MouseEventArgs e) { ratingChangeQue = 5; }

        private void noneToolStripMenuItem_MouseLeave(object sender, EventArgs e) { ratingChangeQue = -1; }

        private void star1toolStripMenuItem_MouseLeave(object sender, EventArgs e) { ratingChangeQue = -1; }

        private void star2toolStripMenuItem_MouseLeave(object sender, EventArgs e) { ratingChangeQue = -1; }

        private void star3toolStripMenuItem_MouseLeave(object sender, EventArgs e) { ratingChangeQue = -1; }

        private void star4toolStripMenuItem_MouseLeave(object sender, EventArgs e) { ratingChangeQue = -1; }

        private void star5toolStripMenuItem_MouseLeave(object sender, EventArgs e) { ratingChangeQue = -1; }

        /*
        private void ratingToolStripMenuItem_DropDown_KeyDown(object sender, KeyEventArgs e)
        {
            ratingChangeQue =
                noneToolStripMenuItem.Selected ? 0 :
                star1toolStripMenuItem.Selected ? 1 :
                star2toolStripMenuItem.Selected ? 2 :
                star3toolStripMenuItem.Selected ? 3 :
                star4toolStripMenuItem.Selected ? 4 :
                star5toolStripMenuItem.Selected ? 5 : -1;
        }

        */


        private PrefixEscapedToolStripMenuItem[] getTagNameToolStripMenuItems()
        {
            var tagNameToolStripMenuItems = new List<PrefixEscapedToolStripMenuItem>();
            bool inTags = false;
            //foreach (var item in tagToolStripMenuItem.DropDownItems)
            foreach (var item in cmsRightClickPrepareAndShow_tagToolStripMenuItemDropDownItems)
            {
                if (!inTags)
                {
                    if (item is ToolStripSeparator)
                    {
                        inTags = true;
                    }
                }
                else
                {
                    if (item is PrefixEscapedToolStripMenuItem)
                    {
                        tagNameToolStripMenuItems.Add(item as PrefixEscapedToolStripMenuItem);
                    }
                    else break;
                }
            }
            return tagNameToolStripMenuItems.ToArray();
        }

        private void renameForTextBoxRatingTagPageSequence(bool noop)
        {
            if (!noop)
            {
                var text = rightClickFileNameToolStripTextBox.Text;
                if (cmsRightClickPrepareAndShow_InitialTextBoxText != text)
                {
                    var selectedIndex = tvCatalog.SelectedIndex;
                    var zna = ZipNameArray;
                    var zpa = ZipPathArray;
                    if (zna != null && zpa != null && selectedIndex >= 0 && selectedIndex < zna.Length && zpa.Length == zna.Length)
                    {
                        var oldName = zna[selectedIndex];
                        var oldPath = zpa[selectedIndex];
                        if (!string.IsNullOrEmpty(oldName) && !string.IsNullOrEmpty(oldPath))
                        {
                            var oldBase = getBaseName(oldName);
                            if (oldBase == cmsRightClickPrepareAndShow_InitialTextBoxText)
                            {
                                var newInfo = new ZipPlaInfo(text);
                                var oldInfo = new ZipPlaInfo(oldName);
                                var changedThumbnail = newInfo.ThumbnailInfo != newInfo.ThumbnailInfo;
                                var changedEffectivePageSequence = false;
                                var isDir = oldName[oldName.Length - 1] == Path.DirectorySeparatorChar;
                                var newPath = isDir ? oldPath.Substring(0, oldPath.Length - oldName.Length + 1) + text :
                                    oldPath.Substring(0, oldPath.Length - oldName.Length) + text + oldName.Substring(oldBase.Length);
                                var needToCheck = !changedThumbnail && InDirectoryVirtualDirectoryOrSmartDirectory && currentClipMode == ClipMode.PlaClip && (isDir || PackedImageLoader.Supports(oldPath));
                                if (needToCheck)
                                {
                                    var oldBind = ZipPlaInfo.GetOnlyBindingModeFromFullName(oldPath);
                                    var newBind = ZipPlaInfo.GetOnlyBindingModeFromFullName(newPath);
                                    var oldEffective = oldBind == null ? currentBindingMode : (BindingMode)oldBind;
                                    var newEffective = newBind == null ? currentBindingMode : (BindingMode)newBind;
                                    changedEffectivePageSequence = oldEffective != newEffective;
                                    //MessageBox.Show($"{oldPath}\n{newPath}\n{changedThumbnail}\n{changedEffectivePageSequence}");
                                }
                                RenameShownArchive(selectedIndex, oldPath, newPath, atFirstArrayChange: true, needToReload: changedThumbnail || changedEffectivePageSequence);
                            }
                        }
                    }

                }
                else if (cmsRightClickPrepareAndShow_TagInitialCheckState != null && cmsRightClickPrepareAndShow_SelectedPathList != null)
                {
                    var changedTags = false;
                    var tagNameToolStripMenuItems = getTagNameToolStripMenuItems();

                    var states = (from item in tagNameToolStripMenuItems select item.CheckState).ToArray();
                    for (var i = 0; i < cmsRightClickPrepareAndShow_TagInitialCheckState.Length; i++)
                    {
                        if (states.Length <= i) return;
                        changedTags = changedTags || states[i] != cmsRightClickPrepareAndShow_TagInitialCheckState[i];
                    }
                    var changedRating = ratingChangeQue >= 0;
                    var changePageSequence = pageSequenceChangeQue != PageSequence.NotClicked;
                    var clearThumbnail = clearThumbnailSettingQue;

                    var changeEffectivePageSequence = false;

                    if (changedTags || changedRating || changePageSequence || clearThumbnail)
                    {
                        var selectedIndices = tvCatalog.SelectedIndices;
                        if (selectedIndices != null && selectedIndices.Length == cmsRightClickPrepareAndShow_SelectedPathList.Length)
                        {
                            var oldNames = new string[selectedIndices.Length];
                            var newNames = new string[selectedIndices.Length];
                            var needToRenameEvenIfFileNameIsNotChanged = new bool[selectedIndices.Length];

                            var tagNames = !changedTags ? null : (from item in tagNameToolStripMenuItems select item.PlainText).ToArray();
                            var tagCount = !changedTags ? 0 : tagNames.Length;

                            // フォルダかどうかの取得用
                            var zna = ZipNameArray;
                            var zpa = ZipPathArray;
                            if (zna != null && zpa != null && zna.Length == zpa.Length)
                            {
                                var count = zpa.Length;

                                // selectedIndices + ZipPathArray からパスを取得することも可能だが、
                                // path が意図しないものになるとフォルダ構成の破壊に繋がるため安全策をとる
                                for (var i = 0; i < cmsRightClickPrepareAndShow_SelectedPathList.Length; i++)
                                {
                                    var index = selectedIndices[i];
                                    if (index >= count) continue;
                                    var path = cmsRightClickPrepareAndShow_SelectedPathList[i];
                                    var name = zna[index];
                                    if (string.IsNullOrEmpty(name) || path != zpa[index]) continue;

                                    var zipPlaInfo = new ZipPlaInfo(path);

                                    if (changedTags)
                                    {
                                        var tags = zipPlaInfo.TagArray?.ToList();
                                        if (tags == null) tags = new List<string>();
                                        var newTags = new List<string>();
                                        for (var j = 0; j < tagCount; j++)
                                        {
                                            var state = states[j];
                                            var tagName = tagNames[j];
                                            if (state == CheckState.Indeterminate)
                                            {
                                                if (tags.Contains(tagName))
                                                {
                                                    newTags.Add(tagName);
                                                    tags.Remove(tagName);
                                                }
                                            }
                                            else
                                            {
                                                if (state == CheckState.Checked)
                                                {
                                                    newTags.Add(tagName);
                                                }
                                                tags.Remove(tagName);
                                            }
                                        }
                                        newTags.AddRange(tags);
                                        zipPlaInfo.TagArray = newTags.Count == 0 ? null : newTags.ToArray();
                                    }

                                    if (changedRating)
                                    {
                                        zipPlaInfo.Rating = ratingChangeQue;
                                    }

                                    if (changePageSequence)
                                    {
                                        var needToCheck = InDirectoryVirtualDirectoryOrSmartDirectory && currentClipMode == ClipMode.PlaClip;

                                        var old = needToCheck ? ZipPlaInfo.GetOnlyBindingModeFromFullName(path) : null;
                                        var oldEffective = old == null ? currentBindingMode : (BindingMode)old;
                                        BindingMode newEffeective;

                                        switch (pageSequenceChangeQue)
                                        {
                                            case PageSequence.Cleared:
                                                zipPlaInfo.BindingModeForSet = null;
                                                var parent = needToCheck ?
                                                    ZipPlaInfo.GetOnlyBindingModeFromFullName(currentLocation) : null;
                                                if (parent != null)
                                                {
                                                    newEffeective = (BindingMode)parent;
                                                }
                                                else
                                                {
                                                    newEffeective = currentBindingMode;
                                                }
                                                break;
                                            case PageSequence.LeftToRight:
                                                zipPlaInfo.BindingModeForSet = BindingMode.LeftToRight;
                                                newEffeective = BindingMode.LeftToRight;
                                                break;
                                            case PageSequence.RightToLeft:
                                                zipPlaInfo.BindingModeForSet = BindingMode.RightToLeft;
                                                newEffeective = BindingMode.RightToLeft;
                                                break;
                                            case PageSequence.SinglePage:
                                                zipPlaInfo.BindingModeForSet = BindingMode.SinglePage;
                                                newEffeective = BindingMode.SinglePage;
                                                break;
                                            default:
                                                newEffeective = oldEffective;
                                                break;
                                        }

                                        changeEffectivePageSequence = needToCheck && oldEffective != newEffeective;
                                    }

                                    if (clearThumbnail)
                                    {
                                        zipPlaInfo.ThumbnailInfo = null;
                                    }

                                    var isDir = name[name.Length - 1] == Path.DirectorySeparatorChar;

                                    oldNames[i] = path;
                                    newNames[i] = zipPlaInfo.GetPathOfCurrentInfo(isDir);

                                    // 先に消しておく
                                    if (isDir && clearThumbnail)
                                    {
                                        try
                                        {
                                            var coverPath = Path.Combine(path, ZipPlaCoverImageFileWithExtension);
                                            if (File.Exists(coverPath))
                                            {
                                                File.Delete(coverPath);
                                            }
                                            needToRenameEvenIfFileNameIsNotChanged[i] = true;
                                        }
                                        catch { }
                                    }
                                }


                                RenameShownArchive(selectedIndices, oldNames, newNames, atFirstArrayChange: true,
                                    needToReload: clearThumbnail || changeEffectivePageSequence,
                                    needToReloadEvenIfFileNameIsNotChanged: needToRenameEvenIfFileNameIsNotChanged);
                            }
                        }
                    }
                }
            }

            cmsRightClickPrepareAndShow_TagInitialCheckState = null;
            cmsRightClickPrepareAndShow_SelectedPathList = null;
            ratingChangeQue = -1;
            pageSequenceChangeQue = PageSequence.NotClicked;
            clearThumbnailSettingQue = false;
            cmsRightClickPrepareAndShow_InitialTextBoxText = null;
        }

        private void RightClickFileNameToolStripTextBox_MouseMove(object sender, MouseEventArgs e)
        {
            rightClickFileNameToolStripTextBox_SelectedTextChanged();
        }

        private void RightClickFileNameToolStripTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            rightClickFileNameToolStripTextBox_SelectedTextChanged();
        }

        private void RightClickFileNameToolStripTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            rightClickFileNameToolStripTextBox_SelectedTextChanged();

            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                cmsRightClick.Close();
            }
            else if (e.KeyCode == Keys.Down)
            {
                cmsRightClick.Focus();
                cmsRightClick.Items[1].Select();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                cmsRightClick.Focus();
                cmsRightClick.Items[cmsRightClick.Items.Count - 1].Select();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.A && e.Control && !e.Alt && !e.Shift)
            {
                if (rightClickFileNameToolStripTextBox != null &&
                    rightClickFileNameToolStripTextBox.Visible &&
                    rightClickFileNameToolStripTextBox.Focused &&
                    rightClickFileNameToolStripTextBox.Selected &&
                    !string.IsNullOrEmpty(rightClickFileNameToolStripTextBox.Text))
                {
                    rightClickFileNameToolStripTextBox.SelectAll();
                }
            }
        }

        private string rightClickFileNameToolStripTextBox_SelectedText;

        private void rightClickFileNameToolStripTextBox_SelectedTextChanged()
        {
            var selectedText = rightClickFileNameToolStripTextBox.SelectedText;
            if (selectedText == rightClickFileNameToolStripTextBox_SelectedText) return;
            rightClickFileNameToolStripTextBox_SelectedText = selectedText;

            foreach (var dss in getCurrentDynamicStringSelectionToolStripMenuItems()) dss.SelectedText = selectedText;

        }

        private void RightClickFileNameToolStripTextBox_MouseLeave(object sender, EventArgs e)
        {
            if (cmsRightClick.Visible && (rightClickFileNameToolStripTextBox.ReadOnly || !string.IsNullOrEmpty(rightClickFileNameToolStripTextBox.SelectedText))) cmsRightClick.Focus();
        }

        private void RightClickFileNameToolStripTextBox_MouseEnter(object sender, EventArgs e)
        {
            rightClickFileNameToolStripTextBox.Focus();
        }

        public void StringToFilterForDynamicStringSelection(string str)
        {
            if (string.IsNullOrEmpty(str)) return;

            cbFilter.Text = SearchManager.TrimAndPutQuotationIfNeeded(str, aliasesToFilteringStrings?.Keys);
            setCurrentFilterToHistory(true);
            cbFilter.SelectAll();
        }

        private static readonly Regex getVirtualFoldersInBookmarks_Regex = new Regex(@"\.sor$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private IEnumerable<ColoredBookmark> getVirtualFoldersInBookmarks()
        {
            return getSpecialFoldersInBookmarks(getVirtualFoldersInBookmarks_Regex, mustBeExistingFile: true);
        }

        private static readonly Regex getSmartFolderInBookmarks_Regex = new Regex($"\\.{SmartFolder.ExtensionWithoutPeriodInLower}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private IEnumerable<ColoredBookmark> getSmartFolderInBookmarks()
        {
            return getSpecialFoldersInBookmarks(getSmartFolderInBookmarks_Regex, mustBeExistingFile: true);
        }

        private IEnumerable<ColoredBookmark> getSpecialFoldersInBookmarks(Regex locationRegex, bool mustBeExistingFile)
        {
            var tbcDirectoryNameINdex = tbcDirectoryName.Index;
            foreach (DataGridViewRow row in dgvDirectoryList.Rows)
            {
                var coloredProfile = row.Cells[tbcDirectoryNameINdex].Value as ColoredBookmark;
                // セパレーターなら無視される
                if (coloredProfile == null || coloredProfile.SimpleBookmark.SpecialRole != SimpleBookmarkSpecialRole.None) continue;
                var target = coloredProfile?.SimpleBookmark?.Location;
                if (mustBeExistingFile && !FileExistsWithoutException(target)) continue;
                if (target != null && locationRegex.IsMatch(target))
                {
                    yield return coloredProfile; // yield は try の外
                }
            }
        }
    }
}
