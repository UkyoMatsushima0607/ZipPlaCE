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
        int graphicalListDrag_SelectedFolderShowIndex;

        private void graphicalListDragEnter(IGraphicalList sender, GraphicalListDragEventArgs e)
        {
            if (dragOuting && dragAndDropAction == DragAndDropAction.ShowThumbnails)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            try
            {
                if (/*e.GetKeyState() == Keys.LButton && */zabLocation.Enabled)
                {
                    if (dragAndDropAction == DragAndDropAction.ShowThumbnails)
                    {
                        if (e.GetKeyState() == Keys.LButton && e.Data.GetDataPresent(DataFormats.FileDrop))
                        {
                            string[] Files = ShortcutResolver.Exec((string[])e.Data.GetData(DataFormats.FileDrop, false)).ToArray();
                            if (Files.Length == 1)
                            {
                                e.Effect = DragDropEffects.Copy;
                            }
                            else
                            {
                                e.Effect = DragDropEffects.None;
                            }
                        }
                        else
                        {
                            e.Effect = DragDropEffects.None;
                        }
                    }
                    else if (dragAndDropAction == DragAndDropAction.MoveOrCopyFiles)
                    {
                        if (InRealDirectory)
                        {
                            graphicalListDrag_SelectedFolderShowIndex = -1;
                            dropToMoveCopyControl_DragEnter(null, e);
                        }
                        else if (InVirtualDirectory)
                        {
                            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                            {
                                e.Effect = DragDropEffects.Copy;
                            }
                            else
                            {
                                e.Effect = DragDropEffects.None;
                            }
                        }
                        else
                        {
                            e.Effect = DragDropEffects.None;
                        }
                    }
                }
            }
            catch (Exception error)
            {
                e.Effect = DragDropEffects.None;
                MessageBox.Show(this, error.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void graphicalListDragOver(IGraphicalList sender, GraphicalListDragEventArgs e)
        {
            if (dragAndDropAction == DragAndDropAction.MoveOrCopyFiles)
            {
                if (!InRealDirectory) return;
                var si = e.Index;
                if (si < 0)
                {
                    e.Effect = getCurrentLocationDragEffect(e);
                    if (graphicalListDrag_SelectedFolderShowIndex != -1)
                    {
                        sender.Invalidate(graphicalListDrag_SelectedFolderShowIndex);
                        graphicalListDrag_SelectedFolderShowIndex = -1;
                    }
                    return;
                }
                var s2d = tvCatalog.ShowIndexToDataIndex;
                if (s2d == null || s2d.Length <= si) return; // 想定外
                var di = s2d[si]; if (di < 0) return; // 想定外
                int zpaLength;
                var zna = ZipNameArray; if (zna == null || (zpaLength = zna.Length) <= di) return; // 想定外 
                var name = zna[di]; if (string.IsNullOrEmpty(name)) return;
                if (name[name.Length - 1] == Path.DirectorySeparatorChar)
                {
                    var zpa = ZipPathArray; if (zpa == null || zpa.Length != zpaLength) return; // 想定外 
                    var path = zpa[di]; if (string.IsNullOrEmpty(path)) return;
                    e.Effect = getDragEffect(path, e.AllowedEffect, SameDirEffectMode.None);
                    if (e.Effect != DragDropEffects.None)
                    {
                        if (graphicalListDrag_SelectedFolderShowIndex != si)
                        {
                            sender.DrawHighlight(si);
                            if (graphicalListDrag_SelectedFolderShowIndex != -1)
                            {
                                sender.Invalidate(graphicalListDrag_SelectedFolderShowIndex);
                            }
                            graphicalListDrag_SelectedFolderShowIndex = si;

                        }
                    }
                    else
                    {
                        if (graphicalListDrag_SelectedFolderShowIndex != -1)
                        {
                            sender.Invalidate(graphicalListDrag_SelectedFolderShowIndex);
                            graphicalListDrag_SelectedFolderShowIndex = -1;
                        }
                    }
                    return;
                }
                else
                {
                    e.Effect = getCurrentLocationDragEffect(e);
                    if (graphicalListDrag_SelectedFolderShowIndex != -1)
                    {
                        sender.Invalidate(graphicalListDrag_SelectedFolderShowIndex);
                        graphicalListDrag_SelectedFolderShowIndex = -1;
                    }
                    return;
                }
            }
        }

        private void graphicalListDragLeave(object sender, EventArgs e)
        {
            if (graphicalListDrag_SelectedFolderShowIndex != -1)
            {
                (sender as IGraphicalList)?.Invalidate(graphicalListDrag_SelectedFolderShowIndex);
                graphicalListDrag_SelectedFolderShowIndex = -1;
            }
        }

        private string graphicalListPathGetter(DragEventArgs e)
        {
            var e2 = (GraphicalListDragEventArgs)e;

            var si = e2.Index;
            if (si < 0)
            {
                return currentLocation;
            }
            else
            {
                var s2d = tvCatalog.ShowIndexToDataIndex;
                if (s2d == null || s2d.Length <= si) return null; // 想定外
                var di = s2d[si]; if (di < 0) return null; // 想定外
                int zpaLength;
                var zna = ZipNameArray; if (zna == null || (zpaLength = zna.Length) <= di) return null;
                var name = zna[di]; if (string.IsNullOrEmpty(name)) return null;
                var isDir = name[name.Length - 1] == Path.DirectorySeparatorChar;
                if (!isDir) return currentLocation;
                var zpa = ZipPathArray; if (zpa == null || zpa.Length != zpaLength) return null; // 想定外 
                return zpa[di];
            }
        }

        private void graphicalListDragDrop(IGraphicalList sender, GraphicalListDragEventArgs e)
        {
            try
            {
                if (dragAndDropAction == DragAndDropAction.ShowThumbnails)
                {
                    var path = ShortcutResolver.Exec(((string[])e.Data.GetData(DataFormats.FileDrop, false))[0]);
                    MoveLocation(ApplicationProviderUser.DragAndDrop, path);
                }
                else if (dragAndDropAction == DragAndDropAction.MoveOrCopyFiles)
                {
                    if (InRealDirectory)
                    {
                        dropToMoveCopyControl_DragDrop(e, graphicalListPathGetter, finallyAction: () =>
                        {
                            if (graphicalListDrag_SelectedFolderShowIndex != -1)
                            {
                                sender.Invalidate(graphicalListDrag_SelectedFolderShowIndex);
                                graphicalListDrag_SelectedFolderShowIndex = -1;
                            }
                        });
                    }
                    else if (InVirtualDirectory)
                    {
                        pasteToCurrentLocation(e.Data, DragDropEffects.Copy);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private DragDropEffects getCurrentLocationDragEffect(GraphicalListDragEventArgs e)
        {
            if (string.IsNullOrEmpty(currentLocation)) return DragDropEffects.None;
            if (InRealDirectory) return getDragEffect(currentLocation, e.AllowedEffect, SameDirEffectMode.None);
            if (InVirtualDirectory) return DragDropEffects.Copy;
            return DragDropEffects.None;
        }

        private void tvCatalog_GraphicalListDragEnter(IGraphicalList sender, GraphicalListDragEventArgs e) { graphicalListDragEnter(sender, e); }

        private void tvCatalog_GraphicalListDragOver(IGraphicalList sender, GraphicalListDragEventArgs e) { graphicalListDragOver(sender, e); }

        private void tvCatalog_GraphicalListDragLeave(object sender, EventArgs e) { graphicalListDragLeave(sender, e); }

        private void tvCatalog_GraphicalListDragDrop(IGraphicalList sender, GraphicalListDragEventArgs e) { graphicalListDragDrop(sender, e); }

        private void dgvFileList_GraphicalListDragEnter(IGraphicalList sender, GraphicalListDragEventArgs e) { graphicalListDragEnter(sender, e); }

        private void dgvFileList_GraphicalListDragOver(IGraphicalList sender, GraphicalListDragEventArgs e) { graphicalListDragOver(sender, e); }

        private void dgvFileList_GraphicalListDragLeave(object sender, EventArgs e) { graphicalListDragLeave(sender, e); }

        private void dgvFileList_GraphicalListDragDrop(IGraphicalList sender, GraphicalListDragEventArgs e) { graphicalListDragDrop(sender, e); }

        private int getArchiveIndexWithAIAInfo(string path)
        {
            var info = new ZipPlaInfo(path);
            var thumb = info.ThumbnailInfo;
            var index = thumb != null ? thumb.Item1 : -1;
            return getArchiveIndexWithAIAInfo(index);
        }

        private int getArchiveIndexWithAIAInfo(int index)
        {
            // 上位 2 ビット使用
            switch (ArchivesInArchiveMode)
            {
                case ArchivesInArchiveMode.IfNoOther1Level: return index ^ (1 << 31 | 0 << 30);

                case ArchivesInArchiveMode.Always1Level: return index ^ (1 << 31 | 1 << 30); // 新たに追加する場合これを潰す

                case ArchivesInArchiveMode.UntilFound2Level: return index ^ (0 << 31 | 1 << 30); // 新規追加

                default: return index;
            }
        }

        /// <summary>
        /// ZipPla CE: サムネイルを作るときに書庫を開いて数えたページ数。キャッシュにない書庫は、ヘッダの取得（getFileCount）と
        /// サムネイルの作成（GetArchiveThumbnail）が別々に書庫を開いて目録（7z・RAR のヘッダ、zip の中央ディレクトリ）を読んでいたので、
        /// サムネイルが先に作られていればヘッダの側はこれを使い、書庫を開かない。キャッシュを使わない設定のときも効く。
        /// ファイルの大きさと更新日時、書庫の中の探し方が同じときだけ使う
        /// </summary>
        private sealed class ArchiveEntryCountMemo
        {
            public long Length;
            public DateTime LastWriteTimeUtc;
            public int LeftHierarchies;
            public PackedImageLoaderSearchMode SearchMode;
            public int Count;
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ArchiveEntryCountMemo> archiveEntryCountMemos
            = new System.Collections.Concurrent.ConcurrentDictionary<string, ArchiveEntryCountMemo>(StringComparer.OrdinalIgnoreCase);

        private const int ArchiveEntryCountMemoMax = 4096;

        private void rememberArchiveEntryCount(string zipPath, int count)
        {
            try
            {
                var info = new FileInfo(zipPath);
                if (!info.Exists) return; // フォルダは対象にしない
                if (archiveEntryCountMemos.Count >= ArchiveEntryCountMemoMax) archiveEntryCountMemos.Clear();
                archiveEntryCountMemos[zipPath] = new ArchiveEntryCountMemo
                {
                    Length = info.Length,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    LeftHierarchies = packedImageLoaderLeftHierarchies,
                    SearchMode = packedImageLoaderSearchMode,
                    Count = count,
                };
            }
            catch { }
        }

        private bool tryGetRememberedArchiveEntryCount(string zipPath, FileStat stat, out int count)
        {
            count = -1;
            if (!archiveEntryCountMemos.TryRemove(zipPath, out var memo)) return false; // 使うのは 1 回だけ（ヘッダは 1 項目につき 1 回取る）
            if (memo.LeftHierarchies != packedImageLoaderLeftHierarchies || memo.SearchMode != packedImageLoaderSearchMode) return false;
            if (stat == null || stat.IsDirectory || stat.Length != memo.Length || stat.LastWriteTimeUtc != memo.LastWriteTimeUtc) return false;
            count = memo.Count;
            return true;
        }

        /// <summary>
        /// ZipPla CE: ヘッダの取得で使う、元のファイルの属性・大きさ・更新日時。1 回の問い合わせ（FileInfo）で取り、
        /// 以前のようにフォルダかどうか（Directory.Exists）・大きさ（FileInfo.Length）・キャッシュの確かめの更新日時（File.GetLastWriteTime）を
        /// 別々にファイルシステムに問い合わせない。HDD・NAS では 1 回ごとにシークや往復が起きうる
        /// </summary>
        internal sealed class FileStat
        {
            public bool IsDirectory;
            public long Length;
            public DateTime LastWriteTimeUtc;
            public DateTime LastWriteTime; // ローカル時刻（GPSizeThumbnail の比べ方に合わせる）

            /// <summary>取れなければ（ない・読めない）null</summary>
            public static FileStat TryGet(string path)
            {
                try
                {
                    var info = new FileInfo(path);
                    var attributes = info.Attributes; // ここで 1 回だけ問い合わせ、以後の値はこの結果から返る
                    if ((int)attributes == -1) return null; // ない
                    var isDir = (attributes & FileAttributes.Directory) != 0;
                    var utc = info.LastWriteTimeUtc;
                    return new FileStat { IsDirectory = isDir, Length = isDir ? -1 : info.Length, LastWriteTimeUtc = utc, LastWriteTime = utc.ToLocalTime() };
                }
                catch
                {
                    return null;
                }
            }
        }

        private void getFileCount(out int filecount, string zipPath) => getFileCount(out filecount, zipPath, null);

        private void getFileCount(out int filecount, string zipPath, FileStat stat)
        {
            filecount = -1;
            try
            {
                if (thumbnailCache != null)
                {

                    byte[] data;
                    if (GPSizeThumbnail.TryGet(thumbnailCache, zipPath, getArchiveIndexWithAIAInfo(zipPath), stat?.LastWriteTime, out data))
                    {
                        try
                        {
                            filecount = dataToFileCount(data);
                        }
                        catch { }
                    }
                }
                if (filecount == -1 && ArchivesInArchiveMode == ArchivesInArchiveMode.Ignore)
                {
                    if (!tryGetRememberedArchiveEntryCount(zipPath, stat ?? FileStat.TryGet(zipPath), out filecount))
                    {
                        using (var loader = CreatePackedImageLoader(zipPath))
                        {
                            filecount = loader.GetPackedImageEntries().Count;
                        }
                    }
                }
            }
            catch
            {
                filecount = -1;
                throw;
            }
        }

        private void getImageHeader(out ImageInfo imageInfo, string zipPath, FileStat stat = null)
        {
            imageInfo = null;
            if (ImageInfo.Supports(zipPath))
            {
                imageInfo = new ImageInfo(zipPath); // パフォーマンス的なロスが少ないので正確な方から取得
            }
            else
            {
                byte[] data;
                if (thumbnailCache != null && GPSizeThumbnail.TryGetWithIgnoredIndex(thumbnailCache, zipPath, stat?.LastWriteTime, out data))
                {
                    imageInfo = ImageInfo.FromData(data);
                }
            }
        }

        bool dgvDirectoryList_MouseLeave_DragOutPart_Dragouting = false;

        void dgvDirectoryList_MouseLeave_DragOutPart(MouseEventArgs e)
        {
            if (dgvSorter.InRowsHolded && !dgvDirectoryList.ClientRectangle.Contains(e.Location))
            {
                dgvSorter.CancelDrag();
                if (MouseButtons == MouseButtons.Left)
                {
                    dgvDirectoryList_DragOutPart();
                }
            }
        }

        void dgvDirectoryList_DragOutPart()
        {
            try
            {
                var files = new List<string>();
                foreach (DataGridViewRow row in dgvDirectoryList.SelectedRows)
                {
                    var bookmark = row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark;
                    var path = bookmark.SimpleBookmark.Location;
                    if (!string.IsNullOrEmpty(path) && (Directory.Exists(path) || File.Exists(path)))
                    {
                        files.Add(path);
                    }
                    else
                    {
                        files.Clear();
                        break;
                    }
                }

                // 他のセルに重なると同時にドラッグが発生した場合に
                // そのセルのツールチップが表示されることがある
                if (dgvDirectoryList.ShowCellToolTips)
                {
                    dgvDirectoryList.ShowCellToolTips = false;
                }

                if (files.Count == 0)
                {
                    dgvDirectoryList_MouseLeave_DragOutPart_Dragouting = true;
                    DragDropEffects dde = dgvDirectoryList.DoDragDrop(new string[0], DragDropEffects.None);
                }
                else
                {
                    //DataObjectを作成する
                    DataObject dataObj = new DataObject(DataFormats.FileDrop, files.ToArray());
                    dgvDirectoryList_MouseLeave_DragOutPart_Dragouting = true;
                    DragDropEffects dde = dgvDirectoryList.DoDragDrop(dataObj, DragDropEffects.All);
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                dgvDirectoryList_MouseLeave_DragOutPart_Dragouting = false;

                // このタイミングで戻すと非表示中に本来表示されたものがドロップ後に表示されてしまうので
                // 次の DragEnter で戻す
                //dgvDirectoryList.ShowCellToolTips = true;
            }
        }

        private void dgvDirectoryList_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            if (((Keys)e.KeyState & (Keys.LButton | Keys.RButton)) == (Keys.LButton | Keys.RButton))
            {
                e.Action = DragAction.Cancel;
            }
        }

        private bool dgvDirectoryList_DragEnter_ByLeftClick;

        private void dgvDirectoryList_DragEnter(object sender, DragEventArgs e)
        {
            if (dgvDirectoryList_MouseLeave_DragOutPart_Dragouting)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            try
            {
                if ((e.KeyState == DragEventKeyState_LeftButton || e.KeyState == (int)Keys.RButton) && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] Files = ShortcutResolver.Exec((string[])e.Data.GetData(DataFormats.FileDrop, false)).ToArray();
                    if (Files.Any(file => Directory.Exists(file) || IsSupportedByCatalogFormExceptFolder(file) && File.Exists(file)))
                    {
                        dgvDirectoryList_DragEnter_ByLeftClick = e.KeyState == (int)Keys.LButton;
                        e.Effect = DragDropEffects.Copy;
                    }
                    else
                    {
                        e.Effect = DragDropEffects.None;
                    }
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            }
            catch (Exception error)
            {
                Program.AlertError(error);
            }
        }

        private Tuple<string[], Point> dgvDirectoryList_DragDrop_DropInfo = null;

        private void dgvDirectoryList_DragDrop(object sender, DragEventArgs e)
        {
            if (dgvDirectoryList_MouseLeave_DragOutPart_Dragouting)
            {
                return;
            }
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] Files = ShortcutResolver.Exec((string[])e.Data.GetData(DataFormats.FileDrop, false)).ToArray();

                    var dropInfo = Tuple.Create(Files, new Point(e.X, e.Y));

                    if (!dgvDirectoryList_DragEnter_ByLeftClick)
                    {
                        dgvDirectoryList_DragDrop_DropInfo = dropInfo;
                        if (this != Form.ActiveForm) Activate();
                        cmsDirectoryListRightClickDrop.Show(e.X, e.Y);
                    }
                    else
                    {
                        dgvDirectoryList_DragDrop_core(setNewColor: false, dropInfo: dropInfo);
                    }
                }
            }
            catch (Exception error)
            {
                Program.AlertError(error);
            }
        }

        private void cmsDirectoryListRightClickDrop_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            if (e.CloseReason != ToolStripDropDownCloseReason.ItemClicked)
            {
                dgvDirectoryList_DragDrop_DropInfo = null;
            }
            AllowFullpower = true;
        }

        private void addToBookmarkWithCurrentProfileColorRightClickDropToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dgvDirectoryList_DragDrop_DropInfo != null)
            {
                try
                {
                    dgvDirectoryList_DragDrop_core(setNewColor: false, dropInfo: dgvDirectoryList_DragDrop_DropInfo);
                    dgvDirectoryList_DragDrop_DropInfo = null;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void addToBookmarkWithNewProfileColorRightClickDropToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dgvDirectoryList_DragDrop_DropInfo != null)
            {
                try
                {
                    dgvDirectoryList_DragDrop_core(setNewColor: true, dropInfo: dgvDirectoryList_DragDrop_DropInfo);
                    dgvDirectoryList_DragDrop_DropInfo = null;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void dgvDirectoryList_DragDrop_core(bool setNewColor, Tuple<string[], Point> dropInfo)
        {
            Color newColor;
            if (setNewColor)
            {
                AllowFullpower = false;
                newColor = getNewProfileColorFromDialog(useCurrentAsDefault: false);
                AllowFullpower = true;
                if (newColor.IsEmpty) return;
            }
            else
            {
                newColor = currentProfileColor;
            }

            var bookmarks = new List<ColoredBookmark>();
            foreach (var file in dropInfo.Item1)
            {
                if (Directory.Exists(file))
                {
                    var path = Path.GetFullPath(GetDirectoryPathWithSeparator(file));
                    if (path.EndsWith(Path.DirectorySeparatorChar.ToString())) path = path.Substring(0, path.Length - 1);
                    bookmarks.Add(currentConditionToColoredBookmark(newColor, path));
                }
                else if (IsSupportedByCatalogFormExceptFolder(file) && File.Exists(file))
                {
                    var path = Path.GetFullPath(file);
                    bookmarks.Add(currentConditionToColoredBookmark(newColor, path));
                }
            }

            if (bookmarks.Count > 0)
            {
                var p = dgvDirectoryList.PointToClient(dropInfo.Item2);
                var hit = dgvDirectoryList.HitTest(p.X, p.Y);

                addCatalogBookmarkToList(bookmarks, hit.RowIndex >= 0 ? hit.RowIndex : int.MaxValue);

                // 表示色が変わってしまうので追加された項目の選択は行わない

                saveBookmarkToIni_bookmarkChanged = true;
                saveBookmarkToConfig();
            }
        }
    }
}
