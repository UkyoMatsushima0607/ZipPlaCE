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
        private void selectionToClipboard(DragDropEffects effects, Keys modifierKeys)//, out string[] selectedPaths)
        {
            if (InMovie)
            {
                selectedMovieCaptchaToClipboard();
            }
            else
            {
                selectedItemsToClipboard(effects, modifierKeys);
            }
        }

        private void selectedMovieCaptchaToClipboard()
        {
            if (CreatingZipPathArray || !InMovie) return;

            string[] selectedPaths;
            bool[] isDirs;
            getSelectedPathArray(-1, out selectedPaths, out isDirs);
            if (selectedPaths == null || selectedPaths.Length != 1)
            {
                return;
            }
            var longPath = selectedPaths[0];
            try
            {
                var altPos = longPath.IndexOf(Path.AltDirectorySeparatorChar);
                if (altPos < 0 || altPos >= longPath.Length - 1) throw new FormatException();
                var entity = longPath.Substring(0, altPos);
                var posString = longPath.Substring(altPos + 1);
                var pos = TimeSpan.Parse(posString);

                if (!MovieThumbnailLoader.ffmpegExists())
                {
                    RecentFfmpegExists = false;
                    throw new FileNotFoundException(null, MovieThumbnailLoader.ffmpegPath);
                }
                else
                {
                    RecentFfmpegExists = true;
                }

                if (!File.Exists(entity))
                {
                    throw new FileNotFoundException(null, entity);
                }
                MovieInfo dummy = null;
                using (var bmp = MovieThumbnailLoader.GetThumbnail(entity, sizeProviderForOriginalSizeAsPossible,
                    t => pos, backgroundWorker: null, timeOut: TimeSpan.FromSeconds(10), movieInfo: ref dummy))
                {
                    Clipboard.SetImage(bmp);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }


        }

        private void selectedItemsToClipboard(DragDropEffects effects, Keys modifierKeys)//, out string[] selectedPaths)
        {
            if (CreatingZipPathArray) return;

            string[] selectedPaths;
            if (!InDirectoryVirtualDirectoryOrSmartDirectory && !InArchive)
            {
                selectedPaths = null;
                return;
            }
            bool[] isDirs;
            getSelectedPathArray(-1, out selectedPaths, out isDirs);
            if (selectedPaths == null || selectedPaths.Length <= 0)
            {
                selectedPaths = null;
                return;
            }

            try
            {
                //selectedPaths = selectedPaths.Where(path => !path.Contains(Path.AltDirectorySeparatorChar)).ToArray();

                if (selectedPaths.Length <= 0)
                {
                    selectedPaths = null;
                    return;
                }

                //var data = new DataObject(DataFormats.FileDrop, selectedPaths);

                var data = GetSelectedDataObject(forClipboard: true);
                if (data == null) return;

                if (!InArchive)
                {
                    for (var i = 0; i < selectedPaths.Length; i++)
                    {
                        var path = selectedPaths[i];
                        var isDir = isDirs[i];
                        if (isDir && !Directory.Exists(path) || !isDir && !File.Exists(path))
                        {
                            throw new FileNotFoundException(null, selectedPaths[i]);
                        }
                    }
                }

                //DragDropEffects.Moveを設定する（DragDropEffects.Move は 2）
                byte[] bs = new byte[] { (byte)effects, 0, 0, 0 };
                System.IO.MemoryStream ms = new System.IO.MemoryStream(bs);
                data.SetData("Preferred DropEffect", ms);

                string strPath;
                if ((modifierKeys & Keys.Shift) == Keys.Shift)
                {
                    // 一つの場合だけ例外的に処理
                    //strPath = selectedPaths.Length == 1 ? $"\"{selectedPaths[0].Replace("\\", "\\\\")}\"" :
                    //    string.Join("", from path in selectedPaths select $"\"{path.Replace("\\", "\\\\")}\",\n");

                    // 一貫性重視
                    strPath = string.Join("\n", from path in selectedPaths select $"\"{path.Replace(@"\", @"\\")}\"");
                }
                else
                {
                    // 一つの場合だけ例外的に処理
                    //strPath = selectedPaths.Length == 1 ? selectedPaths[0] : string.Join("\n", selectedPaths) + "\n";

                    // 一貫性重視
                    strPath = string.Join("\n", selectedPaths);
                }

                data.SetData(DataFormats.UnicodeText, strPath);

                //クリップボードに切り取る
                ClipboardViewer_ClipboardHandler_StopLoaderForClipboardDispose = data is DataObjectEx;

                if (effects == DragDropEffects.Move && !ClipboardViewer_ClipboardHandler_StopLoaderForClipboardDispose && data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = data.GetData(DataFormats.FileDrop, autoConvert: false) as string[];
                    if (files != null)
                    {
                        ClipboardViewer_ClipboardHandler_StopLoaderForClipboardDispose = true;
                        StopLookAheadProcess();
                        OpenedByExternalApplication_Dictionary[GetSelectedDataObject_ClipboardGuid] = new HashSet<string>(from path in files where !string.IsNullOrEmpty(path) select path.ToLower());
                        PrepareLookAheadProcess();
                    }
                }

                Clipboard.SetDataObject(data);
                ClipboardViewer_ClipboardHandler_StopLoaderForClipboardDispose = false;
            }
            catch (Exception e)
            {
                selectedPaths = null;
                MessageBox.Show(this, e.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cutSelection(Keys modifierKeys)
        {
            if (!InRealDirectory) return;
            //string[] selectedPaths;
            ClipboardViewer_ClipboardHandler_StopRedrawCutFile = true;
            selectionToClipboard(DragDropEffects.Move, modifierKeys);//, out selectedPaths);
            setCutFiles(); // 明示的に
            tvCatalog.Refresh();
            dgvFileList.Refresh();

            ClipboardViewer_ClipboardHandler_StopRedrawCutFile = false;
        }

        private void clearCutItemInClipBoard()
        {
            if (cutFilePathSetInLower == null)
            {
                setCutFiles();
            }
            if (cutFilePathSetInLower != null)
            {
                ClipboardViewer_ClipboardHandler_StopRedrawCutFile = true;
                try
                {
                    Clipboard.Clear();
                }
                catch
                {
                }
                ClipboardViewer_ClipboardHandler_StopRedrawCutFile = false;
            }
            setCutFiles(null);
            tvCatalog.Refresh();
            dgvFileList.Refresh();
        }

        private bool ClipboardViewer_ClipboardHandler_StopRedrawCutFile = false;

        private bool ClipboardViewer_ClipboardHandler_StopLoaderForClipboardDispose = false;

        private void ClipboardViewer_ClipboardHandler(object sender, ClipboardEventArgs ev)
        {
            if (!ClipboardViewer_ClipboardHandler_StopRedrawCutFile)
            {
                var preNull = cutFilePathSetInLower == null;

                setCutFiles(); // 明示的に


                //var data = setCutFiles(); // 明示的に

                if (!preNull || cutFilePathSetInLower != null)
                {
                    tvCatalog.Refresh();
                    dgvFileList.Refresh();
                }
            }

            if (!ClipboardViewer_ClipboardHandler_StopLoaderForClipboardDispose)
            {
                packedImageLoaderForClipboard_Dispose();

                OpenedByExternalApplication_Dictionary.Remove(GetSelectedDataObject_ClipboardGuid);
            }
        }

        private HashSet<string> cutFilePathSetInLower = null;

        private void setCutFiles()
        {
            //IDataObject data = null;
            try
            {
                var data = Clipboard.GetDataObject();

                if (!data.GetDataPresent(DataFormats.FileDrop) || GetPreferredDropEffect(data) != DragDropEffects.Move)
                {
                    setCutFiles(null);
                }
                else
                {
                    setCutFiles(data.GetData(DataFormats.FileDrop) as string[]);
                }
            }
            catch
            {
                setCutFiles(null);
            }
            //return data;
        }

        private void setCutFiles(string[] cutFiles)
        {
            if (cutFiles != null && cutFiles.Length > 0)
            {
                cutFilePathSetInLower = new HashSet<string>(from path in cutFiles where !string.IsNullOrEmpty(path) select path.ToLower());
                if (cutFilePathSetInLower.Count == 0) cutFilePathSetInLower = null;
            }
            else
            {
                cutFilePathSetInLower = null;
            }
        }

        private void copySelection(Keys modifierKeys)
        {
            //string[] dummy;
            selectionToClipboard(DragDropEffects.Copy | DragDropEffects.Link, modifierKeys);//, out dummy);
        }

        private void pasteToCurrentLocation()
        {
            if (CreatingZipPathArray || !(InRealDirectory || InVirtualDirectory) || string.IsNullOrEmpty(currentLocation)) return; // 無駄な GetDataObject をしないように
            try
            {
                pasteToCurrentLocation(Clipboard.GetDataObject(), dragDropEffects: null);
            }
            catch { }
            clearCutItemInClipBoard();
        }

        /// <summary>
        /// いつでも実行可能、ただし currentLocation を指定してもペースト後にそこまでスクロールしない
        /// </summary>
        /// <param name="target"></param>
        private void pasteToTarget(string target)
        {
            // 不要
            //if (CreatingZipPathArray || !(InRealDirectory || InVirtualDirectory) || string.IsNullOrEmpty(currentLocation)) return; // 無駄な GetDataObject をしないように
            try
            {
                pasteToTarget(this, target, Clipboard.GetDataObject(), dragDropEffects: null);
            }
            catch { }
            clearCutItemInClipBoard();
        }

        private void newVirtualFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var temp = AllowFullpower;
            try
            {
                AllowFullpower = false;
                string sorPath;
                addToNewListSaveFileDialog.FileName = "";
                if (addToNewListSaveFileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    sorPath = addToNewListSaveFileDialog.FileName;
                    if (File.Exists(sorPath)) File.Delete(sorPath);
                }
                else return;
                if (sorPath != null)
                {
                    VirtualFolder.AddBookmarkData(sorPath, new string[0], new int[0]);
                    MoveLocation(ApplicationProviderUser.OpenFileDialog, sorPath);
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

        private void cmsRightClickOnBackground_Opening(object sender, CancelEventArgs e)
        {
            msSortBy.Stop = msFilter.Stop = msCatalog.Stop = msFileList.Stop = true;
            AllowFullpower = false;
        }

        private void cmsRightClickOnBackground_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            msSortBy.Stop = msFilter.Stop = msCatalog.Stop = msFileList.Stop = false;
            AllowFullpower = true;
        }

        /// <summary>
        /// 名前の末尾に Path.DirectorySeparateChar があるかどうかでフォルダかどうかを判定
        /// ffmpeg の有無が作成されるファイルの表示に影響を与える使い方をする場合修正が必要
        /// </summary>
        /// <param name="names"></param>
        private void createNewToCurrentLocation(params string[] names)
        {
            createNewToCurrentLocation(names as IEnumerable<string>);
        }

        /// <summary>
        /// 名前の末尾に Path.DirectorySeparateChar があるかどうかでフォルダかどうかを判定
        /// 単一のフォルダー作成以外に使う場合は表示メッセージの修正が必要
        /// ffmpeg の有無が作成されるファイルの表示に影響を与える使い方をする場合はロジックの修正が必要
        /// </summary>
        /// <param name="names"></param>
        private void createNewToCurrentLocation(IEnumerable<string> names)
        {
            if (CreatingZipPathArray || !InRealDirectory || names == null || !names.Any()) return;
            var cl = currentLocation;
            if (string.IsNullOrEmpty(cl)) return;

            try
            {
                if (cl.Last() == ':') cl += Path.DirectorySeparatorChar;

                //using (var fo = new MsdnMag.FileOperation(callbackSink: null, owner: this))
                {
                    int dummy;
                    var archiveSkip = ArchiveLoadLevel == ItemLoadLevel.Ignore;
                    var imageSkip = ImageLoadLevel == ItemLoadLevel.Ignore;
                    var videoSkip = VideoLoadLevel == ItemLoadLevel.Ignore;
                    var otherSkip = OtherLoadLevel == ItemLoadLevel.Ignore;

                    var invisibleExists = false;

                    //var moveOrCopy = move ? (Action<string, string, string>)fo.MoveItem : fo.CopyItem;
                    var creates = new List<string>();
                    var isDirs = new List<bool>();
                    foreach (var name in names)
                    {
                        if (string.IsNullOrEmpty(name)) throw new ArgumentException(nameof(name));
                        var isDir = name[name.Length - 1] == Path.DirectorySeparatorChar;
                        var plain = isDir ? name.Substring(0, name.Length - 1) : name;

                        var path = Path.Combine(cl, plain);
                        if (File.Exists(path) || Directory.Exists(path))
                        {
                            var ext = Path.GetExtension(path);
                            var baseName = Path.GetFileNameWithoutExtension(plain);
                            var digit = 2;
                            do
                            {
                                path = Path.Combine(cl, baseName + " (" + digit++ + ")" + ext);
                            }
                            while (File.Exists(path) || Directory.Exists(path));
                        }

                        if (!invisibleExists)
                        {
                            if (isDir)
                            {
                                if (!IsDisplay(SubfolderMode)) invisibleExists = true;
                            }
                            else
                            {
                                invisibleExists = ShouldBeSkipForFile(out dummy, path, archiveSkip, imageSkip, videoSkip, otherSkip, ffmpegExists: false); // ffmpeg の存在が結果に影響を与えるデータは扱わない
                            }
                        }

                        creates.Add(path);
                        isDirs.Add(isDir);
                    }

                    // 先にパスでチェックするためここで行う
                    if (invisibleExists && MessageBox.Show(this, Message.NewFolderCanBeCreatedButSomeOfThemWillNotBeDisplayedBecauseOfViewSetting,
                            Message.Information, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                    {
                        return;
                    }

                    // 追加されたら選択する
                    addToUserAddFileList(creates);

                    for (var i = 0; i < creates.Count; i++)
                    {
                        if (isDirs[i])
                        {
                            Directory.CreateDirectory(creates[i]);
                        }
                        else
                        {
                            using (var fs = File.Create(creates[i])) fs?.Close();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void pasteToCurrentLocation(System.Windows.Forms.IDataObject data, DragDropEffects? dragDropEffects = null)
        {
            if (data == null || CreatingZipPathArray || !(InRealDirectory || InVirtualDirectory)) return;
            var startingGuid = loadingGuid;
            var cl = currentLocation;
            if (string.IsNullOrEmpty(cl)) return;

            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop))
                {

                    var files = (string[])data.GetData(DataFormats.FileDrop);
                    if (files.Length <= 0) return;

                    tvCatalog.SelectedIndices = new int[0];

                    if (InRealDirectory)
                    {

                        DragDropEffects dde = dragDropEffects == null ? GetPreferredDropEffect(data) : (DragDropEffects)dragDropEffects;
                        bool move = dde == DragDropEffects.Move; // 他のプログラムとの互換性を考慮し (DragDropEffects.Copy | DragDropEffects.Link) でなくともコピーとみなす

                        using (var fo = new MsdnMag.FileOperation(callbackSink: null, owner: this))
                        {
                            var movedFiles = queueFileDropOperations(fo, files, cl, move);

                            //if (!alertInvisibleItemsOnPaste(movedFiles, checkAttribute: true)) // 誤り

                            // 先にパスでチェックするためここで行う
                            if (!alertInvisibleItemsOnPaste(files, checkPath: true, checkAttribute: true))
                            {
                                return;
                            }

                            addToUserAddFileList(movedFiles);
                            fo.PerformOperations();
                        }

                    }
                    else if (InVirtualDirectory)
                    {
                        if (cl.ToLower().EndsWith(".sor"))
                        {
                            if (!alertInvisibleItemsOnPaste(files, checkPath: true, checkAttribute: true))
                            {
                                return;
                            }

                            // 多くの項目を addOrReloadItem で読み込もうとすると
                            // その分だけ Task がたまりそれが終わるまで画面が更新されない
                            // その場合は単純にリロードを行う
                            var callAddOrReloadItem = files.Length <= Environment.ProcessorCount;
                            if (callAddOrReloadItem) addToUserAddFileList(files);

                            addToList(files, append: true, sorPath: cl, addToBookmark: false);

                            //UpdatePreview();
                            if (callAddOrReloadItem)
                            {
                                foreach (var file in files) addOrReloadItem(file, thumbnailChanged: true, startingGuid: startingGuid);
                            }
                            else
                            {
                                UpdatePreview();
                            }
                        }
                    }
                }
                else
                {
                    if (!InRealDirectory || !data.GetDataPresent(NativeMethods.CFSTR_FILEDESCRIPTORW) || !data.GetDataPresent(NativeMethods.ZIPPLA_FILECONTENTS)) return;
                    //var dataEx = data as DataObjectEx;
                    //if (dataEx == null) return;

                    var ZipPlaPERFORMEDDROPEFFECTExists = data.GetDataPresent(NativeMethods.ZIPPLA_PERFORMEDDROPEFFECT);

                    try
                    {
                        if (ZipPlaPERFORMEDDROPEFFECTExists)
                        {
                            data.GetData(NativeMethods.ZIPPLA_PERFORMEDDROPEFFECT);
                        }

                        var descriptorData = data.GetData(NativeMethods.CFSTR_FILEDESCRIPTORW) as Stream;
                        if (descriptorData == null) return;

                        DataObjectEx.FILEDESCRIPTOR[] fileDescriptors;
                        using (descriptorData)
                        {
                            fileDescriptors = DataObjectEx.FILEDESCRIPTOR.FromStreamToArray(descriptorData);
                        }
                        if (fileDescriptors.Length == 0) return;


                        //var ienumFormat = idata.EnumFormatEtc(DATADIR.DATADIR_GET);


                        //var rgelt = new System.Runtime.InteropServices.ComTypes.FORMATETC[1];
                        //if (ienumFormat.Next(fileDescriptors.Length, rgelt, null) != 0) return; // S_OK = 0

                        //const int S_OK = 0;
                        //while (ienumFormat.Next(1, rgelt, null) == S_OK)

                        var dstPaths = new string[fileDescriptors.Length];

                        for (var i = 0; i < fileDescriptors.Length; i++)
                        {
                            var desc = fileDescriptors[i];

                            var name = desc.cFileName;
                            var path = MakeCopyNameIfExists(cl, ref name);
                            dstPaths[i] = path;
                        }

                        if (!alertInvisibleItemsOnPaste(dstPaths, checkPath: true, checkAttribute: false))
                        {
                            return;
                        }

                        for (var i = 0; i < fileDescriptors.Length; i++)
                        {
                            var desc = fileDescriptors[i];

                            var path = dstPaths[i];

                            // iStream の read は size が int なので使用しない
                            //using (var content = data.GetData(NativeMethods.CFSTR_FILECONTENTS) as Stream)
                            using (var ms = data.GetData(NativeMethods.ZIPPLA_FILECONTENTS) as Stream)
                            using (var file = new FileStream(path, FileMode.CreateNew))
                            {
                                ms.CopyTo(file);
                            }

                            System.Runtime.InteropServices.ComTypes.FILETIME fileTime;

                            fileTime = desc.ftCreationTime;
                            if (fileTime.dwLowDateTime != 0 || fileTime.dwHighDateTime != 0)
                            {
                                try
                                {
                                    var dateTime = DateTime.FromFileTime(((long)fileTime.dwHighDateTime) << 32 | (long)fileTime.dwLowDateTime);
                                    File.SetCreationTime(path, dateTime);
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                }
                            }

                            fileTime = desc.ftLastAccessTime;
                            if (fileTime.dwLowDateTime != 0 || fileTime.dwHighDateTime != 0)
                            {
                                try
                                {
                                    var dateTime = DateTime.FromFileTime(((long)fileTime.dwHighDateTime) << 32 | (long)fileTime.dwLowDateTime);
                                    File.SetLastAccessTime(path, dateTime);
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                }
                            }

                            fileTime = desc.ftLastWriteTime;
                            if (fileTime.dwLowDateTime != 0 || fileTime.dwHighDateTime != 0)
                            {
                                try
                                {
                                    var dateTime = DateTime.FromFileTime(((long)fileTime.dwHighDateTime) << 32 | (long)fileTime.dwLowDateTime);
                                    File.SetLastWriteTime(path, dateTime);
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (data.GetDataPresent(NativeMethods.CFSTR_FILEDESCRIPTORW))
                        {
                            data.GetData(NativeMethods.CFSTR_FILEDESCRIPTORW);
                        }
                        if (ZipPlaPERFORMEDDROPEFFECTExists)
                        {
                            data.GetData(NativeMethods.ZIPPLA_PERFORMEDDROPEFFECT);
                        }
                    }


                }
            }
            catch (System.Runtime.InteropServices.COMException) { } // fo がエラーを表示するのでこちらでは不要
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // bool alertInvisibleItemsOnPaste(IEnumerable<string> dsts, bool checkPath, bool checkAttribute)

        /// <summary>
        /// 貼り付け先 directory に name がすでにあれば、「名前 - コピー.拡張子」「名前 - コピー (2).拡張子」… と空いている名前に変える。
        /// 貼り付け先のパスを返す
        /// </summary>
        private static string MakeCopyNameIfExists(string directory, ref string name)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            var baseName = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            name = baseName + " - " + Message.Copy + ext;
            path = Path.Combine(directory, name);
            var digit = 2;
            while (File.Exists(path) || Directory.Exists(path))
            {
                name = baseName + " - " + Message.Copy + " (" + digit++ + ")" + ext;
                path = Path.Combine(directory, name);
            }
            return path;
        }

        /// <summary>
        /// FileDrop の項目を target へ移動・コピーする操作を fo に積む（実行は呼び出し側の PerformOperations）。
        /// 同じ場所へのコピーは名前を変える。移動・コピー先のパスを返す
        /// </summary>
        private static List<string> queueFileDropOperations(MsdnMag.FileOperation fo, string[] files, string target, bool move)
        {
            var moveOrCopy = move ? (Action<string, string, string>)fo.MoveItem : fo.CopyItem;
            var movedFiles = new List<string>();
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var path = Path.Combine(target, name);
                if (!move && file == path) path = MakeCopyNameIfExists(target, ref name);
                movedFiles.Add(path);
                moveOrCopy(file, target, name);
            }
            return movedFiles;
        }

        public static void pasteToTarget(CatalogForm owner, string target, System.Windows.Forms.IDataObject data, DragDropEffects? dragDropEffects = null)
        {
            try
            {
                // "C:" のようなドライブ名だけなら、カレントディレクトリではなくドライブのルートを指すように区切りを付ける
                if (!string.IsNullOrEmpty(target) && target[target.Length - 1] == ':') target += Path.DirectorySeparatorChar;
                if (data.GetDataPresent(DataFormats.FileDrop))
                {

                    var files = (string[])data.GetData(DataFormats.FileDrop);
                    if (files.Length <= 0) return;

                    DragDropEffects dde = dragDropEffects == null ? GetPreferredDropEffect(data) : (DragDropEffects)dragDropEffects;
                    bool move = dde == DragDropEffects.Move; // 他のプログラムとの互換性を考慮し (DragDropEffects.Copy | DragDropEffects.Link) でなくともコピーとみなす

                    using (var fo = new MsdnMag.FileOperation(callbackSink: null, owner: owner))
                    {
                        var movedFiles = queueFileDropOperations(fo, files, target, move);
                        fo.PerformOperations();
                    }
                }
                else
                {
                    if (!data.GetDataPresent(NativeMethods.CFSTR_FILEDESCRIPTORW) || !data.GetDataPresent(NativeMethods.ZIPPLA_FILECONTENTS)) return;

                    var ZipPlaPERFORMEDDROPEFFECTExists = data.GetDataPresent(NativeMethods.ZIPPLA_PERFORMEDDROPEFFECT);

                    try
                    {
                        if (ZipPlaPERFORMEDDROPEFFECTExists)
                        {
                            data.GetData(NativeMethods.ZIPPLA_PERFORMEDDROPEFFECT);
                        }

                        var descriptorData = data.GetData(NativeMethods.CFSTR_FILEDESCRIPTORW) as Stream;
                        if (descriptorData == null) return;

                        DataObjectEx.FILEDESCRIPTOR[] fileDescriptors;
                        using (descriptorData)
                        {
                            descriptorData.Seek(0, SeekOrigin.Begin); // ペーストでは元側が行っているため省略しているが通常はこれが必要
                            fileDescriptors = DataObjectEx.FILEDESCRIPTOR.FromStreamToArray(descriptorData);
                        }
                        if (fileDescriptors.Length == 0) return;

                        var dstPaths = new string[fileDescriptors.Length];

                        for (var i = 0; i < fileDescriptors.Length; i++)
                        {
                            var desc = fileDescriptors[i];

                            var name = desc.cFileName;
                            var path = MakeCopyNameIfExists(target, ref name);
                            dstPaths[i] = path;
                        }

                        for (var i = 0; i < fileDescriptors.Length; i++)
                        {
                            var desc = fileDescriptors[i];

                            var path = dstPaths[i];

                            // iStream の read は size が int なので使用しない
                            //using (var content = data.GetData(NativeMethods.CFSTR_FILECONTENTS) as Stream)
                            using (var ms = data.GetData(NativeMethods.ZIPPLA_FILECONTENTS) as Stream)
                            using (var file = new FileStream(path, FileMode.CreateNew))
                            {
                                ms.Seek(0, SeekOrigin.Begin); // ペーストでは元側が行っているため省略しているが通常はこれが必要
                                ms.CopyTo(file);
                            }

                            System.Runtime.InteropServices.ComTypes.FILETIME fileTime;

                            fileTime = desc.ftCreationTime;
                            if (fileTime.dwLowDateTime != 0 || fileTime.dwHighDateTime != 0)
                            {
                                try
                                {
                                    var dateTime = DateTime.FromFileTime(((long)fileTime.dwHighDateTime) << 32 | (long)(uint)fileTime.dwLowDateTime);
                                    File.SetCreationTime(path, dateTime);
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                }
                            }

                            fileTime = desc.ftLastAccessTime;
                            if (fileTime.dwLowDateTime != 0 || fileTime.dwHighDateTime != 0)
                            {
                                try
                                {
                                    var dateTime = DateTime.FromFileTime(((long)fileTime.dwHighDateTime) << 32 | (long)(uint)fileTime.dwLowDateTime);
                                    File.SetLastAccessTime(path, dateTime);
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                }
                            }

                            fileTime = desc.ftLastWriteTime;
                            if (fileTime.dwLowDateTime != 0 || fileTime.dwHighDateTime != 0)
                            {
                                try
                                {
                                    var dateTime = DateTime.FromFileTime(((long)fileTime.dwHighDateTime) << 32 | (long)(uint)fileTime.dwLowDateTime);
                                    File.SetLastWriteTime(path, dateTime);
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (data.GetDataPresent(NativeMethods.CFSTR_FILEDESCRIPTORW))
                        {
                            data.GetData(NativeMethods.CFSTR_FILEDESCRIPTORW);
                        }
                        if (ZipPlaPERFORMEDDROPEFFECTExists)
                        {
                            data.GetData(NativeMethods.ZIPPLA_PERFORMEDDROPEFFECT);
                        }
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException) { } // fo がエラーを表示するのでこちらでは不要
            catch (Exception e)
            {
                MessageBox.Show(owner, e.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private DataObject GetSelectedDataObject(bool forClipboard)
        {
            DataObject result;
            DragDropEffects dummy;
            GetSelectedDataObject(out result, out dummy, forClipboard);
            return result;
        }

        private PackedImageLoaderWithEntries packedImageLoaderForClipboard;

        private readonly object packedImageLoaderForClipboardLocker = new object();

        private void packedImageLoaderForClipboard_Dispose()
        {
            lock (packedImageLoaderForClipboardLocker)
            {
                packedImageLoaderForClipboard?.Dispose();
                packedImageLoaderForClipboard = null;
            }
        }

        readonly Guid GetSelectedDataObject_ClipboardGuid = Guid.NewGuid(); // OpenedByExternalApplication_Dictionary のキー（new Guid() は Guid.Empty になり、ほかのキーと重なりうる）

        private void GetSelectedDataObject(out DataObject dataObject, out DragDropEffects effects, bool forClipboard)
        {
            dataObject = null;
            effects = DragDropEffects.None;
            PackedImageLoaderWithEntries pil;
            string archivePath = null;
            if (InDirectoryVirtualDirectoryOrSmartDirectory)
            {
                pil = null;
            }
            else if (InArchive)
            {
                pil = loadingPackedImageLoader;
                if (pil == null) return;
                archivePath = currentLocation;
                if (archivePath == null || !PackedImageLoader.Supports(archivePath)) return;
            }
            else return;
            if (CreatingZipPathArray) return;
            var indices = tvCatalog.SelectedIndices;
            if (indices == null || indices.Length == 0) return;

            if (pil == null)
            {
                var paths = ZipPathArray;
                if (paths == null) return;
                var files = new string[indices.Length];
                for (var i = 0; i < indices.Length; i++)
                {
                    var index = indices[i];
                    if (index < 0 || index >= paths.Length) return;
                    var file = paths[index];
                    if (file.Contains(Path.AltDirectorySeparatorChar)) return;
                    files[i] = file;
                }
                dataObject = new DataObject(DataFormats.FileDrop, files);

                effects = DragDropEffects.All | DragDropEffects.Link;
                return;
            }
            else
            {
                DataObjectEx.SelectedItem[] SelectedItems = new DataObjectEx.SelectedItem[indices.Length];
                var entries = pil.Entries;
                object locker;
                if (forClipboard)
                {
                    locker = packedImageLoaderForClipboardLocker;
                    PackedImageLoader.PackedImageEntry[] entries2;
                    lock (locker)
                    {
                        packedImageLoaderForClipboard?.Dispose();
                        packedImageLoaderForClipboard = null;
                        var pureLoader = CreatePackedImageLoader(archivePath);
                        try
                        {
                            packedImageLoaderForClipboard = new PackedImageLoaderWithEntries(pureLoader, needToExecOnTask: true);
                        }
                        catch
                        {
                            pureLoader.Dispose();
                            return;
                        }
                        // 表示中の一覧（entries）と、クリップボード用に開き直した書庫の一覧を比べる。
                        // 以前は pil.Entries（entries と同じもの）と比べていて、書庫が変わっても検出できなかった
                        entries2 = packedImageLoaderForClipboard.Entries;
                    }
                    var diff = false;
                    if (entries.Length != entries2.Length) diff = true;
                    if (!diff)
                    {
                        for (var i = 0; i < entries.Length; i++)
                        {
                            var e1 = entries[i];
                            var e2 = entries2[i];
                            if (e1.Path != e2.Path)
                            {
                                diff = true;
                                break;
                            }
                        }
                    }
                    if (diff)
                    {
                        lock (locker)
                        {
                            packedImageLoaderForClipboard.Dispose();
                            packedImageLoaderForClipboard = null;
                        }
                        return;
                    }
                    pil = packedImageLoaderForClipboard;
                }
                else
                {
                    locker = loadingPackedImageLoaderLocker;
                }
                var pil0 = pil.PackedImageLoader;
                var now = default(DateTime);
                for (Int32 ItemCount = 0; ItemCount < SelectedItems.Length; ItemCount++)
                {
                    var index = indices[ItemCount];
                    if (index >= entries.Length)
                    {
                        if (forClipboard)
                        {
                            lock (locker)
                            {
                                packedImageLoaderForClipboard.Dispose();
                                packedImageLoaderForClipboard = null;
                            }
                        }
                        return;
                    }
                    var entry = entries[index];

                    // TODO: Get virtual file name
                    SelectedItems[ItemCount].FileName = Path.GetFileName(entry.Path);
                    // TODO: Get virtual file date
                    if (entry.LastWriteTime != DateTime.MinValue)
                    {
                        SelectedItems[ItemCount].WriteTime = entry.LastWriteTime;
                    }
                    else
                    {
                        if (now == default(DateTime)) now = DateTime.Now;
                        SelectedItems[ItemCount].WriteTime = now;
                    }
                    // TODO: Get virtual file size
                    SelectedItems[ItemCount].FileSize = entry.Size;
                }

                var GetFileContentsResults = new MemoryStream[SelectedItems.Length];

                DataObjectEx dataObjectEx = new DataObjectEx(SelectedItems);
                var RecalcItemsCalled = false;
                dataObjectEx.RecalcItems = selectedItems =>
                {
                    if (RecalcItemsCalled) return;
                    RecalcItemsCalled = true;

                    lock (locker)
                    {
                        // ロックの中で読む（外で読むと、比べる前に別のスレッドが差し替え・破棄しうる）
                        var baseLoader = forClipboard ? packedImageLoaderForClipboard : loadingPackedImageLoader;
                        if (baseLoader == pil)
                        {
                            string digits = null;
                            for (var i = 0; i < selectedItems.Length; i++)
                            {
                                var item = selectedItems[i];
                                if (item.FileSize < 0)
                                {
                                    var entry = entries[indices[i]];
                                    var result0 = pil0.GetStream(entry, out var extension, callback: true);
                                    MemoryStream r = null;
                                    if (digits == null)
                                    {
                                        digits = "0";
                                        var count = entries.Length;
                                        while (count >= 10)
                                        {
                                            digits += "0";
                                            count /= 10;
                                        }
                                    }
                                    try
                                    {
                                        r = ConvertToMemoryStream(result0);
                                        result0 = null;
                                        //string extension;
                                        //r = GetImageType(r, out extension);
                                        item.FileName = digits.Substring(item.FileName.Length) + item.FileName + extension;
                                        item.FileSize = r.Length;
                                        selectedItems[i] = item;
                                        GetFileContentsResults[i] = r;
                                    }
                                    catch
                                    {
                                        result0?.Dispose();
                                        r?.Dispose();
                                        for (var j = 0; j < GetFileContentsResults.Length; j++)
                                        {
                                            var result = GetFileContentsResults[j];
                                            if (result != null)
                                            {
                                                result.Dispose();
                                                GetFileContentsResults[j] = null;
                                            }
                                            else break;
                                        }
                                        return;
                                    }
                                }
                            }
                        }
                    }
                };
                dataObjectEx.GetFileContents = (item, itemCount) =>
                {
                    var result0 = GetFileContentsResults[itemCount];
                    if (result0 != null)
                    {
                        GetFileContentsResults[itemCount] = null;
                        return result0;
                    }
                    else
                    {
                        try
                        {
                            var entry = entries[indices[itemCount]];
                            var baseLoader = forClipboard ? packedImageLoaderForClipboard : loadingPackedImageLoader;
                            lock (locker)
                            {
                                if (baseLoader == pil)
                                {
                                    var stream = pil0.GetStream(entry, out var dummy, callback: true);
                                    try
                                    {
                                        return ConvertToMemoryStream(stream);
                                    }
                                    catch
                                    {
                                        stream.Dispose();
                                        throw;
                                    }
                                }
                                else
                                {
                                    // 例外のスローでコピーをキャンセル
                                    throw new Exception();
                                }
                            }
                        }
                        catch
                        {
                            for (var j = 0; j < GetFileContentsResults.Length; j++)
                            {
                                var result = GetFileContentsResults[j];
                                if (result != null)
                                {
                                    result.Dispose();
                                    GetFileContentsResults[j] = null;
                                }
                            }
                            throw;
                        }
                    }
                };
                dataObjectEx.SetData(NativeMethods.CFSTR_FILEDESCRIPTORW, null);
                dataObjectEx.SetData(NativeMethods.CFSTR_FILECONTENTS, null);
                dataObjectEx.SetData(NativeMethods.ZIPPLA_FILECONTENTS, null);
                dataObjectEx.SetData(NativeMethods.ZIPPLA_PERFORMEDDROPEFFECT, null);
                dataObjectEx.SetData(NativeMethods.CFSTR_PERFORMEDDROPEFFECT, null);
                dataObject = dataObjectEx;
                effects = DragDropEffects.Copy;
                return;
            }
        }

        public static MemoryStream ConvertToMemoryStream(Stream stream)
        {
            var result = stream as MemoryStream;
            if (result != null) return result;
            result = new MemoryStream();
            try
            {
                stream.CopyTo(result);
            }
            catch
            {
                result.Dispose();
                throw;
            }
            stream.Dispose();
            return result;
        }

        private void list_DragReady(object sender, MouseEventArgs e)
        {
            DragReady(sender, e);
        }

        private void pasteOnBackGroundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            pasteToCurrentLocation();
        }

        private void cbSortBy_KeyDown(object sender, KeyEventArgs e)
        {
            // FocusIfRightness が使われており、なおかつコピーなどの操作が意味を持たないので
            var mk = ModifierKeys;
            if (commonKeyAction(e, mk))
            {
                return;
            }
        }

        private void clearFilterWhenMovingLocationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (FilterRemovingMode == FilterRemovingMode.WhenMovingLocation)
                FilterRemovingMode = FilterRemovingMode.Never;
            else
                FilterRemovingMode = FilterRemovingMode.WhenMovingLocation;
        }

        private void fromTheLongestNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetNumberOfLinesOfName(NumberOfLinesOfName.Full, refresh: true);
        }

        private void automaticlessToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetNumberOfLinesOfName(NumberOfLinesOfName.Automatic1, refresh: true);
        }

        private void automaticgreaterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetNumberOfLinesOfName(NumberOfLinesOfName.Automatic2, refresh: true);
        }

        private void NumOfLineOfName1toolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetNumberOfLinesOfName(NumberOfLinesOfName.One, refresh: true);
        }

        private void NumOfLineOfName2toolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetNumberOfLinesOfName(NumberOfLinesOfName.Two, refresh: true);
        }

        private void NumOfLineOfName3toolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetNumberOfLinesOfName(NumberOfLinesOfName.Three, refresh: true);
        }

        private void noneFolderArrangeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetFolderSortMode(FolderSortMode.None, reSort: true);
        }

        private void displayFirstToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetFolderSortMode(FolderSortMode.Ahead, reSort: true);
        }

        private void displayLastToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetFolderSortMode(FolderSortMode.Behind, reSort: true);
        }

        private void fitToDirectionOfSortToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetFolderSortMode(FolderSortMode.FitToDirectionOfSort, reSort: true);
        }

        private void loadLastViewedPageFromHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LoadLastViewedPageFromHistory = !LoadLastViewedPageFromHistory;
            SetFileListPageHeaderWidth();
            if (ArchiveLoadLevel != ItemLoadLevel.Ignore || IsDisplay(SubfolderMode))
            {
                UpdatePreview();
            }
        }

        private void allowReadaheadProcessToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AllowReadAheadProcess = !AllowReadAheadProcess;
        }
    }
}
