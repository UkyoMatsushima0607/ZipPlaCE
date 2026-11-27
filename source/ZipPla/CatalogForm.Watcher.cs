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
        private string fileSystemWatcherErrorPath;

        private void fileSystemWatcher_Error(object sender, ErrorEventArgs e)
        {
            if (IsDisposed || !fileSystemWatcher.EnableRaisingEvents) return;
            fileSystemWatcherErrorPath = fileSystemWatcher.Path;
            if (fileSystemWatcherErrorTimer == null)
            {
                fileSystemWatcherErrorTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                fileSystemWatcherErrorTimer.Tick += (s, ee) =>
                {
                    fileSystemWatcherErrorTimer.Stop();
                    if (IsDisposed || CatalogForm_FormClosing_FormClosed) return;
                    // 待っている間に別の場所へ移ったら、移った先はもう読み込み直している
                    if (!fileSystemWatcher.EnableRaisingEvents || !string.Equals(fileSystemWatcher.Path, fileSystemWatcherErrorPath, StringComparison.OrdinalIgnoreCase)) return;
                    userReload();
                };
            }
            // 続けてあふれたら延ばす
            fileSystemWatcherErrorTimer.Stop();
            fileSystemWatcherErrorTimer.Start();
        }

        private void fileSystemWatcher_Deleted(object sender, FileSystemEventArgs e)
        {
            var fullPath = e.FullPath;
            GPSizeThumbnail.TryMove(thumbnailCache, e.FullPath, null);
            if (CreatingZipPathArray)
            {
                BackgroundMultiWorker.RunWorkerStartingEventHandler eh = null;
                eh = (s2, e2) =>
                {
                    if (!fileSystemWatcherStopper(ref e)) removeItem(fullPath);
                    bmwMakePreview.RunWorkerStarting -= eh;
                };
                bmwMakePreview.RunWorkerStarting += eh;
            }
            else
            {
                removeItem(fullPath);
            }
        }

        private bool isCacheFolder(string path)
        {
            var c = thumbnailCache;
            if (c == null || c == GPSizeThumbnail.AlternateDataStream) return false;
            try
            {
                return Path.GetFileName(path).ToLower() == Path.GetFileName(thumbnailCache).ToLower();
            }
            catch
            {
                return false;
            }
        }

        private bool fileSystemWatcherStopper(ref FileSystemEventArgs e)
        {
            // FileSystemWatcher は以下の２行が意味を持つという期待されない仕様を持つ
            if (!fileSystemWatcher.EnableRaisingEvents) return true;
            var path = fileSystemWatcher.Path;

            if (string.IsNullOrEmpty(path)) return true;
            try
            {
                var pathToLower = path.ToLower();
                if (!string.IsNullOrEmpty(pathToLower) && pathToLower.Last() == ':') pathToLower += Path.DirectorySeparatorChar;
                var dirName = Path.GetDirectoryName(e.FullPath);
                var dirNameInLower = dirName.ToLower();
                if (pathToLower == dirNameInLower)
                {
                    return false;
                }

                var subfolderIsSearched = SubfolderIsSearched();
                if (subfolderIsSearched && !IsDisplay(SubfolderMode))
                {
                    var pathInLowerYen = pathToLower[pathToLower.Length - 1] == Path.DirectorySeparatorChar ? pathToLower : (pathToLower + Path.DirectorySeparatorChar);
                    return !dirNameInLower.StartsWith(pathInLowerYen);
                }
                else
                {
                    if (!subfolderIsSearched && ((e.ChangeType & WatcherChangeTypes.Changed) != WatcherChangeTypes.Changed || !IsDisplay(SubfolderMode)))
                    {
                        return true;
                    }
                    var pathInLowerYen = pathToLower[pathToLower.Length - 1] == Path.DirectorySeparatorChar ? pathToLower : (pathToLower + Path.DirectorySeparatorChar);
                    if (!dirNameInLower.StartsWith(pathInLowerYen))
                    {
                        return true;
                    }
                    var pathYenLen = pathInLowerYen.Length;
                    var pos = dirNameInLower.IndexOf(Path.DirectorySeparatorChar, pathYenLen + 1);
                    if (pos < 0) pos = dirNameInLower.Length;
                    var depth = dirName.Substring(pos).Count(c => c == Path.DirectorySeparatorChar);
                    int depthBound;
                    switch (ArchivesInArchiveMode)
                    {
                        case ArchivesInArchiveMode.Ignore: depthBound = 0; break;
                        case ArchivesInArchiveMode.IfNoOther1Level:
                        case ArchivesInArchiveMode.Always1Level: depthBound = 1; break;
                        default: depthBound = 2; break;
                    }
                    if (depth <= depthBound || depth - 1 == depthBound && Directory.Exists(dirName.Substring(0, pos)))
                    {
                        var childName = dirName.Substring(pathYenLen, pos - pathYenLen);
                        e = new FileSystemEventArgs(e.ChangeType, path, childName);
                        return false;
                    }
                    else return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private void fileSystemWatcher_Created(object sender, FileSystemEventArgs e)
        {
            if (fileSystemWatcherStopper(ref e)) return;
            var fullPath = e.FullPath;
            var startingGuid = loadingGuid;
            if (CreatingZipPathArray)
            {
                BackgroundMultiWorker.RunWorkerStartingEventHandler eh = null;
                eh = (s2, e2) =>
                {
                    //addOrReloadItem(fullPath, thumbnailChanged: null);
                    if (!fileSystemWatcherStopper(ref e)) addOrReloadItem(fullPath, thumbnailChanged: true, startingGuid: startingGuid);
                    bmwMakePreview.RunWorkerStarting -= eh;
                };
                bmwMakePreview.RunWorkerStarting += eh;
            }
            else
            {
                //addOrReloadItem(fullPath, thumbnailChanged: null);
                addOrReloadItem(fullPath, thumbnailChanged: true, startingGuid: startingGuid);
            }
        }

        // 書き込み中の大きなファイルでは Changed が何度も届くので、同じパスは 300ms まとめる（K21）
        private readonly HashSet<string> fileSystemWatcher_ChangedPending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private async void fileSystemWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (isCacheFolder(e.FullPath)) return;
            if (!fileSystemWatcher_ChangedPending.Add(e.FullPath)) return;
            try
            {
                await Task.Delay(300);
            }
            finally
            {
                fileSystemWatcher_ChangedPending.Remove(e.FullPath);
            }
            fileSystemWatcher_Created(sender, e);
        }

        private void fileSystemWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            FileSystemEventArgs alt = e;
            if (fileSystemWatcherStopper(ref alt)) return;
            var startingGuid = loadingGuid;

            // https://connect.microsoft.com/VisualStudio/feedback/details/520436/renamedeventargs-oldfullpath-throws-pathtoolongexception の問題に対処
            string oldFullPath;
            try { oldFullPath = e.OldFullPath; } catch (PathTooLongException) { return; }

            GPSizeThumbnail.TryMove(thumbnailCache, oldFullPath, e.FullPath);
            if (GPSizeThumbnail.Editing(e.FullPath)) return;

            if (CreatingZipPathArray)
            {
                BackgroundMultiWorker.RunWorkerStartingEventHandler eh = null;
                eh = (s2, e2) =>
                {
                    if (!fileSystemWatcherStopper(ref alt)) renameItem(e.FullPath, oldFullPath, startingGuid);
                    bmwMakePreview.RunWorkerStarting -= eh;
                };
                bmwMakePreview.RunWorkerStarting += eh;
            }
            else
            {
                renameItem(e.FullPath, oldFullPath, startingGuid);
            }
        }

        private int QuickReloadProcessingCount = 0;

        private void enterQuickReload()
        {
            QuickReloadProcessingCount++;
        }

        private void exitQuickReload(Guid startingGuid, bool coalesce = false)
        {
            QuickReloadProcessingCount--;
            if (QuickReloadProcessingCount <= 0 && loadingGuid == startingGuid)
            {
                // 削除のイベントが続けて届くと、1 件ごとに全件の並べ替えと一覧の作り直しをしていた
                // （3 万件のフォルダから 300 ファイルを消すと 1 件 0.4 秒 × 300 回で 2 分）。
                // 削除では、前回の並べ替えから間がないときは、重い一覧（DataGridView）の作り直しと並べ替えを少し待ってまとめて 1 回にする。
                // 単発の削除は従来どおりすぐ（K2）。計測では一覧の作り直しが 1 回 125〜318 ms、並べ替えが 28 ms
                // 追加はサムネイルの作成を待つ間に自然にまとまるので従来どおり（まとめると応答がかえって遅れた）
                var elapsed = unchecked(Environment.TickCount - exitQuickReload_LastSortTick);
                if (coalesce && 0 <= elapsed && elapsed < QuickReloadCoalesceMilliseconds)
                {
                    // サムネイル一覧の表示順からは消えた項目をすぐに除く（残りの順番は変わらないので並べ替えは要らない）。
                    // 除かずに後回しにすると、消えた項目を描こうとしてサムネイル一覧が描画中に例外を出す
                    lock (loadingPackedImageLoaderLocker)
                    {
                        var s2d = tvCatalog.ShowIndexToDataIndex;
                        var zpa = ZipPathArray;
                        var zma = ZipMaskArray;
                        if (s2d != null && zpa != null && zma != null && zpa.Length == zma.Length)
                        {
                            var kept = Array.FindAll(s2d, i => i < 0 || i >= zpa.Length || zpa[i] != null && zma[i]);
                            if (kept.Length != s2d.Length)
                            {
                                var sp = tvCatalog.ScrollBarPercentage;
                                tvCatalog.ShowIndexToDataIndex = kept;
                                tvCatalog.ScrollBarPercentage = sp;
                            }
                        }
                    }
                    exitQuickReload_PendingGuid = startingGuid;
                    exitQuickReload_Pending = true;
                    if (exitQuickReload_Timer == null)
                    {
                        exitQuickReload_Timer = new System.Windows.Forms.Timer { Interval = QuickReloadDelayMilliseconds };
                        exitQuickReload_Timer.Tick += exitQuickReload_Timer_Tick;
                    }
                    if (!exitQuickReload_Timer.Enabled) exitQuickReload_Timer.Start();
                    return;
                }
                SortForQuickReload();
            }
        }

        private const int QuickReloadCoalesceMilliseconds = 300;

        private const int QuickReloadDelayMilliseconds = 200;

        private int exitQuickReload_LastSortTick = Environment.TickCount - 1000000;

        private bool exitQuickReload_Pending = false;

        private Guid exitQuickReload_PendingGuid;

        private System.Windows.Forms.Timer exitQuickReload_Timer;

        private void exitQuickReload_Timer_Tick(object sender, EventArgs e)
        {
            exitQuickReload_Timer.Stop();
            if (!exitQuickReload_Pending) return;
            if (loadingGuid != exitQuickReload_PendingGuid)
            {
                exitQuickReload_Pending = false; // 別のフォルダに移った
                return;
            }
            if (QuickReloadProcessingCount > 0) return; // 処理中のものが終わったとき（exitQuickReload）にもう一度判断する
            SortForQuickReload();
            SelectUserAddedFiles(); // まとめた並べ替えのあとに、ユーザーが追加したファイルを選択する
        }

        private void SortForQuickReload()
        {
            exitQuickReload_Pending = false;
            exitQuickReload_Timer?.Stop();
            lock (loadingPackedImageLoaderLocker)
            {
#if PERFLOG
                var perfT0 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                var sp = tvCatalog.ScrollBarPercentage;
                tvCatalog.ShowIndexToDataIndex = GetSortArray(preSortArray: tvCatalog.ShowIndexToDataIndex);
                tvCatalog.ScrollBarPercentage = sp;
#if PERFLOG
                var perfT1 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                setFileListFromThumbnailViewer(tryToKeepScroll: true);
#if PERFLOG
                var perfT2 = System.Diagnostics.Stopwatch.GetTimestamp();
                double perfF = System.Diagnostics.Stopwatch.Frequency;
                try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "quick-reload.log"), $"sort={(perfT1 - perfT0) * 1000 / perfF:F1}ms\tfileList={(perfT2 - perfT1) * 1000 / perfF:F1}ms" + Environment.NewLine); } catch { }
#endif
            }
            exitQuickReload_LastSortTick = Environment.TickCount; // 並べ替えが終わった時刻から数える
        }

        private void removeItem(string path)
        {
            if (CreatingZipPathArray) return;
            var zpa = ZipPathArray;
            var zma = ZipMaskArray;
            if (zpa != null && zma != null && zpa.Length == zma.Length && zpa.Length == tvCatalog.Count)
            {
                var idx = indexOfWatchedPath(zpa, path);
                if (idx >= 0)
                {
                    enterQuickReload();
                    zma[idx] = false;
                    zpa[idx] = null;
                    var item = tvCatalog[idx];
                    if (item != null)
                    {
                        if (idx != tvCatalog.FocusedIndex)
                        {
                            item.Dispose();
                            tvCatalog[idx] = null;
                        }
                        else
                        {
                            var d2s = tvCatalog.DataIndexToShowIndex;
                            var s2d = tvCatalog.ShowIndexToDataIndex;
                            item.Dispose();
                            tvCatalog[idx] = null;
                            if (idx < d2s.Length)
                            {
                                var sIdx = Math.Min(s2d.Length - 2, d2s[idx]);
                                if (sIdx >= 0)
                                {
                                    tvCatalog.FocusedIndex = s2d[sIdx + 1];
                                }
                            }
                        }
                    }
                    exitQuickReload(loadingGuid, coalesce: true);
                }
            }
        }

        private static bool hasSameType(string pathA, string pathB)
        {
            try
            {
                return Path.GetExtension(pathA).ToLower() == Path.GetExtension(pathB).ToLower();
            }
            catch
            {
                return false;
            }
        }

        private void renameItem(string newPath, string oldPath, Guid startingGuid)
        {
            if (newPath == null || oldPath == null) return;
            if (ShouldBeSkippedForFileAndDirWithCheckAttributes(newPath, ffmpeExists: null))
            {
                removeItem(oldPath);
                return;
            }
            if (CreatingZipPathArray) return;
            var zpa = ZipPathArray;
            var zma = ZipMaskArray;
            var zna = ZipNameArray;
            var lra = LoadResultArray;
            var mta = ModifiedTimeArray;
            var d2s = tvCatalog.DataIndexToShowIndex;
            if (zpa != null && zma != null && zna != null && mta != null &&
                zpa.Length == zma.Length && zpa.Length == zna.Length && zpa.Length == mta.Length &&
                zpa.Length == tvCatalog.Count && d2s != null && d2s.Length == zpa.Length
                && lra != null && lra.Length == zpa.Length)
            {
                var idx = indexOfWatchedPath(zpa, oldPath);
                var needNotToReload = idx >= 0;
                bool? thumbnailChanged = null;
                if (needNotToReload)
                {
                    if (!hasSameType(oldPath, newPath))
                    {
                        needNotToReload = false;
                    }
                    else
                    {
                        var oldInfo = new ZipPlaInfo(oldPath);
                        var newInfo = new ZipPlaInfo(newPath);
                        needNotToReload = oldInfo.ThumbnailInfo == newInfo.ThumbnailInfo;
                    }
                    thumbnailChanged = !needNotToReload;
                    needNotToReload = needNotToReload && lra[idx] == LoadResult.Success;
                }
                if (needNotToReload)
                {
                    zma[idx] = true;
                    zpa[idx] = newPath;
                    var name = getRelativePath(newPath, zna[idx]);
                    zna[idx] = name;
                    //enterQuickReload();
                    //await Task.Run(() => ReloadOneThumbnailForSubThread(idx, newPath, newPath, silent: true));

                    swapPathAndNameInSorBookmarkUndoBuff(oldPath, newPath, needToSaveBookmark: true);

                    var item = tvCatalog[idx];
                    item.SilentSet(name);
                    item.DrawItem(onlyFrame: false);
                    var showIndex = d2s[idx];
                    if (showIndex >= 0 && showIndex < dgvFileList.Rows.Count)
                    {
                        try
                        {
                            var info = getFileListRowInfoArray(zpa)[idx];
                            if (info != null) setFileListRowStaticGlobalPart(info, newPath, name, mta[idx]);
                            dgvFileList.InvalidateRow(showIndex);
                        }
                        catch (Exception e)
                        {
#if DEBUG
                            MessageBox.Show(this, e.ToString(), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                            MessageBox.Show(this, e.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
                            return;
                        }
                        ShowStatusBar();
                    }
                    //exitQuickReload();
                }
                else if (Array.IndexOf(zpa, newPath) < 0) // ZipPla 自身がリネームを行った場合既に更新されている
                {
                    addOrReloadItem(newPath, zpa, zma, lra, idx, thumbnailChanged, startingGuid);
                }
            }
        }

        //public static readonly HashSet<string> addOrReloadItem_OneTimeBlackList = new HashSet<string>(); // スレッドセーフ
        //volatile private bool addOrReloadItem_Excuting = false;
        private void addOrReloadItem(string path, bool? thumbnailChanged, Guid startingGuid)
        {
            if (GPSizeThumbnail.Editing(path))
            {
                return;
            }

            //MessageBox.Show($"getzpa");
            //Application.DoEvents(); // fileSystemWatcher のイベントのスレッドの特殊な振る舞いに対処
            //while (addOrReloadItem_Excuting) Application.DoEvents();
            //addOrReloadItem_Excuting = true;
            if (CreatingZipPathArray) return;
            var zpa = ZipPathArray;
            if (zpa == null) return;
            var count = zpa.Length;

            var zma = ZipMaskArray;
            if (zma == null || zma.Length != count) return;

            var lra = LoadResultArray;
            if (lra == null || lra.Length != count) return;

            if (ShouldBeSkippedForFileAndDirWithCheckAttributes(path, ffmpeExists: null)) return;

            var idx = indexOfWatchedPath(zpa, path);
            addOrReloadItem(path, zpa, zma, lra, idx, thumbnailChanged, startingGuid);
        }

        /// <summary>
        /// 監視イベントのパスを一覧から探す。まず完全一致（大文字小文字だけが違う 2 つの項目がある場合に備える）、
        /// なければ大文字小文字を区別せずに探す（イベントのパスと列挙したパスで大文字小文字が違うことがある、B5）
        /// </summary>
        private static int indexOfWatchedPath(string[] zpa, string path)
        {
            if (path == null) return -1;
            // 完全に一致するものを優先し、なければ大文字小文字を無視して最初に一致したもの。
            // 以前は Array.IndexOf と大文字小文字を無視した比較で一覧を 2 回なめていた（新しいファイルは必ず両方とも最後まで見る）。1 回にまとめる
            var length = path.Length;
            var ignoreCaseIndex = -1;
            for (var i = 0; i < zpa.Length; i++)
            {
                var p = zpa[i];
                if (p == null || p.Length != length || !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(p, path, StringComparison.Ordinal)) return i;
                if (ignoreCaseIndex < 0) ignoreCaseIndex = i;
            }
            return ignoreCaseIndex;
        }

        private HashSet<string> addOrReloadItem_UserAddFileInLower = null;

        private async void addOrReloadItem(string path, string[] zpa, bool[] zma, LoadResult[] lra, int idx, bool? thumbnailChanged, Guid startingGuid)
        {
            var count = zpa.Length;
            if (idx >= 0) // reload
            {
                var oldPath = zpa[idx];
                bool tc;
                if (thumbnailChanged != null) tc = (bool)thumbnailChanged;
                else if (oldPath != null)
                {
                    var oldInfo = new ZipPlaInfo(oldPath);
                    var newInfo = new ZipPlaInfo(path);
                    tc = oldInfo.ThumbnailInfo != newInfo.ThumbnailInfo;
                }
                else
                {
                    tc = false;
                }
                var silentMode = tc ? SilentMode.Never : lra[idx] == LoadResult.Success ? SilentMode.Always : SilentMode.OnlyIfNotSuccess;
                zma[idx] = true;
                zpa[idx] = path;
                //enterQuickReload();
                await Task.Run(() => ReloadOneThumbnailForSubThread(idx, path, startingGuid, path, silentMode));
                //exitQuickReload();
                return;
            }
            else // add
            {
                //MessageBox.Show($"{InvokeRequired}, {count}");
                const SilentMode silentModeForAdd = SilentMode.Never;

                for (var i = 0; i < count; i++)
                {
                    if (!zma[i])
                    {
                        zma[i] = true;
                        zpa[i] = path;

                        addOrReloadItem_Add(i, path, silentModeForAdd);
                        return;

                    }
                }

                // 配列の複製から差し替えまでをロックの中で行う。ロックの外で複製すると、その間に別の項目の ReloadOneThumbnailForSubThread が
                // 古い配列へ書いた名前などが失われ、名前が null の項目は一覧から外れる（K21 で 1 件の処理が速くなって起きるようになった）
                Monitor.Enter(ZipPathArrayLocker);
                try
                {
                var zipPathArray = zpa;
                var zipNameArray = ZipNameArray; if (zipNameArray == null || zipNameArray.Length != count) return;
                var zipPageArray = ZipPageArray; if (zipPageArray == null || zipPageArray.Length != count) return;
                var createTimeArray = CreateTimeArray; if (createTimeArray == null || createTimeArray.Length != count) return;
                var lastAccessTimeArray = LastAccessTimeArray; if (lastAccessTimeArray == null || lastAccessTimeArray.Length != count) return;
                var modifiedTimeArray = ModifiedTimeArray; if (modifiedTimeArray == null || modifiedTimeArray.Length != count) return;
                var fileCountArray = FileCountArray; if (fileCountArray == null || fileCountArray.Length != count) return;
                var imageInfoArray = ImageInfoArray; if (imageInfoArray == null || imageInfoArray.Length != count) return;
                var movieInfoArray = MovieInfoArray; if (movieInfoArray == null || movieInfoArray.Length != count) return;
                var loadResultArray = LoadResultArray; if (loadResultArray == null || loadResultArray.Length != count) return;
                var zipSizeArray = ZipSizeArray; if (zipSizeArray == null || zipSizeArray.Length != count) return;
                var zipMaskArray = zma;

                var count21 = Math.Max(count * 2 + 1, count + (addOrReloadItem_UserAddFileInLower?.Count ?? 0));
                //var count21 = count * 2 + 1;

                var zipPathArray2 = new string[count21]; Array.Copy(zipPathArray, zipPathArray2, count);
                var zipNameArray2 = new string[count21]; Array.Copy(zipNameArray, zipNameArray2, count);
                var zipPageArray2 = new int[count21]; Array.Copy(zipPageArray, zipPageArray2, count);
                var createTimeArray2 = new DateTime[count21]; Array.Copy(createTimeArray, createTimeArray2, count);
                var lastAccessTimeArray2 = new DateTime[count21]; Array.Copy(lastAccessTimeArray, lastAccessTimeArray2, count);
                var modifiedTimeArray2 = new DateTime[count21]; Array.Copy(modifiedTimeArray, modifiedTimeArray2, count);
                var fileCountArray2 = new int[count21]; Array.Copy(fileCountArray, fileCountArray2, count);
                var imageInfoArray2 = new ImageInfo[count21]; Array.Copy(imageInfoArray, imageInfoArray2, count);
                var movieInfoArray2 = new MovieInfo[count21]; Array.Copy(movieInfoArray, movieInfoArray2, count);
                var loadResultArray2 = new LoadResult[count21]; Array.Copy(loadResultArray, loadResultArray2, count);
                var zipSizeArray2 = new long[count21]; Array.Copy(zipSizeArray, zipSizeArray2, count);
                var zipMaskArray2 = new bool[count21]; Array.Copy(zipMaskArray, zipMaskArray2, count);

                for (var i = count; i < count21; i++)
                {
                    zipPageArray2[i] = -1;
                }

                {
                    ZipPathArray = zipPathArray2;
                    ZipNameArray = zipNameArray2;
                    ZipPageArray = zipPageArray2;
                    CreateTimeArray = createTimeArray2;
                    LastAccessTimeArray = lastAccessTimeArray2;
                    ModifiedTimeArray = modifiedTimeArray2;
                    FileCountArray = fileCountArray2;
                    ImageInfoArray = imageInfoArray2;
                    MovieInfoArray = movieInfoArray2;
                    LoadResultArray = loadResultArray2;
                    ZipSizeArray = zipSizeArray2;
                    ZipMaskArray = zipMaskArray2;
                }
                ZipMaskArray[count] = true;
                ZipPathArray[count] = path;
                //MessageBox.Show($"{ZipPathArray.Length}");
                tvCatalog.ItemBuffSizeChange(count21);
                }
                finally
                {
                    Monitor.Exit(ZipPathArrayLocker);
                }

                addOrReloadItem_Add(count, path, silentModeForAdd);
            }
        }

        private async void addOrReloadItem_Add(int index, string path, SilentMode silentModeForAdd)
        {
            enterQuickReload();
            //var selectedCount = addOrReloadItem_UserAddFileInLower != null ? tvCatalog.SelectedIndicesCount : 0;
            var startingGuid = loadingGuid;

            // Add が呼ばれるということはそれ以前はそのパスのファイルが存在しなかったとういこと
            // 従ってファイルの追従に失敗していなければ履歴にそのパスは存在しない
            // 失敗している場合は残っているが、そのケースでだけ情報が復元される仕様は不適切
            const bool reloadPage = false;

            await Task.Run(() =>
            {
                ReloadOneThumbnailForSubThread(index, path, startingGuid, path, silentModeForAdd, reloadPage);
            });
            exitQuickReload(startingGuid);
            if (startingGuid != loadingGuid) return;
            // 並べ替えをまとめるために後回しにした場合は、その並べ替えのあとで選択する（K2）
            if (!exitQuickReload_Pending) SelectUserAddedFiles();
        }

        // ユーザーが追加（貼り付けなど）したファイルが一覧に現れたら選択する
        private void SelectUserAddedFiles()
        {
            if (QuickReloadProcessingCount <= 0 && addOrReloadItem_UserAddFileInLower != null)
            {
                if (CreatingZipPathArray) return;
                var s2d = tvCatalog.ShowIndexToDataIndex;
                if (s2d == null || s2d.Length <= 0) return;
                var zpa = ZipPathArray;
                if (zpa == null || zpa.Length <= 0) return;

                var count = zpa.Length;
                var path2indexPair = new Dictionary<string, Tuple<int, int>>();
                for (var i = 0; i < s2d.Length; i++)
                {
                    var si = s2d[i];
                    if (si >= 0 && si < count)
                    {
                        var p = zpa[si];
                        if (p != null) path2indexPair[p] = Tuple.Create(si, i);
                    }
                }
                
                var currentSelected = tvCatalog.SelectedIndices;
                HashSet<int> selectedIndices;
                if (currentSelected != null && currentSelected.Length > 0)
                {
                    selectedIndices = new HashSet<int>(currentSelected);
                }
                else
                {
                    selectedIndices = new HashSet<int>();
                }
                
                var deleteList = new List<string>();
                var topShowIndex = selectedIndices.Count == 0 ? int.MaxValue : -1;
                foreach (var p in addOrReloadItem_UserAddFileInLower)
                {
                    Tuple<int, int> i;
                    if (path2indexPair.TryGetValue(p, out i))
                    {
                        var si = i.Item1;
                        deleteList.Add(p);
                        selectedIndices.Add(si);
                        if (topShowIndex > i.Item2)
                        {
                            topShowIndex = i.Item2;
                        }
                    }
                }
                foreach (var d in deleteList) addOrReloadItem_UserAddFileInLower.Remove(d);
                if (addOrReloadItem_UserAddFileInLower.Count == 0) addOrReloadItem_UserAddFileInLower = null;

                tvCatalog.SelectedIndices = selectedIndices.ToArray();
                if (0 <= topShowIndex && topShowIndex < int.MaxValue)
                {
                    tvCatalog.ScrollBarToIndexWithMinimalMove(showIndex: topShowIndex);
                }
            }
        }

        private void addToUserAddFileList(IEnumerable<string> paths)
        {
            if (paths == null || !paths.Any()) return;
            tvCatalog.SelectedIndex = -1;
            if (addOrReloadItem_UserAddFileInLower == null)
            {
                addOrReloadItem_UserAddFileInLower = new HashSet<string>(from path in paths where path != null select path/*.ToLower()*/);
                if (addOrReloadItem_UserAddFileInLower.Count == 0) addOrReloadItem_UserAddFileInLower = null;
            }
            else
            {
                foreach (var path in paths)
                {
                    if (path != null)
                    {
                        addOrReloadItem_UserAddFileInLower.Add(path/*.ToLower()*/);
                    }
                }
            }
        }

        private void setFileListDockForUser(DockStyle dock)
        {
            if (!thumbnailListToolStripMenuItem.Checked) thumbnailListToolStripMenuItem.Checked = true;
            if (!fileListToolStripMenuItem.Checked) fileListToolStripMenuItem.Checked = true;
            setFileListDock(dock, keepWidth: true, fixWidth: true);
        }
    }
}
