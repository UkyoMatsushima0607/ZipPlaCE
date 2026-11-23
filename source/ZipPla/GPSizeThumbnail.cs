using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace ZipPla
{
    // サムネイルデータの仕様説明のためのクラス
    // エラー処理は行わない。実際には使わない。リリース版ではコンパイルしない。
    // そもそも無条件に全ての画像を読み込むような使い方は想定していない。
#if DEBUG
    class ZipPlaThumbnail
    {
        public uint Version;
        public DateTime LastWriteTime;
        public int Index;
        public byte[] GeneralPurposeData;
        public List<Size> ReducedSize = new List<Size>();
        public List<Fraction> CacheSAR = new List<Fraction>();
        public List<Image> ThumbnailImage = new List<Image>();

        public ZipPlaThumbnail(Stream stream)
        {
            using (var reader = new BinaryReader(stream))
            {
                // サムネイルファイルの仕様のバージョン。現在は 1
                Version = reader.ReadUInt32();

                // 元データの更新日時。サムネイルが最新のものであるかの判定に使う
                LastWriteTime = new DateTime(reader.ReadInt64());

                // サムネイル画像を指定するための数値。書籍のページ数、動画の秒数等。-1 で未指定を表す
                Index = reader.ReadInt32();

                // サムネイル以外の情報。書籍の全ページ数、画像のサイズや色数、動画の長さ等
                // 現在の ZipPla では generalPurposeDataSize が負の数や 128 を超える数になった場合不正なデータとみなす
                var generalPurposeDataSize = reader.ReadInt32();
                GeneralPurposeData = reader.ReadBytes(generalPurposeDataSize);

                // サムネイル画像。小さい方から順に格納される
                // ここに必要なサイズが見つかればそれを利用し、
                // 見つからなければ元データらからサムネイルを作成してここに加える
                // 読み込んだデータを再度サイズ調整する際に高域の周波数成分にロスが生じることを想定し
                // 実際に必要な最大のサイズよりも 1.4 倍程度余裕を持った画像を利用する
                while (stream.Position < stream.Length)
                {
                    // 縮小後の画像サイズ。等倍の場合に限り width == -1, height == -1 となる
                    // 等倍のキャッシュは、動画データのように等倍のサムネイルデータを読み込むコストが
                    // 元データ全てを読み込むコストに比べて著しく低いと認められる場合に作成される
                    var width = reader.ReadInt32();
                    var height = reader.ReadInt32();
                    ReducedSize.Add(width > 0 && height > 0 ? new Size(width, height) : Size.Empty);

                    // キャッシュデータのピクセルの横÷縦の比。
                    // 約分されていなくても読み込めるが、ZipPla が作成するデータは全て約分済み
                    // 現在の実装では ReducedSize が Size.Empty である場合のみ比が 1 以外になるようにデータが作成される
                    // この条件が満たされないとキャッシュデータ読み込み時に必要サイズが正しく評価されない
                    CacheSAR.Add(new Fraction(numerator: reader.ReadInt32(), denominator: reader.ReadInt32()));

                    // 画像データ
                    // ヘッダを含めた完全なデータを格納
                    // 現在は JPEG XR を使用しているため実際には Image.FromStream(memoryStream) は使用できない
                    // 画像サイズに余裕を持たせるため高域の周波数成分を持つノイズは問題にならないので
                    // 比較的圧縮率の高い非可逆圧縮を利用する
                    var imageDataSize = reader.ReadUInt64();
                    var imageData = reader.ReadBytes((int)imageDataSize);
                    using (var memoryStream = new MemoryStream(imageData))
                    {
                        ThumbnailImage.Add(Image.FromStream(memoryStream));
                    }
                }
            }
        }
    }
#endif

    // NTFS でないときのエラーによるペナルティ、NTFS であるかをチェックするためのコスト、NTFS 以外が対象になる頻度を
    // 総合的に考慮し、NTFS であるかどうかのチェックは行わない
    public class GPSizeThumbnail
    {
        public const string AlternateDataStream = "<ADS>";
        public const string STREAM_NAME = "ZipPla.Thumbnail"; // "$ZIPPLA_THUMBNAIL";
        public const string CACHE_FILE_SUFFIX = "." + STREAM_NAME; // "$ZIPPLA_THUMBNAIL";
        // 将来のファイルシステムで代替データストリームとの互換性を考慮した機能が追加された場合
        // 使用頻度の高い Zone.Identifier と同様の書式にしておくことでその恩恵を受けられることが期待できる
        private const double SIZE_MARGIN = 1.4142135623730950488; // Lanczos3 の周波数特性より、完全再現するならこのレベルは必要
        private const int COMMON_RATIO = 2;
        private const int AREA_UBOUND = 160 * 120; // JPEG のサムネイルサイズ
        
        //private const long JPEG_QUALITY = 90; // 最低でも SIZE_MARGIN だけ縮小されるのでモスキートノイズは気にならない
        private const byte JXR_QUALITY = 30; // 大きい方が高圧縮、JPEG の 90 の半分程度に調整、255 まで設定できるが 100 程度を超えると正常に圧縮されない
        private const int DATA_UBOUND = 128; // 安全のため。増やしてもバージョンを変える必要はない
        private const uint VERSION = 2;

        // 注目点の記録（P4）。ピラミッドの後ろに、幅・高さ 0、分子・分母 FOCUS_MAGIC のエントリとして足す。
        // 幅・高さが 0 なので以前の版は選ばずに読み飛ばし、以前の版が書き直すと消える（ヘッダの版は変えない）
        private const int FOCUS_MAGIC = 0x46435A50; // "PZCF"
        private const int FOCUS_ENTRY_VERSION = 2; // 2: グレー値の計算を直した（X10）。1 の記録は読み飛ばして計算し直す
        private const int FOCUS_PAYLOAD_LENGTH = 4 + 4 * 4 + 4 + 4 + 2 * 4;
        private const int MAX_FOCUS_MEMOS = 4;

        /// <summary>注目点（BitmapAnalyzer.FocusCell）と、それを計算した範囲（元画像に対する割合）・画素の縦横比</summary>
        internal sealed class FocusMemo
        {
            public float RectX, RectY, RectWidth, RectHeight;
            public int PixelRatioNumerator, PixelRatioDenominator;
            public BitmapAnalyzer.FocusCell Cell;

            public bool SameKey(float x, float y, float w, float h, Fraction pixelRatio)
            {
                const float eps = 1e-4f;
                return Math.Abs(RectX - x) < eps && Math.Abs(RectY - y) < eps && Math.Abs(RectWidth - w) < eps && Math.Abs(RectHeight - h) < eps &&
                    new Fraction(PixelRatioNumerator, PixelRatioDenominator) == pixelRatio;
            }

            public static FocusMemo Read(byte[] buf, int offset, int length)
            {
                if (length < FOCUS_PAYLOAD_LENGTH || BitConverter.ToInt32(buf, offset) != FOCUS_ENTRY_VERSION) return null;
                var m = new FocusMemo
                {
                    RectX = BitConverter.ToSingle(buf, offset + 4),
                    RectY = BitConverter.ToSingle(buf, offset + 8),
                    RectWidth = BitConverter.ToSingle(buf, offset + 12),
                    RectHeight = BitConverter.ToSingle(buf, offset + 16),
                    PixelRatioNumerator = BitConverter.ToInt32(buf, offset + 20),
                    PixelRatioDenominator = BitConverter.ToInt32(buf, offset + 24),
                    Cell = new BitmapAnalyzer.FocusCell
                    {
                        WorkingWidth = BitConverter.ToInt16(buf, offset + 28),
                        WorkingHeight = BitConverter.ToInt16(buf, offset + 30),
                        X = BitConverter.ToInt16(buf, offset + 32),
                        Y = BitConverter.ToInt16(buf, offset + 34),
                    },
                };
                if (m.PixelRatioNumerator <= 0 || m.PixelRatioDenominator <= 0) return null;
                var c = m.Cell;
                if (c.WorkingWidth <= 0 || c.WorkingHeight <= 0 || c.X < 0 || c.Y < 0 || c.X >= c.WorkingWidth || c.Y >= c.WorkingHeight) return null;
                return m;
            }

            public void WriteEntry(BinaryWriter w)
            {
                w.Write(0u);
                w.Write(0u);
                w.Write(FOCUS_MAGIC);
                w.Write(FOCUS_MAGIC);
                w.Write((long)FOCUS_PAYLOAD_LENGTH);
                w.Write(FOCUS_ENTRY_VERSION);
                w.Write(RectX);
                w.Write(RectY);
                w.Write(RectWidth);
                w.Write(RectHeight);
                w.Write(PixelRatioNumerator);
                w.Write(PixelRatioDenominator);
                w.Write((short)Cell.WorkingWidth);
                w.Write((short)Cell.WorkingHeight);
                w.Write((short)Cell.X);
                w.Write((short)Cell.Y);
            }
        }

        /// <summary>
        /// 1 つのキャッシュファイルの注目点の記録（P4）。PlaClip の切り抜きで注目点の計算（1 件 2〜3 ms）を省く。
        /// 注目点は作業用の縮小画像（長い辺 128）のマスで決まり、元画像の解像度によらないので、表示サイズが変わっても使える
        /// </summary>
        public sealed class FocusCache
        {
            internal readonly string CacheRoot, FileName;
            internal readonly int Index;
            internal readonly List<FocusMemo> Memos = new List<FocusMemo>();

            internal FocusCache(string cacheRoot, string fileName, int index)
            {
                CacheRoot = cacheRoot;
                FileName = fileName;
                Index = index;
            }

            private static void Normalize(Size imageSize, Rectangle rectangle, out float x, out float y, out float w, out float h)
            {
                x = (float)rectangle.X / imageSize.Width;
                y = (float)rectangle.Y / imageSize.Height;
                w = (float)rectangle.Width / imageSize.Width;
                h = (float)rectangle.Height / imageSize.Height;
            }

            public bool TryGetFocus(Size imageSize, Rectangle rectangle, Fraction pixelRatio, out Point focus)
            {
                focus = Point.Empty;
                if (imageSize.Width <= 0 || imageSize.Height <= 0 || rectangle.Width <= 0 || rectangle.Height <= 0) return false;
                Normalize(imageSize, rectangle, out var x, out var y, out var w, out var h);
                var workingSize = BitmapAnalyzer.GetFocusWorkingSize(rectangle.Size, pixelRatio);
                foreach (var m in Memos)
                {
                    if (m.SameKey(x, y, w, h, pixelRatio) && m.Cell.WorkingWidth == workingSize.Width && m.Cell.WorkingHeight == workingSize.Height)
                    {
                        focus = BitmapAnalyzer.FocusCellToPoint(m.Cell, rectangle);
                        return true;
                    }
                }
                return false;
            }

            /// <summary>計算した注目点を記録し、キャッシュファイルへの追記を後回しの書き込みに登録する</summary>
            public void Add(Size imageSize, Rectangle rectangle, Fraction pixelRatio, BitmapAnalyzer.FocusCell cell)
            {
                if (imageSize.Width <= 0 || imageSize.Height <= 0 || rectangle.Width <= 0 || rectangle.Height <= 0) return;
                if (cell.WorkingWidth > short.MaxValue || cell.WorkingHeight > short.MaxValue) return;
                Normalize(imageSize, rectangle, out var x, out var y, out var w, out var h);
                Memos.RemoveAll(m => m.SameKey(x, y, w, h, pixelRatio));
                Memos.Add(new FocusMemo
                {
                    RectX = x, RectY = y, RectWidth = w, RectHeight = h,
                    PixelRatioNumerator = pixelRatio.Numerator, PixelRatioDenominator = pixelRatio.Denominator, Cell = cell,
                });
                while (Memos.Count > MAX_FOCUS_MEMOS) Memos.RemoveAt(0);
                EnqueuePendingWrite(new PendingWrite { CacheRoot = CacheRoot, FileName = FileName, Index = Index, Focus = Memos.ToArray() });
            }
        }

        // キャッシュの画像の形式（P4）。以前は JPEG XR（品質 30）だけだったが、WPF での展開が 1 枚 3.6〜7.3 ms かかり、
        // キャッシュに当たっても作り直すのと変わらなかった。透明度のない画像は JPEG（品質 90）で保存し、GDI+ で展開する（0.4 ms 前後）。
        // 透明度のある画像は従来どおり JPEG XR。読むときはデータの先頭で見分けるので、既存のキャッシュもそのまま使える
        // （古い版は WPF で読むが、WPF は JPEG も読めるので、新しいキャッシュも読める）。ヘッダの版は変えない
        private const long CACHE_JPEG_QUALITY = 90;
        private static readonly ImageCodecInfo CacheJpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        private static void SaveCacheImage(Stream stream, Bitmap img)
        {
            var pf = img.PixelFormat;
            // 透明度を持つ形式でも、実際にはすべて不透明なら JPEG にする（GDI+ はグレーの PNG なども 32bppArgb で読むため）
            if (((pf & PixelFormat.Alpha) == PixelFormat.Alpha || (pf & PixelFormat.PAlpha) == PixelFormat.PAlpha) && !IsOpaque32(img))
            {
                WindowsMediaPhoto.Save(stream, img, JXR_QUALITY);
                return;
            }
            using (var parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, CACHE_JPEG_QUALITY);
                if (pf == PixelFormat.Format24bppRgb || pf == PixelFormat.Format32bppRgb)
                {
                    ((Image)img).Save(stream, CacheJpegCodec, parameters);
                }
                else
                {
                    // 8bpp（グレー）などは 24bpp にしてから保存する（GDI+ に 24bpp としてロックさせて写す。DrawImage は DPI で拡大縮小されうる）
                    using (var converted = new System.Drawing.Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb))
                    {
                        var rect = new Rectangle(0, 0, img.Width, img.Height);
                        var src = img.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                        try
                        {
                            var dst = converted.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                            try
                            {
                                var rowBytes = img.Width * 3;
                                var row = new byte[rowBytes];
                                for (var y = 0; y < img.Height; y++)
                                {
                                    System.Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, row, 0, rowBytes);
                                    System.Runtime.InteropServices.Marshal.Copy(row, 0, dst.Scan0 + y * dst.Stride, rowBytes);
                                }
                            }
                            finally
                            {
                                converted.UnlockBits(dst);
                            }
                        }
                        finally
                        {
                            img.UnlockBits(src);
                        }
                        converted.Save(stream, CacheJpegCodec, parameters);
                    }
                }
            }
        }

        // 32bppArgb / PArgb で、すべての画素が不透明か（それ以外の形式は false）
        private static unsafe bool IsOpaque32(Bitmap img)
        {
            var pf = img.PixelFormat;
            if (pf != PixelFormat.Format32bppArgb && pf != PixelFormat.Format32bppPArgb) return false;
            var rect = new Rectangle(0, 0, img.Width, img.Height);
            var data = img.LockBits(rect, ImageLockMode.ReadOnly, pf);
            try
            {
                for (var y = 0; y < data.Height; y++)
                {
                    var row = (byte*)data.Scan0 + (long)y * data.Stride;
                    for (var x = 3; x < data.Width * 4; x += 4)
                    {
                        if (row[x] != 255) return false;
                    }
                }
                return true;
            }
            finally
            {
                img.UnlockBits(data);
            }
        }

        private static Bitmap LoadCacheImage(byte[] buf)
        {
            using (var stream = new MemoryStream(buf))
            {
                if (buf.Length >= 2 && buf[0] == 0xFF && buf[1] == 0xD8) // JPEG
                {
                    using (var decoded = new System.Drawing.Bitmap(stream))
                    {
                        // ストリームから切り離すため 24bpp に複製する
                        return new Bitmap(decoded.Clone(new Rectangle(0, 0, decoded.Width, decoded.Height), PixelFormat.Format24bppRgb));
                    }
                }
                return WindowsMediaPhoto.Load(stream);
            }
        }
        // バージョン(uint)、更新日時(long)、インデックス(int)、
        // 汎用データ長(int)、汎用データ、
        // 以下縮小後幅(uint)、拡大縮小後高さ(uint)、縮小後ピクセル比横（int）、縮小後ピクセル比縦（int）、ファイルサイズ(long)、実体、の繰り返し
        // ただしオリジナルサイズは高さ・幅共 uint.MaxValue で表す
        //
        // ファイルサイズ情報を含めないことはフォルダに対してもサムネイル情報を付加する前提から正当化される
        // 
        // バージョン履歴（概要）
        // 0: 初期
        // 1: 内部フォーマットを JPEG → JPEG XR に
        // 2: アスペクト比を保存するように

        private static readonly ConcurrentDictionary<string, DateTime> editingInfo = new ConcurrentDictionary<string, DateTime>();
        public static void ClearBlackList()
        {
            editingInfo.Clear();
        }
        public static void EnterEditing(string path)
        {
            var pathInLower = path.ToLower();
            editingInfo[pathInLower] = DateTime.MaxValue;
        }
        public static void ExitedEditing(string path)
        {
            var pathInLower = path.ToLower();
            try
            {
                //editingInfo[pathInLower] = File.GetLastWriteTime(path);
                editingInfo[pathInLower] = File.GetLastAccessTime(path);
            }
            catch { }
        }
        public static bool Editing(string path)
        {
            var pathInLower = path.ToLower();
            DateTime time;
            try
            {
                //return editingInfo.TryGetValue(pathInLower, out time) && File.GetLastWriteTime(path) <= time;
                return editingInfo.TryGetValue(pathInLower, out time) && File.GetLastAccessTime(path) <= time;
            }
            catch
            {
                return false;
            }
        }



        /*
    public static void ClearBlackList()
    {
        lock (blackList)
        {
            blackList.Clear();
        }
    }

    private static readonly Dictionary<string, int> blackList = new Dictionary<string, int>(); // スレッドセーフでない
    public static bool Editing(string path, int weightDecrease = 1)
    {
        //return path != null && blackList.Contains(path.ToLower());
        if (path != null)
        {
            var lower = path.ToLower();
            int weight;
            lock (blackList)
            {
                if (blackList.TryGetValue(lower, out weight))
                {
                    if (weight > weightDecrease)
                    {
                        if (weightDecrease > 0) blackList[lower] = weight - weightDecrease;
                    }
                    else
                    {
                        blackList.Remove(lower);
                    }
                    return true;
                }
            }
        }
        return false;
    }

    public static void AddBlackListWeight(string path, int weightIncrease = 1)
    {
        if (path != null)
        {
            var lower = path.ToLower();
            int weight;
            lock (blackList)
            {
                if (blackList.TryGetValue(lower, out weight))
                {
                    blackList[lower] = weight + weightIncrease;
                }
                else
                {
                    blackList[lower] = weightIncrease;
                }
            }
        }
    }
    */

        /*
        public static void fileCacheTest()
        {
            MessageBox.Show(GetCommonFolderRoot(@"\\localhost\aa\bc\d"));
        }
        */

        private static bool IsPathRooted(string path)
        {
            return Path.IsPathRooted(path) || path.StartsWith(@"\\");
        }

        private static string RemoveRootedString(string path)
        {
            var result = new StringBuilder(path.Length - 1); // 多くの場合に一文字だけ削られる
            bool start = false;
            foreach (var c in path)
            {
                switch (c)
                {
                    case ':': break;
                    default:
                        if (c == Path.DirectorySeparatorChar)
                        {
                            if(start) result.Append(c);
                        }
                        else
                        {
                            result.Append(c);
                            start = true;
                        }
                        break;
                }
            }
            return result.ToString();
        }

        private static string GetCachePath(string filePath, string cacheRoot)
        {
            if (IsPathRooted(cacheRoot))
            {
                return Path.Combine(cacheRoot, RemoveRootedString(filePath) + CACHE_FILE_SUFFIX);
            }
            else
            {
                cacheRoot = GetFullPath(Path.GetDirectoryName(filePath), cacheRoot);
                return Path.Combine(cacheRoot, Path.GetFileName(filePath) + CACHE_FILE_SUFFIX);
            }
        }

        /*
        private static string GetRelativePath(string current, string target)
        {
            var cDirs = current.Split(Path.DirectorySeparatorChar);
            var tDirs = target.Split(Path.DirectorySeparatorChar);
            var cLen = cDirs.Length;
            var len = Math.Min(cLen, tDirs.Length);
            var i = 0;
            var p = 0;
            while (i < len && cDirs[i] == tDirs[i]) p += tDirs[i++].Length + 1;
            var up = "";
            while (i++ < cLen) up += ".." + Path.DirectorySeparatorChar;
            return up + target.Substring(p);
        }
        */

        private static string GetFullPath(string current, string target)
        {
            if (current.Length > 0 && current.Last() != Path.DirectorySeparatorChar) current += Path.DirectorySeparatorChar;
            return new Uri(new Uri(current), target).LocalPath;
        }

        private static void CreateDirectoryWithItsParents(string target)
        {
            var stack = new Stack<string>();
            while (!Directory.Exists(target))
            {
                stack.Push(target);
                var t2 = Path.GetDirectoryName(target);
                if (string.IsNullOrEmpty(t2) || t2 == target) throw new DirectoryNotFoundException();
                target = t2;
            }
            while (stack.Count > 0) Directory.CreateDirectory(stack.Pop());
        }
        

        private static void DeleteParents(string alreadyDeletedTarget, string keep)
        {
            alreadyDeletedTarget = Path.GetDirectoryName(alreadyDeletedTarget);
            while (!keep.StartsWith(alreadyDeletedTarget))
            {
                if (ContainsAnyEntry(alreadyDeletedTarget)) return;
                Directory.Delete(alreadyDeletedTarget);
                alreadyDeletedTarget = Path.GetDirectoryName(alreadyDeletedTarget);
            }
        }

        private static bool ContainsAnyEntry(string dirPath)
        {
            return Directory.EnumerateFileSystemEntries(dirPath).Any();
        }

        public static bool TryGetWithIgnoredIndex(string cacheRoot, string fileName, out byte[] gpData)
        {
            return TryGet(cacheRoot, fileName, null, out gpData);
        }
        /// <param name="knownLastWriteTime">ZipPla CE: 呼び出し側で元のファイルの更新日時（ローカル時刻）を取ってあれば渡す。ファイルへの問い合わせを 1 回減らす</param>
        public static bool TryGetWithIgnoredIndex(string cacheRoot, string fileName, DateTime? knownLastWriteTime, out byte[] gpData)
        {
            return TryGet(cacheRoot, fileName, null, knownLastWriteTime, out gpData);
        }
        public static bool TryGet(string cacheRoot, string fileName, int? index, out byte[] gpData)
        {
            return TryGet(cacheRoot, fileName, index, null, out gpData);
        }
        public static bool TryGet(string cacheRoot, string fileName, int? index, DateTime? knownLastWriteTime, out byte[] gpData)
        {
            gpData = null;
            if (cacheRoot == AlternateDataStream)
            {
                if (!NTFSMultiStream.Exists(fileName, STREAM_NAME)) return false;
                try
                {
                    var lastWriteTime = knownLastWriteTime ?? File.GetLastWriteTime(fileName);
                    using (var s = new NTFSMultiStream(fileName, STREAM_NAME, FileAccess.Read))
                    using (var r = new BinaryReader(s))
                    {
                        getHeaderData(r, index, lastWriteTime, out gpData);
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                try
                {
                    var cachePath = GetCachePath(fileName, cacheRoot);
                    if (!File.Exists(cachePath)) return false;
                    var lastWriteTime = knownLastWriteTime ?? File.GetLastWriteTime(fileName);
                    using (var s = new FileStream(cachePath, FileMode.Open, FileAccess.Read))
                    using (var r = new BinaryReader(s))
                    {
                        getHeaderData(r, index, lastWriteTime, out gpData);
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        private static void getHeaderData(BinaryReader r, int? index, DateTime lastWriteTime, out byte[] gpData)
        {
            if (r.ReadUInt32() != VERSION) throw new Exception();
            if (r.ReadInt64() != lastWriteTime.Ticks) throw new Exception();
            var indexOfData = r.ReadInt32();
            if (index != null && index != indexOfData) throw new Exception();
            var dataLength = r.ReadInt32();
            if (dataLength < 0 || dataLength > DATA_UBOUND) throw new Exception();

            gpData = r.ReadBytes(dataLength);
            if (gpData.Length != dataLength)
            {
                gpData = null;
                throw new Exception();
            }
        }

        public static bool TryGet(string cacheRoot, string fileName, Size requiredSize, bool letterBox, out Bitmap thumbnail, out byte[] gpData, out FocusCache focusCache)
        {
            return TryGet(cacheRoot, fileName, -1, requiredSize, letterBox, out thumbnail, out gpData, out focusCache);
        }

        /// <summary>focusCache には、ファイルにあった注目点の記録が入る。ファイルがない・合わないときは空（あとで計算した注目点を追記するのに使う）</summary>
        public static bool TryGet(string cacheRoot, string fileName, int index, Size requiredSize, bool letterBox, out Bitmap thumbnail, out byte[] gpData, out FocusCache focusCache)
        {
            focusCache = new FocusCache(cacheRoot, fileName, index);
            return TryGet(cacheRoot, fileName, index, requiredSize, letterBox, out thumbnail, out Fraction dummy, true, out gpData, focusCache);
        }

        public static bool TryGet(string cacheRoot, string fileName, Size requiredSize, bool letterBox, out Bitmap thumbnail, out byte[] gpData)
        {
            return TryGet(cacheRoot, fileName, -1, requiredSize, letterBox, out thumbnail, out gpData);
        }

        public static bool TryGet(string cacheRoot, string fileName, int index, Size requiredSize, bool letterBox, out Bitmap thumbnail, out byte[] gpData)
        {
            Fraction dummy;
            return TryGet(cacheRoot, fileName, index, requiredSize, letterBox, out thumbnail, out dummy, true, out gpData);
        }

        public static bool TryGet(string cacheRoot, string fileName, int index, Size requiredSize, bool letterBox, out Bitmap thumbnail, out Fraction pixelRatio, out byte[] gpData)
        {
            return TryGet(cacheRoot, fileName, index, requiredSize, letterBox, out thumbnail, out pixelRatio, false, out gpData);
        }

        private static bool TryGet(string cacheRoot, string fileName, int index, Size requiredSize, bool letterBox, out Bitmap thumbnail, out Fraction pixelRatio, bool pixelRatioMustBe1, out byte[] gpData)
        {
            return TryGet(cacheRoot, fileName, index, requiredSize, letterBox, out thumbnail, out pixelRatio, pixelRatioMustBe1, out gpData, focusCache: null);
        }

        private static bool TryGet(string cacheRoot, string fileName, int index, Size requiredSize, bool letterBox, out Bitmap thumbnail, out Fraction pixelRatio, bool pixelRatioMustBe1, out byte[] gpData, FocusCache focusCache)
        {
            thumbnail = null;
            gpData = null;
            pixelRatio = 0;

            var ads = cacheRoot == AlternateDataStream;
            var cachePath = ads ? null : GetCachePath(fileName, cacheRoot);
            if (ads && !NTFSMultiStream.Exists(fileName, STREAM_NAME) || !ads && !File.Exists(cachePath))
            {
                return false;
            }
            try
            {
                var lastWriteTime = File.GetLastWriteTime(fileName);
                byte[] all;
                using (var s = ads ? new NTFSMultiStream(fileName, STREAM_NAME, FileAccess.Read) : new FileStream(cachePath, FileMode.Open, FileAccess.Read) as Stream)
                {
                    all = ReadAllBytes(s); // 1 回で読む（シークのたびに読み直さない）
                }
                using (var s = new MemoryStream(all, writable: false))
                using (var r = new BinaryReader(s))
                {
                    long fileSize = all.Length;

                    getHeaderData(r, index, lastWriteTime, out gpData);

                    fileSize -= (4 + 8 + 4 + 4) + gpData.Length;

                    var requiredWidthWithMargin = (int)Math.Round(requiredSize.Width * SIZE_MARGIN);
                    var requiredHeightWithMargin = (int)Math.Round(requiredSize.Height * SIZE_MARGIN);

                    while (fileSize > 0)
                    {
                        var width = r.ReadUInt32();
                        var height = r.ReadUInt32();
                        var numerator = r.ReadInt32();
                        var denominator = r.ReadInt32();
                        var length = (int)r.ReadInt64();
                        if (numerator <= 0 || denominator <= 0 || length <= 0 || pixelRatioMustBe1 && numerator != denominator)
                        {
                            if (thumbnail != null) break; // 画像は取れている。後ろの注目点の記録を読めなかっただけ
                            return false;
                        }
                        if (width == 0 && height == 0 && numerator == FOCUS_MAGIC && denominator == FOCUS_MAGIC)
                        {
                            if (focusCache != null && s.Position + length <= all.Length)
                            {
                                var memo = FocusMemo.Read(all, (int)s.Position, length);
                                if (memo != null) focusCache.Memos.Add(memo);
                            }
                            s.Seek(length, SeekOrigin.Current);
                        }
                        else if (thumbnail == null && (
                            // pixelRatio が 1 でないのは width と height が MaxValue のときに限るので考慮する必要はない
                            // pixelRatio が 1 でないモニタなどを想定して縮小された異方性キャッシュを考える場合は
                            // width と height の一方を減らして実効的な解像度を取得すればよいが、
                            // その際は TrySet で作成されるものが TryGet で受け入れられるようによく検討する必要がある
                            // TrySet に失敗するのは良いが、成功したのに TryGet が受け入れないと無限ループの危険がある
                            letterBox && (width >= requiredWidthWithMargin || height >= requiredHeightWithMargin) ||
                            !letterBox && width >= requiredWidthWithMargin && height >= requiredHeightWithMargin
                            ))
                        {
                            var buf = r.ReadBytes(length);
                            thumbnail = LoadCacheImage(buf); // JPEG（P4）と JPEG XR（従来）の両方を読む
                            pixelRatio = new Fraction(numerator, denominator);
                            if (focusCache == null) break; // 注目点の記録が要らなければ後ろは読まない
                        }
                        else
                        {
                            s.Seek(length, SeekOrigin.Current);
                        }
                        fileSize -= (4 + 4 + 4 + 4 + 8) + length;
                    }
                }
                return thumbnail != null;
            }
            catch
            {
                if (thumbnail != null)
                {
                    thumbnail.Dispose();
                    thumbnail = null;
                }
                return false;
            }
        }

        private static byte[] ReadAllBytes(Stream s)
        {
            var length = (int)s.Length;
            var all = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var n = s.Read(all, offset, length - offset);
                if (n <= 0) break;
                offset += n;
            }
            if (offset != length) Array.Resize(ref all, offset);
            return all;
        }

        // フォルダ方式でサムネイルを作るたびに、以前の ADS 方式で残ったキャッシュを消していた（ストリームの存在確認が毎回 0.1〜0.3ms）。
        // 同じファイルはこのセッションで 1 回だけ確認する（K7 (b)）。ADS 方式で書いたファイルは確認済みから外す
        private static readonly ConcurrentDictionary<string, byte> leftoverAdsChecked = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        public static void TryDeleteLeftoverAlternateDataStream(string fileName)
        {
            if (!leftoverAdsChecked.TryAdd(fileName, 0)) return;
            TryDeleteAlternateDataStream(fileName);
        }

        public static bool TryDeleteAlternateDataStream(string fileName)
        {
            if (!NTFSMultiStream.Exists(fileName, STREAM_NAME)) return false;
            var readOnly = false;
            FileAttributes att = default(FileAttributes);
            try
            {
                return editFile(fileName, func: () => // 代替ストリームの削除は deleted イベントを起こさない
                {
                    att = File.GetAttributes(fileName);
                    readOnly = (att & FileAttributes.ReadOnly) == FileAttributes.ReadOnly; ;
                    if (readOnly)
                    {
                        readOnly = false;
                        File.SetAttributes(fileName, att & ~FileAttributes.ReadOnly);
                        readOnly = true;
                    }
                    return NTFSMultiStream.Delete(fileName, STREAM_NAME);
                });
            }
            catch
            {
                return false;
            }
            finally
            {
                if(readOnly)
                {
                    if (readOnly) File.SetAttributes(fileName, att);
                }
            }
        }

        public static void TryMove(string cacheRoot, string oldPath, string newPath)
        {
            if (cacheRoot == null || cacheRoot == AlternateDataStream) return;
            try
            {
                var cachePathOld = GetCachePath(oldPath, cacheRoot);
                if(File.Exists(cachePathOld))
                {
                    if (newPath == null)
                    {
                        File.Delete(cachePathOld);
                    }
                    else
                    {
                        var cachePathNew = GetCachePath(newPath, cacheRoot);
                        if (File.Exists(cachePathNew)) File.Delete(cachePathNew);
                        File.Move(cachePathOld, cachePathNew);
                    }
                    if (Path.IsPathRooted(cacheRoot) && cachePathOld.StartsWith(cacheRoot))
                    {
                        DeleteParents(cachePathOld, cacheRoot);
                    }
                }
            }
            catch
            {
            }
        }

        public static void TryDelete(string cacheRoot, string path)
        {
            TryMove(cacheRoot, path, null);
        }

        private struct SizeD
        {
            public double Width, Height;

            public SizeD(double width, double height)
            {
                Width = width;
                Height = height;
            }

            public static implicit operator SizeD(Size size)
            {
                return new SizeD(size.Width, size.Height);
            }
            
            public static SizeD operator *(SizeD sizeD, double x)
            {
                return new SizeD(sizeD.Width * x, sizeD.Height * x);
            }

            public static SizeD operator *(double x, SizeD sizeD)
            {
                return new SizeD(sizeD.Width * x, sizeD.Height * x);
            }

            public static SizeD operator /(SizeD sizeD, double x)
            {
                return new SizeD(sizeD.Width / x, sizeD.Height / x);
            }

            public Size Round()
            {
                return new Size((int)Math.Round(Width), (int)Math.Round(Height));
            }

            public bool LessThanOrEqualTo(Size providedSize, bool letterBox)
            {
                if(letterBox)
                {
                    return Width <= providedSize.Width || Height <= providedSize.Height;
                }
                else
                {
                    return Width <= providedSize.Width && Height <= providedSize.Height;
                }
            }
        }

        private static SizeD getEffectiveSize(Size size, Fraction pixelRatio)
        {
            double effectiveWidth, effectiveHeight;
            if (pixelRatio > 1)
            {
                effectiveWidth = size.Width;
                effectiveHeight = size.Height / (double)pixelRatio;
            }
            else
            {
                effectiveWidth = size.Width * (double)pixelRatio;
                effectiveHeight = size.Height;
            }
            return new SizeD(effectiveWidth, effectiveHeight);
        }

        private static readonly char DirectorySeparatorChar = Path.DirectorySeparatorChar;
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private static string GetCommonFolderRoot(string commonFolderPath)
        {
            if (commonFolderPath != null && commonFolderPath.Length >= 5 && commonFolderPath[0] == DirectorySeparatorChar &&
               commonFolderPath[1] == DirectorySeparatorChar && !InvalidFileNameChars.Contains(commonFolderPath[2]))
            {
                var index = 3;
                var count = 0;
                char c = '\0';
                while (index < commonFolderPath.Length)
                {
                    c = commonFolderPath[index];
                    if (c == DirectorySeparatorChar)
                    {
                        count++;
                        if (count == 2)
                        {
                            return commonFolderPath.Substring(0, index);
                        }
                    }
                    else if (InvalidFileNameChars.Contains(c)) return null;
                    index++;
                }
                if (count == 1 && c != DirectorySeparatorChar) return commonFolderPath;
            }
            return null;
        }
        
        private static bool TryTestAdsSave(string path)
        {
            bool result;
            const string testStreamFileName = "testStream";
            if (NTFSMultiStream.Exists(path, testStreamFileName))
            {
                result = true;
            }
            else
            {
                var att = File.GetAttributes(path);
                var readOnly = (att & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
                try
                {
                    if (readOnly)
                    {
                        readOnly = false;
                        File.SetAttributes(path, att & ~FileAttributes.ReadOnly);
                        readOnly = true;
                    }
                    var writeTime = File.GetLastWriteTime(path);
                    var successWrite = false;
                    try
                    {
                        NTFSMultiStream.WriteFile(path, testStreamFileName, new byte[] { });
                        successWrite = true;
                    }
                    catch { }
                    try
                    {
                        if (NTFSMultiStream.Exists(path, testStreamFileName))
                        {
                            NTFSMultiStream.Delete(path, testStreamFileName);
                            result = successWrite;
                        }
                        else
                        {
                            result = false;
                        }
                    }
                    finally
                    {
                        if (writeTime != File.GetLastWriteTime(path))
                            File.SetLastWriteTime(path, writeTime);
                    }
                }
                finally
                {
                    if (readOnly) File.SetAttributes(path, att);
                }
            }
            return result;
        }

        // 複数のスレッド（サムネイルの並列読み込み・後回しの書き込み）から呼ばれるので、2 つの集合は GetDriveFormatString_Lock の中で読み書きする（B4）
        private static readonly object GetDriveFormatString_Lock = new object();
        private static HashSet<string> GetDriveFormatString_NTFSSet = new HashSet<string>();
        private static HashSet<string> GetDriveFormatString_NoNTFSSet = new HashSet<string>();
        private static string GetDriveFormatString(string driveName, bool checkNew = false)
        {
            var commonFolderRoot = GetCommonFolderRoot(driveName);
            if (commonFolderRoot != null)
            {
                lock (GetDriveFormatString_Lock)
                {
                    if (!checkNew)
                    {
                        if (GetDriveFormatString_NTFSSet.Contains(commonFolderRoot))
                        {
                            return "NTFS";
                        }
                        else if (GetDriveFormatString_NoNTFSSet.Contains(commonFolderRoot))
                        {
                            return "UNKNOWN";
                        }
                    }
                    else
                    {
                        GetDriveFormatString_NTFSSet.Remove(commonFolderRoot);
                        GetDriveFormatString_NoNTFSSet.Remove(commonFolderRoot);
                    }
                }

                // commonFolderRoot が ADS を持たないがその子が持つケースが存在する場合
                // \\localhost\D 等はこれに該当しないためコメントアウト
                //var testTarget = commonFolderRoot != driveName ? driveName : (new DirectoryInfo(driveName).EnumerateFileSystemInfos())
                //    .FirstOrDefault(info => (info.Attributes & FileAttributes.System) != FileAttributes.System)?.FullName;
                //if (testTarget == null) return null;

                var testTarget = commonFolderRoot;
                
                // 書き込みの試験はロックの外で行う（同時に試験が走っても結果は同じ）
                var supported = TryTestAdsSave(testTarget);
                lock (GetDriveFormatString_Lock)
                {
                    if (supported)
                    {
                        GetDriveFormatString_NoNTFSSet.Remove(commonFolderRoot);
                        GetDriveFormatString_NTFSSet.Add(commonFolderRoot);
                    }
                    else
                    {
                        GetDriveFormatString_NTFSSet.Remove(commonFolderRoot);
                        GetDriveFormatString_NoNTFSSet.Add(commonFolderRoot);
                    }
                }
                return supported ? "NTFS" : "UNKNOWN";
            }
            return new DriveInfo(driveName).DriveFormat;
        }
        
        /// <summary>
        /// フォルダを開くたびの ADS の判定（K20）。対応していると分かっている共有は試し書きをし直さない
        /// （以前は毎回キャッシュを消して共有のルートに試験用の ADS を書いて消していた）。
        /// 対応していない・まだ調べていない場所は、状況が変わったかもしれないので従来どおり調べ直す
        /// </summary>
        public static long SupportADSSizeRecheckingUnsupported(string driveName)
        {
            var commonFolderRoot = GetCommonFolderRoot(driveName);
            var knownSupported = false;
            if (commonFolderRoot != null)
            {
                lock (GetDriveFormatString_Lock)
                {
                    knownSupported = GetDriveFormatString_NTFSSet.Contains(commonFolderRoot);
                }
            }
            return SupportADSSize(driveName, checkNew: !knownSupported);
        }

        public static long SupportADSSize(string driveName, bool checkNew)
        {
            var driveFormatString = GetDriveFormatString(driveName, checkNew);

            // GetDriveFormatString が取得できないケースは commonFolderRoot が空なのでチェックする必要がない
            //if (driveFormatString == null) return long.MaxValue;

            return SupportADSSizeFromFormatString(driveFormatString);
        }
        private static long SupportADSSizeFromFormatString(string formatString)
        {
            // ReFS ではサポートされないという記述
            // https://blogs.msdn.microsoft.com/b8_ja/2012/01/20/windows-refs/

            // 最新の ReFS ではサポートされるという記述
            // http://yamanxworld.blogspot.jp/2013/10/windows-server-2012-r2-refs.html

            // 公式でもクライアント OS で使う ReFS は代替データストリームに対応しているという記述がある
            // https://msdn.microsoft.com/ja-jp/library/hh831724(v=ws.11).aspx

            // ただし上にもあるが 128KB の制限付き。通常の使い方では概ねこれを超えることはないし超えたからと言ってエラーになるわけではない。
            // http://bakins-bits.com/wordpress/?p=140

            switch (formatString.ToUpper())
            {
                case "NTFS": return long.MaxValue;
                case "REFS": return 128 * 1000; // 余裕を持って 128 * 1024 にはしない
                default: return 0;
            }
        }
        

        //public static readonly ImageCodecInfo TrySet_ImageCodecInfo = GetEncoderInfo(ImageFormat.Jpeg);
        //public static readonly EncoderParameters TrySet_EncoderParameters = GetJpegEncoderParameters(JPEG_QUALITY);
        private static readonly HashSet<string> TrySet_Creating = new HashSet<string>(); // スレッドセーフ
        public static bool TrySet(string cachRoot, string fileName, Size requiredSize, bool letterBox, bool fullSizeCache, Bitmap thumbnail, byte[] gpData)
        {
            return TrySet(cachRoot, fileName, -1, requiredSize, letterBox, fullSizeCache, thumbnail, gpData);
        }
        public static bool TrySet(string cachRoot, string fileName, int index, Size requiredSize, bool letterBox, bool fullSizeCache, Bitmap thumbnail, byte[] gpData)
        {
            Bitmap dummy;
            Fraction dummy2;
            return TrySet(cachRoot, fileName, index, requiredSize, letterBox, fullSizeCache, thumbnail, 1, gpData, getBiggest: false,
                biggestBitmap: out dummy, biggestBitmapPixelRatio: out dummy2);
        }
        public static bool TrySet(string cachRoot, string fileName, int index, Size requiredSize, bool letterBox, bool fullSizeCache, Bitmap thumbnail, byte[] gpData, out Bitmap biggestBitmap)
        {
            Fraction dummy;
            return TrySet(cachRoot, fileName, index, requiredSize, letterBox, fullSizeCache, thumbnail, 1, gpData, getBiggest: true,
                biggestBitmap: out biggestBitmap, biggestBitmapPixelRatio: out dummy);
        }
        public static bool TrySet(string cachRoot, string fileName, int index, Size requiredSize, bool letterBox, bool fullSizeCache, Bitmap thumbnail,
            Fraction pixelRatio, byte[] gpData, out Bitmap biggestBitmap, out Fraction biggestBitmapPixelRatio)
        {
            return TrySet(cachRoot, fileName, index, requiredSize, letterBox, fullSizeCache, thumbnail, pixelRatio, gpData, getBiggest: true,
                biggestBitmap: out biggestBitmap, biggestBitmapPixelRatio: out biggestBitmapPixelRatio);
        }
        private static bool TrySet(string cacheRoot, string fileName, int index, Size requiredSize, bool letterBox, bool fullSizeCache, Bitmap thumbnail,
            Fraction pixelRatio, byte[] gpData, bool getBiggest, out Bitmap biggestBitmap, out Fraction biggestBitmapPixelRatio)
        {
            if (getBiggest)
            {
                // 後回しの書き込みは 1 件ごとに最大サイズの画像の複製を持つ。書き込みが追いつかずにたまったら、ここで書く（Y15。
                // 以前は上限がなく、CPU が埋まった状態で多くのファイルをすばやくスクロールすると溜まり続け、閉じるときも最大 10 秒待った）
                int pendingCount;
                lock (pendingWrites) pendingCount = pendingImageWriteCount; // ZipPla CE: 画像を持つ書き込みだけ数える（K36）
                if (pendingCount >= MaxPendingWrites)
                {
                    return TrySetCore(cacheRoot, fileName, index, requiredSize, letterBox, fullSizeCache, thumbnail, pixelRatio, gpData, getBiggest: true,
                        biggestBitmap: out biggestBitmap, biggestBitmapPixelRatio: out biggestBitmapPixelRatio, pending: null);
                }
                return TrySetDeferred(cacheRoot, fileName, index, requiredSize, letterBox, fullSizeCache, thumbnail, pixelRatio, gpData, out biggestBitmap, out biggestBitmapPixelRatio);
            }
            return TrySetCore(cacheRoot, fileName, index, requiredSize, letterBox, fullSizeCache, thumbnail, pixelRatio, gpData, getBiggest, out biggestBitmap, out biggestBitmapPixelRatio, pending: null);
        }

        // キャッシュの書き込みを後回しにする（K7 (a)）。
        // 表示に使う最大サイズの縮小画像だけをここで作って返し、残りのサイズの縮小・JPEG XR のエンコード・書き込みは
        // 低優先度の 1 本のスレッドで行う。キャッシュの中身は従来と同じ（最大サイズの画像から順に縮小する手順も同じ）。
        // 書き込むまでのあいだは TrySet_Creating に入れておき、同じファイルの TrySet は false を返す。
        private sealed class PendingWrite
        {
            public string CacheRoot, FileName;
            public int Index;
            public Size RequiredSize;
            public bool LetterBox, FullSizeCache;
            public Fraction PixelRatio;
            public byte[] GpData;
            public Size[] ResizedSize;
            public Fraction[] ResizedPixelRatio;
            public bool MaxSizeIsFullSize;
            public Bitmap Biggest; // 最大サイズの縮小画像の複製。書き込み処理が持ち、使い終わったら Dispose する
            public DateTime ExpectedLastWriteTime; // サムネイルを作ったときの元ファイルの更新日時。書くときに変わっていたら書かない
            public FocusMemo[] Focus; // null でなければ、画像は書かずに注目点の記録だけを書き換える（P4）
            public bool SeekPenalty; // ZipPla CE: 元ファイルが HDD（シークに時間のかかるドライブ）にある（K36）。EnqueuePendingWrite で入れる
            public FocusMemo[] AppendFocus; // ZipPla CE: 画像のあとに続けて書く注目点の記録（K37）。同じまとまりの注目点の記録の登録を付けたもの
        }

        private static bool TrySetDeferred(string cacheRoot, string fileName, int index, Size requiredSize, bool letterBox, bool fullSizeCache, Bitmap thumbnail,
            Fraction pixelRatio, byte[] gpData, out Bitmap biggestBitmap, out Fraction biggestBitmapPixelRatio)
        {
            if (pixelRatio == 0) pixelRatio = 1;
            biggestBitmap = null;
            biggestBitmapPixelRatio = 0;

            lock (TrySet_Creating)
            {
                if (TrySet_Creating.Contains(fileName)) return false;
                TrySet_Creating.Add(fileName);
            }
            var queued = false;
            Bitmap biggest = null, copy = null;
            try
            {
                var dataLength = gpData != null ? gpData.Length : 0;
                if (dataLength > DATA_UBOUND) return false;
                if (cacheRoot == AlternateDataStream && SupportADSSize(fileName, checkNew: false) - ((4 + 8 + 4 + 4) + dataLength) <= 0) return false;
                if (!GetResizedInfo(requiredSize, letterBox, thumbnail.Size, pixelRatio, fullSizeCache, out var resizedSize, out var resizedPixelRatio)) return false;
                var maxSizeIndex = resizedSize.Length - 1;
                var expectedLastWriteTime = File.GetLastWriteTime(fileName); // フォルダでも取れる
                var maxSize = resizedSize[maxSizeIndex];
                biggest = BitmapResizer.CreateNew(thumbnail, maxSize.Width, maxSize.Height);
                copy = DeepCopy(biggest);
                EnqueuePendingWrite(new PendingWrite
                {
                    CacheRoot = cacheRoot, FileName = fileName, Index = index, RequiredSize = requiredSize, LetterBox = letterBox, FullSizeCache = fullSizeCache,
                    PixelRatio = pixelRatio, GpData = gpData, ResizedSize = resizedSize, ResizedPixelRatio = resizedPixelRatio,
                    MaxSizeIsFullSize = maxSize == thumbnail.Size, Biggest = copy, ExpectedLastWriteTime = expectedLastWriteTime,
                });
                queued = true;
                biggestBitmap = biggest;
                biggestBitmapPixelRatio = resizedPixelRatio[maxSizeIndex];
                return true;
            }
            catch
            {
                if (!queued)
                {
                    copy?.Dispose();
                    biggest?.Dispose();
                }
                biggestBitmap = null;
                return false;
            }
            finally
            {
                if (!queued)
                {
                    lock (TrySet_Creating) TrySet_Creating.Remove(fileName);
                }
            }
        }

        // 画素をすべて複製する（GDI+ の Clone は元と画素を共有することがあり、別スレッドで使うと危険なため）
        /// <summary>
        /// キャッシュファイルの注目点の記録を書き換える（P4）。ヘッダ（版・元の更新日時・インデックス）が合わなければ何もしない。
        /// 画像のエントリはそのまま写し、注目点の記録だけを入れ替える。ADS の場合は TrySetCore と同じく元ファイルの更新日時を保つ
        /// </summary>
        private static void WriteFocusMemos(PendingWrite job)
        {
            var fileName = job.FileName;
            var cacheRoot = job.CacheRoot;
            lock (TrySet_Creating)
            {
                if (TrySet_Creating.Contains(fileName)) return;
                TrySet_Creating.Add(fileName);
            }
            var readOnly = false;
            var att = default(FileAttributes);
            try
            {
                var ads = cacheRoot == AlternateDataStream;
                var cachePath = ads ? null : GetCachePath(fileName, cacheRoot);
                if (ads ? !NTFSMultiStream.Exists(fileName, STREAM_NAME) : !File.Exists(cachePath)) return;
                // ZipPla CE: 属性・フォルダか・更新日時を 1 回の問い合わせで取る（K37。K34 と同じ）
                var info = new FileInfo(fileName); // フォルダでも属性と更新日時は取れる
                var attributes = info.Attributes;
                if ((int)attributes == -1) return;
                var isFile = (attributes & FileAttributes.Directory) == 0;
                var lastWriteTime = info.LastWriteTime;
                byte[] all;
                using (var s = ads ? new NTFSMultiStream(fileName, STREAM_NAME, FileAccess.Read) : new FileStream(cachePath, FileMode.Open, FileAccess.Read) as Stream)
                {
                    all = ReadAllBytes(s);
                }
                byte[] newBytes;
                using (var s = new MemoryStream(all, writable: false))
                using (var r = new BinaryReader(s))
                using (var o = new MemoryStream())
                using (var w = new BinaryWriter(o))
                {
                    getHeaderData(r, job.Index, lastWriteTime, out var gpData); // 合わなければ例外
                    var headerLength = (int)s.Position;
                    o.Write(all, 0, headerLength);
                    while (s.Position < all.Length)
                    {
                        var start = (int)s.Position;
                        if (all.Length - start < 4 + 4 + 4 + 4 + 8) return;
                        var width = r.ReadUInt32();
                        var height = r.ReadUInt32();
                        var numerator = r.ReadInt32();
                        var denominator = r.ReadInt32();
                        var length = r.ReadInt64();
                        if (length <= 0 || s.Position + length > all.Length) return;
                        var end = (int)(s.Position + length);
                        if (!(width == 0 && height == 0 && numerator == FOCUS_MAGIC && denominator == FOCUS_MAGIC))
                        {
                            o.Write(all, start, end - start);
                        }
                        s.Position = end;
                    }
                    foreach (var memo in job.Focus) memo.WriteEntry(w);
                    w.Flush();
                    newBytes = o.ToArray();
                }

                if (ads)
                {
                    if (SupportADSSize(fileName, checkNew: false) < newBytes.Length) return;
                    att = attributes;
                    if ((att & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(fileName, att & ~FileAttributes.ReadOnly);
                        readOnly = true;
                    }
                    editFile(fileName, () =>
                    {
                        var current = isFile ? File.GetLastWriteTime(fileName) : Directory.GetLastWriteTime(fileName);
                        if (current != lastWriteTime) return; // 元ファイルが変わった
                        try
                        {
                            if (NTFSMultiStream.Exists(fileName, STREAM_NAME))
                            {
                                try { NTFSMultiStream.Delete(fileName, STREAM_NAME); } catch { }
                            }
                            // ZipPla CE: ハンドルで元ファイルの更新日時が変わらないようにして書く（K37。K34 と同じ）
                            NTFSMultiStream.WriteFile(fileName, STREAM_NAME, newBytes, preserveTimes: true);
                            leftoverAdsChecked.TryRemove(fileName, out var dummy);
                        }
                        finally
                        {
                            // ストリームの書き換えで元ファイルの更新日時が変わっていたら戻す（ZipPla CE: 変わっていたときだけ。以前は必ず書き直していた）
                            var now = isFile ? File.GetLastWriteTime(fileName) : Directory.GetLastWriteTime(fileName);
                            if (now != lastWriteTime)
                            {
                                if (isFile) File.SetLastWriteTime(fileName, lastWriteTime);
                                else Directory.SetLastWriteTime(fileName, lastWriteTime);
                            }
                        }
                    });
                }
                else
                {
                    using (var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
                    {
                        fileStream.Write(newBytes, 0, newBytes.Length);
                    }
                }
            }
            catch { }
            finally
            {
                if (readOnly)
                {
                    try { File.SetAttributes(fileName, att); } catch { }
                }
                lock (TrySet_Creating)
                {
                    TrySet_Creating.Remove(fileName);
                }
            }
        }

        private static unsafe Bitmap DeepCopy(Bitmap src)
        {
            var w = src.Width;
            var h = src.Height;
            var pf = src.PixelFormat;
            var dst = new Bitmap(w, h, pf);
            try
            {
                if ((pf & PixelFormat.Indexed) == PixelFormat.Indexed) dst.Palette = src.Palette;
                var rect = new Rectangle(0, 0, w, h);
                var s = src.LockBits(rect, ImageLockMode.ReadOnly, pf);
                try
                {
                    var d = dst.LockBits(rect, ImageLockMode.WriteOnly, pf);
                    try
                    {
                        var rowBytes = (Image.GetPixelFormatSize(pf) * w + 7) / 8;
                        for (var y = 0; y < h; y++)
                        {
                            Buffer.MemoryCopy((byte*)s.Scan0 + (long)y * s.Stride, (byte*)d.Scan0 + (long)y * d.Stride, rowBytes, rowBytes);
                        }
                    }
                    finally
                    {
                        dst.UnlockBits(d);
                    }
                }
                finally
                {
                    src.UnlockBits(s);
                }
                return dst;
            }
            catch
            {
                dst.Dispose();
                throw;
            }
        }

        private static readonly Queue<PendingWrite> pendingWrites = new Queue<PendingWrite>();
        private static int pendingWriteCount = 0; // 順番待ち＋処理中。pendingWrites で lock する
        private static int pendingImageWriteCount = 0; // ZipPla CE: そのうち画像を書くもの（注目点の記録を除く、K36）。pendingWrites で lock する
        // 後回しの書き込みの順番待ちの上限（Y15）。これを超えたら呼び出し側のスレッドで書く
        private const int MaxPendingWrites = 64;
        private static Thread pendingWriteThread = null;
        // ZipPla CE: 最後の登録からこれだけ新しい登録がなければ、溜まった分をまとめて書く（K35）。
        // 見えているサムネイルの読み込み・スクロールの間は書かず、HDD で読み込みと書き込みが交互になってシークが増えるのを防ぐ
        private const int PendingWriteQuietMilliseconds = 1000;
        // これだけ溜まったら待たずに書く（上限 MaxPendingWrites に達して呼び出し側で書くことにならないように）
        private const int PendingWriteBatchLimit = MaxPendingWrites / 2;
        // ZipPla CE: HDD のファイルの書き込みが溜まっているときは、もっと長く待つ（K36）
        private const int PendingWriteQuietMillisecondsOnHdd = 3000;
        // HDD のファイルがあるときは、溜まっても待つ数を増やす（書いている途中の中断も、合わせてこの数まではする）。
        // MaxPendingWrites に達すると呼び出し側で書くことになるので、少し余裕を残す
        private const int PendingWriteBatchLimitOnHdd = MaxPendingWrites * 3 / 4;
        private static int pendingWriteLastEnqueueTick;
        private static long pendingWriteEnqueueSerial; // 登録のたびに増やす（書いている途中に新しい登録があったかを見る、K36）。pendingWrites で lock する
        private static int pendingWriteFlushRequests; // FlushPendingWrites が待っている数。0 でなければ待たずに書く。pendingWrites で lock する

        private static void EnqueuePendingWrite(PendingWrite job)
        {
            try { job.SeekPenalty = StorageInfo.HasSeekPenalty(job.FileName) == true; } catch { } // ドライブごとに 1 回だけ調べる
            lock (pendingWrites)
            {
                pendingWrites.Enqueue(job);
                pendingWriteCount++;
                if (job.Focus == null) pendingImageWriteCount++;
                pendingWriteLastEnqueueTick = Environment.TickCount;
                pendingWriteEnqueueSerial++;
#if PERFLOG
                PerfTrace.Write($"cacheWrite enqueue {Path.GetFileName(job.FileName)}{(job.Focus != null ? " (focus)" : "")}");
#endif
                if (pendingWriteThread == null)
                {
                    pendingWriteThread = new Thread(PendingWriteLoop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "ThumbnailCacheWriter" };
                    pendingWriteThread.Start();
                }
                Monitor.PulseAll(pendingWrites);
            }
        }

        private static void PendingWriteLoop()
        {
            while (true)
            {
                PendingWrite[] batch;
                bool interruptible; // 新しい登録が来たら HDD のファイルの書き込みを中断してよいか（K36）
                long batchSerial;
                lock (pendingWrites)
                {
                    while (pendingWrites.Count == 0) Monitor.Wait(pendingWrites);
                    // ZipPla CE: 新しい登録が止まるまで待ってから、溜まった分をまとめて書く（K35）。HDD のファイルがあれば長く待つ（K36）
                    interruptible = true;
                    while (true)
                    {
                        var hdd = pendingWrites.Any(j => j.SeekPenalty);
                        if (pendingWriteFlushRequests != 0 || countImageWrites(pendingWrites) >= (hdd ? PendingWriteBatchLimitOnHdd : PendingWriteBatchLimit))
                        {
                            interruptible = false; // 閉じるとき・溜まりすぎたときは中断せずに書ききる
                            break;
                        }
                        var quiet = hdd ? PendingWriteQuietMillisecondsOnHdd : PendingWriteQuietMilliseconds;
                        var rest = quiet - unchecked(Environment.TickCount - pendingWriteLastEnqueueTick);
                        if (rest <= 0 || rest > quiet) break;
                        Monitor.Wait(pendingWrites, rest);
                    }
                    // パスの順に書く（同じフォルダのファイルが続く）。OrderBy は安定なので、同じファイルの書き込みと注目点の記録の順番は保たれる
                    batch = mergeFocusIntoImageWrites(dropSupersededFocusWrites(pendingWrites.ToArray())).OrderBy(j => j.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
                    pendingWrites.Clear();
                    batchSerial = pendingWriteEnqueueSerial;
                }
#if PERFLOG
                PerfTrace.Write($"cacheWrite batch n={batch.Length} interruptible={interruptible}");
#endif
                for (var k = 0; k < batch.Length; k++)
                {
                    var job = batch[k];
                    if (interruptible && job.SeekPenalty && requeueIfResumed(batch, k, batchSerial))
                    {
#if PERFLOG
                        PerfTrace.Write($"cacheWrite requeue rest={batch.Length - k}");
#endif
                        break;
                    }
#if PERFLOG
                    // 計測用: 環境変数 ZIPPLA_CACHE_WRITE_DELAY_MS で 1 件ごとの書き込みを遅くする（SSD で HDD の遅さをまねて中断を確かめる）
                    if (int.TryParse(Environment.GetEnvironmentVariable("ZIPPLA_CACHE_WRITE_DELAY_MS"), out var perfDelay) && perfDelay > 0) Thread.Sleep(perfDelay);
#endif
                    try
                    {
                        if (job.Focus != null)
                        {
                            WriteFocusMemos(job);
                        }
                        else
                        {
                            TrySetCore(job.CacheRoot, job.FileName, job.Index, job.RequiredSize, job.LetterBox, job.FullSizeCache, null, job.PixelRatio, job.GpData,
                                getBiggest: false, biggestBitmap: out var dummy, biggestBitmapPixelRatio: out var dummy2, pending: job);
                        }
                    }
                    catch { }
                    finally
                    {
#if PERFLOG
                        PerfTrace.Write($"cacheWrite done {Path.GetFileName(job.FileName)}{(job.Focus != null ? " (focus)" : job.AppendFocus != null ? " (+focus)" : "")}");
#endif
                        job.Biggest?.Dispose();
                        job.Biggest = null;
                        lock (pendingWrites)
                        {
                            pendingWriteCount--;
                            if (job.Focus == null) pendingImageWriteCount--;
                            Monitor.PulseAll(pendingWrites);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// ZipPla CE: 画像を書く登録の数（注目点の記録（P4）は画像を持たず小さいので数えない）。
        /// 表示するたびに画像の書き込みと注目点の記録の 2 つが登録されるので、まとめて数えると半分の件数で上限に達していた（K36）
        /// </summary>
        private static int countImageWrites(IEnumerable<PendingWrite> jobs)
        {
            var n = 0;
            foreach (var j in jobs) if (j.Focus == null) n++;
            return n;
        }

        /// <summary>
        /// ZipPla CE: 同じキャッシュの注目点の記録が複数あれば、最後のものだけを残す（記録は全部を書き直し、
        /// 登録のたびにそれまでの記録をすべて含むので、最後の 1 回で足りる。K36）。pendingWrites の lock の中で呼ぶ
        /// </summary>
        private static List<PendingWrite> dropSupersededFocusWrites(PendingWrite[] jobs)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keep = new bool[jobs.Length];
            for (var i = jobs.Length - 1; i >= 0; i--)
            {
                var j = jobs[i];
                keep[i] = j.Focus == null || seen.Add(j.CacheRoot + "\n" + j.FileName + "\n" + j.Index);
            }
            var result = new List<PendingWrite>(jobs.Length);
            for (var i = 0; i < jobs.Length; i++)
            {
                if (keep[i]) result.Add(jobs[i]);
                else pendingWriteCount--; // 書かずに済ませた分
            }
            if (result.Count != jobs.Length) Monitor.PulseAll(pendingWrites);
            return result;
        }

        /// <summary>
        /// ZipPla CE: 注目点の記録の登録の前に、同じキャッシュの画像の書き込みがあれば、記録をその書き込みに付けて（AppendFocus）
        /// 注目点の記録の登録は書かずに済ませる（K37。初めて表示したファイルのキャッシュを 2 回書かない）。
        /// dropSupersededFocusWrites のあとに、pendingWrites の lock の中で呼ぶ
        /// </summary>
        private static List<PendingWrite> mergeFocusIntoImageWrites(List<PendingWrite> jobs)
        {
            var imageWrites = new Dictionary<string, PendingWrite>(StringComparer.OrdinalIgnoreCase);
            var result = new List<PendingWrite>(jobs.Count);
            foreach (var j in jobs)
            {
                var key = j.CacheRoot + "\n" + j.FileName + "\n" + j.Index;
                if (j.Focus == null)
                {
                    imageWrites[key] = j;
                    result.Add(j);
                }
                else if (imageWrites.TryGetValue(key, out var image))
                {
                    image.AppendFocus = j.Focus; // 記録は登録のたびにそれまでの分をすべて含むので、最後のもので置き換えてよい
                    pendingWriteCount--; // 書かずに済ませた分
                    Monitor.PulseAll(pendingWrites);
                }
                else
                {
                    result.Add(j);
                }
            }
            return result;
        }

        /// <summary>
        /// ZipPla CE: まとめて書いている途中に新しい登録があった（表示・スクロールが再開した）なら、batch の k 番目からの残りを
        /// 順番待ちの先頭に戻して true を返す（HDD で、読み込みと書き込みが交互になってシークが増えるのを防ぐ、K36）。
        /// 閉じるとき・戻すと溜まりすぎるときは戻さない
        /// </summary>
        private static bool requeueIfResumed(PendingWrite[] batch, int k, long batchSerial)
        {
            lock (pendingWrites)
            {
                if (pendingWriteEnqueueSerial == batchSerial || pendingWriteFlushRequests != 0) return false;
                if (countImageWrites(pendingWrites) + countImageWrites(batch.Skip(k)) >= PendingWriteBatchLimitOnHdd) return false; // 戻すとすぐに書くことになる
                var newer = pendingWrites.ToArray();
                pendingWrites.Clear();
                for (var i = k; i < batch.Length; i++) pendingWrites.Enqueue(batch[i]); // 先に登録された分を前に（同じファイルの順番を保つ）
                foreach (var job in newer) pendingWrites.Enqueue(job);
                return true;
            }
        }

        /// <summary>
        /// 後回しにしたキャッシュの書き込みが終わるまで待つ（カタログを閉じるとき）。終わったら true
        /// </summary>
        public static bool FlushPendingWrites(int millisecondsTimeout)
        {
            var limit = Environment.TickCount + millisecondsTimeout;
            lock (pendingWrites)
            {
                // ZipPla CE: 書き込みのスレッドが新しい登録を待っていれば、待たずに書かせる（K35）
                pendingWriteFlushRequests++;
                Monitor.PulseAll(pendingWrites);
                try
                {
                    while (pendingWriteCount > 0)
                    {
                        var rest = limit - Environment.TickCount;
                        if (rest <= 0) return false;
                        Monitor.Wait(pendingWrites, rest);
                    }
                    return true;
                }
                finally
                {
                    pendingWriteFlushRequests--;
                }
            }
        }

        private static bool TrySetCore(string cacheRoot, string fileName, int index, Size requiredSize, bool letterBox, bool fullSizeCache, Bitmap thumbnail,
            Fraction pixelRatio, byte[] gpData, bool getBiggest, out Bitmap biggestBitmap, out Fraction biggestBitmapPixelRatio, PendingWrite pending)
        {
            if (pixelRatio == 0) pixelRatio = 1;

            biggestBitmap = null;
            biggestBitmapPixelRatio = 0;
            //MemoryStream memoryStream = null;


            if (pending == null) // 後回しの書き込みでは TrySetDeferred で登録済み
            {
                lock (TrySet_Creating) // 一番最初に
                {
                    if (TrySet_Creating.Contains(fileName)) return false;
                    TrySet_Creating.Add(fileName);
                }
            }
            var readOnly = false;
            FileAttributes att = default(FileAttributes);
            try
            {
                var dataLength = gpData != null ? gpData.Length : 0;
                if (dataLength > DATA_UBOUND) return false;

                var ads = cacheRoot == AlternateDataStream;

                //var driveInfo = new DriveInfo(fileName);
                long sizeBound;
                if (ads)
                {
                    sizeBound = SupportADSSize(fileName, checkNew: false) - ((4 + 8 + 4 + 4) + dataLength);
                    if (sizeBound <= 0) return false;
                }
                else
                {
                    sizeBound = long.MaxValue;
                }
                
                Size[] resizedSize;
                Fraction[] resizedPixelRatio;
                if (pending != null)
                {
                    resizedSize = pending.ResizedSize;
                    resizedPixelRatio = pending.ResizedPixelRatio;
                }
                else if (!GetResizedInfo(requiredSize, letterBox, thumbnail.Size, pixelRatio, fullSizeCache, out resizedSize, out resizedPixelRatio)) return false;
                
                var maxSizeIndex = resizedSize.Length - 1;

                var maxSizeIsFullSize = pending != null ? pending.MaxSizeIsFullSize : resizedSize[maxSizeIndex] == thumbnail.Size;

                
                MemoryStream[] compressed = new MemoryStream[maxSizeIndex + 1];
                try
                {

                    bool isFile;
                    DateTime lastWriteTime0 = default(DateTime);
                    if (ads)
                    {
                        // ZipPla CE: 属性・フォルダか・更新日時を 1 回の問い合わせで取る（K34）。
                        // 以前は GetAttributes・Exists・GetLastWriteTime を別々に問い合わせたうえ、書けるかを確かめるために
                        // 更新日時を同じ値で書き直していた（メタデータの書き込みとフォルダ監視の Changed が 1 回ずつ増え、HDD ではシークが起きる）。
                        // 書けないファイルは ADS を開くところで例外になり、従来どおり false を返す
                        var info = new FileInfo(fileName); // フォルダでも属性と更新日時は取れる
                        att = info.Attributes;
                        if ((int)att == -1) throw new FileNotFoundException(null, fileName);
                        isFile = (att & FileAttributes.Directory) == 0;
                        lastWriteTime0 = info.LastWriteTime;
                        readOnly = (att & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
                        if (readOnly)
                        {
                            readOnly = false;
                            File.SetAttributes(fileName, att & ~FileAttributes.ReadOnly);
                            readOnly = true;
                        }
                    }
                    else
                    {
                        isFile = File.Exists(fileName);
                    }

                    // 大きい方から順番に縮小していく
                    // 最初の一回以外の縮小のコストが大幅に削減される
                    // 量子化は統計上公比 √(1/4) の等比級数になるので問題にならない
                    // 周波数歪みも画像の公比と縮小アルゴリズムの周波数特性から問題にならない
                    var prevBitmap = thumbnail;
                    var dontDisposePrev = true;
                    for (var i = maxSizeIndex; i >= 0; i--)
                    {
                        sizeBound -= 4 + 4 + 4 + 4 + 8;
                        if (sizeBound <= 0)
                        {
                            if (!dontDisposePrev) prevBitmap.Dispose();
                            throw new Exception(); // compressed や biggestBitmap の Dispose などが必要なので
                        }
                        var resizedSizei = resizedSize[i];
                        var width = resizedSizei.Width;
                        var height = resizedSizei.Height;

                        Bitmap img;
                        if (pending != null && i == maxSizeIndex)
                        {
                            // 最大サイズは TrySetDeferred で作った画像の複製を使う（ここで所有を引き取る）
                            img = pending.Biggest;
                            pending.Biggest = null;
                        }
                        else
                        {
                            img = BitmapResizer.CreateNew(prevBitmap, width, height);
                        }
                        try
                        {
                            var m = new MemoryStream();
                            //img.Save(m, TrySet_ImageCodecInfo, TrySet_EncoderParameters);
                            SaveCacheImage(m, img);

                            if (sizeBound < long.MaxValue / 2)
                            {
                                sizeBound -= m.Position;
                                if (sizeBound < 0) // これで終わりの可能性があるので等号は含めない
                                {
                                    throw new Exception();
                                }
                            }
                            compressed[i] = m;
                        }
                        catch
                        {
                            img.Dispose();
                            if (!dontDisposePrev) prevBitmap.Dispose();
                            throw;
                        }

                        /*
                        var img = new Bitmap(width, height);
                        try
                        {
                            using (var g = Graphics.FromImage(img))
                            {
                                g.DrawImage(prevBitmap, 0, 0, width, height);
                            }
                            var m = new MemoryStream();
                            //img.Save(m, TrySet_ImageCodecInfo, TrySet_EncoderParameters);
                            WindowsMediaPhoto.Save(m, img, JXR_QUALITY);

                            if (sizeBound < long.MaxValue / 2)
                            {
                                sizeBound -= m.Position;
                                if (sizeBound < 0) // これで終わりの可能性があるので等号は含めない
                                {
                                    throw new Exception();
                                }
                            }

                            compressed[i] = m;

                        }
                        catch
                        {
                            img.Dispose();
                            if (!dontDisposePrev) prevBitmap.Dispose();
                            throw;
                        }
                        */


                        if (!dontDisposePrev) prevBitmap.Dispose();

                        if(getBiggest && i == maxSizeIndex)
                        {
                            biggestBitmap = img;
                            biggestBitmapPixelRatio = resizedPixelRatio[i];
                            prevBitmap = img;
                            dontDisposePrev = true;
                        }
                        else
                        {
                            prevBitmap = img;
                            dontDisposePrev = false;
                        }
                    }
                    if (!dontDisposePrev) prevBitmap.Dispose();
                    
                    using (var memoryStream = new MemoryStream())
                    {
                        using (var w = new BinaryWriter(memoryStream))
                        {
                            w.Write(VERSION);

                            // できるだけ直前の更新日時を取得
                            DateTime lastWriteTime;
                            if (isFile) lastWriteTime = File.GetLastWriteTime(fileName);
                            else lastWriteTime = Directory.GetLastWriteTime(fileName);
                            if (ads)
                            {
                                if (lastWriteTime != lastWriteTime0) throw new Exception(); // biggestBitmap などの処理があるので例外を投げる
                            }
                            // 後回しの書き込みでは、サムネイルを作ったあとに元ファイルが変わっていたら書かない（古い画像に新しい日時を付けてしまうため）
                            if (pending != null && lastWriteTime != pending.ExpectedLastWriteTime) throw new Exception();
                            //var fileInfo = new FileInfo(fileName);
                            //var lastWriteTime = fileInfo.LastWriteTime;
                            //fileInfo = null;

                            w.Write(lastWriteTime.Ticks);
                            w.Write(index);

                            if (gpData == null || gpData.Length == 0)
                            {
                                w.Write(0);
                            }
                            else
                            {
                                w.Write(gpData.Length);
                                w.Write(gpData);
                            }

                            for (var i = 0; i <= maxSizeIndex; i++)
                            {
                                if (!maxSizeIsFullSize || i < maxSizeIndex)
                                {
                                    var resizedSizei = resizedSize[i];
                                    w.Write((uint)resizedSizei.Width);
                                    w.Write((uint)resizedSizei.Height);
                                }
                                else
                                {
                                    w.Write(uint.MaxValue);
                                    w.Write(uint.MaxValue);
                                }

                                var resizedPixelRatioi = resizedPixelRatio[i];
                                w.Write((uint)resizedPixelRatioi.Numerator);
                                w.Write((uint)resizedPixelRatioi.Denominator);

                                var m = compressed[i];
                                w.Write(m.Position);
                                m.Seek(0, SeekOrigin.Begin);
                                m.WriteTo(memoryStream);
                                m.Dispose();
                                compressed[i] = null;
                            }

                            // ZipPla CE: 同じまとまりで登録された注目点の記録を続けて書く（K37。WriteFocusMemos が作るのと同じ並び）。
                            // 以前は画像を書いたあと、WriteFocusMemos がキャッシュを読み直して全体を書き直していた
                            var appendFocus = pending?.AppendFocus;
                            if (appendFocus != null && sizeBound - (long)appendFocus.Length * (4 + 4 + 4 + 4 + 8 + FOCUS_PAYLOAD_LENGTH) > 0)
                            {
                                foreach (var memo in appendFocus) memo.WriteEntry(w);
                                w.Flush();
                            }

                            memoryStream.Seek(0, SeekOrigin.Begin);

                            StreamWriteResult result = StreamWriteResult.Success;

                            if (ads)
                            {
                                // ZipPla CE: 以前はここでも書けるかを確かめるために更新日時を同じ値で書き直していた（K34 でやめた）

                                // 既に存在する場合、そのまま上書きすると Changed イベントが１回多く発生する
                                // 削除ではイベントは何も発生しない
                                if (NTFSMultiStream.Exists(fileName, STREAM_NAME))
                                {
                                    try
                                    {
                                        NTFSMultiStream.Delete(fileName, STREAM_NAME);
                                    }
                                    catch { }
                                }

                                editFile(fileName, 
                                action: () =>
                                {
                                    try
                                    {
                                        var buf = new byte[memoryStream.Length];
                                        memoryStream.Read(buf, 0, buf.Length);
                                        // ZipPla CE: ADS のハンドルで元ファイルの更新日時が変わらないようにして書く（K34）
                                        result = NTFSMultiStream.WriteFile(fileName, STREAM_NAME, buf, preserveTimes: true);
                                        leftoverAdsChecked.TryRemove(fileName, out var dummy);
                                    }
                                    finally
                                    {
                                        // 読み込み専用属性を治す前に更新日時を変更するために editFile の中で。
                                        // ZipPla CE: 更新日時が変わっていたときだけ戻す（K34。SMB など、ハンドルでの指定が効かない場合の保険）
                                        if (result != StreamWriteResult.NotEdited)
                                        {
                                            var current = isFile ? File.GetLastWriteTime(fileName) : Directory.GetLastWriteTime(fileName);
                                            if (current != lastWriteTime)
                                            {
                                                if (isFile)
                                                {
                                                    File.SetLastWriteTime(fileName, lastWriteTime);
                                                }
                                                else
                                                {
                                                    Directory.SetLastWriteTime(fileName, lastWriteTime);
                                                }
                                            }
                                        }
                                    }

                                });
                            }
                            else // ファイルに保存
                            {
                                var cachePath = GetCachePath(fileName, cacheRoot);
                                //DeleteItemAndParents(cachePath, false, cachePath);
                                CreateDirectoryWithItsParents(Path.GetDirectoryName(cachePath));
                                using (var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
                                {
                                    memoryStream.CopyTo(fileStream);
                                }
                            }
                            return true;
                        }
                    }
                }
                finally
                {
                    for (var i = 0; i <= maxSizeIndex; i++)
                    {
                        var c = compressed[i];
                        if (c != null) c.Dispose();
                    }
                }

            }
            catch
            {
                if (biggestBitmap != null)
                {
                    biggestBitmap.Dispose();
                    biggestBitmap = null;
                }
                return false;
            }
            finally
            {
                if(readOnly)
                {
                    File.SetAttributes(fileName, att);
                }
                lock (TrySet_Creating)
                {
                    TrySet_Creating.Remove(fileName);
                }
            }
        }

        /*
        static bool fileIsLocked(string fileName)
        {
            try
            {
                File.SetLastWriteTime(fileName, File.GetLastWriteTime(fileName));
                return false;
            }
            catch
            {
                return true;
            }
        }

        static bool directoryIsLocked(string directoryName)
        {
            try
            {
                Directory.SetLastWriteTime(directoryName, Directory.GetLastWriteTime(directoryName));
                return false;
            }
            catch
            {
                return true;
            }
        }
        */


        private static bool GetResizedInfo(Size requiredSize, bool letterBox, Size originalSize, Fraction pixelRatio, bool cacheFullSize,
            out Size[] resizedSize, out Fraction[] resizedPixelRatio)
        {
            resizedSize = null;
            resizedPixelRatio = null;

            var aspect = (Fraction)originalSize * pixelRatio;

            // クリティカルな方が厳密に元の値 * SIZE_MARGIN になるように
            SizeD resizedMaxSize;
            if (letterBox ^ (Fraction)requiredSize > aspect) // 幅フィット
            {
                resizedMaxSize = new SizeD(requiredSize.Width, (double)(requiredSize.Width / aspect)) * SIZE_MARGIN;
            }
            else
            {
                resizedMaxSize = new SizeD((double)(requiredSize.Height * aspect), requiredSize.Height) * SIZE_MARGIN;
            }
            var resizedMaxSizeInt = resizedMaxSize.Round();

            /*
            var wZoom = (double)requiredSize.Width / aspect.Numerator;
            var hZoom = (double)requiredSize.Height / aspect.Denominator;
            var zoom = letterBox ? Math.Min(wZoom, hZoom) : Math.Max(wZoom, hZoom);
            var resizedMaxSize = new SizeD(aspect.Numerator, aspect.Denominator) * zoom;
            */

            // 容量節約のため、必要でない限りオリジナルに近いサイズは作らない
            if (!cacheFullSize && (
                resizedMaxSizeInt.Width * COMMON_RATIO > originalSize.Width ||
                resizedMaxSizeInt.Height * COMMON_RATIO > originalSize.Height))
            {
                return false;
            }

            var resizedSizeList = new List<Size>();
            var resizedPixelRatioList = new List<Fraction>();

            // 容量節約のため、オリジナルに近いサイズにでも縮小する
            // 容量節約が目的であり、オリジナルに近いサイズを作らないこととの整合性は重要でない
            if (resizedMaxSizeInt.Width > originalSize.Width || resizedMaxSizeInt.Height > originalSize.Height)
            {
                resizedSizeList.Add(originalSize);
                resizedPixelRatioList.Add(pixelRatio);

                var wZoom = (double)originalSize.Width / aspect.Numerator;
                var hZoom = (double)originalSize.Height / aspect.Denominator;
                var zoom = Math.Min(wZoom, hZoom);
                resizedMaxSize = new SizeD(aspect.Numerator, aspect.Denominator) * (zoom / COMMON_RATIO);
                resizedMaxSizeInt = resizedMaxSize.Round();
            }

            while (resizedMaxSizeInt.Width * resizedMaxSizeInt.Height >= AREA_UBOUND)
            {
                resizedSizeList.Add(resizedMaxSizeInt);
                resizedPixelRatioList.Add(1);
                resizedMaxSize /= COMMON_RATIO;
                resizedMaxSizeInt = resizedMaxSize.Round();
            }


            if (resizedSizeList.Count == 0)
            {
                if (originalSize.Width * originalSize.Height >= AREA_UBOUND)
                {
                    // オリジナルが大きく要求サイズが小さい場合は必ずキャッシュが必要
                    var z2 = Math.Sqrt(AREA_UBOUND / (resizedMaxSize.Width * resizedMaxSize.Height));
                    resizedMaxSizeInt = new Size((int)Math.Ceiling(resizedMaxSize.Width * z2), (int)Math.Ceiling(resizedMaxSize.Height * z2));

                    if (resizedMaxSizeInt.Width > originalSize.Width || resizedMaxSizeInt.Height > originalSize.Height)
                    {
                        // オリジナルサイズよりも大きいサイズは返さないことを保証
                        //（アスペクト比の違いから一方の辺の長さの逆転はありうる）
                        resizedSizeList.Add(originalSize);
                        resizedPixelRatioList.Add(pixelRatio);
                    }
                    else
                    {
                        resizedSizeList.Add(resizedMaxSizeInt);
                        resizedPixelRatioList.Add(1);
                    }
                }
                else
                {
                    // オリジナルも小さい場合、そのキャッシュが必要かどうかは cacheFullSize に依存
                    if (cacheFullSize)
                    {
                        resizedSizeList.Add(originalSize);
                        resizedPixelRatioList.Add(pixelRatio);
                    }
                    else return false;
                }
            }
            resizedSizeList.Reverse();
            resizedSize = resizedSizeList.ToArray();
            resizedPixelRatioList.Reverse();
            resizedPixelRatio = resizedPixelRatioList.ToArray();
            return true;
        }

        /*
        private static Size[] GetSizeArray(Size requiredSize, Size originalSize)
        {
            var reversedResult = new List<Size>();
            var zoom = 1;
            while (true)
            {
                var size = new Size(requiredSize.Width / zoom, requiredSize.Height / zoom);
                if (size.Width * size.Height < AREA_UBOUND)
                {
                    if (zoom == 1)
                    {
                        var m = originalSize.Width * originalSize.Height;
                        if (m < AREA_UBOUND)
                        {
                            reversedResult.Add(originalSize);
                        }
                        else
                        {
                            var z2 = Math.Sqrt((double)AREA_UBOUND / m);
                            reversedResult.Add(new Size((int)Math.Ceiling(originalSize.Width * z2), (int)Math.Ceiling(originalSize.Height * z2)));
                        }
                    }
                    break;
                }
                reversedResult.Add(size);
                zoom *= COMMON_RATIO;
            }
            var result = new Size[reversedResult.Count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = reversedResult[result.Length - i - 1];
            }
            return result;
        }
        */

        /*
        private static EncoderParameters GetJpegEncoderParameters(long quality)
        {
            var eps = new EncoderParameters(1);
            var ep = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            eps.Param[0] = ep;
            return eps;
        }

        private static ImageCodecInfo GetEncoderInfo(ImageFormat f)
        {
            return ImageCodecInfo.GetImageEncoders().FirstOrDefault(enc => enc.FormatID == f.Guid);
        }
        */

        private static void editFile(string fileName, Action action)
        {
            editFile<object>(fileName, () => { action(); return null; });
        }

        private static T editFile<T>(string fileName, Func<T> func)
        {
            EnterEditing(fileName);
            try { return func(); } finally { ExitedEditing(fileName); }
            /*
            var fileNameLowerCase = fileName.ToLower();
            //blackList.Add(fileNameLowerCase);
            
            if (preAction != null)
            {
                if(preBlackListWeight != null)
                {
                    var preBlackListWeightInt = (int)preBlackListWeight;
                    lock (blackList)
                    {
                        blackList[fileNameLowerCase] = preBlackListWeightInt;
                    }
                }
                preAction();
            }

            if (blackListWeight > 0)
            {
                lock (blackList)
                {
                    if (preAction == null || !blackList.ContainsKey(fileNameLowerCase))
                    {
                        blackList[fileNameLowerCase] = blackListWeight;
                    }
                    else
                    {
                        blackList[fileNameLowerCase] += blackListWeight;
                    }
                }
            }

            return func();
            */
        }
    }
    
    public enum StreamWriteResult { Success, NotEdited, FaildInEditing }

    public class NTFSMultiStream : Stream
    {
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        private SafeFileHandle safeFileHandle;
        private IntPtr fileHandle;
        private FileStream fileStream;

        public NTFSMultiStream(string fileName, string streamName, FileAccess access)
        {
            fileHandle = GetFileHandle(fileName, streamName, access);
            safeFileHandle = new SafeFileHandle(fileHandle, ownsHandle: true);
            fileStream = new FileStream(safeFileHandle, access);
        }
        
        private static IntPtr GetFileHandle(string fileName, string streamName, FileAccess access)
        {
            var fullName = fileName + ":" + streamName;
            IntPtr result;

            switch (access)
            {
                case FileAccess.Write:
                    result = CreateFile(fullName,
                                DesiredAccess.GENERIC_WRITE,
                                ShareMode.FILE_SHARE_NONE, 0,
                                CreationDisposition.CREATE_ALWAYS,
                                FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                IntPtr.Zero);
                    break;
                case FileAccess.Read:
                    result = CreateFile(fullName,
                                DesiredAccess.GENERIC_READ,
                                ShareMode.FILE_SHARE_READ, 0,
                                CreationDisposition.OPEN_EXISTING,
                                FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                IntPtr.Zero);
                    break;
                default: throw new NotSupportedException();
            }
            if (result == INVALID_HANDLE_VALUE) throw new IOException();
            return result;
        }

        public override bool CanRead
        {
            get
            {
                return fileStream.CanRead;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return fileStream.CanSeek;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return fileStream.CanWrite;
            }
        }

        public override long Length
        {
            get
            {
                return fileStream.Length;
            }
        }

        public override long Position
        {
            get
            {
                return fileStream.Position;
            }

            set
            {
                fileStream.Position = value;
            }
        }
        
        /*
        public static bool IsNTFS(string fileName)
        {
            try
            {
                var info = new DriveInfo(fileName);
                return info.DriveFormat.ToLower() == "ntfs";
            }
            catch
            {
                return false;
            }
        }
        */
        
        public static bool Exists(string fileName, string streamName)
        {
            var fullName = fileName + ":" + streamName;
            try
            {
                var fileHandle = CreateFile(fullName,
                                    DesiredAccess.GENERIC_EXISTENCE,
                                    ShareMode.FILE_SHARE_READ, 0,
                                    CreationDisposition.OPEN_EXISTING,
                                    FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                    IntPtr.Zero);
                if(fileHandle == INVALID_HANDLE_VALUE)
                {
                    return false;
                }
                else
                {
                    (new SafeFileHandle(fileHandle, ownsHandle: true)).Dispose();
                    //CloseHandle(fileHandle);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool Delete(string fileName, string streamName)
        {
            var fullName = fileName + ":" + streamName;
            return DeleteFile(fullName);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (fileStream != null)
                {
                    try
                    {
                        fileStream.Dispose();
                    }
                    finally
                    {
                        fileStream = null;
                        safeFileHandle.Dispose();
                        //CloseHandle(fileHandle); // Dispose と CloseHandle の両方を行ってはいけない
                    }
                }
            }
            base.Dispose(disposing);
        }

        public static StreamWriteResult WriteFile(string fileName,string streamName, byte[] buff) => WriteFile(fileName, streamName, buff, preserveTimes: false);

        /// <param name="preserveTimes">ZipPla CE: true なら、このハンドルでの操作で元ファイルの更新日時・最終アクセス日時が変わらないようにする（K34）。
        /// SetFileTime に 0xFFFFFFFF を渡すと、そのハンドルでの以後の操作でファイルシステムが日時を更新しなくなる</param>
        public static StreamWriteResult WriteFile(string fileName, string streamName, byte[] buff, bool preserveTimes)
        {
            var fileHandle = GetFileHandle(fileName, streamName, FileAccess.Write);
            if (fileHandle == INVALID_HANDLE_VALUE) return StreamWriteResult.NotEdited;
            try
            {
                if (preserveTimes)
                {
                    long keep = -1; // dwLowDateTime・dwHighDateTime とも 0xFFFFFFFF
                    long keep2 = -1;
                    SetFileTime(fileHandle, IntPtr.Zero, ref keep, ref keep2); // 失敗しても書き込みは続ける（呼び出し側が日時を確かめて戻す）
                }
                uint written = 0;
                var result = WriteFile(fileHandle, buff, (uint)buff.Length, ref written, IntPtr.Zero);
                if (result && written == buff.Length) return StreamWriteResult.Success;
                else return StreamWriteResult.FaildInEditing;
            }
            finally
            {
                CloseHandle(fileHandle);
            }
        }

#region Win32 API宣言

        [System.Security.SuppressUnmanagedCodeSecurity] // 呼び出しごとのセキュリティのスタックウォークを省く（K5）
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr CreateFile(
            string lpFileName, // ファイル名
            DesiredAccess dwDesiredAccess, // アクセスモード
            ShareMode dwShareMode, // 共有モード
            int lpSecurityAttributes, // セキュリティ記述子
            CreationDisposition dwCreationDisposition, // 作成方法
            FlagsAndAttributes dwFlagsAndAttributes, // ファイル属性
            IntPtr hTemplateFile // テンプレートファイルのハンドル
            );

        [System.Security.SuppressUnmanagedCodeSecurity]
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetFileTime(IntPtr hFile, IntPtr lpCreationTime, ref long lpLastAccessTime, ref long lpLastWriteTime);

        [System.Security.SuppressUnmanagedCodeSecurity] // 呼び出しごとのセキュリティのスタックウォークを省く（K5）
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool DeleteFile(
            string lpFileName // ファイル名
            );

        [System.Security.SuppressUnmanagedCodeSecurity] // 呼び出しごとのセキュリティのスタックウォークを省く（K5）
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern unsafe bool CloseHandle
        (
            IntPtr hObject // handle to object
        );

        [System.Security.SuppressUnmanagedCodeSecurity] // 呼び出しごとのセキュリティのスタックウォークを省く（K5）
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool WriteFile(
         IntPtr hFile,
         byte[] aBuffer,
         uint cbToWrite,
         ref uint cbThatWereWritten,
         IntPtr pOverlapped);

        public override void Flush()
        {
            fileStream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return fileStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            fileStream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return fileStream.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            fileStream.Write(buffer, offset, count);
        }


#region 列挙体

        private enum DesiredAccess : uint
        {
            GENERIC_EXISTENCE = 0x00000000,
            GENERIC_READ = 0x80000000,
            GENERIC_WRITE = 0x40000000,
            GENERIC_EXECUTE = 0x20000000
        }

        private enum ShareMode : uint
        {
            FILE_SHARE_NONE = 0x00000000,
            FILE_SHARE_READ = 0x00000001,
            FILE_SHARE_WRITE = 0x00000002,
            FILE_SHARE_DELETE = 0x00000004
        }

        private enum CreationDisposition : uint
        {
            CREATE_NEW = 1,
            CREATE_ALWAYS = 2,
            OPEN_EXISTING = 3,
            OPEN_ALWAYS = 4,
            TRUNCATE_EXISTING = 5
        }

        private enum FlagsAndAttributes : uint
        {
            FILE_ATTRIBUTE_ARCHIVE = 0x00000020,
            FILE_ATTRIBUTE_ENCRYPTED = 0x00004000,
            FILE_ATTRIBUTE_HIDDEN = 0x00000002,
            FILE_ATTRIBUTE_NORMAL = 0x00000080,
            FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x00002000,
            FILE_ATTRIBUTE_OFFLINE = 0x00001000,
            FILE_ATTRIBUTE_READONLY = 0x00000001,
            FILE_ATTRIBUTE_SYSTEM = 0x00000004,
            FILE_ATTRIBUTE_TEMPORARY = 0x00000100
        }

#endregion

#endregion
    }
}
