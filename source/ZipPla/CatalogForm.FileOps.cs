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
        private string getBaseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name[name.Length - 1] == Path.DirectorySeparatorChar)
            {
                return name.Substring(0, name.Length - 1);
            }
            else
            {
                try
                {
                    var ext = Path.GetExtension(name);
                    if (!string.IsNullOrEmpty(ext))
                    {
                        return name.Substring(0, name.Length - ext.Length);
                    }
                }
                catch { }
                return name;
            }
        }

        private bool dgvFileList_CellEndEdit_Edited = false;

        private void dgvFileList_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            var editingShowIndex = fileListEditingShowIndex;
            fileListEditingShowIndex = -1;
            fileListEditingText = null;
            if (editingShowIndex >= 0 && editingShowIndex < dgvFileList.RowCount) dgvFileList.InvalidateRow(editingShowIndex);
            if (!dgvFileList.ReadOnly)
            {
                dgvFileList.ReadOnly = true;

                if (!dgvFileList_CellEndEdit_Edited)
                {
                    var d2s = tvCatalog.DataIndexToShowIndex;
                    if (renameToolStripMenuItem_Click_Index < d2s.Length)
                    {
                        var showIndex = d2s[renameToolStripMenuItem_Click_Index];
                        if (showIndex >= 0)
                        {
                            var rows = dgvFileList.Rows;
                            if (showIndex < rows.Count)
                            {
                                dgvFileList.InvalidateRow(showIndex); // 行の情報は元の名前のまま
                            }
                        }
                    }
                }
            }
            dgvFileList_CellEndEdit_Edited = false;
            renameToolStripMenuItem_Click_Index = -1;
            renameToolStripMenuItem_Click_OldName = null;
            renameToolStripMenuItem_Click_OldPath = null;
        }

        private void dgvFileList_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            if (e.RowIndex == dgv.NewRowIndex || !dgv.IsCurrentCellDirty)
            {
                return;
            }

            var oldName = renameToolStripMenuItem_Click_OldName;
            var selectedIndex = renameToolStripMenuItem_Click_Index;
            var oldPath = renameToolStripMenuItem_Click_OldPath;

            var isDirectrory = oldName.EndsWith(Path.DirectorySeparatorChar.ToString());
            var newName = e.Value.ToString();

            e.Value = oldName;

            try
            {
                newName = newName + (isDirectrory ? "" : Path.GetExtension(oldPath));
                var newPath = Path.Combine(oldPath.Substring(0, oldPath.Length - oldName.Length), newName);
                Program.FileSaveCheckWithException(Path.GetDirectoryName(oldPath), newName);
                RenameShownArchive(selectedIndex, oldPath, newPath);
                e.Value = isDirectrory ? newName + Path.DirectorySeparatorChar : newName;
                e.ParsingApplied = true;
                dgvFileList_CellEndEdit_Edited = true;
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void openHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            zabLocation.Text = Program.HistorySorPath;
            MakePreview();
        }

        private void deleteonlyForListFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            deleteSelectedItems(ModifierKeys);
        }

        private async void deleteSelectedItems(Keys modifierKeys)
        {
            var current = currentLocation;
            if (current == null || CreatingZipPathArray) return;
            if (!InDirectoryVirtualDirectoryOrSmartDirectory) return;


            var selectedIndices = tvCatalog.SelectedIndices;
            if (selectedIndices != null && selectedIndices.Length > 0)
            {
                var isSor = InVirtualDirectory;
                if (!isSor && !InSmartDirectory)
                {
                    try
                    {
                        if (!Directory.Exists(current))
                        {
                            return;
                        }
                    }
                    catch
                    {
                        return;
                    }
                }


                string[] deleteList;
                //
                int[] d2s;
                lock (ZipPathArrayLocker)
                {
                    if (ZipMaskArray == null) return;
                    d2s = tvCatalog.DataIndexToShowIndex;
                    if (d2s == null) return;
                    var indexBound = Math.Min(ZipMaskArray.Length, d2s.Length);
                    deleteList = new string[selectedIndices.Length];
                    var showIndexArray = new int[selectedIndices.Length];
                    for (var k = 0; k < selectedIndices.Length; k++)
                    {
                        var index = selectedIndices[k];
                        if (index < 0 || index >= indexBound) return;
                        var path = ZipPathArray[index]; if (path == null) return;
                        var name = ZipNameArray[index]; if (name == null) return;
                        deleteList[k] = path;
                        showIndexArray[k] = d2s[index];
                        //if (!isSor) isDirList.Add(name.EndsWith(Path.DirectorySeparatorChar.ToString()));
                    }
                    var sorter = Enumerable.Range(0, deleteList.Length).OrderBy(i => showIndexArray[i]).ToArray();
                    Arrange(deleteList, sorter);
                    Arrange(selectedIndices, sorter);
                    //deleteList.Sort()
                }

                var deleteFromDisk = false;
                if (isSor)
                {
                    if (!await showConfirmationDialogIfIndicesAreMany(Message.DoYouRemoveFollowing1ItemsFromVirtualFolder, deleteList)) return;
                }
                else
                {
                    deleteFromDisk = (modifierKeys & Keys.Shift) == Keys.Shift;
                    if (!await showConfirmationDialogAlways(deleteFromDisk ? Message.DoYouDeleteFollowing1ItemsPermanently : Message.DoYouSendFollowing1ItemsToRecycleBin, deleteList)) return;
                }

                var isDirList = isSor ? null : new List<bool>();
                // 削除の確認は出来ているので表示が変わっていても削除は行う
                lock (ZipPathArrayLocker)
                {
                    if (ZipMaskArray != null)
                    {
                        var leastIndex = int.MaxValue;

                        var fullBufCount = ZipMaskArray.Length;
                        var indexindex = -1;
                        foreach (var index in selectedIndices)
                        {
                            indexindex++;
                            if (index < 0 || index >= fullBufCount || index >= d2s.Length) break; // fullBufCount は d2s.Length より長い場合もある
                            var path = ZipPathArray[index]; if (path == null) break; if (deleteList[indexindex] != path) break;
                            var name = ZipNameArray[index]; if (name == null) break;
                            leastIndex = Math.Min(leastIndex, d2s[index]);
                            ZipMaskArray[index] = false;
                            ZipPathArray[index] = null;
                            ZipNameArray[index] = null;
                            tvCatalog[index] = null; // これで Dispose される
                            //deleteList.Add(path);
                            if (!isSor) isDirList.Add(name.EndsWith(Path.DirectorySeparatorChar.ToString()));
                        }
                        indexindex++;
                        if (indexindex == selectedIndices.Length && leastIndex >= 0 && leastIndex < int.MaxValue)
                        {
                            var sp = tvCatalog.ScrollBarPercentage;
                            var s2d = GetSortArray(preSortArray: tvCatalog.ShowIndexToDataIndex);
                            tvCatalog.ShowIndexToDataIndex = s2d;
                            tvCatalog.ScrollBarPercentage = sp;
                            leastIndex = Math.Min(leastIndex, s2d.Length - 1); // s2d がゼロなら -1
                            if (leastIndex >= 0) tvCatalog.FocusedIndex = s2d[leastIndex];
                            setFileListFromThumbnailViewer(tryToKeepScroll: true);
                        }
                    }
                }

                if (isSor)
                {

                    try
                    {
                        VirtualFolder.DeleteBookmarkData(current, deleteList);
                    }
                    catch { }
                }
                else
                {

                    var count = deleteList.Length;
                    //Exception exception = null;

                    StopLookAheadProcess();

                    // 1 件ずつ送るとごみ箱の処理が件数分かかるので、IFileOperation に全件積んで 1 回で行う（K25）
                    var deleteSuccessedList = deleteItemsInOneOperation(deleteList, deleteFromDisk, this);
                    if (deleteSuccessedList.Count < count)
                    {
                        MakePreview(tvCatalog.ScrollBarPercentage, tvCatalog.SelectedIndex);
                    }


                    var renameTargetsSet = new HashSet<string>(from bmk in getVirtualFoldersInBookmarks() select bmk.SimpleBookmark.Location); // getVir... は null を含むものは返さない
                    renameTargetsSet.Add(Program.HistorySorPath);
                    foreach (var renameTarget in renameTargetsSet)
                    {
                        try
                        {
                            VirtualFolder.DeleteBookmarkData(renameTarget, deleteSuccessedList);
                        }
                        catch { }
                    }

                    PrepareLookAheadProcess();
                }
            }
        }

        /// <summary>
        /// パスの一覧をまとめて削除する（ごみ箱へ送るか完全に削除）。削除できたパス（引数の文字列のまま）を返す（K25）
        /// </summary>
        private static List<string> deleteItemsInOneOperation(IList<string> paths, bool deleteFromDisk, IWin32Window owner)
        {
            var original = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                var key = trimDirectorySeparator(path);
                if (!original.ContainsKey(key)) original.Add(key, path);
            }
            var sink = new DeleteResultSink();
            try
            {
                using (var fo = new MsdnMag.FileOperation(sink, owner))
                {
                    var flags = MsdnMag.FileOperationFlags.FOF_NOCONFIRMATION | MsdnMag.FileOperationFlags.FOF_NOCONFIRMMKDIR;
                    if (!deleteFromDisk)
                    {
                        // ごみ箱に入らないものは確認する（以前の VB の DeleteFile と同じく、黙って完全に削除しない）
                        flags |= MsdnMag.FileOperationFlags.FOF_ALLOWUNDO | MsdnMag.FileOperationFlags.FOFX_RECYCLEONDELETE | MsdnMag.FileOperationFlags.FOF_WANTNUKEWARNING;
                    }
                    fo.SetOperationFlags(flags);
                    foreach (var key in original.Keys)
                    {
                        try { fo.DeleteItem(key); } catch { } // 既にない項目など
                    }
                    fo.PerformOperations();
                }
            }
            catch (COMException) { } // 取り消しや失敗。消せた分は sink に記録されている
            var result = new List<string>();
            foreach (var deleted in sink.Deleted)
            {
                string path;
                if (original.TryGetValue(trimDirectorySeparator(deleted), out path)) result.Add(path);
            }
            return result;
        }

        private static string trimDirectorySeparator(string path)
        {
            if (path.Length > 3) path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return path;
        }

        private sealed class DeleteResultSink : MsdnMag.FileOperationProgressSink
        {
            public readonly List<string> Deleted = new List<string>();
            public override void PostDeleteItem(uint dwFlags, MsdnMag.IShellItem psiItem, uint hrDelete, MsdnMag.IShellItem psiNewlyCreated)
            {
                if ((int)hrDelete < 0 || psiItem == null) return;
                try { Deleted.Add(psiItem.GetDisplayName(MsdnMag.SIGDN.SIGDN_FILESYSPATH)); } catch { }
            }
        }

        bool StopLookAheadProcessByProgram = false;

        public void StopLookAheadProcess()
        {
            if (waitingProcess != null && !waitingProcess.HasExited)
            {
                var p = waitingProcess;
                waitingProcess = null;
                p.Kill(); // CloseWindow は隠れているプロセスには効かない。Close すると非同期であるにも関わらず内部で Dispose されてその後 WaitForExit できない。従って Kill が最善。
                p.WaitForExit();
                if (OpenWaitingProcess_Processing)
                {
                    StopLookAheadProcessByProgram = true;
                }
            }
        }

        private void addToNewListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            addSelectedToList(false, addToBookmark: true);
        }

        private void addSelectedToList(bool append, string sorPath = null, bool addToBookmark = false)
        {
            if (string.Compare(sorPath, Program.HistorySorPath, ignoreCase: true) == 0)
            {
                string[] pathArray;
                bool[] isDirArray;
                int[] pageArray;
                getSelectedPathArray(-1, out pathArray, out pageArray, out isDirArray);
                addToList(pathArray, append, pageArray: pageArray, sorPath: sorPath, addToBookmark: addToBookmark);
            }
            else
            {
                string[] pathArray;
                bool[] isDirArray;
                getSelectedPathArray(-1, out pathArray, out isDirArray);
                addToList(pathArray, append, pageArray: null, sorPath: sorPath, addToBookmark: addToBookmark);
            }

        }

        private void addToList(string[] addArray, bool append, int[] pageArray = null, string sorPath = null, bool addToBookmark = false)
        {
            if (addArray == null || addArray.Length <= 0) return;
            if (pageArray == null || pageArray.Length != addArray.Length)
            {
                pageArray = new int[addArray.Length];
                for (var i = 0; i < pageArray.Length; i++) pageArray[i] = -1;
            }
            var temp = AllowFullpower;
            try
            {
                if (sorPath == null)
                {
                    AllowFullpower = false;
                    addToNewListSaveFileDialog.FileName = "";
                    if (addToNewListSaveFileDialog.ShowDialog(this) == DialogResult.OK)
                    {
                        sorPath = addToNewListSaveFileDialog.FileName;
                        if (!append && File.Exists(sorPath)) File.Delete(sorPath);
                    }
                }
                if (sorPath != null)
                {
                    VirtualFolder.AddBookmarkData(sorPath, addArray, pageArray);
                    if (addToBookmark)
                    {
                        addCatalogBookmarkToList(currentConditionToColoredBookmark(currentProfileColor, sorPath));
                    }

                    // addToBookmark == false は既に存在することを表す。その場合も表示は行う
                    bookmarkToolStripMenuItem.Checked = true;
                    var rowIndexBound = dgvDirectoryList.Rows.Count - 1;
                    for (var i = 0; i <= rowIndexBound; i++) dgvDirectoryList.Rows[i].Selected = i == rowIndexBound;
                    saveBookmarkToIni_bookmarkChanged = true;
                    saveBookmarkToConfig();
                }
            }
            catch (Exception error)
            {
#if DEBUG
                MessageBox.Show(this, error.ToString(), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                MessageBox.Show(this, error.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
            }
            finally
            {
                AllowFullpower = temp;
            }
        }

        private void removeFromHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            removeSelectedFromHistory();
        }

        private void removeSelectedFromHistory()
        {
            string[] pathArray;
            bool[] isDirArray;
            int[] dummy;
            getSelectedPathArray(-1, out pathArray, out dummy, out isDirArray, pageReset: true);
            removeSelectedFromList(pathArray, Program.HistorySorPath);
        }

        private void removeSelectedFromList(string[] removeArray, string sorPath)
        {
            if (removeArray == null || removeArray.Length <= 0) return;
            var temp = AllowFullpower;
            try
            {
                if (sorPath != null)
                {
                    VirtualFolder.DeleteBookmarkData(sorPath, removeArray);
                }
            }
            catch (Exception error)
            {
#if DEBUG
                MessageBox.Show(this, error.ToString(), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                MessageBox.Show(this, error.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
            }
            finally
            {
                AllowFullpower = temp;
            }
        }

        private void btnGoToBack_Click(object sender, EventArgs e)
        {
            gotToUnderBuffer(undoBufferIndex - 1, ApplicationProviderUser.MouseClick);
        }

        private void btnGoToForward_Click(object sender, EventArgs e)
        {
            gotToUnderBuffer(undoBufferIndex + 1, ApplicationProviderUser.MouseClick);
        }

        private void gotToUnderBuffer(int index, ApplicationProviderUser user)
        {
            if (index == undoBufferIndex) return;
            //if (CreatingZipPathArray) return;
            var preIndex = undoBufferIndex;
            UndoBufferClass newData = null;
            if (index >= 0 && index < undoBuffer.Count)
            {
                undoBufferIndex = index;
                newData = undoBuffer[index];
            }
            btnGoToForward.Enabled = undoBufferIndex < undoBuffer.Count - 1;
            btnGoToBack.Enabled = undoBufferIndex > 0;

            if (!string.IsNullOrEmpty(newData?.Location))
            {
                //zabLocation.Text = newData.Location;

                // キー操作でも選択項目が存在すれば一番上を選択するという操作は行わない
                if (user == ApplicationProviderUser.EnterKey && !string.IsNullOrEmpty(newData.SelectedFileName)) user = ApplicationProviderUser.MouseClick;

                var prevColor = currentProfileColor;
                var prevProfile = prepareMoveLocationWithChangingProfile(newData.Location, newData.ProfileColor, newData.Profile, user);

                MakePreview(manualUndoShift: undoBufferIndex - preIndex, selectedIndex: -1, selectedFileName: newData.SelectedFileName,
                    tScrollbar: newData.ThumbnailPosition, fScrollbar: newData.FileListPosition, prevProfileInfo: Tuple.Create(prevColor, prevProfile));
            }
        }

        private void goUpFromCurrentLocation()
        {
            var basePath = preCurrentLocation; // 読み込み準備中に呼び出した場合準備している方を基準にする
            changeCurrentAndGoUpFromCurrentLocation(basePath);
        }

        private void changeCurrentAndGoUpFromCurrentLocation(string newBasePath)
        {
            var cl = currentLocation;
            if (string.IsNullOrEmpty(cl)) return;
            string parent;
            try
            {
                parent = Path.GetDirectoryName(cl);
            }
            catch
            {
                return;
            }
            goUpFromCurrentLocation(parent, newBasePath);
        }

        private void goUpFromCurrentLocation(string newPath)
        {
            var basePath = preCurrentLocation; // 読み込み準備中に呼び出した場合準備している方を基準にする
            goUpFromCurrentLocation(newPath, basePath);
        }

        private void goUpFromCurrentLocation(string newPath, string basePath)
        {
            if (string.IsNullOrEmpty(newPath)) return;

            var c = newPath.Last();
            if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar)
            {
                if (newPath.Length == 1) return;
                newPath = newPath.Substring(0, newPath.Length - 1);
            }

            if (basePath == null || basePath.Length - newPath.Length < 2 || !basePath.StartsWith(newPath, StringComparison.OrdinalIgnoreCase))
            {
                zabLocation.Text = newPath;
                MakePreview();
                return;
            }

            c = basePath[newPath.Length];
            if (c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar)
            {
                zabLocation.Text = newPath;
                MakePreview();
                return;
            }

            var selectedFileName = basePath.Substring(newPath.Length + 1);
            var stop1 = selectedFileName.IndexOf(Path.DirectorySeparatorChar); if (stop1 < 0) stop1 = int.MaxValue;
            var stop2 = selectedFileName.IndexOf(Path.AltDirectorySeparatorChar); if (stop2 < 0) stop2 = int.MaxValue;
            var stop = Math.Min(stop1, stop2);
            if (stop != int.MaxValue)
            {
                selectedFileName = selectedFileName.Substring(0, stop) + Path.DirectorySeparatorChar;
            }
            else
            {
                if (!CreatingZipPathArray)
                {
                    if (InRealDirectory)
                    {
                        selectedFileName += Path.DirectorySeparatorChar;
                    }
                }
                else
                {
                    try
                    {
                        if (Directory.Exists(newPath + Path.DirectorySeparatorChar + selectedFileName))
                        {
                            selectedFileName += Path.DirectorySeparatorChar;
                        }
                    }
                    catch
                    {
                        selectedFileName = null;
                    }
                }
            }

            if (selectedFileName == null)
            {
                zabLocation.Text = newPath;
                MakePreview();
                return;
            }

            zabLocation.Text = newPath;
            MakePreview(selectedIndex: -1, selectedFileName: selectedFileName,
                tScrollbar: MakePreview_tScrollbarMoveToSelectedFile, fScrollbar: MakePreview_fScrollbarMoveToSelectedFile);
            return;

        }

        private void RenameShownArchive(int selectedIndex, string path, string newPath, bool atFirstArrayChange = true, bool? needToReload = null)
        {
            RenameShownArchive(new int[] { selectedIndex }, new string[] { path }, new string[] { newPath }, atFirstArrayChange, needToReload);
        }

        private async Task<bool> showConfirmationDialogAlways(string message, IReadOnlyList<string> paths)
        {
            return await showConfirmationDialog(message, paths, 1, getFileName: true);
        }

        private async Task<bool> showConfirmationDialogIfIndicesAreMany(string message, IReadOnlyList<string> paths)
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

            if (!await showConfirmationDialogIfIndicesAreMany(Message.DoYouRenameFollowing1Items, paths)) return;

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
    }
}
