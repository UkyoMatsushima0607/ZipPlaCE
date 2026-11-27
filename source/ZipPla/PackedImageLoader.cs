using iTextSharp.text.pdf;
using SevenZipExtractor;
using SharpCompress.Archives;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ZipPla
{
    public class PasswordLockedException : Exception { }

    public enum PackedImageLoaderSearchMode { UntilFoundItem, UntilFoundLayer, Full}

    public enum PackedImageLoaderFileInfo { LastWriteTime = 1, Size = 2, All = LastWriteTime | Size }
    public enum PackedImageLoaderOnMemoryMode { None, OnMemory, Releasable }
    class PackedImageLoader : IDisposable
    {
        // 「書庫をメモリに読む」で読む書庫の大きさの上限（MemoryStream の上限の 2GB より少し小さく）（V18）
        private const long OnMemoryLimit = int.MaxValue - 64 * 1024 * 1024;

        //const long SolidSizeBound = 128 * (1 << 20); // 書庫ファイルサイズ自体ではなく読み取りサイズの上限

        public enum PackType { Directory, Zip, ZipM, Rar, Rar5, Cab, Tar, Pdf, SevenZip, Lha, Unknown, Gz } // Gz はアーカイブ機能がないので対応させない
        public PackType Type { get; private set; }
        public readonly bool IsSupportedSingleFile;
        public PackedImageLoaderOnMemoryMode OnMemoryMode => stream is ReleasableStream releasableStream ?
            releasableStream.IsReleased ? PackedImageLoaderOnMemoryMode.OnMemory : PackedImageLoaderOnMemoryMode.Releasable : PackedImageLoaderOnMemoryMode.None;
        public void Release()
        {
            if (stream is ReleasableStream releasableStream && !releasableStream.IsReleased)
            {
                Task.Run((Action)releasableStream.Release);
            }
        }

        public static string GetSupportedArchiveFileFilter()
        {
            if (sevenZipExists)
            {
                return "*.zip;*.rar;*.pdf;*.7z;*.cab;*.tar;*.cbz;*.cbr;*.epub;*.lzh";
            }
            else
            {
                return "*.zip;*.rar;*.pdf;*.7z;*.cab;*.tar;*.cbz;*.cbr;*.epub";
            }
        }

        public static bool Supports(string path, bool forFile = true)
        {
            try
            {
                return path != null && Supports(path, Path.GetExtension(path).ToLower(), forFile);
            }
            catch
            {
                return false;
            }
        }

        private const bool SharpCompressSupportsMultiVolumeZip = false;

        public static bool Supports(string path, string extensionWithPeriodInLowerCase, bool forFile = true)
        {
            switch (extensionWithPeriodInLowerCase)
            {
                case ".zip":
                case ".rar":
                case ".7z":
                case ".cab":
                case ".tar":
                case ".cbz":
                case ".cbr":
                case ".epub":
                    switch (GetPartInfo(path, path.Length - extensionWithPeriodInLowerCase.Length))
                    {
                        case PartInfo.None: return true;
                        case PartInfo.NotFirst: return false;
                        case PartInfo.First: return forFile && supportsMultiVolume(extensionWithPeriodInLowerCase);
                    }
                    return false;

                case ".lzh":
                    return sevenZipExists;

                case ".pdf":
                    return true;

                default:
                    return false;
            }
        }

        private enum PartInfo { None, First, NotFirst }
        private static PartInfo GetPartInfo(string name, int baseNameLength)
        {
            var i = baseNameLength - 1;

            // 数値を取得
            var digit = false;
            var first = false;
            char c;
            while (i >= 4)
            {
                c = name[i];
                if ('0' <= c && c <= '9')
                {
                    if (digit)
                    {
                        if (first) first = c == '0';
                    }
                    else
                    {
                        digit = true;
                        first = c == '1';
                    }
                    i--;
                }
                else break;
            }
            if (!digit || i < 4) return PartInfo.None;

            c = name[i - 0]; if (c != 't' && c != 'T') return PartInfo.None;
            c = name[i - 1]; if (c != 'r' && c != 'R') return PartInfo.None;
            c = name[i - 2]; if (c != 'a' && c != 'A') return PartInfo.None;
            c = name[i - 3]; if (c != 'p' && c != 'P') return PartInfo.None;
            c = name[i - 4]; if (c != '.') return PartInfo.None;

            return first ? PartInfo.First : PartInfo.NotFirst;
        }

        private static bool supportsMultiVolume(string extensionWithPeriodInLowerCase)
        {
            if (SharpCompressSupportsMultiVolumeZip || sevenZipExists) return true;
            var c1 = extensionWithPeriodInLowerCase[1];
            return c1 != 'z' && c1 != 'e' && (c1 != 'c' || extensionWithPeriodInLowerCase[3] != 'z'); // ".7z" は c1 != 'c' を満たすので IndexOutOfRangeException は投げられない
        }

        //private readonly static string sevenZipPath_Installed = Path.Combine(Environment.GetFolderPath(
        //    !Environment.Is64BitProcess && Environment.Is64BitOperatingSystem ? Environment.SpecialFolder.ProgramFilesX86 : Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.dll");
        private readonly static string sevenZipPath_Local_xXX = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), Environment.Is64BitProcess ? "7z-x64.dll" : "7z-x86.dll");
        private readonly static string sevenZipPath_Local = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "7z.dll");
        //private readonly static string[] sevenZipPath_RegPaths = new string[] { @"HKEY_CURRENT_USER\Software\7-Zip" };
        private readonly static string[] sevenZipPath_RegPaths = new string[] { @"HKEY_CURRENT_USER\Software\7-Zip", @"HKEY_LOCAL_MACHINE\Software\7-Zip" };
        //private readonly static string sevenZipPath_Name = Environment.Is64BitProcess ? Environment.Is64BitOperatingSystem ? "Path" : "Path64" : Environment.Is64BitOperatingSystem ? "Path32" : "Path";
        private readonly static string sevenZipPath_Name = Environment.Is64BitProcess ? "Path64" : "Path32";
        public static string GetSevenZipPath()
        {
            bool dummy;
            return GetSevenZipPath(out dummy);
        }
        public static string GetSevenZipPath(out bool gotPathFromRegistry)
        {
            string result;
            try
            {
                if (File.Exists(sevenZipPath_Local_xXX))
                {
                    result = sevenZipPath_Local_xXX;
                    gotPathFromRegistry = false;
                }
                else if (File.Exists(sevenZipPath_Local))
                {
                    result = sevenZipPath_Local;
                    gotPathFromRegistry = false;
                }
                else
                {
                    result = null;
                    gotPathFromRegistry = false;
                    foreach (var path in sevenZipPath_RegPaths)
                    {
                        result = ZipPlaAddressBar.RegistryGetValueWithoutException(path, sevenZipPath_Name, null as string);
                        if (result != null)
                        {
                            // レジストリに無意味なデータがある可能性を想定
                            try
                            {
                                result = Path.Combine(result, "7z.dll");
                                if (File.Exists(result))
                                {
                                    gotPathFromRegistry = true;
                                    break;
                                }
                                else result = null;
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch
            {
                result = null;
                gotPathFromRegistry = false;
            }
            sevenZipExists = result != null;
            return result;
        }
        // 最後に読み込めることを確かめた 7z.dll（パス・サイズ・更新日時）。同じなら LoadLibrary での確認を省く（K20）
        private static string checkedSevenZipSignature;

        public static void CheckSevenZipExistence(System.Windows.Forms.Form owner)
        {
            bool gotPathFromRegistry;
            var path = GetSevenZipPath(out gotPathFromRegistry);
            //if (path != null && !gotPathFromRegistry)
            if (path != null) // 安全性をとってこちらを採用
                {
                string signature = null;
                try
                {
                    var info = new FileInfo(path);
                    signature = path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
                }
                catch { }
                if (signature != null && signature == checkedSevenZipSignature) return;
                try
                {
                    using (new SevenZipHandle(path))
                    {
                    }
                    checkedSevenZipSignature = signature;
                }
                catch
                {
                    string a, b;
                    if (Environment.Is64BitProcess)
                    {
                        a = "x86";
                        b = "x64";
                    }
                    else
                    {
                        a = "x64";
                        b = "x86";
                    }

                    var message =
                        Message.FollowingDllCouldNotBeLoaded + "\n" +
                        Message.ConfirmItIsNotFor1But2.Replace("$1", a).Replace("$2", b) + "\n" +
                        "\n" +
                        path;

                    System.Windows.Forms.MessageBox.Show(owner, message, null,
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
        }

        private static bool sevenZipExists;

        private string path;
        private Stream stream;
        private string[] dirEntries;
        private ZipArchive zipArchive;
        private ZipArchiveEntry[] zipArchiveEntries;
        private IArchive iArchive;
        private IArchiveEntry[] iArchiveEntries;
        private ArchiveFile sevenZipArchive;
        private Entry[] sevenZipArchiveEntries;
        private IReader iReader;
        // Keep a bounded cache of extracted files; decoded bitmaps are much larger.
        // solid 書庫で展開済みのエントリ（画像はもともと圧縮されているので、展開後もほぼ書庫と同じ大きさ）（B2）
        private Dictionary<int, byte[]> solidImageCache;
        private long solidImageCacheBytes;

        // 呼び出し側（ビューア）が縮小画像を作り終えたエントリ（D9）。どのスレッドからでも積めるようにキューで受け取り、
        // solid のキャッシュに触るとき（書庫のロックの中）に反映する
        private readonly System.Collections.Concurrent.ConcurrentQueue<int> consumedEntryQueue = new System.Collections.Concurrent.ConcurrentQueue<int>();
        private HashSet<int> consumedEntries;
        // 使用済みでも、表示中のページからこの範囲は残す（表示中のページの原寸での読み直し（V22）やルーペ、少し戻る場合のため）
        private const int KeepConsumedRange = 16;
        // 呼び出し側（ビューア）が表示しているエントリ（D9）。-1 なら要求されたページを基準にする
        private volatile int viewPositionIndex = -1;

        /// <summary>
        /// 表示しているエントリを伝える（D9）。使用済みのエントリを捨てるときの基準にする。先読みの要求は表示から遠くへ飛ぶので、
        /// 要求されたページを基準にすると表示中のページまで捨ててしまう。書庫のロックは要らない
        /// </summary>
        public void SetViewPosition(PackedImageEntry entry)
        {
            var cIndex = entry.ChildIndex;
            if (cIndex < 0)
            {
                viewPositionIndex = entry.Index;
            }
            else
            {
                var c = children;
                if (c == null || cIndex >= c.Length) return;
                var t = c[cIndex];
                t.Item1.SetViewPosition(t.Item2[entry.Index]);
            }
        }

        /// <summary>
        /// このエントリはもう縮小画像ができていて、solid のキャッシュのバイト列は要求されたページから遠ければ捨ててよい（D9）。
        /// 書庫のロックは要らない（どのスレッドからでも呼べる）
        /// </summary>
        public void MarkEntryConsumed(PackedImageEntry entry)
        {
            var cIndex = entry.ChildIndex;
            if (cIndex < 0)
            {
                consumedEntryQueue.Enqueue(entry.Index);
            }
            else
            {
                var c = children;
                if (c == null || cIndex >= c.Length) return;
                var t = c[cIndex];
                t.Item1.MarkEntryConsumed(t.Item2[entry.Index]);
            }
        }

        /// <summary>
        /// 呼び出し側（ビューア）がこのエントリの縮小画像を捨てたので、使用済みの印を外す（D9）。
        /// 外さないと、戻ったときにこのページがまとめて展開されず、1 ページごとに solid のブロックを最初から展開し直す。
        /// 書庫のロックは要らない
        /// </summary>
        public void UnmarkEntryConsumed(PackedImageEntry entry)
        {
            var cIndex = entry.ChildIndex;
            if (cIndex < 0)
            {
                consumedEntryQueue.Enqueue(~entry.Index); // 負の値は印を外す
            }
            else
            {
                var c = children;
                if (c == null || cIndex >= c.Length) return;
                var t = c[cIndex];
                t.Item1.UnmarkEntryConsumed(t.Item2[entry.Index]);
            }
        }

        /// <summary>
        /// 使用済みの印をすべて外す（D9。同じ本の読み直しで縮小画像を作り直すとき）。書庫のロックは要らない
        /// </summary>
        public void ClearConsumedEntries()
        {
            consumedEntryQueue.Enqueue(ClearConsumedMarker);
            viewPositionIndex = -1;
            var c = children;
            if (c != null) foreach (var t in c) t.Item1.ClearConsumedEntries();
        }
        private const int ClearConsumedMarker = int.MinValue;

        // 使用済みの印を反映し、要求されたページから遠い使用済みのエントリを solid のキャッシュから捨てる（D9）
        private void applyConsumedEntries(int anchorIndex)
        {
            while (consumedEntryQueue.TryDequeue(out var index))
            {
                if (index == ClearConsumedMarker)
                {
                    consumedEntries?.Clear();
                }
                else if (index < 0)
                {
                    consumedEntries?.Remove(~index);
                }
                else
                {
                    if (consumedEntries == null) consumedEntries = new HashSet<int>();
                    consumedEntries.Add(index);
                }
            }
            // 要求されたページは、もう一度要るので使用済みではない
            consumedEntries?.Remove(anchorIndex);
            var view = viewPositionIndex;
            if (view >= 0) anchorIndex = view;
            if (consumedEntries == null || solidImageCache == null || solidImageCache.Count == 0) return;
            List<int> remove = null;
            foreach (var pair in solidImageCache)
            {
                if (Math.Abs(pair.Key - anchorIndex) > KeepConsumedRange && consumedEntries.Contains(pair.Key))
                {
                    (remove ?? (remove = new List<int>())).Add(pair.Key);
                }
            }
            if (remove == null) return;
            foreach (var key in remove)
            {
                solidImageCacheBytes -= solidImageCache[key].Length;
                solidImageCache.Remove(key);
            }
        }

        private bool isConsumedFarEntry(int index, int anchorIndex)
        {
            var view = viewPositionIndex;
            if (view >= 0) anchorIndex = view;
            return consumedEntries != null && Math.Abs(index - anchorIndex) > KeepConsumedRange && consumedEntries.Contains(index);
        }
        private int[] solidEntryOccurrences;
        // (エントリ名, 同名の何番目か) → physicalIndex。リーダーで通過したエントリをキャッシュに入れるために使う
        private Dictionary<string, int[]> solidIndexByKey;
        private Dictionary<string, int> solidReaderOccurrences;
        private int solidLastRequestedIndex = -1;
        private PdfReader pdfArchive;
        private bool pdfImageMustBeConvert = false;

        private int leftHierarchies;
        private PackedImageLoaderSearchMode searchMode;
        private Tuple<PackedImageLoader, List<PackedImageEntry>>[] children;
        /// <summary>作った子のローダーすべて。列挙の途中で例外が出て children に入らなかったものも含めて Dispose で解放する（X4）</summary>
        private List<PackedImageLoader> ownedChildLoaders;

        bool animation = false;

        /// <summary>
        /// エントリー取得時にセットされる。HasAnyEntry では取得されない
        /// </summary>
        public int LoadedDepth { get; private set; } = 0;

        //public PackedImageLoader(string path) : this(path, 0, false) { }

        public PackedImageLoader(string path, int leftHierarchies, PackedImageLoaderSearchMode searchMode, 
            bool animation = false, PackedImageLoaderOnMemoryMode onMemory = PackedImageLoaderOnMemoryMode.None)
        {
            if (path != null && path.Length > 0 && path.Last() == ':') path += Path.DirectorySeparatorChar;
            this.path = path;
            this.leftHierarchies = leftHierarchies;
            this.searchMode = searchMode;
            if (Directory.Exists(path))
            {
                Type = PackType.Directory;
            }
            else if (File.Exists(path))
            {
                IsSupportedSingleFile = Supports(path, forFile: false);
                if (!IsSupportedSingleFile) onMemory = PackedImageLoaderOnMemoryMode.None;
                switch (onMemory)
                {
                    case PackedImageLoaderOnMemoryMode.OnMemory:
                        {
                            var fs = File.OpenRead(path);
                            var length = fs.Length;
                            if (length > OnMemoryLimit)
                            {
                                // MemoryStream は 2GB を超えられないので、以前は途中で失敗していた。大きな書庫は通常どおりファイルから読む（V18）
                                stream = fs;
                                break;
                            }
                            using (fs)
                            {
                                // 容量を先に確保して、倍々の拡張でファイルサイズの約2倍を一時的に確保しないようにする（B8）
                                stream = new MemoryStream((int)length);
                                fs.CopyTo(stream);
                            }
                            stream.Seek(0, SeekOrigin.Begin);
                        }
                        break;
                    case PackedImageLoaderOnMemoryMode.Releasable:
                        stream = new ReleasableStream(OpenArchiveFile(path));
                        break;
                    default:
                        stream = OpenArchiveFile(path);
                        break;
                }
                setType();
                if (!IsSupportedSingleFile)
                {
                    stream.Close();
                    stream.Dispose();
                    stream = null;

                    if (!SharpCompressSupportsMultiVolumeZip && Type == PackType.Zip)
                    {
                        Type = PackType.ZipM;
                    }
                }
            }
            else
            {
                throw new FileNotFoundException(null, path);
            }
            this.animation = animation;
        }
        
        /// <summary>
        /// pathToCheckType はそれが TAR かどうかの確認にのみ用いる。渡された stream は Dispose まで PackedImageLoder が管理する
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="pathToCheckType"></param>
        private PackedImageLoader(Stream stream, long streamLength, string pathToCheckType, int leftHierarchies, PackedImageLoaderSearchMode searchMode, bool animation)
        {
            this.stream = SeekableStream.Seekablize(stream, streamLength);
            path = pathToCheckType;
            this.leftHierarchies = leftHierarchies;
            this.searchMode = searchMode;
            setType();
            this.animation = animation;
        }

        /// <summary>
        /// 失敗したら stream を dispose
        /// </summary>
        private void setType()
        {
            // TAR は仕様上ファイルの先頭からファイル形式を確定することができない
            try
            {
                if (Path.GetExtension(path).ToLower() == ".tar")
                {
                    Type = PackType.Tar;
                }
                else
                {
                    Type = GetTypeFromStreamAndSeekFirst(stream);
                }
                if (Type >= PackType.Unknown)
                {
                    throw new NotSupportedException();
                }
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 書庫のファイルを開く。File.OpenRead と同じ共有モードで、読み込みのバッファだけ 64KB にする。
        /// 既定の 4KB では、ZipArchive の Deflate が 8KB ずつ読むたびにバッファを通らず ReadFile になり、
        /// 1 ページ（数 MB）で数百回の ReadFile（NAS では同じ数の要求）になる
        /// </summary>
        private static FileStream OpenArchiveFile(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);

        public class PackedImageEntry
        {
            public int ChildIndex { get; private set; }
            public int Index { get; private set; }
            public string Path { get; private set; }
            public DateTime LastWriteTime { get; private set; }
            public long Size { get; private set; }

            public PackedImageEntry(int childIndex, int index, string path)
            {
                ChildIndex = childIndex;
                Index = index;
                Path = path;
                LastWriteTime = DateTime.MinValue;
                Size = -1;
            }

            public PackedImageEntry(int childIndex, int index, string path, DateTime lastWriteTime, long size)
            {
                ChildIndex = childIndex;
                Index = index;
                Path = path;
                LastWriteTime = lastWriteTime;
                Size = size;
            }
        }

        public static string[] GetSeparatorReplacedArray(List<PackedImageEntry> list, PackType type)
        {
            var count = list.Count;
            var replaced = new string[count];
            char s, r;
            if (type == PackType.Directory)
            {
                s = Path.DirectorySeparatorChar;
                r = Convert.ToChar(1);
            }
            else
            {
                s = Path.AltDirectorySeparatorChar;
                r = Convert.ToChar(2);
            }
            for (var i = 0; i < count; i++)
            {
                var name = list[i].Path;
                if (name.Contains(s))
                {
                    replaced[i] = name.Replace(s, r);
                }
                else
                {
                    replaced[i] = name;
                }
            }
            return replaced;
        }

        public static string[] GetSeparatorReplacedArray(PackedImageEntry[] list, PackType type)
        {
            var count = list.Length;
            var replaced = new string[count];
            char s, r;
            if (type == PackType.Directory)
            {
                s = Path.DirectorySeparatorChar;
                r = Convert.ToChar(1);
            }
            else
            {
                s = Path.AltDirectorySeparatorChar;
                r = Convert.ToChar(2);
            }
            for (var i = 0; i < count; i++)
            {
                var name = list[i].Path;
                if (name.Contains(s))
                {
                    replaced[i] = name.Replace(s, r);
                }
                else
                {
                    replaced[i] = name;
                }
            }
            return replaced;
        }

        // パフォーマンス改善版
        public List<PackedImageEntry> GetPackedImageEntries(PackedImageLoaderFileInfo neededFileInfos = 0, bool needToExecOnTask = false)
        {
            var fullEntriesList = GetPackedImageFullEntries(neededFileInfos, needToExecOnTask);

            if (Type == PackType.Pdf) return fullEntriesList;

            var replaced = GetSeparatorReplacedArray(fullEntriesList, Type);

            var count = fullEntriesList.Count;

            //var fullEntries = fullEntriesList.OrderBy(entry => entry.Path, new LogicalStringComparer()).ToArray();
            var fullEntries = Enumerable.Range(0, count).OrderBy(i => replaced[i], new LogicalStringComparer()).Select(i => fullEntriesList[i]).ToArray();

            replaced = null;

            if (count < 3) return fullEntries.ToList(); // ソートは必要

            var count1 = count - 1;
            var delete = new bool[count];
            var declesedCount = count;
            var path0 = fullEntries[0].Path;
            var path1 = fullEntries[1].Path;
            for (var i = 1; i < count1; i++)
            {
                var path2 = fullEntries[i + 1].Path;

                var len0 = path0.Length;
                var len1 = path1.Length;
                var len2 = path2.Length;
                int centerOffset;
                string first, second, center;
                int lenShort, lenCenter;
                if (len0 > len1 && len1 == len2)
                {
                    if (len0 - len1 < 2)
                    {
                        path0 = path1;
                        path1 = path2;
                        continue;
                    }
                    centerOffset = -1;
                    lenShort = len1;
                    lenCenter = len0;
                    center = path0;
                    first = path1;
                    second = path2;
                }
                else if (len0 < len1 && len0 == len2)
                {
                    if (len1 - len0 < 2)
                    {
                        path0 = path1;
                        path1 = path2;
                        continue;
                    }
                    centerOffset = 0;
                    lenShort = len0;
                    lenCenter = len1;
                    first = path0;
                    center = path1;
                    second = path2;
                }
                else if (len0 == len1 && len1 < len2)
                {
                    if (len2 - len0 < 2)
                    {
                        path0 = path1;
                        path1 = path2;
                        continue;
                    }
                    centerOffset = 1;
                    lenShort = len0;
                    lenCenter = len2;
                    first = path0;
                    second = path1;
                    center = path2;
                }
                else
                {
                    path0 = path1;
                    path1 = path2;
                    continue;
                }
                var digitStart = -1;
                var digitStop = -1;
                var j = 1;
                string preCenter = null, alt = null;
                for (; j < lenShort; j++)
                {
                    var cc = center[j];
                    if (first[j] == cc)
                    {
                        if (second[j] != cc)
                        {
                            preCenter = first;
                            alt = second;
                            break;
                        }
                    }
                    else if (second[j] == cc)
                    {
                        preCenter = second;
                        alt = first;
                        break;
                    }
                    else break;
                }
                if (preCenter == null)
                {
                    path0 = path1;
                    path1 = path2;
                    continue;
                }
                char c0;
                var jTemp = j;
                while (j >= 0 && '0' <= (c0 = preCenter[j]) && c0 <= '9')
                {
                    j--;
                }
                if (j == jTemp)
                {
                    path0 = path1;
                    path1 = path2;
                    continue;
                }
                j++;
                for (; j < lenShort; j++)
                {
                    c0 = preCenter[j];
                    var c1 = center[j];
                    var c0IsDigit = '0' <= c0 && c0 <= '9';
                    if (c0 == c1)
                    {
                        if (c0IsDigit)
                        {
                            if (digitStop < j) digitStart = j;
                            digitStop = j + 1;
                        }
                        continue;
                    }
                    if(digitStop == j && (c0IsDigit || '0' <= c1 && c1 <= '9'))
                    {
                        digitStart = -1;
                    }
                    break;
                }
                if (digitStart < 0)
                {
                    path0 = path1;
                    path1 = path2;
                    continue;
                }
                //var lenC = centerCenter ? len1 : len0;
                var lenBound = Math.Min(lenShort + 3, lenCenter);
                while(j < lenBound)
                {
                    var cc = center[j++];
                    if ('0' <= cc && cc <= '9') break;
                }
                if (j == lenBound)
                {
                    path0 = path1;
                    path1 = path2;
                    continue;
                }
                var digitStart2 = j - 1;
                while (j < lenBound)
                {
                    var cc = center[j++];
                    if (!('0' <= cc && cc <= '9')) break;
                }
                int nLeft, nRight;
                if (!int.TryParse(center.Substring(digitStart, digitStop - digitStart), out nLeft) || !int.TryParse(center.Substring(digitStart2, j - digitStart2 - 1), out nRight) ||
                   Math.Abs(nLeft - nRight) != 1)
                {
                    path0 = path1;
                    path1 = path2;
                    continue;
                }
                
                if(alt == center.Substring(0, digitStart) + center.Substring(digitStart2))
                {
                    declesedCount -= 2; // 削除の重複は起こらない。また起こっても問題にならない。
                    switch (centerOffset)
                    {
                        case -1:
                            delete[i] = true;
                            delete[i + 1] = true;
                            break;
                        case 0:
                            delete[i - 1] = true;
                            delete[i + 1] = true;
                            break;
                        case 1:
                            delete[i - 1] = true;
                            delete[i] = true;
                            break;

                    }
                }

                path0 = path1;
                path1 = path2;
            }

            if (declesedCount < count)
            {
                var result = new List<PackedImageEntry>(capacity: Math.Max(1, declesedCount));
                for (var i = 0; i < count; i++)
                {
                    if (!delete[i]) result.Add(fullEntries[i]);
                }
                return result;
            }
            else
            {
                return fullEntries.ToList();
            }
        }


        public static bool PrioritizeSevenZip = false;

        // 7z 形式は、7z.dll があれば SharpCompress より 7z.dll で読む方が速い（300 ページの 7z で solid 2.4 秒→1.0 秒、非 solid 2.5 秒→0.8 秒）。
        // 7z.dll が見つからなければ従来どおり SharpCompress で読む。
        // CAB は 7z.dll だけで読む（B7。WiX DTF の Microsoft.Deployment.Compression.Cab は MS-RL で GPL と両立しない可能性があるのでやめた）
        // RAR4 も 7z.dll で読む。SharpCompress の RAR の展開は managed の実装で、7z.dll（unrar と同じネイティブの実装）より遅い。
        // ただしヘッダーごと暗号化された RAR4 は、7z.dll ではパスワードの問い合わせに答えられず開けないだけになるので、
        // パスワード付きと分かるよう従来どおり SharpCompress で読む。項目だけ暗号化されている場合は一覧を作るときに PasswordLockedException にする
        private bool PreferSevenZipDll(PackType type) => type == PackType.SevenZip || type == PackType.Cab || type == PackType.Rar && !rar4HeaderEncrypted && !rar4SevenZipFailed;

        // 7z.dll で RAR4 を開けなかった（RAR を読めない 7z.dll など）。以後この書庫は従来どおり SharpCompress で読む
        private bool rar4SevenZipFailed = false;

        /// <summary>
        /// RAR4 をまず 7z.dll で読み、開けなかったら（パスワード付き以外の例外）従来どおり SharpCompress で読み直す。
        /// 見つかった 7z.dll が RAR を読めない場合（7za.dll など）や 7z.dll が開けない書庫でも、以前と同じく開けるように
        /// </summary>
        private T withRar4SevenZipFallback<T>(Func<T> sevenZip, Func<T> sharpCompress)
        {
            try
            {
                return sevenZip();
            }
            catch (PasswordLockedException)
            {
                throw;
            }
            catch
            {
                var archive = sevenZipArchive;
                sevenZipArchive = null;
                sevenZipArchiveEntries = null;
                sevenZipSolid = null;
                try { archive?.DisposeWithoutClosingStream(); } catch { }
                rar4SevenZipFailed = true;
                stream.Seek(0, SeekOrigin.Begin);
                return sharpCompress();
            }
        }

        // RAR4 のメインヘッダーに MHD_PASSWORD（0x80）が立っているか。読めなければ true（従来どおり SharpCompress で読む）
        private bool rar4HeaderEncrypted = false;

        /// <summary>
        /// RAR4 の署名（7 バイト）の直後のメインヘッダー（HEAD_CRC 2・HEAD_TYPE 1 = 0x73・HEAD_FLAGS 2）を見て、ヘッダーが暗号化されているかを返す。
        /// stream は書庫の先頭にあること。位置は戻す
        /// </summary>
        private static bool isRar4HeaderEncrypted(Stream stream)
        {
            var position = stream.Position;
            try
            {
                var buffer = new byte[12];
                if (!readFully(stream, buffer, buffer.Length)) return true;
                if (buffer[9] != 0x73) return true;
                return (buffer[10] & 0x80) != 0;
            }
            catch
            {
                return true;
            }
            finally
            {
                stream.Seek(position, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// 7z.dll で読む RAR4 の画像の項目が暗号化されていれば PasswordLockedException にする（SharpCompress で読んでいたときと同じく、パスワード付きと分かるように）
        /// </summary>
        private static void throwIfEncryptedRar4Entry(KnownSevenZipFormat format, Entry entry)
        {
            if (format == KnownSevenZipFormat.Rar && entry.IsEncrypted) throw new PasswordLockedException();
        }

        private bool UseZipArchiveClass = true;
        // ZipArchive に渡す名前の文字コード。null なら UTF-8 フラグのない名前をシステムの既定のコードページで読む
        private Encoding ZipArchiveEntryNameEncoding = null;

        public bool HasAnyEntry()
        {
            switch (Type)
            {
                case PackType.Directory:
                    return hasAnyDirEntry();
                case PackType.ZipM:
                    return hasAnySevenZipArchiveEntry(KnownSevenZipFormat.Zip, GetSevenZipPath());
                case PackType.Rar5:
                    return hasAnySevenZipArchiveEntry(KnownSevenZipFormat.Rar5, GetSevenZipPath());
                case PackType.Lha:
                    return hasAnySevenZipArchiveEntry(KnownSevenZipFormat.Lzh, GetSevenZipPath());
                case PackType.Pdf:
                    return hasAnyPdfEntry();
                default:
                    var sevenZipPath = stream == null || !(PrioritizeSevenZip || PreferSevenZipDll(Type)) ? null : GetSevenZipPath();

                    if (sevenZipPath == null)
                    {
                        switch (Type)
                        {
                            case PackType.Zip: return UseZipArchiveClass ? hasAnyZipArchiveEntry() : hasAnyIArchiveEntry();
                            case PackType.Cab: throw new NotSupportedException(); // 7z.dll が要る（B7）
                            default: return hasAnyIArchiveEntry();
                        }
                    }
                    else
                    {
                        switch (Type)
                        {
                            case PackType.Zip: return hasAnySevenZipArchiveEntry(KnownSevenZipFormat.Zip, sevenZipPath);
                            case PackType.Rar: return withRar4SevenZipFallback(() => hasAnySevenZipArchiveEntry(KnownSevenZipFormat.Rar, sevenZipPath), hasAnyIArchiveEntry);
                            case PackType.Tar: return hasAnySevenZipArchiveEntry(KnownSevenZipFormat.Tar, sevenZipPath);
                            case PackType.SevenZip: return hasAnySevenZipArchiveEntry(KnownSevenZipFormat.SevenZip, sevenZipPath);
                            case PackType.Cab: return hasAnySevenZipArchiveEntry(KnownSevenZipFormat.Cab, sevenZipPath);
                        }
                    }

                    throw new NotSupportedException();
            }
        }

        /// <summary>
        /// これをメインスレッドで呼び出し画像の取得をサブスレッドで呼び出す場合 needToExecOnTask = true とすること
        /// </summary>
        /// <param name="neededFileInfos"></param>
        /// <param name="needToExecOnTask"></param>
        /// <returns></returns>
        public List<PackedImageEntry> GetPackedImageFullEntries(PackedImageLoaderFileInfo neededFileInfos = 0, bool needToExecOnTask = false)
        {
            switch (Type)
            {
                case PackType.Directory:
                    return getDirEntries(neededFileInfos, needToExecOnTask);
                case PackType.ZipM:
                    return getSevenZipArchiveEntry(neededFileInfos, KnownSevenZipFormat.Zip, GetSevenZipPath(), needToExecOnTask);
                case PackType.Rar5:
                    return getSevenZipArchiveEntry(neededFileInfos, KnownSevenZipFormat.Rar5, GetSevenZipPath(), needToExecOnTask);
                case PackType.Lha:
                    return getSevenZipArchiveEntry(neededFileInfos, KnownSevenZipFormat.Lzh, GetSevenZipPath(), needToExecOnTask);
                case PackType.Pdf:
                    return getPdfEntries();
                default:
                    var sevenZipPath = stream == null || !(PrioritizeSevenZip || PreferSevenZipDll(Type)) ? null : GetSevenZipPath();

                    if (sevenZipPath == null)
                    {
                        switch (Type)
                        {
                            case PackType.Zip:
                                return UseZipArchiveClass ? getZipArchiveEntries(neededFileInfos, needToExecOnTask) : getIArchiveEntries(neededFileInfos, needToExecOnTask);
                            case PackType.Rar:
                            case PackType.Tar:
                            case PackType.SevenZip:
                                return getIArchiveEntries(neededFileInfos, needToExecOnTask);
                            case PackType.Cab: throw new NotSupportedException(); // 7z.dll が要る（B7）
                        }
                    }
                    else
                    {
                        switch (Type)
                        {
                            case PackType.Zip:
                                return getSevenZipArchiveEntry(neededFileInfos, KnownSevenZipFormat.Zip, sevenZipPath, needToExecOnTask);
                            case PackType.Rar:
                                return withRar4SevenZipFallback(() => getSevenZipArchiveEntry(neededFileInfos, KnownSevenZipFormat.Rar, sevenZipPath, needToExecOnTask),
                                    () => getIArchiveEntries(neededFileInfos, needToExecOnTask));
                            case PackType.Tar:
                                return getSevenZipArchiveEntry(neededFileInfos, KnownSevenZipFormat.Tar, sevenZipPath, needToExecOnTask);
                            case PackType.SevenZip:
                                return getSevenZipArchiveEntry(neededFileInfos, KnownSevenZipFormat.SevenZip, sevenZipPath, needToExecOnTask);
                            case PackType.Cab: return getSevenZipArchiveEntry(neededFileInfos, KnownSevenZipFormat.Cab, sevenZipPath, needToExecOnTask);
                        }
                    }
                    throw new NotSupportedException();
            }
        }
        
        private bool hasAnyDirEntry()
        {
            var ffmpegEixsts = MovieThumbnailLoader.ffmpegExists();
            var files = new DirectoryInfo(path).EnumerateFiles();
            if (files.Any(info => ImageLoader.SupportsFullReading(info.FullName, ffmpegEixsts))) return true;
            // Name でもよいが今後の更新で getDirEntries とギャップが出来る可能性を考慮
            var h = leftHierarchies - 1;
            return h >= 0 &&
                (
                     new DirectoryInfo(path).EnumerateDirectories().Any(cInfo =>
                     {
                         using (var cLoader = new PackedImageLoader(cInfo.FullName, h, PackedImageLoaderSearchMode.UntilFoundItem))
                         {
                             return cLoader.HasAnyEntry();
                         }
                     })
                 ||
                     files.Any(cInfo =>
                     {
                         var path = cInfo.FullName;
                         if (!Supports(path, forFile: true)) return false;
                         using (var cLoader = new PackedImageLoader(path, h, PackedImageLoaderSearchMode.UntilFoundItem))
                         {
                             return cLoader.HasAnyEntry();
                         }
                     })
                );
        }

        private List<PackedImageEntry> getDirEntries(PackedImageLoaderFileInfo neededFileInfos, bool needToExecOnTask)
        {
            var needAny = neededFileInfos != 0;
            FileInfo[] fileInfos = null;
            if (needAny)
            {
                fileInfos = new DirectoryInfo(path).GetFiles();
                dirEntries = new string[fileInfos.Length];
            }
            else
            {
                dirEntries = Directory.GetFiles(path);
            }

            var result = new List<PackedImageEntry>();
            var length = dirEntries.Length;
            var parentPathLength = this.path.Length;
            if(!this.path.EndsWith(Path.DirectorySeparatorChar.ToString()) && !this.path.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                parentPathLength++;
            }
            var needTime = (neededFileInfos & PackedImageLoaderFileInfo.LastWriteTime) == PackedImageLoaderFileInfo.LastWriteTime;
            var needSize = (neededFileInfos & PackedImageLoaderFileInfo.Size) == PackedImageLoaderFileInfo.Size;
            var ffmpegEixsts = MovieThumbnailLoader.ffmpegExists();
            if (needAny)
            {
                for (var i = 0; i < length; i++)
                {
                    var info = fileInfos[i];
                    var path = info.FullName;
                    dirEntries[i] = path;
                    if (ImageLoader.SupportsFullReading(path, ffmpegEixsts))
                    {
                        DateTime lastWriteTime;
                        if (needTime)
                        {
                            try
                            {
                                lastWriteTime = info.LastWriteTime;
                            }
                            catch
                            {
                                lastWriteTime = new DateTime();
                            }
                        }
                        else
                        {
                            lastWriteTime = DateTime.MinValue;
                        }

                        long size;
                        if (needSize)
                        {
                            try
                            {
                                size = info.Length;
                            }
                            catch
                            {
                                size = -1;
                            }
                        }
                        else
                        {
                            size = -1;
                        }

                        result.Add(new PackedImageEntry(-1, i, path.Substring(parentPathLength), lastWriteTime, size));
                    }
                }
            }
            else
            {
                for (var i = 0; i < length; i++)
                {
                    var path = dirEntries[i];
                    if (ImageLoader.SupportsFullReading(path, ffmpegEixsts))
                    {
                        result.Add(new PackedImageEntry(-1, i, path.Substring(parentPathLength)));
                    }
                }
            }
            
            var h = leftHierarchies - 1;
            if (h >= 0 && (searchMode == PackedImageLoaderSearchMode.Full || result.Count == 0))
            {
                var childrenList = new List<Tuple<PackedImageLoader, List<PackedImageEntry>>>();

                var stop = false;

                if (!stop)
                {
                    foreach (var path in Directory.GetDirectories(path))
                    {
                        var name = path.Substring(parentPathLength);
                        var res = addChildFile(result, childrenList, h, path, name, neededFileInfos, needToExecOnTask);
                        if (searchMode == PackedImageLoaderSearchMode.UntilFoundItem && res > 0)
                        {
                            stop = true;
                            break;
                        }
                    }
                }

                if (!stop)
                {
                    foreach (var path in dirEntries)
                    {
                        var name = path.Substring(parentPathLength);
                        if (Supports(name, forFile: true))
                        {
                            var res = addChildFile(result, childrenList, h, path, name, neededFileInfos, needToExecOnTask);
                            if (searchMode == PackedImageLoaderSearchMode.UntilFoundItem && res > 0)
                            {
                                stop = true;
                                break;
                            }
                        }
                    }
                }
                
                children = childrenList.ToArray();
            }

            // 他に画像がない場合

            return result;
        }

        private int addChildFile(List<PackedImageEntry> thisEntries,
            List<Tuple<PackedImageLoader, List<PackedImageEntry>>> childrenList, int nextLeft,
            string path, string name, PackedImageLoaderFileInfo neededFileInfos, bool needToExecOnTask)
        {
            return addChildFile(thisEntries, childrenList, nextLeft, new PackedImageLoader(path, nextLeft, searchMode), name, neededFileInfos, needToExecOnTask);
        }

        private int addChildFile(List<PackedImageEntry> thisEntries,
            List<Tuple<PackedImageLoader, List<PackedImageEntry>>> childrenList, int nextLeft,
            Stream stream, long streamLength, string name, PackedImageLoaderFileInfo neededFileInfos, bool needToExecOnTask)
        {
            return addChildFile(thisEntries, childrenList, nextLeft, new PackedImageLoader(stream, streamLength, name, nextLeft, searchMode, animation), name, neededFileInfos, needToExecOnTask);
        }
        
        private int addChildFile(List<PackedImageEntry> thisEntries,
            List<Tuple<PackedImageLoader, List<PackedImageEntry>>> childrenList, int nextLeft,
            PackedImageLoader loader, string loaderName, PackedImageLoaderFileInfo neededFileInfos, bool needToExecOnTask)
        {
            try
            {
                var entries = loader.GetPackedImageFullEntries(neededFileInfos, needToExecOnTask);
                var count = entries.Count;
                if (count == 0)
                {
                    var l = loader;
                    loader = null;
                    l.Dispose(); // これに失敗したら再試行しないように変数 l を使用
                    return 0;
                }
                var childIndex = childrenList.Count;
                for (var i = 0; i < count; i++)
                {
                    var e = entries[i];
                    thisEntries.Add(new PackedImageEntry(childIndex, i, loaderName + Path.AltDirectorySeparatorChar + e.Path, e.LastWriteTime, e.Size));
                }
                var newDepth = loader.LoadedDepth + 1;
                if (newDepth > LoadedDepth) LoadedDepth = newDepth;
                (ownedChildLoaders ?? (ownedChildLoaders = new List<PackedImageLoader>())).Add(loader);
                childrenList.Add(Tuple.Create(loader, entries));
                return count;
            }
            catch// (Exception  e)
            {
                if (loader != null) loader.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Stream は Dispose する。必ず遅延割り当てで渡すこと。
        /// </summary>
        /// <param name="children"></param>
        /// <returns></returns>
        private bool hasAnyChildStreamEntry(IEnumerable<Tuple<Stream, long, string>> children, int nextLeft)
        {
            foreach (var child in children)
            {
                var stream = child.Item1; // 遅延割り当てのためこの直前で作成される
                var streamLength = child.Item2;
                var name = child.Item3;
                using (var loader = new PackedImageLoader(stream, streamLength, name, nextLeft, PackedImageLoaderSearchMode.UntilFoundItem, animation)) // エラーの有無にかかわらず stream は Dispose される
                {
                    if (loader.HasAnyEntry())
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool　hasAnyZipArchiveEntry()
        {
            try
            {
                if (zipArchive == null)
                {
                    zipArchive = new ZipArchive(stream, ZipArchiveMode.Read, false, ZipArchiveEntryNameEncoding);
                }
                var ffmpegEixsts = MovieThumbnailLoader.ffmpegExists();
                var h = leftHierarchies - 1;
                var hNN = h >= 0;
                var zipEntries = hNN ? new List<ZipArchiveEntry>() : null;
                foreach (var entry in zipArchive.Entries)
                {
                    if (ImageLoader.SupportsFullReading(entry.FullName, ffmpegEixsts) && !maxosxIgnore(entry.FullName))
                    {
                        return true;
                    }
                    if (hNN) zipEntries.Add(entry);
                }
                return hNN && hasAnyChildStreamEntry(from e in zipEntries let name = e.FullName where(Supports(name, forFile: false))
                                                     select Tuple.Create(e.Open(), e.Length, name), h);
            }
            catch (InvalidDataException)
            {
                throw new PasswordLockedException();
            }
        }
        private List<PackedImageEntry> getZipArchiveEntries(PackedImageLoaderFileInfo neededFileInfos, bool needToExecOnTask)
        {
            try
            {
                if (zipArchive == null)
                {
                    zipArchive = new ZipArchive(stream, ZipArchiveMode.Read, false, ZipArchiveEntryNameEncoding);
                }
                zipArchiveEntries = zipArchive.Entries.ToArray();
                var result = new List<PackedImageEntry>();
                var length = zipArchiveEntries.Length;
                //var needAny = neededFileInfos != 0;
                var needTime = (neededFileInfos & PackedImageLoaderFileInfo.LastWriteTime) == PackedImageLoaderFileInfo.LastWriteTime;
                var needSize = (neededFileInfos & PackedImageLoaderFileInfo.Size) == PackedImageLoaderFileInfo.Size;
                var ffmpegEixsts = MovieThumbnailLoader.ffmpegExists();
                for (var i = 0; i < length; i++)
                {
                    var entry = zipArchiveEntries[i];
                    var path = entry.FullName;
                    if (ImageLoader.SupportsFullReading(path, ffmpegEixsts))
                    {
                        // macOS の Finder が作る zip の __MACOSX/._* は画像ではない（SharpCompress の経路と同じく除く）
                        if (maxosxIgnore(path)) continue;
                        result.Add(new PackedImageEntry(-1, i, path, needTime ? entry.LastWriteTime.DateTime : new DateTime(), needSize ? entry.Length : -1));
                    }
                }
                
                var h = leftHierarchies - 1;
                if (h >= 0 && (searchMode == PackedImageLoaderSearchMode.Full || result.Count == 0))
                {
                    var childrenList = new List<Tuple<PackedImageLoader, List<PackedImageEntry>>>();
                    foreach (var entry in zipArchiveEntries)
                    {
                        var name = entry.FullName;
                        if (Supports(name, forFile: false))
                        {
                            var res = addChildFile(result, childrenList, h, entry.Open(), entry.Length, name, neededFileInfos, needToExecOnTask);
                            if (searchMode == PackedImageLoaderSearchMode.UntilFoundItem && res > 0) break;
                        }
                    }
                    children = childrenList.ToArray();
                }

                return result;
            }
            catch (InvalidDataException)
            {
                throw new PasswordLockedException();
            }
        }

        private bool hasAnyIArchiveEntry()
        {
            try
            {
                if (iArchive == null)
                {
                    iArchive = stream != null ? ArchiveFactory.Open(stream) : ArchiveFactory.Open(path);
                }
                //var leftSize = long.MaxValue;
                //if (iArchive.IsSolid) leftSize = SolidSizeBound;

                var ffmpegEixsts = MovieThumbnailLoader.ffmpegExists();
                var h = leftHierarchies - 1;
                var hNN = h >= 0;
                var iArchiveEntries = hNN ? new List<IArchiveEntry>() : null;

                foreach (var entry in iArchive.Entries)
                {
                    //leftSize -= entry.Size;
                    //if (leftSize < 0) break;
                    if (ImageLoader.SupportsFullReading(entry.Key, ffmpegEixsts))
                    {
                        return true;
                    }
                    if (hNN) iArchiveEntries.Add(entry);
                }
                
                return hNN && hasAnyChildStreamEntry(from e in iArchiveEntries let name = e.Key
                                                     where Supports(name, forFile: false) select Tuple.Create(e.OpenEntryStream(), e.Size, name), h);
            }
            catch (Exception e)
            {
                if (e.ToString().Contains("Crypto"))
                {
                    throw new PasswordLockedException();
                }
                else
                {
                    throw;
                }
            }
        }

        private bool hasAnySevenZipArchiveEntry(KnownSevenZipFormat format, string sevenZipPath)
        {
            if (sevenZipArchive == null)
            {
                if (sevenZipPath != null)
                {
                    sevenZipArchive = stream != null ? new ArchiveFile(stream, format, sevenZipPath) : new ArchiveFile(path, sevenZipPath);
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            //var sizeBound = long.MaxValue;
            //long size = 0;

            var ffmpegEixsts = MovieThumbnailLoader.ffmpegExists();
            var h = leftHierarchies - 1;
            var hNN = h >= 0;
            var sevenZipEntries = hNN ? new List<Entry>() : null;

            foreach (var entry in sevenZipArchive.EnumerableEntries)
            {
                if (!entry.IsFolder && ImageLoader.SupportsFullReading(entry.FileName, ffmpegEixsts))
                {
                    throwIfEncryptedRar4Entry(format, entry);
                    return true;
                }
                if (hNN) sevenZipEntries.Add(entry);
            }

            return hNN && hasAnyChildStreamEntry(from e in sevenZipEntries
                                                 let name = e.FileName
                                                 where Supports(name, forFile: false)
                                                 select Tuple.Create(openSevenZipStream(e) as Stream, e.Size, name), h);
        }

        private static MemoryStream openSevenZipStream(Entry entry)
        {
            var size = entry.Size;
            var result = size >= 0 && size <= int.MaxValue ? new MemoryStream((int)size) : new MemoryStream(); // B9
            try
            {
                entry.Extract(result);
                result.Seek(0, SeekOrigin.Begin);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
        
        private List<PackedImageEntry> getIArchiveEntries(PackedImageLoaderFileInfo neededFileInfos, bool needToExecOnTask)
        {
            try
            {
                if (iArchive == null)
                {
                    iArchive = stream != null ? ArchiveFactory.Open(stream) : ArchiveFactory.Open(path);
                }
                //var leftSize = long.MaxValue;
                if (iArchive.IsSolid)
                {
                    OpenInnerImageStreamSharpCompress_IsSolid = true;
                    //leftSize = SolidSizeBound;
                }
                
                var needToIgnoreMacList = Type == PackType.Zip;
                //var macFileList = needToIgnoreMacList ? new List<PackedImageEntry>() : null;
                
                iArchiveEntries = iArchive.Entries.ToArray();
                if (OpenInnerImageStreamSharpCompress_IsSolid)
                {
                    initializeSolidEntryOccurrences();
                }
                var result = new List<PackedImageEntry>();
                var length = iArchiveEntries.Length;
                var needTime = (neededFileInfos & PackedImageLoaderFileInfo.LastWriteTime) == PackedImageLoaderFileInfo.LastWriteTime;
                var needSize = (neededFileInfos & PackedImageLoaderFileInfo.Size) == PackedImageLoaderFileInfo.Size;
                var ffmpegEixsts = MovieThumbnailLoader.ffmpegExists();
                for (var i = 0; i < length; i++)
                {
                    var entry = iArchiveEntries[i];
                    var size = entry.Size;
                    //leftSize -= size;
                    //if (leftSize < 0) break;
                    var path = entry.Key;
                    if (ImageLoader.SupportsFullReading(path, ffmpegEixsts))
                    {
                        DateTime dateTime;
                        if (needTime)
                        {
                            var dateTime_ = entry.LastModifiedTime;
                            if (dateTime_ != null) dateTime = (DateTime)dateTime_;
                            else dateTime = DateTime.MinValue;
                        }
                        else
                        {
                            dateTime = DateTime.MinValue;
                        }
                        var piEntry = new PackedImageEntry(-1, i, path, dateTime, needSize ? size : -1);

                        if (needToIgnoreMacList && maxosxIgnore(entry.Key))
                        {
                            //macFileList.Add(piEntry);
                            continue;
                        }

                        result.Add(piEntry);
                    }
                }

                
                var h = leftHierarchies - 1;
                if (h >= 0 && (searchMode == PackedImageLoaderSearchMode.Full || result.Count == 0))
                {
                    var childrenList = new List<Tuple<PackedImageLoader, List<PackedImageEntry>>>();
                    foreach (var entry in iArchiveEntries)
                    {
                        var name = entry.Key;
                        if (Supports(name, forFile: false))
                        {
                            var res = addChildFile(result, childrenList, h, entry.OpenEntryStream(), entry.Size, name, neededFileInfos, needToExecOnTask);
                            if (searchMode == PackedImageLoaderSearchMode.UntilFoundItem && res > 0) break;
                        }
                    }
                    children = childrenList.ToArray();
                }

                return result;
            }
            catch (Exception e)
            {
                if (e.ToString().Contains("Crypto"))
                {
                    throw new PasswordLockedException();
                }
                else
                {
                    throw;
                }
            }
        }

        private static bool maxosxIgnore(string key)
        {
            if (!key.StartsWith("__MACOSX/")) return false;
            var fileNameIndex = key.LastIndexOf('/') + 1;
            return fileNameIndex < key.Length && key[fileNameIndex] == '.';
        }

        private List<PackedImageEntry> getSevenZipArchiveEntry(PackedImageLoaderFileInfo neededFileInfos, KnownSevenZipFormat format,
            string sevenZipPath, bool needToExecOnTask)
        {
            if (needToExecOnTask)
            {
                using (var task = Task.Run(() => getSevenZipArchiveEntry_Base(neededFileInfos, format, sevenZipPath, needToExecOnTask)))
                {
                    // Wait だと例外が AggregateException に包まれ、PasswordLockedException が呼び出し側で区別できない。元の例外のまま投げ直す
                    return task.GetAwaiter().GetResult();
                }
            }
            else return getSevenZipArchiveEntry_Base(neededFileInfos, format, sevenZipPath, needToExecOnTask);
        }

        private List<PackedImageEntry> getSevenZipArchiveEntry_Base(PackedImageLoaderFileInfo neededFileInfos, KnownSevenZipFormat format, string sevenZipPath, bool needToExecOnTask)
        {
            if (sevenZipArchive == null)
            {
                sevenZipArchive = stream != null ? new ArchiveFile(stream, format, sevenZipPath) : new ArchiveFile(path, sevenZipPath);
            }

            // RAR5 の Solid はパフォーマンスに甚大な影響は与えないので明示的なキャッシングは行わない

            //var sizeBound = long.MaxValue;
            //long totalSize = 0;
            
            //System.Windows.Forms.MessageBox.Show($"get entries");
            sevenZipArchiveEntries = sevenZipArchive.Entries.ToArray();
            //System.Windows.Forms.MessageBox.Show("got entries");
            var result = new List<PackedImageEntry>();
            var length = sevenZipArchiveEntries.Length;
            var needTime = (neededFileInfos & PackedImageLoaderFileInfo.LastWriteTime) == PackedImageLoaderFileInfo.LastWriteTime;
            var needSize = (neededFileInfos & PackedImageLoaderFileInfo.Size) == PackedImageLoaderFileInfo.Size;
            var ffmpegEixsts = MovieThumbnailLoader.ffmpegExists();
            for (var i = 0; i < length; i++)
            {
                var entry = sevenZipArchiveEntries[i];
                var size = entry.Size;
                var path = entry.FileName;
                if (ImageLoader.SupportsFullReading(path, ffmpegEixsts))
                {
                    throwIfEncryptedRar4Entry(format, entry);
                    DateTime dateTime;
                    if (needTime)
                    {
                        var dateTime_ = entry.LastWriteTime;
                        if (dateTime_ != null) dateTime = (DateTime)dateTime_;
                        else dateTime = DateTime.MinValue;
                    }
                    else
                    {
                        dateTime = DateTime.MinValue;
                    }
                    result.Add(new PackedImageEntry(-1, i, path, dateTime, needSize ? size : -1));
                }
            }

            var h = leftHierarchies - 1;
            if (h >= 0 && (searchMode == PackedImageLoaderSearchMode.Full || result.Count == 0))
            {
                var childrenList = new List<Tuple<PackedImageLoader, List<PackedImageEntry>>>();
                foreach (var entry in sevenZipArchiveEntries)
                {
                    var name = entry.FileName;
                    if (Supports(name, forFile: false))
                    {
                        var res = addChildFile(result, childrenList, h, openSevenZipStream(entry), entry.Size, name, neededFileInfos, needToExecOnTask);
                        if (searchMode == PackedImageLoaderSearchMode.UntilFoundItem && res > 0) break;
                    }
                }
                children = childrenList.ToArray();
            }

            return result;
        }

        private bool hasAnyPdfEntry()
        {
            try
            {
                //if (pdfArchive == null) pdfArchive = new PdfReader(path, null, true);
                if (pdfArchive == null) pdfArchive = GetPdfReader(stream, null);
                return pdfArchive.NumberOfPages > 0;
            }
            catch (iTextSharp.text.exceptions.BadPasswordException)
            {
                throw new PasswordLockedException();
            }
            catch
            {
                throw new NotSupportedException();
            }
        }

        private List<PackedImageEntry> getPdfEntries()
        {
            try
            {
                //if (pdfArchive == null) pdfArchive = new PdfReader(path, null, true);
                if (pdfArchive == null) pdfArchive = GetPdfReader(stream, null);
                var result = new List<PackedImageEntry>();
                var length = pdfArchive.NumberOfPages;

                for (var i = 0; i < length; i++)
                {
                    result.Add(new PackedImageEntry(-1, i, (i + 1).ToString()));
                }

                return result;
            }
            catch(iTextSharp.text.exceptions.BadPasswordException)
            {
                throw new PasswordLockedException();
            }
            catch// (Exception e)
            {
                throw new NotSupportedException();
            }
        }

        private PdfReader GetPdfReader(Stream isp, byte[] ownerPassword)
        {
            return PdfReaderBaseConstructor(
                byteSource: new MyRandomAccessSource(isp),
                partialRead: true,
                ownerPassword: ownerPassword,
                certificate: null,
                certificateKey: null,
                closeSourceOnConstructorError: false
            );
        }

        private static PdfReader PdfReaderBaseConstructor(
            iTextSharp.text.io.IRandomAccessSource byteSource,
            bool partialRead,
            byte[] ownerPassword,
            Org.BouncyCastle.X509.X509Certificate certificate,
            Org.BouncyCastle.Crypto.ICipherParameters certificateKey,
            bool closeSourceOnConstructorError)
        {
            return PdfReaderBaseConstructorInfo.Invoke(new object[] {
                byteSource,
                partialRead,
                ownerPassword,
                certificate,
                certificateKey,
                closeSourceOnConstructorError
            }) as PdfReader;
        }

        private static readonly System.Reflection.ConstructorInfo PdfReaderBaseConstructorInfo = GetPdfReaderBaseConstructorInfo(); // スレッドセーフ
        private static System.Reflection.ConstructorInfo GetPdfReaderBaseConstructorInfo()
        {
            return typeof(PdfReader).GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new Type[] {
                typeof(iTextSharp.text.io.IRandomAccessSource),
                typeof(bool),
                typeof(byte[]),
                typeof(Org.BouncyCastle.X509.X509Certificate),
                typeof(Org.BouncyCastle.Crypto.ICipherParameters),
                typeof(bool)
            }, null);
        }
        
        public Stream GetStream(PackedImageEntry entry, out string extensionWithPeriodForPDF, bool callback = false)
        {
            var cIndex = entry.ChildIndex;
            if (cIndex < 0)
            {
                return GetStream(entry.Index, out extensionWithPeriodForPDF, callback);
            }
            else
            {
                var t = children[cIndex];
                return t.Item1.GetStream(t.Item2[entry.Index], out extensionWithPeriodForPDF, callback);
            }
        }

        private Stream GetStream(int physicalIndex, out string extensionWithPeriodForPDF, bool callback)
        {
            extensionWithPeriodForPDF = null;
            switch (Type)
            {
                case PackType.ZipM:
                case PackType.Rar5:
                case PackType.Lha:
                    return GetInnerImageStreamSevenZip(physicalIndex, seekToBegin: true, callback: callback);
                case PackType.Pdf:
                    Bitmap dummy;
                    pdfImageMustBeConvert = true; // コンバートが不要な場合得られるバイナリに違いはないので GetStream の場合は常に true に
                    return GetInnerImageStreamPdf(physicalIndex, getImage: false, extensionWithPeriodForPDF: out extensionWithPeriodForPDF, image: out dummy);
                default:
                    if (sevenZipArchive != null)
                    {
                        return GetInnerImageStreamSevenZip(physicalIndex, seekToBegin: true, callback: callback);
                    }
                    else
                    {
                        switch (Type)
                        {
                            case PackType.Zip:
                                if (zipArchive != null)
                                {
                                    return GetInnerImageStream(physicalIndex);
                                }
                                else
                                {
                                    return GetInnerImageStreamSharpCompress(physicalIndex);
                                }
                            case PackType.Rar:
                            case PackType.Tar:
                            case PackType.SevenZip:
                                //case PackType.Gz:
                                return GetInnerImageStreamSharpCompress(physicalIndex);
                        }
                    }
                    throw new NotSupportedException();
            }

        }

        /// <summary>
        /// entry がフォルダの中のファイルで、書庫を通らずに直接読めるか（X2）。
        /// フォルダの中の書庫のエントリは子のローダー（スレッド安全ではない）を通るので false
        /// </summary>
        public bool ReadsEntryFileDirectly(PackedImageEntry entry)
        {
            var cIndex = entry.ChildIndex;
            if (cIndex < 0) return Type == PackType.Directory;
            var t = children[cIndex];
            return t.Item1.ReadsEntryFileDirectly(t.Item2[entry.Index]);
        }

        public Bitmap OpenImageStream(PackedImageEntry entry)
        {
            var cIndex = entry.ChildIndex;
            if (cIndex < 0)
            {
                return OpenImageStream(entry.Index);
            }
            else
            {
                var t = children[cIndex];
                return t.Item1.OpenImageStream(t.Item2[entry.Index]);
            }
        }

        private Bitmap OpenImageStream(int physicalIndex)
        {
            switch (Type)
            {
                case PackType.Directory:
                    var path = dirEntries[physicalIndex];
                    return ImageLoader.GetFullBitmap(path, animation);
                case PackType.ZipM:
                case PackType.Rar5:
                case PackType.Lha:
                    return OpenInnerImageStreamSevenZip(physicalIndex);
                case PackType.Pdf:
                    return OpenInnerImageStreamPdf(physicalIndex);
                default:
                    if (sevenZipArchive != null)
                    {
                        return OpenInnerImageStreamSevenZip(physicalIndex);
                    }
                    else
                    {
                        switch (Type)
                        {
                            case PackType.Zip:
                            case PackType.Rar:
                            case PackType.Tar:
                            case PackType.SevenZip:
                                //case PackType.Gz:
                                {
                                    // 展開のストリームはシークできず長さも伝わらないので、デコード側で倍々に伸びる MemoryStream に写されていた。
                                    // 長さぴったりのメモリに取り出してからデコードする（D7。ビューアの V1 と同じ経路）
                                    MemoryStream extracted;
                                    string fileNameForType;
                                    long length;
                                    if (Type == PackType.Zip && zipArchive != null) ExtractZip(physicalIndex, out extracted, out fileNameForType, out length);
                                    else ExtractSharpCompress(physicalIndex, out extracted, out fileNameForType, out length);
                                    using (extracted)
                                    {
                                        return DecodeExtracted(extracted, fileNameForType, animation, length);
                                    }
                                }
                        }
                    }



                    throw new NotSupportedException();
            }
        }

        /// <summary>アニメーションを読むか（取り出したエントリを呼び出し側でデコードするとき用）</summary>
        public bool Animation => animation;

        /// <summary>
        /// エントリを書庫からメモリに取り出すだけで、デコードはしない（V1）。デコードは DecodeExtracted で書庫のロックの外で行える。
        /// 取り出しとデコードを分けられない形式（フォルダ・PDF・CAB）は false を返す（従来どおり OpenImageStream を使う）。
        /// このクラスはスレッド安全ではないので、呼び出し側が OpenImageStream と同じく直列にすること
        /// </summary>
        public bool TryExtractEntry(PackedImageEntry entry, out MemoryStream stream, out string fileNameForType, out long length)
        {
            var cIndex = entry.ChildIndex;
            if (cIndex < 0)
            {
                return TryExtractEntry(entry.Index, out stream, out fileNameForType, out length);
            }
            else
            {
                var t = children[cIndex];
                return t.Item1.TryExtractEntry(t.Item2[entry.Index], out stream, out fileNameForType, out length);
            }
        }

        private bool TryExtractEntry(int physicalIndex, out MemoryStream stream, out string fileNameForType, out long length)
        {
            stream = null;
            fileNameForType = null;
            length = -1;
            switch (Type)
            {
                case PackType.Pdf:
                    {
                        // そのまま JPEG（D2）・JPEG 2000（D6）として読めるページだけ、バイト列を取り出す（デコードは呼び出し側がロックの外で並列に行う）
                        var bytes = TryGetPdfPlainJpegBytes(physicalIndex, allowJpx: true, out var isJpx);
                        if (bytes == null) return false;
                        stream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
                        fileNameForType = isJpx ? PdfJpxName : PdfPlainJpegName;
                        length = bytes.Length;
                        return true;
                    }
                case PackType.Directory:
                    return false;
                case PackType.ZipM:
                case PackType.Rar5:
                case PackType.Lha:
                    ExtractSevenZip(physicalIndex, out stream, out fileNameForType, out length);
                    return true;
                default:
                    if (sevenZipArchive != null)
                    {
                        ExtractSevenZip(physicalIndex, out stream, out fileNameForType, out length);
                        return true;
                    }
                    switch (Type)
                    {
                        case PackType.Zip:
                            if (zipArchive != null)
                            {
                                ExtractZip(physicalIndex, out stream, out fileNameForType, out length);
                            }
                            else
                            {
                                ExtractSharpCompress(physicalIndex, out stream, out fileNameForType, out length);
                            }
                            return true;
                        case PackType.Rar:
                        case PackType.Tar:
                        case PackType.SevenZip:
                            ExtractSharpCompress(physicalIndex, out stream, out fileNameForType, out length);
                            return true;
                    }
                    return false;
            }
        }

        private void ExtractSevenZip(int physicalIndex, out MemoryStream stream, out string fileNameForType, out long length)
        {
            var entry = sevenZipArchiveEntries[physicalIndex];
            stream = GetInnerImageStreamSevenZip(physicalIndex, entry, seekToBegin: true, callback: false);
            fileNameForType = entry.FileName;
            length = entry.Size;
        }

        private static MemoryStream CopyToMemory(Stream source, long length)
        {
            var result = length >= 0 && length <= int.MaxValue ? new MemoryStream((int)length) : new MemoryStream();
            try
            {
                source.CopyTo(result);
                result.Position = 0;
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private void ExtractZip(int physicalIndex, out MemoryStream stream, out string fileNameForType, out long length)
        {
            var entry = zipArchiveEntries[physicalIndex];
            try
            {
                using (var s = entry.Open())
                {
                    stream = CopyToMemory(s, entry.Length);
                }
                fileNameForType = entry.Name;
                length = entry.Length;
            }
            catch (InvalidDataException e)
            {
                // AES などは Open で、ZipCrypto は展開の途中で InvalidDataException になる。展開の途中の例外はデータの破損でも起きるので、
                // 暗号化の印のないエントリは普通の読み込みの失敗にする（Y23。D7 で展開をデコードの前に移してから、破損もパスワード付きの扱いになっていた）
                if (IsZipEntryEncrypted(entry) == false) throw new ArgumentException("The zip entry data is corrupted.", e);
                throw new PasswordLockedException();
            }
        }

        // zip のエントリが暗号化されているか（汎用ビットフラグの bit 0）。.NET Framework の ZipArchiveEntry は公開していないので非公開のフィールドを読む。
        // 読めなければ null（Y23）
        private static readonly System.Reflection.FieldInfo zipEntryGeneralPurposeBitFlagField =
            typeof(ZipArchiveEntry).GetField("_generalPurposeBitFlag", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static bool? IsZipEntryEncrypted(ZipArchiveEntry entry)
        {
            try
            {
                var value = zipEntryGeneralPurposeBitFlagField?.GetValue(entry);
                if (value == null) return null;
                return (Convert.ToInt32(value) & 1) != 0;
            }
            catch
            {
                return null;
            }
        }

        private void ExtractSharpCompress(int physicalIndex, out MemoryStream stream, out string fileNameForType, out long length)
        {
            try
            {
                var entry = iArchiveEntries[physicalIndex];
                fileNameForType = entry.Key;
                length = entry.Size;
                if (!OpenInnerImageStreamSharpCompress_IsSolid)
                {
                    try
                    {
                        using (var s = entry.OpenEntryStream())
                        {
                            stream = CopyToMemory(s, entry.Size);
                        }
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        OpenInnerImageStreamSharpCompress_IsSolid = true;
                        initializeSolidEntryOccurrences();
                    }
                }
                var bytes = getSolidEntryBytes(physicalIndex);
                stream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
                length = bytes.Length;
            }
            catch (Exception e)
            {
                if (e.ToString().Contains("Crypto"))
                {
                    throw new PasswordLockedException();
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>TryExtractEntry で取り出したエントリをデコードする（OpenImageStream と同じ処理。書庫のロックは不要）（V1）</summary>
        public static Bitmap DecodeExtracted(Stream stream, string fileNameForType, bool animation, long length)
        {
            return ImageFromStream(stream, fileNameForType, animation, length);
        }

        private Stream GetInnerImageStream(int physicalIndex)
        {
            try
            {
                var entry = zipArchiveEntries[physicalIndex];
                return entry.Open();
            }
            catch (InvalidDataException)
            {
                throw new PasswordLockedException();
            }
        }


        private bool OpenInnerImageStreamSharpCompress_IsSolid = false;
        private Stream GetInnerImageStreamSharpCompress(int physicalIndex)
        {
            try
            {
                if (!OpenInnerImageStreamSharpCompress_IsSolid)
                {
                    var entry = iArchiveEntries[physicalIndex];
                    try
                    {
                        return entry.OpenEntryStream();
                    }
                    catch (InvalidOperationException)
                    {
                        OpenInnerImageStreamSharpCompress_IsSolid = true;
                        initializeSolidEntryOccurrences();
                    }
                }

                // キャッシュの配列は書き換えないので、コピーせず読み取り専用で包んで返す（B2e）
                var bytes = getSolidEntryBytes(physicalIndex);
                return new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
            }
            catch (Exception e)
            {
                if (e.ToString().Contains("Crypto"))
                {
                    throw new PasswordLockedException();
                }
                else
                {
                    throw;
                }
            }
        }

        private void initializeSolidEntryOccurrences()
        {
            if (solidEntryOccurrences != null) return;
            solidEntryOccurrences = new int[iArchiveEntries.Length];
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < iArchiveEntries.Length; i++)
            {
                if (iArchiveEntries[i].IsDirectory) continue;
                var key = iArchiveEntries[i].Key ?? string.Empty;
                int count;
                occurrences.TryGetValue(key, out count);
                solidEntryOccurrences[i] = count;
                occurrences[key] = count + 1;
            }
            solidIndexByKey = new Dictionary<string, int[]>(StringComparer.Ordinal);
            foreach (var pair in occurrences) solidIndexByKey[pair.Key] = new int[pair.Value];
            for (var i = 0; i < iArchiveEntries.Length; i++)
            {
                if (iArchiveEntries[i].IsDirectory) continue;
                solidIndexByKey[iArchiveEntries[i].Key ?? string.Empty][solidEntryOccurrences[i]] = i;
            }
        }

        /// <summary>
        /// solid 書庫のエントリを先頭から順に読む共有リーダーで取り出す（B2c）。
        /// 取り出しの途中で例外が起きた場合、リーダーは途中まで読んだ・壊れた状態のまま残るので、
        /// 次の要求で必ず作り直させる（画像のデコードで起きた例外はリーダーに影響しないので対象外）。
        /// </summary>
        private byte[] getSolidEntryBytes(int physicalIndex)
        {
            applyConsumedEntries(physicalIndex);
            byte[] cached;
            if (solidImageCache != null && solidImageCache.TryGetValue(physicalIndex, out cached)) return cached;
            try
            {
                byte[] bytes;
                using (var entryStream = openSolidEntryStream(physicalIndex))
                {
                    bytes = readAllSolidEntryBytes(entryStream, iArchiveEntries[physicalIndex].Size);
                }
                addToSolidCache(physicalIndex, bytes, physicalIndex);
                return bytes;
            }
            catch
            {
                // openSolidEntryStream は physicalIndex <= solidLastRequestedIndex のときリーダーを作り直す
                solidLastRequestedIndex = int.MaxValue;
                throw;
            }
        }

        private static byte[] readAllSolidEntryBytes(Stream entryStream, long size)
        {
            using (var ms = size >= 0 && size <= int.MaxValue ? new MemoryStream((int)size) : new MemoryStream())
            {
                entryStream.CopyTo(ms);
                return ms.Capacity == ms.Length ? ms.GetBuffer() : ms.ToArray();
            }
        }

        // solid キャッシュの上限: 物理メモリの 1/16（最低 64MB、最大 512MB）
        private static readonly long SolidCacheBudget = getSolidCacheBudget();
        private static long getSolidCacheBudget()
        {
            try
            {
                var total = (long)new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory;
                return Math.Max(64L << 20, Math.Min(512L << 20, total / 16));
            }
            catch
            {
                return 256L << 20;
            }
        }

        /// <summary>
        /// 展開済みのエントリをキャッシュに入れる。あふれたら anchorIndex（今要求されているページ）から遠いものから捨てる（B2b）
        /// </summary>
        private void addToSolidCache(int physicalIndex, byte[] bytes, int anchorIndex)
        {
            if (bytes.Length > SolidCacheBudget / 4) return;
            if (solidImageCache == null) solidImageCache = new Dictionary<int, byte[]>();
            if (solidImageCache.ContainsKey(physicalIndex)) return;
            var myDistance = Math.Abs(physicalIndex - anchorIndex);
            while (solidImageCacheBytes + bytes.Length > SolidCacheBudget && solidImageCache.Count > 0)
            {
                var farthest = -1; var farthestDistance = -1;
                foreach (var key in solidImageCache.Keys)
                {
                    var d = Math.Abs(key - anchorIndex);
                    if (d > farthestDistance) { farthestDistance = d; farthest = key; }
                }
                // 入れようとしているものの方が遠ければ入れない
                if (farthestDistance <= myDistance) return;
                solidImageCacheBytes -= solidImageCache[farthest].Length;
                solidImageCache.Remove(farthest);
            }
            solidImageCache[physicalIndex] = bytes;
            solidImageCacheBytes += bytes.Length;
        }

        private void resetSolidReader()
        {
            if (iReader != null)
            {
                // 例外で壊れたリーダーは Dispose でも例外を出すことがあるが、作り直しは続ける
                try { iReader.Dispose(); } catch { }
                iReader = null;
                if (stream == null)
                {
                    try { iArchive.Dispose(); } catch { }
                    iArchive = ArchiveFactory.Open(path);
                    iArchiveEntries = iArchive.Entries.ToArray();
                }
            }
            iReader = iArchive.ExtractAllEntries();
            solidReaderOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            solidLastRequestedIndex = -1;
        }

        private Stream openSolidEntryStream(int physicalIndex)
        {
            if (iReader == null || physicalIndex <= solidLastRequestedIndex) resetSolidReader();

            // The 7z reader can enumerate entries in a different order from the archive index.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                while (iReader.MoveToNextEntry())
                {
                    if (iReader.Entry.IsDirectory) continue;
                    var key = iReader.Entry.Key ?? string.Empty;
                    int occurrence;
                    solidReaderOccurrences.TryGetValue(key, out occurrence);
                    solidReaderOccurrences[key] = occurrence + 1;
                    if (key == (iArchiveEntries[physicalIndex].Key ?? string.Empty) && occurrence == solidEntryOccurrences[physicalIndex])
                    {
                        solidLastRequestedIndex = physicalIndex;
                        return iReader.OpenEntryStream();
                    }
                    // 目的のエントリまで読み進める途中のエントリも、どのみち展開するのでキャッシュに入れておく。
                    // これで戻る方向に読んだときに先頭から展開し直さずに済む（B2a）
                    cachePassedSolidEntry(key, occurrence, physicalIndex);
                }
                if (attempt == 0) resetSolidReader();
            }
            throw new InvalidDataException("The archive entry could not be found during solid extraction.");
        }

        private void cachePassedSolidEntry(string key, int occurrence, int anchorIndex)
        {
            int[] indices;
            if (solidIndexByKey == null || !solidIndexByKey.TryGetValue(key, out indices) || occurrence >= indices.Length) return;
            var index = indices[occurrence];
            if (solidImageCache != null && solidImageCache.ContainsKey(index)) return;
            if (isConsumedFarEntry(index, anchorIndex)) return; // 縮小画像ができていて遠いページは持たない（D9）
            if (!ImageLoader.SupportsFullReading(key, ffmpegExists: true)) return; // 画像以外は読み飛ばす
            var size = iReader.Entry.Size;
            if (size > SolidCacheBudget / 4) return;
            byte[] bytes;
            using (var entryStream = iReader.OpenEntryStream())
            {
                bytes = readAllSolidEntryBytes(entryStream, size);
            }
            addToSolidCache(index, bytes, anchorIndex);
        }

        private Bitmap OpenInnerImageStreamSevenZip(int physicalIndex)
        {
            var entry = sevenZipArchiveEntries[physicalIndex];
            using (var ms = GetInnerImageStreamSevenZip(physicalIndex, entry, seekToBegin: true, callback: false))
            {
                return ImageFromStream(ms, entry.FileName, animation, entry.Size);
            }
        }

        private MemoryStream GetInnerImageStreamSevenZip(int physicalIndex, bool seekToBegin, bool callback)
        {
            return GetInnerImageStreamSevenZip(physicalIndex, sevenZipArchiveEntries[physicalIndex], seekToBegin, callback);
        }

        private MemoryStream GetInnerImageStreamSevenZip(int physicalIndex, Entry entry, bool seekToBegin, bool callback)
        {
            // solid 書庫（RAR5 など）は、7z.dll に1件ずつ展開させるとブロックの先頭から毎回展開し直すので、
            // 前後のページもまとめて展開してキャッシュする（B1）
            if (sevenZipArchive != null && sevenZipArchiveEntries != null && entry == sevenZipArchiveEntries[physicalIndex] && isSevenZipSolid())
            {
                var bytes = getSevenZipSolidEntryBytes(physicalIndex, callback);
                return new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
            }

            var ms = entry.Size >= 0 && entry.Size <= int.MaxValue ? new MemoryStream((int)entry.Size) : new MemoryStream();
            try
            {
                if(callback)
                {
                    SevenZipEntryExtractInTask(entry, ms);
                }
                else
                {
                    entry.Extract(ms);
                }
                
                if (seekToBegin) ms.Seek(0, SeekOrigin.Begin);
                return ms;
            }
            catch
            {
                ms.Dispose();
                throw;
            }
        }

        private bool? sevenZipSolid;
        private bool isSevenZipSolid()
        {
            if (sevenZipSolid == null) sevenZipSolid = sevenZipArchive.IsSolid;
            return (bool)sevenZipSolid;
        }

        // まとめて展開する範囲（要求されたページの前後）
        private const int SevenZipSolidBatchBehind = 16, SevenZipSolidBatchAhead = 64;
        private int sevenZipSolidExtractCount = 0;

        private byte[] getSevenZipSolidEntryBytes(int physicalIndex, bool inTask)
        {
            applyConsumedEntries(physicalIndex);
            byte[] cached;
            if (solidImageCache != null && solidImageCache.TryGetValue(physicalIndex, out cached)) return cached;

            // 要求されたページと、前後のまだキャッシュにない画像エントリをまとめて展開する。
            // ただしこの書庫で最初の展開は1件だけにする（サムネイル作成のように1ページしか読まない場合に無駄が出ないように）
            var targets = new List<int>();
            var batch = sevenZipSolidExtractCount++ > 0;
            var start = batch ? Math.Max(0, physicalIndex - SevenZipSolidBatchBehind) : physicalIndex;
            var stop = batch ? Math.Min(sevenZipArchiveEntries.Length, physicalIndex + SevenZipSolidBatchAhead + 1) : physicalIndex + 1;
            for (var i = start; i < stop; i++)
            {
                if (i != physicalIndex)
                {
                    var e = sevenZipArchiveEntries[i];
                    if (e.IsFolder || (solidImageCache != null && solidImageCache.ContainsKey(i))) continue;
                    if (isConsumedFarEntry(i, physicalIndex)) continue; // 縮小画像ができていて遠いページはまとめて展開しない（D9）
                    if (!ImageLoader.SupportsFullReading(e.FileName, ffmpegExists: true)) continue;
                    var s = e.Size;
                    if (s < 0 || s > SolidCacheBudget / 4) continue;
                }
                targets.Add(i);
            }
#if PERFLOG
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "solid.log"), $"req={physicalIndex}	targets={targets.Count}	min={(targets.Count > 0 ? targets.Min() : -1)}	max={(targets.Count > 0 ? targets.Max() : -1)}	cached={solidImageCache?.Count ?? 0}" + Environment.NewLine); } catch { }
#endif
            var streams = new MemoryStream[targets.Count];
            Action extract = () => Entry.ExtractMany(targets.Select(i => sevenZipArchiveEntries[i]).ToList(), k =>
            {
                var size = sevenZipArchiveEntries[targets[k]].Size;
                return streams[k] = size >= 0 && size <= int.MaxValue ? new MemoryStream((int)size) : new MemoryStream();
            },
            // CRC エラー・データの破損などのエントリは、壊れたバイト列をキャッシュに入れない（要求されたページなら下で例外になる）
            k => streams[k] = null);
            if (inTask)
            {
                using (var task = Task.Run(extract))
                {
                    task.Wait();
                }
            }
            else
            {
                extract();
            }

            byte[] result = null;
            for (var k = 0; k < targets.Count; k++)
            {
                var ms = streams[k];
                if (ms == null) continue;
                var bytes = ms.Capacity == ms.Length ? ms.GetBuffer() : ms.ToArray();
                if (targets[k] == physicalIndex) result = bytes;
                addToSolidCache(targets[k], bytes, physicalIndex);
            }
            if (result == null) throw new IOException($"7z.dll did not extract the entry: {sevenZipArchiveEntries[physicalIndex].FileName}");
            return result;
        }

        private static void SevenZipEntryExtractInTask(Entry entry, Stream dst)
        {

            using (var task = Task.Run(() => entry.Extract(dst)))
            {
                task.Wait();
                var e = task.Exception;
                if (e != null) throw e;
            }
        }

        private Bitmap OpenInnerImageStreamPdf(int physicalIndex)
        {
            // そのまま JPEG として読めるページは、JPEG のデコード（縮小デコードを含む）で読む（D2。以前は new Bitmap で常に原寸）
            var jpeg = TryGetPdfPlainJpegBytes(physicalIndex, allowJpx: false, out _);
            if (jpeg != null)
            {
                try
                {
                    using (var ms = new MemoryStream(jpeg, writable: false))
                    {
                        return ImageFromStream(ms, PdfPlainJpegName, animation, jpeg.Length);
                    }
                }
                catch (ArgumentException) { } // 従来の読み方で読み直す
            }

            Bitmap result;
            GetInnerImageStreamPdf(physicalIndex, getImage: true, extensionWithPeriodForPDF: out var dummy, image: out result).Dispose();
            //using (var ms = GetInnerImageStreamPdf(physicalIndex))
            {
                // pdf ↓ が必要なデータが入っていることは想定できず拡張子が分からないデメリットに見合わない
                //result0 = ImageFromStream(stream, filePathForGetFileType: null);
                if (result != null)
                {
                    return result;
                }
                else
                {
                    throw new ArgumentException();
                }
            }
        }


        private MemoryStream GetInnerImageStreamPdf(int physicalIndex, bool getImage, out string extensionWithPeriodForPDF, out Bitmap image)
        {
            try
            {
                //var sw = new System.Diagnostics.Stopwatch(); var s = "";  sw.Start();

                var page = pdfArchive.GetPageN(physicalIndex + 1); // ページは１オリジン
                var res = PdfReader.GetPdfObject(page.Get(PdfName.RESOURCES)) as PdfDictionary;
                var xobj = PdfReader.GetPdfObject(res.Get(PdfName.XOBJECT)) as PdfDictionary;
                if (xobj == null)
                {
                    throw new ArgumentException();
                }
                var cb = xobj.Keys.Count;

                //sw.Stop(); s += $"{sw.Elapsed}\n"; sw.Restart();

                foreach (var name in xobj.Keys)
                {
                    var obj = xobj.Get(name);
                    if (!obj.IsIndirect()) continue;

                    var tg = PdfReader.GetPdfObject(obj) as PdfDictionary;
                    var type = PdfReader.GetPdfObject(tg.Get(PdfName.SUBTYPE)) as PdfName;
                    if (!PdfName.IMAGE.Equals(type)) continue;

                    int XrefIndex = (obj as PRIndirectReference).Number;
                    var pdfStream = pdfArchive.GetPdfObject(XrefIndex) as PRStream;

                    if (!pdfImageMustBeConvert)
                    {
                        try
                        {
                            var stream = new MemoryStream(PdfReader.GetStreamBytesRaw(pdfStream));
                            try
                            {
                                if (getImage)
                                {
                                    image = new Bitmap(stream);
                                    extensionWithPeriodForPDF = null;
                                }
                                else
                                {
                                    image = null;
                                    stream = GetImageType(stream, out extensionWithPeriodForPDF);
                                }
                                return stream;
                            }
                            catch
                            {
                                stream.Close();
                                stream.Dispose();
                                throw;
                            }
                        }
                        catch (ArgumentException)
                        {
                            pdfImageMustBeConvert = true;
                        }

                    }

                    MemoryStream result;
                    RiosFastPdfImageDecoder.Type imageType;
                    if (getImage)
                    {
                        // Flate・CCITT などのグレー・RGB・インデックスの画像は、PNG に作り直さずに Bitmap を直接作る（D4）。
                        // 作れなければ同じ展開結果から PNG・TIFF を作る（Y12。以前は最初から展開し直していた）
                        var direct = RiosFastPdfImageDecoder.DecodeToBitmapOrStream(pdfStream, out result, out imageType);
                        if (direct != null)
                        {
                            image = direct;
                            extensionWithPeriodForPDF = null;
                            return new MemoryStream();
                        }
                    }
                    else
                    {
                        result = RiosFastPdfImageDecoder.Decode(pdfStream, out imageType);
                    }
                    if (getImage)
                    {
                        extensionWithPeriodForPDF = null;
                        try
                        {
                            switch (imageType)
                            {
                                case RiosFastPdfImageDecoder.Type.JP2:
#if DEBUG
                                    //using (var fs = new FileStream("x.jp2", FileMode.CreateNew, FileAccess.Write)) result.CopyTo(fs);
                                    //result.Seek(0, SeekOrigin.Begin);
#endif
                                    image = ImageLoader.GetFullBitmap(result, "x.jp2");
                                    break;
                                default:
                                    image = new Bitmap(result);
                                    break;
                            }
                        }
                        catch
                        {
                            result.Dispose();
                            throw;
                        }
                    }
                    else
                    {
                        image = null;
                        switch (imageType)
                        {
                            case RiosFastPdfImageDecoder.Type.JP2:
                                extensionWithPeriodForPDF = ".jp2";
                                break;
                            case RiosFastPdfImageDecoder.Type.JPG:
                                extensionWithPeriodForPDF = ".jpg";
                                break;
                            case RiosFastPdfImageDecoder.Type.PNG:
                                extensionWithPeriodForPDF = ".png";
                                break;
                            default:
                                extensionWithPeriodForPDF = ".tiff";
                                break;
                        }
                    }

                    return result;
                }
            }
            catch (iTextSharp.text.exceptions.BadPasswordException)
            {
                throw new PasswordLockedException();
            }
            catch (Exception ex)
            {
                throw new NotSupportedException(null, ex);
            }

            throw new ArgumentException();
        }

        // PDF から取り出した JPEG を読むときの名前（形式の判定にだけ使う、D2）
        private const string PdfPlainJpegName = "x.jpg";
        // PDF の JPXDecode の画像を読むときの名前。従来の読み方（RiosFastPdfImageDecoder → GetFullBitmap(…, "x.jp2")）と同じ（D6）
        private const string PdfJpxName = "x.jp2";

        /// <summary>
        /// PDF のページの画像（GetInnerImageStreamPdf が読むのと同じ、最初の画像の XObject）が、そのまま JPEG として読めるものなら、
        /// そのバイト列を返す。読めなければ null（従来の読み方を使う）（D2）。
        /// 表示が変わらないように、DCTDecode だけで圧縮され、/Decode（値の反転）がなく、色空間が DeviceRGB・DeviceGray・1 か 3 成分の ICCBased で、
        /// Exif（APP1）を持たない JPEG に限る（JPEG のデコードは Exif の向きで回転するが、PDF では無視するのが正しい）。
        /// このクラスはスレッド安全ではないので、呼び出し側が直列にすること
        /// </summary>
        private byte[] TryGetPdfPlainJpegBytes(int physicalIndex, bool allowJpx, out bool isJpx)
        {
            isJpx = false;
            try
            {
                var page = pdfArchive.GetPageN(physicalIndex + 1); // ページは１オリジン
                var res = PdfReader.GetPdfObject(page.Get(PdfName.RESOURCES)) as PdfDictionary;
                var xobj = res == null ? null : PdfReader.GetPdfObject(res.Get(PdfName.XOBJECT)) as PdfDictionary;
                if (xobj == null) return null;
                foreach (var name in xobj.Keys)
                {
                    var obj = xobj.Get(name);
                    if (!obj.IsIndirect()) continue;
                    var tg = PdfReader.GetPdfObject(obj) as PdfDictionary;
                    if (tg == null) continue;
                    var type = PdfReader.GetPdfObject(tg.Get(PdfName.SUBTYPE)) as PdfName;
                    if (!PdfName.IMAGE.Equals(type)) continue;

                    // 最初の画像だけを見る（GetInnerImageStreamPdf と同じ）
                    if (allowJpx && IsOnlyJpxImageDictionary(tg) && ImageLoader.Jpeg2000DecodesWithFfmpeg("jp2"))
                    {
                        // JPXDecode だけの画像は、従来どおり生のバイト列を jp2 として読む（ffmpeg は別のプロセスなので並列に読める、D6）
                        var jpxStream = pdfArchive.GetPdfObject((obj as PRIndirectReference).Number) as PRStream;
                        if (jpxStream == null) return null;
                        isJpx = true;
                        return PdfReader.GetStreamBytesRaw(jpxStream);
                    }
                    if (!IsPlainJpegImageDictionary(tg)) return null;
                    var pdfStream = pdfArchive.GetPdfObject((obj as PRIndirectReference).Number) as PRStream;
                    if (pdfStream == null) return null;
                    var bytes = PdfReader.GetStreamBytesRaw(pdfStream);
                    return IsJpegWithoutExif(bytes) ? bytes : null;
                }
                return null;
            }
            catch (iTextSharp.text.exceptions.BadPasswordException)
            {
                throw new PasswordLockedException();
            }
            catch
            {
                return null;
            }
        }

        // フィルタが JPXDecode だけか（D6）
        private static bool IsOnlyJpxImageDictionary(PdfDictionary image)
        {
            var filter = PdfReader.GetPdfObject(image.Get(PdfName.FILTER));
            if (filter is PdfArray filters)
            {
                if (filters.Size != 1) return false;
                filter = PdfReader.GetPdfObject(filters[0]);
            }
            return PdfName.JPXDECODE.Equals(filter);
        }

        private static bool IsPlainJpegImageDictionary(PdfDictionary image)
        {
            var filter = PdfReader.GetPdfObject(image.Get(PdfName.FILTER));
            if (filter is PdfArray filters)
            {
                if (filters.Size != 1) return false;
                filter = PdfReader.GetPdfObject(filters[0]);
            }
            if (!PdfName.DCTDECODE.Equals(filter)) return false;
            if (image.Get(PdfName.DECODE) != null) return false;

            var colorSpace = PdfReader.GetPdfObject(image.Get(PdfName.COLORSPACE));
            if (colorSpace is PdfName csName)
            {
                return PdfName.DEVICERGB.Equals(csName) || PdfName.DEVICEGRAY.Equals(csName);
            }
            if (colorSpace is PdfArray csArray && csArray.Size == 2 && PdfName.ICCBASED.Equals(PdfReader.GetPdfObject(csArray[0])))
            {
                var icc = PdfReader.GetPdfObject(csArray[1]) as PdfDictionary;
                var n = icc == null ? null : PdfReader.GetPdfObject(icc.Get(PdfName.N)) as PdfNumber;
                return n != null && (n.IntValue == 1 || n.IntValue == 3);
            }
            return false;
        }

        // JPEG の SOI で始まり、スキャンの前に Exif（APP1）がないか
        private static bool IsJpegWithoutExif(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return false;
            var pos = 2;
            while (pos + 4 <= bytes.Length)
            {
                if (bytes[pos] != 0xFF) return false;
                var marker = bytes[pos + 1];
                if (marker == 0xFF) { pos++; continue; } // 詰め物
                if (marker == 0xDA) return true; // SOS
                var segmentLength = bytes[pos + 2] << 8 | bytes[pos + 3];
                if (segmentLength < 2) return false;
                if (marker == 0xE1 && segmentLength >= 8 && pos + 10 <= bytes.Length &&
                    bytes[pos + 4] == (byte)'E' && bytes[pos + 5] == (byte)'x' && bytes[pos + 6] == (byte)'i' && bytes[pos + 7] == (byte)'f') return false;
                pos += 2 + segmentLength;
            }
            return false;
        }

        private static MemoryStream GetImageType(MemoryStream ms, out string extension)
        {
            try
            {
                var imageOpened = false;
                try
                {
                    using (var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false)) // 検証なし
                    {
                        imageOpened = true;
                        var format = img.RawFormat;
                        if (format.Equals(ImageFormat.Jpeg))
                        {
                            extension = ".jpg";
                            return ms;
                        }
                        if (format.Equals(ImageFormat.Tiff))
                        {
                            extension = ".tiff";
                            return ms;
                        }
                        if (format.Equals(ImageFormat.Bmp))
                        {
                            extension = ".bmp";
                            return ms;
                        }
                        if (format.Equals(ImageFormat.Emf))
                        {
                            extension = ".emf";
                            return ms;
                        }
                        if (format.Equals(ImageFormat.Gif))
                        {
                            extension = ".gif";
                            return ms;
                        }
                        if (format.Equals(ImageFormat.Icon))
                        {
                            extension = ".ico";
                            return ms;
                        }
                        if (format.Equals(ImageFormat.Png))
                        {
                            extension = ".png";
                            return ms;
                        }
                        if (format.Equals(ImageFormat.Wmf))
                        {
                            extension = ".wmf";
                            return ms;
                        }
                        extension = "";
                        return ms;
                    }
                }
                finally
                {
                    if (imageOpened)
                    {
                        ms.Seek(0, SeekOrigin.Begin);
                    }
                }
            }
            catch
            {
                extension = "";
                return ms;
            }
        }

        private static Bitmap ImageFromStream(Stream stream, string filePathForGetFileType, bool animation, long streamLength = -1)
        {
            Bitmap result;
            try
            {
                //result = Image.FromStream(stream);
                result = ImageLoader.GetFullBitmap(stream, filePathForGetFileType, animation, streamLength);
            }
            catch (Exception e)
            {
                throw new ArgumentException(null, e);
            }
            if (result == null) throw new ArgumentException();
            return result;
        }

        private static bool readFully(Stream stream, byte[] buffer, int count)
        {
            var read = 0;
            while (read < count)
            {
                var r = stream.Read(buffer, read, count - read);
                if (r <= 0) return false;
                read += r;
            }
            return true;
        }

        /// <summary>
        /// zip のセントラルディレクトリを読み、すべての項目が無圧縮か Deflate か（.NET Framework の ZipArchive で読めるか）を返す。
        /// ZIP64 や、読めない・形の合わない書庫では false（従来どおり SharpCompress で読む）。stream の位置は戻さない
        /// </summary>
        private static bool zipUsesOnlyStoreOrDeflate(Stream stream)
        {
            try
            {
                const int eocdSize = 22;
                var length = stream.Length;
                if (length < eocdSize) return false;
                var tailLength = (int)Math.Min(length, eocdSize + 0xFFFF);
                var tail = new byte[tailLength];
                stream.Seek(length - tailLength, SeekOrigin.Begin);
                if (!readFully(stream, tail, tailLength)) return false;
                var eocd = -1;
                for (var i = tailLength - eocdSize; i >= 0; i--)
                {
                    if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 0x05 && tail[i + 3] == 0x06) { eocd = i; break; }
                }
                if (eocd < 0) return false;
                int u16(byte[] b, int o) => b[o] | b[o + 1] << 8;
                long u32(byte[] b, int o) => (long)b[o] | (long)b[o + 1] << 8 | (long)b[o + 2] << 16 | (long)b[o + 3] << 24;
                var entryCount = u16(tail, eocd + 10);
                var cdSize = u32(tail, eocd + 12);
                var cdOffset = u32(tail, eocd + 16);
                if (entryCount == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF) return false; // ZIP64
                if (cdSize > 64 * 1024 * 1024 || cdOffset + cdSize > length) return false;
                var cd = new byte[cdSize];
                stream.Seek(cdOffset, SeekOrigin.Begin);
                if (!readFully(stream, cd, (int)cdSize)) return false;
                var pos = 0;
                var count = 0;
                while (pos + 46 <= cd.Length)
                {
                    if (cd[pos] != 0x50 || cd[pos + 1] != 0x4B || cd[pos + 2] != 0x01 || cd[pos + 3] != 0x02) return false;
                    var method = u16(cd, pos + 10);
                    if (method != 0 && method != 8) return false;
                    pos += 46 + u16(cd, pos + 28) + u16(cd, pos + 30) + u16(cd, pos + 32);
                    count++;
                }
                return count == entryCount;
            }
            catch (Exception)
            {
                return false; // 判定できなければ従来どおり SharpCompress で読む
            }
        }

        private PackType GetTypeFromStreamAndSeekFirst(Stream stream)
        {
            const int bufferSize = 8;
            var buffer = new byte[bufferSize];
            var index = 0;
            while (index < bufferSize)
            {
                var read = stream.Read(buffer, index, bufferSize - index);
                if (read <= 0) throw new IOException($"index = {index}, stream = {stream}");
                index += read;
            }
            if (buffer[0] == 0x50 && buffer[1] == 0x4B && (buffer[2] == 0x03 && buffer[3] == 0x04 || buffer[2] == 0x07 && buffer[3] == 0x08))
            {
                /*
                UTF-8 フラグがあてにならないので直接文字コードをチェックする
                ただし UTF-8 は ASCII を包含するので一つ目のファイルが ASCII 内だが他に ASCII 外のファイルを含む場合は正しく判定できない
                最も多く想定されるのは ASCII 内のフォルダがルートに存在する場合なのでこれを回避するため
                ASCII 判定かつサイズ 0 の間は判定を繰り返す
                */
                // ローカルヘッダを順にたどる。データディスクリプタ付き（サイズが後ろにある）のエントリや、
                // 次のヘッダのシグネチャが合わないときはそこで打ち切る（X15）
                var foundUtf8 = false;
                var header = new byte[30];
                long position = buffer[2] == 0x07 ? 4 : 0; // 分割書庫の印（PK）の後ろに最初のヘッダがある
                for (var n = 0; n < 256; n++)
                {
                    stream.Seek(position, SeekOrigin.Begin);
                    if (!readFully(stream, header, header.Length)) break;
                    if (header[0] != 0x50 || header[1] != 0x4B || header[2] != 0x03 || header[3] != 0x04) break;
                    var flags = header[6] | header[7] << 8;
                    var compressedSize = header[18] | (uint)header[19] << 8 | (uint)header[20] << 16 | (uint)header[21] << 24;
                    var uncompressedSize = header[22] | (uint)header[23] << 8 | (uint)header[24] << 16 | (uint)header[25] << 24;
                    var fileNameLength = header[26] | header[27] << 8;
                    var extraFieldLength = header[28] | header[29] << 8;
                    var nameBuffer = new byte[fileNameLength];
                    if (!readFully(stream, nameBuffer, fileNameLength)) break;
                    var code = ImageLoader.GetCode(nameBuffer);
                    foundUtf8 = code == Encoding.UTF8;

#if DEBUG
                    int preambleLength = code.GetPreamble().Length;
                    var s = code.GetString(nameBuffer, preambleLength, nameBuffer.Length - preambleLength);
#endif
                    if (code != Encoding.ASCII || uncompressedSize != 0) break;
                    if ((flags & 0x8) != 0 || compressedSize == 0xffffffff) break; // 圧縮データの長さがヘッダから分からない
                    position += header.Length + fileNameLength + extraFieldLength + compressedSize;
                }
                stream.Seek(0, SeekOrigin.Begin);

                // UTF-8 の名前の zip も、ZipArchive に UTF-8 を指定すれば正しい名前で読める（UTF-8 フラグのある名前は常に UTF-8 で読まれる）。
                // SharpCompress の Deflate は managed の実装で、ZipArchive（ネイティブの zlib）の 2 倍以上遅い（70MB の書庫で 790ms→340ms）。
                // ただし .NET Framework の ZipArchive は無圧縮と Deflate しか読めないので、それ以外の方式の項目があれば従来どおり SharpCompress で読む
                if (foundUtf8 && zipUsesOnlyStoreOrDeflate(stream))
                {
                    UseZipArchiveClass = true;
                    ZipArchiveEntryNameEncoding = Encoding.UTF8;
                }
                else
                {
                    UseZipArchiveClass = !foundUtf8;
                }
                stream.Seek(0, SeekOrigin.Begin);


                return PackType.Zip;
            }
            else if (buffer[0] == 0x52 && buffer[1] == 0x61 && buffer[2] == 0x72 && buffer[3] == 0x21 && buffer[4] == 0x1A && buffer[5] == 0x07)
            {
                var buffer6 = buffer[6];
                if (buffer6 == 0x00)
                {
                    stream.Seek(-bufferSize, SeekOrigin.Current);
                    rar4HeaderEncrypted = isRar4HeaderEncrypted(stream);
                    return PackType.Rar;
                }
                else if (buffer6 == 0x01 && buffer[7] == 0x00)
                {
                    stream.Seek(-bufferSize, SeekOrigin.Current);
                    return PackType.Rar5;
                }
                return PackType.Unknown;
            }
            else if (buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46)
            {
                stream.Seek(-bufferSize, SeekOrigin.Current);
                return PackType.Pdf;
            }
            else if (buffer[0] == 0x37 && buffer[1] == 0x7A && buffer[2] == 0xBC && buffer[3] == 0xAF && buffer[4] == 0x27 && buffer[5] == 0x1C)
            {
                stream.Seek(-bufferSize, SeekOrigin.Current);
                return PackType.SevenZip;
            }
            else if (buffer[0] == 0x4D && buffer[1] == 0x53 && buffer[2] == 0x43 && buffer[3] == 0x46)
            {
                stream.Seek(-bufferSize, SeekOrigin.Current);
                return PackType.Cab;
            }
            else if (buffer[2] == 0x2D && buffer[3] == 0x6C)
            {
                stream.Seek(-bufferSize, SeekOrigin.Current);
                return PackType.Lha;
            }
            return PackType.Unknown;

            // ほとんどの場合に以下で TAR は判定可能だが、
            // 意図すれば TAR でありながら他のファイル形式のように見えるヘッダが作成可能
        }


        private bool Disposed = false;

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    // 子のローダー（入れ子・フォルダの中の書庫）と CAB はそれぞれのファイナライザがあるので、明示的に Dispose されたときだけ解放する（X4）
                    var owned = ownedChildLoaders;
                    ownedChildLoaders = null;
                    if (owned != null)
                    {
                        foreach (var child in owned)
                        {
                            try { child.Dispose(); } catch { }
                        }
                    }
                }

                if (iReader != null)
                {
                    try { iReader.Dispose(); } catch { }
                }

                if (zipArchive != null) zipArchive.Dispose();
                if (iArchive != null) iArchive.Dispose();
                if (sevenZipArchive != null) sevenZipArchive.Dispose();
                if (pdfArchive != null) pdfArchive.Dispose();

                if (stream != null)
                {
                    stream.Close();
                    stream.Dispose();
                    stream = null;
                }
                Disposed = true;
            }
        }
        
        ~PackedImageLoader()
        {
            Dispose(disposing: false);
        }

        private class MyRandomAccessSource : iTextSharp.text.io.IRandomAccessSource
        {
            private readonly Stream raf;
            private readonly long length;
            public MyRandomAccessSource(Stream raf)
            {
                this.raf = raf;
                length = raf.Length;
            }
            public virtual int Get(long position)
            {
                if (position > length)
                    return -1;

                // Not thread safe!
                if (raf.Position != position)
                    raf.Seek(position, SeekOrigin.Begin);

                return raf.ReadByte();
            }
            public virtual int Get(long position, byte[] bytes, int off, int len)
            {
                if (position > length)
                    return -1;

                // Not thread safe!
                if (raf.Position != position)
                    raf.Seek(position, SeekOrigin.Begin);

                int n = raf.Read(bytes, off, len);
                return n == 0 ? -1 : n; //in .NET Streams return 0 on EOF, not -1
            }
            public virtual long Length
            {
                get
                {
                    return length;
                }
            }
            public virtual void Close()
            {
                raf.Close();
            }
            virtual public void Dispose()
            {
                Close();
            }
        }
    }

    public class SeekableStream : Stream
    {
        public static Stream Seekablize(Stream originalStream, long streamLength, bool leaveOpen = false) =>
            originalStream.CanSeek ? (leaveOpen ? new NonClosingStream(originalStream) : originalStream) : new SeekableStream(originalStream, streamLength, leaveOpen);

        /// <summary>
        /// Dispose しても元のストリームを閉じない包み（X19: シークできるストリームでも leaveOpen を効かせる）
        /// </summary>
        private sealed class NonClosingStream : Stream
        {
            private readonly Stream s;
            public NonClosingStream(Stream s) { this.s = s; }
            public override bool CanRead => s.CanRead;
            public override bool CanSeek => s.CanSeek;
            public override bool CanWrite => false;
            public override long Length => s.Length;
            public override long Position { get => s.Position; set => s.Position = value; }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => s.Read(buffer, offset, count);
            public override int ReadByte() => s.ReadByte();
            public override long Seek(long offset, SeekOrigin origin) => s.Seek(offset, origin);
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private readonly bool leaveOpen;
        private readonly Stream originalStream;
        private readonly MemoryStream buffStream;
        private readonly long streamLength;
        private long positionLookahead;
        private bool stopBuffering = false;
        private bool bufferDisposed = false;
        private SeekableStream(Stream unseekableStream, long streamLength, bool leaveOpen = false)
        {
            originalStream = unseekableStream;
            this.streamLength = streamLength;
            bufferSize = (int)Math.Min(streamLength, maxBufferSize);
            buffStream = new MemoryStream();
            this.leaveOpen = leaveOpen;
        }

        public override bool CanRead => originalStream.CanRead;
        public override bool CanSeek => !stopBuffering;
        public override bool CanWrite => false;
        public override long Length => streamLength;
        public override long Position
        {
            get => bufferDisposed ? originalStream.Position : buffStream.Position + positionLookahead;
            set => Seek(value, SeekOrigin.Begin);
        }
        public override void Flush() => originalStream.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (bufferDisposed)
            {
                return originalStream.Read(buffer, offset, count);
            }
            var buffPosition = buffStream.Position;
            var loadedPosition = buffStream.Length;
            int result;
            if (buffPosition < loadedPosition)
            {
                var fromBuffCount = Math.Min(count, (int)(loadedPosition - buffPosition));
                result = buffStream.Read(buffer, offset, fromBuffCount);
                offset += result;
                count -= result;
            }
            else
            {
                result = 0;
                if (positionLookahead > 0)
                {
                    CopyStream(originalStream, stopBuffering ? null : buffStream, positionLookahead);
                    positionLookahead = 0;
                }
            }
            if (stopBuffering)
            {
                if (buffPosition + result >= loadedPosition)
                {
                    buffStream.Dispose();
                    bufferDisposed = true;
                    if (count > 0) result += originalStream.Read(buffer, offset, count);
                }
            }
            else
            {
                if (count > 0)
                {
                    var read = originalStream.Read(buffer, offset, count);
                    buffStream.Write(buffer, offset, read);
                    result += read;
                }
            }
            return result;
        }

        private bool CopyStream_bufferEmpty = true;
        private byte[] CopyStream_buffer;
        private const int maxBufferSize = 81920;
        private readonly int bufferSize;
        private void CopyStream(Stream src, Stream dst, long count)
        {
            if (CopyStream_bufferEmpty)
            {
                CopyStream_buffer = new byte[bufferSize];
                CopyStream_bufferEmpty = false;
            }
            if (dst == null)
            {
                while (count > 0)
                {
                    var read = src.Read(CopyStream_buffer, 0, (int)Math.Min(count, bufferSize));
                    if (read > 0)
                    {
                        count -= read;
                    }
                    else
                    {
                        throw new IOException();
                    }
                }
            }
            else
            {
                while (count > 0)
                {
                    var read = src.Read(CopyStream_buffer, 0, (int)Math.Min(count, bufferSize));
                    if (read > 0)
                    {
                        dst.Write(CopyStream_buffer, 0, read);
                        count -= read;
                    }
                    else
                    {
                        throw new IOException();
                    }
                }
            }
        }
        
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (bufferDisposed)
            {
                return originalStream.Seek(offset, origin);
            }
            switch (origin)
            {
                case SeekOrigin.Begin:
                    {
                        var loadedPosition = buffStream.Length;
                        if (offset <= loadedPosition)
                        {
                            positionLookahead = 0;
                            return buffStream.Seek(offset, SeekOrigin.Begin);
                        }
                        else
                        {
                            buffStream.Seek(0, SeekOrigin.End);
                            if (offset > streamLength) offset = streamLength;
                            positionLookahead = offset - loadedPosition;
                            return offset;
                        }
                    }
                case SeekOrigin.Current: return Seek(Position + offset, SeekOrigin.Begin);
                case SeekOrigin.End: return Seek(streamLength + offset, SeekOrigin.Begin);
                default: throw new ArgumentException();
            }
        }

        public override void SetLength(long value) => throw new NotImplementedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        public void StopBuffering() => stopBuffering = true;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!leaveOpen) originalStream.Dispose();
                if (!bufferDisposed)
                {
                    buffStream.Dispose();
                    bufferDisposed = true;
                }

            }
            base.Dispose(disposing);
        }
    }

    public class ReleasableStream : Stream
    {
        // MemoryStream の上限の 2GB より少し小さく（PackedImageLoader.OnMemoryLimit と同じ）（V18）
        private const long ReleaseLimit = int.MaxValue - 64 * 1024 * 1024;
        // 写すときに 1 回でロックを持つ大きさ
        private const int ReleaseChunkSize = 4 * 1024 * 1024;
        readonly object thisLock = new object();
        volatile bool isReleased = false;
        public bool IsReleased => isReleased;
        Stream stream;
        public ReleasableStream(Stream originalStream)
        {
            stream = originalStream;
        }

        public void Release()
        {
            if (!isReleased)
            {
                // 長さぶんの配列を先に確保し、少しずつロックを取って写す。以前は容量なしの MemoryStream に
                // ロックを持ったまま全体を写していたので、倍々の拡張で約 2 倍を確保し、その間ほかのスレッドの読み込みが止まり、
                // 2GB を超える書庫では例外になっていた（OnMemory 側の B8・V18 と同じ扱いにする）
                byte[] buffer;
                long length;
                try
                {
                    lock (thisLock)
                    {
                        if (isReleased || stream == null) return;
                        length = stream.Length;
                    }
                    // 大きすぎる書庫は MemoryStream にできないので、ファイルから読み続ける（V18）
                    if (length > ReleaseLimit) return;
                    buffer = new byte[length];
                    long done = 0;
                    while (done < length)
                    {
                        lock (thisLock)
                        {
                            if (isReleased || stream == null) return;
                            var position = stream.Position;
                            try
                            {
                                stream.Seek(done, SeekOrigin.Begin);
                                var count = (int)Math.Min(ReleaseChunkSize, length - done);
                                var read = stream.Read(buffer, (int)done, count);
                                if (read <= 0) return; // 途中でファイルが縮んだ。写すのをやめてファイルから読み続ける
                                done += read;
                            }
                            finally
                            {
                                stream.Seek(position, SeekOrigin.Begin);
                            }
                        }
                    }
                }
                catch
                {
                    // Task.Run の中で呼ばれるので、失敗したらファイルから読み続ける
                    return;
                }

                var temp = null as Stream;
                lock (thisLock)
                {
                    if (!isReleased && stream != null)
                    {
                        var memoryStream = new MemoryStream(buffer, 0, (int)length, writable: false, publiclyVisible: true);
                        try
                        {
                            memoryStream.Seek(stream.Position, SeekOrigin.Begin);
                        }
                        catch
                        {
                            memoryStream.Dispose();
                            return;
                        }
                        temp = stream;
                        stream = memoryStream;
                        isReleased = true;
                    }
                }
                temp?.Dispose();
            }
        }

        public override bool CanRead{ get { if (isReleased) return stream.CanRead; lock(thisLock) return stream.CanRead; } }

        public override bool CanSeek { get { if(isReleased) return stream.CanSeek; lock(thisLock) return stream.CanSeek; } }

        public override bool CanWrite { get { if (isReleased) return stream.CanWrite; lock (thisLock) return stream.CanWrite; } }

        public override long Length { get { if (isReleased) return stream.Length; lock (thisLock) return stream.Length; } }

        public override long Position
        {
            get { if (isReleased) return stream.Position; lock (thisLock) return stream.Position; }
            set { if (isReleased) stream.Position = value; else lock (thisLock) stream.Position = value; }
        }

        public override void Flush() { if (isReleased) stream.Flush(); else lock(thisLock) stream.Flush(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (isReleased) return stream.Read(buffer, offset, count); lock (thisLock) return stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (isReleased) return stream.Seek(offset, origin); lock (thisLock) return stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            if (isReleased) stream.SetLength(value); else lock (thisLock) stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (isReleased) stream.Write(buffer, offset, count); else lock (thisLock) stream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (stream != null)
                {
                    if (isReleased)
                    {
                        stream?.Dispose();
                        stream = null;
                    }
                    else
                    {
                        lock (thisLock)
                        {
                            stream?.Dispose();
                            stream = null;
                            isReleased = true;
                        }
                    }
                }
            }
            base.Dispose(disposing);
        }
    }
}
