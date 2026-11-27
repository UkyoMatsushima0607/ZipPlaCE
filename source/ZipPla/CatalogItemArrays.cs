using System;

namespace ZipPla
{
    /// <summary>
    /// カタログの項目ごとの情報を並べた 12 本の配列（CatalogForm の ZipPathArray〜ZipMaskArray）をまとめて持つ。
    /// 全部を同じ長さで確保する・伸ばして複製する・まとめて差し替える（CatalogForm.SetItemArrays）ときに使う。
    /// 配列の要素の読み書きは、これまでどおり CatalogForm のフィールドから行う
    /// </summary>
    internal sealed class CatalogItemArrays
    {
        public string[] Path;
        public string[] Name;
        public int[] Page;
        public DateTime[] CreateTime;
        public DateTime[] LastAccessTime;
        public DateTime[] ModifiedTime;
        public int[] FileCount;
        public ImageInfo[] ImageInfo;
        public MovieInfo[] MovieInfo;
        public CatalogForm.LoadResult[] LoadResult;
        public long[] Size;
        public bool[] Mask;

        public static CatalogItemArrays Allocate(int count)
        {
            return new CatalogItemArrays
            {
                Path = new string[count],
                Name = new string[count],
                Page = new int[count],
                CreateTime = new DateTime[count],
                LastAccessTime = new DateTime[count],
                ModifiedTime = new DateTime[count],
                FileCount = new int[count],
                ImageInfo = new ImageInfo[count],
                MovieInfo = new MovieInfo[count],
                LoadResult = new CatalogForm.LoadResult[count],
                Size = new long[count],
                Mask = new bool[count],
            };
        }

        /// <summary>12 本すべてがあり、長さが count か</summary>
        public bool HasLength(int count)
        {
            return Path?.Length == count && Name?.Length == count && Page?.Length == count &&
                CreateTime?.Length == count && LastAccessTime?.Length == count && ModifiedTime?.Length == count &&
                FileCount?.Length == count && ImageInfo?.Length == count && MovieInfo?.Length == count &&
                LoadResult?.Length == count && Size?.Length == count && Mask?.Length == count;
        }

        /// <summary>長さ newCount の配列を作り、先頭の count 個を写す。増えた部分のページは -1（未設定）</summary>
        public CatalogItemArrays Grow(int count, int newCount)
        {
            var result = Allocate(newCount);
            Array.Copy(Path, result.Path, count);
            Array.Copy(Name, result.Name, count);
            Array.Copy(Page, result.Page, count);
            Array.Copy(CreateTime, result.CreateTime, count);
            Array.Copy(LastAccessTime, result.LastAccessTime, count);
            Array.Copy(ModifiedTime, result.ModifiedTime, count);
            Array.Copy(FileCount, result.FileCount, count);
            Array.Copy(ImageInfo, result.ImageInfo, count);
            Array.Copy(MovieInfo, result.MovieInfo, count);
            Array.Copy(LoadResult, result.LoadResult, count);
            Array.Copy(Size, result.Size, count);
            Array.Copy(Mask, result.Mask, count);
            for (var i = count; i < newCount; i++) result.Page[i] = -1;
            return result;
        }
    }
}
