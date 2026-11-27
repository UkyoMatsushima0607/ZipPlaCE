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
        class filePathComparator : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (x == null)
                {
                    if (y == null) return 0;
                    else return -1;
                }
                else
                {
                    if (y == null) return 1;
                    else return LogicalStringComparer.StrCmpLogicalW(x, y);
                }
            }
        }

        private string[] GetSeparatorReplacedArray(string[] nameArray)
        {
            // セパレーターなし
            if (InMovie || InArchive && currentLocation?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true ||
                InDirectoryVirtualDirectoryOrSmartDirectory && SubfolderMode == SubfolderMode.Ignore) return nameArray;

            var count = nameArray.Length;
            var result = new string[count];

            // / のみ
            if (InArchive)
            {
                var s = Path.AltDirectorySeparatorChar;
                var r = Convert.ToChar(2);
                for (var i = 0; i < count; i++)
                {
                    var name = nameArray[i];
                    if (name != null && name.Contains(s))
                    {
                        result[i] = name.Replace(s, r);
                    }
                    else
                    {
                        result[i] = name;
                    }
                }
                return result;
            }

            // 末尾に \ のみ
            if (InDirectoryVirtualDirectoryOrSmartDirectory && !InRealDirectory || InRealDirectory && !SubfolderIsSearched())// SubfolderMode != SubfolderMode.Search)
            {
                var s = Path.DirectorySeparatorChar;
                var r = Convert.ToChar(1);
                for (var i = 0; i < count; i++)
                {
                    var name = nameArray[i];
                    if (name != null && name.Any() && name.Last() == s)
                    {
                        result[i] = name.Replace(s, r);
                    }
                    else
                    {
                        result[i] = name;
                    }
                }
                return result;
            }

            // \ のみ
            if (InRealDirectory)
            {
                var s = Path.DirectorySeparatorChar;
                var r = Convert.ToChar(1);
                for (var i = 0; i < count; i++)
                {
                    var name = nameArray[i];
                    if (name != null && name.Contains(s))
                    {
                        result[i] = name.Replace(s, r);
                    }
                    else
                    {
                        result[i] = name;
                    }
                }
                return result;
            }

            // 将来別のパターンを追加したとき用

            var s1 = Path.DirectorySeparatorChar;
            var r1 = Convert.ToChar(1);
            var s2 = Path.AltDirectorySeparatorChar;
            var r2 = Convert.ToChar(2);
            for (var i = 0; i < count; i++)
            {
                var name = nameArray[i];
                if (name != null && name.Any(c => c == s1 || c == s2))
                {
                    result[i] = name.Replace(s1, r1).Replace(s2, r2);
                }
                else
                {
                    result[i] = name;
                }
            }
            return result;
        }

        //int[] GetSortArray_RandomOrder = null;
        string GetSortArray_RandomSeed;

        //bool GetSortArray_RandomSeed_Set = false;
        public static readonly LogicalStringComparer GetSortArray_NaturalSort = new LogicalStringComparer();

        private static readonly filePathComparator GetSortArray_FilePathComparator = new filePathComparator();

        //private readonly Random randomForPermutation = new Random();
        // 絞り込みの判定の記録（K29）。名前の参照が同じ項目は、同じ絞り込みなら前回の結果を使い、
        // 前回の絞り込みを狭めただけ（AND の単純な語を延長・追加）なら前回一致しなかった項目は調べない
        private string filterMemo_Text;

        private string[] filterMemo_Names;

        private bool[] filterMemo_Matched;

        private readonly object filterMemo_Lock = new object();

        private void filterWithMemo(int count, List<int> orderList)
        {
            var manager = cbFilter_SearchManager;
            var text = manager.Text;
            var names = ZipNameArray;
            var memoNames = filterMemo_Names;
            var memoMatched = filterMemo_Matched;
            var same = false;
            var narrowing = false;
            if (text != null && filterMemo_Text != null && memoNames != null && memoMatched != null && memoNames.Length == count && memoMatched.Length == count)
            {
                same = text == filterMemo_Text;
                narrowing = !same && SearchManager.IsNarrowing(filterMemo_Text, text);
            }
            var matched = new bool[count];
            for (var i = 0; i < count; i++)
            {
                var name = names[i];
                bool m;
                if ((same || narrowing) && ReferenceEquals(name, memoNames[i]))
                {
                    if (same) m = memoMatched[i];
                    else m = memoMatched[i] && name != null && manager.Match(name);
                }
                else
                {
                    m = name != null && manager.Match(name);
                }
                matched[i] = m;
                if (m && ZipMaskArray[i]) orderList.Add(i);
            }
            filterMemo_Text = text;
            filterMemo_Names = (string[])names.Clone();
            filterMemo_Matched = matched;
        }

        private int[] GetSortArray(int[] preSortArray, bool careTvCatalog = true, SortMode? sortMode = null, bool recursiveCall = false)
        {
            var count = ZipPathArray.Length;
            if (careTvCatalog && (tvCatalog.Count != count || CreatingZipPathArray)) return new int[0];

            int[] order;
            // this.sortMode が名前ソートなどの場合も安定ソートは不要に思えるが、スマートフォルダ等を考慮すると実際にはそうではない
            if (preSortArray == null && sortMode == null && prevSortMode != SortMode.Random && this.sortMode != SortMode.Random && prevSortMode != this.sortMode)
            {
                order = GetSortArray(null, careTvCatalog, prevSortMode, recursiveCall: true);
            }
            else
            {
                var orderList = new List<int>();
                var invalid = InArchive || InMovie || cbFilter_SearchManager == null;
                if (invalid)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (ZipMaskArray[i]) orderList.Add(i);
                    }
                }
                else
                {
                    lock (filterMemo_Lock) filterWithMemo(count, orderList); // 背景のスレッドから呼ばれても記録が混ざらないように
                }

                if (preSortArray != null)
                {
                    // 前の並び順にある項目はその順に、ない項目はその後ろに元の順で並べる（OrderBy(prePosition) と同じ結果を O(n) で。K28）
                    var included = new bool[count];
                    foreach (var dataIndex in orderList) included[dataIndex] = true;
                    var inPre = new bool[count];
                    var ordered = new List<int>(orderList.Count);
                    foreach (var dataIndex in preSortArray)
                    {
                        if (dataIndex < 0 || dataIndex >= count || inPre[dataIndex]) continue;
                        inPre[dataIndex] = true;
                        if (included[dataIndex]) ordered.Add(dataIndex);
                    }
                    foreach (var dataIndex in orderList)
                    {
                        if (!inPre[dataIndex]) ordered.Add(dataIndex);
                    }
                    order = ordered.ToArray();
                }
                else
                {
                    order = orderList.ToArray();
                }
            }

            var localSortMode = sortMode == null ? this.sortMode : (SortMode)sortMode;
            int[] result;
            switch (localSortMode)
            {
                case SortMode.TypeInAsc:
                case SortMode.TypeInDesc:
                    {
                        var typeArray = GetTypeNameArray(ZipNameArray);
                        result = SortBy(order, typeArray, localSortMode == SortMode.TypeInAsc, GetSortArray_FilePathComparator);
                        break;
                    }
                case SortMode.NameInAsc:
                case SortMode.NameInDesc:
                    {
                        // ZipPathArray でソートすると明らかに表示との不整合が生じる
                        var replaced = GetSeparatorReplacedArray(ZipNameArray);
                        result = SortBy(order, replaced, localSortMode == SortMode.NameInAsc, GetSortArray_NaturalSort);
                        break;
                    }
                case SortMode.RatingInAsc:
                case SortMode.RatingInDesc:
                    var ratingArray = (from name in ZipNameArray select name == null ? 0 : ZipPlaInfo.GetOnlyRating(name)).ToArray();
                    if (localSortMode == SortMode.RatingInAsc)
                    {
                        var sortCount = ratingArray.Length;
                        for (var i = 0; i < sortCount; i++)
                        {
                            if (ratingArray[i] < 0) ratingArray[i] = int.MaxValue;
                        }
                    }
                    result = SortBy(order, ratingArray, localSortMode == SortMode.RatingInAsc);
                    break;
                case SortMode.CreatedInAsc:
                case SortMode.CreatedInDesc:
                    result = SortBy(order, CreateTimeArray, localSortMode == SortMode.CreatedInAsc);
                    break;
                case SortMode.AccessedInAsc:
                case SortMode.AccessedInDesc:
                    result = SortBy(order, LastAccessTimeArray, localSortMode == SortMode.AccessedInAsc);
                    break;
                case SortMode.ModifiedInAsc:
                case SortMode.ModifiedInDesc:
                    result = SortBy(order, ModifiedTimeArray, localSortMode == SortMode.ModifiedInAsc);
                    break;
                case SortMode.SizeInAsc:
                case SortMode.SizeInDesc:
                    {
                        var asc = localSortMode == SortMode.SizeInAsc;
                        result = SortBy(order, GetFileSizeSortKeys(ZipSizeArray, ZipNameArray, count, asc), asc);
                        break;
                    }
                case SortMode.VPageInAsc:
                case SortMode.VPageInDesc:
                    {
                        var asc = localSortMode == SortMode.VPageInAsc;
                        result = SortBy(order, GetPageCountSortKeys(FileCountArray, count, asc), asc);
                        break;
                    }
                case SortMode.VResolutionInAsc:
                case SortMode.VResolutionInDesc:
                    {
                        var asc = localSortMode == SortMode.VResolutionInAsc;
                        result = SortBy(order, GetImageSizeSortKeys(count, getSizeInLock, size => unchecked(size.Width * size.Height), UnknownIntSortKey(asc, 1), UnknownIntSortKey(asc, 2)), asc);
                        break;
                    }
                case SortMode.VRatioInAsc:
                case SortMode.VRatioInDesc:
                    {
                        var asc = localSortMode == SortMode.VRatioInAsc;
                        // 高さ 0 の比率は向きによらず最大値の手前（昇順では不明の前、降順では先頭）
                        result = SortBy(order, GetImageSizeSortKeys(count, getSizeInLock, size => size.Height > 0 ? (double)size.Width / size.Height : double.MaxValue * 0.998,
                            UnknownDoubleSortKey(asc, 1), UnknownDoubleSortKey(asc, 2)), asc);
                        break;
                    }
                case SortMode.VWidthInAsc:
                case SortMode.VWidthInDesc:
                    {
                        var asc = localSortMode == SortMode.VWidthInAsc;
                        result = SortBy(order, GetImageSizeSortKeys(count, getSizeInLock, size => size.Width, UnknownIntSortKey(asc, 1), UnknownIntSortKey(asc, 2)), asc);
                        break;
                    }
                case SortMode.VHeightInAsc:
                case SortMode.VHeightInDesc:
                    {
                        var asc = localSortMode == SortMode.VHeightInAsc;
                        result = SortBy(order, GetImageSizeSortKeys(count, getSizeInLock, size => size.Height, UnknownIntSortKey(asc, 1), UnknownIntSortKey(asc, 2)), asc);
                        break;
                    }
                case SortMode.VLengthInAsc:
                case SortMode.VLengthInDesc:
                    {
                        var asc = localSortMode == SortMode.VLengthInAsc;
                        result = SortBy(order, GetMovieSortKeys(MovieInfoArray, ZipNameArray, count, m => m.Duration != default(TimeSpan) ? m.Duration.TotalSeconds : (double?)null, asc), asc);
                        break;
                    }
                case SortMode.VFPSInAsc:
                case SortMode.VFPSInDesc:
                    {
                        var asc = localSortMode == SortMode.VFPSInAsc;
                        result = SortBy(order, GetMovieSortKeys(MovieInfoArray, ZipNameArray, count, m => m.FPS >= 0 ? m.FPS : (double?)null, asc), asc);
                        break;
                    }
                default:
                    {
                        if (GetSortArray_RandomSeed == null)
                        {
                            var now = DateTime.Now;
                            GetSortArray_RandomSeed = now.Ticks.ToString();
                            //GetSortArray_RandomSeed_Set = true;
                        }

                        // ZipPathArray を使った場合：
                        // フォルダごと移動すると Seed が変わらなくても順番が変わる
                        // Seed を変えなくても PDF 内のソート順が同じということにならない
                        var table = (from key in ZipPathArray select GetRandomIndex(key, GetSortArray_RandomSeed)).ToArray(); // 10 万程度であれば処理時間はこちらが主要


                        var result2 = order.OrderBy(x => table[x]).ToArray(); // 係数の問題で現実的な範囲ではこちらの計算時間は無視できる


                        result = result2;
                        break;
                    }
            }

            if (!recursiveCall)
            {
                var resolved = ResolveFitToDirectionOfSort(FolderSortMode, localSortMode);
                var dsc = Path.DirectorySeparatorChar;
                if (resolved != FolderSortMode.None)
                {
                    var folderIndentity = result.Select(i =>
                    {
                        var name = ZipNameArray[i];
                        return !string.IsNullOrEmpty(name) && name.Last() == dsc;
                    }).ToArray();
                    var folderCount = folderIndentity.Count(isFolder => isFolder);
                    int file, folder;
                    if (resolved == FolderSortMode.Ahead)
                    {
                        folder = 0;
                        file = folderCount;
                    }
                    else
                    {
                        file = 0;
                        folder = result.Length - folderCount;
                    }
                    var newResult = new int[result.Length];
                    for (var i = 0; i < result.Length; i++)
                    {
                        if (folderIndentity[i])
                        {
                            newResult[folder++] = result[i];
                        }
                        else
                        {
                            newResult[file++] = result[i];
                        }
                    }
                    result = newResult;
                }
            }

            return result;
        }

        // ---- GetSortArray の並べ替えのキー ----
        // 値が分からない項目（読み込み前など）と対象外の項目は、昇順・降順のどちらでも末尾に来るよう、昇順では最大値側、降順では負の値にする。
        // rank は 1 が「まだ分からない」、2 が「対象外（画像・動画でないなど）」で、どちらの向きでも 1 が先に来る

        private static int[] SortBy<T>(int[] order, IReadOnlyList<T> keys, bool asc, IComparer<T> comparer = null)
        {
            return asc ? order.OrderBy(x => keys[x], comparer).ToArray() : order.OrderByDescending(x => keys[x], comparer).ToArray();
        }

        internal static int UnknownIntSortKey(bool asc, int rank) => asc ? int.MaxValue - 2 + rank : -rank;

        internal static double UnknownDoubleSortKey(bool asc, int rank) => asc ? (rank == 1 ? double.MaxValue * 0.999 : double.MaxValue) : -rank;

        /// <summary>ファイルサイズ。サイズ不明とフォルダはフォルダと一緒に末尾へ（名前がない項目は 0）</summary>
        internal static long[] GetFileSizeSortKeys(IReadOnlyList<long> sizes, IReadOnlyList<string> names, int count, bool asc)
        {
            var unknown = asc ? long.MaxValue : -1;
            var keys = new long[count];
            for (var i = 0; i < count; i++)
            {
                var s = sizes[i];
                if (s < 0)
                {
                    keys[i] = unknown;
                    continue;
                }
                var name = names[i];
                if (string.IsNullOrEmpty(name)) continue;
                var c = name[name.Length - 1];
                keys[i] = c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar ? s : unknown;
            }
            return keys;
        }

        /// <summary>ページ数。負の値（数えていない・数えられない）は昇順では末尾へ（値ごとの順を保つ）。降順ではそのまま（負なので末尾）</summary>
        internal static int[] GetPageCountSortKeys(int[] pageCounts, int count, bool asc)
        {
            var keys = new int[count];
            if (!asc)
            {
                Array.Copy(pageCounts, keys, count);
                return keys;
            }
            for (var i = 0; i < count; i++)
            {
                var p = pageCounts[i];
                keys[i] = p >= 0 ? p : int.MaxValue - 100 - p;
            }
            return keys;
        }

        internal delegate int ImageSizeGetter(int index, out Size size);

        /// <summary>
        /// 画像・動画の大きさから作るキー。getSize の戻り値が 0 なら value(size)、1（まだ分からない）なら unknown1、
        /// 2（対象外）なら unknown2、それ以外（名前がないなど）は既定値
        /// </summary>
        internal static T[] GetImageSizeSortKeys<T>(int count, ImageSizeGetter getSize, Func<Size, T> value, T unknown1, T unknown2)
        {
            var keys = new T[count];
            for (var i = 0; i < count; i++)
            {
                switch (getSize(i, out var size))
                {
                    case 0: keys[i] = value(size); break;
                    case 1: keys[i] = unknown1; break;
                    case 2: keys[i] = unknown2; break;
                }
            }
            return keys;
        }

        /// <summary>動画の長さ・FPS。value が null（値がない）か、動画情報がまだない動画は「まだ分からない」、動画でなければ「対象外」</summary>
        internal static double[] GetMovieSortKeys(IReadOnlyList<MovieInfo> movieInfos, IReadOnlyList<string> names, int count, Func<MovieInfo, double?> value, bool asc)
        {
            var keys = new double[count];
            for (var i = 0; i < count; i++)
            {
                var m = movieInfos[i];
                if (m != null)
                {
                    keys[i] = value(m) ?? UnknownDoubleSortKey(asc, 1);
                }
                else
                {
                    keys[i] = UnknownDoubleSortKey(asc, MovieThumbnailLoader.Supports(names[i]) ? 1 : 2);
                }
            }
            return keys;
        }

        public static string[] GetTypeNameArray(IReadOnlyList<string> names)
        {
            var count = names.Count;
            var typeArray = new string[count];
            var typeNameDictionary = new Dictionary<string, string>();
            for (var i = 0; i < count; i++)
            {
                var name = names[i];
                if (!string.IsNullOrEmpty(name))
                {
                    var nameLength = name.Length;
                    if (nameLength == 0)
                    {
                        typeArray[i] = "";
                        continue;
                    }
                    if (name[name.Length - 1] != Path.DirectorySeparatorChar)
                    {
                        try
                        {
                            var ext = Path.GetExtension(name).ToLower();
                            string typeName;
                            if (!typeNameDictionary.TryGetValue(ext, out typeName))
                            {
                                // 描画用としては十分高速だが、ソート用としては少し遅いので辞書を使う
                                typeName = FileTypeManager.GetTypeName(name, useFileAttrinutes: true);
                                typeNameDictionary[ext] = typeName;
                            }
                            typeArray[i] = typeName;
                        }
                        catch
                        {
                            typeArray[i] = "";
                        }
                    }
                }
            }
            return typeArray;
        }

        // ランダムの並べ替えのたびに全パスの SHA1 を計算しないよう、同じ種のあいだは結果を覚える（K28）
        private static readonly Dictionary<string, UBigInteger> getRandomIndex_Cache = new Dictionary<string, UBigInteger>();

        private static string getRandomIndex_CacheSeed;

        private const int GetRandomIndex_CacheLimit = 200000;

        public static UBigInteger GetRandomIndex(string fullPath, string randomSeed)
        {
            var basePath = ZipPlaInfo.GetBasePathRoughly(fullPath); // タグの書き換えで順序が変わらないように
            lock (getRandomIndex_Cache)
            {
                if (getRandomIndex_CacheSeed != randomSeed || getRandomIndex_Cache.Count >= GetRandomIndex_CacheLimit)
                {
                    getRandomIndex_Cache.Clear();
                    getRandomIndex_CacheSeed = randomSeed;
                }
                UBigInteger result;
                if (!getRandomIndex_Cache.TryGetValue(basePath, out result))
                {
                    result = new UBigInteger(getHash_SHA1.ComputeHash(Encoding.UTF8.GetBytes(basePath + "\t" + randomSeed)));
                    getRandomIndex_Cache.Add(basePath, result);
                }
                return result;
            }
        }

        //private static System.Security.Cryptography.MD5CryptoServiceProvider getHash64_MD5 = new System.Security.Cryptography.MD5CryptoServiceProvider();

        // 計算速度は早い順で SHA256Managed > SHA256Cng > SHA256CryptoServiceProvider
        // 対応範囲の広さは SHA256Managed > SHA256Cng = SHA256CryptoServiceProvider
        // Managed が高速なのは近年の最適化技術と CPU の進化によるものと考えられる
        //private static readonly System.Security.Cryptography.SHA256 getHash_SHA256 = new System.Security.Cryptography.SHA256Managed();

        // SHA256 より 2 倍弱高速なので SHA1 を採用（1 万個のファイルの場合で計算時間がおよそ 2 フレーム → 1 フレーム に改善）
        // セキュリティが目的ではないのでその点のデメリットはない
        // 値域は狭くなるがそれでも 160bit あるので、1 京個のファイルがあっても衝突する確率は 1 京分の 1 未満
        // 1 万個のファイルの場合衝突の確率は 1正（ 10 の 40 乗）分の 1 未満
        // さらに、衝突が起こっても実害はないので、値域の狭さのデメリットも皆無
        private static readonly System.Security.Cryptography.SHA1 getHash_SHA1 = new System.Security.Cryptography.SHA1Managed();

        // Managed が使えず遅い
        //private static readonly System.Security.Cryptography.MD5 getHash_MD5 = new System.Security.Cryptography.MD5Cng();


        private int getSizeInLock(int index, out Size size)
        {
            var name = ZipNameArray[index];
            if (string.IsNullOrEmpty(name))
            {
                size = Size.Empty;
                return -1;
            }
            Size? sizeN;
            ImageInfo ii;
            if (MovieThumbnailLoader.Supports(name))
            {
                sizeN = MovieInfoArray[index]?.SizeByPixel;
            }
            else if ((ii = ImageInfoArray[index]) != null)
            {
                sizeN = ii.Size;
            }
            else
            {
                size = Size.Empty;
                return 2;
            }
            if (sizeN == null)
            {
                size = Size.Empty;
                return 1;
            }
            else
            {
                size = (Size)sizeN;
                return size.IsEmpty ? 1 : 0;
            }
        }

        private void resetVolatilitySort()
        {
            foreach (DataGridViewColumn c in dgvFileList.Columns)
            {
                if (
                    c != tbcFileName &&
                    c != tbcRating &&
                    c != tbcCreateTime &&
                    c != tbcAccessTime &&
                    c != tbcWriteTime &&
                    c != tbcSize)
                {
                    c.HeaderCell.SortGlyphDirection = SortOrder.None;
                }
            }
        }

        private void setNonVolatilitySort()
        {
            resetVolatilitySort();
            dgvFileList.Columns[tbcIcon.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.TypeInAsc ? SortOrder.Ascending : sortMode == SortMode.TypeInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcFileName.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.NameInAsc ? SortOrder.Ascending : sortMode == SortMode.NameInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcRating.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.RatingInAsc ? SortOrder.Ascending : sortMode == SortMode.RatingInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcCreateTime.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.CreatedInAsc ? SortOrder.Ascending : sortMode == SortMode.CreatedInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcAccessTime.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.AccessedInAsc ? SortOrder.Ascending : sortMode == SortMode.AccessedInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcWriteTime.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.ModifiedInAsc ? SortOrder.Ascending : sortMode == SortMode.ModifiedInDesc ? SortOrder.Descending : SortOrder.None;
            dgvFileList.Columns[tbcSize.Index].HeaderCell.SortGlyphDirection = sortMode == SortMode.SizeInAsc ? SortOrder.Ascending : sortMode == SortMode.SizeInDesc ? SortOrder.Descending : SortOrder.None;
            var temp = cbSortBy_SelectedValueChanged_Stop;
            cbSortBy_SelectedValueChanged_Stop = true;
            //while (cbSortBy.Items.Count > (int)SortMode.Random + 1) cbSortBy.Items.RemoveAt(cbSortBy.Items.Count - 1);
            cbSortBy.SelectedIndex = (int)sortMode;
            cbSortBy_SelectedValueChanged_Stop = temp;
        }
    }
}
