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
                    {
                        var typeArray = GetTypeNameArray(ZipNameArray);
                        result = order.OrderBy(x => typeArray[x], GetSortArray_FilePathComparator).ToArray();
                        break;
                    }
                case SortMode.TypeInDesc:
                    {
                        var typeArray = GetTypeNameArray(ZipNameArray);
                        result = order.OrderByDescending(x => typeArray[x], GetSortArray_FilePathComparator).ToArray();
                        break;
                    }
                case SortMode.NameInAsc:
                    {
                        var replaced = GetSeparatorReplacedArray(ZipNameArray);
                        // ZipPathArray でソートすると明らかに表示との不整合が生じる
                        result = order.OrderBy(x => replaced[x], GetSortArray_NaturalSort).ToArray();
                        break;
                    }
                case SortMode.NameInDesc:
                    {
                        var replaced = GetSeparatorReplacedArray(ZipNameArray);
                        result = order.OrderByDescending(x => replaced[x], GetSortArray_NaturalSort).ToArray();
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
                        result = order.OrderBy(x => ratingArray[x]).ToArray();
                        break;
                    }
                    else
                    {
                        result = order.OrderByDescending(x => ratingArray[x]).ToArray();
                        break;
                    }
                case SortMode.CreatedInAsc:
                    result = order.OrderBy(x => CreateTimeArray[x]).ToArray();
                    break;
                case SortMode.CreatedInDesc:
                    result = order.OrderByDescending(x => CreateTimeArray[x]).ToArray();
                    break;
                case SortMode.AccessedInAsc:
                    result = order.OrderBy(x => LastAccessTimeArray[x]).ToArray();
                    break;
                case SortMode.AccessedInDesc:
                    result = order.OrderByDescending(x => LastAccessTimeArray[x]).ToArray();
                    break;
                case SortMode.ModifiedInAsc:
                    result = order.OrderBy(x => ModifiedTimeArray[x]).ToArray();
                    break;
                case SortMode.ModifiedInDesc:
                    result = order.OrderByDescending(x => ModifiedTimeArray[x]).ToArray();
                    break;
                case SortMode.SizeInAsc:
                    {
                        var sizeArray = new long[count];
                        for (var i = 0; i < count; i++)
                        {
                            var s = ZipSizeArray[i];
                            if (s >= 0)
                            {
                                var name = ZipNameArray[i];
                                if (!string.IsNullOrEmpty(name))
                                {
                                    var c = name[name.Length - 1];
                                    if (c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar)
                                    {
                                        sizeArray[i] = s;
                                    }
                                    else
                                    {
                                        sizeArray[i] = long.MaxValue;
                                    }
                                }
                            }
                            else
                            {
                                sizeArray[i] = long.MaxValue;
                            }
                        }
                        result = order.OrderBy(x => sizeArray[x]).ToArray();
                        break;
                    }
                case SortMode.SizeInDesc:
                    {
                        var sizeArray = new long[count];
                        for (var i = 0; i < count; i++)
                        {
                            var s = ZipSizeArray[i];
                            if (s >= 0)
                            {
                                var name = ZipNameArray[i];
                                if (!string.IsNullOrEmpty(name))
                                {
                                    var c = name[name.Length - 1];
                                    if (c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar)
                                    {
                                        sizeArray[i] = s;
                                    }
                                    else
                                    {
                                        sizeArray[i] = -1;
                                    }
                                }
                            }
                            else
                            {
                                sizeArray[i] = -1; // サイズ不明は昇順と同じくフォルダと一緒に末尾へ
                            }
                        }
                        result = order.OrderByDescending(x => sizeArray[x]).ToArray();
                        break;
                    }
                case SortMode.VPageInAsc:
                    {
                        var pageArray = new int[count];
                        for (var i = 0; i < count; i++)
                        {
                            var p = FileCountArray[i];
                            if (p >= 0)
                            {
                                pageArray[i] = p;
                            }
                            else
                            {
                                pageArray[i] = int.MaxValue - 100 - p;
                            }
                        }
                        result = order.OrderBy(x => pageArray[x]).ToArray();
                        break;
                    }
                case SortMode.VPageInDesc:
                    {
                        var pageArray = new int[count];
                        Array.Copy(FileCountArray, pageArray, count);
                        result = order.OrderByDescending(x => pageArray[x]).ToArray();
                        break;
                    }
                case SortMode.VResolutionInAsc:
                    {
                        var resoArray = new int[count];
                        for (var i = 0; i < count; i++)
                        {
                            Size size;
                            switch (getSizeInLock(i, out size))
                            {
                                case 0:
                                    resoArray[i] = unchecked(size.Width * size.Height); continue;
                                case 1: resoArray[i] = int.MaxValue - 1; continue;
                                case 2: resoArray[i] = int.MaxValue; continue;
                            }

                        }
                        result = order.OrderBy(x => resoArray[x]).ToArray();
                        break;
                    }
                case SortMode.VResolutionInDesc:
                    {
                        var resoArray = new int[count];
                        for (var i = 0; i < count; i++)
                        {
                            Size size;
                            switch (getSizeInLock(i, out size))
                            {
                                case 0:
                                    resoArray[i] = unchecked(size.Width * size.Height); continue;
                                case 1: resoArray[i] = -1; continue;
                                case 2: resoArray[i] = -2; continue;
                            }
                        }
                        result = order.OrderByDescending(x => resoArray[x]).ToArray();
                        break;
                    }
                case SortMode.VRatioInAsc:
                    {
                        var ratioArray = new double[count];
                        for (var i = 0; i < count; i++)
                        {
                            Size size;
                            switch (getSizeInLock(i, out size))
                            {
                                case 0:
                                    ratioArray[i] = size.Height > 0 ? (double)size.Width / size.Height : double.MaxValue * 0.998; continue;
                                case 1: ratioArray[i] = double.MaxValue * 0.999; continue;
                                case 2: ratioArray[i] = double.MaxValue; continue;
                            }

                        }
                        result = order.OrderBy(x => ratioArray[x]).ToArray();
                        break;
                    }
                case SortMode.VRatioInDesc:
                    {
                        var ratioArray = new double[count];
                        for (var i = 0; i < count; i++)
                        {
                            Size size;
                            switch (getSizeInLock(i, out size))
                            {
                                case 0:
                                    ratioArray[i] = size.Height > 0 ? (double)size.Width / size.Height : double.MaxValue * 0.998; continue;
                                case 1: ratioArray[i] = -1; continue;
                                case 2: ratioArray[i] = -2; continue;
                            }

                        }
                        result = order.OrderByDescending(x => ratioArray[x]).ToArray();
                        break;
                    }
                case SortMode.VWidthInAsc:
                    {
                        var widthArray = new int[count];
                        for (var i = 0; i < count; i++)
                        {
                            Size size;
                            switch (getSizeInLock(i, out size))
                            {
                                case 0:
                                    widthArray[i] = size.Width; continue;
                                case 1: widthArray[i] = int.MaxValue - 1; continue;
                                case 2: widthArray[i] = int.MaxValue; continue;
                            }
                        }
                        result = order.OrderBy(x => widthArray[x]).ToArray();
                        break;
                    }
                case SortMode.VWidthInDesc:
                    {
                        var widthArray = new int[count];
                        for (var i = 0; i < count; i++)
                        {
                            Size size;
                            switch (getSizeInLock(i, out size))
                            {
                                case 0:
                                    widthArray[i] = size.Width; continue;
                                case 1: widthArray[i] = -1; continue;
                                case 2: widthArray[i] = -2; continue;
                            }
                        }
                        result = order.OrderByDescending(x => widthArray[x]).ToArray();
                        break;
                    }
                case SortMode.VHeightInAsc:
                    {
                        var heightArray = new int[count];
                        for (var i = 0; i < count; i++)
                        {
                            Size size;
                            switch (getSizeInLock(i, out size))
                            {
                                case 0:
                                    heightArray[i] = size.Height; continue;
                                case 1: heightArray[i] = int.MaxValue - 1; continue;
                                case 2: heightArray[i] = int.MaxValue; continue;
                            }
                        }
                        result = order.OrderBy(x => heightArray[x]).ToArray();
                        break;
                    }
                case SortMode.VHeightInDesc:
                    {
                        var heightArray = new int[count];
                        for (var i = 0; i < count; i++)
                        {
                            Size size;
                            switch (getSizeInLock(i, out size))
                            {
                                case 0:
                                    heightArray[i] = size.Height; continue;
                                case 1: heightArray[i] = -1; continue;
                                case 2: heightArray[i] = -2; continue;
                            }
                        }
                        result = order.OrderByDescending(x => heightArray[x]).ToArray();
                        break;
                    }
                case SortMode.VLengthInAsc:
                    {
                        var timeArray = new double[count];
                        for (var i = 0; i < count; i++)
                        {
                            var m = MovieInfoArray[i];
                            if (m != null)
                            {
                                var t = m.Duration;
                                if (t != default(TimeSpan))
                                {
                                    timeArray[i] = t.TotalSeconds;
                                }
                                else
                                {
                                    timeArray[i] = double.MaxValue * 0.999;
                                }
                            }
                            else if (MovieThumbnailLoader.Supports(ZipNameArray[i]))
                            {
                                timeArray[i] = double.MaxValue * 0.999;
                            }
                            else
                            {
                                timeArray[i] = double.MaxValue;
                            }
                        }
                        result = order.OrderBy(x => timeArray[x]).ToArray();
                        break;
                    }
                case SortMode.VLengthInDesc:
                    {
                        var timeArray = new double[count];
                        for (var i = 0; i < count; i++)
                        {
                            var m = MovieInfoArray[i];
                            if (m != null)
                            {
                                var t = m.Duration;
                                if (t != default(TimeSpan))
                                {
                                    timeArray[i] = t.TotalSeconds;
                                }
                                else
                                {
                                    timeArray[i] = -1;
                                }
                            }
                            else if (MovieThumbnailLoader.Supports(ZipNameArray[i]))
                            {
                                timeArray[i] = -1;
                            }
                            else
                            {
                                timeArray[i] = -2;
                            }
                        }
                        result = order.OrderByDescending(x => timeArray[x]).ToArray();
                        break;
                    }
                case SortMode.VFPSInAsc:
                    {
                        var fpsArray = new double[count];
                        for (var i = 0; i < count; i++)
                        {
                            var m = MovieInfoArray[i];
                            if (m != null)
                            {
                                var t = m.FPS;
                                if (t >= 0)
                                {
                                    fpsArray[i] = t;
                                }
                                else
                                {
                                    fpsArray[i] = double.MaxValue * 0.999;
                                }
                            }
                            else if (MovieThumbnailLoader.Supports(ZipNameArray[i]))
                            {
                                fpsArray[i] = double.MaxValue * 0.999;
                            }
                            else
                            {
                                fpsArray[i] = double.MaxValue;
                            }
                        }
                        result = order.OrderBy(x => fpsArray[x]).ToArray();
                        break;
                    }
                case SortMode.VFPSInDesc:
                    {
                        var fpsArray = new double[count];
                        for (var i = 0; i < count; i++)
                        {
                            var m = MovieInfoArray[i];
                            if (m != null)
                            {
                                var t = m.FPS;
                                if (t >= 0)
                                {
                                    fpsArray[i] = t;
                                }
                                else
                                {
                                    fpsArray[i] = -1;
                                }
                            }
                            else
                            {
                                var name = ZipNameArray[i];
                                if (!string.IsNullOrEmpty(name) && MovieThumbnailLoader.Supports(name))
                                {
                                    fpsArray[i] = -1;
                                }
                                else
                                {
                                    fpsArray[i] = -2;
                                }
                            }
                        }
                        result = order.OrderByDescending(x => fpsArray[x]).ToArray();
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
    }
}
