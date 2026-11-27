using Alteridem.WinTouch;
using Common;
using GenerarClasses;
using SharpCompress.Common;
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
        private void getMovieHeader(out MovieInfo movieInfo, string zipPath, FileStat stat = null)
        {
            movieInfo = null;

            byte[] data;
            if (thumbnailCache != null && GPSizeThumbnail.TryGetWithIgnoredIndex(thumbnailCache, zipPath, stat?.LastWriteTime, out data))
            {
                try
                {
                    movieInfo = MovieInfo.FromData(data);
                }
                catch { }
            }

            if (movieInfo == null)
            {
                movieInfo = MovieInfo.FromFileCached(zipPath); // サムネイル作成（MovieThumbnailLoader.GetThumbnail）と結果を共有する
            }
        }

        internal enum LoadResult { Success, FileNotFound, LoadError, NotYet }

        private bool GetHeader(string zipPath, bool isDir, out bool plainFile, out int filecount, out LoadResult loadResult, out long fileSize, out ImageInfo imageInfo, out MovieInfo movieInfo)
            => GetHeader(zipPath, isDir, null, out plainFile, out filecount, out loadResult, out fileSize, out imageInfo, out movieInfo);

        /// <param name="stat">ZipPla CE: 呼び出し側で FileStat.TryGet を済ませていれば渡す（isDir はそれと同じ値にすること）</param>
        private bool GetHeader(string zipPath, bool isDir, FileStat stat, out bool plainFile, out int filecount, out LoadResult loadResult, out long fileSize, out ImageInfo imageInfo, out MovieInfo movieInfo)
        {
            filecount = MetaPageCount_NotBook;
            loadResult = LoadResult.NotYet;
            fileSize = -1;
            imageInfo = null;
            movieInfo = null;
            plainFile = false;

            if (zipPath.Contains(Path.AltDirectorySeparatorChar)) return false;

            try
            {
                if (isDir)
                {
                }
                else
                {
                    fileSize = stat != null ? stat.Length : (new FileInfo(zipPath)).Length;
                }

                bool? ffmpegExists = null;
                if (isDir || PackedImageLoader.Supports(zipPath))
                {
                    getFileCount(out filecount, zipPath, stat);
                }
                else if (ImageLoader.SupportsAtLeastThumbnailReading(zipPath, ref ffmpegExists))
                {
                    getImageHeader(out imageInfo, zipPath, stat);
                }
                else if (MovieThumbnailLoader.Supports(zipPath) && (ffmpegExists ?? MovieThumbnailLoader.ffmpegExists()))
                {
                    getMovieHeader(out movieInfo, zipPath, stat);
                }
                else
                {
                    plainFile = true;
                }
            }
            catch
            {
                loadResult = LoadResult.LoadError;
            }

            return true;
        }

        private bool SubfolderIsFullDisplayed() => SubfolderMode == SubfolderMode.Display || SubfolderMode == SubfolderMode.DisplayAndSearch;

        private static bool SubfolderIsSearched(SubfolderMode subfolderMode) => subfolderMode == SubfolderMode.DisplayAndSearch || subfolderMode == SubfolderMode.DisplayHeaderAndSearch || subfolderMode == SubfolderMode.Search;

        private bool SubfolderIsSearched() => SubfolderIsSearched(SubfolderMode);

        // サイズ取得は基本的に最初に行うが、最新の情報を得るためここで再取得。またフォルダについてはここで初めて取得
        public const int MetaPageCount_NotLoaded = -1;

        public const int MetaPageCount_NotBook = -2;

        internal Bitmap GetThumbnail(string zipPath, bool isDir, BackgroundWorker backgroundWorker, out bool plainFile, out int filecount, out LoadResult loadError, out long fileSize, out ImageInfo imageInfo, ref MovieInfo movieInfo, bool existsHeader)
        {
            var altSepPos = zipPath.IndexOf(Path.AltDirectorySeparatorChar);

            movieInfo = null;
            plainFile = false;

            if (altSepPos >= 0)
            {
                filecount = MetaPageCount_NotBook;
                fileSize = -1;
                try
                {
                    var path = zipPath.Substring(0, altSepPos);
                    var innerName = zipPath.Substring(altSepPos + 1);

                    if (PackedImageLoader.Supports(path))
                    {
                        loadError = LoadResult.Success;
                        var commonLoader = loadingPackedImageLoader != null;
                        PackedImageLoaderWithEntries loader;

                        if (commonLoader)
                        {
                            loader = loadingPackedImageLoader;
                        }
                        else
                        {
                            var pureLoader = CreatePackedImageLoader(path);
                            try
                            {
                                loader = new PackedImageLoaderWithEntries(pureLoader);
                            }
                            catch
                            {
                                pureLoader.Dispose();
                                throw;
                            }
                        }
                        try
                        {
                            var entry = loader.FindEntry(innerName) ?? throw new FileNotFoundException(); // 名前の辞書で探す（K9）
                            // 大きな JPEG は縮小してデコードする（画像ファイルの A2 と同じくサムネイルの 3 倍以上）（K9）
                            var request = new ReducedJpegDecodeRequest(originalSize => ThumbnailReducedDecodeSize());
                            Bitmap img = null;
                            if (commonLoader)
                            {
                                // 書庫からの取り出しだけを共有のローダーのロックの中で行い、デコードはロックの外で並列にする（K9、ビューアの V1 と同じ）
                                MemoryStream extracted = null;
                                string fileNameForType = null;
                                long length = -1;
                                var animation = false;
                                lock (loadingPackedImageLoaderLocker)
                                {
                                    var packedImageLoader = loader.PackedImageLoader;
                                    if (packedImageLoader == null) throw new ObjectDisposedException(nameof(PackedImageLoader));
                                    animation = packedImageLoader.Animation;
                                    if (!packedImageLoader.TryExtractEntry(entry, out extracted, out fileNameForType, out length))
                                    {
                                        img = OpenArchiveImageForThumbnail(packedImageLoader, entry, request);
                                    }
                                }
                                if (img == null)
                                {
                                    using (extracted)
                                    {
                                        ReducedJpegDecodeRequest.Current = request;
                                        try
                                        {
                                            img = PackedImageLoader.DecodeExtracted(extracted, fileNameForType, animation, length);
                                        }
                                        finally
                                        {
                                            ReducedJpegDecodeRequest.Current = null;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                img = OpenArchiveImageForThumbnail(loader.PackedImageLoader, entry, request);
                            }
                            try
                            {
                                imageInfo = ImageLoader.GetImageInfo(img);
                                // 縮小してデコードした場合も、画像の情報は原寸の大きさにする
                                if (request.Reduced && imageInfo != null) imageInfo = new ImageInfo(request.OriginalSize, imageInfo.BitPerPixel);
                                return DoJustClipping(img, needToResize: true);
                            }
                            finally
                            {
                                img.Dispose();
                            }
                        }
                        finally
                        {
                            if (!commonLoader && loader != null) loader.Dispose();
                        }
                    }
                    else if (MovieThumbnailLoader.Supports(path) && MovieThumbnailLoader.ffmpegExists())
                    {
                        var pos = TimeSpan.Parse(innerName);
                        return GetMovieThumbnail(path, backgroundWorker, out filecount, out loadError, out fileSize, out imageInfo, ref movieInfo, t => pos);
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
                catch
                {
                    loadError = LoadResult.LoadError;
                    imageInfo = null;
                    movieInfo = null;
                    if (backgroundWorker != null && backgroundWorker.CancellationPending) return null;
                    var result = Program.GetErrorImage(tvCatalog.ThumbnailWidth, tvCatalog.ThumbnailHeight);
                    return result;
                }
            }
            else
            {
                if (thumbnailCache != GPSizeThumbnail.AlternateDataStream)
                {
#if DEBUG
                    if (NTFSMultiStream.Exists(zipPath, GPSizeThumbnail.STREAM_NAME) && !GPSizeThumbnail.TryDeleteAlternateDataStream(zipPath))
                    {
                        MessageBox.Show("Delete cache error");
                    }
#else
#if PERFLOG
                    var perfAds = PerfTrace.Now();
#endif
                    GPSizeThumbnail.TryDeleteLeftoverAlternateDataStream(zipPath);
#if PERFLOG
                    PerfTrace.AddStage("deleteADS", PerfTrace.Now() - perfAds);
#endif
#endif
                }

                loadError = LoadResult.NotYet;
                fileSize = -1;
                imageInfo = null;

                try
                {
                    bool? ffmpegExists = null;
                    if (isDir || PackedImageLoader.Supports(zipPath))
                    {
                        Bitmap result;
                        if (isDir && SubfolderIsFullDisplayed() || !isDir && ArchiveLoadLevel == ItemLoadLevel.Full)
                        {
                            result = GetArchiveThumbnail(zipPath, backgroundWorker, out filecount, out loadError, out fileSize, out imageInfo);
                        }
                        else
                        {
                            if (!existsHeader)
                            {
                                getFileCount(out filecount, zipPath);
                            }
                            else
                            {
                                filecount = -1;
                            }
                            result = null;
                        }
                        return result;
                    }
                    else if (ImageLoader.SupportsAtLeastThumbnailReading(zipPath, ref ffmpegExists))
                    {
                        var imageLoad = ImageLoadLevel == ItemLoadLevel.Full || ImageLoadLevel == ItemLoadLevel.Ignore;
                        var otherLoad = OtherLoadLevel == ItemLoadLevel.Full || OtherLoadLevel == ItemLoadLevel.Ignore;
                        bool loadThumbnail;
                        var isImage = true;
                        if (imageLoad && otherLoad)
                        {
                            loadThumbnail = true;
                        }
                        else
                        {
                            isImage = ImageLoader.SupportsImageAtLeastThumbnailReading(zipPath, ref ffmpegExists);
                            loadThumbnail = isImage ? imageLoad : otherLoad;
                        }

                        if (loadThumbnail)
                        {
                            return GetImageThumbnail(zipPath, backgroundWorker, out filecount, out loadError, out fileSize, out imageInfo);
                        }
                        else
                        {
                            if (isImage && !existsHeader)
                            {
                                getImageHeader(out imageInfo, zipPath);
                            }
                            filecount = MetaPageCount_NotBook;
                            return null;
                        }
                    }
                    else if (MovieThumbnailLoader.Supports(zipPath) && (ffmpegExists ?? MovieThumbnailLoader.ffmpegExists()))
                    {
                        if (VideoLoadLevel == ItemLoadLevel.Full)
                        {
                            return GetMovieThumbnail(zipPath, backgroundWorker, out filecount, out loadError, out fileSize, out imageInfo, ref movieInfo);
                        }
                        else
                        {
                            if (!existsHeader)
                            {
                                getMovieHeader(out movieInfo, zipPath);
                            }
                            filecount = MetaPageCount_NotBook;
                            return null;
                        }
                    }
                    else
                    {
                        plainFile = true;
                        if (OtherLoadLevel == ItemLoadLevel.Full)
                        {
                            return GetOtherFileThumbnail(zipPath, backgroundWorker, out filecount, out loadError, out fileSize, out imageInfo);
                        }
                        else
                        {
                            filecount = MetaPageCount_NotBook;
                            return null;
                        }
                    }
                }
                catch
                {
                    loadError = LoadResult.LoadError;
                    imageInfo = null;
                    movieInfo = null;
                    filecount = MetaPageCount_NotBook;
                    if (backgroundWorker != null && backgroundWorker.CancellationPending) return null;
                    var result = Program.GetErrorImage(tvCatalog.ThumbnailWidth, tvCatalog.ThumbnailHeight);
                    return result;
                }
            }
        }

        private static Func<TimeSpan, TimeSpan> getpositionProvider(string path)
        {
            if (path == null) return positionProviderForMovie;
            var info = new ZipPlaInfo(path);
            var ti = info.ThumbnailInfo;
            if (ti == null)
            {
                return positionProviderForMovie; // null にすると thumbnail フィルタ
            }
            else
            {
                return t => TimeSpan.FromSeconds(ti.Item1 + ti.Item2);
            }
        }

        private static int dataToFileCount(byte[] data)
        {
            using (var m = new MemoryStream(data))
            using (var r = new BinaryReader(m, Encoding.ASCII))
            {
                if (!r.ReadChars(4).SequenceEqual("BOOK")) throw new Exception("Not book data");
                return r.ReadInt32();
            }
        }

        private static void dataToBookData(byte[] data, out int fileCount, out bool zipPlaImageData)
        {
            using (var m = new MemoryStream(data))
            using (var r = new BinaryReader(m, Encoding.ASCII))
            {
                if (!r.ReadChars(4).SequenceEqual("BOOK")) throw new Exception("Not book data");
                fileCount = r.ReadInt32();
                zipPlaImageData = r.ReadBoolean();
            }
        }

        private static byte[] fileCountToData(int fileCount, bool zipPlaImageData)
        {
            using (var m = new MemoryStream())
            using (var w = new BinaryWriter(m, Encoding.ASCII))
            {
                w.Write("BOOK".ToCharArray());
                w.Write(fileCount);
                w.Write(zipPlaImageData);
                return m.ToArray();
            }
        }

        private Bitmap GetMovieThumbnail(string zipPath, BackgroundWorker backgroundWorker, out int filecount, out LoadResult loadError, out long fileSize, out ImageInfo imageInfo, ref MovieInfo movieInfo, Func<TimeSpan, TimeSpan> positionProvider = null)
        {
            filecount = MetaPageCount_NotBook;
            try
            {
                fileSize = (new FileInfo(zipPath)).Length;
            }
            catch
            {
                fileSize = -1;
            }

            try
            {

                loadError = LoadResult.Success;
                if (positionProvider != null)
                {
                    using (var img = MovieThumbnailLoader.GetThumbnail(zipPath, sizeProviderForVideo, positionProvider, backgroundWorker, TimeSpan.FromSeconds(10), ref movieInfo))
                    {
                        imageInfo = null;
                        return DoJustClipping(img, needToResize: false);
                    }
                }
                else
                {
                    var info = new ZipPlaInfo(zipPath);
                    var ti = info.ThumbnailInfo;
                    int index;
                    if (ti == null || ti.Item1 < 0)
                    {
                        index = -1;
                    }
                    else
                    {
                        index = ti.Item1;
                    }

                    Bitmap result;
                    byte[] data;
                    Fraction thumbnailCachePixelRatio;
                    if (thumbnailCache != null && GPSizeThumbnail.TryGet(thumbnailCache, zipPath, index, tvCatalog.ThumbnailSize, currentClipMode == ClipMode.Letterbox, out result, out thumbnailCachePixelRatio, out data))
                    {
                        using (result)
                        {
                            imageInfo = null;
                            try
                            {
                                movieInfo = MovieInfo.FromData(data); // ここのミスをエラーにすると一度誤ったデータができると一旦消さない限り回復しなくなる
                                return DoJustClipping(result, needToResize: true, pixelRatio: thumbnailCachePixelRatio);
                            }
                            catch
                            {
                                // usingt と重複
                                //if (result != null) result.Dispose();
                            }
                        }
                    }

                    var pp = index < 0 ? positionProviderForMovie : new Func<TimeSpan, TimeSpan>(t => TimeSpan.FromSeconds(index));
                    // 位置を指定していない動画は、長さの 1/3 以降の最初のキーフレームで作る（速い）。位置を指定した動画は、その時刻ちょうどのコマ
                    using (var img = MovieThumbnailLoader.GetThumbnail(zipPath, /*sizeProvider*/ null, pp, backgroundWorker, TimeSpan.FromSeconds(10), ref movieInfo, nextKeyframe: index < 0))
                    {
                        imageInfo = null;
                        Bitmap resized;
                        Fraction resizedRatio;
                        if (thumbnailCache != null && GPSizeThumbnail.TrySet(thumbnailCache, zipPath, index, tvCatalog.ThumbnailSize, currentClipMode == ClipMode.Letterbox, true, img, movieInfo.SAR, movieInfo.ToData(), out resized, out resizedRatio))
                        {
                            try
                            {
                                return DoJustClipping(resized, needToResize: true, pixelRatio: resizedRatio);
                            }
                            finally
                            {
                                resized.Dispose();
                            }
                        }
                        else
                        {
                            return DoJustClipping(img, needToResize: true, pixelRatio: movieInfo.SAR);
                        }
                    }
                }
            }
            catch// (Exception e)
            {
                loadError = LoadResult.LoadError;
                imageInfo = null;
                movieInfo = null;
                if (backgroundWorker != null && backgroundWorker.CancellationPending) return null;
                var result = Program.GetErrorImage(tvCatalog.ThumbnailWidth, tvCatalog.ThumbnailHeight);
                return result;
            }
        }

        private Size sizeProviderForVideo(Fraction srcFraction, Size size)
        {
            // srcFraction を Size にキャストすると極めて小さい比のようになるため
            // sizeProvider(Size srcSize, ClipMode localClipMode) が常にサムネイルにフィットしたサイズを返してしまう
            // return sizeProvider(srcFraction, currentClipMode);

            var sizeFraction = (Fraction)size;

            if (srcFraction == sizeFraction)
            {
                return sizeProvider(size, currentClipMode);
            }
            else if (srcFraction > sizeFraction)
            {
                return sizeProvider(new Size(size.Width, (size.Height * sizeFraction / srcFraction).FloorOfPlusHalf()), currentClipMode);
            }
            else
            {
                return sizeProvider(new Size((size.Width * srcFraction / sizeFraction).FloorOfPlusHalf(), size.Height), currentClipMode);
            }
        }

        private Size sizeProvider(Size srcSize, ClipMode localClipMode)
        {
            var tw = tvCatalog.ThumbnailWidth;
            var th = tvCatalog.ThumbnailHeight;
            var sw = srcSize.Width;
            var sh = srcSize.Height;


            bool land, port, lett;

            land = (2 * th - 1) * (2 * sw - 1) >= (2 * tw + 1) * (2 * sh + 1);
            port = (2 * th + 1) * (2 * sw + 1) <= (2 * tw - 1) * (2 * sh - 1);
            lett = localClipMode == ClipMode.Letterbox;

            // ソースのアスペクト比に誤差がなければこちらでも良い
            // キャッシュを使わない場合はこちらを採用するということも考えられるが、
            // 外部プログラムがアスペクト比に誤差を与えた場合にも
            // 隙間を埋める処理を行うのは悪い選択肢ではないと判断しコメントアウト

            if (land && lett || port && !lett)
            {
                return new Size(tw, (2 * sh * tw + sw) / (2 * sw));
            }
            else if (land && !lett || port && lett)
            {
                return new Size((2 * sw * th + sh) / (2 * sh), th);
            }
            else
            {
                return new Size(tw, th);
            }
        }

        private static TimeSpan positionProviderForMovie(TimeSpan srcPosition)
        {
            return TimeSpan.FromSeconds(srcPosition.TotalSeconds / 3);
        }

        private Bitmap GetOtherFileThumbnail(string zipPath, BackgroundWorker backgroundWorker, out int filecount, out LoadResult loadError, out long fileSize, out ImageInfo imageInfo)
        {
            filecount = MetaPageCount_NotBook;
            fileSize = -1;
            imageInfo = null;
            try
            {
                loadError = LoadResult.FileNotFound;
                imageInfo = null;
                try
                {
                    fileSize = (new FileInfo(zipPath)).Length;
                }
                catch { }
                var b = GetFileBackBrush(zipPath);
                {
                    return getImageWithFileIInfo(tvCatalog.ThumbnailWidth, tvCatalog.ThumbnailHeight,
                        /*Brushes.LightGray*/ b);//,
                }
            }
            catch
            {
                loadError = LoadResult.LoadError;
                if (backgroundWorker != null && backgroundWorker.CancellationPending) return null;
                var result = Program.GetErrorImage(tvCatalog.ThumbnailWidth, tvCatalog.ThumbnailHeight);
                return result;
            }

        }

        private Bitmap GetImageThumbnail(string zipPath, BackgroundWorker backgroundWorker, out int filecount, out LoadResult loadError, out long fileSize, out ImageInfo imageInfo)
        {
            filecount = MetaPageCount_NotBook;
            try
            {
                fileSize = (new FileInfo(zipPath)).Length;
            }
            catch
            {
                fileSize = -1;
            }
            try
            {
                Bitmap img;
                GPSizeThumbnail.FocusCache focusCache = null; // キャッシュファイルの注目点の記録（P4）
                if (thumbnailCache != null && ImageLoader.SupportsFullReading(zipPath, ffmpegExists: true))
                {
                    byte[] data;
                    imageInfo = null;
#if PERFLOG
                    var perfT = PerfTrace.Now();
#endif
                    var cacheHit = GPSizeThumbnail.TryGet(thumbnailCache, zipPath, tvCatalog.ThumbnailSize, currentClipMode == ClipMode.Letterbox, out img, out data, out focusCache) && ImageInfo.TryParseData(data, out imageInfo);
#if PERFLOG
                    PerfTrace.AddStage(cacheHit ? "cacheGetHit" : "cacheGetMiss", PerfTrace.Now() - perfT);
#endif
                    if (!cacheHit)
                    {
                        if (img != null) img.Dispose();
#if PERFLOG
                        perfT = PerfTrace.Now();
#endif
                        // 大きな JPEG は縮小してデコードする（A2）。キャッシュの作り方が変わらないよう、
                        // サムネイルの 3 倍以上の大きさを確保できる場合だけ（画像情報は元ファイルのヘッダから取る）
                        ImageInfo headerInfo = null; // 縮小デコードで開いたときに読んだヘッダ（K19）
                        img = ImageInfo.Supports(zipPath) ? ImageLoader.TryLoadReducedJpeg(zipPath, ThumbnailReducedDecodeSize(), out headerInfo) : null;
                        if (img == null) img = ImageLoader.GetFullBitmap(zipPath);
                        imageInfo = ImageInfo.Supports(zipPath) ? (headerInfo ?? new ImageInfo(zipPath)) : ImageLoader.GetImageInfo(img);
#if PERFLOG
                        PerfTrace.AddStage("decodeFull", PerfTrace.Now() - perfT);
                        perfT = PerfTrace.Now();
#endif
                        Bitmap img2;
                        if (GPSizeThumbnail.TrySet(thumbnailCache, zipPath, -1, tvCatalog.ThumbnailSize, currentClipMode == ClipMode.Letterbox,
                            !ImageLoader.IsLowLoad(zipPath), img, imageInfo.ToData(), out img2))
                        {
                            img.Dispose();
                            img = img2;
                        }
#if PERFLOG
                        PerfTrace.AddStage("cacheSet", PerfTrace.Now() - perfT);
#endif
                    }
                    if (img == null) imageInfo = null;
                    //imageInfo = img != null ? new ImageInfo(zipPath) : null; // img からではなくバイナリから取得
                }
                else
                {
#if PERFLOG
                    var perfT = PerfTrace.Now();
#endif
                    // 大きな JPEG は縮小してデコードする（A2）
                    ImageInfo headerInfo = null; // 縮小デコードで開いたときに読んだヘッダ（K19）
                    img = ImageInfo.Supports(zipPath) ? ImageLoader.TryLoadReducedJpeg(zipPath, ThumbnailReducedDecodeSize(), out headerInfo) : null;
                    if (img != null) imageInfo = headerInfo ?? new ImageInfo(zipPath);
                    else img = ImageLoader.GetAtLeastThumbnailBitmap(zipPath, tvCatalog.ThumbnailSize, true, out imageInfo);
#if PERFLOG
                    PerfTrace.AddStage("decodeNoCache", PerfTrace.Now() - perfT);
#endif
                }

                if (img != null)
                {
                    using (img)
                    {
                        loadError = LoadResult.Success;
#if PERFLOG
                        var perfClip = PerfTrace.Now();
#endif
                        var result = DoJustClipping(img, needToResize: true, focusCache: focusCache);
#if PERFLOG
                        PerfTrace.AddStage("clip", PerfTrace.Now() - perfClip);
#endif
                        return result;
                    }
                }
                else // サムネイルしかサポートしていない形式でサムネイルを持たないなど想定内の読み取りミスは例外を投げず null を返す
                {
                    loadError = LoadResult.NotYet;
                    return null;
                }
            }
            catch
            {
                loadError = LoadResult.LoadError;
                imageInfo = null;
                if (backgroundWorker != null && backgroundWorker.CancellationPending) return null;
                var result = Program.GetErrorImage(tvCatalog.ThumbnailWidth, tvCatalog.ThumbnailHeight);
                return result;
            }
        }

        // サムネイル用の縮小デコードで確保する大きさ。キャッシュ（GPSizeThumbnail）は表示サイズの √2 倍を最大に作り、
        // 渡された画像がその 2 倍に満たないと作り方が変わるので、余裕を見て表示サイズの 3 倍にする（A2）
        /// <summary>
        /// ThumbViewerItem が作ったサムネイルの読み込み結果を LoadResultArray に戻す（UI スレッドで呼ぶ）（A11）。
        /// これでサムネイルのない項目（読めないファイルなど）にアイコンが描かれ、読めた項目には重ならない。
        /// </summary>
        /// <summary>
        /// サムネイルのメモリキャッシュ（A3）で、一覧を作り直しても使い回してよいかを確かめるための印（K26）。
        /// ファイルの更新日時。書庫の中・動画の位置・フォルダ（中身で表紙が変わる）・不明なときは 0（作り直したら使わない）
        /// </summary>
        internal long GetThumbnailStamp(int dataIndex, string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOf(Path.AltDirectorySeparatorChar) >= 0) return 0;
            if (path[path.Length - 1] == Path.DirectorySeparatorChar) return 0;
            lock (ZipPathArrayLocker)
            {
                var zpa = ZipPathArray;
                var mta = ModifiedTimeArray;
                var zna = ZipNameArray;
                if (zpa == null || mta == null || zna == null || dataIndex < 0 || dataIndex >= zpa.Length || dataIndex >= mta.Length || dataIndex >= zna.Length) return 0;
                if (zpa[dataIndex] != path) return 0;
                var name = zna[dataIndex];
                if (string.IsNullOrEmpty(name) || name[name.Length - 1] == Path.DirectorySeparatorChar) return 0;
                var time = mta[dataIndex];
                return time == DateTime.MinValue ? 0 : time.Ticks;
            }
        }

        /// <summary>
        /// サムネイルの見た目を決める設定（K26）。これが同じなら、一覧を作り直してもメモリキャッシュのサムネイルを使い回す。
        /// サムネイルの大きさはキャッシュから取り出すときに比べる
        /// </summary>
        private string getThumbnailAppearanceKey()
        {
            return string.Join("|", currentClipMode, archiveLoadLevel, imageLoadLevel, videoLoadLevel, otherLoadLevel, SubfolderMode,
                thumbnailCache ?? "", packedImageLoaderSearchMode, packedImageLoaderLeftHierarchies, SystemColors.Window.ToArgb());
        }

        internal void ReportThumbnailLoadResult(int dataIndex, string path, LoadResult result)
        {
            lock (ZipPathArrayLocker)
            {
                var zpa = ZipPathArray;
                var lra = LoadResultArray;
                if (zpa == null || lra == null || dataIndex < 0 || dataIndex >= zpa.Length || dataIndex >= lra.Length) return;
                if (zpa[dataIndex] != path) return;
                lra[dataIndex] = result;
            }
        }

        /// <summary>
        /// 書庫のサムネイル用にページを読む。minSize が空でなければ、JPEG をその大きさ以上の範囲で縮小デコードしてよいことにする。
        /// 表紙用の画像ファイル（画像の大きさを画像情報として使う）はここを通さない。
        /// </summary>
        private Bitmap OpenArchiveImageForThumbnail(PackedImageLoader loader, PackedImageLoader.PackedImageEntry entry, Size minSize)
        {
            if (minSize.IsEmpty) return loader.OpenImageStream(entry);
            return OpenArchiveImageForThumbnail(loader, entry, new ReducedJpegDecodeRequest(originalSize => minSize));
        }

        private Bitmap OpenArchiveImageForThumbnail(PackedImageLoader loader, PackedImageLoader.PackedImageEntry entry, ReducedJpegDecodeRequest request)
        {
            ReducedJpegDecodeRequest.Current = request;
#if PERFLOG
            var perfDecode = PerfTrace.Now();
#endif
            try
            {
                return loader.OpenImageStream(entry);
            }
            finally
            {
#if PERFLOG
                PerfTrace.AddStage("archiveDecode", PerfTrace.Now() - perfDecode); // 取り出しとデコード（表紙を選ぶまでに読んだページの数だけ）
#endif
                ReducedJpegDecodeRequest.Current = null;
            }
        }

        private Size ThumbnailReducedDecodeSize()
        {
            var size = tvCatalog.ThumbnailSize;
            return new Size(size.Width * 3, size.Height * 3);
        }

        private Rectangle Round(RectangleF rect) { return new Rectangle((int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height)); }

        /// <summary>注目点。キャッシュファイルに記録があれば使い、なければ計算して記録する（P4）</summary>
        private static Point getFocusWithCache(Bitmap src, Rectangle rectangle, Fraction pixelRatio, GPSizeThumbnail.FocusCache focusCache)
        {
            if (focusCache != null && focusCache.TryGetFocus(src.Size, rectangle, pixelRatio, out var cached)) return cached;
            var cell = BitmapAnalyzer.GetFocusCell(src, rectangle, pixelRatio);
            focusCache?.Add(src.Size, rectangle, pixelRatio, cell);
            return BitmapAnalyzer.FocusCellToPoint(cell, rectangle);
        }

        private Bitmap DoJustClipping(Bitmap src, bool needToResize, ClipMode? localClipMode = null, GPSizeThumbnail.FocusCache focusCache = null)
        {
            return DoJustClipping(src, needToResize, 1, localClipMode, focusCache);
        }

        /// <param name="focusCache">キャッシュファイルの注目点の記録。あれば PlaClip の注目点の計算を省き、なければ計算した結果を記録する（P4）</param>
        private Bitmap DoJustClipping(Bitmap src, bool needToResize, Fraction pixelRatio, ClipMode? localClipMode = null, GPSizeThumbnail.FocusCache focusCache = null)
        {
            if (pixelRatio == 0) pixelRatio = 1;

            var tvCatalogThumbnailSize = tvCatalog.ThumbnailSize;
            if (needToResize && pixelRatio == 1 && src != null && src.Size == tvCatalogThumbnailSize)
            {
                return DoJustClipping(src, needToResize: false, pixelRatio: 1, localClipMode: localClipMode, focusCache: focusCache);
            }

            var clipMode = localClipMode != null ? (ClipMode)localClipMode : currentClipMode;
            Bitmap result = new Bitmap(tvCatalogThumbnailSize.Width, tvCatalogThumbnailSize.Height, PixelFormat.Format24bppRgb);
            try
            {
                // 自前のアルゴリズムを使う方法
                using (var g = Graphics.FromImage(result))
                {

                    switch (clipMode)
                    {
                        case ClipMode.Letterbox:
                            {

                                // これで概ね良いが、絶対的なサイズに応じて無意味な隙間を埋める処理をするので
                                // 厳密には正しくない
                                //var size = sizeProvider((Fraction)src.Size * pixelRatio, clipMode);

                                // こちらが正しい
                                Size size;
                                if (pixelRatio > 1)
                                {
                                    size = sizeProvider(new Size(src.Width, (src.Height / pixelRatio).FloorOfPlusHalf()), clipMode);
                                }
                                else
                                {
                                    size = sizeProvider(new Size((src.Width * pixelRatio).FloorOfPlusHalf(), src.Height), clipMode);
                                }

                                var w = size.Width;
                                var h = size.Height;
                                var poorRect = new RectangleF((result.Width - w) / 2, (result.Height - h) / 2, w, h);
                                g.FillRectangle(SystemBrushes.Window/*Brushes.White*/, new RectangleF(new PointF(), result.Size));
                                if (needToResize)
                                {
                                    // 説明用クリッピングなしサムネイルの作成
                                    g.DrawImage(src, poorRect, new RectangleF(new PointF(), src.Size), GraphicsUnit.Pixel);
                                }
                                else
                                {
                                    g.DrawImageUnscaled(src, (int)Math.Round(poorRect.X), (int)Math.Round(poorRect.Y));
                                }
                                break;
                            }
                        case ClipMode.PanAndScan:
                            {
                                var resultSize2 = (Size)((Fraction)result.Size / pixelRatio);
                                var zoomP = Math.Max(resultSize2.Width / (float)src.Width, resultSize2.Height / (float)src.Height);
                                var wP = resultSize2.Width / zoomP;
                                var hP = resultSize2.Height / zoomP;
                                var poorRectP = new RectangleF((src.Width - wP) / 2, (src.Height - hP) / 2, wP, hP);
                                if ((src.PixelFormat & PixelFormat.Alpha) != 0)
                                {
                                    g.FillRectangle(SystemBrushes.Window/*Brushes.White*/, new RectangleF(new PointF(), result.Size));
                                }
                                if (needToResize)
                                {
                                    g.DrawImage(src, new RectangleF(new PointF(), result.Size), poorRectP, GraphicsUnit.Pixel);
                                }
                                else
                                {
                                    g.DrawImageUnscaled(src, -(int)Math.Round(poorRectP.X), -(int)Math.Round(poorRectP.Y));
                                }
                                break;
                            }
                        default:
                            {

#if PERFLOG
                                var perfFocus = PerfTrace.Now();
#endif
                                var focusPoint = getFocusWithCache(src, new Rectangle(0, 0, src.Width, src.Height), pixelRatio, focusCache);
#if PERFLOG
                                PerfTrace.AddStage("clipFocus", PerfTrace.Now() - perfFocus);
#endif
                                var resultSize2 = (Size)((Fraction)result.Size / pixelRatio);
                                var zoom = Math.Max(resultSize2.Width / (float)src.Width, resultSize2.Height / (float)src.Height);
                                var iw = src.Width;
                                var ih = src.Height;

                                var w = resultSize2.Width / zoom;
                                var h = resultSize2.Height / zoom;
                                var plaRectP = new RectangleF(focusPoint.X - w / 2, focusPoint.Y - h / 2, w, h);
                                if (plaRectP.Left < 0) plaRectP.X = 0;
                                else if (plaRectP.Right > iw) plaRectP.X = iw - w;
                                if (plaRectP.Top < 0) plaRectP.Y = 0;
                                else if (plaRectP.Bottom > ih) plaRectP.Y = ih - h;
#if DEBUG
                                var p = new PointF((focusPoint.X - plaRectP.X) * result.Width / w, (focusPoint.Y - plaRectP.Y) * result.Height / h);
                                var r = (float)Math.Sqrt(result.Width * result.Height) / 10;
#endif
                                if ((src.PixelFormat & PixelFormat.Alpha) != 0)
                                {
                                    g.FillRectangle(SystemBrushes.Window/*Brushes.White*/, new RectangleF(new PointF(), result.Size));
                                }
                                if (needToResize)
                                {
                                    // このオフセットが厳密に正しいものと思われる
                                    // width や height が soruce と一致する場合にオフセットがなくても正常に描画されるので一見筋が通らないように思えるが、
                                    // 実験の結果、一致する場合は例外的な処理が行われるらしく、オフセットを仕掛けておけば常に期待通りの結果が得られる
                                    // レターボックスやパンスキャンではその例外処理が有効に働くのでオフセットは必須ではない

                                    var zoom2 = Math.Max(result.Width / (float)src.Width, result.Height / (float)src.Height);

                                    var areaRect = plaRectP; // 自前の縮小には GDI+ 用の補正をする前の範囲を渡す
                                    var pOffset = 0.5f * (1 / zoom2 - 1);
                                    plaRectP.Width -= pOffset;
                                    plaRectP.Height -= pOffset;
#if PERFLOG
                                    var perfDraw = PerfTrace.Now();
#endif
                                    // 縮小は GDI+ を使わない面積平均で（GDI+ の DrawImage はスレッドをまたいで直列になる）（P4）
                                    g.Flush();
                                    if (!AreaAverageResizer.TryDraw(src, areaRect, result, new Rectangle(new Point(), result.Size)))
                                    {
                                        g.DrawImage(src, new Rectangle(new Point(), result.Size), plaRectP, GraphicsUnit.Pixel);
                                    }
#if PERFLOG
                                    PerfTrace.AddStage("clipDraw", PerfTrace.Now() - perfDraw);
#endif
                                }
                                else
                                {
                                    g.DrawImageUnscaled(src, -(int)Math.Round(0.0 + plaRectP.X), -(int)Math.Round(0.0 + plaRectP.Y));
                                }
#if DEBUG
                                using (var pen = new Pen(Color.Cyan, r / 5))
                                {
                                    g.DrawEllipse(pen, new RectangleF(p.X - r, p.Y - r, 2 * r, 2 * r));
                                }
#endif
                                break;
                            }
                    }
                    return result;
                }
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private Bitmap GetArchiveThumbnail(string zipPath, double ratio)
        {
            int fileCount;
            long fileSize;
            ImageInfo imageInfo;
            LoadResult loadError;
            return GetArchiveThumbnail(zipPath, null, -1, -1, (float)ratio, out fileCount, out loadError, out fileSize, out imageInfo);
        }

        private Bitmap GetArchiveThumbnail(string zipPath, BackgroundWorker backgroundWorker, out int filecount, out LoadResult loadError, out long fileSize, out ImageInfo imageInfo)
        {
            return GetArchiveThumbnail(zipPath, backgroundWorker, tvCatalog.ThumbnailWidth, tvCatalog.ThumbnailHeight, -1, out filecount, out loadError, out fileSize, out imageInfo);
        }

        private static readonly bool SystemColorsWindowIsNearToWhite = ZipTag.NearToWhite(SystemColors.Window);

        private static Brush GetFileBackBrush(string path)
        {
            return SystemColorsWindowIsNearToWhite ? Brushes.LightGray : Brushes.DimGray;// Brushes.LightYellow;;
        }

        private static Brush GetDirectoryBackBrush(string path)
        {
            return SystemColorsWindowIsNearToWhite ? Brushes.LightYellow : Brushes.DarkSlateGray;// Brushes.LightYellow;;// Brushes.LightYellow;;
        }

        private static Brush GetFileDirectoryForeBrush(string path)
        {
            return SystemColorsWindowIsNearToWhite ? Brushes.Black : Brushes.White;// Brushes.LightYellow;
        }

        private Bitmap GetArchiveThumbnail(string zipPath, BackgroundWorker backgroundWorker, int width, int height, double ratio, out int filecount, out LoadResult loadError, out long fileSize, out ImageInfo imageInfo)
        {
            var givenSize = width > 0 && height > 0;
            loadError = LoadResult.Success;
            Bitmap MaxPointImage = null;
            GPSizeThumbnail.FocusCache archiveFocusCache = null; // 書庫のキャッシュファイルの注目点の記録（P4）
            var isDirectory = false;
            fileSize = -1;
            try
            {
                isDirectory = Directory.Exists(zipPath);
                if (isDirectory)
                {
                }
                else
                {
                    fileSize = new FileInfo(zipPath).Length;
                }

                double MaxPoint = double.NegativeInfinity;
                RectangleF MaxPointRectangleF = RectangleF.Empty;
                double ReadNextDownPoint = 0;

                var filepath = Path.GetFileName(zipPath);
                var thumbnailInfo = (new ZipPlaInfo(filepath)).ThumbnailInfo;
                var bindingModeNullable = ZipPlaInfo.GetOnlyBindingModeFromFullName(zipPath);
                var bindingMode = bindingModeNullable == null ? currentBindingMode : (BindingMode)bindingModeNullable;

                var coverPage = thumbnailInfo?.Item1;

                var index = coverPage != null && coverPage >= 0 ? (int)coverPage : -1;
                var indexWithAIAInfo = getArchiveIndexWithAIAInfo(index);

                var singleMode = bindingMode == BindingMode.SinglePage || bindingMode == BindingMode.SinglePageWithoutScalingUp;

                ClipMode localClipMode = givenSize && index < 0 ? currentClipMode : ClipMode.PlaClip;
                if (localClipMode == ClipMode.PlaClip && singleMode) localClipMode = ClipMode.PanAndScan;

                var supportCache = givenSize && thumbnailCache != null;
                var fromCache = false;
                filecount = -1;
                var isZipPlaImageData = false;

                var requiredSize = Size.Empty;

                if (supportCache)
                {
                    // 1 : √2 を切り出して更に内側を切り出す処理をする場合、requiredRectangle はサムネイルサイズに
                    // 外接する 1: √2 の長方形になる。この場合も clipMode が letterBox であるかどうかの真理値は変わらない
                    if (localClipMode == ClipMode.PlaClip)
                    {
                        var tsW = tvCatalog.ThumbnailWidth;
                        var tsH = tvCatalog.ThumbnailHeight;
                        var tsWSqrt2 = tsW * Math.Sqrt(2);
                        if (tsWSqrt2 < tsH)
                        {
                            requiredSize = new Size((int)Math.Round(tsH / Math.Sqrt(2)), tsH);
                        }
                        else
                        {
                            requiredSize = new Size(tsW, (int)Math.Round(tsWSqrt2));
                        }
                    }
                    else
                    {
                        requiredSize = tvCatalog.ThumbnailSize;
                    }


                    Bitmap got;
                    byte[] data;
                    if (GPSizeThumbnail.TryGet(thumbnailCache, zipPath, indexWithAIAInfo,
                        requiredSize, localClipMode == ClipMode.Letterbox, out got, out data, out archiveFocusCache))
                    {
                        try
                        {
                            dataToBookData(data, out filecount, out isZipPlaImageData);
                            if (isZipPlaImageData && index != -1)
                            {
                                isZipPlaImageData = false;
                                throw new Exception();
                            }
                            MaxPointImage = got;
                            fromCache = true;
                        }
                        catch
                        {
                            if (got != null) got.Dispose();
                        }
                    }
                    data = null;
                }

                // サムネイル用に JPEG を縮小デコードするときに確保する大きさ。キャッシュ（GPSizeThumbnail）は requiredSize の √2 倍を
                // 最大に作るので、サムネイルとキャッシュの requiredSize の大きい方の √2 倍を縦横とも確保すれば、作られるキャッシュの大きさは変わらない
                // （画素は縮小の経路が変わる分だけわずかに変わる。テスト画像で平均 1.5、見た目では区別できない）
                var reducedDecodeSize = Size.Empty;
                if (givenSize)
                {
                    var thumbnailSize = tvCatalog.ThumbnailSize;
                    var baseWidth = Math.Max(thumbnailSize.Width, requiredSize.Width);
                    var baseHeight = Math.Max(thumbnailSize.Height, requiredSize.Height);
                    reducedDecodeSize = new Size((int)Math.Ceiling(baseWidth * Math.Sqrt(2)) + 1, (int)Math.Ceiling(baseHeight * Math.Sqrt(2)) + 1);
                }

                if (!fromCache)
                {
                    List<PackedImageLoader.PackedImageEntry> entries;
#if PERFLOG
                    var perfArchiveOpen = PerfTrace.Now();
#endif
                    using (var loader = CreatePackedImageLoader(zipPath))
                    {
                        // http://www.weblio.jp/content/DllImport+Attribute LogicalStringComparer インスタンスはスレッドセーフのため新たに作成する
                        //var entries = loader.GetPackedImageEntries().OrderBy(entry => entry.Path, new LogicalStringComparer());
                        entries = loader.GetPackedImageEntries();
#if PERFLOG
                        PerfTrace.AddStage("archiveOpenList", PerfTrace.Now() - perfArchiveOpen); // 書庫を開いて一覧を作る
#endif
                        filecount = entries.Count;
                        if (loader.Type != PackedImageLoader.PackType.Directory) rememberArchiveEntryCount(zipPath, filecount);

                        if (index >= 0)
                        {
                            if (backgroundWorker != null && backgroundWorker.CancellationPending)
                            {
                                imageInfo = null;
                                if (MaxPointImage != null) MaxPointImage.Dispose();
                                return null;
                            }
                            try
                            {
                                var image = OpenArchiveImageForThumbnail(loader, entries[index], reducedDecodeSize);
                                try
                                {
                                    MaxPointImage = image;


                                    var v = thumbnailInfo?.Item2;


                                    MaxPointRectangleF = getCoverRectangleByRatioFromThumbnailInfo(givenSize ? Math.Sqrt(0.5) : ratio, MaxPointImage.Size, v);



                                    ///////////
                                    //////////
                                }
                                catch
                                {
                                    image.Dispose();
                                    throw;
                                }
                            }
                            catch (ArgumentException) { }
                        }
                        else
                        {
                            // ZipPlaImageFile は手動で作成することも想定されるので削除は手軽ではない。従って ZipPlaImage と ZipPlaInfo の情報が競合した場合後者が優先されるべきである。
                            var zipPlaImageIndex = -1;
                            for (var i = 0; i < filecount; i++)
                            {
                                if (isZipPlaCoverImageFile(entries[i].Path))
                                {
                                    zipPlaImageIndex = i;
                                    break;
                                }
                            }

                            if (zipPlaImageIndex >= 0)
                            {

                                try
                                {
                                    var image = loader.OpenImageStream(entries[zipPlaImageIndex]);
                                    try
                                    {
                                        double point;
                                        RectangleF rect;
                                        GetImagePointAndEtc(out point, out rect, image, leftToRight: bindingMode == BindingMode.LeftToRight); // point はゼロ以下、SignlePage では rect は使われない
                                        if (MaxPointImage != null) MaxPointImage.Dispose();
                                        MaxPointImage = image;

                                        isZipPlaImageData = true;

                                    }
                                    catch
                                    {
                                        image.Dispose();
                                        throw;
                                    }
                                }
                                catch (ArgumentException) { }
                            }
                            else
                            {

                                foreach (var entry in entries)
                                {
                                    if (backgroundWorker != null && backgroundWorker.CancellationPending)
                                    {
                                        imageInfo = null;
                                        if (MaxPointImage != null) MaxPointImage.Dispose();
                                        return null;
                                    }
                                    try
                                    {
                                        var image = OpenArchiveImageForThumbnail(loader, entry, reducedDecodeSize);
                                        if (localClipMode != ClipMode.PlaClip)
                                        {
                                            if (MaxPointImage != null) MaxPointImage.Dispose();
                                            MaxPointImage = image;
                                            break;
                                        }
                                        try
                                        {
                                            double point;
                                            RectangleF rect;
                                            GetImagePointAndEtc(out point, out rect, image, leftToRight: bindingMode == BindingMode.LeftToRight); // point はゼロ以下、SignlePage では rect は使われない
                                            point += ReadNextDownPoint;
                                            if (point > MaxPoint)
                                            {
                                                if (MaxPointImage != null) MaxPointImage.Dispose();
                                                MaxPointImage = image;
                                                MaxPoint = point;
                                                MaxPointRectangleF = rect;
                                            }
                                        }
                                        catch
                                        {
                                            image.Dispose();
                                            throw;
                                        }
                                        if (MaxPoint >= ReadNextDownPoint - 1) break;
                                    }
                                    catch (ArgumentException) { }
                                    ReadNextDownPoint--;
                                }
                            }
                        }
                    }
                }
                else
                {
                }

                if (backgroundWorker != null && backgroundWorker.CancellationPending)
                {
                    imageInfo = null;
                    if (MaxPointImage != null) MaxPointImage.Dispose();
                    return null;
                }

                if (MaxPointImage == null)
                {
                    if (width < 0 || height < 0) throw new Exception(Message.NoReadableFile);
                    loadError = LoadResult.FileNotFound;
                    imageInfo = null;
                    if (isDirectory)
                    {
                        var b = GetDirectoryBackBrush(zipPath);
                        {
                            return getImageWithFileIInfo(width, height,
                                b);//,
                        }
                    }
                    else
                    {
                        var b = GetFileBackBrush(zipPath);
                        {
                            return getImageWithFileIInfo(width, height,
                                b);//,
                        }
                    }
                }

                var resized = false;
                if (supportCache && !fromCache)
                {
                    Bitmap img2;
                    var mpiBitmap = MaxPointImage as Bitmap;
                    if (mpiBitmap != null)
                    {
                        if (GPSizeThumbnail.TrySet(thumbnailCache, zipPath, indexWithAIAInfo, requiredSize, localClipMode == ClipMode.Letterbox, true, mpiBitmap, fileCountToData(filecount, isZipPlaImageData), out img2))
                        {
                            MaxPointImage.Dispose();
                            MaxPointImage = img2;
                            resized = true;
                        }
                    }
                    else
                    {
                        using (mpiBitmap = new Bitmap(MaxPointImage))
                        {
                            if (GPSizeThumbnail.TrySet(thumbnailCache, zipPath, indexWithAIAInfo, requiredSize, localClipMode == ClipMode.Letterbox, true, mpiBitmap, fileCountToData(filecount, isZipPlaImageData), out img2))
                            {
                                MaxPointImage.Dispose();
                                MaxPointImage = img2;
                                resized = true;
                            }
                        }
                    }
                }

                if (resized || MaxPointRectangleF.IsEmpty)
                {
                    if (index >= 0)
                    {
                        var v = thumbnailInfo?.Item2;
                        MaxPointRectangleF = getCoverRectangleByRatioFromThumbnailInfo(givenSize ? Math.Sqrt(0.5) : ratio, MaxPointImage.Size, v);
                    }
                    else if (isZipPlaImageData)
                    {
                    }
                    else
                    {
                        double point;
                        GetImagePointAndEtc(out point, out MaxPointRectangleF, MaxPointImage, leftToRight: bindingMode == BindingMode.LeftToRight); // point はゼロ以下、SignlePage では rect は使われない
                    }
                }

                if (!givenSize)
                {
                    var origRatio = (double)MaxPointImage.Width / MaxPointImage.Height;
                    if (origRatio < ratio)
                    {
                        width = MaxPointImage.Width;
                        height = (int)Math.Round(width / ratio);
                    }
                    else
                    {
                        height = MaxPointImage.Height;
                        width = (int)Math.Round(height * ratio);
                    }

                }
                Bitmap canvas;
                if (isZipPlaImageData)
                {
                    var result = DoJustClipping(MaxPointImage, needToResize: true);
                    imageInfo = ImageLoader.GetImageInfo(MaxPointImage);
                    MaxPointImage.Dispose();
                    return result;
                }
                else if (localClipMode == ClipMode.PlaClip)
                {
                    canvas = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                    try
                    {
                        using (var g = Graphics.FromImage(canvas))
                        {

                            ratio = (double)canvas.Width / canvas.Height;
                            Point focusPoint = Point.Empty;
                            float z = 0;
                            if (ratio < 0.7 || ratio > 0.714)
                            {
                                z = Math.Min(MaxPointRectangleF.Width / canvas.Width, MaxPointRectangleF.Height / canvas.Height);
                                var rectWidth = z * canvas.Width;// - zHalf;
                                var rectHeight = z * canvas.Height;// - zHalf;
#if PERFLOG
                                var perfArchiveFocus = PerfTrace.Now();
#endif
                                focusPoint = getFocusWithCache(MaxPointImage, Rectangle.Round(MaxPointRectangleF), 1, archiveFocusCache);
#if PERFLOG
                                PerfTrace.AddStage("archiveClipFocus", PerfTrace.Now() - perfArchiveFocus);
#endif
                                var newRect = new RectangleF(focusPoint.X - rectWidth / 2, focusPoint.Y - rectHeight / 2, rectWidth, rectHeight);
                                if (newRect.Left < MaxPointRectangleF.Left) newRect.X = MaxPointRectangleF.Left;
                                else if (newRect.Right > MaxPointRectangleF.Right) newRect.X = MaxPointRectangleF.Right - rectWidth;
                                if (newRect.Top < MaxPointRectangleF.Top) newRect.Y = MaxPointRectangleF.Top;
                                else if (newRect.Bottom > MaxPointRectangleF.Bottom) newRect.Y = MaxPointRectangleF.Bottom - rectHeight;
                                MaxPointRectangleF = newRect;

                            }
                            if ((MaxPointImage.PixelFormat & PixelFormat.Alpha) != 0)
                            {
                                g.FillRectangle(SystemBrushes.Window/*Brushes.White*/, new RectangleF(new PointF(), canvas.Size));
                            }
                            var pOffset = (z - 1) / 2;
#if PERFLOG
                            var perfArchiveDraw = PerfTrace.Now();
#endif
                            // 縮小は GDI+ を使わない面積平均で（GDI+ の DrawImage はスレッドをまたいで直列になる）。GDI+ 用の補正をする前の範囲を渡す（P4）
                            g.Flush();
                            if (!AreaAverageResizer.TryDraw(MaxPointImage, MaxPointRectangleF, canvas, new Rectangle(0, 0, canvas.Width, canvas.Height)))
                            {
                                g.DrawImage(MaxPointImage, new RectangleF(new PointF(), canvas.Size),
                                    new RectangleF(MaxPointRectangleF.X, MaxPointRectangleF.Y, MaxPointRectangleF.Width - pOffset, MaxPointRectangleF.Height - pOffset), GraphicsUnit.Pixel);
                            }
#if PERFLOG
                            PerfTrace.AddStage("archiveClipDraw", PerfTrace.Now() - perfArchiveDraw);
#endif
#if DEBUG
                            var p = new PointF((focusPoint.X - MaxPointRectangleF.X) / z, (focusPoint.Y - MaxPointRectangleF.Y) / z);
                            var r = (float)Math.Sqrt(canvas.Width * canvas.Height) / 10;
                            using (var pen = new Pen(Color.Cyan, r / 5))
                            {
                                g.DrawEllipse(pen, new RectangleF(p.X - r, p.Y - r, 2 * r, 2 * r));
                            }
#endif
                        }
                    }
                    catch
                    {
                        canvas.Dispose();
                        throw;
                    }
                }
                else if (!givenSize) // givenSize でないのに PlaClip にならないのは SinglePage でフォルダのサムネイルを作る場合のみ
                {
                    // MaxPointImage はそのままでは保存できないケースがある
                    canvas = new Bitmap(MaxPointImage);
                }
                else
                {
                    canvas = DoJustClipping(MaxPointImage, needToResize: true, localClipMode: localClipMode);
                }
                imageInfo = ImageLoader.GetImageInfo(MaxPointImage);
                MaxPointImage.Dispose();


                return canvas;
            }
            catch (PasswordLockedException)
            {
                filecount = -1;

                if (MaxPointImage != null) MaxPointImage.Dispose();

                if (!givenSize) throw new Exception(Message.FailedToLoadFile);

                loadError = LoadResult.FileNotFound;// LoadResult.LoadError;
                imageInfo = null;
                if (isDirectory)
                {
                    var b = GetDirectoryBackBrush(zipPath);
                    {
                        return getImageWithFileIInfo(width, height,
                            b);//,
                    }
                }
                else
                {
                    var b = GetFileBackBrush(zipPath);
                    {
                        return getImageWithFileIInfo(width, height,
                            b);//,
                    }
                }
            }
            catch (Exception error)
            {
                filecount = -1;

                if (MaxPointImage != null) MaxPointImage.Dispose();

                if (!givenSize) throw new Exception(Message.FailedToLoadFile);

                if (IgnoreFileError)
                {
                    loadError = LoadResult.LoadError;
                    imageInfo = null;
                    if (backgroundWorker != null && backgroundWorker.CancellationPending) return null;
                    var result = Program.GetErrorImage(width, height);
                    return result;
                }
                else
                {
                    throw new Exception(zipPath + "\r\n\r\n" + Message.FileBroken, error);
                }
            }
        }

        private static RectangleF getCoverRectangleByRatioFromThumbnailInfo(double ratio, Size imageSize, double? fractionalPortion)
        {
            if (fractionalPortion == null)
            {
                return new RectangleF(0, 0, imageSize.Width, imageSize.Height);
            }
            else if (imageSize.Width >= imageSize.Height * ratio)
            {
                var shortEdge = imageSize.Height * ratio;
                return new RectangleF((float)(fractionalPortion * (imageSize.Width - shortEdge)), 0, (float)shortEdge, imageSize.Height);
            }
            else
            {
                var shortEdge = imageSize.Width / ratio;
                return new RectangleF(0, (float)(fractionalPortion * (imageSize.Height - shortEdge)), imageSize.Width, (float)shortEdge);
            }
        }

        private static Size drawFileIconImage(Graphics g, Rectangle rect, string iconPath, DateTime lastWriteTime, bool isDir, bool isExist)
        {
            try
            {
                var img = SmartStaticIconProvider.GetSmall(iconPath, lastWriteTime, isDir);
                //g.DrawImage(img, rect.Right - img.Width, rect.Y); // 右上
                g.DrawImage(img, rect.Left, rect.Bottom - img.Height); // 左下
                return img.Size;

            }
            catch
            {
                return new Size(0, 0);
            }
        }

        private static void drawFileImage(Graphics g, Rectangle rect, Font font, Brush foreBrush, string text, string iconPath, DateTime lastWriteTime, Brush backBrush, bool isDir, bool isExist)
        {
            // 8ea4928 ではサムネイルの読み込み結果が LoadResultArray に戻らず、常に NotYet のままだったため、
            // サムネイルの上にアイコンが重ならないよう描画自体を止めていた。読み込み結果を戻すようにしたので元に戻した（A11）
            try
            {
                var icon = iconPath != null ? SmartStaticIconProvider.GetLarge(iconPath, lastWriteTime, isDir) : null;
                {
                    var size = g.MeasureString(text, font, rect.Width);
                    size.Width += drawLeftMagin + drawRightMagin;
                    size.Height += drawTopMagin + drawBottomMagin;

                    RectangleF textRectangle;
                    if (icon == null)
                    {
                        textRectangle = new RectangleF(rect.X + Math.Max(0, (rect.Width - size.Width) / 2), rect.Y + Math.Max(0, (rect.Height - size.Height) / 2), size.Width, size.Height);
                    }
                    else
                    {
                        var margin = 10 * Program.DisplayMagnificationY;
                        g.DrawImage(icon, rect.X + (rect.Width - icon.Width) / 2, rect.Y + (int)((rect.Height - icon.Height) / 2 + 0.5));
                        textRectangle = new RectangleF(rect.X + Math.Max(0, (rect.Width - size.Width) / 2), rect.Y + Math.Max(0, (rect.Height + icon.Height) / 2 + margin), size.Width, size.Height);
                    }
                    if (backBrush != null)
                    {
                        g.FillRectangle(backBrush, textRectangle);
                    }
                    g.DrawString(text, font, foreBrush, new RectangleF(textRectangle.X + drawLeftMagin, textRectangle.Y + drawTopMagin,
                        textRectangle.Width - (drawLeftMagin + drawRightMagin), textRectangle.Height - (drawTopMagin + drawBottomMagin)));
                }
                {
                }
            }
            catch
            {
            }
        }

        private static Bitmap getImageWithFileIInfo(int width, int height, Brush backBrush)
        {
            Bitmap result = new Bitmap(width, height);

            using (var g = Graphics.FromImage(result))
            {
                g.FillRectangle(backBrush, 0, 0, width, height);
            }
            return result;
        }

        private const int drawLeftMagin = 0;

        private const int drawRightMagin = -1;

        private const int drawTopMagin = 1;

        private const int drawBottomMagin = 0;

        private static void drawSimpleString(Graphics g, string str, Font font, Rectangle rect, Color foreColor, Brush backBrush, Dictionary<string, Size> sizeDictionary)
        {
            var stringSize = MeasureStringSizeByTextRenderer(g, str, font, sizeDictionary);

            var rectBack = new Rectangle(rect.X, rect.Y, stringSize.Width + MeasureStringSizeByTextRenderer(g, str, font, sizeDictionary).Width / 2, stringSize.Height * 4 / 3);
            var rectFore = rectBack;
            rectFore.X += (rectBack.Width - stringSize.Width) / 2;
            g.FillRectangle(backBrush, rectBack);
            TextRenderer.DrawText(g, str, font, rectFore, foreColor, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static Size MeasureStringSizeByTextRenderer(Graphics g, string str, Font font, Dictionary<string, Size> sizeDictionary)
        {
            Size result;
            if (!sizeDictionary.TryGetValue(str, out result))
            {
                result = TextRenderer.MeasureText(g, str, font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                sizeDictionary[str] = result;
            }
            return result;
        }

        public Rectangle drawRating_Bound0 = Rectangle.Empty;

        public Rectangle drawRating_Bound = Rectangle.Empty;

        public Rectangle drawRating_StarsBound = Rectangle.Empty;

        private bool drawRating_StarSizeNotSet = true;

        private Size drawRating_FiveStarSize;

        private Point drawRating_FiveStarLocation;

        private int[] drawRating_PartStarSizesWidth;

        private void drawRating(Graphics g, Rectangle rect, ZipPlaInfo zipPlaInfo) { drawRating(g, rect, zipPlaInfo.Rating); }

        private void drawRating(Graphics g, Rectangle rect, int? rating)
        {
            if (rating == null) rating = 0;
            var r = (int)rating;
            r = Math.Max(0, Math.Min(5, r));
            var font = DefaultFont;

            if (drawRating_StarSizeNotSet)
            {
                drawRating_StarSizeNotSet = false;

                var fiveStarRect = RainbowDataGridViewTextBoxCell.GetCharRectangle("★★★★★", font, TextFormatFlags.NoPadding, onlyHorizontal: false);
                var frameLeft = Program.DpiScaling(3);
                var frameRight = frameLeft;
                var frameTop = Program.DpiScaling(2);
                var frameBottom = frameLeft;
                drawRating_FiveStarSize = fiveStarRect.Size;
                drawRating_FiveStarLocation = new Point(frameLeft - fiveStarRect.X, frameTop - fiveStarRect.Y);

                drawRating_Bound0 = new Rectangle(0, 0, drawRating_FiveStarSize.Width + frameLeft + frameRight, drawRating_FiveStarSize.Height + frameTop + frameBottom);


                drawRating_PartStarSizesWidth = new int[6]
                {
                    TextRenderer.MeasureText(g, "", DefaultFont, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width,
                    TextRenderer.MeasureText(g, "★", DefaultFont, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width,
                    TextRenderer.MeasureText(g, "★★", DefaultFont, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width,
                    TextRenderer.MeasureText(g, "★★★", DefaultFont, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width,
                    TextRenderer.MeasureText(g, "★★★★", DefaultFont, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width,
                    TextRenderer.MeasureText(g, "★★★★★", DefaultFont, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width,
                };

            }

            var partStars = "★★★★★".Substring(0, r);

            var rectBack = new Rectangle(rect.Location, drawRating_Bound0.Size);
            var rectFore = rectBack;
            rectFore.X += drawRating_FiveStarLocation.X; //(rectBack.Width - drawRating_FiveStarSize.Width) / 2;
            rectFore.Y += drawRating_FiveStarLocation.Y;

            if (!rect.Contains(rectBack))
            {
                drawRating_Bound = Rectangle.Empty;
                drawRating_StarsBound = Rectangle.Empty;
                return;
            }

            g.FillRectangle(Brushes.Black, rectBack);
            TextRenderer.DrawText(g, partStars, DefaultFont, rectFore, Color.Gold, TextFormatFlags.NoPadding);// | TextFormatFlags.VerticalCenter);
            rectFore.X += drawRating_PartStarSizesWidth[r];
            TextRenderer.DrawText(g, "★★★★★".Substring(r), DefaultFont, rectFore, Color.LightSlateGray, TextFormatFlags.NoPadding);// | TextFormatFlags.VerticalCenter);

            drawRating_Bound = drawRating_Bound0;
            drawRating_StarsBound = new Rectangle((rectBack.Width - drawRating_FiveStarSize.Width) / 2, 0, drawRating_FiveStarSize.Width, drawRating_FiveStarSize.Height);

        }

        private static SizeF MeasureStringSizeByGraphics(Graphics g, string str, Font font, Dictionary<string, SizeF> sizeDictionary)
        {
            SizeF result;
            if (!sizeDictionary.TryGetValue(str, out result))
            {
                result = g.MeasureString(str, font);
                sizeDictionary[str] = result;
            }
            return result;
        }

        private readonly Dictionary<string, SizeF> drawTags_sizeDictionary = new Dictionary<string, SizeF>();

        private void drawTags(Graphics g, Rectangle rect, ZipPlaInfo zipPlaInfo, Size iconSize)
        {
            var tagNames = zipPlaInfo.TagArray;
            if (tagNames == null) return;

            var leftBorder = rect.X + iconSize.Width;

            var tags = Tags;
            if (tags == null) tags = new ZipTag[0];

            var font = DefaultFont;
            var bottommRight = new Point(rect.Right, rect.Bottom);
            for (var i = tagNames.Length - 1; i >= 0; i--)
            {
                var tagName = tagNames[i];
                var tag = tags.FirstOrDefault(t => t.Name == tagName);
                if (tag == default(ZipTag)) continue;

                var labelSizeF = MeasureStringSizeByGraphics(g, tagName, font, drawTags_sizeDictionary);
                var labelSize = new Size((int)(labelSizeF.Width + 0.5) + drawLeftMagin + drawRightMagin, (int)(labelSizeF.Height + 0.5) + drawTopMagin + drawBottomMagin);
                if (bottommRight.X < rect.Right && bottommRight.X < leftBorder + labelSize.Width)
                {
                    bottommRight.X = rect.Right;
                    bottommRight.Y -= labelSize.Height;

                    if (bottommRight.Y <= rect.Bottom - iconSize.Height)
                    {
                        leftBorder = rect.X;
                    }
                }
                var topLeft = new Point(bottommRight.X - labelSize.Width, bottommRight.Y - labelSize.Height);
                var labelRect = new Rectangle(topLeft, labelSize);
                if (labelRect.Left < leftBorder) labelRect.X = leftBorder;
                var fullDisplayed = rect.Contains(labelRect);
                Rectangle original = Rectangle.Empty;
                if (!fullDisplayed)
                {
                    original = labelRect;
                    labelRect.Intersect(rect);
                }

                using (var b = new SolidBrush(tag.BackColor))
                {
                    g.FillRectangle(b, labelRect);
                }

                var stringRect = new Rectangle(labelRect.X + drawLeftMagin, labelRect.Y + drawTopMagin,
                    labelRect.Width - drawLeftMagin - drawRightMagin, labelRect.Height - drawTopMagin - drawBottomMagin);

                using (var b = new SolidBrush(tag.ForeColor))
                {
                    if (fullDisplayed)
                    {
                        g.DrawString(tagName, font, b, stringRect.Location);
                    }
                    else
                    {
                        var canvasWidth = (int)Math.Ceiling(labelSizeF.Width);
                        var canvasHeight = (int)Math.Ceiling(labelSizeF.Height);
                        if (canvasWidth > 0 && canvasHeight > 0)
                        {
                            using (var canvas = new Bitmap(canvasWidth, canvasHeight))
                            {
                                using (var gc = Graphics.FromImage(canvas))
                                {
                                    gc.DrawString(tagName, font, b, 0, 0);
                                }
                                if (stringRect.Right > labelRect.Right) stringRect.Width = labelRect.Right - stringRect.Left;
                                g.DrawImageUnscaledAndClipped(canvas, stringRect);
                            }
                        }
                    }
                }

                bottommRight.X -= labelSize.Width;
            }
        }

        // point はゼロ以下
        private void GetImagePointAndEtc(out double point, out RectangleF clip, Bitmap image, bool leftToRight)
        {
#if PERFLOG
            var perfPoint = PerfTrace.Now();
            GetImagePointAndEtcCore(out point, out clip, image, leftToRight);
            PerfTrace.AddStage("archivePoint", PerfTrace.Now() - perfPoint); // 表紙らしさの点数（余白の検出）
        }

        private void GetImagePointAndEtcCore(out double point, out RectangleF clip, Bitmap image, bool leftToRight)
        {
#endif
            var thumbWidth = tvCatalog.ThumbnailWidth;
            var thumbHeight = tvCatalog.ThumbnailHeight;

            var blankAvoidRectangle = Program.GetBlankAvoidRectangle(image, thumbWidth, thumbHeight, 20, true);


            point = 0;
            if (blankAvoidRectangle == Rectangle.Empty)
            {
                point = -3.75;
                blankAvoidRectangle = new Rectangle(0, 0, image.Width, image.Height);
            }

            float dstWidth, dstHeight;
            if (2 * thumbHeight + 1 <= Math.Sqrt(2) * (2 * thumbWidth - 1))
            {
                dstWidth = thumbWidth;
                dstHeight = (float)(Math.Sqrt(2) * thumbWidth);
            }
            else if (2 * thumbHeight - 1 >= Math.Sqrt(2) * (2 * thumbWidth + 1))
            {
                dstWidth = (float)(Math.Sqrt(1 / 2.0) * thumbHeight);
                dstHeight = thumbHeight;
            }
            else
            {
                dstWidth = thumbWidth;
                dstHeight = thumbHeight;
            }

            float srcWidth = blankAvoidRectangle.Width;
            float srcHeight = blankAvoidRectangle.Height;

            const float AspectRatio = 148f / 210f;
            const float ThickRatio = 15f / 210f;
            const float ArmRatio = 75f / 210f;

            var dstAsp = dstWidth / dstHeight;
            var dstTck = dstAsp * (ThickRatio / AspectRatio);
            var dstArm = dstAsp * (ArmRatio / AspectRatio);

            var srcAsp = srcWidth / srcHeight;

            float cutPerOpposite;

            // 最も近いもので決め打ち
            if (srcAsp < dstAsp)
            {
                cutPerOpposite = (1 / srcAsp - 1 / dstAsp) / 2;
            }
            else if (srcAsp < dstAsp + (dstTck + dstArm) * 0.5)
            {
                cutPerOpposite = 0;
            }
            else if (srcAsp < dstArm + dstAsp + 0.5 * dstTck)
            {
                cutPerOpposite = srcAsp - dstAsp;
            }
            else if (srcAsp < dstAsp + dstTck + dstArm * 0.45 + dstAsp * 0.55) // ここは非常にシビア
            {
                cutPerOpposite = srcAsp - dstAsp - dstTck;
            }
            else if (srcAsp < dstAsp + dstTck + dstAsp + 0.5 * dstArm)
            {
                cutPerOpposite = 0;
            }
            else if (srcAsp < dstArm + dstAsp + dstTck + dstAsp + dstArm * 0.15)
            {
                cutPerOpposite = srcAsp - 2 * dstAsp - dstTck;
            }
            else
            {
                cutPerOpposite = (srcAsp - dstTck - 2 * dstAsp) / 2;
            }

            // 単純に線形補間

            if (srcAsp < dstAsp)
            {
                clip = new RectangleF(blankAvoidRectangle.X, blankAvoidRectangle.Y + srcWidth * cutPerOpposite, srcWidth, srcWidth * dstHeight / dstWidth);
            }
            else
            {
                if (leftToRight) cutPerOpposite = srcAsp - cutPerOpposite - dstAsp;
                clip = new RectangleF(blankAvoidRectangle.X + srcHeight * cutPerOpposite, blankAvoidRectangle.Y, srcHeight * dstWidth / dstHeight, srcHeight);
            }


            // 最も近いもので決め打ち
            if (srcAsp < 0.7 * dstAsp || srcAsp > 1.4 * (dstArm + dstAsp + dstTck + dstAsp + dstArm))
            {
                point -= 2.5;
            }
        }

        private static string getPathWithoutSeparator(string path)
        {
            if (path == null) return null;
            var length = path.Length;
            if (length >= 2 && path.EndsWith(Path.DirectorySeparatorChar.ToString()) && !path.EndsWith(Path.DirectorySeparatorChar.ToString() + Path.DirectorySeparatorChar))
            {
                return path.Substring(0, length - 1);
            }
            else
            {
                return path;
            }
        }

        public static string GetDirectoryOrRootFullPath(string path)
        {
            return getPathWithoutSeparator(Path.GetFullPath(GetDirectoryPathWithSeparator(path)));
        }

        public static bool IsSupportedByCatalogFormExceptFolder(string fileName)
        {
            if (fileName == null) return false;
            return VirtualFolder.IsBookmarkPath(fileName) || SmartFolder.IsSmartFolderPath(fileName) || PackedImageLoader.Supports(fileName) ||
                    (MovieThumbnailLoader.Supports(fileName) && MovieThumbnailLoader.ffmpegExists());
        }

        private static bool IsSupportedByCatalogFormAndExists(string fileName, out bool isDir)
        {
            isDir = false;
            if (fileName == null) return false;
            try
            {
                return (isDir = Directory.Exists(fileName)) || (File.Exists(fileName) && IsSupportedByCatalogFormExceptFolder(fileName));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSupportedByCatalogFormAndExists(string fileName)
        {
            if (fileName == null) return false;
            try
            {
                return Directory.Exists(fileName) || (File.Exists(fileName) && IsSupportedByCatalogFormExceptFolder(fileName));
            }
            catch
            {
                return false;
            }
        }

        object ZipPathArrayLocker = new object();

        class PackedImageLoaderWithEntries : IDisposable
        {
            public PackedImageLoaderWithEntries(PackedImageLoader loader, bool needToExecOnTask = false)
            {
                PackedImageLoader = loader;
                Entries = loader.GetPackedImageEntries(PackedImageLoaderFileInfo.All, needToExecOnTask).ToArray(); //.OrderBy(entry => entry.Path, new LogicalStringComparer()).ToArray();
            }
            public PackedImageLoader PackedImageLoader;
            public PackedImageLoader.PackedImageEntry[] Entries;

            // 書庫の中を表示するとき、サムネイルごとに Entries を先頭から探していた（K9）。同じ名前が複数あれば先のもの（First と同じ）
            private Dictionary<string, PackedImageLoader.PackedImageEntry> entryByPath;
            public PackedImageLoader.PackedImageEntry FindEntry(string path)
            {
                var dict = entryByPath;
                if (dict == null)
                {
                    var entries = Entries;
                    dict = new Dictionary<string, PackedImageLoader.PackedImageEntry>(entries.Length, StringComparer.Ordinal);
                    foreach (var e in entries)
                    {
                        if (e.Path != null && !dict.ContainsKey(e.Path)) dict.Add(e.Path, e);
                    }
                    entryByPath = dict; // 複数のスレッドが同時に作っても結果は同じ
                }
                return dict.TryGetValue(path, out var found) ? found : null;
            }

            public void Dispose()
            {
                if (PackedImageLoader != null)
                {
                    PackedImageLoader.Dispose();
                    PackedImageLoader = null;
                }
            }
        }

        // カタログの設定（書庫の中の書庫をたどる深さ・探し方）で書庫を開く
        internal PackedImageLoader CreatePackedImageLoader(string path) => new PackedImageLoader(path, packedImageLoaderLeftHierarchies, packedImageLoaderSearchMode);

        PackedImageLoaderWithEntries loadingPackedImageLoader = null;

        readonly object loadingPackedImageLoaderLocker = new object();

        // 12 本の配列をまとめて取る・差し替える（CatalogItemArrays）。呼び出し側が ZipPathArrayLocker を持つこと
        private CatalogItemArrays GetItemArrays() => new CatalogItemArrays { Path = ZipPathArray, Name = ZipNameArray, Page = ZipPageArray, CreateTime = CreateTimeArray, LastAccessTime = LastAccessTimeArray, ModifiedTime = ModifiedTimeArray, FileCount = FileCountArray, ImageInfo = ImageInfoArray, MovieInfo = MovieInfoArray, LoadResult = LoadResultArray, Size = ZipSizeArray, Mask = ZipMaskArray };

        private void SetItemArrays(CatalogItemArrays items)
        {
            ZipPathArray = items.Path;
            ZipNameArray = items.Name;
            ZipPageArray = items.Page;
            CreateTimeArray = items.CreateTime;
            LastAccessTimeArray = items.LastAccessTime;
            ModifiedTimeArray = items.ModifiedTime;
            FileCountArray = items.FileCount;
            ImageInfoArray = items.ImageInfo;
            MovieInfoArray = items.MovieInfo;
            LoadResultArray = items.LoadResult;
            ZipSizeArray = items.Size;
            ZipMaskArray = items.Mask;
        }

        bool CreatingZipPathArray = false;

        string[] ZipPathArray;

        string[] ZipNameArray;

        int[] ZipPageArray;

        DateTime[] CreateTimeArray;

        DateTime[] LastAccessTimeArray;

        DateTime[] ModifiedTimeArray;

        int[] FileCountArray;

        ImageInfo[] ImageInfoArray;

        MovieInfo[] MovieInfoArray;

        LoadResult[] LoadResultArray;

        long[] ZipSizeArray;

        bool[] ZipMaskArray;

        ClipMode currentClipMode = ClipMode.PlaClip;

        BindingMode currentBindingMode = BindingMode.LeftToRight;

        // 64bit では動かない
        // http://pieceofnostalgy.blogspot.jp/2011/11/net-framework.html
        //[System.Runtime.InteropServices.DllImport("user32.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
        //public static extern IntPtr WindowFromPoint(int x, int y);

        void ReloadOneThumbnailForSubThread(int index, Guid startingGuid)
        {
            if (index >= 0)
            {
                lock (ZipPathArrayLocker)
                {
                    if (ZipPathArray != null && index < ZipPathArray.Length)
                    {
                        ReloadOneThumbnailForSubThread(index, ZipPathArray[index], startingGuid);
                    }
                }
            }
        }

        void ReloadOneThumbnailForSubThread(string oldPath, string newPath, Guid startingGuid)
        {
            if (!string.IsNullOrEmpty(oldPath) && !string.IsNullOrEmpty(newPath))
            {
                lock (ZipPathArrayLocker)
                {
                    if (ZipPathArray != null)
                    {
                        ReloadOneThumbnailForSubThread(Array.IndexOf(ZipPathArray, oldPath), newPath, startingGuid);
                    }
                }
            }
        }

        enum SilentMode { Always, Never, OnlyIfNotSuccess }

        void ReloadOneThumbnailForSubThread(int index, string newPath, Guid startingGuid, string oldPath = null, SilentMode silentMode = SilentMode.Never, bool reloadPage = false)
        {
            if (index >= 0 && !string.IsNullOrEmpty(newPath))
            {
                if (startingGuid != loadingGuid) return;
                var enterIf = false;
                Bitmap thumbnail = null;
                int filecount = -1;
                LoadResult loadResult = LoadResult.NotYet;
                long fileSize = -1;
                ImageInfo imageInfo;
                MovieInfo movieInfo;
                string zipName = null;
                var path = newPath;
                var isDir = false;
                lock (ZipPathArrayLocker)
                {
                    if (CreatingZipPathArray) return;

                    if (ZipPathArray != null && index < ZipPathArray.Length)
                    {
                        enterIf = true;
                        try
                        {
                            if (oldPath != null && oldPath != ZipPathArray[index]) return;
                            ZipPathArray[index] = path;

                            isDir = Directory.Exists(newPath);
                            ZipNameArray[index] = zipName = getRelativePath(path, isDir);// name + Path.DirectorySeparatorChar;

                            FileSystemInfo info;
                            if (isDir)
                            {
                                info = new DirectoryInfo(path);
                            }
                            else
                            {
                                info = new FileInfo(path);
                            }
                            CreateTimeArray[index] = Program.GetCreateTime(info);
                            LastAccessTimeArray[index] = Program.GetLastAccessTime(info); // info.LastAccessTime;
                            ModifiedTimeArray[index] = Program.GetLastWriteTime(info);// .LastWriteTime;
                            FileCountArray[index] = -1;
                            ImageInfoArray[index] = null;// Size.Empty;
                            MovieInfoArray[index] = null;

                            if (reloadPage && LoadLastViewedPageFromHistory && File.Exists(Program.HistorySorPath))
                            {
                                ZipPageArray[index] = VirtualFolder.GetPageFromBookmark(path, isDir, Program.HistorySorPath);
                            }

                        }
                        catch (Exception error)
                        {
                            Program.AlertError(error);
                            return;
                        }
                    }
                }
                if (enterIf)
                {
                    // サムネイルは作らず、ヘッダ（ページ数・解像度・動画情報など）だけを取る。サムネイルは、表示範囲に入っている項目だけ
                    // ThumbViewerItem.LoadAsync が作る（K21。以前は監視イベントのたびに、見えていない項目のサムネイルまで作ってキャッシュに書いていた）
                    GetHeader(path, isDir, out _, out filecount, out loadResult, out fileSize, out imageInfo, out movieInfo);
                    if (fileSize < 0 && !isDir) // フォルダの場合サイズは取得済みまたはサイズは表示しない設定
                    {
                        try
                        {
                            fileSize = (new FileInfo(path)).Length;
                        }
                        catch { }
                    }
                    {
                        if (startingGuid != loadingGuid)
                        {
                            thumbnail?.Dispose();
                            return;
                        }
                        try
                        {
                            Invoke((MethodInvoker)(() =>
                            {
                                if (CreatingZipPathArray || startingGuid != loadingGuid)
                                {
                                    thumbnail?.Dispose();
                                    return;
                                }
                                try
                                {
                                    if (index >= tvCatalog.Count)
                                    {
                                        thumbnail?.Dispose();
                                        return;
                                    }


                                    lock (ZipPathArrayLocker)
                                    {
                                        if (CreatingZipPathArray || index >= FileCountArray.Length)
                                        {
                                            thumbnail?.Dispose();
                                            return;
                                        }

                                        //NameInAsc, NameInDesc, RatingInAsc, RatingInDesc, AccessedInAsc, AccessedInDesc, ModifiedInAsc, ModifiedInDesc, SizeInAsc, SizeInDesc

                                        if (sortMode == SortMode.NameInAsc || sortMode == SortMode.NameInDesc)
                                        {
                                            var s2d = tvCatalog.ShowIndexToDataIndex;
                                            var sign = sortMode == SortMode.NameInAsc ? 1 : -1;
                                            var showIndex = tvCatalog.DataIndexToShowIndex[index];
                                            if (showIndex >= 0)
                                            {
                                                var clearSortOrder = false;
                                                var s2dLength = s2d.Length;
                                                var showIndexMinus1 = showIndex - 1;
                                                var ZipNameArrayLength = ZipNameArray.Length;
                                                if (showIndexMinus1 >= 0 && showIndexMinus1 < s2dLength)
                                                {
                                                    var preIndex = s2d[showIndexMinus1];
                                                    if (preIndex >= 0 && preIndex < ZipNameArrayLength)
                                                    {
                                                        var preName = ZipNameArray[preIndex];
                                                        if (preName != null && zipName != null && sign * GetSortArray_NaturalSort.Compare(preName, zipName) > 0)
                                                        {
                                                            clearSortOrder = true;
                                                        }
                                                    }
                                                }
                                                if (!clearSortOrder)
                                                {
                                                    var showIndexPlus1 = showIndex + 1;
                                                    if (showIndexPlus1 >= 0 && showIndexPlus1 < s2dLength)
                                                    {
                                                        var nexIndex = s2d[showIndexPlus1];
                                                        if (nexIndex >= 0 && nexIndex < ZipNameArrayLength)
                                                        {
                                                            var nexName = ZipNameArray[nexIndex];
                                                            if (nexName != null && zipName != null && sign * GetSortArray_NaturalSort.Compare(zipName, nexName) > 0)
                                                            {
                                                                clearSortOrder = true;
                                                            }
                                                        }

                                                    }
                                                }

                                                if (clearSortOrder)
                                                {
                                                    tbcFileName.HeaderCell.SortGlyphDirection = SortOrder.None;
                                                    cbSortBy.SelectedIndex = -1;
                                                }
                                            }
                                        }


                                        FileCountArray[index] = filecount;
                                        LoadResultArray[index] = loadResult;
                                        ZipSizeArray[index] = fileSize;
                                        ImageInfoArray[index] = imageInfo;
                                        MovieInfoArray[index] = movieInfo;

                                        setFileListRowInLock(index, dynamicOnly: false, localOnly: false, cultureForOnlyStatic: Message.CurrentLanguage, volatilitySortCancelIfChanged: true);

                                    }

                                    // 最後にやらないと描画時に Success 等が正しく参照されない
                                    var silent = silentMode == SilentMode.Always || silentMode == SilentMode.OnlyIfNotSuccess && loadResult != LoadResult.Success;
                                    if (tvCatalog[index] == null)
                                    {
                                        var item = new ThumbViewerItem()
                                        {
                                            OwnerCatalog = this,
                                            FilePath = path,
                                            Image = thumbnail,
                                            Text = zipName,
                                        };
                                        if (silent)
                                        {
                                            tvCatalog.SilentSet(index, item);
                                        }
                                        else
                                        {
                                            tvCatalog[index] = item;
                                        }
                                    }
                                    else
                                    {
                                        if (silent)
                                        {
                                            tvCatalog[index].SilentSet(zipName); // サムネイルは変わっていないので今の画像のまま（K21）
                                        }
                                        else
                                        {
                                            tvCatalog[index].ResetForReload(zipName);
                                        }
                                    }
                                    // 表示範囲に入っている項目のサムネイルを作らせる（描画のときに LoadAsync が呼ばれる。K21）
                                    tvCatalog.Invalidate();
                                }
                                catch (Exception error)
                                {
                                    Program.AlertError(error);
                                }
                                if (tvCatalog.IsSelected(index))
                                {
                                    ShowStatusBarAfterItemLoaded();
                                }
                            }));
                        }
                        catch (ObjectDisposedException) { }
                    }
                }
            }
        }

        private void setThumbnailManuallyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenSelectedArchiveCoverSetting();
        }

        private void OpenSelectedArchiveCoverSetting()
        {
            var selectedIndex = tvCatalog.SelectedIndex;
            if (selectedIndex >= 0)
            {
                string selectedPath = null;
                lock (ZipPathArrayLocker)
                {
                    if (ZipPathArray != null && selectedIndex < ZipPathArray.Length)
                    {
                        if (LoadResultArray == null || selectedIndex >= LoadResultArray.Length) return;
                        // LoadResult.NotYet は読み込む
                        switch (LoadResultArray[selectedIndex])
                        {
                            case LoadResult.LoadError:
                                MessageBox.Show(Message.FailedToLoadFile, null, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                                return;
                            case LoadResult.FileNotFound:

                                MessageBox.Show(Message.NoReadableFile, null, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                                return;
                        }
                        try
                        {
                            selectedPath = ZipPathArray[selectedIndex];
                        }
                        catch (Exception error)
                        {
                            Program.AlertError(error);
                        }
                    }
                }
                if (selectedPath != null)
                {
                    try
                    {
                        if (PackedImageLoader.Supports(selectedPath))
                        {
                            OpenWaitingProcess(selectedPath, -1, true);
                        }
                        else if (InArchive)
                        {
                            OpenWaitingProcess(selectedPath, page: -1, coverSettingMode: true); // selectedPath が / を含むパスの場合 page は -1 でよい
                        }
                        else if (MovieThumbnailLoader.Supports(selectedPath) && MovieThumbnailLoader.ffmpegExists())
                        {
                            zabLocation.Text = selectedPath;
                            ThumbnailSettingToCatalog_CoverSettingModeAtOnce = true;
                            MakePreview();
                        }
                    }
                    catch (Exception error)
                    {
                        Program.AlertError(error);
                    }
                }
            }
        }
    }
}
