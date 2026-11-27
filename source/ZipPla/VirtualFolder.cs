using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZipPla
{
    public static class VirtualFolder
    {

        public static bool DirectoryOrBookmarkExists(string path)
        {
            // エラー OK とする
            return Path.GetExtension(path).ToUpper() == ".SOR" && File.Exists(path) || Directory.Exists(path);
        }

        public static bool IsBookmarkPath(string path)
        {
            try
            {
                // 削除コマンドが実行可能なものを追加する場合 CatalogForm の該当箇所を変更すること
                return Path.GetExtension(path).ToUpper() == ".SOR";
            }
            catch
            {
                return false;
            }
        }

        private static bool EndsWithDirectorySeparatorChar(string path)
        {
            return !string.IsNullOrEmpty(path) && path.Last() == Path.DirectorySeparatorChar;
        }

        public static int GetPageFromBookmark(string path, bool isDir, string bmkPath)
        {
            var pages = GetBookmarkPageDictionaries(bmkPath);
            var dict = isDir ? pages.Folders : pages.Files;
            return path != null && dict.TryGetValue(path, out var page) ? page : -1;
        }

        public static void GetPageFromBookmark(out int[] filePages, out int[] folderPages, string[] filePaths, string[] folderPaths, string bmkPath)
        {
            // 以前はフォルダを開くたびに履歴ファイル全体を解析し、両方をカルチャ依存の比較で並べ替えて突き合わせていた（K10）。
            // 解析結果をファイルの更新日時とサイズをキーに使い回し、パスの辞書（序数比較）で引く
            var pages = GetBookmarkPageDictionaries(bmkPath);
            filePages = LookUpPages(filePaths, pages.Files);
            folderPages = LookUpPages(folderPaths, pages.Folders);
        }

        private static int[] LookUpPages(string[] paths, Dictionary<string, int> dict)
        {
            var result = new int[paths.Length];
            for (var i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                result[i] = path != null && dict.TryGetValue(path, out var page) ? page : -1;
            }
            return result;
        }

        private sealed class BookmarkPages
        {
            public Dictionary<string, int> Files, Folders;
        }
        private static readonly object bookmarkPagesCacheLocker = new object();
        private static string bookmarkPagesCachePath;
        private static DateTime bookmarkPagesCacheTime;
        private static long bookmarkPagesCacheLength = -1;
        private static BookmarkPages bookmarkPagesCache;

        // 同じパスが複数あれば先のもの（以前の突き合わせは安定ソートで先のものを使っていた）
        private static BookmarkPages GetBookmarkPageDictionaries(string bmkPath)
        {
            var info = new FileInfo(bmkPath);
            if (!info.Exists) throw new FileNotFoundException(null, bmkPath);
            var time = info.LastWriteTimeUtc;
            var length = info.Length;
            lock (bookmarkPagesCacheLocker)
            {
                if (bookmarkPagesCache != null && bookmarkPagesCacheLength == length && bookmarkPagesCacheTime == time &&
                    string.Equals(bookmarkPagesCachePath, bmkPath, StringComparison.OrdinalIgnoreCase))
                {
                    return bookmarkPagesCache;
                }
            }
            GetBookmarkFiles(out var bmkFilePaths, out var bmkFilePages, out var bmkFolderPaths, out var bmkFolderPages, bmkPath);
            var result = new BookmarkPages { Files = ToPageDictionary(bmkFilePaths, bmkFilePages), Folders = ToPageDictionary(bmkFolderPaths, bmkFolderPages) };
            lock (bookmarkPagesCacheLocker)
            {
                bookmarkPagesCache = result;
                bookmarkPagesCachePath = bmkPath;
                bookmarkPagesCacheTime = time;
                bookmarkPagesCacheLength = length;
            }
            return result;
        }

        private static Dictionary<string, int> ToPageDictionary(string[] paths, int[] pages)
        {
            var dict = new Dictionary<string, int>(paths.Length, StringComparer.Ordinal);
            for (var i = 0; i < paths.Length; i++)
            {
                if (!dict.ContainsKey(paths[i])) dict.Add(paths[i], pages[i]);
            }
            return dict;
        }

        // 名前の変更に合わせた .sor の書き換えを、UI スレッドではなく背景で直列に行う（K23）。
        // 同じプロセスでその .sor を読み書きするときは、たまっている書き換えを先に済ませる（WaitPendingReplacements）
        private static readonly object pendingReplaceLock = new object();
        private static readonly Dictionary<string, Tuple<List<string>, List<string>>> pendingReplacements = new Dictionary<string, Tuple<List<string>, List<string>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> replacingNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // 書き換えの途中
        private static Task pendingReplaceTask = Task.CompletedTask;
        [ThreadStatic] private static bool inReplaceWorker;

        /// <summary>ReplaceBookmarkData を背景で行う（K23）。同じ .sor への書き換えは順にまとめて 1 回で書く</summary>
        public static void ReplaceBookmarkDataInBackground(string bmkPath, IReadOnlyList<string> paths, IReadOnlyList<string> replacePaths)
        {
            if (string.IsNullOrEmpty(bmkPath) || paths == null || replacePaths == null || paths.Count != replacePaths.Count || paths.Count == 0) return;
            lock (pendingReplaceLock)
            {
                if (!pendingReplacements.TryGetValue(bmkPath, out var pair))
                {
                    pair = Tuple.Create(new List<string>(), new List<string>());
                    pendingReplacements.Add(bmkPath, pair);
                    var target = bmkPath;
                    pendingReplaceTask = pendingReplaceTask.ContinueWith(_ => runPendingReplacement(target), TaskScheduler.Default);
                }
                pair.Item1.AddRange(paths);
                pair.Item2.AddRange(replacePaths);
            }
        }

        // .sor ごとの書き換えのロック（Y14）。背景のタスクと、WaitPendingReplacements で先に書き換える呼び出し側が同じ .sor を同時に書かないように
        private static readonly Dictionary<string, object> replaceLocks = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static object getReplaceLock(string bmkPath)
        {
            lock (pendingReplaceLock)
            {
                if (!replaceLocks.TryGetValue(bmkPath, out var l)) replaceLocks.Add(bmkPath, l = new object());
                return l;
            }
        }

        private static void runPendingReplacement(string bmkPath) => runPendingReplacement(bmkPath, Timeout.Infinite);

        // その .sor にたまっている書き換えを行う。ほかのスレッドが書き換えの途中なら、終わるまで（最大 millisecondsTimeout）待つ
        private static void runPendingReplacement(string bmkPath, int millisecondsTimeout)
        {
            var replaceLock = getReplaceLock(bmkPath);
            var lockTaken = false;
            try
            {
                Monitor.TryEnter(replaceLock, millisecondsTimeout, ref lockTaken);
                if (!lockTaken) return;
                Tuple<List<string>, List<string>> pair;
                lock (pendingReplaceLock)
                {
                    if (!pendingReplacements.TryGetValue(bmkPath, out pair)) return;
                    pendingReplacements.Remove(bmkPath);
                    replacingNow.Add(bmkPath);
                }
                var wasInWorker = inReplaceWorker;
                inReplaceWorker = true;
                try
                {
                    // 同じ名前が続けて変わった（A→B→C）場合も順に置き換わるように、追加した順に 1 組ずつではなく、まとめて 1 回で書く。
                    // ReplaceBookmarkData は組ごとに順に適用する
                    ReplaceBookmarkData(bmkPath, pair.Item1, pair.Item2);
                }
                catch { }
                finally
                {
                    inReplaceWorker = wasInWorker;
                    lock (pendingReplaceLock) replacingNow.Remove(bmkPath);
                }
            }
            finally
            {
                if (lockTaken) Monitor.Exit(replaceLock);
            }
        }

        /// <summary>
        /// その .sor（null ならすべて）への背景の書き換えが終わるまで待つ（K23）。
        /// .sor を指定したときは、ほかの .sor の書き換えを待たず、たまっていればこのスレッドで先に書き換える（Y14。以前は背景の
        /// 書き換えの列の最後まで、つまりほかのすべての .sor の書き換えを最大 10 秒待っていた）
        /// </summary>
        public static void WaitPendingReplacements(string bmkPath = null, int millisecondsTimeout = 10000)
        {
            if (inReplaceWorker) return;
            Task task;
            lock (pendingReplaceLock)
            {
                if (bmkPath != null && !pendingReplacements.ContainsKey(bmkPath) && !replacingNow.Contains(bmkPath)) return;
                if (bmkPath == null && pendingReplacements.Count == 0 && replacingNow.Count == 0) return;
                task = pendingReplaceTask;
            }
            if (bmkPath != null)
            {
                runPendingReplacement(bmkPath, millisecondsTimeout);
                return;
            }
            try { task.Wait(millisecondsTimeout); } catch { }
        }

        public static void GetBookmarkFiles(out string[] filePaths, out int[] filePages, out string[] folderPaths, out int[] folderPages, string bmkPath)
        {
            WaitPendingReplacements(bmkPath); // 背景の書き換えが済んでから読む（K23）
            var filePathList = new List<string>();
            var filePageList = new List<int>();
            var folderPathList = new List<string>();
            var folderPageList = new List<int>();

            if (!File.Exists(bmkPath)) throw new FileNotFoundException(null, bmkPath);
            using (var r = new StreamReader(bmkPath))
            {
                string path = null;
                int page = -1;
                for(var line = r.ReadLine(); true; line = r.ReadLine())
                {
                    if(line == "[Item]" || line == null)
                    {
                        if(path != null)
                        {
                            if(EndsWithDirectorySeparatorChar(path))

                            {
                                folderPathList.Add(path.Substring(0, path.Length - 1));
                                folderPageList.Add(page);
                            }
                            else
                            {
                                filePathList.Add(path);
                                filePageList.Add(page);
                            }
                        }
                        if (line != null)
                        {
                            path = null;
                            page = -1;
                            continue;
                        }
                        else break;
                    }
                    var separatorPosition = line.IndexOf('=');
                    if (separatorPosition < 0) continue;
                    var value = line.Substring(separatorPosition + 1);
                    switch(line.Substring(0,separatorPosition))
                    {
                        case "Path": if (path == null) path = value; break;
                        case "Page": if (page < 0) if (!int.TryParse(value, out page)) page = -1; break;
                    }
                }
            }

            filePaths = filePathList.ToArray();
            filePages = filePageList.ToArray();
            folderPaths = folderPathList.ToArray();
            folderPages = folderPageList.ToArray();
        }

        public static void AddBookmarkData(string bmkPath, string path, int page, int limitOfItemsCount = int.MaxValue, bool deleteLostPath = false)
        {
            AddBookmarkData(bmkPath, new string[] { path }, new int[] { page }, limitOfItemsCount, deleteLostPath);
        }
        public static int AddBookmarkData(string bmkPath, IReadOnlyList<string> paths, IReadOnlyList<int> pages, int limitOfItemsCount = int.MaxValue, bool deleteLostPath = false)
        {
            AddDeleteBookmarkData(bmkPath, paths, pages, limitOfItemsCount, out var dummy, deleteLostPath, false,
            replacePaths: null, firstMatchedPage: out var firstMatchedPage);
            return firstMatchedPage;
        }

        public static void DeleteBookmarkData(string bmkPath, IReadOnlyList<string> paths)
        {
            AddDeleteBookmarkData(bmkPath, paths, null, limitOfItemsCount: int.MaxValue, trimmed: out var dummy,
                deleteLostPath: false, deleteOrReplace: true, replacePaths: null, firstMatchedPage: out var dummy2);
        }

        public static bool TrimBookmarkData(string bmkPath, int limitOfItemsCount)
        {
            AddDeleteBookmarkData(bmkPath, new string[0], null, limitOfItemsCount, out var trimmed, deleteLostPath: true,
                deleteOrReplace: false, replacePaths: null, firstMatchedPage: out var dummy);
            return trimmed;
        }

        /// <summary>
        /// VirtualFolder の要素を置換します。path と replacePats について、ファイルかどうかが一致しなければなりません。
        /// </summary>
        /// <param name="bmkPath"></param>
        /// <param name="path"></param>
        /// <param name="replacePath"></param>
        public static void ReplaceBookmarkData(string bmkPath, string path, string replacePath)
        {
            ReplaceBookmarkData(bmkPath, new string[1] { path }, new string[1] { replacePath });
        }

        /// <summary>
        /// VirtualFolder の要素を置換します。paths と replacePaths の同じインデックス毎、ファイルかどうかが一致しなければなりません。
        /// </summary>
        /// <param name="bmkPath"></param>
        /// <param name="paths"></param>
        /// <param name="replacePaths"></param>
        public static void ReplaceBookmarkData(string bmkPath, IReadOnlyList<string> paths, IReadOnlyList<string> replacePaths)
        {
            AddDeleteBookmarkData(bmkPath, paths, null, limitOfItemsCount: int.MaxValue, trimmed: out var dummy,
                deleteLostPath: false, deleteOrReplace: true, replacePaths: replacePaths, firstMatchedPage: out var dummy2);
        }

        private static bool EntityFileExists(string path)
        {
            var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
            return File.Exists(altPos < 0 ? path : path.Substring(0, altPos));
        }

        private static bool ContainsIgnoreCase(List<string> list, string path)
        {
            foreach (var item in list)
            {
                if (string.Compare(path, item, ignoreCase: true) == 0) return true;
            }
            return false;
        }

        private static void AddDeleteBookmarkData(string bmkPath, IReadOnlyList<string> paths, IReadOnlyList<int> pages, int limitOfItemsCount, out bool trimmed, bool deleteLostPath,
            bool deleteOrReplace, IReadOnlyList<string> replacePaths, out int firstMatchedPage)
        {
            var edited = false;
            firstMatchedPage = -1;

            var filePathList = new List<string>();
            var filePageList = new List<int>();
            var folderPathList = new List<string>();
            var folderPageList = new List<int>();

            var delete = deleteOrReplace && replacePaths == null;
            var replace = deleteOrReplace && replacePaths != null;

            // 追加・置換・削除するパスは開いたばかり（最終アクセス日時が更新されている）か状態が変わったので、覚えている結果を捨てる（K11）
            ForgetCachedStates(paths);
            if (replacePaths != null) ForgetCachedStates(replacePaths);

            if (File.Exists(bmkPath))
            {
                GetBookmarkFiles(out var filePaths, out var filePages, out var folderPaths, out var folderPages, bmkPath);
                if (deleteLostPath)
                {
                    var fileCount = filePaths.Length;
                    for(var i = 0; i < fileCount; i++)
                    {
                        if (CachedExists(filePaths[i], isDir: false))
                        {
                            filePathList.Add(filePaths[i]);
                            filePageList.Add(filePages[i]);
                        }
                        else edited = true;
                    }
                    var folderCount = folderPaths.Length;
                    for (var i = 0; i < folderCount; i++)
                    {
                        if (CachedExists(folderPaths[i], isDir: true))
                        {
                            folderPathList.Add(folderPaths[i]);
                            folderPageList.Add(folderPages[i]);
                        }
                        else edited = true;
                    }
                }
                else
                {
                    filePathList.AddRange(filePaths);
                    filePageList.AddRange(filePages);
                    folderPathList.AddRange(folderPaths);
                    folderPageList.AddRange(folderPages);
                }
            }
            else
            {
                trimmed = false;
                if (deleteOrReplace) return;

                // なければ作る
                edited = true;
            }

            var newCount = paths.Count;
            var newPathSet = new HashSet<string>();
            for (var i = 0; i < newCount; i++)
            {
                var path = paths[i];
                var replacedPath = replace ? replacePaths[i] : null;
                bool isDir;
                if (!EndsWithDirectorySeparatorChar(path))
                {
                    // 名前変更・削除はファイル操作のあとに呼ばれるので、旧パスのフォルダはもう存在しない。
                    // その場合は記録の側にフォルダとしてだけあるかで決める（X5）
                    if (Directory.Exists(path) ||
                        deleteOrReplace && !File.Exists(path) && ContainsIgnoreCase(folderPathList, path) && !ContainsIgnoreCase(filePathList, path))
                    {
                        path += Path.DirectorySeparatorChar;
                        isDir = true;
                    }
                    else
                    {
                        isDir = false;
                    }
                }
                else
                {
                    isDir = true;
                }
                int page;
                if ((isDir || PackedImageLoader.Supports(replacedPath ?? path)) && pages != null)
                {
                    page = pages[i];
                }
                else
                {
                    page = -1;
                }
                if (isDir)
                {
                    path = path.Substring(0, path.Length - 1);
                    var existingFolderCount = folderPathList.Count;

                    var found = false;
                    for (var j = 0; j < existingFolderCount; j++)
                    {
                        if (string.Compare(path, folderPathList[j], ignoreCase: true) == 0)
                        {
                            if (firstMatchedPage < 0) firstMatchedPage = folderPageList[j];
                            edited = true; // ファイル側と同じく、一致したら書き直す（X5）
                            if (delete || found)
                            {
                                folderPathList.RemoveAt(j);
                                folderPageList.RemoveAt(j);
                                j--;
                                existingFolderCount--;
                            }
                            else if (replace)
                            {
                                folderPathList[j] = replacedPath;
                                if (page >= 0) folderPageList[j] = page;
                                newPathSet.Add(replacedPath);
                            }
                            else
                            {
                                folderPathList[j] = path; // 大文字小文字の補正
                                folderPageList[j] = page;
                                newPathSet.Add(path);
                            }
                            found = true;
                        }
                    }
                    if (!found && !deleteOrReplace)
                    {
                        edited = true;
                        folderPathList.Add(path);
                        folderPageList.Add(page);
                        newPathSet.Add(path);
                    }

                }
                else
                {
                    var existingFileCount = filePathList.Count;
                    var found = false;
                    for (var j = 0; j < existingFileCount; j++)
                    {
                        if (string.Compare(path, filePathList[j], ignoreCase: true) == 0)
                        {
                            if (firstMatchedPage < 0) firstMatchedPage = filePageList[j];
                            edited = true;
                            if (delete || found)
                            {
                                filePathList.RemoveAt(j);
                                filePageList.RemoveAt(j);
                                j--;
                                existingFileCount--;
                            }
                            else if (replace)
                            {
                                filePathList[j] = replacedPath;
                                if (page >= 0) filePageList[j] = page;
                                newPathSet.Add(replacedPath);
                            }
                            else
                            {
                                filePathList[j] = path; // 大文字小文字の補正
                                filePageList[j] = page;
                                newPathSet.Add(path);
                            }
                            found = true;
                        }
                    }
                    if (!found && !deleteOrReplace)
                    {
                        edited = true;
                        filePathList.Add(path);
                        filePageList.Add(page);
                        newPathSet.Add(path);
                    }

                }
            }

            trimmed = filePathList.Count + folderPathList.Count > limitOfItemsCount;
            if (trimmed)
            {
                edited = true;
                var AccessDates = new List<DateTime>();
                AccessDates.AddRange(from path in filePathList select newPathSet.Contains(path) ? DateTime.MaxValue : CachedLastAccessTime(path, isDir: false));//  File.GetLastAccessTime(path));
                AccessDates.AddRange(from path in folderPathList select newPathSet.Contains(path) ? DateTime.MaxValue : CachedLastAccessTime(path, isDir: true));//File.GetLastAccessTime(path));
                while (AccessDates.Count > limitOfItemsCount)
                {
                    var targetAccessDate = DateTime.MaxValue;
                    var targetIndex = -1;
                    var count = AccessDates.Count;
                    for(var i = 0; i < count; i++)
                    {
                        if(AccessDates[i] < targetAccessDate)
                        {
                            targetAccessDate = AccessDates[i];
                            targetIndex = i;
                        }
                    }
                    AccessDates.RemoveAt(targetIndex);
                    // ページのリストからも消す（以前はパスだけ消していたので、それより後ろの項目のページがずれて保存されていた）
                    if(targetIndex < filePathList.Count)
                    {
                        filePathList.RemoveAt(targetIndex);
                        filePageList.RemoveAt(targetIndex);
                    }
                    else
                    {
                        folderPathList.RemoveAt(targetIndex - filePathList.Count);
                        folderPageList.RemoveAt(targetIndex - filePathList.Count);
                    }
                }
            }

            if (edited) SaveBookmarkData(bmkPath, filePathList.ToArray(), filePageList.ToArray(), folderPathList.ToArray(), folderPageList.ToArray());
        }


        // 履歴に追加するたびに全項目の存在確認と（上限に達していれば）最終アクセス日時の取得をしていた（K11）。
        // 履歴が 1 万件でネットワークドライブの項目があると、追加のたびに数秒かかりうる。結果を短い時間だけ覚えておく。
        // 存在しないという結果は覚えない（その項目はすぐ履歴から消える）
        private const int PathStateCacheMilliseconds = 5 * 60 * 1000;
        private sealed class PathState
        {
            public int ExistsTick;
            public bool ExistsKnown;
            public int AccessTick;
            public bool AccessKnown;
            public DateTime LastAccessTime;
        }
        private static readonly Dictionary<string, PathState> pathStateCache = new Dictionary<string, PathState>(StringComparer.OrdinalIgnoreCase);

        private static string PathStateKey(string path, bool isDir) => (isDir ? "D|" : "F|") + path;

        private static bool CachedExists(string path, bool isDir)
        {
            var key = PathStateKey(path, isDir);
            var now = Environment.TickCount;
            lock (pathStateCache)
            {
                if (pathStateCache.TryGetValue(key, out var state) && state.ExistsKnown && unchecked(now - state.ExistsTick) < PathStateCacheMilliseconds) return true;
            }
            var exists = isDir ? Directory.Exists(path) : EntityFileExists(path);
            lock (pathStateCache)
            {
                if (exists)
                {
                    if (!pathStateCache.TryGetValue(key, out var state)) pathStateCache[key] = state = new PathState();
                    state.ExistsKnown = true;
                    state.ExistsTick = now;
                }
                else pathStateCache.Remove(key);
            }
            return exists;
        }

        private static DateTime CachedLastAccessTime(string path, bool isDir)
        {
            var key = PathStateKey(path, isDir);
            var now = Environment.TickCount;
            lock (pathStateCache)
            {
                if (pathStateCache.TryGetValue(key, out var state) && state.AccessKnown && unchecked(now - state.AccessTick) < PathStateCacheMilliseconds) return state.LastAccessTime;
            }
            var time = isDir ? Program.GetLastAccessTimeOfDirectory(path) : Program.GetLastAccessTimeOfFile(path);
            lock (pathStateCache)
            {
                if (!pathStateCache.TryGetValue(key, out var state)) pathStateCache[key] = state = new PathState();
                state.AccessKnown = true;
                state.AccessTick = now;
                state.LastAccessTime = time;
            }
            return time;
        }

        private static void ForgetCachedStates(IReadOnlyList<string> paths)
        {
            lock (pathStateCache)
            {
                if (pathStateCache.Count == 0) return;
                foreach (var path in paths)
                {
                    if (path == null) continue;
                    var p = path.TrimEnd(Path.DirectorySeparatorChar);
                    pathStateCache.Remove(PathStateKey(p, isDir: false));
                    pathStateCache.Remove(PathStateKey(p, isDir: true));
                }
            }
        }

        private static void SaveBookmarkData(string bmkPath, string[] filePaths, int[] filePages, string[] folderPaths, int[] folderPages)
        {
            var fileCount = filePaths == null ? 0 : filePaths.Length;
            var folderCount = folderPaths == null ? 0 : folderPaths.Length;
            using (var w = new StreamWriter(bmkPath, append: false))
            {
                for (var i = 0; i < fileCount; i++)
                {
                    w.WriteLine("[Item]");
                    w.WriteLine($"Path={filePaths[i]}");
                    if(filePages != null && filePages[i] >= 0) w.WriteLine($"Page={filePages[i]}");
                }
                for (var i = 0; i < folderCount; i++)
                {
                    w.WriteLine("[Item]");
                    w.WriteLine($"Path={folderPaths[i]}{Path.DirectorySeparatorChar}");
                    if (folderPages != null && folderPages[i] >= 0) w.WriteLine($"Page={folderPages[i]}");
                }
            }
        }
    }
}
