using Alteridem.WinTouch;
using Common;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZipPla
{
    public partial class ViewerForm
    {
        private OpenResult OpenFile(string path, int startPage, SortModeDetails sort = null, string startName = null, bool forLookAhead = false)//, BindingMode? binding = null)
        {
            ImageLoader.SusieReset();
            path = Program.GetFullPath(path);
            if (bmwLoadEachPage.IsBusy)
            {
                bmwLoadEachPage.CancelAsync();
            }
            nextData?.Dispose();
            nextData = null;

            // currentArchiveFilePath を書き換える前に今開いているファイルを履歴に保存
            if (ipcLookAheadInfo == null && !DoNotSaveSetting && !string.IsNullOrEmpty(currentStartingFilePath) &&
                !string.IsNullOrEmpty(currentArchiveFilePath) && currentPage >= 0)
            {
                //var page = currentStartingFilePath == currentArchiveFilePath ? Math.Max(0, currentPage) : -1;
                if (saveToHistory)
                {
                    trySaveCurrentConditionToHistory(SelfLastAccessTimeUpdateMode.Reserve); // ここでアクセス日時が更新される
                }
            }

            string name = null;
            currentStartingFilePath = path;

            bool sortShouldBeUsed;

            char separator = Path.DirectorySeparatorChar;
            
            try
            {
                //if (ImageLoader.SupportsFullReading(path))
                if (!PackedImageLoader.Supports(path) && !Directory.Exists(path))
                {
                    var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
                    string container = null;
                    if (altPos >= 0 && altPos < path.Length - 1)
                    {
                        container = path.Substring(0, altPos);
                        try
                        {
                            if (!File.Exists(container)) container = null;
                        }
                        catch
                        {
                            container = null;
                        }
                    }
                    if (container == null)
                    {
                        if (ImageLoader.SupportsFullReading(path, ffmpegExists: true) && File.Exists(path))
                        {
                            currentArchiveFilePath = Path.GetDirectoryName(path);
                            if (currentArchiveFilePath.Last() == Path.DirectorySeparatorChar)
                            {
                                currentArchiveFilePath = currentArchiveFilePath.Substring(0, currentArchiveFilePath.Length - 1);
                            }
                            name = Path.GetFileName(path);
                            sortShouldBeUsed = true;
                        }
                        else
                        {
                            currentArchiveFilePath = path;
                            sortShouldBeUsed = false;
                        }
                    }
                    else
                    {
                        currentArchiveFilePath = container;
                        name = path.Substring(altPos + 1);
                        sortShouldBeUsed = true;
                        separator = Path.AltDirectorySeparatorChar;
                    }

                }
                else
                {
                    currentArchiveFilePath = path;
                    sortShouldBeUsed = false;
                }
            }
            catch
            {
                currentArchiveFilePath = path;
                name = null;
                sortShouldBeUsed = false;
            }

            currentSortModeDetails = sort?.Clone(); // 参照渡しでは requested も変更されてしまう

            PackedImageLoader nextLoaderBase;
            PackedImageLoader.PackedImageEntry[] nextEntriesBase;
            string[] nextEntriesPathBase;
            try
            {
                if (ipcLookAheadInfo == null)
                {
                    Program.TryUpdateLastAccessTime(path); // 先読みでなければファイルを開く前もアクセス日時を更新

                    // 先読み中でなければ選択ファイル変更のリクエスト
                    // 画像ファイルをドロップした場合にその親フォルダが指定される currentArchiveFilePath は不適切
                    if (!mtbPage_ValueChanged_SendRequestToChangeSelection_Stop)
                    {
                        if (!string.IsNullOrEmpty(path))
                        {
                            SendRequestToChangeSelection(path, requestedSortModeDetails?.SortMode); // 第二引数がない場合ページ送り用のソートモードが利用される
                        }
                    }
                }

                nextLoaderBase = getPackedImageLoader(currentArchiveFilePath, forLookAhead ? ViewerFormOnMemoryMode.ForLookAhead : ViewerFormOnMemoryMode.Default);
                var nextLoader = nextLoaderBase;
                if (!sortShouldBeUsed)
                {
                    // Full でない方は一部の画像をスキップする。
                    nextEntriesBase = applyFilterAndReturnItsSelf(nextLoader, nextLoader.GetPackedImageEntries(needToExecOnTask: true)/*, noOp: nextLoader.Type != PackedImageLoader.PackType.Directory*/).ToArray();
                    if (sort == null) currentSortModeDetails = new SortModeDetails();
                    currentSortModeDetails.SortMode = SortMode.NameInAsc;
                }
                else if (sort == null || sort.SortMode == SortMode.NameInAsc)
                {
                    // 画像ファイル指定の場合は NameInAsc でもスキップしない
                    // それが本当に最適か、ここの仕様設計は難しいが。
                    //nextEntriesBase = applyFilterAndReturnItsSelf(nextLoader.GetPackedImageFullEntries(needToExecOnTask: true), noOp: nextLoader.Type != PackedImageLoader.PackType.Directory)
                    //    .OrderBy(entry => entry.Path, new LogicalStringComparer()).ToArray();


                    nextEntriesBase = getNameSortedEntries(applyFilterAndReturnItsSelf(nextLoader, nextLoader.GetPackedImageFullEntries(needToExecOnTask: true)/*, noOp: nextLoader.Type != PackedImageLoader.PackType.Directory*/)
                        , nextLoader.Type, desc: false);
                    if (sort == null)
                    {
                        currentSortModeDetails = new SortModeDetails();
                        currentSortModeDetails.SortMode = SortMode.NameInAsc;
                    }
                }
                else if (sort.SortMode == SortMode.NameInDesc)
                {
                    //nextEntriesBase = applyFilterAndReturnItsSelf(nextLoader.GetPackedImageFullEntries(needToExecOnTask: true), noOp: nextLoader.Type != PackedImageLoader.PackType.Directory)
                    //    .OrderByDescending(entry => entry.Path, new LogicalStringComparer()).ToArray();


                    nextEntriesBase = getNameSortedEntries(applyFilterAndReturnItsSelf(nextLoader, nextLoader.GetPackedImageFullEntries(needToExecOnTask: true)/*, noOp: nextLoader.Type != PackedImageLoader.PackType.Directory*/)
                        , nextLoader.Type, desc: true);
                }
                else if (sort.SortMode == SortMode.Random && sort.RandomSeed != null)
                {
                    var temp = applyFilterAndReturnItsSelf(nextLoader, nextLoader.GetPackedImageFullEntries(needToExecOnTask: true)/*, noOp: nextLoader.Type != PackedImageLoader.PackType.Directory*/);
                    var withSep = currentArchiveFilePath + separator;
                    var seed = sort.RandomSeed;
                    var table = (from e in temp select CatalogForm.GetRandomIndex(withSep + e.Path, seed)).ToArray();
                    var order = new int[table.Length]; for (var i = 0; i < order.Length; i++) order[i] = i;
                    nextEntriesBase = (from i in order orderby table[i] select temp[i]).ToArray();
                }
                else if (OpenFile_SuportedSortModes.Contains(sort.SortMode))
                {
                    // 疑似安定ソートが必要なパターン
                    var modes = sort.SortMode == sort.PreSortMode ?
                        new SortMode[1] { sort.SortMode } :
                        new SortMode[2] { sort.PreSortMode, sort.SortMode };
                    PackedImageLoaderFileInfo needed = 0;
                    if (sort.SortMode == SortMode.ModifiedInAsc || sort.SortMode == SortMode.ModifiedInDesc ||
                       sort.PreSortMode == SortMode.ModifiedInAsc || sort.PreSortMode == SortMode.ModifiedInDesc)
                        needed |= PackedImageLoaderFileInfo.LastWriteTime;
                    if (sort.SortMode == SortMode.SizeInAsc || sort.SortMode == SortMode.SizeInDesc ||
                       sort.PreSortMode == SortMode.SizeInAsc || sort.PreSortMode == SortMode.SizeInDesc)
                        needed |= PackedImageLoaderFileInfo.Size;
                    //var sw = new System.Diagnostics.Stopwatch();
                    //sw.Start();
                    nextEntriesBase = applyFilterAndReturnItsSelf(nextLoader, nextLoader.GetPackedImageFullEntries(needed, needToExecOnTask: true)/*, noOp: nextLoader.Type != PackedImageLoader.PackType.Directory*/).ToArray();
                    //sw.Stop();MessageBox.Show(sw.Elapsed.ToString());
                    foreach (var sMode in modes)
                    {
                        switch (sMode)
                        {
                            case SortMode.TypeInAsc:
                                {
                                    var typeArray = CatalogForm.GetTypeNameArray((from e in nextEntriesBase select e.Path).ToArray());
                                    var order = new int[typeArray.Length]; for (var i = 0; i < order.Length; i++) order[i] = i;
                                    nextEntriesBase = order.
                                        OrderBy(x => typeArray[x], new LogicalStringComparer()).
                                        Select(i => nextEntriesBase[i]).ToArray();
                                    break;
                                }
                            case SortMode.TypeInDesc:
                                {
                                    var typeArray = CatalogForm.GetTypeNameArray((from e in nextEntriesBase select e.Path).ToArray());
                                    var order = new int[typeArray.Length]; for (var i = 0; i < order.Length; i++) order[i] = i;
                                    nextEntriesBase = order.
                                        OrderByDescending(x => typeArray[x], new LogicalStringComparer()).
                                        Select(i => nextEntriesBase[i]).ToArray();
                                    break;
                                }
                            case SortMode.NameInAsc:
                                //nextEntriesBase = nextEntriesBase.OrderBy(e => e.Path, new LogicalStringComparer()).ToArray();
                                nextEntriesBase = getNameSortedEntries(nextEntriesBase, nextLoader.Type, desc: false);
                                break;
                            case SortMode.NameInDesc:
                                //nextEntriesBase = nextEntriesBase.OrderByDescending(e => e.Path, new LogicalStringComparer()).ToArray();
                                nextEntriesBase = getNameSortedEntries(nextEntriesBase, nextLoader.Type, desc: true);
                                break;
                            case SortMode.RatingInAsc:
                                {
                                    var rateArray = (from e in nextEntriesBase let r = ZipPlaInfo.GetOnlyRating(e.Path) select r > 0 ? r : int.MaxValue).ToArray();
                                    var order = new int[rateArray.Length]; for (var i = 0; i < order.Length; i++) order[i] = i;
                                    nextEntriesBase = order.
                                        OrderBy(x => rateArray[x]).
                                        Select(i => nextEntriesBase[i]).ToArray();
                                    break;
                                }
                            case SortMode.RatingInDesc:
                                {
                                    var rateArray = (from e in nextEntriesBase let r = ZipPlaInfo.GetOnlyRating(e.Path) select r).ToArray();
                                    var order = new int[rateArray.Length]; for (var i = 0; i < order.Length; i++) order[i] = i;
                                    nextEntriesBase = order.
                                        OrderByDescending(x => rateArray[x]).
                                        Select(i => nextEntriesBase[i]).ToArray();
                                    break;
                                }
                            case SortMode.CreatedInAsc:
                                {
                                    if (nextLoader.Type != PackedImageLoader.PackType.Directory) break;
                                    var dateArray = (from e in nextEntriesBase
                                                     select Program.GetCreateTimeOfFile(currentArchiveFilePath + Path.DirectorySeparatorChar + e.Path)).ToArray();
                                    var order = new int[dateArray.Length]; for (var i = 0; i < order.Length; i++) order[i] = i;
                                    nextEntriesBase = order.
                                        OrderBy(x => dateArray[x]).
                                        Select(i => nextEntriesBase[i]).ToArray();
                                    break;
                                }
                            case SortMode.CreatedInDesc:
                                {
                                    if (nextLoader.Type != PackedImageLoader.PackType.Directory) break;
                                    var dateArray = (from e in nextEntriesBase
                                                     select Program.GetCreateTimeOfFile(currentArchiveFilePath + Path.DirectorySeparatorChar + e.Path)).ToArray();
                                    var order = new int[dateArray.Length]; for (var i = 0; i < order.Length; i++) order[i] = i;
                                    nextEntriesBase = order.
                                        OrderByDescending(x => dateArray[x]).
                                        Select(i => nextEntriesBase[i]).ToArray();
                                    break;
                                }
                            case SortMode.AccessedInAsc:
                                {
                                    if (nextLoader.Type != PackedImageLoader.PackType.Directory) break;
                                    var dateArray = (from e in nextEntriesBase
                                                     select Program.GetLastAccessTimeOfFile(currentArchiveFilePath + Path.DirectorySeparatorChar + e.Path)).ToArray();
                                    var order = new int[dateArray.Length]; for (var i = 0; i < order.Length; i++) order[i] = i;
                                    nextEntriesBase = order.
                                        OrderBy(x => dateArray[x]).
                                        Select(i => nextEntriesBase[i]).ToArray();
                                    break;
                                }
                            case SortMode.AccessedInDesc:
                                {
                                    if (nextLoader.Type != PackedImageLoader.PackType.Directory) break;
                                    var dateArray = (from e in nextEntriesBase
                                                     select Program.GetLastAccessTimeOfFile(currentArchiveFilePath + Path.DirectorySeparatorChar + e.Path)).ToArray();
                                    var order = new int[dateArray.Length]; for (var i = 0; i < order.Length; i++) order[i] = i;
                                    nextEntriesBase = order.
                                        OrderByDescending(x => dateArray[x]).
                                        Select(i => nextEntriesBase[i]).ToArray();
                                    break;
                                }
                            case SortMode.ModifiedInAsc:
                                {
                                    nextEntriesBase = nextEntriesBase.OrderBy(e => e.LastWriteTime).ToArray();
                                    break;
                                }
                            case SortMode.ModifiedInDesc:
                                {
                                    nextEntriesBase = nextEntriesBase.OrderByDescending(e => e.LastWriteTime).ToArray();
                                    break;
                                }
                            case SortMode.SizeInAsc:
                                {
                                    nextEntriesBase = nextEntriesBase.OrderBy(e => e.Size).ToArray();
                                    break;
                                }
                            case SortMode.SizeInDesc:
                                {
                                    nextEntriesBase = nextEntriesBase.OrderByDescending(e => e.Size).ToArray();
                                    break;
                                }
                            default:
                                {
                                    nextEntriesBase = nextEntriesBase.OrderBy(e => e.Path, new LogicalStringComparer()).ToArray();
                                    break;
                                }
                        }
                    }
                }
                else
                {
                    nextEntriesBase = applyFilterAndReturnItsSelf(nextLoader, nextLoader.GetPackedImageFullEntries(needToExecOnTask: true)/*, noOp: nextLoader.Type != PackedImageLoader.PackType.Directory*/)
                        .OrderBy(entry => entry.Path, new LogicalStringComparer()).ToArray();
                    currentSortModeDetails.SortMode = SortMode.NameInAsc;
                }

                //nextEntries = nextEntriesBase.Select(entry => entry.PhysicalIndex).ToArray();
                var nextEntries = nextEntriesBase;
                var currentArchiveFilePathWithSeparator = currentArchiveFilePath + Path.AltDirectorySeparatorChar;
                nextEntriesPathBase = nextEntriesBase.Select(entry => currentArchiveFilePathWithSeparator + entry.Path).ToArray();
                if (nextEntries.Length <= 0)
                {
                    nextLoader.Dispose();
                    nextLoader = null;
                    currentArchiveFilePath = null;
                    currentStartingFilePath = null;
                    mtbPage.Maximum = 0;
                    if (ipcLookAheadInfo == null)
                    {
                        if (OpenFile_UserOpen)
                        {
                            Program.ShowCursor();
                            MessageBox.Show(this, Message.NoReadableFile, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            showCurrentPage();
                            return OpenResult.ShowWarning;
                        }
                        else
                        {
                            Program.exitException = new Exception(Message.NoReadableFile);
                            CloseWithoutSave();
                            showCurrentPage();
                            return OpenResult.CloseForm;
                        }
                    }
                    showCurrentPage();
                    return OpenResult.NoOperation;
                }
            }
            catch (Exception error)
            {
                nextData?.Dispose();
                nextData = null;
                currentArchiveFilePath = null;
                currentStartingFilePath = null;
                mtbPage.Maximum = 0;
                if (ipcLookAheadInfo == null)
                {
                    Exception error2;

                    if (error is FileNotFoundException)
                    {
                        error2 = error;
                    }
                    else
                    {
#if DEBUG
                        error2 = new Exception(Message.FailedToLoadFile + "\n\n" + error.ToString());
#else
                        error2 = new Exception(Message.FailedToLoadFile);
#endif
                    }
                    if (OpenFile_UserOpen)
                    {
                        Program.ShowCursor();
#if DEBUG
                        MessageBox.Show(this, error2.ToString(), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                        MessageBox.Show(this, error2.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
                        showCurrentPage();
                        return OpenResult.ShowWarning;
                    }
                    else
                    {
                        Program.exitException = error2;
                        CloseWithoutSave();
                        return OpenResult.CloseForm;
                    }
                }
                showCurrentPage();
                return OpenResult.NoOperation;
            }

            if (startName != null) name = startName;
            if (name != null)
            {
                for (var i = 0; i < nextEntriesBase.Length; i++)
                {
                    var e = nextEntriesBase[i];
                    if (e.Path == name)
                    {
                        startPage = i;
                        break;
                    }
                }
            }
            
            if (saveToHistory)
            {
                try
                {
                    //Program.TryUpdateLastAccessTime(path); // 既にファイルを開いているのでここでは不可
                    {
                        AddHistoryInBackground(path, Math.Max(0, startPage)); // V26
                        SendSorMessageToCatalogForm(path, Program.HistorySorPath, Math.Max(0, startPage), requestToUpdateLastAccessTime: false, m: SelfLastAccessTimeUpdateMode.None);
                    }
                }
                catch { }
            }

            mtbPage.Maximum = Math.Max(0, nextEntriesBase.Length - 1);
            btnOpenLeft.Enabled = btnOpenRight.Enabled = true;
            mtbPage_ValueChanged_SendRequestToChangeSelection_Stop = true;
            if (startPage < 0 || startPage >= nextEntriesBase.Length)
            {
                // この順で実行しないと無駄な再描画が行われてしまう
                currentPage = 0;
                mtbPage.Value = 0;
            }
            else
            {
                currentPage = startPage;
                mtbPage.Value = startPage;
            }
            mtbPage_ValueChanged_SendRequestToChangeSelection_Stop = false;
            //ShowCurrentPageToStatusBar();

            // seemToBeBook は廃止
            {
                onePageModeForNext = currentPage == 0 && CoverBindingMode/*requestedCoverBindingMode*/ == CoverBindingMode.ForceSingle;
            }
            CurrentDividedPosition = 0;

            //bmwLoadEachPage.RunWorkerAsyncWithInterrupt(nextEntries.Length, true); // キャンセルを待たない場合 OriginalImageArray を毎回破棄するようにする必要あり


            // bmwLoadEachPage が動き出す前に Paint が動いて古い ResizedImage が使われることの無いように
            clearResizedImageArray();

            // リロードでは不要、オープンでのみ使用
            IfTouchModifyingThenMoveOrigin(inverse: false);

            // currentArchiveFilePath が変更されてもいいようにデータを埋め込んでおく
            var arguments = new object[nextEntriesBase.Length];
            arguments[0] = Tuple.Create(currentArchiveFilePath, currentStartingFilePath, false, false); // loader が変化するので updateLoader はどちらでも同じ
            //OpenFileFinishedButStartingIsNotCalled = true;
            nextData = new NextData(nextEntriesPathBase, nextEntriesBase, nextLoaderBase);
            bmwLoadEachPage.RunWorkerAsyncWithInterrupt(arguments, true); // キャンセルを待たない場合 OriginalImageArray を毎回破棄するようにする必要あり

            if (ipcLookAheadInfo == null) OpenFile_UserOpen = true;

            return OpenResult.Success;
        }

        private static PackedImageLoader.PackedImageEntry[] getNameSortedEntries(List<PackedImageLoader.PackedImageEntry> list, PackedImageLoader.PackType type, bool desc)
        {
            var replaced = PackedImageLoader.GetSeparatorReplacedArray(list, type);

            if (desc)
            {
                return Enumerable.Range(0, list.Count).OrderByDescending(i => replaced[i], CatalogForm.GetSortArray_NaturalSort).Select(i => list[i]).ToArray();
            }
            else
            {
                return Enumerable.Range(0, list.Count).OrderBy(i => replaced[i], CatalogForm.GetSortArray_NaturalSort).Select(i => list[i]).ToArray();
            }
        }

        private static PackedImageLoader.PackedImageEntry[] getNameSortedEntries(PackedImageLoader.PackedImageEntry[] list, PackedImageLoader.PackType type, bool desc)
        {
            var replaced = PackedImageLoader.GetSeparatorReplacedArray(list, type);

            if (desc)
            {
                return Enumerable.Range(0, list.Length).OrderByDescending(i => replaced[i], CatalogForm.GetSortArray_NaturalSort).Select(i => list[i]).ToArray();
            }
            else
            {
                return Enumerable.Range(0, list.Length).OrderBy(i => replaced[i], CatalogForm.GetSortArray_NaturalSort).Select(i => list[i]).ToArray();
            }
        }

        private List<PackedImageLoader.PackedImageEntry> applyFilterAndReturnItsSelf(PackedImageLoader nextLoader, List<PackedImageLoader.PackedImageEntry> list/*, bool noOp*/)
        {
            if (searchManagerFromCatalogForm == null) return list;
            var noOp = nextLoader.Type != PackedImageLoader.PackType.Directory || currentArchiveFilePath == currentStartingFilePath;
            //if (noOp || searchManagerFromCatalogForm == null) return list;
            if (noOp) return list;
            else return list.Where(e => searchManagerFromCatalogForm.Match(e.Path)).ToList();
        }

        private void CloseWithoutSave()
        {
            DoNotSaveSetting = true;
            Close();
        }

        enum ViewerFormOnMemoryMode { Default, ForLookAhead, ForCheckOnlyEntries }

        private PackedImageLoader getPackedImageLoader(string path, ViewerFormOnMemoryMode mode)
        {
            var onMemory =
                ReadOnMemoryMode == ReadOnMemoryMode.None || mode == ViewerFormOnMemoryMode.ForCheckOnlyEntries ? PackedImageLoaderOnMemoryMode.None :
                ReadOnMemoryMode == ReadOnMemoryMode.Always || mode == ViewerFormOnMemoryMode.Default ? PackedImageLoaderOnMemoryMode.OnMemory :
                PackedImageLoaderOnMemoryMode.Releasable;

            return Program.GetPackedImageLoader(path, ArchivesInArchiveMode, animation: true, onMemory: onMemory);
        }

        private void Reload(bool force = false, bool updateOriginal = false, bool keepSameSizePages = false)
        {
            if (force || ViewSize != (FullScreen_pseudoFullScreen ? Screen.FromControl(this).Bounds.Size : PseudoMaximized ? PseudoMaximizedSize : pbView.Size))
            {
                if (string.IsNullOrEmpty(currentArchiveFilePath)) return;
                //nextLoader = loader;
                //bmwLoadEachPage.RunWorkerAsyncWithInterrupt(EntryNumberArray.Length, true); // OriginalImageArray の問題があるからキャンセルを待たないわけにはいかない

                // ウィンドウの大きさだけが変わった場合は、縮小後の大きさが変わらないページの縮小画像を残す（V4）
                if (keepSameSizePages && !updateOriginal) StashPagesWithSameResizedSize();

                // bmwLoadEachPage が動き出す前に Paint が動いて古い ResizedImage が使われることの無いように
                clearResizedImageArray();

                // currentArchiveFilePath が変更されてもいいようにデータを埋め込んでおく
                var arguments = new object[EntryArray.Length];
                arguments[0] = Tuple.Create(currentArchiveFilePath, currentStartingFilePath, true, updateOriginal);
                bmwLoadEachPage.RunWorkerAsyncWithInterrupt(arguments, true); // キャンセルを待たない場合 OriginalImageArray を毎回破棄するようにする必要あり

                //pbPaintInvalidate();
            }
        }

        // ウィンドウの大きさを変えたとき、縮小後の大きさが変わらないページの縮小画像を残す（V4）。
        // Reload で取り分け、読み込み開始（bmwLoadEachPage_RunWorkerStarting）で新しい配列に戻す。
        // 静止画で、原寸に作用するフィルタがない場合だけ。縮小デコードから作ったページ（Trimmed）も残す（表示の近くなら従来どおり原寸から読み直される）。
        // 以前は大きさを変えるたびに全ページの縮小画像を捨てて、書庫からの取り出し・デコードからやり直していた
        private sealed class ResizeKeepData
        {
            public VirtualBitmapEx[] Images;
            public Size[] Sizes, LayoutSourceSizes;
            public ulong[] Accounted;
            public byte[] States;
            public ViewerFormImageInfo[] Infos; // 読み直しでは原寸の情報も作り直されるので（ステータスバーの画像サイズなどに使う）
        }

        private ResizeKeepData resizeKeep = null;

        private void StashPagesWithSameResizedSize()
        {
            DisposeResizeKeep();
            var images = ResizedImageArray;
            var sizes = ResizedSizeArray;
            var sources = LayoutSourceSizeArray;
            var accounted = AccountedSizeArray;
            var states = OriginalStateArray;
            var infos = OriginalImageInfoArray;
            if (images == null || sizes == null || sources == null || accounted == null || states == null || infos == null) return;
            var length = images.Length;
            if (sizes.Length != length || sources.Length != length || accounted.Length != length || states.Length != length || infos.Length != length) return;
            if (ViewerMode == ViewerModeEnum.CoverSetting || imageFilter.PreFilterExists()) return;

            // 新しい表示サイズで縮小後の大きさを求める（読み込み開始時にも同じ値が設定される）
            ViewSize = FullScreen_pseudoFullScreen ? Screen.FromControl(this).Bounds.Size : PseudoMaximized ? PseudoMaximizedSize : ViewerForm_SizeChanged_pbBoundsMemoryForMinimize.Size;
            ResizeKeepData keep = null;
            for (var i = 0; i < length; i++)
            {
                var image = images[i];
                if (image == null || image.FrameCount != 1 || sources[i].IsEmpty) continue;
                if (image.SharesOtherMemory) continue; // 原寸とメモリを共有している等倍のページは、読み直しで原寸を Dispose すると壊れるので残さない（X1）
                if (states[i] == OriginalState_Reloading) continue; // 原寸の読み直し中のページは作り直す
                if (image.Size != sizes[i] || GetResizedSize(sources[i]) != sizes[i]) continue;
                if (keep == null)
                {
                    keep = new ResizeKeepData
                    {
                        Images = new VirtualBitmapEx[length], Sizes = new Size[length], LayoutSourceSizes = new Size[length],
                        Accounted = new ulong[length], States = new byte[length], Infos = new ViewerFormImageInfo[length],
                    };
                }
                keep.Images[i] = image;
                keep.Sizes[i] = sizes[i];
                keep.LayoutSourceSizes[i] = sources[i];
                keep.Accounted[i] = accounted[i];
                keep.States[i] = states[i];
                keep.Infos[i] = infos[i];
                images[i] = null; // clearResizedImageArray で Dispose されないように
            }
            resizeKeep = keep;
#if PERFLOG
            try
            {
                var keptCount = keep == null ? 0 : keep.Images.Count(x => x != null);
                var reasons = string.Join(",", Enumerable.Range(0, length).Where(i => images[i] != null || keep?.Images[i] != null)
                    .Select(i => keep?.Images[i] != null ? "K" : states[i] == OriginalState_Trimmed ? "t" : states[i] == OriginalState_Reloading ? "r" : sources[i].IsEmpty ? "e" : images[i].Size != sizes[i] ? "s" : "z"));
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "viewer-load.log"),
                    $"resizeKeep={keptCount}\tview={ViewSize.Width}x{ViewSize.Height}\tpages={reasons}" + Environment.NewLine);
            }
            catch { }
#endif
        }

        private void DisposeResizeKeep()
        {
            var keep = resizeKeep;
            resizeKeep = null;
            if (keep == null) return;
            foreach (var image in keep.Images) image?.Dispose();
        }

        // 残したページを新しい配列に戻す（読み込み開始時）。戻したページは読み込みジョブを 1 回読み飛ばす
        private void RestorePagesWithSameResizedSize(bool sameBook)
        {
            var keep = resizeKeep;
            resizeKeep = null;
            if (keep == null) return;
            if (!sameBook || ResizedImageArray == null || keep.Images.Length != ResizedImageArray.Length)
            {
                foreach (var image in keep.Images) image?.Dispose();
                return;
            }
            for (var i = 0; i < keep.Images.Length; i++)
            {
                var image = keep.Images[i];
                if (image == null) continue;
                ResizedImageArray[i] = image;
                ResizedSizeArray[i] = keep.Sizes[i];
                LayoutSourceSizeArray[i] = keep.LayoutSourceSizes[i];
                AccountedSizeArray[i] = keep.Accounted[i];
                // 読み直しでは原寸を捨てることがある（読み込み開始の updatedLoader）。原寸がなければ「原寸から作ったが捨てた」にする
                var state = keep.States[i];
                if (state == OriginalState_Kept && (PreFilteredImageArray == null || i >= PreFilteredImageArray.Length || PreFilteredImageArray[i] == null)) state = OriginalState_Discarded;
                OriginalStateArray[i] = state;
                if (OriginalImageInfoArray != null && i < OriginalImageInfoArray.Length && OriginalImageInfoArray[i] == null) OriginalImageInfoArray[i] = keep.Infos[i];
                KeptAfterResizeArray[i] = true;
                usedMemory += keep.Accounted[i];
            }
#if PERFLOG
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "viewer-load.log"), $"resizeRestore={keep.Images.Count(x => x != null)}" + Environment.NewLine); } catch { }
#endif
            // 残したページは読み込みの完了イベントが来ないので、表示中のページを残した場合はここで表示する（読み込み開始の処理が終わってから）
            var shown = currentPage;
            if (shown >= 0 && Enumerable.Range(shown, Math.Max(1, MaxPageCountInWindow)).Any(i => i < keep.Images.Length && keep.Images[i] != null))
            {
                try
                {
                    BeginInvoke((MethodInvoker)(() =>
                    {
                        if (ResizedImageArray != null && currentPage == shown) showCurrentPage(false);
                    }));
                }
                catch (InvalidOperationException) { } // ウィンドウハンドルがまだない
            }
        }

        // 読み込みジョブをやり直させる。残したページの読み飛ばしの印も外す（V4）
        private void ReworkPage(int idx)
        {
            var kept = KeptAfterResizeArray;
            if (kept != null && idx < kept.Length) kept[idx] = false;
            bmwLoadEachPage.ReworkOrder(idx);
        }

        private void clearResizedImageArray()
        {
            if (ResizedImageArray != null)
            {
                foreach (var img in ResizedImageArray)
                {
                    if (img != null)
                    {
                        img.Dispose();
                    }
                }
                ResizedImageArray = null;
            }
            ResizedSizeArray = null;
            AccountedSizeArray = null;
            OriginalStateArray = null;
            ReducedDecodedArray = null;
            LayoutSourceSizeArray = null;
            KeptAfterResizeArray = null;
            usedMemory = 0;
        }

        private void bmwLoadEachPage_RunWorkerStarting(object sender, GenerarClasses.BackgroundMultiWorker.RunWorkerStartingEventArgs e)
        {
            //OpenFileFinishedButStartingIsNotCalled = false;

            var x = e.Arguments as object[];
            var xx = x[0] as Tuple<string, string, bool, bool>;
            currentArchiveFilePath = xx.Item1;
            currentStartingFilePath = xx.Item2;
            var reload = xx.Item3;
            //var binding = xx.Item4;
            var updateOriginal = xx.Item4;
            x[0] = null;
            clearResizedImageArray();

            var hadNextData = nextData != null; // false なら同じ本の読み直し（Reload）（V4）
            bool updatedLoader;
            if (nextData != null)
            {
                var next = nextData;
                updatedLoader = loader != next.Loader;
                if (updatedLoader)
                {
                    DisposeLoader(); // 読み込みジョブが取り出し中でないときに（V1）
                    if (TryUpdateLastAccessTimeAfterLoaderDisposed != null)
                    {
                        Program.TryUpdateLastAccessTime(TryUpdateLastAccessTimeAfterLoaderDisposed);
                    }
                    loader = next.Loader;
                    // 別の本ではルーペを使うとは限らないので、使うまで拡大画像を作らず原寸も残さない（V25）
                    magnifierWanted = false;
                    magnifierUnusedPageTurns = 0;
                }
                EntryArray = next.Entries;
                EntryLongPathArray = next.EntriesPaths;
            }
            else
            {
                updatedLoader = loader != null;
            }
            TryUpdateLastAccessTimeAfterLoaderDisposed = null;
            nextData = null;

            if (updatedLoader || updateOriginal)
            {
                if (PreFilteredImageArray != null)
                {
                    for (var i = 0; i < PreFilteredImageArray.Length; i++)
                    {
                        var img = PreFilteredImageArray[i];
                        if (img != null)
                        {
                            lock (img)
                            {
                                img.Dispose();
                                PreFilteredImageArray[i] = null; // lock 内で null に
                            }
                        }
                    }
                    PreFilteredImageArray = null;
                }
                //PreFilteredImageArray = new Bitmap[EntryNumberArray.Length];
                //OriginalImageInfoArray = new ImageInfo[EntryNumberArray.Length];
                PreFilteredImageArray = new BitmapEx[EntryArray.Length];
                OriginalImageInfoArray = new ViewerFormImageInfo[EntryArray.Length];
            }
            else
            {
                if (PreFilteredImageArray == null)
                {
                    //PreFilteredImageArray = new Bitmap[EntryNumberArray.Length];
                    //OriginalImageInfoArray = new ImageInfo[EntryNumberArray.Length];
                    PreFilteredImageArray = new BitmapEx[EntryArray.Length];
                    OriginalImageInfoArray = new ViewerFormImageInfo[EntryArray.Length];
                }
            }
            //ResizedImageArray = new Bitmap[EntryNumberArray.Length];
            //ResizedSizeArray = new Size[EntryNumberArray.Length];
            // 縮小画像を作り直すので、書庫の使用済みの印も外す（D9）
            loader?.ClearConsumedEntries();
            ResizedImageArray = new VirtualBitmapEx[EntryArray.Length];
            ResizedSizeArray = new Size[EntryArray.Length];
            AccountedSizeArray = new ulong[EntryArray.Length];
            OriginalStateArray = new byte[EntryArray.Length];
            ReducedDecodedArray = new bool[EntryArray.Length];
            LayoutSourceSizeArray = new Size[EntryArray.Length];
            KeptAfterResizeArray = new bool[EntryArray.Length];
            ViewSize = FullScreen_pseudoFullScreen ? Screen.FromControl(this).Bounds.Size : PseudoMaximized ? PseudoMaximizedSize : ViewerForm_SizeChanged_pbBoundsMemoryForMinimize.Size;
            RestorePagesWithSameResizedSize(sameBook: reload && !hadNextData && !updateOriginal);
            SetBackgroundMode();
            bmwLoadEachPage_EachRunWorkerCompleted_PrevPage = currentPage; // 無駄な優先順位変更を防止

            if (updatedLoader)
            {
                setTitleBar();
            }

            if (ipcLookAheadInfo == null) SetCurrentImageLongPath(); // ページ番号の描画よりも後で呼び出す

            Magnifier_ZoomedPage?.Dispose();
            Magnifier_ZoomedPage = null;
            Magnifier_currentPageReady = -1;

            if (!reload)
            {

                // 常にリクエストに従う方法
                var bindingFromPath = ZipPlaInfo.GetOnlyBindingModeFromFullName(currentArchiveFilePath);
                if (bindingFromPath == null)
                {
                    var binding = requestedBindingMode;
                    BindingMode = binding ?? Program.GetBindingModeFromCulture(Message.CurrentLanguage);
                }
                else
                {
                    BindingMode = (BindingMode)bindingFromPath;
                }

                // 名前昇順以外は RightToLeft を禁止する方法 
            }
        }

        private void setTitleBar()
        {
            string text;
            try
            {
                if (string.IsNullOrEmpty(currentArchiveFilePath))
                {
                    text = Program.Name;
                }
                else
                {
                    var slideshow = getSlideshowEnabled() ? $" <{Message.Slideshow}>" : "";
                    //var filter = requestedFilterString == null ? "" : $" [{requestedFilterString}]";
                    var archiveFileName = Path.GetFileName(currentArchiveFilePath);
                    if (Directory.Exists(currentArchiveFilePath))
                    {
                        text = $"{archiveFileName + Path.DirectorySeparatorChar}{slideshow} - {Program.Name}";
                    }
                    else
                    {
                        text = $"{archiveFileName}{slideshow} - {Program.Name}";
                    }
                }
            }
            catch
            {
                text = Program.Name;
            }
            Text = text;
        }

        private void removeHiddenConfigFromCatalogForm()
        {
            requestedFilterString = null;
            requestedFilterStringWithoutAlias = null;
            searchManagerFromCatalogForm = null;

            requestedSortModeDetails = null;
            currentSortModeDetails = null;
        }

        private bool SetCurrentImageLongPath_Que = false;

        private void SetCurrentImageLongPath()
        {
            if (string.IsNullOrEmpty(currentArchiveFilePath))
            {
                return;
            }
            try
            {
#region １つめを表示
                //currentImageLongPathToolStripTextBox.Text = EntryLongPathArray[mtbPage.Value];
                //Program.TextBoxShowRight(currentImageLongPathToolStripTextBox);
#endregion

#region 全て表示
                var cPage = currentPage;
                var nextPage = NextPage;
                var pageCount = nextPage - currentPage;
                var pageDivision = 1;
                var cdPosition = CurrentDividedPosition;
                if (nextPage <= cPage)
                {
                    if (nextPage < cPage || MaxDivision <= 1 || cdPosition < 0 || (pageDivision = getPageDivison(cPage)) <= 1 || cdPosition >= pageDivision)
                    {
                        SetCurrentImageLongPath_Que = true;
                        return;
                    }
                    nextPage++;
                }
                SetCurrentImageLongPath_Que = false;
                int start, stop, delta;
                if (BindingMode == BindingMode.LeftToRight)
                {
                    start = cPage;
                    stop = nextPage;
                    delta = 1;
                }
                else
                {
                    start = nextPage - 1;
                    stop = cPage - 1;
                    delta = -1;
                }
                var isEntity = loader?.Type == PackedImageLoader.PackType.Directory;
                var baseText = isEntity ? EntryLongPathArray[start]?.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar) : EntryLongPathArray[start];
                var text = baseText;
                for (var i = start + delta; i != stop; i += delta)
                {
                    text += " | " + GetDifferentSuffix(baseText, isEntity ? EntryLongPathArray[i]?.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar) : EntryLongPathArray[i]);
                }

                // 現在の分割位置を追加する場合、分割内の移動が起こったときにこのメソッドが呼び出されるようにすること

                if (currentImageLongPathToolStripTextBox.Text != text)
                {
                    currentImageLongPathToolStripTextBox.Text = text;
                    Program.TextBoxShowRight(currentImageLongPathToolStripTextBox);
                }
#endregion
            }
            catch { }
        }

        private static readonly System.Text.RegularExpressions.Regex GetDifferentSuffix_Regex = new System.Text.RegularExpressions.Regex(@"^.\b.$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string GetDifferentSuffix(string reference, string target)
        {
            if (reference == null || target == null) return target;
            var stop = Math.Min(reference.Length, target.Length) - 1;
            var i = 0;
            while (i < stop && reference[i] == target[i]) i++;
            do { i--; } while (i >= 0 && !GetDifferentSuffix_Regex.IsMatch(target.Substring(i, 2)));
            return target.Substring(i + 1);
        }

#if PERFLOG
        // 計測用: ページ読み込みの時間（exe のフォルダの viewer-load.log）。読み込みスレッドは 1 本
        [ThreadStatic] private static long perfLoadStart, perfDecodeEnd, perfJobStart; // 読み込みは並列（V1）なのでスレッドごと
        // ジョブの開始・終了と完了イベントの時刻（ms、Stopwatch のタイムスタンプから）。ジョブのあいだの空き時間を調べる（V1）
        private static double PerfMs(long timestamp) => timestamp * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        private static readonly object perfLoadLogLock = new object();
#endif

        private int bmwLoadEachPage_DoWork_DEBUG_WorkNumber = -1;

        private int bmwLoadEachPage_DoWork_DEBUG_Phase;

        // 読み込みは複数のスレッドで並列に行う（V1）。書庫からの取り出しは loaderLock で直列にし、デコード・色判定・縮小は並列。
        // 同じページのジョブ（原寸の読み直しの ReworkOrder など）が重ならないように、ページごとのロック（剰余で共有）を取る
        private readonly object loaderLock = new object();

        // 先読みの範囲（前後 64 ページ）より十分多くし、表示中のページと先読みの遠いページが同じロックにならないようにする（Y11。以前は 64 で、c と c±64 がぶつかった）
        private const int LoadPageLockCount = 256;

        private static readonly object[] loadPageLocks = Enumerable.Range(0, LoadPageLockCount).Select(_ => new object()).ToArray();

        private static int LoadThreadCount
        {
            get
            {
#if PERFLOG
                // 計測用: 環境変数 ZIPPLA_VIEWER_THREADS で読み込みのスレッド数を指定する
                if (int.TryParse(Environment.GetEnvironmentVariable("ZIPPLA_VIEWER_THREADS"), out var forced) && forced > 0) return forced;
#endif
                return Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));
            }
        }

        private void bmwLoadEachPage_DoWork(object sender, GenerarClasses.BackgroundMultiWorker.EachDoWorkEventArgs e)
        {
            // 本の切り替えや Reload でキャンセルされたジョブは、ロックやセマフォを待つ前後で抜ける（Y9。以前はデコードの後だけ見ていたので、
            // 待っていたジョブが取り出しと原寸のデコードを最後までしてから捨てていた）
            if (bmwLoadEachPage.CancellationPending) return;
            lock (loadPageLocks[e.WorkNumber % LoadPageLockCount])
            {
                if (bmwLoadEachPage.CancellationPending) return;
                // 原寸でデコードするページ（表示の近く。3000x4300 のカラーで 1 枚 38MB）は同時に読む数を絞る。
                // 4 本とも原寸を読むと最大メモリが 1.6〜1.8 倍になった。縮小デコードのページ（小さい）は制限しない。
                // 原寸から縮小するだけのジョブも枠を取る（縮小は原寸の一時的なコピーを作るので、枠の外で並列にすると最大メモリが増えた。Y10 は見送り）
                var full = !CanDecodeReduced(e.WorkNumber);
                // 表示中のページは専用の枠（見開きの枚数ぶん）で読み、表示外のページ（前後の見開き）とは枠を取り合わない。
                // 開いた直後や遠くへ飛んだ直後に、見開きの 2 ページが直列になったり、表示外のページの後ろで待ったりしないようにする（V23）
                var usesDisplayedSlot = full && IsDisplayedPage(e.WorkNumber);
                if (usesDisplayedSlot) DisplayedPageFullDecodeSemaphore.Wait();
                else if (full) FullDecodeSemaphore.Wait();
                try
                {
                    if (bmwLoadEachPage.CancellationPending) return; // 枠を待っている間にキャンセルされた（Y9）
                    bmwLoadEachPage_DoWorkCore(sender, e);
                }
                finally
                {
                    if (usesDisplayedSlot) DisplayedPageFullDecodeSemaphore.Release();
                    else if (full) FullDecodeSemaphore.Release();
                }
            }
        }

        private static int FullDecodeConcurrency
        {
            get
            {
#if PERFLOG
                if (int.TryParse(Environment.GetEnvironmentVariable("ZIPPLA_VIEWER_FULL_DECODES"), out var forced) && forced > 0) return forced;
#endif
                return 1;
            }
        }

        private readonly System.Threading.SemaphoreSlim FullDecodeSemaphore = new System.Threading.SemaphoreSlim(FullDecodeConcurrency);

        // 表示中のページの原寸デコード用の枠（V23）。見開きの 2 ページを並列に読む
        private readonly System.Threading.SemaphoreSlim DisplayedPageFullDecodeSemaphore = new System.Threading.SemaphoreSlim(2);

        // 今表示しているページか（V23）。ワーカースレッドから呼ばれる
        private bool IsDisplayedPage(int idx)
        {
            var cPage = currentPage;
            if (cPage < 0) return false;
            return cPage <= idx && idx < cPage + Math.Max(1, MaxPageCountInWindow);
        }

        // GDI+ などで並列にデコードしてよい形式か。Susie プラグインや PDF などは従来どおり書庫のロックの中で読む
        private static bool CanDecodeInParallel(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".tif" || ext == ".tiff" ||
                ext == ".webp" && ImageLoader.WebPDecodesWithLibWebP || // libwebp はスレッド安全（D1）
                ext.Length > 1 && ImageLoader.Jpeg2000DecodesWithFfmpeg(ext.Substring(1)); // ffmpeg は別のプロセス（D6）
        }

        // loader を破棄して null にする。どちらも loaderLock の中で行うので、読み込みジョブはロックの中で
        // 「手元のローダーがまだ loader か」を確かめれば、破棄されたローダーに触らずに済む（OpenPageImage）
        private void DisposeLoader()
        {
            lock (loaderLock)
            {
                var l = loader;
                loader = null;
                l?.Dispose();
            }
        }

        // ロックの中で呼ぶ。読み込みジョブが手元に持っているローダーが、破棄・差し替えされていたら止める
        private void ThrowIfLoaderReplaced(PackedImageLoader l)
        {
            if (!ReferenceEquals(l, loader)) throw new LoaderReplacedException();
        }

        // 読み込みジョブのローダーが破棄・差し替えされた。ジョブはキャンセルされていなくても結果なしで抜ける（DoWorkCore）
        private sealed class LoaderReplacedException : OperationCanceledException { }

        // ページの画像を読む（V1）。並列にデコードできる形式は、書庫からの取り出しだけをロックの中で行い、デコードはロックの外で行う
        private Bitmap OpenPageImage(PackedImageLoader.PackedImageEntry entry)
        {
            var l = loader;
            if (l == null) throw new ObjectDisposedException(nameof(loader));
            // PDF のエントリ名には拡張子がないが、そのまま JPEG として読めるページは TryExtractEntry が取り出す（D2）
            if (CanDecodeInParallel(entry.Path) || l.Type == PackedImageLoader.PackType.Pdf && entry.ChildIndex < 0)
            {
                if (l.ReadsEntryFileDirectly(entry))
                {
                    return l.OpenImageStream(entry); // ファイルを直接読むので書庫のロックは要らない（フォルダの中の書庫は子のローダーを通るのでロックが要る、X2）
                }
                MemoryStream stream;
                string name;
                long length;
                bool extracted;
#if PERFLOG
                var perfLockRequest = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                lock (loaderLock)
                {
#if PERFLOG
                    var perfLockTaken = System.Diagnostics.Stopwatch.GetTimestamp();
                    perfLockWaitTicks = perfLockTaken - perfLockRequest;
#endif
                    ThrowIfLoadCanceled(); // ロックを待っている間にキャンセルされたら取り出さない（Y9）
                    ThrowIfLoaderReplaced(l); // ロックを待っている間に名前の変更などで破棄された
                    extracted = l.TryExtractEntry(entry, out stream, out name, out length);
#if PERFLOG
                    perfExtractTicks = System.Diagnostics.Stopwatch.GetTimestamp() - perfLockTaken;
#endif
                }
                if (extracted)
                {
                    using (stream)
                    {
                        return PackedImageLoader.DecodeExtracted(stream, name, l.Animation, length);
                    }
                }
            }
            lock (loaderLock)
            {
                ThrowIfLoadCanceled(); // Y9
                ThrowIfLoaderReplaced(l);
                return l.OpenImageStream(entry);
            }
        }

        // 本の切り替えや Reload でキャンセルされた読み込みジョブを、書庫のロックを取った直後に止める（Y9）。DoWorkCore が捕まえて結果なしで抜ける
        private void ThrowIfLoadCanceled()
        {
            if (bmwLoadEachPage.CancellationPending) throw new OperationCanceledException();
        }

#if PERFLOG
        // 書庫のロックを待った時間と、ロックの中で取り出した時間（D5 の計測）。ジョブのスレッドごと
        [ThreadStatic] private static long perfLockWaitTicks, perfExtractTicks;
#endif

        private void bmwLoadEachPage_DoWorkCore(object sender, GenerarClasses.BackgroundMultiWorker.EachDoWorkEventArgs e)
        {
#if PERFLOG
            perfJobStart = System.Diagnostics.Stopwatch.GetTimestamp();
            perfLockWaitTicks = perfExtractTicks = 0;
#endif
            var rsa = ResizedSizeArray;
            if (rsa == null || rsa.Length <= e.WorkNumber)
            {
                return;
            }
            // ウィンドウの大きさを変えたときに縮小画像を残したページ（V4）。結果なしで終わる（完了イベントは何もしない）
            var keptAfterResize = KeptAfterResizeArray;
            if (keptAfterResize != null && e.WorkNumber < keptAfterResize.Length && keptAfterResize[e.WorkNumber])
            {
                keptAfterResize[e.WorkNumber] = false;
#if PERFLOG
                try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "viewer-load.log"), $"resizeSkip={e.WorkNumber}" + Environment.NewLine); } catch { }
#endif
                return;
            }
            var layoutSources = LayoutSourceSizeArray;

            var reducedFlags = ReducedDecodedArray;
            if (reducedFlags != null && e.WorkNumber < reducedFlags.Length) reducedFlags[e.WorkNumber] = false;

            System.Diagnostics.Stopwatch sw = null;
            bmwLoadEachPage_DoWork_DEBUG_WorkNumber = e.WorkNumber;
            bmwLoadEachPage_DoWork_DEBUG_Phase = 0;
            // 書庫からの取り出しは OpenPageImage の中で loaderLock により直列にし、デコード以降は複数のスレッドで並列に行う（V1）
            try
            {
                LockManager.ForIReadOnlyList(PreFilteredImageArray, e.WorkNumber, item =>
                {
                    bmwLoadEachPage_DoWork_DEBUG_Phase = 1;
                    BitmapEx bmp;
                    ViewerFormImageInfo imageInfo;
                    // 縮小デコードしたページ（C5）では bmp は原寸ではない。表示サイズは原寸から求め、bmp は保持しない
                    var reducedDecoded = false;
                    var layoutSize = Size.Empty;
                    if (item == null)
                    {
                        bmwLoadEachPage_DoWork_DEBUG_Phase = 2;
                        //var sw = new System.Diagnostics.Stopwatch(); sw.Start();
                        //var img = loader.OpenImageStream(EntryNumberArray[e.WorkNumber]);
                        var reduceRequest = CanDecodeReduced(e.WorkNumber) ? new ReducedJpegDecodeRequest(originalSize =>
                        {
                            // 縮小後が表示サイズより大きければよい（等しいと縮小せずに原寸と共有する経路に入るので 1 画素多く求める）
                            var resized = GetResizedSize(originalSize);
                            return new Size(resized.Width + 1, resized.Height + 1);
                        }) : null;
                        BitmapEx img;
#if PERFLOG
                        perfLoadStart = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                        ReducedJpegDecodeRequest.Current = reduceRequest;
                        try
                        {
                            img = BitmapEx.ConvertToBitmapEx(OpenPageImage(EntryArray[e.WorkNumber]));
                        }
                        finally
                        {
                            ReducedJpegDecodeRequest.Current = null;
                        }
#if PERFLOG
                        perfDecodeEnd = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                        // 本の切り替えや Reload でキャンセルされていれば、色の判定・変換・縮小をせずに抜ける（V28）。
                        // まだどこにも登録していないので捨てる
                        if (bmwLoadEachPage.CancellationPending)
                        {
                            img.Dispose();
                            return;
                        }
                        if (reduceRequest != null && reduceRequest.Reduced)
                        {
                            reducedDecoded = true;
                            layoutSize = reduceRequest.OriginalSize;
                        }
                        ImageInfo imageInfo0;
                        int? maxColorDiff = 0;
                        try
                        {
                            //var originalRotate = ViewerFormImageFilter.GetOrientation(img);
                            bmwLoadEachPage_DoWork_DEBUG_Phase = 3;
                            //sw.Stop(); MessageBox.Show($"{sw.Elapsed}");

                            //Thread.Sleep(500);


                            //var isColorImage = imageFilter.NeedToCheckWhetherColorOrNot() && ViewerMode != ViewerModeEnum.CoverSetting ?
                            //    ViewerFormImageFilter.TryGetWhetherColorOrNot(img) : null;


                            // アニメーションの遅延読み込み実装前

                            // アニメーションの遅延読み込み実装後
                            maxColorDiff = ViewerFormImageFilter.TryGetMaxColorDiff(img); // TryGet は全てのフレームに大して同じ効果
                            if (maxColorDiff == null)
                            {
                                if (img.FrameCount > 1)
                                {
                                    // アニメーションの場合 null は許さない
                                    // try で彩度が確定しなければカラーで決め打ち
                                    maxColorDiff = 255;
                                }
                                else if (PixelFormatConverter.IsGrayScaleBitmap(img))
                                {
                                    maxColorDiff = 0;
                                }
                            }

                            imageInfo0 = ImageLoader.GetImageInfo(img); // img がピクセルフォーマット変換で Dispose される前に行う
                            if (reducedDecoded) imageInfo0 = new ImageInfo(layoutSize, imageInfo0.BitPerPixel);

                            if (maxColorDiff == 0)
                            {
                                //if (img.PixelFormat != PixelFormat.Format8bppIndexed) // 遅延読み込みのためできるだけ ApplyToAll は使いたくない
                                //  → ApplayToAll も遅延化したので問題ない＋白黒でもインデックスと画素値が対応しない画像がごくまれに存在する
                                {
                                    img.ApplyToAll(PixelFormatConverter.ToBitmap8Unsafe);
                                }
                            }
                            else
                            {
                                if (img.PixelFormat != PixelFormat.Format24bppRgb) // 遅延読み込みのためできるだけ ApplyToAll は使いたくない
                                {
                                    img.ApplyToAll(PixelFormatConverter.ToBitmap24Unsafe);
                                }
                            }

                            if (ViewerMode != ViewerModeEnum.CoverSetting && imageFilter.PreFilterExists())
                            {
                                img.ApplyToAll(imageFilter.PreFilter);
                            }
                        }
                        catch
                        {
                            img.Dispose();
                            throw;
                        }
                        bmp = img;

                        if (!reducedDecoded) PreFilteredImageArray[e.WorkNumber] = bmp;
                        imageInfo = new ViewerFormImageInfo(imageInfo0, maxColorDiff);
                        OriginalImageInfoArray[e.WorkNumber] = imageInfo;
                    }
                    else
                    {
                        bmwLoadEachPage_DoWork_DEBUG_Phase = 4;
                        bmp = item;
                        if (ViewerMode != ViewerModeEnum.CoverSetting)
                        {
                            try
                            {
                                imageInfo = OriginalImageInfoArray[e.WorkNumber];
                            }
                            catch
                            {
                                imageInfo = null;
                            }
                        }
                        else imageInfo = null;
                    }
                    //var zoom = Math.Min(ViewSize.Width / (double)img.Width, ViewSize.Height / (double)img.Height);
                    Action body = () =>
                    {
                        // img が Dispose された場合エラーになるが想定内

                        bmwLoadEachPage_DoWork_DEBUG_Phase = 5;
                        var layoutSource = reducedDecoded ? layoutSize : bmp.Size;
                        var resizedSize = GetResizedSize(layoutSource);
                        //var result = new Bitmap((int)Math.Round(zoom * img.Width), (int)Math.Round(zoom * img.Height));

                        rsa[e.WorkNumber] = resizedSize;
                        if (layoutSources != null && e.WorkNumber < layoutSources.Length) layoutSources[e.WorkNumber] = layoutSource;

                        // DrawImage が Src と Dst のサイズが一致する場合としない場合で異なる挙動をする
                        //var outWidth = rect.Width == bmp.Width ? rect.Width : bmp.Width - 1;
                        //var outHeight = rect.Height == bmp.Height ? rect.Height : bmp.Height - 1;

                        bmwLoadEachPage_DoWork_DEBUG_Phase = 6;
                        VirtualBitmapEx result;
                        if (bmp.FrameCount == 1/* || BitmapEx.GetDataSizeInBytes(bmp, resizedSize) < GetUserMemoryUBound() / 2*/)
                        {
                            BitmapEx entity;
                            var sharesOriginal = false; // entity が原寸と画素のメモリを共有しているか（V14）
                            // リサイズが変更も不要なら Graphics は使わない
                            if (resizedSize.Width == bmp.Width && resizedSize.Height == bmp.Height)
                            {
                                var bench = standardScalingAlgorithm_BenchRequest == e.WorkNumber && !bmwLoadEachPage.CancellationPending;
                                if (bench)
                                {
                                    standardScalingAlgorithm_BenchRequest = -1;

                                    var message = "Scaling is no excuted.";
                                    Invoke(((MethodInvoker)(() =>
                                    {
                                        MessageBox.Show(this, message,
                                            "Measurement of computation time", MessageBoxButtons.OK, MessageBoxIcon.Information);

                                    })));
                                }

                                var rect = new Rectangle(0, 0, resizedSize.Width, resizedSize.Height);

                                //result = Clone(bmp);
                                // Clone は原寸のメモリを共有する別名を作る。表示フィルタ（PostFilter）は画素をその場で書き換えるので、
                                // フィルタがあるときは本当に複製する（共有したままだと原寸まで書き換わり、作り直すたびに重ねがけされる、C9）
                                // ただし 8bpp でパレットだけが変わる場合は共有のままでよい（V13）
                                sharesOriginal = !(ViewerMode != ViewerModeEnum.CoverSetting && imageFilter.PostFilterExists()
                                    && !imageFilter.PostFilterChangesOnlyPalette(bmp.PixelFormat, imageInfo?.MaxColorDiff));
                                entity = sharesOriginal ? bmp.CreateNew(Clone) : bmp.CreateNew(DeepClone);
                            }
                            else
                            {
                                bmwLoadEachPage_DoWork_DEBUG_Phase = 10;


                                //var sw = new System.Diagnostics.Stopwatch();  sw.Start();

                                ScalingAlgorithm scalingAlgorithm;
                                if ((long)resizedSize.Width * resizedSize.Height > (long)bmp.Width * bmp.Height)
                                {
                                    scalingAlgorithm = StandardScalingAlgorithm.ScaleUp;
                                }
                                else
                                {
                                    scalingAlgorithm = StandardScalingAlgorithm.ScaleDown;
                                }

                                var gamma = ResizeGammaNormal;

                                var bench = standardScalingAlgorithm_BenchRequest == e.WorkNumber && !bmwLoadEachPage.CancellationPending;
                                if (bench)
                                {
                                    standardScalingAlgorithm_BenchRequest = -1;
                                    BitmapResizer.CreateNew(bmp, resizedSize, keepGrayscale: true).Dispose(); // キャッシュ関係での不公平が生じないように
                                    sw = new System.Diagnostics.Stopwatch();
                                    sw.Start();
                                }

                                const int pixelsPerPixelToBeLeft = int.MaxValue;
                                // 縮小の内部並列は表示中のページだけ。先読みのページは読み込みのスレッド（最大 4 本）どうしで並列になっているので、
                                // さらに全コアで並列にすると表示中のページの縮小と CPU を取り合う（V29）
                                var parallel = IsDisplayedPage(e.WorkNumber);
                                switch (scalingAlgorithm)
                                {
                                    case ScalingAlgorithm.HighSpeed: entity = bmp.CreateNew(bmpi => QuickGraphic.CreateNew(bmpi, resizedSize, parallel: parallel)); break;
                                    case ScalingAlgorithm.NearestNeighbor: entity = bmp.CreateNew(bmpi => LongVectorImageResizer.NearestNeighbor(bmpi, resizedSize, parallel)); break;
                                    case ScalingAlgorithm.AreaAverage: entity = bmp.CreateNew(bmpi => LongVectorImageResizer.AreaAverage(bmpi, pixelsPerPixelToBeLeft, gamma, resizedSize, parallel)); break;
                                    case ScalingAlgorithm.Lanczos1: entity = bmp.CreateNew(bmpi => LongVectorImageResizer.Lanczos(1, bmpi, pixelsPerPixelToBeLeft, gamma, resizedSize, parallel)); break;
                                    case ScalingAlgorithm.Lanczos2: entity = bmp.CreateNew(bmpi => LongVectorImageResizer.Lanczos(2, bmpi, pixelsPerPixelToBeLeft, gamma, resizedSize, parallel)); break;
                                    case ScalingAlgorithm.Lanczos3: entity = bmp.CreateNew(bmpi => LongVectorImageResizer.Lanczos(3, bmpi, pixelsPerPixelToBeLeft, gamma, resizedSize, parallel)); break;
                                    case ScalingAlgorithm.Lanczos4: entity = bmp.CreateNew(bmpi => LongVectorImageResizer.Lanczos(4, bmpi, pixelsPerPixelToBeLeft, gamma, resizedSize, parallel)); break;
                                    case ScalingAlgorithm.Spline4: entity = bmp.CreateNew(bmpi => LongVectorImageResizer.Spline4(bmpi, pixelsPerPixelToBeLeft, gamma, resizedSize, parallel)); break;
                                    case ScalingAlgorithm.Spline16: entity = bmp.CreateNew(bmpi => LongVectorImageResizer.Spline16(bmpi, pixelsPerPixelToBeLeft, gamma, resizedSize, parallel)); break;
                                    case ScalingAlgorithm.Spline36: entity = bmp.CreateNew(bmpi => LongVectorImageResizer.Spline36(bmpi, pixelsPerPixelToBeLeft, gamma, resizedSize, parallel)); break;
                                    case ScalingAlgorithm.Spline64: entity = bmp.CreateNew(bmpi => LongVectorImageResizer.Spline64(bmpi, pixelsPerPixelToBeLeft, gamma, resizedSize, parallel)); break;
                                    default: entity = bmp.CreateNew(bmpi => BitmapResizer.CreateNew(bmpi, resizedSize, keepGrayscale: true)); break; // グレースケールは 8bpp のまま縮小する（C2）
                                }

                                if (bench)
                                {
                                    sw.Stop();
                                    var time = sw.Elapsed;
                                    TimeSpan time2;
                                    if (scalingAlgorithm != ScalingAlgorithm.Default)
                                    {
                                        sw.Restart();
                                        using (var temp = bmp.CreateNew(bmpi => BitmapResizer.CreateNew(bmpi, resizedSize, keepGrayscale: true)))
                                        {
                                            sw.Stop();
                                            time2 = sw.Elapsed;
                                        }
                                    }
                                    else time2 = time;
                                    string algorithm;
                                    switch (scalingAlgorithm)
                                    {
                                        default: algorithm = scalingAlgorithm.ToString(); break;
                                    }
                                    var linearize = isIndividuallyImplemented(scalingAlgorithm) ? "" : gamma == GammaConversion.Value1_0 ? "no linearized / " : "linearize γ = 2.2 → 1.0 / ";
                                    var message =
                                       algorithm + (bmp.PixelFormat == PixelFormat.Format24bppRgb ? $" ({linearize}Color)\n" : $" ({linearize}Grayscale)\n") +
                                        $"source:\n\t{bmp.Size}\n" +
                                        $"destination:\n\t{resizedSize}\n" +
                                        $"computation time:\n\t{time}\n" +
                                        // $"the ratio to \"System.Windows.Media.ScaleTransform\":\n\t{(double)100 * time.Ticks / time2.Ticks:F2} %";
                                        $"the ratio to \"BitmapScalingMode.Unspecified (Linear)\":\n\t{(double)100 * time.Ticks / time2.Ticks:F2} %";
                                    //$"the ratio to \"{BitmapResizer.LastBitmapScalingMode}\":\n\t{(double)100 * time.Ticks / time2.Ticks:F2} %";
                                    Invoke(((MethodInvoker)(() =>
                                    {
                                        var res = MessageBox.Show(this, message + "\n\nAre you sure you want to copy above data to clipboard?",
                                            "Measurement of computation time", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                                        if (res == DialogResult.Yes)
                                        {
                                            Clipboard.SetText(message);
                                        }
                                    })));
                                }
                            }

                            if (ViewerMode != ViewerModeEnum.CoverSetting)
                            {
                                if (entity.FrameCount == 1)
                                {
                                    if (imageInfo != null)
                                    {
                                        imageFilter.PostFilter(entity, ref imageInfo.MaxColorDiff);
                                    }
                                    else
                                    {
                                        int? dummy = null;
                                        imageFilter.PostFilter(entity, ref dummy);
                                    }
                                }
                                else
                                {
                                    int? dummy = imageInfo?.MaxColorDiff ?? 255;
                                    entity.ApplyToAll(img => { imageFilter.PostFilter(img, ref dummy); return img; });
                                }
                            }

                            bmwLoadEachPage_DoWork_DEBUG_Phase = 11;

                            //ピクセルオフセット確認用
                            //QuickGraphic.DrawImage(result, rect, img, null);

                            // 原寸と共有している（等倍の Clone）なら、原寸のぶんを足すと同じメモリを二重に数えてしまい、
                            // ReduceUsingMemory が早く捨てすぎる（V14）。共有している原寸は TrimOriginals でも捨てない
                            result = new VirtualBitmapEx(entity, reducedDecoded || sharesOriginal ? 0 : bmp.GetDataSizeInBytes(), sharesOriginal);
                        }
                        else
                        {
                            var bench = standardScalingAlgorithm_BenchRequest == e.WorkNumber && !bmwLoadEachPage.CancellationPending;
                            if (bench)
                            {
                                standardScalingAlgorithm_BenchRequest = -1;
                            }

                            result = GetVirtualBitmapExForStandard(bmp, resizedSize);

                            if (bench)
                            {
                                var message = "This function does not work for animation.";
                                //$"the ratio to \"{BitmapResizer.LastBitmapScalingMode}\":\n\t{(double)100 * time.Ticks / time2.Ticks:F2} %";
                                Invoke(((MethodInvoker)(() =>
                                {
                                    MessageBox.Show(this, message, "Measurement of computation time", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                })));
                            }
                        }
                        result.ReducedDecoded = reducedDecoded; // 完了イベントへは結果と一緒に渡す（Y21）
                        
                        e.Result = result;// new Bitmap(img, (int)Math.Round(zoom * img.Width), (int)Math.Round(zoom * img.Height));
                    };
                    var reducedArray = ReducedDecodedArray;
                    if (reducedArray != null && e.WorkNumber < reducedArray.Length) reducedArray[e.WorkNumber] = reducedDecoded;
                    // 縮小の前にもキャンセルを見る（V28）。原寸は PreFilteredImageArray に登録済みなのでそのまま残し、
                    // 縮小デコードの画像（どこにも登録していない）は捨てる
                    if (bmwLoadEachPage.CancellationPending)
                    {
                        if (reducedDecoded) bmp.Dispose();
                        return;
                    }
#if PERFLOG
                    if (item == null)
                    {
                        var perfSize = Size.Empty;
                        try { lock (bmp) { perfSize = bmp.Size; body(); } } finally { if (reducedDecoded) bmp.Dispose(); } // bmp は登録済みなのでロックの中で触る
                        double f = System.Diagnostics.Stopwatch.Frequency;
                        var end = System.Diagnostics.Stopwatch.GetTimestamp();
                        try
                        {
                            // 並列に読むと同時に書いて片方が失敗し、行が抜けるのでロックする（V23 の計測で判明）
                            lock (perfLoadLogLock)
                            File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "viewer-load.log"),
                                $"page={e.WorkNumber}\tlockWait={perfLockWaitTicks * 1000 / f:F1}ms\textract={perfExtractTicks * 1000 / f:F1}ms\treduced={reducedDecoded}\tdecoded={perfSize.Width}x{perfSize.Height}\tresized={rsa[e.WorkNumber].Width}x{rsa[e.WorkNumber].Height}\tdecode={(perfDecodeEnd - perfLoadStart) * 1000 / f:F1}ms\trest={(end - perfDecodeEnd) * 1000 / f:F1}ms\tjobStart={PerfMs(perfJobStart):F2}\tjobEnd={PerfMs(end):F2}" + Environment.NewLine);
                        }
                        catch { }
                        return;
                    }
#endif
                    if (reducedDecoded)
                    {
                        // 縮小デコードした画像はどこにも登録していないので、縮小画像を作ったら捨てる
                        try
                        {
                            body();
                        }
                        finally
                        {
                            bmp.Dispose();
                        }
                    }
                    else if (item == bmp)
                    {
                        body();
                    }
                    else
                    {
                        lock (bmp)
                        {
                            body();
                        }
                    }
                });
            }
            catch (OperationCanceledException oce) when (bmwLoadEachPage.CancellationPending || oce is LoaderReplacedException)
            {
                // キャンセルされた・読んでいた書庫が閉じられたので、エラー画像も作らずに抜ける（Y9）
            }
#if DEBUG || PERFLOG
            catch (Exception ee)
#else
            catch
#endif
            {
#if DEBUG
                var errorMessage = ee.ToString();
#endif
#if PERFLOG
                try { lock (perfLoadLogLock) File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "viewer-error.log"), $"page={e.WorkNumber}	{ee}" + Environment.NewLine); } catch { }
#endif
                var errorSize = new Size(-210, -297);
                var resizedSize = GetResizedSize(errorSize);
                rsa[e.WorkNumber] = resizedSize;
                //e.Result = BitmapEx.ConvertToBitmapEx(Program.GetErrorImage(resizedSize.Width, resizedSize.Height));
                e.Result = VirtualBitmapEx.GetErrorImage(resizedSize);
            }
            finally
            {
                sw?.Stop();
                bmwLoadEachPage_DoWork_DEBUG_WorkNumber = -1;
            }

        }

        // 入力のメモリをそのまま使えば高速だがマネージドではなくなる
        // 今の形では決して速くない


        // 画素のメモリも複製する Clone（C9）
        private static unsafe Bitmap DeepClone(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var result = new Bitmap(bmp.Width, bmp.Height, bmp.PixelFormat);
            try
            {
                var src = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);
                try
                {
                    var dst = result.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
                    try
                    {
                        var rowBytes = Math.Min(Math.Abs(src.Stride), Math.Abs(dst.Stride));
                        for (var y = 0; y < rect.Height; y++)
                        {
                            NativeMethods.CopyMemory(dst.Scan0 + y * dst.Stride, src.Scan0 + y * src.Stride, rowBytes);
                        }
                    }
                    finally
                    {
                        result.UnlockBits(dst);
                    }
                }
                finally
                {
                    bmp.UnlockBits(src);
                }
                if ((bmp.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
                {
                    result.Palette = bmp.Palette;
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static Bitmap Clone(Bitmap bmp)
        {
            //return bmp.Clone() as Bitmap; // 極めて高速だが Susie plugin 由来のデータなどをこれで複製するとその後 lockBits 等ができなくなる
            //return bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), bmp.PixelFormat); // こちらも同様

            // 安全かつ高速（ifwebp-x64.spi で動作確認）
            Bitmap result = null;
            try
            {
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, bmp.PixelFormat);
                try
                {
                    result = new Bitmap(data.Width, data.Height, data.Stride, data.PixelFormat, data.Scan0);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                if ((bmp.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
                {
                    result.Palette = bmp.Palette;
                }
                return result;
            }
            catch
            {
                result?.Dispose();
                throw;
            }
        }

    }
}
