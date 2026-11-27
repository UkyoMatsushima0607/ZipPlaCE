using CSharpImageLibrary;
using LibAPNG;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Collections;

namespace ZipPla
{
    public static class ImageLoader
    {
        //private static readonly object psdLocker = new object();

        private static readonly string[] defaultSupportExtensions = new string[]
            { "bmp", "gif", "jpg", "jpeg", "png", "apng", "exf", "tif", "tiff", "ico", "emf", "wmf" };
        private static readonly string[] individualSupportExtensions = (new string[]
            { "psd", "dds", "tga" }).Concat(WindowsMediaPhoto.SupportExtensionsInLowerWithoutPeriod).ToArray();//, "xcf", "webp" };
        private static readonly string[] thumbnailSupportImageExtensions = new string[]
            { "pdn" };
        private static readonly string[] thumbnailSupportOtherExtensionsExpectTag = new string[]
            {  "txt", "ini", "html", "htm" }; //, "xml", "mht" };
        private static readonly string[] supportsFullReadingWithoutSusieFfmpeg;
        private static string[] supportsFullReadingWithoutFfmpeg;
        private static string supportsFullReadingStringWithoutFfmpeg;
        private static readonly string[] ffmpegSupportsWithoutPeriod;
        //private static string[] supportsGetImageInfoWithFfmpegWithPeriedInLower;
        private static string supportsFullReadingStringWithFfmpeg;
        private static readonly Regex defaultSupportPathRegex;//, supportsGetImageInfo;//, //supportsFullReadingPathRegex,
                                                               /*supportsImageAtLeastThumbnailReadingPathRegex, supportsAtLeastThumbnailReadingPathRegex, *///supportsGetImageInfo;
        private static string[] supportsImageAtLeastThumbnailReadingExtensionWithPeriodInLowerCase, supportsAtLeastThumbnailReadingExtensionWithPeriodInLowerCase;
        //public static readonly string SupportedFileFilter;
        private static ConcurrentDictionary<string, string> susieSupportsExtensionWithoutPeriodInLowerCaseToSpiName;

        /// <summary>
        /// WebP を libwebp で（Susie を通らずに）読むか（D1）。libwebp のデコードはスレッド安全なので、ビューアで並列にデコードしてよい。
        /// WebP 用の Susie プラグインがあると Susie が先に使われるので false
        /// </summary>
        /// <summary>
        /// JPEG 2000（jp2・j2k・jpf・jpx）を Susie を通らずに ffmpeg で読むか（D6）。ffmpeg は別のプロセスなので、ビューアで並列にデコードしてよい。
        /// その形式の Susie プラグインがあると Susie が先に使われるので false
        /// </summary>
        public static bool Jpeg2000DecodesWithFfmpeg(string extensionWithoutPeriodInLowerCase)
        {
            if (extensionWithoutPeriodInLowerCase != "jp2" && extensionWithoutPeriodInLowerCase != "j2k" &&
                extensionWithoutPeriodInLowerCase != "jpf" && extensionWithoutPeriodInLowerCase != "jpx") return false;
            if (!MovieThumbnailLoader.ffmpegExists()) return false;
            var susie = susieSupportsExtensionWithoutPeriodInLowerCaseToSpiName;
            return susie == null || !susie.ContainsKey(extensionWithoutPeriodInLowerCase);
        }

        public static bool WebPDecodesWithLibWebP
        {
            get
            {
                if (!WebPDecoder.Available) return false;
                var susie = susieSupportsExtensionWithoutPeriodInLowerCaseToSpiName;
                return susie == null || !susie.ContainsKey("webp");
            }
        }
        static ImageLoader()
        {
            defaultSupportPathRegex = new Regex(@"\.(?:" + string.Join("|", defaultSupportExtensions) + ")$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            supportsFullReadingWithoutSusieFfmpeg = defaultSupportExtensions.Concat(individualSupportExtensions).ToArray();

            ffmpegSupportsWithoutPeriod = (from withP in MovieThumbnailLoader.SupportedImageExtensionsWithPeriodInLowerCase select withP.Substring(1)).ToArray();

            //SusieReset();
        }

        private static bool allowUntested;
        //public static bool AllowUntestedPlugins { get { return allowUntestedPlugins; } }
        private static bool searchInstallationFolder;
        //public static bool SearchAlsoSusieInstallationFolder { get { return searchInstallationFolder; } }

        public static void SusieReset()
        {
            var config = new GeneralConfig();
            SusieReset(config.AllowUntestedPlugins, config.SearchAlsoSusieInstallationFolder);
        }
        public static void SusieResetIfSettingChanged(bool allowUntested, bool searchInstallationFolder)
        {
            if (ImageLoader.allowUntested != allowUntested || ImageLoader.searchInstallationFolder != searchInstallationFolder)
            {
                SusieReset(allowUntested, searchInstallationFolder);
            }
        }
        // 前回 Susie を構成したときの設定とプラグインのファイルの一覧（K20）
        private static string susieSignature;

        /// <summary>
        /// Susie の構成を決めるもの（設定・プラグインのフォルダにある .spi/.sph の名前・サイズ・更新日時）を文字列にする（K20）。
        /// フォルダを開くたびに全プラグインの MD5 と LoadLibrary をし直していたのを、これが変わったときだけにする
        /// </summary>
        private static string GetSusieSignature(bool allowUntested, bool searchInstallationFolder)
        {
            var sb = new StringBuilder();
            sb.Append(allowUntested).Append('|').Append(searchInstallationFolder);
            AppendSusiePluginFolderSignature(sb, Application.StartupPath);
            if (searchInstallationFolder)
            {
                try
                {
                    using (var regkey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Takechin\Susie\Plug-in", false))
                    {
                        var path = regkey?.GetValue("Path") as string;
                        sb.Append("|reg:").Append(path);
                        if (path != null) AppendSusiePluginFolderSignature(sb, path);
                    }
                }
                catch
                {
                    sb.Append("|reg:error");
                }
            }
            return sb.ToString();
        }

        private static void AppendSusiePluginFolderSignature(StringBuilder sb, string folder)
        {
            sb.Append("|dir:").Append(folder);
            try
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.sp?"))
                {
                    sb.Append('|').Append(file.Name).Append(':').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks);
                }
            }
            catch
            {
                sb.Append("|error");
            }
        }

        private static void SusieReset(bool allowUntested, bool searchInstallationFolder)
        {
            ImageLoader.allowUntested = allowUntested;
            ImageLoader.searchInstallationFolder = searchInstallationFolder;
            var signature = GetSusieSignature(allowUntested, searchInstallationFolder);
            if (signature == susieSignature) return; // 設定もプラグインも前回と同じ（K20）
            {
                susieSupportsExtensionWithoutPeriodInLowerCaseToSpiName = GetSusieSupports(supportsFullReadingWithoutSusieFfmpeg, allowUntested, searchInstallationFolder);
            }
            var list = supportsFullReadingWithoutSusieFfmpeg.Concat(susieSupportsExtensionWithoutPeriodInLowerCaseToSpiName.Keys);
            // libwebp のデコーダ DLL があれば WebP は ffmpeg なしで読める
            if (WebPDecoder.Available) list = list.Concat(new[] { "webp" }).Distinct();

            supportsFullReadingWithoutFfmpeg = list.ToArray();
            supportsFullReadingStringWithoutFfmpeg = "*." + string.Join(";*.", supportsFullReadingWithoutFfmpeg);
            var onlyFfmpegSupports = ffmpegSupportsWithoutPeriod.Where(e => !list.Contains(e));
            //supportsFullReadingWithFfmpeg = supportsFullReadingWithoutFfmpeg.Concat(onlyFfmpegSupports).ToArray();
            supportsFullReadingStringWithFfmpeg = supportsFullReadingStringWithoutFfmpeg + ";*." + string.Join(";*.", onlyFfmpegSupports);
            //SupportedFileFilter = "*." + string.Join(";*.", list);

            var atLeastThumbnailList = new List<string>(list);
            atLeastThumbnailList.AddRange(thumbnailSupportImageExtensions);

            //supportsImageAtLeastThumbnailReadingPathRegex = new Regex(@"\.(?:" + string.Join("|", atLeastThumbnailList) + ")$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            supportsImageAtLeastThumbnailReadingExtensionWithPeriodInLowerCase = (from ext in atLeastThumbnailList select "." + ext).ToArray();
            //supportsGetImageInfoWithFfmpegWithPeriedInLower = ImageInfo.SupportExtensionsInLowerWithoutPeriod

            atLeastThumbnailList.AddRange(thumbnailSupportOtherExtensionsExpectTag);

            var tagFiles = from ext in TagLib.SupportedMimeType.AllExtensions
                           where !MovieThumbnailLoader.Supports("." + ext) && !PackedImageLoader.Supports("." + ext)
                           select ext.StartsWith(".") ? ext.Substring(1).ToLower() : ext.ToLower();
            atLeastThumbnailList.AddRange(tagFiles);

            //supportsAtLeastThumbnailReadingPathRegex = new Regex(@"\.(?:" + string.Join("|", atLeastThumbnailList) + ")$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            supportsAtLeastThumbnailReadingExtensionWithPeriodInLowerCase = (from ext in atLeastThumbnailList select "." + ext).ToArray();
            susieSignature = signature;

            //list.AddRange(ImageInfo.SupportExtensionsInLowerWithoutPeriod);
            //supportsGetImageInfo = new Regex(@"\.(?:" + string.Join("|", list) + ")$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            
        }

        public static bool IndividualSupportFullReading(string path)
        {
            try
            {
                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext)) return false;
                ext = ext.Substring(1).ToLower();
                return defaultSupportExtensions.Contains(ext) || individualSupportExtensions.Contains(ext);
            }
            catch
            {
                return false;
            }
        }

        private static ConcurrentDictionary<string, string> GetSusieSupports(IEnumerable<string> notRequired, bool allowUntested, bool searchInstallationFolder)
        {
            var result = GetSusieSupports(allowUntested, searchInstallationFolder);
            foreach (var key in result.Keys.ToArray())
            {
                if (notRequired.Contains(key)) result.TryRemove(key, out var dummy);
            }
            return result;
        }

        private static ConcurrentDictionary<string, string> GetSusieSupports(bool allowUntested, bool searchInstallationFolder)
        {
            try
            {
                using (var susie = new garu.Util.Susie(allowUntested, searchInstallationFolder))
                {
                    return susie.GetExtensionToSpiPathDictionary();
                }
            }
            catch
            {
                return new ConcurrentDictionary<string, string>();
            }
        }

        public static bool IsLowLoad(string path)
        {
            try
            {
                var ext = Path.GetExtension(path).ToLower();
                switch(ext)
                {
                    case ".bmp":
                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                    case ".ico":
                        return true;
                    default: return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string GetSupportedFileFilter()
        {
            if(MovieThumbnailLoader.ffmpegExists())
            {
                return supportsFullReadingStringWithFfmpeg;
            }
            else
            {
                return supportsFullReadingStringWithoutFfmpeg;
            }
        }

        
        public static bool SupportsFullReading(string path, bool ffmpegExists)
        {
            var nullable = ffmpegExists as bool?;
            return SupportsFullReading(path, ref nullable, out var dummy);
        }
        

        public static bool SupportsFullReading(string path, ref bool? ffmpegExists)
        {
            return SupportsFullReading(path, ref ffmpegExists, out var dummy);
        }

        
        public static bool SupportsFullReading(string path, bool ffmpegExists, out bool requiredFfmpeg)
        {
            var nullable = ffmpegExists as bool?;
            return SupportsFullReading(path, ref nullable, out requiredFfmpeg);
        }
        

        public static bool SupportsFullReading(string path, ref bool? ffmpegExists, out bool requiredFfmpeg)
        {
            requiredFfmpeg = false;
            if (path == null) return false;
            try
            {
                var ext = Path.GetExtension(path);
                if (ext == "") return false;
                if (supportsFullReadingWithoutFfmpeg != null)
                {
                    if (supportsFullReadingWithoutFfmpeg.Contains(ext.Substring(1).ToLower())) return true;
                }
                else
                {
                    var ext2 = ext.Substring(1).ToLower();
                    if (supportsFullReadingWithoutSusieFfmpeg.Contains(ext2)) return true;
                    SusieReset();
                    if (supportsFullReadingWithoutFfmpeg.Contains(ext2)) return true;
                }
                
                requiredFfmpeg = MovieThumbnailLoader.SupportedImageExtensionsWithPeriodInLowerCase.Contains(ext);

                if (!requiredFfmpeg) return false;
                if (ffmpegExists == null) ffmpegExists = MovieThumbnailLoader.ffmpegExists();

                return (bool)ffmpegExists;
            }
            catch
            {
                return false;
            }
            //return path != null && supportsFullReadingPathRegex.IsMatch(path);
        }

        public static string GetExtensionWithPeriodInLowerSafety(string path)
        {
            if (path == null) return null;
            try
            {
                var extensionWithPeriodInLower = Path.GetExtension(path);
                if (string.IsNullOrEmpty(extensionWithPeriodInLower)) return null;
                return extensionWithPeriodInLower.ToLower();
            }
            catch
            {
                return null;
            }

        }
        
        public static bool SupportsGetImageInfo(string path, ref bool? ffmpegExists)
        {
            return SupportsGetImageInfoByExtensionWithPeriodInLower(GetExtensionWithPeriodInLowerSafety(path), ref ffmpegExists);
        }

        public static bool SupportsGetImageInfoByExtensionWithPeriodInLower(string extensionWithPeriodInLower, ref bool? ffmpegExists)
        {
            //return path != null && supportsGetImageInfo.IsMatch(path);

            if (extensionWithPeriodInLower == null) return false;

            if (supportsAtLeastThumbnailReadingExtensionWithPeriodInLowerCase.Contains(extensionWithPeriodInLower)) return true;

            var onlyFfmpeg = (from e in MovieThumbnailLoader.SupportedImageExtensionsWithPeriodInLowerCase
                              where !supportsAtLeastThumbnailReadingExtensionWithPeriodInLowerCase.Contains(e)
                              select e).ToArray();

            if (!onlyFfmpeg.Any()) return false;

            if (ffmpegExists == null) ffmpegExists = MovieThumbnailLoader.ffmpegExists();
            if ((bool)ffmpegExists) return onlyFfmpeg.Contains(extensionWithPeriodInLower);
            else return false;
        }


        public static bool SupportsAtLeastThumbnailReading(string path, ref bool? ffmpegExists)
        {
            //return path != null && supportsAtLeastThumbnailReadingPathRegex.IsMatch(path);
            if (path == null) return false;
            try
            {
                var ext = Path.GetExtension(path).ToLower();

                if (supportsAtLeastThumbnailReadingExtensionWithPeriodInLowerCase.Contains(ext)) return true;

                return MovieThumbnailLoader.SupportedImageExtensionsWithPeriodInLowerCase.Contains(ext)
                    && (ffmpegExists ?? (bool)(ffmpegExists = MovieThumbnailLoader.ffmpegExists()));
            }
            catch
            {
                return false;
            }
        }

        public static bool SupportsImageAtLeastThumbnailReading(string path, bool ffmpegExists)
        {
            var nullable = ffmpegExists as bool?;
            return SupportsImageAtLeastThumbnailReading(path, ref nullable);
        }
        public static bool SupportsImageAtLeastThumbnailReading(string path, ref bool? ffmpegExists)
        {
            if (path == null) return false;
            //return path != null && supportsImageAtLeastThumbnailReadingPathRegex.IsMatch(path);
            try
            {
                var ext = Path.GetExtension(path).ToLower();

                if (supportsImageAtLeastThumbnailReadingExtensionWithPeriodInLowerCase.Contains(ext)) return true;

                if (!MovieThumbnailLoader.SupportedImageExtensionsWithPeriodInLowerCase.Contains(ext)) return false;

                if (ffmpegExists == null) ffmpegExists = MovieThumbnailLoader.ffmpegExists();

                return (bool)ffmpegExists;
                
            }
            catch
            {
                return false;
            }
        }

        public static bool SupportsImageAtLeastThumbnailReadingByExtension(string extensionWithPeriodInLowerCase, bool? ffmpegExists)
        {
            if (extensionWithPeriodInLowerCase == null) return false;
            if (supportsImageAtLeastThumbnailReadingExtensionWithPeriodInLowerCase.Contains(extensionWithPeriodInLowerCase)) return true;
            return MovieThumbnailLoader.SupportedImageExtensionsWithPeriodInLowerCase.Contains(extensionWithPeriodInLowerCase)
                && (ffmpegExists != null ? (bool)ffmpegExists : MovieThumbnailLoader.ffmpegExists());
        }

        public static bool SupportsImageAtLeastThumbnailReadingByExtension(string extensionWithPeriodInLowerCase, ref bool? ffmpegExists)
        {
            if (extensionWithPeriodInLowerCase == null) return false;
            if (supportsImageAtLeastThumbnailReadingExtensionWithPeriodInLowerCase.Contains(extensionWithPeriodInLowerCase)) return true;
            if (!MovieThumbnailLoader.SupportedImageExtensionsWithPeriodInLowerCase.Contains(extensionWithPeriodInLowerCase)) return false;
            if (ffmpegExists == null)
            {
                var exists = MovieThumbnailLoader.ffmpegExists();
                ffmpegExists = exists;
                return exists;
            }
            else
            {
                return (bool)ffmpegExists;
            }
        }

        private static DihedralFraction GetJpegOrientation(Stream stream)
        {
            var p = stream.Position;
            var tags = Exif.GetAll(stream);
            stream.Position = p;
            //stream.Seek(0, SeekOrigin.Begin);
            var value = tags?.FirstOrDefault(tag => tag.Tag == 0x0112)?.Value;
            return value == null ? new DihedralFraction() : ViewerFormImageFilter.GetOrientation((int)value);
        }

        /// <summary>
        /// サムネイル用に JPEG を縮小してデコードする（A2）。WIC の JPEG デコーダは DCT の段階で 1/2・1/4・1/8 に
        /// 縮小できるので、大きな JPEG ほど速い。EXIF の回転も反映する。
        /// 回転後のサイズが minSize の幅・高さとも以上になるようにし、元の画像がその 2 倍に満たない（縮小の効果がない）
        /// 場合や読めない場合は null を返す（呼び出し側は従来どおり全体をデコードする）。
        /// </summary>
        public static Bitmap TryLoadReducedJpeg(string filePath, Size minSize)
        {
            return TryLoadReducedJpeg(filePath, minSize, out _);
        }

        /// <summary>
        /// 上と同じ。開いたファイルからヘッダの情報（ImageInfo(string) と同じもの）も読んで返す（K19: 同じファイルを 2 回開かない）。
        /// 戻り値が null でも、ヘッダが読めていれば imageInfo は null でない
        /// </summary>
        public static Bitmap TryLoadReducedJpeg(string filePath, Size minSize, out ImageInfo imageInfo)
        {
            imageInfo = null;
            if (!IsJpegPath(filePath) || minSize.Width <= 0 || minSize.Height <= 0) return null;
            try
            {
                using (var s = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { imageInfo = new ImageInfo(s); } catch { } // 読めなければ呼び出し側が従来どおり ImageInfo(string) を使う
                    s.Position = 0;
                    var orientation = GetJpegOrientation(s);
                    s.Position = 0;
                    // ZipPla CE: libjpeg-turbo があればそれで縮小して読む。縮小率の決め方は下の WIC の経路と同じ
                    if (TurboJpegDecoder.Available && TurboJpegDecoder.TryReadAll(s, out var jpegData, out var jpegOffset, out var jpegLength) &&
                        TurboJpegDecoder.TryReadHeader(jpegData, jpegOffset, jpegLength, out var jpegHeader))
                    {
                        var turboDenominator = ReducedJpegDenominatorForThumbnail(jpegHeader.Width, jpegHeader.Height, orientation, minSize);
                        if (turboDenominator <= 1) return null;
                        var turbo = TurboJpegDecoder.TryDecode(jpegData, jpegOffset, jpegLength, turboDenominator, grayAsIndexed: false); // WIC の経路と同じく灰色も 24bpp
                        if (turbo != null) return ViewerFormImageFilter.Rotate(new Bitmap(turbo), orientation);
                    }
                    s.Position = 0;
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(s,
                        System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile | System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                        System.Windows.Media.Imaging.BitmapCacheOption.None);
                    var frame = decoder.Frames[0];
                    int width = frame.PixelWidth, height = frame.PixelHeight;
                    // 90 度回転する向きなら、縦横を入れ替えて比べる
                    var swap = orientation == new DihedralFraction(1, 4) || orientation == new DihedralFraction(3, 4) ||
                        orientation == new DihedralFraction(1, -4) || orientation == new DihedralFraction(3, -4);
                    int needW = swap ? minSize.Height : minSize.Width, needH = swap ? minSize.Width : minSize.Height;
                    var scale = Math.Max((double)needW / width, (double)needH / height);
                    if (scale > 0.5) return null;
                    s.Position = 0;
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    // ZipPla CE: OnLoad では WPF のキャッシュに全部デコードしてから CopyPixels でもう一度コピーしていた。
                    // None なら GetBitmap の CopyPixels のときに WIC が GDI+ のバッファに直接書く。s は using の中なので開いている
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.None;
                    image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
                    // 1/2・1/4・1/8 ちょうどの大きさを求め、WIC が DCT の段階の縮小だけで済むようにする。
                    // 半端な大きさを求めると WIC がさらに再標本化し、呼び出し側の縮小と合わせて 2 回になっていた（D10）
                    var denominator = 2;
                    while (denominator < 8 && (width + denominator * 2 - 1) / (denominator * 2) >= Math.Ceiling(width * scale) &&
                        (height + denominator * 2 - 1) / (denominator * 2) >= Math.Ceiling(height * scale)) denominator *= 2;
                    image.DecodePixelWidth = (width + denominator - 1) / denominator;
                    image.DecodePixelHeight = (height + denominator - 1) / denominator; // 高さも指定しないと縦横比から 1 画素ずれて再標本化される
                    image.StreamSource = s;
                    image.EndInit();
                    System.Windows.Media.Imaging.BitmapSource source = image;
                    if (source.Format != System.Windows.Media.PixelFormats.Bgr24)
                    {
                        source = new System.Windows.Media.Imaging.FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgr24, null, 0);
                    }
                    return ViewerFormImageFilter.Rotate(BitmapResizer.GetBitmap(source), orientation);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// TryLoadReducedJpeg の縮小率（2・4・8）。縮小の効果がなければ 1。WIC の経路の計算と同じ
        /// </summary>
        private static int ReducedJpegDenominatorForThumbnail(int width, int height, DihedralFraction orientation, Size minSize)
        {
            var swap = orientation == new DihedralFraction(1, 4) || orientation == new DihedralFraction(3, 4) ||
                orientation == new DihedralFraction(1, -4) || orientation == new DihedralFraction(3, -4);
            int needW = swap ? minSize.Height : minSize.Width, needH = swap ? minSize.Width : minSize.Height;
            var scale = Math.Max((double)needW / width, (double)needH / height);
            if (scale > 0.5) return 1;
            var denominator = 2;
            while (denominator < 8 && (width + denominator * 2 - 1) / (denominator * 2) >= Math.Ceiling(width * scale) &&
                (height + denominator * 2 - 1) / (denominator * 2) >= Math.Ceiling(height * scale)) denominator *= 2;
            return denominator;
        }

        private static Bitmap LoadRotateBitmap(Stream stream)
        {
            using (var seekable = SeekableStream.Seekablize(stream, long.MaxValue, leaveOpen: true))
            {
                var orientation = GetJpegOrientation(seekable);
                var request = ReducedJpegDecodeRequest.Current;
                if (request != null)
                {
                    var reduced = TryDecodeReducedJpeg(seekable, orientation, request);
                    if (reduced != null) return reduced;
                }
                // ZipPla CE: libjpeg-turbo があればそれで読む（灰色は GDI+ と同じく 8bppIndexed）。読めなければ従来どおり GDI+
                var turbo = TurboJpegDecoder.TryDecode(seekable, 1, grayAsIndexed: true);
                if (turbo != null) return ViewerFormImageFilter.Rotate(new Bitmap(turbo), orientation);
                (seekable as SeekableStream)?.StopBuffering();
                return ViewerFormImageFilter.Rotate(new Bitmap(seekable), orientation);
            }
        }

        /// <summary>
        /// ビューアの先読み用に JPEG を 1/2・1/4・1/8 で縮小デコードする（C5）。WIC の JPEG デコーダは DCT の段階で縮小できる。
        /// 縮小後（回転後）のサイズが request の求める最小サイズ以上になる範囲で一番小さく読む。
        /// 縮小できない（1/2 でも足りない）、グレースケール・カラー以外（CMYK など）、読めない場合は
        /// ストリームの位置を戻して null を返す（呼び出し側は従来どおり原寸で読む）。
        /// </summary>
        private static Bitmap TryDecodeReducedJpeg(Stream stream, DihedralFraction orientation, ReducedJpegDecodeRequest request)
        {
            var start = stream.Position;
            System.Drawing.Bitmap result = null;
            // ZipPla CE: libjpeg-turbo があればそれで縮小して読む。ヘッダが読めたら（縮小しないと決まったときも）WIC の経路は通らない
            if (TurboJpegDecoder.Available)
            {
                if (tryDecodeReducedJpegWithTurbo(stream, orientation, request, out var turbo)) return turbo;
                stream.Position = start;
            }
            try
            {
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile | System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                    System.Windows.Media.Imaging.BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                int width = frame.PixelWidth, height = frame.PixelHeight;
                var swap = orientation == new DihedralFraction(1, 4) || orientation == new DihedralFraction(3, 4) ||
                    orientation == new DihedralFraction(1, -4) || orientation == new DihedralFraction(3, -4);
                var originalSize = swap ? new Size(height, width) : new Size(width, height);
                var minSize = request.MinimumSizeProvider(originalSize);
                if (minSize.Width <= 0 || minSize.Height <= 0) return rewind(stream, start);
                int needW = swap ? minSize.Height : minSize.Width, needH = swap ? minSize.Width : minSize.Height;
                var denom = 1;
                while (denom < 8 && (width + denom * 2 - 1) / (denom * 2) >= needW && (height + denom * 2 - 1) / (denom * 2) >= needH) denom *= 2;
                if (denom == 1) return rewind(stream, start);

                stream.Position = start;
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                // ZipPla CE: OnLoad では WPF のキャッシュに全部デコードしてから CopyPixels でもう一度コピーしていた。
                // None なら CopyPixels のときに WIC が（縮小・変換して）GDI+ のバッファに直接書く。stream は CopyPixels が終わるまで開いている
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.None;
                image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
                image.DecodePixelWidth = (width + denom - 1) / denom;
                image.StreamSource = stream;
                image.EndInit();

                // 縮小すると Bgr32 で返ることがある。CMYK などは GDI+ と色の扱いが違うかもしれないので従来どおり原寸で読む
                System.Windows.Media.Imaging.BitmapSource source = image;
                var gray = source.Format == System.Windows.Media.PixelFormats.Gray8;
                if (!gray)
                {
                    if (source.Format != System.Windows.Media.PixelFormats.Bgr24 && source.Format != System.Windows.Media.PixelFormats.Bgr32) return rewind(stream, start);
                    if (source.Format != System.Windows.Media.PixelFormats.Bgr24)
                    {
                        source = new System.Windows.Media.Imaging.FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgr24, null, 0);
                    }
                }
                if (source.PixelWidth < needW || source.PixelHeight < needH) return rewind(stream, start);

                // GDI+ と同じく、グレースケールは恒等パレットの 8bppIndexed、カラーは 24bpp にする
                result = new System.Drawing.Bitmap(source.PixelWidth, source.PixelHeight,
                    gray ? System.Drawing.Imaging.PixelFormat.Format8bppIndexed : System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                if (gray)
                {
                    var palette = result.Palette;
                    for (var i = 0; i < 256; i++) palette.Entries[i] = Color.FromArgb(i, i, i);
                    result.Palette = palette;
                }
                var data = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, result.PixelFormat);
                try
                {
                    source.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * data.Height, data.Stride);
                }
                finally
                {
                    result.UnlockBits(data);
                }
                var rotated = ViewerFormImageFilter.Rotate(new Bitmap(result), orientation);
                result = null; // Rotate に渡した
                request.OriginalSize = originalSize;
                request.Reduced = true;
                return rotated;
            }
            catch
            {
                result?.Dispose();
                return rewind(stream, start);
            }
        }

        /// <summary>
        /// TryDecodeReducedJpeg の libjpeg-turbo の経路。ヘッダが読めれば true を返し、bitmap は縮小して読んだもの（縮小しないときは null で、
        /// stream の位置は戻してある）。ヘッダが読めない・対応していない（CMYK など）・デコードに失敗したときは false（呼び出し側は WIC で読む）
        /// </summary>
        private static bool tryDecodeReducedJpegWithTurbo(Stream stream, DihedralFraction orientation, ReducedJpegDecodeRequest request, out Bitmap bitmap)
        {
            bitmap = null;
            var start = stream.Position;
            if (!TurboJpegDecoder.TryReadAll(stream, out var data, out var offset, out var length)) return false;
            if (!TurboJpegDecoder.TryReadHeader(data, offset, length, out var header)) return false;
            int width = header.Width, height = header.Height;
            var swap = orientation == new DihedralFraction(1, 4) || orientation == new DihedralFraction(3, 4) ||
                orientation == new DihedralFraction(1, -4) || orientation == new DihedralFraction(3, -4);
            var originalSize = swap ? new Size(height, width) : new Size(width, height);
            var minSize = request.MinimumSizeProvider(originalSize);
            stream.Position = start;
            if (minSize.Width <= 0 || minSize.Height <= 0) return true;
            int needW = swap ? minSize.Height : minSize.Width, needH = swap ? minSize.Width : minSize.Height;
            var denom = 1;
            while (denom < 8 && (width + denom * 2 - 1) / (denom * 2) >= needW && (height + denom * 2 - 1) / (denom * 2) >= needH) denom *= 2;
            if (denom == 1) return true;
            var decoded = TurboJpegDecoder.TryDecode(data, offset, length, denom, grayAsIndexed: true); // GDI+ と同じく、灰色は 8bppIndexed、カラーは 24bpp
            if (decoded == null) return false;
            bitmap = ViewerFormImageFilter.Rotate(new Bitmap(decoded), orientation);
            request.OriginalSize = originalSize;
            request.Reduced = true;
            return true;
        }

        private static Bitmap rewind(Stream stream, long position)
        {
            try { stream.Position = position; } catch { }
            return null;
        }

        private static Bitmap GetFullBitmap(string filePath, bool setImageInfo, out ImageInfo imageInfo, bool animation)
        {
            return GetFullBitmap(filePath, setImageInfo, out imageInfo, out var dummy, animation);
        }

        private static bool IsJpegPath(string path)
        {
            if (path == null) return false;
            var length = path.Length;
            return length >= 4 && path.Substring(length - 4).ToLower() == ".jpg" || length >= 5 && path.Substring(length - 5).ToLower() == ".jpeg";
        }

        // これ以下の大きさのファイルは、デコードの前に 1 回で読み込む（D8）
        private const long ReadWholeFileLimit = 64L << 20;

        /// <summary>
        /// デコード用にファイルを開く（D8）。Exif・ヘッダ・本体を先頭に戻りながら 4KB のバッファで何度も読まないように、
        /// 上限以下ならファイル全体を 1 回で読んだメモリのストリームを返す（NAS・HDD で効く）
        /// </summary>
        private static Stream OpenFileForDecode(string filePath)
        {
            var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            try
            {
                var length = fs.Length;
                if (length <= 0 || length > ReadWholeFileLimit)
                {
                    fs.Dispose();
                    return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
                }
                var bytes = new byte[length];
                var read = 0;
                while (read < bytes.Length)
                {
                    var r = fs.Read(bytes, read, bytes.Length - read);
                    if (r <= 0) break;
                    read += r;
                }
                fs.Dispose();
                return new MemoryStream(bytes, 0, read, writable: false, publiclyVisible: true);
            }
            catch
            {
                fs.Dispose();
                throw;
            }
        }

        private static Bitmap GetFullBitmap(string filePath, bool setImageInfo, out ImageInfo imageInfo, out bool needToRefresh, bool animation)
        {
            if (defaultSupportPathRegex.IsMatch(filePath))
            {
                needToRefresh = false;
                // var sw = new System.Diagnostics.Stopwatch();
                //sw.Start();

                // 0.7 秒程度
                var ext = Path.GetExtension(filePath).ToLower();
                Bitmap img;
                if (ext == ".ico")
                {
                    img = new Bitmap(filePath);
                }
                else
                {
                    using (var s = OpenFileForDecode(filePath))
                    {
                        if (animation && ext == ".gif")
                        {
                            img = GetGif(s);
                        }
                        else if (animation && (ext == ".png" || ext == ".apng"))
                        {
                            img = GetAPng(s);
                        }
                        else
                        {
                            img = IsJpegPath(filePath) ? LoadRotateBitmap(s) : new Bitmap(s);
                        }
                    }
                }

                if (setImageInfo) imageInfo = GetImageInfo(img);
                else imageInfo = null;
                return img;


                // この方法だと Bitmap が Dispose されるまで filePath にロックが掛かる
            }
            else
            {
                var extension = Path.GetExtension(filePath).ToLower();
                switch(extension)
                {
                    case ".psd":
                        {
                            needToRefresh = false;
#if !AUTOBUILD
                            try
#endif
                            {
                                var psd = new System.Drawing.PSD.PsdFile();
                                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                                {
                                    //psd.Load(filePath); // このオーバーロードは内部で FileAccess.Read が指定されておらずエラーが出ることがある
                                    psd.Load(stream);
                                    imageInfo = new ImageInfo(psd.Columns, psd.Rows, psd.Depth * psd.ImageData.Length);

                                    //var result = System.Drawing.PSD.ImageDecoder.DecodeImage(psd);
                                    var result = RiosFastPsdDecoder.DecodeImage(psd);
                                    return result;
                                }
                            }
#if !AUTOBUILD
                            catch (Exception e)
                            {
                                MessageBox.Show($"psd error:\n\n{e.ToString()}");
                                throw;
                            }
#endif
                        }
                    case ".dds":
                    case ".tga":
                        needToRefresh = false;
                        using (var image = new ImageEngineImage(filePath))
                        {
                            var result = image.GetGDIBitmap(ignoreAlpha: false, mergeAlpha: false);
                            imageInfo = GetImageInfo(result);
                            return (Bitmap)result;
                        }
                    default:
                        var withoutPeriod = extension.Substring(1);
                        if (WindowsMediaPhoto.SupportExtensionsInLowerWithoutPeriod.Contains(withoutPeriod))
                        {
                            needToRefresh = false;
                            var result = WindowsMediaPhoto.Load(filePath);
                            imageInfo = GetImageInfo(result);
                            return result;
                        }
                        string spiPath;
                        if (susieSupportsExtensionWithoutPeriodInLowerCaseToSpiName.TryGetValue(withoutPeriod, out spiPath))
                        {
                            needToRefresh = true;
                            using (var susie = new garu.Util.Susie(spiPath))
                            {
                                var result = susie.GetPicture(filePath);
                                if (result != null)
                                {
                                    imageInfo = GetImageInfo(result);
                                    return (Bitmap)result;
                                }
                            }
                            if (withoutPeriod == "jp2" && MovieThumbnailLoader.ffmpegExists())
                            {
                                needToRefresh = false;
                                var result = MovieThumbnailLoader.GetImageByFfmpeg(filePath);
                                imageInfo = GetImageInfo(result);
                                return result;
                            }
                            else
                            {
                                throw new Exception();
                            }
                        }
                        if (MovieThumbnailLoader.SupportedImageExtensionsWithPeriodInLowerCase.Contains(extension))// && MovieThumbnailLoader.ffmpegExists())
                        {
                            needToRefresh = false;
                            var result = extension == ".webp" && WebPDecoder.Available ? decodeWebPFile(filePath, animation) : null;
                            if (result == null) result = MovieThumbnailLoader.GetImageByFfmpeg(filePath);
                            imageInfo = GetImageInfo(result);
                            return result;
                        }
                        break;
                }
                throw new NotSupportedException();
            }
        }

        
        private static Bitmap GetFullBitmap(Stream stream, bool setImageInfo, out ImageInfo imageInfo, string filePathForGetFileType, bool animation, long streamLength)
        {
            if (filePathForGetFileType == null) throw new ArgumentNullException(nameof(filePathForGetFileType));
            if (defaultSupportPathRegex.IsMatch(filePathForGetFileType))
            {
                var ext = Path.GetExtension(filePathForGetFileType).ToLower();
                Bitmap img;
                if (animation && ext == ".gif")
                {
                    img = GetGif(stream);
                }
                else if (animation && (ext == ".png" || ext == ".apng"))
                {
                    img = GetAPng(stream);
                }
                else
                {
                    img = IsJpegPath(filePathForGetFileType) ? LoadRotateBitmap(stream) : new Bitmap(stream);
                }
                if (setImageInfo) imageInfo = GetImageInfo(img);
                else imageInfo = null;
                return img;
            }
            else
            {
                var extension = Path.GetExtension(filePathForGetFileType).ToLower();
                switch (extension)
                {
                    case ".psd":
                        {
                            var psd = new System.Drawing.PSD.PsdFile();
                            if (stream.CanSeek)
                            {
                                psd.Load(stream);
                                imageInfo = new ImageInfo(psd.Columns, psd.Rows, psd.Depth * psd.ImageData.Length);
                                return RiosFastPsdDecoder.DecodeImage(psd);
                            }
                            else
                            {
                                using (var ms = new MemoryStream())
                                {
                                    stream.CopyTo(ms);
                                    psd.Load(ms.ToArray());
                                    //psd.Load(ms);
                                    imageInfo = new ImageInfo(psd.Columns, psd.Rows, psd.Depth * psd.ImageData.Length);
                                    return RiosFastPsdDecoder.DecodeImage(psd);
                                }
                            }
                        }
                    case ".dds":
                    case ".tga":
                        if (stream.CanSeek)
                        {
                            using (var image = new ImageEngineImage(stream))
                            {
                                var result = image.GetGDIBitmap(ignoreAlpha: false, mergeAlpha: false);
                                imageInfo = GetImageInfo(result);
                                return (Bitmap)result;
                            }
                        }
                        else
                        {
                            using (var ms = new MemoryStream())
                            {
                                stream.CopyTo(ms);
                                using (var image = new ImageEngineImage(ms))
                                {
                                    var result = image.GetGDIBitmap(ignoreAlpha: false, mergeAlpha: false);
                                    imageInfo = GetImageInfo(result);
                                    return (Bitmap)result;
                                }
                            }
                        }
                    default:
                        var withoutPeriod = extension.Substring(1);
                        if (WindowsMediaPhoto.SupportExtensionsInLowerWithoutPeriod.Contains(withoutPeriod))
                        {
                            var result = WindowsMediaPhoto.Load(stream);
                            imageInfo = GetImageInfo(result);
                            return result;
                        }
                        string spiPath;
                        if (susieSupportsExtensionWithoutPeriodInLowerCaseToSpiName.TryGetValue(withoutPeriod, out spiPath))
                        {
                            using (var susie = new garu.Util.Susie(spiPath))
                            {
                                var buf = ReadImageBytes(stream, streamLength);
                                var result = susie.GetPicture(filePathForGetFileType, buf);
                                if (result != null)
                                {
                                    imageInfo = GetImageInfo(result);
                                    return (Bitmap)result;
                                }
                                else if (withoutPeriod == "jp2" && MovieThumbnailLoader.ffmpegExists())
                                {
                                    using (var ms = new MemoryStream(buf, writable: false))
                                    {
                                        var result2 = MovieThumbnailLoader.GetJpeg2000ImageByFfmpeg(ms, buf.Length); // 一時ファイルを使わない（D6）
                                        imageInfo = GetImageInfo(result2);
                                        return result2;
                                    }
                                }
                                else throw new Exception();
                            }
                        }

                        if (extension == ".webp" && WebPDecoder.Available)
                        {
                            // 静止画は libwebpdecoder、アニメーションは libwebpdemux で読む。読めなければ（DLL がないなど）同じバイト列を ffmpeg に渡す
                            var buf = ReadImageBytes(stream, streamLength);
                            var result = WebPDecoder.TryDecode(buf, buf.Length) ?? WebPDecoder.TryDecodeAnimation(buf, buf.Length, animation);
                            if (result == null)
                            {
                                using (var ms = new MemoryStream(buf, writable: false))
                                {
                                    result = MovieThumbnailLoader.GetWebPImageByFfmpeg(ms, buf.Length);
                                }
                            }
                            imageInfo = GetImageInfo(result);
                            return result;
                        }
                        if (MovieThumbnailLoader.SupportedImageExtensionsWithPeriodInLowerCase.Contains(extension))// && MovieThumbnailLoader.ffmpegExists())
                        {
                            var result = extension == ".webp" ? MovieThumbnailLoader.GetWebPImageByFfmpeg(stream, streamLength) :
                                extension != ".mj2" ? MovieThumbnailLoader.GetJpeg2000ImageByFfmpeg(stream, streamLength) : // 一時ファイルを使わない（D6。mj2 は動画なので従来どおり）
                                MovieThumbnailLoader.GetImageByFfmpeg(stream);
                            imageInfo = GetImageInfo(result);
                            return result;
                        }


                        break;
                }
                throw new NotSupportedException();
            }
        }

        private static Bitmap decodeWebPFile(string filePath, bool animation)
        {
            byte[] buf;
            try
            {
                buf = File.ReadAllBytes(filePath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is OutOfMemoryException)
            {
                return null; // ffmpeg に任せる
            }
            return WebPDecoder.TryDecode(buf, buf.Length) ?? WebPDecoder.TryDecodeAnimation(buf, buf.Length, animation);
        }

        private static byte[] ReadImageBytes(Stream stream, long streamLength)
        {
            var memory = stream as MemoryStream;
            ArraySegment<byte> segment;
            if (memory != null && memory.Position == 0 && memory.TryGetBuffer(out segment) &&
                segment.Offset == 0 && segment.Count == segment.Array.Length)
            {
                memory.Position = memory.Length;
                return segment.Array;
            }

            if (streamLength >= 0 && streamLength <= int.MaxValue)
            {
                var result = new byte[(int)streamLength];
                var read = 0;
                while (read < result.Length)
                {
                    var count = stream.Read(result, read, result.Length - read);
                    if (count == 0) break;
                    read += count;
                }
                var next = stream.ReadByte();
                if (next < 0 && read == result.Length) return result;

                using (var remainder = new MemoryStream())
                {
                    remainder.Write(result, 0, read);
                    if (next >= 0) remainder.WriteByte((byte)next);
                    stream.CopyTo(remainder);
                    return remainder.ToArray();
                }
            }

            using (var result = new MemoryStream())
            {
                stream.CopyTo(result);
                return result.ToArray();
            }
        }

        public static Bitmap GetFullBitmap(Stream stream, string filePathForGetFileType, bool animation = false)
        {
            return GetFullBitmap(stream, filePathForGetFileType, animation, -1);
        }

        public static Bitmap GetFullBitmap(Stream stream, string filePathForGetFileType, bool animation, long streamLength)
        {
            return GetFullBitmap(stream, false, out var dummy, filePathForGetFileType, animation, streamLength);
        }

        public static Bitmap GetFullBitmap(string filePath, bool animation = false)
        {
            return GetFullBitmap(filePath, false, out var dummy, animation);
        }

        //private static Bitmap getThumbnailBitmap(string filePath, bool setImageInfo, Size desiredSize, Func<Delegate, object> invoke, out ImageInfo imageInfo)
        private static Bitmap GetThumbnailBitmap(string filePath, bool setImageInfo, Size desiredSize, out ImageInfo imageInfo)
        {
            var ext = Path.GetExtension(filePath).ToLower();
            switch (ext)
            {
                case ".pdn":
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        if (setImageInfo)
                        {
                            var size = ImageInfo.GetPdnSizeAndSeekNext(fs);
                            imageInfo = new ImageInfo(size, 32);
                        }
                        else
                        {
                            ImageInfo.GetPdnSizeAndSeekNext(fs);
                            imageInfo = null;
                        }
                        var buf14 = new byte[14];
                        if (fs.Read(buf14, 0, 14) < 14) return null;
                        if (Encoding.ASCII.GetString(buf14).ToLower() != "<custom><thumb") return null;
                        var buf = new List<byte>();
                        var start = false;
                        var stop = false;
                        while (true)
                        {
                            var b = fs.ReadByte();
                            if (b < 0) return null;
                            if (b == 0x22)// '"'
                            {
                                if (start)
                                {
                                    stop = true;
                                    break;
                                }
                                else
                                {
                                    start = true;
                                }
                            }
                            else if (b == 0x3e) return null; // '>'
                            else if (start) buf.Add((byte)b);
                        }
                        if (!stop) return null;
                        var base64String = Encoding.ASCII.GetString(buf.ToArray());
                        buf = null;
                        using (var s = new MemoryStream(Convert.FromBase64String(base64String)))
                        {
                            return new Bitmap(s);
                        }
                    }
                case ".txt":
                case ".ini":
                    var text = GetText(filePath);
                    imageInfo = null;
                    if (text == null) return null;
                    var result = new Bitmap(desiredSize.Width, desiredSize.Height);
                    try
                    {
                        using (var g = Graphics.FromImage(result))
                        {
                            var font = SystemFonts.DefaultFont;
                            var rioSize = g.MeasureString("Rio", font);
                            DrawStringQuick(g, desiredSize, text, SystemFonts.DefaultFont, Color.Black, new PointF(rioSize.Width, rioSize.Height * 2));
                            //g.DrawString(text, SystemFonts.DefaultFont, Brushes.Black, new PointF(rioSize.Width, rioSize.Height * 2));
                        }
                        return result;
                    }
                    catch
                    {
                        result.Dispose();
                        throw;
                    }
                    
                case ".html":
                case ".htm":
                //case ".xml":
                //case ".mht":
                    // WebBrowser による実装

                    // HtmlRender による実装
                    const int zoom = 2; // / Program.DisplayMagnification;
                    var page = ConvertHtmlFromFileToImage(filePath, new SizeF(desiredSize.Width * zoom, desiredSize.Height * zoom),
                        stylesheetLoad: true, imageLoad: true);
                    imageInfo = null;
                    return page;
                default:
                    // 残りは TagLib で処理できる
                    imageInfo = null;
                    var file = TagLib.File.Create(filePath);
                    if (file == null) return null;
                    using (file)
                    {
                        var tag = file.Tag;
                        if (tag == null || tag.IsEmpty) return null;
                        var pictures = tag.Pictures;
                        if (pictures == null || pictures.Length <= 0) return null;
                        using (var s = new MemoryStream(pictures[0].Data.Data))
                        {
                            return new Bitmap(s);
                        }
                    }
            }
            throw new NotSupportedException();
        }

        private static void DrawStringQuick(Graphics g, Size canvasSize, string text, Font font, Color foreColor, PointF point)
        {
            var pointInt = new Point((int)Math.Round(point.X), (int)Math.Round(point.Y));
            var sb = new StringBuilder();
            var cursor = pointInt.Y;
            var height = canvasSize.Height;
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;// | TextFormatFlags.ExpandTabs;
            using (var sr = new StringReader(text))
            {
                while (sr.Peek() >= 0 && cursor < height)
                {
                    var line = sr.ReadLine().Replace("\t", "    ");
                    cursor += TextRenderer.MeasureText(line, font, new Size(int.MaxValue, int.MinValue), flags).Height;
                    sb.AppendLine(line);
                }
                TextRenderer.DrawText(g, sb.ToString(), font, new Rectangle(pointInt.X, pointInt.Y, canvasSize.Width - pointInt.X, canvasSize.Height - pointInt.Y), foreColor, flags);
            }
        }

        //private static Bitmap getAtLeastThumbnailBitmap(string filePath, bool setImageInfo, Size desiredSize, Func<Delegate, object> invoke, out ImageInfo imageInfo)
        private static Bitmap GetAtLeastThumbnailBitmap(string filePath, bool setImageInfo, Size desiredSize, ref bool? ffmpegExists, out ImageInfo imageInfo, out bool needToRefresh)
        {
            if (SupportsFullReading(filePath, ref ffmpegExists, out var requiredFfmpeg))
            {
                return GetFullBitmap(filePath, setImageInfo, out imageInfo, out needToRefresh, animation: false);
            }
            else if (!requiredFfmpeg)
            {
                //return getThumbnailBitmap(filePath, setImageInfo, desiredSize, invoke, out imageInfo);
                needToRefresh = false;
                return GetThumbnailBitmap(filePath, setImageInfo, desiredSize, out imageInfo);
            }
            else throw new Exception();
        }


        //public static Bitmap GetAtLeastThumbnailBitmap(string filePath, Size desiredSize, Func<Delegate, object> invoke, out ImageInfo imageInfo)
        public static Bitmap GetAtLeastThumbnailBitmap(string filePath, Size desiredSize, bool ffmpegExists, out ImageInfo imageInfo)
        {
            var nullable = ffmpegExists as bool?;
            return GetAtLeastThumbnailBitmap(filePath, true, desiredSize, ref nullable, out imageInfo, out var dummy);
        }
        public static Bitmap GetAtLeastThumbnailBitmap(string filePath, Size desiredSize, ref bool? ffmpegExists, out ImageInfo imageInfo)
        {
            //return getAtLeastThumbnailBitmap(filePath, true, desiredSize, invoke, out imageInfo);
            return GetAtLeastThumbnailBitmap(filePath, true, desiredSize, ref ffmpegExists, out imageInfo, out var dummy);
        }

        public static Bitmap GetAtLeastThumbnailBitmap(string filePath, Size desiredSize, ref bool? ffmpegExists, out ImageInfo imageInfo, out bool needToRefresh)
        {
            //return getAtLeastThumbnailBitmap(filePath, true, desiredSize, invoke, out imageInfo);
            return GetAtLeastThumbnailBitmap(filePath, true, desiredSize, ref ffmpegExists, out imageInfo, out needToRefresh);
        }


        public static ImageInfo GetImageInfo(string filePath, bool colorDepthMustBeGot, ref bool? ffmpegExists)
        {
            if(ImageInfo.Supports(filePath))
            {
                var result = new ImageInfo(filePath);
                if (!colorDepthMustBeGot || result.BitPerPixel > 0) return result;

                if (!SupportsFullReading(filePath, ref ffmpegExists)) return result;
            }
            
            using (var img = GetFullBitmap(filePath, false, out var dummy, animation: false))
            {
                return GetImageInfo(img);
            }
        }

        public static ImageInfo GetImageInfo(Image img)
        {
            return new ImageInfo(img.Size, Image.GetPixelFormatSize(img.PixelFormat));
        }

        private static string GetText(string fileName)
        {
            return GetText(File.ReadAllBytes(fileName));
        }

        private static string GetText(byte[] data)
        {
            var encode = GetCode(data);
            if (encode != null)
            {
                int preambleLength = encode.GetPreamble().Length;
                return encode.GetString(data, preambleLength, data.Length - preambleLength);
            }
            else return null;
        }

        public static Encoding GetCode(byte[] data)
        {
            if (data.Length >= 2)
            {
                if ((data[0] == 0xfe) && (data[1] == 0xff))
                {
                    return new UnicodeEncoding(true, true);
                }
                if ((data[0] == 0xff) && (data[1] == 0xfe))
                {
                    if ((4 <= data.Length) &&
                        (data[2] == 0x00) && (data[3] == 0x00))
                    {
                        return new UTF32Encoding(false, true);
                    }
                    return new UnicodeEncoding(false, true);
                }
                if (data.Length >= 3)
                {
                    if ((data[0] == 0xef) && (data[1] == 0xbb) && (data[2] == 0xbf))
                    {
                        return new UTF8Encoding(true, true);
                    }
                    if (data.Length >= 4)
                    {
                        if ((data[0] == 0x00) && (data[1] == 0x00) &&
                            (data[2] == 0xfe) && (data[3] == 0xff))
                        {
                            return new UTF32Encoding(true, true);
                        }
                    }
                }

            }

            const byte bEscape = 0x1B;
            const byte bAt = 0x40;
            const byte bDollar = 0x24;
            const byte bAnd = 0x26;
            const byte bOpen = 0x28;
            const byte bB = 0x42;
            const byte bD = 0x44;
            const byte bJ = 0x4A;
            const byte bI = 0x49;
            int len = data.Length;
            byte b1, b2, b3, b4;
            bool isBinary = false;
            for (int i = 0; i < len; i++)
            {
                b1 = data[i];
                if (b1 <= 0x06 || b1 == 0x7F || b1 == 0xFF)
                {
                    isBinary = true;
                    if (b1 == 0x00 && i < len - 1 && data[i + 1] <= 0x7F)
                    {
                        return Encoding.Unicode;
                    }
                }
            }
            if (isBinary)
            {
                return null;
            }
            bool notJapanese = true;
            for (int i = 0; i < len; i++)
            {
                b1 = data[i];
                if (b1 == bEscape || 0x80 <= b1)
                {
                    notJapanese = false;
                    break;
                }
            }
            if (notJapanese)
            {
                return Encoding.ASCII;
            }
            for (int i = 0; i < len - 2; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];
                b3 = data[i + 2];

                if (b1 == bEscape)
                {
                    if (b2 == bDollar && b3 == bAt)
                    {
                        return Encoding.GetEncoding(50220);
                    }
                    else if (b2 == bDollar && b3 == bB)
                    {
                        return Encoding.GetEncoding(50220);
                    }
                    else if (b2 == bOpen && (b3 == bB || b3 == bJ))
                    {
                        return Encoding.GetEncoding(50220);
                    }
                    else if (b2 == bOpen && b3 == bI)
                    {
                        return Encoding.GetEncoding(50220);
                    }
                    if (i < len - 3)
                    {
                        b4 = data[i + 3];
                        if (b2 == bDollar && b3 == bOpen && b4 == bD)
                        {
                            return Encoding.GetEncoding(50220);
                        }
                        if (i < len - 5 &&
                            b2 == bAnd && b3 == bAt && b4 == bEscape &&
                            data[i + 4] == bDollar && data[i + 5] == bB)
                        {
                            return Encoding.GetEncoding(50220);
                        }
                    }
                }
            }
            int sjis = 0;
            int euc = 0;
            int utf8 = 0;
            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];
                if (((0x81 <= b1 && b1 <= 0x9F) || (0xE0 <= b1 && b1 <= 0xFC)) &&
                    ((0x40 <= b2 && b2 <= 0x7E) || (0x80 <= b2 && b2 <= 0xFC)))
                {
                    sjis += 2;
                    i++;
                }
            }
            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];
                if (((0xA1 <= b1 && b1 <= 0xFE) && (0xA1 <= b2 && b2 <= 0xFE)) ||
                    (b1 == 0x8E && (0xA1 <= b2 && b2 <= 0xDF)))
                {
                    euc += 2;
                    i++;
                }
                else if (i < len - 2)
                {
                    b3 = data[i + 2];
                    if (b1 == 0x8F && (0xA1 <= b2 && b2 <= 0xFE) &&
                        (0xA1 <= b3 && b3 <= 0xFE))
                    {
                        euc += 3;
                        i += 2;
                    }
                }
            }
            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];
                if ((0xC0 <= b1 && b1 <= 0xDF) && (0x80 <= b2 && b2 <= 0xBF))
                {
                    utf8 += 2;
                    i++;
                }
                else if (i < len - 2)
                {
                    b3 = data[i + 2];
                    if ((0xE0 <= b1 && b1 <= 0xEF) && (0x80 <= b2 && b2 <= 0xBF) &&
                        (0x80 <= b3 && b3 <= 0xBF))
                    {
                        utf8 += 3;
                        i += 2;
                    }
                }
            }
            if (euc > sjis && euc > utf8)
            {
                return Encoding.GetEncoding(51932);
            }
            else if (sjis > euc && sjis > utf8)
            {
                return Encoding.GetEncoding(932);
            }
            else if (utf8 > euc && utf8 > sjis)
            {
                return Encoding.UTF8;
            }
            return null;
        }

        // WebBrowser を使う方法。パフォーマンスに問題があるので廃止
        
        private static Bitmap ConvertHtmlFromFileToImage(string fileName, SizeF size, bool stylesheetLoad, bool imageLoad)
        {
            var html = GetText(fileName);
            if (html == null) return null;
            Bitmap m_Bitmap = new Bitmap((int)Math.Round(size.Width), (int)Math.Round(size.Height));
            try
            {
                PointF point = new PointF(0, 0);
                //SizeF maxSize = size;
                //SizeF maxSize = new SizeF(float.MaxValue, size.Height);
                SizeF maxSize = SizeF.Empty;
                var fileUri = stylesheetLoad || imageLoad ? new Uri(fileName) : null;
                using (var g = Graphics.FromImage(m_Bitmap))
                {
                    TheArtOfDev.HtmlRenderer.WinForms.HtmlRender.Render(g, html, point, maxSize,
                        stylesheetLoad: !stylesheetLoad ? null as EventHandler<TheArtOfDev.HtmlRenderer.Core.Entities.HtmlStylesheetLoadEventArgs>:
                        (sender, e) =>
                        {
                            try
                            {
                                var srcPath = e.Src;
                                if (string.IsNullOrEmpty(srcPath)) return;
                                srcPath = System.Net.WebUtility.UrlDecode(srcPath);
                                srcPath = new Uri(fileUri, srcPath).LocalPath;
                                if (File.Exists(srcPath))
                                {
                                    e.SetStyleSheetData = TheArtOfDev.HtmlRenderer.WinForms.HtmlRender.ParseStyleSheet(GetText(srcPath));
                                }
                            }
                            catch { }
                        },
                        imageLoad: !imageLoad ? null as EventHandler<TheArtOfDev.HtmlRenderer.Core.Entities.HtmlImageLoadEventArgs>:
                        (sender, e) =>
                        {
                            try
                            {
                                var srcPath = e.Src;
                                if (string.IsNullOrEmpty(srcPath)) return;
                                srcPath = System.Net.WebUtility.UrlDecode(srcPath);
                                srcPath = new Uri(fileUri, srcPath).LocalPath;
                                if (File.Exists(srcPath))
                                {
                                    //e.SetStyleSheetData = TheArtOfDev.HtmlRenderer.WinForms.HtmlRender.ParseStyleSheet(getText(srcPath));
                                    e.Handled = true;
                                    e.Callback(srcPath);
                                }

                            }
                            catch { }
                        }
                   );
                }
                return m_Bitmap;
            }
            catch
            {
                m_Bitmap.Dispose();
                throw;
            }
        }


        private static Bitmap GetAPng(Stream stream)
        {
            var result = APngLoader.GetAPng(stream);
            return result;
        }

        private static Bitmap GetGif(Stream stream)
        {
            //var tm = new TimeMeasure();
            //tm.Block("ana");
            MemoryStream ms;
            int frameCount;
            List<GifImageData> imageDatas;
            using (var seekable = SeekableStream.Seekablize(stream, long.MaxValue, leaveOpen: true))
            {
                var p = seekable.Position;
                GifFileData fileData;
                try
                {
                    fileData = GifAnalyzer.Analyz(seekable);
                }
                catch
                {
                    seekable.Position = p;
                    (seekable as SeekableStream)?.StopBuffering();
                    return new Bitmap(seekable);
                }
                seekable.Position = p;
                (seekable as SeekableStream)?.StopBuffering();
                if (fileData == null) return new Bitmap(seekable);
                imageDatas = fileData.ImageDatas;
                frameCount = imageDatas.Count;
                if (frameCount <= 1) return new Bitmap(seekable);
                //tm.Block("get");

                // 取り出し済みのメモリなら、同じ配列を指すストリームを作り、全体を写さない（D10）
                if (stream is MemoryStream source && source.TryGetBuffer(out var segment) && p >= 0 && p <= source.Length)
                {
                    ms = new MemoryStream(segment.Array, segment.Offset + (int)p, (int)(source.Length - p), writable: false, publiclyVisible: true);
                }
                else
                {
                    ms = new MemoryStream();
                    try
                    {
                        seekable.CopyTo(ms);
                    }
                    catch
                    {
                        ms.Dispose();
                        throw;
                    }
                }
            }
            return new BitmapEx(new GifProvider(ms, frameCount), from d in imageDatas select TimeSpan.FromMilliseconds(d.iDelayTime * 10));
        }

        private class GifProvider : IEnumerator<Bitmap>
        {
            private Stream stream;
            private Image image;
            private System.Drawing.Imaging.FrameDimension frameDimension;
            private int frameCount;
            private int currentIndex;
            public GifProvider(Stream stream, int delayFrameCount)
            {
                this.stream = stream;
                try
                {
                    image = Image.FromStream(stream);
                    try
                    {
                        frameDimension = new System.Drawing.Imaging.FrameDimension(image.FrameDimensionsList[0]);
                        if (image.GetFrameCount(frameDimension) != delayFrameCount) throw new Exception();
                        frameCount = delayFrameCount;

                        currentIndex = -1;
                    }
                    catch
                    {
                        image.Dispose();
                        throw;
                    }
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }

            public Bitmap Current
            {
                get
                {
                    image.SelectActiveFrame(frameDimension, currentIndex);
                    return CreateNewBitmap(image, removeAlpha: true); // ここで 32 -> 24 にすることで遅延読み込みが無駄になることを防ぐ
                }
            }

            object IEnumerator.Current => Current as object;

            public void Dispose()
            {
                if (stream != null)
                {
                    stream.Dispose();
                    stream = null;
                    image.Dispose();
                }
            }

            public bool MoveNext()
            {
                return ++currentIndex < frameCount;
            }

            public void Reset()
            {
                currentIndex = 0;
            }
        }

        public static Bitmap CreateNewBitmap(Image image, bool removeAlpha)
        {
            var bitmap = image as System.Drawing.Bitmap;
            var isNewBitmap = bitmap == null;
            if (isNewBitmap) bitmap = new System.Drawing.Bitmap(image);
            if (removeAlpha)
            {
                var myBitmap = (Bitmap)bitmap;
                Bitmap changed;
                if (ViewerFormImageFilter.TryGetMaxColorDiff(myBitmap) == 0)
                {
                    changed = ViewerForm.ToBitmap8Unsafe(myBitmap, leaveOpenOriginal: true);
                }
                else
                {
                    changed = ViewerForm.ToBitmap24Unsafe(myBitmap, leaveOpenOriginal: true);
                }
                if (isNewBitmap)
                {
                    if (changed != myBitmap) myBitmap.Dispose();
                    return changed;
                }
                else if (changed != myBitmap)
                {
                    return changed;
                }
            }
            else if (isNewBitmap)
            {
                return (Bitmap)bitmap;
            }
            
            var pixelFormat = bitmap.PixelFormat;
            var result = new Bitmap(bitmap.Width, bitmap.Height, pixelFormat);
            try
            {
                if ((pixelFormat & System.Drawing.Imaging.PixelFormat.Indexed) == System.Drawing.Imaging.PixelFormat.Indexed)
                {
                    result.Palette = bitmap.Palette;
                }
                var d = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, pixelFormat);
                try
                {
                    var dp = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, pixelFormat);
                    try
                    {
                        ViewerForm.CopyMemory(d.Scan0, dp.Scan0, d.Height * d.Stride);
                    }
                    finally
                    {
                        bitmap.UnlockBits(dp);
                    }
                }
                finally
                {
                    result.UnlockBits(d);
                }
            }
            catch
            {
                result.Dispose();
                throw;
            }
            return result;
        }
    }


    /// <summary>
    /// JPEG を縮小デコードしてよいことを、同じスレッドの ImageLoader に伝える（C5）。
    /// ビューアは先読みのページを読む間だけ Current に設定する。書庫・フォルダなど読み込み経路によらず、
    /// JPEG のデコード（LoadRotateBitmap）がこれを見る。縮小したら Reduced と回転後の原寸 OriginalSize が入る。
    /// </summary>
    public sealed class ReducedJpegDecodeRequest
    {
        [ThreadStatic] private static ReducedJpegDecodeRequest current;
        public static ReducedJpegDecodeRequest Current { get { return current; } set { current = value; } }

        /// <summary>回転後の原寸を受け取り、縮小後に必要な最小サイズを返す。縮小しないなら Size.Empty。</summary>
        public readonly Func<Size, Size> MinimumSizeProvider;
        public Size OriginalSize { get; internal set; }
        public bool Reduced { get; internal set; }

        public ReducedJpegDecodeRequest(Func<Size, Size> minimumSizeProvider)
        {
            MinimumSizeProvider = minimumSizeProvider ?? throw new ArgumentNullException(nameof(minimumSizeProvider));
        }
    }

}

