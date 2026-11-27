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
        private void bmwMakePreview_DoWork(object sender, BackgroundMultiWorker.EachDoWorkEventArgs e)
        {
            // ジョブ番号 2*i はヘッダ（ページ数・解像度・動画情報など）の取得。
            // サムネイル（2*i+1）は ThumbViewerItem.LoadAsync が表示中のアイテムだけ読み込むため、
            // WorkSkipper で実行されないようにしている（A1）。
            e.Result = null;

            var index = e.WorkNumber;
            if ((index & 1) == 1) return;
            var index2 = index / 2;

            var zpa = ZipPathArray;

            if (loadingGuid != e.WorkSetGuid || zpa == null || zpa.Length <= index2) return;

            var zipPath = zpa[index2];

            // 見えていない項目のヘッダは取らずに後回しにする（K33）。見えたとき・列で並べ替えるときに requestDeferredHeader で取る。
            // 書庫・動画の中の項目は GetHeader がファイルを読まないので後回しにしない
            if (!headerWantAll && zipPath.IndexOf(Path.AltDirectorySeparatorChar) < 0)
            {
                var shown = headerShownSet;
                if (shown != null && !shown.Contains(index2))
                {
                    lock (headerDeferLock)
                    {
                        if (headerDeferGuid == e.WorkSetGuid)
                        {
                            headerDeferred.Add(zipPath);
                            return;
                        }
                    }
                }
            }

            var stat = FileStat.TryGet(zipPath); // フォルダか・大きさ・更新日時を 1 回の問い合わせで取る
            var isDir = stat != null && stat.IsDirectory;

            if (bmwMakePreview.CancellationPending) return;

            var task = Task.Run(() =>
            {
                try
                {
                    var done = GetHeader(zipPath, isDir, stat, out var plainFile, out var filecount, out var loadResult, out var fileSize, out var imageInfo, out var movieInfo);
                    e.Result = done ? new HeaderResult(zipPath, plainFile, filecount, loadResult, fileSize, imageInfo, movieInfo) : null;
                }
                catch { }
            });

            while (!bmwMakePreview.CancellationPending && !CatalogForm_FormClosing_FormClosed)
            {
                if (task.Wait(50))
                {
                    task.Dispose(); // task の Dispose は必須ではないが可能なら行う
                    break;
                }
            }
        }

        private string IfNegativeThenQuestion(int i)
        {
            return i >= 0 ? i.ToString() : "?";
        }

#if PERFLOG
        // 計測用（PERFLOG 定数を付けたビルドのみ）。bmwMakePreview の1回の実行（フォルダを開く→全ジョブ完了）ごとに
        // exe と同じフォルダの perf.log へ1行追記する。
        private static class MakePreviewPerfLog
        {
            private static readonly Stopwatch wall = new Stopwatch();
            private static long eachCount, eachTicks, sbmCalls, sbmOrderCalls, sbmTicks, headerResults, withPageCount, withImageInfo;
            public static void AddHeaderResult(int fileCount, bool hasImageInfo)
            {
                headerResults++; if (fileCount >= 0) withPageCount++; if (hasImageInfo) withImageInfo++;
            }
            private static double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
            public static void Start(int works)
            {
                wall.Restart();
                eachCount = eachTicks = sbmCalls = sbmOrderCalls = sbmTicks = headerResults = withPageCount = withImageInfo = 0;
                Write($"{DateTime.Now:HH:mm:ss}\tRunWorkerStarting\tworks={works}");
            }
            public static void AddEach(long ticks)
            {
                eachCount++; eachTicks += ticks;
                if (eachCount % 1000 == 0) Write($"{DateTime.Now:HH:mm:ss}\tprogress\teachCompleted={eachCount}\twallMs={wall.Elapsed.TotalMilliseconds:F0}");
            }
            private static void Write(string line)
            {
                PerfLog.Append("perf-detail.log", line);
            }
            public static void AddSetBackgroundMode(bool checkOrder, long ticks)
            {
                sbmCalls++; if (checkOrder) sbmOrderCalls++; sbmTicks += ticks;
            }
            public static void Finish(int works, int items)
            {
                if (!wall.IsRunning) return;
                wall.Stop();
                var line = $"{DateTime.Now:HH:mm:ss}\tAllRunWorkersCompleted\titems={items}\tworks={works}\twallMs={wall.Elapsed.TotalMilliseconds:F0}" +
                    $"\teachCompleted={eachCount}\teachHandlerMs={ToMs(eachTicks):F1}" +
                    $"\tsetBackgroundMode={sbmCalls}\t(withOrder={sbmOrderCalls})\tsetBackgroundModeMs={ToMs(sbmTicks):F1}" +
                    $"\theaderResults={headerResults}\twithPageCount={withPageCount}\twithImageInfo={withImageInfo}";
                PerfLog.Append("perf.log", line);
            }
        }
#endif

        Point bmwMakePreview_EachRunWorkerCompleted_CursorPosition = Point.Empty;

        private void bmwMakePreview_EachRunWorkerCompleted(object sender, BackgroundMultiWorker.EachRunWorkerCompletedEventArgs e)
        {
#if PERFLOG
            var perfStart = Stopwatch.GetTimestamp();
            try
            {
                bmwMakePreview_EachRunWorkerCompletedCore(e);
            }
            finally
            {
                MakePreviewPerfLog.AddEach(Stopwatch.GetTimestamp() - perfStart);
            }
#else
            bmwMakePreview_EachRunWorkerCompletedCore(e);
#endif
        }

        private void bmwMakePreview_EachRunWorkerCompletedCore(BackgroundMultiWorker.EachRunWorkerCompletedEventArgs e)
        {
            Program.AlertError(e.Error);
            var headerResult = e.Result as HeaderResult;
            // 結果が来るのはヘッダ取得のジョブだけ（サムネイルのジョブは WorkSkipper で実行されない）
            var show = headerResult != null && !e.Cancelled && e.WorkSetGuid == loadingGuid;
#if PERFLOG
            if (show) MakePreviewPerfLog.AddHeaderResult(headerResult.FileCount, headerResult.ImageInfo != null);
#endif
            if (show)
            {
                applyHeaderResult(e.WorkNumber / 2, headerResult);
            }

            // 実行順の並べ替え（全件 O(N)）はマウスが動いたときだけ、かつ 100ms に1回までにする。
            // 間引いた回はカーソル位置を記録しないので、次の完了時に改めて並べ替える。
            var now = Environment.TickCount;
            var cp = Cursor.Position;
            if (cp != bmwMakePreview_EachRunWorkerCompleted_CursorPosition && unchecked(now - bmwMakePreview_EachRunWorkerCompleted_LastOrderTick) >= 100)
            {
                bmwMakePreview_EachRunWorkerCompleted_CursorPosition = cp;
                bmwMakePreview_EachRunWorkerCompleted_LastOrderTick = now;
                SetBackgroundMode(true, 1);
            }
            else
            {
                SetBackgroundMode(false, 1);
            }

            // 進捗表示も 100ms に1回まで（最終値は AllRunWorkersCompleted で設定される）
            if (unchecked(now - bmwMakePreview_EachRunWorkerCompleted_LastCountTick) >= 100)
            {
                bmwMakePreview_EachRunWorkerCompleted_LastCountTick = now;
                loadCountToolStripMenuItem.Text = bmwMakePreview.CurrentThreadCount + "   " + e.CompletedWorksCount + " / " + ZipPathArray.Length;
            }
        }

        /// <summary>ヘッダ取得（GetHeader）の結果。ワーカーから完了イベントと、後回しにしたヘッダの読み込み（K33）へ渡す</summary>
        private sealed class HeaderResult
        {
            public readonly string Path;
            public readonly bool PlainFile;
            public readonly int FileCount;
            public readonly LoadResult LoadResult;
            public readonly long FileSize;
            public readonly ImageInfo ImageInfo;
            public readonly MovieInfo MovieInfo;
            public HeaderResult(string path, bool plainFile, int fileCount, LoadResult loadResult, long fileSize, ImageInfo imageInfo, MovieInfo movieInfo)
            {
                Path = path; PlainFile = plainFile; FileCount = fileCount; LoadResult = loadResult; FileSize = fileSize; ImageInfo = imageInfo; MovieInfo = movieInfo;
            }
        }

        /// <summary>
        /// ヘッダ取得（GetHeader）の結果を配列・ファイル一覧・ステータスバーに反映する（UI スレッドで呼ぶ）。
        /// bmwMakePreview のジョブと、後回しにしたヘッダの読み込み（K33）の両方から使う
        /// </summary>
        private void applyHeaderResult(int index, HeaderResult headerResult)
        {
            {
                var plainFile = headerResult.PlainFile;
                //var singlePath = "";
                try
                {
                    bool loadedIsSelected = false;
                    //bool singleEmpty = false;
                    bool singleCannotOpen = false;
                    //bool isDir = false;
                    string path, name;
                    lock (ZipPathArrayLocker)
                    {
                        // リロードによって FileCountArray 等が変更されることがある。
                        // 一度消えて同じパスが再設定された場合ここでは判定できないが、
                        // その場合このまま続ければ良い
                        if (index >= FileCountArray.Length || index >= LoadResultArray.Length || headerResult.Path != ZipPathArray[index])
                        {
                            return;
                        }

                        // マスクされていたら何もしない
                        if (!ZipMaskArray[index])
                        {
                            return;
                        }

                        FileCountArray[index] = headerResult.FileCount;
                        // ヘッダの結果（通常は NotYet）で、サムネイル側が先に設定した Success などを上書きしない（A11）
                        if (headerResult.LoadResult != LoadResult.NotYet || LoadResultArray[index] == LoadResult.NotYet)
                        {
                            LoadResultArray[index] = headerResult.LoadResult;
                        }
                        if (headerResult.FileSize >= 0) ZipSizeArray[index] = headerResult.FileSize;
                        if (headerResult.ImageInfo != null) ImageInfoArray[index] = headerResult.ImageInfo;
                        if (headerResult.MovieInfo != null) MovieInfoArray[index] = headerResult.MovieInfo;
                        //page = ZipPageArray[index];
                        path = ZipPathArray[index];
                        name = ZipNameArray[index];

                        if (cmsRightClickPrepareAndShow_ZipPlaInfoReady)
                        {
                            if (tvCatalog.IsSelected(index))
                            {
                                var selectedIndices = tvCatalog.SelectedIndices;
                                loadedIsSelected = true;
                                if (selectedIndices.Length == 1)
                                {
                                    var selectedIndex = selectedIndices[0];
                                    if (0 <= selectedIndex && selectedIndex < LoadResultArray.Length)
                                    {
                                        //singlePath = ZipPathArray[selectedIndex];
                                        var loadResult = LoadResultArray[selectedIndex];
                                        singleCannotOpen = loadResult == LoadResult.FileNotFound || loadResult == LoadResult.LoadError;
                                        //if (singleCannotOpen)
                                        {
                                            // isDir = ZipNameArray[selectedIndex].EndsWith(Path.DirectorySeparatorChar.ToString());
                                        }
                                    }
                                }
                            }
                        }

                        setFileListRowInLock(index, dynamicOnly: true, localOnly: false, cultureForOnlyStatic: null, volatilitySortCancelIfChanged: true);
                    }

                    // tvCatalog が更新されるのは古いワーカーが片付いたあとなので
                    // インデクサへのアクセスは問題にならない。
                    {
                        // 一度消えて同じパスが再設定された場合を考慮する必要がある
                        // サムネイルは ThumbViewerItem が自分で読み込むので、既存のアイテムの Image には触らない
                        // （null にすると読み込み済みのサムネイルが消えて再生成される）。
                        // パスが変わっていれば FilePath の setter が Image を破棄する。
                        var item = tvCatalog[index];
                        if (item == null)
                        {
                            item = new ThumbViewerItem(){ OwnerCatalog = this };
                            item.Text = name;
                            tvCatalog[index] = item;
                        }
                        item.FilePath = headerResult.Path;
                    }

                    // setFileListRowInLock 内部で行う

                    var dir = name.EndsWith(Path.DirectorySeparatorChar.ToString());
                    var archive = dir || PackedImageLoader.Supports(path);

                    if (tvCatalog.IsSelected(index))
                    {
                        ShowStatusBarAfterItemLoaded();
                    }

                    if (cmsRightClickPrepareAndShow_ZipPlaInfoReady && loadedIsSelected)
                    {

                        if (singleCannotOpen)// && BuiltInViewerIsDefault())
                        {

                            setAppInContextMenu(-1);
                        }
                    }
                }
                catch (Exception error)
                {
                    Program.AlertError(error);
                }
            }
        }

        private int bmwMakePreview_EachRunWorkerCompleted_LastOrderTick = Environment.TickCount - 1000;

        private int bmwMakePreview_EachRunWorkerCompleted_LastCountTick = Environment.TickCount - 1000;

        // 表示順に並べた選択を、クリックした項目が先頭になるように回す。クリックした項目が選択になければ null
        private static int[] rotateToStartFrom(int[] indices, int firstValue)
        {
            var start = Array.IndexOf(indices, firstValue);
            if (start < 0) return null;
            var result = new int[indices.Length];
            Array.Copy(indices, start, result, 0, indices.Length - start);
            Array.Copy(indices, 0, result, indices.Length - start, start);
            return result;
        }

        private void getSelectedPathArray(int clickedIndex, out string[] selectedPaths, out int[] pages, out bool[] isDirs, bool pageReset = false)
        {
            selectedPaths = null;
            isDirs = null;
            pages = null;
            var selectedIndices = tvCatalog.SelectedIndices;
            if (selectedIndices.Length == 0) return;
            var zpa = ZipPathArray;
            if (zpa == null) return;
            var zna = ZipNameArray;
            if (zna == null) return;
            var pga = ZipPageArray;
            if (pga == null) return;
            var count = zpa.Length;
            if (count != zna.Length || count != pga.Length) return;
            int[] fca;
            if (pageReset)
            {
                fca = FileCountArray;
                if (fca == null || fca.Length != count) return;
            }
            else fca = null;

            int[] d2s;
            try
            {
                d2s = tvCatalog.DataIndexToShowIndex;
                selectedIndices = selectedIndices.OrderBy(i => d2s[i]).ToArray();
                if (!pageReset) d2s = null;

                if (clickedIndex >= 0)
                {
                    selectedIndices = rotateToStartFrom(selectedIndices, clickedIndex);
                    if (selectedIndices == null) return;
                }
            }
            catch
            {
                return;
            }

            selectedPaths = new string[selectedIndices.Length];
            isDirs = new bool[selectedIndices.Length];
            if (!pageReset) pages = new int[selectedIndices.Length];
            var page = -1;
            var tbcPageIndex = pageReset ? tbcPage.Index : -1;
            for (var i = 0; i < selectedIndices.Length; i++)
            {
                var index = selectedIndices[i];
                if (index < 0 || index >= count)
                {
                    selectedPaths = null;
                    isDirs = null;
                    pages = null;
                    return;
                }
                var path = zpa[index];
                var name = zna[index];
                if (pageReset)
                {
                    pga[index] = -1;
                    setFileListText(d2s[index], dgvFileList.Columns[tbcPageIndex], getPageString(-1, fca[index]));
                }
                else
                {
                    page = pga[index];
                }
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name))
                {
                    selectedPaths = null;
                    isDirs = null;
                    pages = null;
                    return;
                }
                selectedPaths[i] = path;
                isDirs[i] = name.EndsWith(Path.DirectorySeparatorChar.ToString());
                if (!pageReset) pages[i] = page;
            }
            if (pageReset) tvCatalog.Invalidate();
        }

        private void getSelectedPathArray(int clickedIndex, out string[] selectedPaths, out bool[] isDirs)
        {
            selectedPaths = null;
            isDirs = null;
            var selectedIndices = tvCatalog.SelectedIndices;
            if (selectedIndices.Length == 0) return;
            var zpa = ZipPathArray;
            if (zpa == null) return;
            var zna = ZipNameArray;
            if (zna == null) return;
            var count = zpa.Length;
            if (count != zna.Length) return;

            try
            {
                var d2s = tvCatalog.DataIndexToShowIndex;
                selectedIndices = selectedIndices.OrderBy(i => d2s[i]).ToArray();

                if (clickedIndex >= 0)
                {
                    selectedIndices = rotateToStartFrom(selectedIndices, clickedIndex);
                    if (selectedIndices == null) return;
                }
            }
            catch
            {
                return;
            }

            selectedPaths = new string[selectedIndices.Length];
            isDirs = new bool[selectedIndices.Length];
            for (var i = 0; i < selectedIndices.Length; i++)
            {
                var index = selectedIndices[i];
                if (index < 0 || index >= count)
                {
                    selectedPaths = null;
                    isDirs = null;
                    return;
                }
                var path = zpa[index];
                var name = zna[index];
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name))
                {
                    selectedPaths = null;
                    isDirs = null;
                    return;
                }
                selectedPaths[i] = path;
                isDirs[i] = name.EndsWith(Path.DirectorySeparatorChar.ToString());
            }
        }

        private int setAppInContextMenu_LastClickedIndex;

        private void setAppInContextMenu(int clickedIndex)
        {
            if (clickedIndex < 0) clickedIndex = setAppInContextMenu_LastClickedIndex;
            else setAppInContextMenu_LastClickedIndex = clickedIndex;
            var items = cmsRightClick.Items;
            var start = items.IndexOf(appStartSeparator);
            if (start < 0) return;
            start++;
            var stop = items.IndexOf(appStopSeparator);
            if (stop < 0) return;

            for (var i = start; i < stop; i++)
            {
                var disposedItem = items[start];
                items.RemoveAt(start);
                disposedItem.Dispose();
            }

            stop = start;

            string[] selectedPaths;
            bool[] isDirs;
            getSelectedPathArray(clickedIndex, out selectedPaths, out isDirs);
            if (selectedPaths == null || selectedPaths.Length <= 0) return;

            var defaultNotFound = true;
            ApplicationProvider[] applicationArray;
            if (CoverSettingMode)
            {
                defaultNotFound = false;
                var tsmi = new ToolStripMenuItem(Message.SetCoverToThis);
                tsmi.Font = Program.StyledFont(tsmi.Font, FontStyle.Bold);
                tsmi.ShortcutKeyDisplayString = "Enter";
                if (selectedPaths.Length == 1)
                {
                    tsmi.Click += setCoverToThisToolStripMenuItem_Click;
                }
                else
                {
                    tsmi.Enabled = false;
                }
                items.Insert(stop++, tsmi);

                var openInZipPla = ApplicationArray?.FirstOrDefault(a => a is MoveLocationProvider) ?? new MoveLocationProvider(this, null, null, null);
                applicationArray = new ApplicationProvider[1] { openInZipPla };
            }
            else
            {
                applicationArray = ApplicationArray;
            }
            var infos = new FileInfoForApplicationProvider[selectedPaths.Length];
            for (var i = 0; i < selectedPaths.Length; i++)
            {
                infos[i] = new FileInfoForApplicationProvider { Path = selectedPaths[i], IsDir = isDirs[i] };
            }
            if (InsertAppToCToolStrip(items, stop, infos, defaultNotFound, selectedPaths, applicationArray, AppInContextMenuEnabled) == 0 && !CoverSettingMode)
            {
                var tsmi = new ToolStripMenuItem(Message.ApplicationIsNotSpecified);
                tsmi.Enabled = false;
                items.Insert(stop++, tsmi);
            }
        }

        bool AppInContextMenuEnabled;

        void SetAppInContextMenuEnabled(bool value)
        {
            if (AppInContextMenuEnabled != value)
            {
                var items = cmsRightClick.Items;
                var start = items.IndexOf(appStartSeparator);
                if (start < 0) return;
                start++;
                var stop = items.IndexOf(appStopSeparator);
                if (stop < 0) return;
                for (var i = start; i < stop; i++) items[i].Enabled = value;
                AppInContextMenuEnabled = value;
            }
        }

        private int InsertAppToCToolStrip(ToolStripItemCollection items, int startIndex, FileInfoForApplicationProvider[] infos,
            bool addDefault, string[] selectedPaths, ApplicationProvider[] applicationArray, bool enabled)
        {
            if (applicationArray == null) return 0;
            if (selectedPaths == null) selectedPaths = (from info in infos select info.Path).ToArray();
            var index = startIndex;
            foreach (var app in applicationArray)
            {
                //if (selectedPaths.Length > 1 && app.GetMultiFileMode() == MultiFileMode.Single) continue;
                var ok = app.Supports(infos);
                var isDefault = addDefault && app.IsDefaultFor(infos);

                if (isDefault) addDefault = false;
                if (!ok) continue;
                var tsmi = app.GetToolStripMenuItem(this, selectedPaths);
                if (isDefault)
                {
                    tsmi.Font = Program.StyledFont(tsmi.Font, FontStyle.Bold);
                    tsmi.ShortcutKeyDisplayString = "Enter";
                }
                else
                {
                    tsmi.Font = Program.StyledFont(tsmi.Font, FontStyle.Regular);
                    tsmi.ShortcutKeyDisplayString = null;
                }
                if (!enabled) tsmi.Enabled = false;
                items.Insert(index++, tsmi);
            }
            return index - startIndex;

        }

        private void bmwMakePreview_AllRunWorkersCompleted(object sender, BackgroundMultiWorker.AllRunWorkerCompletedEventArgs e)
        {
            if (!CreatingZipPathArray)
            {
                // ベースの RunWorkerCompleted がメインスレッドで実行されないので
                try
                {
                    Invoke(((MethodInvoker)(() =>
                    {
                        if (!CreatingZipPathArray)
                        {
                            // ドラッグアウトのため Dispose してはいけない
                            // いずれにしても場所切替時に Dispose される

                            loadCountToolStripMenuItem.Text = bmwMakePreview.CurrentThreadCount + "   " + e.Results.Count + " / " + ZipPathArray.Length;
#if PERFLOG
                            MakePreviewPerfLog.Finish(e.Results.Count, ZipPathArray.Length);
#endif
                            // 列での並べ替えがヘッダを待っていたら、後回しの分の読み込みも終わっていれば並べ直す（K33）。
                            // この時点では IsBusy がまだ true なので、メッセージループに戻ってから確かめる
                            if (pendingVolatileSort != null) BeginInvoke((MethodInvoker)tryFinishPendingVolatileSort);
                        }
                    })));
                }
                catch (ObjectDisposedException) { }
            }
        }

        private bool selectionSynchronizing = false;

        private void tvCatalog_SelectedIndexChanged(object sender, ThumbViewer.SelectedIndexChangedEventArgs e)
        {
            var newIndex = e.NewIndex;
            lock (ZipPathArrayLocker)
            {
                if (newIndex >= 0 && ZipPathArray != null && ZipPathArray.Length > newIndex && LoadResultArray != null)
                {
                    if (LoadResultArray[newIndex] != LoadResult.LoadError && LoadResultArray[newIndex] != LoadResult.FileNotFound)
                    {
                        //SetLookAheadArchive(ZipPathArray[newIndex], ZipPageArray[newIndex]);
                        SetLookAheadArchive(newIndex);
                    }
                    //ShowStatusBar(newIndex);
                }
                else
                {
                    //ShowStatusBar(-1);
                }
                ShowStatusBar();
            }

            selectionSynchronizingFromThumbnailToFileList();
        }

        private void selectionSynchronizingFromThumbnailToFileList(bool changeScroll = true)
        {
            //if (!fileListToolStripMenuItem.Checked) return; // ファイルリストが一度も表示されていない場合この処理は不要、かつ正常に動作しない
            var firstDisplayedScrollingRowIndex = dgvFileList.FirstDisplayedScrollingRowIndex;
            if (firstDisplayedScrollingRowIndex < 0)
            {
                return; // こちらでより自然な動作に
            }
            if (!selectionSynchronizing && tvCatalog.ShowIndexToDataIndex.Length == dgvFileList.Rows.Count)
            {
                selectionSynchronizing = true;
                var selectedIndices = tvCatalog.SelectedIndices;
                var d2s = tvCatalog.DataIndexToShowIndex;
                var selectedIndicesCount = selectedIndices.Length;
                var minimumShowSelectedIndex = int.MaxValue;
                for (var i = 0; i < selectedIndicesCount; i++)
                {
                    var showIndex = d2s[selectedIndices[i]];
                    selectedIndices[i] = showIndex;
                    if (showIndex < minimumShowSelectedIndex)
                    {
                        minimumShowSelectedIndex = showIndex;
                    }
                }
                var firstDisplayedScrollingRowIndexShouldBeChange = false;
                if (dgvFileList.Visible)
                {
                    var f = tvCatalog.FocusedIndex;
                    if (f >= 0) f = d2s[f];
                    else f = minimumShowSelectedIndex;
                    if (0 <= f && f < dgvFileList.RowCount)
                    {
                        //dgvFileList.Focus();
                        dgvFileList.CurrentCell = dgvFileList.Rows[f].Cells[tbcFileName.Index];
                        if (dgvFileList.FirstDisplayedScrollingRowIndex != firstDisplayedScrollingRowIndex)
                        {
                            if (!changeScroll)
                            {
                                dgvFileList.FirstDisplayedScrollingRowIndex = firstDisplayedScrollingRowIndex;
                            }
                            else
                            {
                                firstDisplayedScrollingRowIndexShouldBeChange = true;
                            }
                        }
                    }
                }
                // 行を共有したまま選択を変える（Rows[i] で行を取り出すと、その行のセルが作られる）
                var rowCount = dgvFileList.RowCount;
                var shouldBeSelected = new bool[rowCount];
                foreach (var showIndex in selectedIndices)
                {
                    if (0 <= showIndex && showIndex < rowCount) shouldBeSelected[showIndex] = true;
                }
                dgvFileList.BeginSelectionBatch();
                try
                {
                    for (var i = 0; i < rowCount; i++) dgvFileList.SetRowSelected(i, shouldBeSelected[i]);
                }
                finally
                {
                    dgvFileList.EndSelectionBatch();
                }
                if (changeScroll)
                {
                    int selectedIndex;
                    if (selectedIndicesCount == 1 && (selectedIndex = selectedIndices[0]) >= 0)
                    {
                        if (selectedIndex < firstDisplayedScrollingRowIndex)
                        {
                            dgvFileList.FirstDisplayedScrollingRowIndex = selectedIndices[0];
                        }
                        else
                        {
                            var realFirstDisplayedScrollingRowIndex = firstDisplayedScrollingRowIndexShouldBeChange ? dgvFileList.FirstDisplayedScrollingRowIndex : firstDisplayedScrollingRowIndex;
                            var topRect = dgvFileList.GetRowDisplayRectangle(realFirstDisplayedScrollingRowIndex, false);
                            if (topRect.Height > 0)
                            {
                                // CurrentCell 変更時に FirstDisplayedScrollingRowIndex が変わっていたら変化後の RowIndex が必要
                                var rowsHeight = dgvFileList.ClientRectangle.Bottom - topRect.Top;
                                var verticalOffset = rowsHeight / topRect.Height - 1;
                                var lastDisplayedScrollingRowIndex = firstDisplayedScrollingRowIndex + verticalOffset;
                                if ((dgvFileList.ScrollBars & ScrollBars.Horizontal) != 0)
                                {
                                    var count1 = dgvFileList.Rows.Count - 1;
                                    while (verticalOffset > 0 && dgvFileList.GetRowDisplayRectangle(
                                        Math.Min(realFirstDisplayedScrollingRowIndex + verticalOffset, count1), true).Height < topRect.Height)
                                    {
                                        lastDisplayedScrollingRowIndex--;
                                        verticalOffset--;
                                    }
                                }
                                if (selectedIndex > lastDisplayedScrollingRowIndex)
                                {
                                    var firstIndex = Math.Max(selectedIndices[0] - verticalOffset, 0);
                                    dgvFileList.FirstDisplayedScrollingRowIndex = firstIndex;
                                }
                                else if (firstDisplayedScrollingRowIndexShouldBeChange)
                                {
                                    dgvFileList.FirstDisplayedScrollingRowIndex = firstDisplayedScrollingRowIndex;
                                }
                            }
                            else if (firstDisplayedScrollingRowIndexShouldBeChange)
                            {
                                dgvFileList.FirstDisplayedScrollingRowIndex = firstDisplayedScrollingRowIndex;
                            }
                        }

                    }
                    else if (firstDisplayedScrollingRowIndexShouldBeChange)
                    {
                        dgvFileList.FirstDisplayedScrollingRowIndex = firstDisplayedScrollingRowIndex;
                    }
                }
                selectionSynchronizing = false;
            }
        }

        private void dgvFileList_SelectionChanged(object sender, EventArgs e)
        {
            var s2d = tvCatalog.ShowIndexToDataIndex;
            if (fileListToolStripMenuItem.Checked && !selectionSynchronizing && s2d.Length == dgvFileList.Rows.Count)
            {
                selectionSynchronizing = true;

                var selectedIndices = dgvFileList.GetSelectedRowIndices();
                var selectedIndicesCount = selectedIndices.Length;
                for (var i = 0; i < selectedIndicesCount; i++)
                {
                    selectedIndices[i] = s2d[selectedIndices[i]];
                }
                tvCatalog.SelectedIndices = selectedIndices;
                if (selectedIndices.Length == 1)
                {
                    tvCatalog.ScrollBarToIndexWithMinimalMove(tvCatalog.DataIndexToShowIndex[selectedIndices[0]]);
                }

                selectionSynchronizing = false;
            }
        }

        private bool SenseBindingMode()
        {
            if (InDirectoryVirtualDirectoryOrSmartDirectory && (IsDisplay(SubfolderMode) || ArchiveLoadLevel != ItemLoadLevel.Ignore) &&
                currentClipMode == ClipMode.PlaClip && ZipPlaInfo.GetOnlyBindingModeFromFullName(currentLocation) == null)
            {
                var zna = ZipNameArray;
                var zma = ZipMaskArray;
                if (zna != null && zma != null)
                {
                    var count = zna.Length;
                    if (zma.Length == count)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            if (zma[i])
                            {
                                var name = zna[i];
                                if (!string.IsNullOrEmpty(name) && (name[name.Length - 1] == Path.DirectorySeparatorChar || PackedImageLoader.Supports(name)) &&
                                    ZipPlaInfo.GetOnlyBindingModeFromFullName(name) == null)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        int SetBackgroundMode_ShownRange = 0;

        int[] SetBackgroundMode_WorkerOrder = null;

        // サムネヘッダ→リストヘッダ→サムネサムネ→残りヘッダ→残りサムネ
        private void SetBackgroundMode(bool checkOrder, int processDecrease, bool DoEvenIfNotBusy = false)
        {
#if PERFLOG
            var perfStart = Stopwatch.GetTimestamp();
            try
            {
                SetBackgroundModeCore(checkOrder, processDecrease, DoEvenIfNotBusy);
            }
            finally
            {
                MakePreviewPerfLog.AddSetBackgroundMode(checkOrder, Stopwatch.GetTimestamp() - perfStart);
            }
#else
            SetBackgroundModeCore(checkOrder, processDecrease, DoEvenIfNotBusy);
#endif
        }

        private void SetBackgroundModeCore(bool checkOrder, int processDecrease, bool DoEvenIfNotBusy)
        {
            if (CreatingZipPathArray) return;
            if (RenameShownArchive_MakePreviewSleepRequest) return;

            var refThumbnail = pnlCenter.Visible;
            var refFileList = !refThumbnail || pnlRight.Visible;

            var cursorPosition = Cursor.Position;
            Point? cursorPositionOnTvCatalog = null;

            int thumbnailBitForShown, thumbnailBitForHidden;
            if (!refFileList)
            {
                thumbnailBitForShown = 1;
                thumbnailBitForHidden = 1;
            }
            else if (!refThumbnail)
            {
                thumbnailBitForShown = 0;
                thumbnailBitForHidden = 0;
            }
            else
            {
                Point p;
                p = tvCatalog.PointToClient(cursorPosition);
                cursorPositionOnTvCatalog = p;
                if (tvCatalog.Bounds.Contains(p))
                {
                    thumbnailBitForShown = 1;
                    thumbnailBitForHidden = 1;
                }
                else
                {
                    thumbnailBitForShown = 1;
                    thumbnailBitForHidden = 0;
                }
            }


            int[] s2d;
            lock (ZipPathArrayLocker)
            {
                if (ZipPathArray == null || !DoEvenIfNotBusy && !bmwMakePreview.IsBusy) return;

                // ZipPathArray の作成がサブスレッドで行われ、その後メインスレッドで tvCatalog の初期化が行われるため、
                // その間にこの関数が呼びだされた場合インデックスの範囲に矛盾が生じるためこれを回避する。
                var d2s = tvCatalog.DataIndexToShowIndex;
                if (d2s == null || d2s.Length != ZipPathArray.Length || d2s.Length > initialZipPathLength)
                {
                    return;
                }

                s2d = tvCatalog.ShowIndexToDataIndex;

                long stt = 0, stp = -1;
                if (checkOrder)
                {
                    long u = -1, d = 0;
                    var Order = new int[initialZipPathLength * 2];
                    var alreadyAdded = new bool[initialZipPathLength];
                    var idx = 0;
                    var thumbStop = 0;
                    if (refThumbnail)
                    {
                        var preIdx = 0;
                        var start0 = tvCatalog.DisplayedStartShowIndex;
                        var stop = tvCatalog.DisplayedStopShowIndex;
                        var OrderInDisplay = new int[stop - start0];
                        var Distances = new int[stop - start0];
                        var p = cursorPositionOnTvCatalog == null ? tvCatalog.PointToClient(cursorPosition) : (Point)cursorPositionOnTvCatalog;
                        int x = 2 * p.X - tvCatalog.gridSizeH, y = 2 * p.Y - tvCatalog.gridSizeV;
                        for (var i = (int)start0; i < stop; i++)
                        {
                            var p2 = tvCatalog.GetUpperLeftClientcoordinateOf(i);
                            int dx = x - 2 * p2.X, dy = y - 2 * p2.Y;
                            OrderInDisplay[preIdx] = i;
                            Distances[preIdx] = dx * dx + dy * dy;
                            preIdx++;
                        }
                        OrderInDisplay = OrderInDisplay.OrderBy(v => Distances[v - start0]).ToArray();
                        for (var i = 0; i < Distances.Length; i++)
                        {
                            var di = s2d[OrderInDisplay[i]];
                            if (di < initialZipPathLength && !alreadyAdded[di])
                            {
                                alreadyAdded[di] = true;
                                Order[idx++] = 2 * di | thumbnailBitForShown;
                            }
                        }
                        u = start0 - 1;
                        d = stop;
                        stt = start0;
                        stp = stop;
                        thumbStop = idx;
                    }
                    if (refFileList)
                    {
                        var preIdx = 0;
                        var start0 = dgvFileList.FirstDisplayedScrollingRowIndex;
                        var stop = start0 + dgvFileList.DisplayedRowCount(includePartialRow: true); if (stop > s2d.Length) stop = s2d.Length;
                        if (stop < start0 || stop < 0 || start0 < 0) start0 = stop = 0;
                        var OrderInDisplay = new int[stop - start0];
                        var Distances = new int[stop - start0];
                        Point p;
                        p = dgvFileList.PointToClient(cursorPosition);
                        var crec = dgvFileList.ClientRectangle;
                        var mouseShowIndex = dgvFileList.HitTest(crec.X, p.Y).RowIndex;
                        if (mouseShowIndex < 0)
                        {
                            if (p.X < crec.Bottom) mouseShowIndex = (int)start0;
                            else mouseShowIndex = (int)stop - 1;
                        }

                        for (var i = (int)start0; i < stop; i++)
                        {
                            var di = Math.Abs(i - mouseShowIndex);
                            OrderInDisplay[preIdx] = i;
                            Distances[preIdx] = di;
                            preIdx++;
                        }
                        OrderInDisplay = OrderInDisplay.OrderBy(v => Distances[v - start0]).ToArray();
                        for (var i = 0; i < Distances.Length; i++)
                        {
                            var di = s2d[OrderInDisplay[i]];
                            if (di < initialZipPathLength && !alreadyAdded[di])
                            {
                                alreadyAdded[di] = true;
                                Order[idx++] = 2 * di;
                            }
                        }
                        if (!refThumbnail)
                        {
                            u = start0 - 1;
                            d = stop;
                            stt = start0;
                            stp = stop;
                        }
                    }

                    var shownCount = idx;
                    var listCount = shownCount - thumbStop;

                    // 表示中の項目だけヘッダを取り、ほかは後回しにする（K33）
                    var shownSet = new HashSet<int>();
                    for (var i = 0; i < shownCount; i++) shownSet.Add(Order[i] >> 1);
                    headerShownSet = shownSet;
                    {
                        for (var i = 0; i < thumbStop; i++) Order[i + idx] = Order[i] ^ 1;
                        idx += thumbStop;
                    }

                    SetBackgroundMode_ShownRange = idx;

                    long len = tvCatalog.ShowIndexToDataIndex.Length;
                    while (true)
                    {
                        if (stt - u < d - stp && u >= 0)
                        {
                            var di = s2d[u--];
                            if (di < initialZipPathLength && !alreadyAdded[di])
                            {
                                alreadyAdded[di] = true;
                                Order[idx++] = 2 * di | thumbnailBitForHidden;
                            }
                        }
                        else if (d < len)
                        {
                            var di = s2d[d++];///
                            if (di < initialZipPathLength && !alreadyAdded[di])
                            {
                                alreadyAdded[di] = true;
                                Order[idx++] = 2 * di | thumbnailBitForHidden;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    while (u >= 0)
                    {
                        var di = s2d[u--];
                        if (di < initialZipPathLength && !alreadyAdded[di])
                        {
                            alreadyAdded[di] = true;
                            Order[idx++] = 2 * di | thumbnailBitForHidden;
                        }
                    }
                    while (d < len)
                    {
                        var di = s2d[d++];
                        if (di < initialZipPathLength && !alreadyAdded[di])
                        {
                            alreadyAdded[di] = true;
                            Order[idx++] = 2 * di | thumbnailBitForHidden;
                        }
                    }
                    for (var i = 0; i < initialZipPathLength; i++)
                    {
                        if (d2s[i] < 0 && ZipMaskArray[i])
                        {
                            Order[idx++] = 2 * i | thumbnailBitForHidden;
                        }
                    }

                    var hideStop = idx;
                    var hideCount = idx - SetBackgroundMode_ShownRange;

                    var shownAndhideCount = shownCount + hideCount;
                    for (var i = thumbStop; i < shownCount; i++) Order[i + shownAndhideCount] = Order[i] ^ 1;

                    var hideAndListCount = shownCount + shownAndhideCount - SetBackgroundMode_ShownRange;

                    for (var i = SetBackgroundMode_ShownRange; i < hideStop; i++) Order[i + hideAndListCount] = Order[i] ^ 1;


                    idx = hideStop + hideAndListCount;

                    SetBackgroundMode_WorkerOrder = new int[idx];
                    Array.Copy(Order, SetBackgroundMode_WorkerOrder, idx);
                    bmwMakePreview.SetWorksOrder(SetBackgroundMode_WorkerOrder);
                }
            }

            if (AllowFullpower && !CursorIsOnControl() && !(((MouseButtons & MouseButtons.Left) == MouseButtons.Left || cbFilter.Focused) && Form.ActiveForm == this))
            {
                bmwMakePreview.ThreadCount = Math.Max(2, Environment.ProcessorCount - 1);
                Program.SetPriority(ProcessPriorityClass.BelowNormal);
            }
            else
            {
                try
                {
                    int WaitingDisplayedCount = 0;
                    if (SetBackgroundMode_WorkerOrder != null && SetBackgroundMode_WorkerOrder.Length >= SetBackgroundMode_ShownRange)
                    {
                        for (var i = 0; i < SetBackgroundMode_ShownRange; i++)
                        {
                            var workerIndex = SetBackgroundMode_WorkerOrder[i];
                            if (bmwMakePreview.GetWorkState(workerIndex) == BackgroundMultiWorker.WorkState.Waiting)
                            {
                                WaitingDisplayedCount++;
                            }
                        }
                    }
                    bmwMakePreview.ThreadCount = Math.Max(1, Math.Min(bmwMakePreview.ThreadCount + WaitingDisplayedCount - processDecrease, Environment.ProcessorCount / 2));
                    Program.SetPriority(ProcessPriorityClass.Normal);
                    return;
                }
                catch (Exception error)
                {
                    Program.AlertError(error);
                }
            }
        }
    }
}
