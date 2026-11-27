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
        private void MakePreview(string selectedFileName) { MakePreview(-1, -1, selectedFileName); }

        double MakePreview_tScrollbar = 0;

        int MakePreview_fScrollbar = -1;

        int MakePreview_selectedIndex = -1;

        string MakePreview_selectedFileName = null;

        Tuple<TimeSpan, TimeSpan> MakePreview_subdivideInterval = null;

        bool MakePreview_Que = false;

        bool MakePreview_StopResetFilter = false;

        const int MakePreview_fScrollbarMoveToSelectedFile = -2;

        const double MakePreview_tScrollbarMoveToSelectedFile = -2;

        bool MakePreview_needToSynchroTree = false;

        private void MakePreview(double tScrollbar = 0, int selectedIndex = -1, string selectedFileName = null, int manualUndoShift = 0, bool resetBookmark = true,
            int fScrollbar = -1, Tuple<Color, CatalogProfile> prevProfileInfo = null, Tuple<TimeSpan, TimeSpan> subdivideInterval = null, bool needToSynchroTree = true)
        {
            showStartHelpMessage = false;
            MakePreview_subdivideInterval = subdivideInterval;
            MakePreview_needToSynchroTree = needToSynchroTree;
            bool isDir;
            string finalMessage = null;
            var stopResetFilter = false;
            string text;
            try
            {
                if (MakePreview_StopResetFilter)
                {
                    MakePreview_StopResetFilter = false;
                    stopResetFilter = true;
                }

                ImageLoader.SusieReset();
                PackedImageLoader.CheckSevenZipExistence(this);

                text = zabLocation.Text;
                if (string.IsNullOrEmpty(text))
                {
                    bmwMakePreviewRunWorkerStartingFinalAction = null;
                    return;
                }
                if (!IsSupportedByCatalogFormAndExists(text, out isDir))
                {
                    MessageBox.Show(this, Message._1CanNotBeOpend.Replace("$1", zabLocation.Text), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    MakePreview_Rollback(prevProfileInfo);
                    return;
                }

                if (isDir && thumbnailCache == GPSizeThumbnail.AlternateDataStream
                    && GPSizeThumbnail.SupportADSSizeRecheckingUnsupported(text) <= 0
                    && (string.IsNullOrEmpty(currentLocation)
                    || GPSizeThumbnail.SupportADSSize(currentLocation, checkNew: false) > 0
                    ))
                {
                    //MessageBox.Show(this, Message.AdsNotSupportedWarningMessage, Message.Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    //このタイミングでメッセージを表示するとフォルダが読み込まれない
                    finalMessage = Message.AdsNotSupportedWarningMessage;
                }

                var fullPath = Path.GetFullPath(/*File.Exists(text)*/ !isDir ? text : GetDirectoryPathWithSeparator(text));
                //if (fullPath != text) zabLocation.Text = getPathWithoutSeparator(fullPath);
                if (fullPath != text) zabLocation.SetText(getPathWithoutSeparator(fullPath), makeButtons: false, makeEvent: false);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                MakePreview_Rollback(prevProfileInfo);
                return;
            }

            if (resetBookmark) foreach (DataGridViewRow row in dgvDirectoryList.Rows) row.Selected = false;

            setNonVolatilitySort();

            preCurrentLocation = getPathWithoutSeparator(zabLocation.Text);
            //addToolStripMenuItem.Enabled = false;


            undoBufferUpdateForMovingLocation(manualUndoShift, prevProfileInfo);

            if (!stopResetFilter && FilterRemovingMode == FilterRemovingMode.WhenMovingLocation)
            {
                cbFilter_TextChanged_StopResort = true;
                cbFilter.Text = "";
                cbFilter_TextChanged_StopResort = false;
            }

            MakePreview_tScrollbar = tScrollbar;
            MakePreview_fScrollbar = fScrollbar;
            MakePreview_selectedIndex = selectedIndex;
            if (/*scrollbar == 0 &&*/ selectedIndex < 0)
            {
                MakePreview_selectedFileName = selectedFileName;
            }
            else
            {
                MakePreview_selectedFileName = null;
            }
            zabLocation.Enabled = false;

            bmwMakePreview.CancelAsync();

            if (bwMakePreviewPrepare.IsBusy)
            {
                MakePreview_Que = true;
                bwMakePreviewPrepare.CancelAsync();
            }
            else
            {
                if (isDir)
                {
                    try
                    {
                        fileSystemWatcher.Path = preCurrentLocation;
                        fileSystemWatcher.IncludeSubdirectories = true;// SubfolderIsSearched()
                        fileSystemWatcher.EnableRaisingEvents = true;
                    }
                    catch
                    {
                        fileSystemWatcher.EnableRaisingEvents = false;
                    }
                }
                else
                {
                    fileSystemWatcher.EnableRaisingEvents = false;
                }

                CreatingZipPathArray = true;
                CloseVisibleContextMenuStrips(all: false);

                loadingGuid = Guid.Empty;
                bwMakePreviewPrepare.RunWorkerAsync(preCurrentLocation);
            }

            //MessageBox.Show($"{InvokeRequired}");
            //optionToolStripMenuItem.Text = "a";

            if (needToSynchroTree && SynchroFromBarToTree && folderTreeToolStripMenuItem.Checked)
            {
                setToTree(text, CollapseOthersWhenSynchronizing);
            }
            //optionToolStripMenuItem.Text = "b";

            if (finalMessage != null) MessageBox.Show(this, finalMessage, Message.Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void setToTree(string path, bool collapse)
        {
            //var treeWnd = explorerTreeView.TreeViewWnd;
            //treeWnd.BeginUpdate();
            explorerTreeView.BeginUpdate();
            try
            {
                if (!folderTreeToolStripMenuItem.Checked)
                {
                    folderTreeToolStripMenuItem.Checked = true;
                    setFolderTreeVisible(silent: false);
                }

                explorerTreeView.TrySelect(path);

                var rootNode = explorerTreeView.TreeViewWnd.Nodes[0];

                if (!rootNode.IsExpanded)
                {
                    rootNode.Expand();
                }
                else if (collapse)
                {
                    var selectedNode = explorerTreeView.SelectedNode;
                    if (selectedNode != null)
                    {
                        var node = selectedNode == rootNode ? selectedNode : selectedNode.Parent;
                        var preNode = null as TreeNode;
                        do
                        {
                            foreach (TreeNode subNode in node.Nodes)
                            {
                                if (subNode != preNode && subNode.IsExpanded) subNode.Collapse();
                            }
                            preNode = node;
                            node = node.Parent;
                        }
                        while (node != null);

                        // text が長いと右にクスロールしてしまうのを回避
                        var text = selectedNode.Text;
                        selectedNode.Text = "";
                        selectedNode.EnsureVisible();
                        selectedNode.Text = text;
                    }
                }
            }
            catch { }
            finally
            {
                explorerTreeView.EndUpdate();
            }
        }

        private void MakePreview_Rollback(Tuple<Color, CatalogProfile> prevProfileInfo)
        {
            if (prevProfileInfo == null || prevProfileInfo.Item2 == null)
            {
                zabLocation.Text = currentLocation;
            }
            else
            {
                prepareMoveLocationWithChangingProfile(currentLocation, prevProfileInfo.Item1, prevProfileInfo.Item2, ApplicationProviderUser.Error);
            }
            bmwMakePreviewRunWorkerStartingFinalAction = null;

            // updatePreview のオーバーロードには currentLocation を書きかえるものがあるが
            // そのオーバーロードは ZipPla 自身がファイル名を書き換えたときに使うものなので
            // preCurrent よりも current の方が高い正当性が期待される
        }

        private void undoBufferUpdateForMovingLocation(int manualUndoShift, Tuple<Color, CatalogProfile> prevProfileInfo)
        {
            if (prevProfileInfo != null && prevProfileInfo.Item2 == null) prevProfileInfo = null;
            if (undoBufferIndex >= 0 && !string.IsNullOrEmpty(currentLocation))
            {
                var index = tvCatalog.SelectedIndex;
                var zna = ZipNameArray;
                string fileName;
                if (index < 0 || zna == null || zna.Length <= index)
                {
                    fileName = null;
                }
                else
                {
                    fileName = zna[index];
                }
                var newData = new UndoBufferClass
                {
                    Location = currentLocation,
                    ThumbnailPosition = tvCatalog.ScrollBarPercentage,
                    FileListPosition = dgvFileList.FirstDisplayedScrollingRowIndex,
                    SelectedFileName = fileName,
                    ProfileColor = prevProfileInfo != null ? prevProfileInfo.Item1 : currentProfileColor,
                    Profile = prevProfileInfo != null ? prevProfileInfo.Item2 : currentConditionToProfile()
                };

                var preIndex = undoBufferIndex - manualUndoShift;
                if (0 <= preIndex && preIndex < undoBuffer.Count)
                {
                    undoBuffer[preIndex] = newData;
                }
            }

            if (manualUndoShift == 0)
            {
                var newPath = true;
                if (0 <= undoBufferIndex && undoBufferIndex < undoBuffer.Count)
                {
                    newPath = undoBuffer[undoBufferIndex].Location != preCurrentLocation ||
                        (prevProfileInfo != null && prevProfileInfo.Item1 != currentProfileColor);
                }
                if (newPath)
                {
                    var newData = new UndoBufferClass
                    {
                        Location = preCurrentLocation,
                        ProfileColor = currentProfileColor
                    };
                    undoBufferIndex++;
                    if (undoBufferIndex < undoBuffer.Count)
                    {
                        undoBuffer.RemoveRange(undoBufferIndex, undoBuffer.Count - undoBufferIndex);
                    }
                    if (undoBufferIndex > undoBuffer.Count)
                    {
                        undoBufferIndex = undoBuffer.Count;
                    }
                    undoBuffer.Add(newData);
                    btnGoToBack.Enabled = undoBufferIndex > 0;
                    btnGoToForward.Enabled = false;
                }
            }
        }

        public void UpdatePreview(string renamedLocation = null, bool stopResetFilter = true)
        {
            var loc = renamedLocation != null ? renamedLocation : preCurrentLocation;

            //var locChanged = loc != preCurrentLocation; // 厳密には renamedLocation == null とは条件が異なる
            //var locChanged = loc != zabLocation.Text; // アドレスバーは表示中のテキストからボタンを作るのでこちらがより適切
            var locChanged = loc != currentLocation; // 仕様変更によりこちらが適切に

            currentLocation = renamedLocation;
            if (string.IsNullOrEmpty(loc)) return;
            var zna = ZipNameArray;
            var index = tvCatalog.SelectedIndex;
            string fileName;
            if (index < 0 || zna == null || zna.Length <= index) fileName = null;
            else fileName = zna[index];
            if (string.IsNullOrEmpty(fileName)) fileName = null;

            //zabLocation.Text = loc;
            zabLocation.SetText(loc, makeButtons: locChanged, makeEvent: locChanged);

            //MakePreview(tvCatalog.ScrollBarPercentage, tvCatalog.SelectedIndex, fScrollbar: dgvFileList.FirstDisplayedScrollingRowIndex)
            MakePreview_StopResetFilter = stopResetFilter;
            MakePreview(tvCatalog.ScrollBarPercentage, selectedIndex: -1, /*tvCatalog.SelectedIndex*/ selectedFileName: fileName, fScrollbar: dgvFileList.FirstDisplayedScrollingRowIndex);
        }

        private bool ShouldBeSkipForFileAndDirWithCheck_Only_Attributes(string path, bool? ffmpeExists)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    //if (!IsDisplay(SubfolderMode)) return true;

                    return hasAttributeToSkip(new DirectoryInfo(path));
                }
                else
                {
                    return hasAttributeToSkip(new FileInfo(path));
                }
            }
            catch
            {
                // 想定外のドライブエラー
                return true;
            }
        }

        private bool ShouldBeSkippedForFileAndDirWithCheckAttributes(string path, bool? ffmpeExists)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    if (!IsDisplay(SubfolderMode)) return true;

                    return hasAttributeToSkip(new DirectoryInfo(path));
                }

                int dummy;
                var archiveSkip = ArchiveLoadLevel == ItemLoadLevel.Ignore;
                var imageSkip = ImageLoadLevel == ItemLoadLevel.Ignore;
                var videoSkip = VideoLoadLevel == ItemLoadLevel.Ignore;
                var otherSkip = OtherLoadLevel == ItemLoadLevel.Ignore;
                var result = ShouldBeSkipForFile(out dummy, path, archiveSkip, imageSkip, videoSkip, otherSkip, ffmpeExists);
                if (result)
                {
                    return true;
                }
                else
                {
                    return hasAttributeToSkip(new FileInfo(path));
                }
            }
            catch
            {
                // 想定外のドライブエラー
                return true;
            }
        }

        private bool ShouldBeSkipForFileAndDirWithCheckAttributes(string path, bool archiveSkip, bool imageSkip, bool videoSkip, bool otherSkip, bool? ffmpegExists, out bool becauseOfAtt)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    if (!IsDisplay(SubfolderMode))
                    {
                        becauseOfAtt = false;
                        return true;
                    }

                    becauseOfAtt = true;
                    return hasAttributeToSkip(new DirectoryInfo(path));
                }
            }
            catch
            {
                // 想定外のドライブエラー
                becauseOfAtt = false;
                return true;
            }

            int dummy;
            var result = ShouldBeSkipForFile(out dummy, path, archiveSkip, imageSkip, videoSkip, otherSkip, ffmpegExists);
            if (result)
            {
                becauseOfAtt = false;
                return true;
            }
            else
            {
                becauseOfAtt = true;
                try
                {
                    return hasAttributeToSkip(new FileInfo(path));
                }
                catch
                {
                    return true;
                }
            }
        }

        private bool ShouldBeSkipForFile(out int initialPageCount, string path, bool archiveSkip, bool imageSkip, bool videoSkip, bool otherSkip, bool? ffmpegExists)
        {
            initialPageCount = MetaPageCount_NotBook;
            var extensionWithPeriodInLowerCase = Path.GetExtension(path).ToLower();
            if (PackedImageLoader.Supports(path, extensionWithPeriodInLowerCase))
            {
                if (archiveSkip) return true;
                initialPageCount = MetaPageCount_NotLoaded;
            }
            else if (ImageLoader.SupportsImageAtLeastThumbnailReadingByExtension(extensionWithPeriodInLowerCase, ffmpegExists))
            {
                if (imageSkip) return true;
            }
            else if (MovieThumbnailLoader.SupportsByExtension(extensionWithPeriodInLowerCase))
            {
                if (videoSkip) return true;
            }
            else
            {
                if (otherSkip) return true;
            }
            return false;
        }

        private static bool hasAttributeToSkip(FileSystemInfo info)
        {
            FileAttributes attr;
            try
            {
                attr = info.Attributes;
            }
            catch
            {
                return true;
            }
            return hasAttributeToSkip(attr);
        }

        private static bool hasAttributeToSkip(FileAttributes attr)
        {
            return (attr & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }

        private void getDirectoryItemsInfo(
            //out string[] zipPathArrayDup,
            out CatalogItemArrays items,
            bool archiveSkip,
            bool imageSkip,
            bool videoSkip,
            bool otherSkip,
            int dirPathFullPathLengthPlusOne,
            bool isDirectory,
            FileSystemInfo[] fileInfos,
            int[] filePages,
            DirectoryInfo[] dirInfos,
            int[] folderPages)
        {
            var filePathsLength = fileInfos != null ? fileInfos.Length : 0;
            var folderPathsLength = dirInfos != null ? dirInfos.Length : 0;

            int[] MatchIndexArray = new int[filePathsLength + folderPathsLength];
            int[] initialPageCountArray = new int[filePathsLength];
            //bool[] IsImageFileArray = new bool[filePathsLength];
            int MatchFilesCount = 0;
            var ffmpegExists = MovieThumbnailLoader.ffmpegExists();
            for (var i = 0; i < filePathsLength; i++)
            {
                var fileInfo = fileInfos[i];
                var entityInfo = FileInfoEx.GetEntityFileInfo(fileInfo);
                var path = entityInfo.FullName;
                int initialPageCount;
                if (ShouldBeSkipForFile(out initialPageCount, path, archiveSkip, imageSkip, videoSkip, otherSkip, ffmpegExists))
                {
                    continue;
                }
                //if (!PackedImageLoader.SupportedArchivesPath.IsMatch(path) && !PackedImageLoader.IsSupertedImage(path)) continue;
                if (hasAttributeToSkip(entityInfo)) continue;

                //IsImageFileArray[MatchFilesCount] = isImageFile;
                MatchIndexArray[MatchFilesCount] = i;
                initialPageCountArray[MatchFilesCount] = initialPageCount;

                MatchFilesCount++;

                if (bwMakePreviewPrepare.CancellationPending)
                {
                    throw new TimeoutException();
                }
            }

            int MatchFilesAndFoldersCount = MatchFilesCount;
            for (var i = 0; i < folderPathsLength; i++)
            {
                var dirInfo = dirInfos[i];
                var path = dirInfo.FullName;

                if (hasAttributeToSkip(dirInfo)) continue;

                MatchIndexArray[MatchFilesAndFoldersCount++] = i;

                if (bwMakePreviewPrepare.CancellationPending)
                {
                    throw new TimeoutException();
                }
            }


            var MatchCountFolderPathsLength = MatchFilesAndFoldersCount; // MatchFilesCount + folderPathsLength;

            //zipPathArrayDup = new string[MatchCountFolderPathsLength * 2];

            var isNotDirectory = !isDirectory; //filePages != null && folderPages != null;

            items = CatalogItemArrays.Allocate(MatchCountFolderPathsLength);
            var zipPathArray = items.Path;
            var zipNameArray = items.Name;
            var zipPageArray = items.Page;
            var createTimeArray = items.CreateTime;
            var lastAccessTimeArray = items.LastAccessTime;
            var modifiedTimeArray = items.ModifiedTime;
            var fileCountArray = items.FileCount;
            var imageInfoArray = items.ImageInfo;
            var movieInfoArray = items.MovieInfo;
            var loadResultArray = items.LoadResult;
            var zipSizeArray = items.Size;
            var zipMaskArray = items.Mask;

            var filePagesExists = filePages != null;
            var folderPagesExists = folderPages != null;

            for (var i = 0; i < MatchFilesCount; i++)
            {
                var index = MatchIndexArray[i];
                var fileInfo = fileInfos[index];
                var entityInfo = FileInfoEx.GetEntityFileInfo(fileInfo);

                var path = fileInfo.FullName;
                //zipPathArrayDup[2 * i] = zipPathArrayDup[2 * i + 1] = 
                zipPathArray[i] = path;
                var name = isNotDirectory ? /*getFileName(path)*/ fileInfo.Name : path.Substring(dirPathFullPathLengthPlusOne);
                zipNameArray[i] = name;
                zipPageArray[i] = filePagesExists ? filePages[index] : -1;

                createTimeArray[i] = Program.GetCreateTime(entityInfo);
                lastAccessTimeArray[i] = Program.GetLastAccessTime(entityInfo);// fileInfo.LastAccessTime;
                modifiedTimeArray[i] = Program.GetLastWriteTime(entityInfo);// fileInfo.LastWriteTime;
                fileCountArray[i] = initialPageCountArray[i];
                loadResultArray[i] = LoadResult.NotYet;
                zipSizeArray[i] = entityInfo.Length;// -1;
                zipMaskArray[i] = true;

                if (bwMakePreviewPrepare.CancellationPending)
                {
                    throw new TimeoutException();
                }
            }
            for (var i = MatchFilesCount; i < MatchCountFolderPathsLength; i++)
            {
                var index = MatchIndexArray[i];
                var dirInfo = dirInfos[index];

                var path = dirInfo.FullName;
                //zipPathArrayDup[2 * i] = zipPathArrayDup[2 * i + 1] = 
                zipPathArray[i] = path;
                var name = isNotDirectory ? /*getFileName(path)*/ dirInfo.Name : path.Substring(dirPathFullPathLengthPlusOne);
                zipNameArray[i] = name + Path.DirectorySeparatorChar.ToString();
                zipPageArray[i] = folderPagesExists ? folderPages[index] : -1;

                createTimeArray[i] = Program.GetCreateTime(dirInfo);
                lastAccessTimeArray[i] = Program.GetLastAccessTime(dirInfo);// dirInfo.LastAccessTime;
                modifiedTimeArray[i] = Program.GetLastWriteTime(dirInfo);// dirInfo.LastWriteTime;
                fileCountArray[i] = -1;
                //ImageSizeArray[i] = Size.Empty;
                loadResultArray[i] = LoadResult.NotYet;
                zipSizeArray[i] = -1; // フォルダのサイズ取得は時間がかかるので GetThumbnail で行う Program.GetDirectoryFileSize(dirInfo);
                zipMaskArray[i] = true;

                if (bwMakePreviewPrepare.CancellationPending)
                {
                    throw new TimeoutException();
                }
            }
        }

        private string currentLocation = "";

        private string preCurrentLocation = "";

        enum LoadTargetType { Directory, VirtualFolder, SmartFolder, ImageArchive, Movie }

        private static string GetDirectoryPathWithSeparator(string dirPath)
        {
            return dirPath.EndsWith(Path.DirectorySeparatorChar.ToString()) ? dirPath : (dirPath + Path.DirectorySeparatorChar);
        }

        // 大量のファイルがある場合の GetFiles の待ち時間でユーザーにストレスを与えないためにバックグラウンド処理
        private void bwMakePreviewPrepare_DoWork(object sender, DoWorkEventArgs e)
        {
            tvCatalog.DrawStop = true;

            var archiveSkip = ArchiveLoadLevel == ItemLoadLevel.Ignore;
            var imageSkip = ImageLoadLevel == ItemLoadLevel.Ignore;
            var videoSkip = VideoLoadLevel == ItemLoadLevel.Ignore;
            var otherSkip = OtherLoadLevel == ItemLoadLevel.Ignore;


            var dirPath = (string)e.Argument;
            var dirPathWithSeparator = GetDirectoryPathWithSeparator(dirPath);

            // Path.GetFullPath("C:") がカレントディレクトリを返すのでこの方法は使えない
            //var dirPathFullPathLengthPlusOne = Path.GetFullPath(dirPath).Length + 1;
            var dirFullPathWithSeparator = Path.GetFullPath(dirPathWithSeparator);
            var dirPathFullPathLengthPlusOne = dirFullPathWithSeparator.Length;

            var subfolderMode = SubfolderMode;// (new GeneralConfig()).SubfolderMode;

            string[] filePaths = null, folderPaths = null;
            FileSystemInfo[] fileInfos = null;
            int[] filePages = null;
            int filePathsLength = 0;
            DirectoryInfo[] dirInfos = null;
            int[] folderPages = null;
            int folderPathsLength = 0;
            LoadTargetType type;
            try
            {
                if (File.Exists(dirPath))
                {
                    if (VirtualFolder.IsBookmarkPath(dirPath))
                    {
                        type = LoadTargetType.VirtualFolder;
                        VirtualFolder.GetBookmarkFiles(out filePaths, out filePages, out folderPaths, out folderPages, dirPath);
                        if (bwMakePreviewPrepare.CancellationPending)
                        {
                            return;
                        }

                        if (dirPath != Program.HistorySorPath) filePages = folderPages = null;

                        // ファイルが存在しない場合に FileInfo.Attribute がフルビット(-1)を返し、
                        // システムファイルのビットが立っていればそのファイルをスキップする動作のため
                        // 存在しないファイルをスキップする処理を行わなくても「たまたま」ほとんどの場合に問題は起こらないが、
                        // それに頼るのは明らかに不適切な上、FileInfo.Atrribute はフォルダの属性も取得するため
                        // ファイルが存在しないが同名のフォルダが存在する状況では明確に動作不良を起こすため明示的なスキップ処理は必要
                        var newFiles = new List<string>(filePaths.Length);
                        var newFilePages = filePages == null ? null : new List<int>(filePages.Length);
                        for (var i = 0; i < filePaths.Length; i++)
                        {
                            if (File.Exists(filePaths[i]))
                            {
                                newFiles.Add(filePaths[i]);
                                if (newFilePages != null) newFilePages.Add(filePages[i]);
                            }
                        }
                        if (newFiles.Count < filePaths.Length)
                        {
                            filePaths = newFiles.ToArray();
                            filePages = newFilePages?.ToArray();
                        }
                        newFiles = null;
                        newFilePages = null;

                        var newFolders = new List<string>(folderPaths.Length);
                        var newFolderPages = folderPages == null ? null : new List<int>(folderPages.Length);
                        for (var i = 0; i < folderPaths.Length; i++)
                        {
                            if (Directory.Exists(folderPaths[i]))
                            {
                                newFolders.Add(folderPaths[i]);
                                if (newFolderPages != null) newFolderPages.Add(folderPages[i]);
                            }
                        }
                        if (newFolders.Count < folderPaths.Length)
                        {
                            folderPaths = newFolders.ToArray();
                            folderPages = newFolderPages?.ToArray();
                        }
                        newFolders = null;
                        newFolderPages = null;


                        filePathsLength = filePaths.Length;
                        folderPathsLength = folderPaths.Length;
                        fileInfos = new FileSystemInfo[filePathsLength];
                        for (var i = 0; i < filePathsLength; i++)
                        {
                            fileInfos[i] = new FileInfoEx(filePaths[i]);
                            if ((i & 15) == 15 && bwMakePreviewPrepare.CancellationPending)
                            {
                                return;
                            }
                        }
                        dirInfos = new DirectoryInfo[folderPathsLength];
                        for (var i = 0; i < folderPathsLength; i++)
                        {
                            dirInfos[i] = new DirectoryInfo(folderPaths[i]);
                            if ((i & 15) == 15 && bwMakePreviewPrepare.CancellationPending)
                            {
                                return;
                            }
                        }

                        //fileInfos = (from path in filePaths select new FileInfo(path)).ToArray();
                        //dirInfos = (from path in folderPaths select new DirectoryInfo(path)).ToArray();
                        if (bwMakePreviewPrepare.CancellationPending)
                        {
                            return;
                        }
                    }
                    else if (SmartFolder.IsSmartFolderPath(dirPath))
                    {
                        //var searchPatternForFiles = GetFileFilter(archiveSkip, imageSkip, videoSkip, otherSkip);
                        const string searchPatternForFiles = "*";

                        type = LoadTargetType.SmartFolder;
                        var searchOption = SubfolderIsSearched(subfolderMode) ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                        var smartFolder = new SmartFolder(dirPath);
                        fileInfos = smartFolder.GetFiles(
                            searchPatternForFiles,
                            searchOption: searchOption,
                            bw: bwMakePreviewPrepare);
                        if (bwMakePreviewPrepare.CancellationPending)
                        {
                            return;
                        }
                        filePathsLength = fileInfos.Length;
                        if (IsDisplay(subfolderMode))
                        {
                            dirInfos = smartFolder.GetDirectories(searchPattern: "*", searchOption: searchOption, bw: bwMakePreviewPrepare);
                            if (bwMakePreviewPrepare.CancellationPending)
                            {
                                return;
                            }
                            folderPathsLength = dirInfos.Length;
                        }
                    }
                    else if (PackedImageLoader.Supports(dirPath))
                    {
                        type = LoadTargetType.ImageArchive;
                    }
                    else if (MovieThumbnailLoader.Supports(dirPath))
                    {
                        type = LoadTargetType.Movie;
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }
                }
                else
                {
                    //var searchPatternForFiles = GetFileFilter(archiveSkip, imageSkip, videoSkip, otherSkip);
                    const string searchPatternForFiles = "*";

                    //sw.Restart();

                    type = LoadTargetType.Directory;
                    var dirInfo = new DirectoryInfo(dirPathWithSeparator);
                    var subfolderIsSearched = SubfolderIsSearched(subfolderMode);
                    if (subfolderIsSearched)
                    {
                        // フォルダも表示する場合は、ファイルと一緒にサブフォルダも 1 回の列挙で取る
                        var dirList = IsDisplay(subfolderMode) ? new List<DirectoryInfo>() : null;
                        fileInfos = Program.GetFilesInAllDirectoriesOnErrorResumeNext(dirInfo, bwMakePreviewPrepare, dirList);
                        if (dirList != null) dirInfos = dirList.ToArray();
                    }
                    else if (IsDisplay(subfolderMode))
                    {
                        // ファイルとフォルダを 1 回の列挙で取る（以前は GetFiles と GetDirectories で 2 回列挙していた、K10）
                        var all = dirInfo.GetFileSystemInfos(searchPatternForFiles, SearchOption.TopDirectoryOnly);
                        var fileList = new List<FileInfo>(all.Length);
                        var dirList = new List<DirectoryInfo>();
                        foreach (var info in all)
                        {
                            if (info is FileInfo fi) fileList.Add(fi);
                            else if (info is DirectoryInfo di) dirList.Add(di);
                        }
                        fileInfos = fileList.ToArray();
                        dirInfos = dirList.ToArray();
                    }
                    else
                    {
                        fileInfos = dirInfo.GetFiles(searchPatternForFiles, SearchOption.TopDirectoryOnly);
                    }
                    filePathsLength = fileInfos.Length;

                    //sw.Stop(); ss += $"GetFiles:\t{sw.Elapsed}\n";


                    if (bwMakePreviewPrepare.CancellationPending)
                    {
                        return;
                    }

                    if (IsDisplay(subfolderMode))
                    {
                        if (dirInfos == null) // 上でファイルと一緒に取っていなければ
                        {
                            dirInfos = dirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly);
                        }
                        folderPathsLength = dirInfos.Length;

                        if (bwMakePreviewPrepare.CancellationPending)
                        {
                            return;
                        }
                    }
                }
            }
            catch (Exception error)
            {
                try
                {
                    Invoke((MethodInvoker)(() =>
                    {
                        cancelLastUndoBufferChange();

#if DEBUG
                        if (MessageBox.Show(this, error.ToString(), null, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error) == DialogResult.Retry)
#else
                        if (MessageBox.Show(this, error.Message, null, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error) == DialogResult.Retry)
#endif
                        {
                            //MakePreview(tScrollbar: MakePreview_tScrollbar, selectedIndex: MakePreview_selectedIndex, selectedFileName: MakePreview_selectedFileName,
                            //    fScrollbar: MakePreview_fScrollbar, subdivideInterval: MakePreview_subdivideInterval, needToSynchroTree: MakePreview_needToSynchroTree);
                            // Busy が分かりきっている＋Busy による先延ばしは UndoBuffer を更新するのでここでは明示的な予約をする


                            MakePreview_Que = true;
                        }
                        else
                        {
                            if (currentLocation != preCurrentLocation)
                            {
                                preCurrentLocation = currentLocation;
                                zabLocation.Text = currentLocation;
                            }
                        }
                        //cancelLastUndoBufferChange(); // makePreview よりも先に
                        zabLocation.Enabled = true;
                        tvCatalog.DrawStop = false;
                        tvCatalog.Invalidate();
                    }
              ));
                }
                catch (ObjectDisposedException) { }
                return;
            }

            //string[] ZipPathArrayDup = null;

            if (type == LoadTargetType.ImageArchive)
            {
                PackedImageLoaderWithEntries loader;
                try
                {
                    var pureLoader = CreatePackedImageLoader(dirPath);
                    try
                    {
                        loader = new PackedImageLoaderWithEntries(pureLoader);
                    }
                    catch
                    {
                        pureLoader.Dispose();
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        Invoke((MethodInvoker)(() =>
                        {
                            MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            cancelLastUndoBufferChange();
                            zabLocation.Enabled = true;
                            tvCatalog.DrawStop = false;
                            tvCatalog.Invalidate();
                        }
                  ));
                    }
                    catch (ObjectDisposedException) { }
                    return;
                }

                var entries = loader.Entries;
                var fileCount = entries.Length;

                //ZipPathArrayDup = new string[fileCount * 2];

                lock (ZipPathArrayLocker)
                {
                    lock (loadingPackedImageLoaderLocker)
                    {
                        if (loadingPackedImageLoader != null)
                        {
                            loadingPackedImageLoader.Dispose();
                            loadingPackedImageLoader = null;
                        }
                        loadingPackedImageLoader = loader;
                    }

                    SetItemArrays(CatalogItemArrays.Allocate(fileCount));

                    for (var i = 0; i < fileCount; i++)
                    {
                        var entry = entries[i];
                        var name = entry.Path;

                        //ZipPathArrayDup[2 * i] = ZipPathArrayDup[2 * i + 1] =
                        ZipPathArray[i] =
                            dirFullPathWithSeparator.Substring(0, dirFullPathWithSeparator.Length - 1) + Path.AltDirectorySeparatorChar + name;
                        ZipNameArray[i] = name;
                        ZipPageArray[i] = -1;

                        CreateTimeArray[i] = DateTime.MinValue;
                        LastAccessTimeArray[i] = DateTime.MinValue;
                        ModifiedTimeArray[i] = entry.LastWriteTime;
                        FileCountArray[i] = MetaPageCount_NotBook;
                        LoadResultArray[i] = LoadResult.NotYet;
                        ZipSizeArray[i] = entry.Size;
                        ZipMaskArray[i] = true;
                    }
                }
            }

            else if (type == LoadTargetType.Movie)
            {
                TimeSpan duration;
                MovieInfo movieInfo;
                try
                {
                    movieInfo = MovieInfo.FromFile(dirPath);
                    duration = movieInfo.Duration;
                    if (duration == default(TimeSpan))
                    {
                        throw new InvalidDataException();
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        Invoke((MethodInvoker)(() =>
                        {
#if DEBUG
                            MessageBox.Show(this, ex.ToString(), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                            MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
                            cancelLastUndoBufferChange();
                            zabLocation.Enabled = true;
                            tvCatalog.DrawStop = false;
                            tvCatalog.Invalidate();
                        }
                  ));
                    }
                    catch (ObjectDisposedException) { }
                    return;
                }

                //const double minimumMovieThumbnailInterval = 1.0;
                //const int maximumMovieThumbnailCount = 60;
                int maximumMovieThumbnailCount = MaxCaptchaCountInAVideo <= 0 ? Math.Max(5, GetThumbnailCountInTvCatalogOrFileList(movieInfo)) : MaxCaptchaCountInAVideo;

                int thumbnailCount;
                double a, b;
                var subdivideInterval = MakePreview_subdivideInterval; // 右辺はメインスレッドで書き換えられる可能性あり
                if (subdivideInterval == null)
                {
                    var totalSeconds = duration.TotalSeconds;
                    var intTotalSeconds = (int)totalSeconds;
                    thumbnailCount = Math.Min(intTotalSeconds, maximumMovieThumbnailCount);
                    if (thumbnailCount > 0)
                    {
                        a = totalSeconds / thumbnailCount;
                        b = a / 2 + 0.5; // 両端を除く
                    }
                    else
                    {
                        thumbnailCount = 1;
                        a = 0;
                        b = 1; // Ceiling 用。Floor を使う場合は 0
                    }
                }
                else
                {
                    if (CoverSettingMode) ThumbnailSettingToCatalog_CoverSettingModeAtOnce = true;

                    var startTime = Math.Min(Math.Max(0, subdivideInterval.Item1.TotalSeconds), duration.TotalSeconds);
                    var totalSeconds = Math.Min(duration.TotalSeconds, subdivideInterval.Item2.TotalSeconds) - startTime;
                    var intTotalSeconds = (int)totalSeconds;


                    // thumbnailCount > 0 の場合のインクリメントで maximumMovieThumbnailCount を超えてしまう

                    //修正版
                    if (intTotalSeconds == 0)
                    {
                        thumbnailCount = 1;
                        a = 0;
                        b = 1; // Ceiling 用。Floor を使う場合は 0
                    }
                    else
                    {
                        thumbnailCount = Math.Min(intTotalSeconds, maximumMovieThumbnailCount - 1);
                        a = totalSeconds / thumbnailCount;
                        b = startTime + 0.5; // 両端を含む
                        thumbnailCount++; // 両端を含む
                    }

                }

                //ZipPathArrayDup = new string[thumbnailCount * 2];

                lock (ZipPathArrayLocker)
                {
                    lock (loadingPackedImageLoaderLocker)
                    {
                        if (loadingPackedImageLoader != null)
                        {
                            loadingPackedImageLoader.Dispose();
                            loadingPackedImageLoader = null;
                        }
                    }

                    SetItemArrays(CatalogItemArrays.Allocate(thumbnailCount));


                    for (var i = 0; i < thumbnailCount; i++)
                    {
                        //var name = TimeSpan.FromSeconds(Math.Floor(a * i + b)).ToString(); // 四捨五入
                        var name = TimeSpan.FromSeconds(Math.Ceiling(a * i + b - 1)).ToString(); // 半整数は切り下げ

                        // 半整数は切り下げないと、a = 1 で b が半整数の場合に最後の一枚が切り上げられてサムネイル作成に失敗することがある
                        // この場合 a = 1 では 0 秒のサムネイルが作成されるが、これは失敗しない

                        //ZipPathArrayDup[2 * i] = ZipPathArrayDup[2 * i + 1] = 
                        ZipPathArray[i] =
                            dirFullPathWithSeparator.Substring(0, dirFullPathWithSeparator.Length - 1) + Path.AltDirectorySeparatorChar + name;
                        ZipNameArray[i] = name;
                        ZipPageArray[i] = -1;

                        CreateTimeArray[i] = DateTime.MinValue;
                        LastAccessTimeArray[i] = DateTime.MinValue;
                        ModifiedTimeArray[i] = DateTime.MinValue;
                        FileCountArray[i] = MetaPageCount_NotBook;
                        LoadResultArray[i] = LoadResult.NotYet;
                        ZipSizeArray[i] = -1;
                        ZipMaskArray[i] = true;

                        MovieInfoArray[i] = movieInfo;
                    }
                }
            }

            else if (type == LoadTargetType.Directory || type == LoadTargetType.VirtualFolder || type == LoadTargetType.SmartFolder)
            {
                //////////////////////////////////

                if ((!archiveSkip || IsDisplay(subfolderMode)) &&
                    LoadLastViewedPageFromHistory && string.Compare(dirPath, Program.HistorySorPath, ignoreCase: true) != 0 && File.Exists(Program.HistorySorPath))
                {
                    if (filePaths == null)
                    {
                        filePaths = new string[filePathsLength];
                        for (var i = 0; i < filePathsLength; i++)
                        {
                            filePaths[i] = fileInfos[i].FullName;
                        }
                    }
                    if (folderPaths == null)
                    {
                        folderPaths = new string[folderPathsLength];
                        for (var i = 0; i < folderPathsLength; i++)
                        {
                            folderPaths[i] = dirInfos[i].FullName;
                        }
                    }

                    VirtualFolder.GetPageFromBookmark(out filePages, out folderPages, filePaths, folderPaths, Program.HistorySorPath);
                }

                CatalogItemArrays items;

                try
                {
                    //sw.Start();
                    getDirectoryItemsInfo(
                        //out ZipPathArrayDup,
                        out items,
                        archiveSkip,
                        imageSkip,
                        videoSkip,
                        otherSkip,
                        dirPathFullPathLengthPlusOne,
                        type == LoadTargetType.Directory,
                        fileInfos,
                        filePages,
                        dirInfos,
                        folderPages);
                    //sw.Stop(); ss += $"GetDirInfo:\t{sw.Elapsed}\n";
                    //MessageBox.Show($"{PackedImageLoader.Supports(".ZIP")}\n{PackedImageLoader.Supports("aaa")}\n{ss}");
                }
                catch (TimeoutException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    try
                    {
                        Invoke((MethodInvoker)(() =>
                        {
#if DEBUG
                            MessageBox.Show(this, ex.ToString(), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                            MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
                            cancelLastUndoBufferChange();
                            zabLocation.Enabled = true;
                            tvCatalog.DrawStop = false;
                            tvCatalog.Invalidate();
                        }
                  ));
                    }
                    catch (ObjectDisposedException) { }
                    return;
                }


                if (bwMakePreviewPrepare.CancellationPending)
                {
                    return;
                }


                lock (ZipPathArrayLocker)
                {

                    lock (loadingPackedImageLoaderLocker)
                    {
                        if (loadingPackedImageLoader != null)
                        {
                            loadingPackedImageLoader.Dispose();
                            loadingPackedImageLoader = null;
                        }
                    }

                    SetItemArrays(items);
                }


                ////////////
            }
            else
            {
                try
                {
                    Invoke((MethodInvoker)(() =>
                    {
                        MessageBox.Show(this, (new NotSupportedException()).Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        cancelLastUndoBufferChange();
                        zabLocation.Enabled = true;
                        tvCatalog.DrawStop = false;
                        tvCatalog.Invalidate();
                    }
              ));
                }
                catch (ObjectDisposedException) { }
                return;
            }
            //sw.Stop();
            //MessageBox.Show($"{sw.Elapsed} {Image.GetPixelFormatSize(0)}");

            e.Result = true; // completed に成功を伝え、Creating を false にさせない

            try
            {
                Invoke((MethodInvoker)(() =>
                {
                    currentLocation = dirPath;
                    //addToolStripMenuItem.Enabled = locationValid();
                    currentClipMode = clipModeByControl;
                    currentBindingMode = bindingModeByControl;

                    //DefaultLeftToRight = SettingForm.LeftToRight;
                    InArchive = type == LoadTargetType.ImageArchive; // directoryは含まない。これによって currentLocation のリネームで対象がファイルであることを担保しているので不用意に変更しないこと
                    InMovie = type == LoadTargetType.Movie;
                    InRealDirectory = type == LoadTargetType.Directory;
                    InVirtualDirectory = type == LoadTargetType.VirtualFolder;
                    InSmartDirectory = type == LoadTargetType.SmartFolder;
                    InDirectoryVirtualDirectoryOrSmartDirectory = InRealDirectory || InVirtualDirectory || InSmartDirectory;
                    InDocument = InArchive && dirPath.ToLower().EndsWith(".pdf"); // 現在の実装では InArchive と等価
                    LocationZipPlaInfo = dirPath.Contains(Path.DirectorySeparatorChar) ? new ZipPlaInfo(dirPath) : null; // ルートは取得できない

                    cbFilter_Refresh(cbFilter.Text);

                    initialZipPathLength = ZipPathArray.Length;

                    while (cbLocation.Items.Contains(dirPath)) cbLocation.Items.Remove(dirPath);

                    cbLocation.Items.Insert(0, dirPath);
                    TrimHistoryItems(cbLocation.Items, LocationHistoryLimit); // 古いものから捨てる（S1）
                    zabLocation.Text = dirPath;

                    //bmwMakePreview.RunWorkerAsyncWithInterrupt(ZipPathArrayDup, false);
                    bmwMakePreview.RunWorkerAsyncWithInterrupt(new object[2 * initialZipPathLength], false);

                    Text = $"{SimpleBookmark.GetDisplayName(dirPath)} - {Program.Name}";
                }));
            }
            catch (ObjectDisposedException) { }
        }

        private void cancelLastUndoBufferChange()
        {
            var last = undoBuffer.Count - 1;
            if (last >= 0 && last == undoBufferIndex)
            {
                undoBuffer.RemoveAt(undoBufferIndex);
                undoBufferIndex--;
                btnGoToBack.Enabled = undoBufferIndex > 0;
            }
        }

        int initialZipPathLength;

        public bool InArchive, InMovie, InDocument, InRealDirectory, InVirtualDirectory, InSmartDirectory, InDirectoryVirtualDirectoryOrSmartDirectory;

        ZipPlaInfo LocationZipPlaInfo;

        bool CoverSettingMode;

        private static string getFileName(string path)
        {
            var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
            var sepPos = altPos >= 0 ? altPos : path.LastIndexOf(Path.DirectorySeparatorChar);
            if (sepPos >= 0)
            {
                if (sepPos < path.Length - 1) return path.Substring(sepPos + 1);
            }
            return path;
        }

        private void bwMakePreviewPrepare_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Program.AlertError(e.Error);
            if (e.Result as bool? != true) CreatingZipPathArray = false;
            if (MakePreview_Que && !CatalogForm_FormClosing_FormClosed)
            {
                MakePreview_Que = false;
                MakePreview(tScrollbar: MakePreview_tScrollbar, selectedIndex: MakePreview_selectedIndex,
                    selectedFileName: MakePreview_selectedFileName, fScrollbar: MakePreview_fScrollbar, subdivideInterval: MakePreview_subdivideInterval, needToSynchroTree: MakePreview_needToSynchroTree);


                //private void MakePreview(double tScrollbar = 0, int selectedIndex = -1, string selectedFileName = null, int manualUndoShift = 0, bool resetBookmark = true,
                //    int fScrollbar = -1, Tuple<Color, CatalogProfile> prevProfileInfo = null, Tuple<TimeSpan, TimeSpan> subdivideInterval = null, bool needToSynchroTree = true)
            }
        }

        //private readonly Dictionary<string, FileSystemWatcher> folderWatchers = new Dictionary<string, FileSystemWatcher>();

        //private static readonly Color sorColor = Color.LightGreen;
        private void bmwMakePreview_RunWorkerStarting(object sender, BackgroundMultiWorker.RunWorkerStartingEventArgs e)
        {
#if PERFLOG
            MakePreviewPerfLog.Start(e.Arguments?.Cast<object>().Count() ?? -1);
#endif
            resetDeferredHeaders(e.WorkSetGuid); // K33（下の SetBackgroundMode が表示中の項目を設定する）
            //var sw = new Stopwatch();
            //var ss = "";

            //sw.Restart();


            SetFileListPageHeaderWidth();

            CreatingZipPathArray = false;
            int selectedIndex;
            double tScrollbar;
            int fScrollbar;
            var selectByName = !string.IsNullOrEmpty(MakePreview_selectedFileName);

            CoverSettingMode = ThumbnailSettingToCatalog_CoverSettingModeAtOnce && InMovie;

            ThumbnailSettingToCatalog();

            setCutFiles();
            addOrReloadItem_UserAddFileInLower = null;

            lock (ZipPathArrayLocker)
            {
                if (!selectByName)
                {
                    selectedIndex = MakePreview_selectedIndex;
                }
                else
                {
                    selectedIndex = Array.IndexOf(ZipNameArray, MakePreview_selectedFileName);
                }
                tScrollbar = MakePreview_tScrollbar;
                fScrollbar = MakePreview_fScrollbar;

                if (NumberOfLinesOfName == NumberOfLinesOfName.Full)
                {
                    tvCatalog.SetTextLinesFromText(getMaxLengthString(ZipNameArray), true);
                }

                var len = ZipNameArray.Length;

                //sw.Stop(); ss += $"sortStart:\t{sw.Elapsed}\n"; sw.Restart();
                var sortedArray = GetSortArray(preSortArray: null, careTvCatalog: false);
                //sw.Stop(); ss += $"srtEnd:\t{sw.Elapsed}\n"; sw.Restart();
                tvCatalog.ThumbnailAppearanceKey = getThumbnailAppearanceKey(); // 同じならメモリキャッシュを残す（K26）
                tvCatalog.Clear(len, selectedIndex, tScrollbar, sortedArray, ChangeThumbnailSize_Width, ChangeThumbnailSize_Height, Program.DpiScaling(ChangeThumbnailSize_Padding * currentMinimumFrameThickness));
                sortedArray = null;

                //sw.Stop(); ss += $"clearEnd:\t{sw.Elapsed}\n"; sw.Restart();
                for (var i = 0; i < len; i++)
                {
                    var item = new ThumbViewerItem()
                    { 
                        OwnerCatalog = this, 
                        FilePath = ZipPathArray[i],
                        Text = ZipNameArray[i],
                    };
                    if (i == selectedIndex) item.selected = true;
                    //tvCatalog[i] = item;
                    tvCatalog.SilentSet(i, item);
                }

                //sw.Stop(); ss += $"setEnd:\t{sw.Elapsed}\n"; sw.Restart();

                tvCatalog.DrawStop = false;
                tvCatalog.Update();

                // 背景色を変えるならここで記述
                //tvCatalog.BackColor = XXX ? sorColor : WindowColor;

                // 仮想フォルダなどを開いた状態でサムネイルキャッシュが更新された場合の項目を削除するため
                // そもそもブラックリストに登録しないという実装もあり得るが、こちらの方が安定した動作が期待される
                GPSizeThumbnail.ClearBlackList();

                //sw.Stop(); ss += $"waitStart:\t{sw.Elapsed}\n"; sw.Restart();
                //sw.Stop(); ss += $"waitStop:\t{sw.Elapsed}\n"; sw.Restart();

                //sw.Stop(); ss += $"flStart:\t{sw.Elapsed}\n"; sw.Restart();
                setFileListFromThumbnailViewer();
                //sw.Stop(); ss += $"flEnd:\t{sw.Elapsed}\n"; sw.Restart();
            }


            if (selectByName && selectedIndex >= 0 && tScrollbar < 0)
            {
                tvCatalog.ScrollBarToIndexWithMinimalMove(tvCatalog.DataIndexToShowIndex[selectedIndex]);
            }
            else
            {
                if (fScrollbar >= 0 && fScrollbar < dgvFileList.Rows.Count) dgvFileList.FirstDisplayedScrollingRowIndex = fScrollbar;
            }

            loadCountToolStripMenuItem.Text = 0 + " / " + ZipPathArray.Length;

            // cbLocation.Enabled = false 中の変化に対応
            //InitCursorIsOnControllField();

            SetBackgroundMode(true, 0, true);

            zabLocation.Enabled = true;

            ShowStatusBar();

            // 先読み状態を解除
            StopLookAheadProcess();

            SetLookAheadArchive_PrevDataIndex = -1;

            setTagFilteringMenuItems(needToRepairTagsFromBookmark: false, needToSaveCurrentTag: true);


            SetLookAheadArchive_PrevDataIndex = -1;

            bmwMakePreviewRunWorkerStartingFinalAction?.Invoke();

            loadingGuid = e.WorkSetGuid;
            //sw.Stop(); ss += $"fin:\t{sw.Elapsed}\n";
            //MessageBox.Show(ss);

        }

        // 今の読み込みの識別子。ヘッダの読み込みのワーカーからも読まれる。Guid は 16 バイトで 1 回の読み書きにならず、
        // 書き換えの途中を読むと壊れた値になりうるので、箱に入れて参照（volatile）で読み書きする
        private volatile object loadingGuidBox = Guid.Empty;
        private Guid loadingGuid
        {
            get => (Guid)loadingGuidBox;
            set => loadingGuidBox = value;
        }

        // ---- 後回しにしたヘッダの読み込み（K33） ----
        // 以前はフォルダを開くと全項目のヘッダ（ページ数・解像度・動画情報など）を取っていて、表示中のサムネイルが揃ったあとも
        // HDD を読み続けていた（画像 3200 件で 13 秒、小さな zip 5000 件で 47 秒）。ヘッダが見えていない項目に要るのは
        // ファイル一覧の列（ページ数・解像度など）で並べ替えるときだけなので、見えていない項目は後回しにし、
        // 見えたとき（サムネイルの描画・一覧の行の表示）と列で並べ替えるときに取る。
        private readonly object headerDeferLock = new object();

        private HashSet<string> headerDeferred = new HashSet<string>(); // 後回しにした項目のパス。headerDeferLock の中で読み書きする

        private Guid headerDeferGuid = Guid.Empty;

        private volatile HashSet<int> headerShownSet = null; // 表示中の項目（データのインデックス）。SetBackgroundModeCore が作り直す。null なら全部取る

        private volatile bool headerWantAll = false; // 列で並べ替えるために全項目のヘッダが要る

        private readonly List<KeyValuePair<int, string>> headerRequests = new List<KeyValuePair<int, string>>(); // UI スレッドだけ。末尾から取る

        private int headerLoadersActive = 0; // UI スレッドだけ

        private bool headerPumpScheduled = false; // UI スレッドだけ

        private sealed class PendingVolatileSort
        {
            public DataGridViewColumn Column;
            public SortOrder Direction;
            public SortMode SortMode;
            public Guid Guid;
        }

        private PendingVolatileSort pendingVolatileSort = null; // ヘッダが揃ったら並べ直す列の並べ替え。UI スレッドだけ

        private void resetDeferredHeaders(Guid workSetGuid)
        {
            lock (headerDeferLock)
            {
                headerDeferred = new HashSet<string>();
                headerDeferGuid = workSetGuid;
            }
            headerShownSet = null;
            headerWantAll = false;
            headerRequests.Clear();
            pendingVolatileSort = null;
        }

        /// <summary>後回しにしていたら、その項目のヘッダを取る（UI スレッドで呼ぶ。描画中に呼んでよい）</summary>
        private void requestDeferredHeader(int dataIndex)
        {
            var zpa = ZipPathArray;
            if (zpa == null || dataIndex < 0 || dataIndex >= zpa.Length) return;
            var path = zpa[dataIndex];
            if (path == null) return;
            lock (headerDeferLock)
            {
                if (headerDeferred.Count == 0 || headerDeferGuid != loadingGuid || !headerDeferred.Remove(path)) return;
            }
            headerRequests.Add(new KeyValuePair<int, string>(dataIndex, path));
            scheduleHeaderPump();
        }

        /// <summary>
        /// 後回しにした全項目のヘッダを取る（列で並べ替えるとき）。読み込みが残っていれば true
        /// </summary>
        private bool requestAllDeferredHeaders()
        {
            headerWantAll = true; // まだ始まっていないジョブも後回しにしない
            HashSet<string> deferred;
            lock (headerDeferLock)
            {
                deferred = headerDeferred;
                if (headerDeferGuid != loadingGuid) deferred = null;
                headerDeferred = new HashSet<string>();
            }
            var zpa = ZipPathArray;
            if (deferred != null && deferred.Count > 0 && zpa != null)
            {
                // 表示順に取る（末尾から取り出すので逆順に積む）。すでに積んである見えている項目は、その後ろ（先に取る）へ回す
                var visible = headerRequests.ToArray();
                headerRequests.Clear();
                var s2d = tvCatalog.ShowIndexToDataIndex;
                var added = new bool[zpa.Length];
                var list = new List<KeyValuePair<int, string>>(deferred.Count);
                if (s2d != null)
                {
                    foreach (var di in s2d)
                    {
                        if (di >= 0 && di < zpa.Length && !added[di] && deferred.Contains(zpa[di])) { added[di] = true; list.Add(new KeyValuePair<int, string>(di, zpa[di])); }
                    }
                }
                for (var i = 0; i < zpa.Length; i++) // 絞り込みで隠れている項目
                {
                    if (!added[i] && deferred.Contains(zpa[i])) list.Add(new KeyValuePair<int, string>(i, zpa[i]));
                }
                list.Reverse();
                headerRequests.AddRange(list);
                headerRequests.AddRange(visible);
                scheduleHeaderPump();
            }
            return headerRequests.Count > 0 || headerLoadersActive > 0 || bmwMakePreview.IsBusy;
        }

        private void scheduleHeaderPump()
        {
            if (headerPumpScheduled) return;
            headerPumpScheduled = true;
            try
            {
                // 描画中に呼ばれても結果の反映が描画の中で起きないよう、メッセージループに戻ってから始める
                BeginInvoke((MethodInvoker)(() => { headerPumpScheduled = false; pumpHeaderRequests(); }));
            }
            catch (InvalidOperationException) { headerPumpScheduled = false; } // ハンドルがない（閉じている）
        }

        private bool headerRequestsOnSeekPenaltyDrive()
        {
#if PERFLOG
            // 計測用: 環境変数 ZIPPLA_ASSUME_HDD=1 で HDD とみなす、0 でみなさない
            var forced = Environment.GetEnvironmentVariable("ZIPPLA_ASSUME_HDD");
            if (forced == "1") return true;
            if (forced == "0") return false;
#endif
            if (headerRequests.Count == 0) return false;
            return StorageInfo.HasSeekPenalty(headerRequests[headerRequests.Count - 1].Value) == true;
        }

        private void pumpHeaderRequests()
        {
            // 見えている分は 1 本（HDD のシークを増やさない）。列で並べ替えるときは bmwMakePreview と同じだけ並列に
            // ZipPla CE: HDD（シークに時間のかかるドライブ）では、並べ替えのために全部取るときも 1 本にする（並べて読むとシークが増えるだけ）
            var max = headerWantAll ? (headerRequestsOnSeekPenaltyDrive() ? 1 : Math.Max(2, Environment.ProcessorCount - 1)) : 1;
            while (headerLoadersActive < max && headerRequests.Count > 0)
            {
                var last = headerRequests.Count - 1;
                var request = headerRequests[last];
                headerRequests.RemoveAt(last);
                // スクロールで通り過ぎただけの項目は後回しに戻す（また見えたら描画のときに取る）
                if (!headerWantAll && !isShownNow(request.Key, request.Value))
                {
                    lock (headerDeferLock) { if (headerDeferGuid == loadingGuid) headerDeferred.Add(request.Value); }
                    continue;
                }
                headerLoadersActive++;
                _ = loadDeferredHeaderAsync(request.Key, request.Value, loadingGuid);
            }
            if (headerLoadersActive == 0 && headerRequests.Count == 0) tryFinishPendingVolatileSort();
        }

        /// <summary>項目がサムネイル一覧かファイル一覧に今表示されているか（UI スレッドで呼ぶ）</summary>
        private bool isShownNow(int dataIndex, string path)
        {
            var zpa = ZipPathArray;
            var d2s = tvCatalog.DataIndexToShowIndex;
            if (zpa == null || d2s == null || dataIndex < 0 || dataIndex >= zpa.Length || dataIndex >= d2s.Length || zpa[dataIndex] != path) return true; // 判断できなければ取る
            var showIndex = d2s[dataIndex];
            if (showIndex < 0) return false;
            if (pnlCenter.Visible && showIndex >= tvCatalog.DisplayedStartShowIndex && showIndex < tvCatalog.DisplayedStopShowIndex) return true;
            if (dgvFileList.Visible)
            {
                var first = dgvFileList.FirstDisplayedScrollingRowIndex;
                if (first >= 0 && showIndex >= first && showIndex < first + dgvFileList.DisplayedRowCount(includePartialRow: true)) return true;
            }
            return false;
        }

        private async Task loadDeferredHeaderAsync(int dataIndex, string path, Guid guid)
        {
            HeaderResult result = null;
            try
            {
                result = await Task.Run(() =>
                {
                    if (guid != loadingGuid || CatalogForm_FormClosing_FormClosed) return null;
                    var stat = FileStat.TryGet(path); // フォルダか・大きさ・更新日時を 1 回の問い合わせで取る
                    var isDir = stat != null && stat.IsDirectory;
                    var done = GetHeader(path, isDir, stat, out var plainFile, out var filecount, out var loadResult, out var fileSize, out var imageInfo, out var movieInfo);
                    return done ? new HeaderResult(path, plainFile, filecount, loadResult, fileSize, imageInfo, movieInfo) : null;
                });
            }
            catch { }
            finally
            {
                headerLoadersActive--;
            }
            if (result != null && guid == loadingGuid && !CreatingZipPathArray && !CatalogForm_FormClosing_FormClosed)
            {
                try
                {
                    // 待っている間に項目が増減して位置がずれていたら探し直す
                    var zpa = ZipPathArray;
                    if (zpa != null && (dataIndex >= zpa.Length || zpa[dataIndex] != path)) dataIndex = Array.IndexOf(zpa, path);
                    if (dataIndex >= 0) applyHeaderResult(dataIndex, result);
                }
                catch (Exception error)
                {
                    Program.AlertError(error);
                }
            }
            if (!CatalogForm_FormClosing_FormClosed) pumpHeaderRequests();
        }

        /// <summary>列で並べ替えたあと、ヘッダが揃ったら同じ列でもう一度並べ替える</summary>
        private void tryFinishPendingVolatileSort()
        {
            var pending = pendingVolatileSort;
            if (pending == null) return;
            if (pending.Guid != loadingGuid) { pendingVolatileSort = null; return; }
            if (headerRequests.Count > 0 || headerLoadersActive > 0 || bmwMakePreview.IsBusy) return;
            pendingVolatileSort = null;
            applyVolatileSort(pending.Column, pending.Direction, pending.SortMode, scrollToTopIfNoSelection: false); // 待っている間に動かしたスクロールは戻さない
        }
    }
}
