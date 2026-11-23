using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ZipPla
{
    /// <summary>
    /// ZipPla CE: libjpeg-turbo（IJG・BSD 系のライセンス）の TurboJPEG API で JPEG を読む。
    /// SIMD（SSE2・AVX2）の IDCT と色の変換を使うので、GDI+・WIC より速い。DCT の段階で 1/2・1/4・1/8 に縮小して読める。
    /// exe と同じフォルダに turbojpeg.dll があるときだけ使い、ない・読めない・対応していない（CMYK など）ときは null を返す（呼び出し側は従来どおり GDI+・WIC で読む）。
    /// 使うのは TurboJPEG 2.x の API だけで、libjpeg-turbo 3.x の turbojpeg.dll でも動く。DLL は tools/build-libjpeg-turbo.ps1 で公式ソースからビルドできる。
    /// 1 回のデコードごとにハンドルを作って捨てるので、スレッド安全
    /// </summary>
    public static class TurboJpegDecoder
    {
        private const string DllName = "turbojpeg.dll";

        // turbojpeg.h（2.x）
        private const int TJPF_BGR = 1;
        private const int TJPF_GRAY = 6;
        private const int TJCS_RGB = 0, TJCS_YCbCr = 1, TJCS_GRAY = 2;

        // jpegSize は C の unsigned long（Windows では 32bit、Linux の 64bit では 64bit）。どちらも UIntPtr で渡せる
        // （Windows x64 では 3 番目の引数でレジスタ渡しなので下位 32bit が読まれる）
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr tjInitDecompress();
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int tjDecompressHeader3(IntPtr handle, IntPtr jpegBuf, UIntPtr jpegSize, out int width, out int height, out int jpegSubsamp, out int jpegColorspace);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int tjDecompress2(IntPtr handle, IntPtr jpegBuf, UIntPtr jpegSize, IntPtr dstBuf, int width, int pitch, int height, int pixelFormat, int flags);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int tjDestroy(IntPtr handle);

        // 0: 未確認、1: 使える、-1: 使えない
        private static int availability;

        /// <summary>テスト用。exe のフォルダを見ずに使えるものとする</summary>
        internal static void ForceAvailableForTest() => availability = 1;

        public static bool Available
        {
            get
            {
                if (availability == 0)
                {
                    bool ok;
                    try
                    {
                        ok = File.Exists(Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), DllName));
                    }
                    catch
                    {
                        ok = false;
                    }
                    availability = ok ? 1 : -1;
                }
                return availability > 0;
            }
        }

        public struct Header
        {
            public int Width, Height;
            public bool Gray;
        }

        /// <summary>
        /// 大きさと色の種類を読む。対応していない（CMYK・YCCK など）・読めないときは false
        /// </summary>
        public static bool TryReadHeader(byte[] data, int offset, int length, out Header header)
        {
            header = default(Header);
            if (!Available || data == null || length <= 0 || offset < 0 || offset + length > data.Length) return false;
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            var handle = IntPtr.Zero;
            try
            {
                handle = tjInitDecompress();
                if (handle == IntPtr.Zero) return false;
                return readHeader(handle, pin.AddrOfPinnedObject() + offset, length, out header);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException)
            {
                availability = -1; // 32bit で動いているとき・DLL が壊れているときは以後使わない
                return false;
            }
            finally
            {
                if (handle != IntPtr.Zero) tjDestroy(handle);
                pin.Free();
            }
        }

        private static bool readHeader(IntPtr handle, IntPtr ptr, int length, out Header header)
        {
            header = default(Header);
            if (tjDecompressHeader3(handle, ptr, (UIntPtr)(uint)length, out var width, out var height, out _, out var colorspace) != 0) return false;
            if (width <= 0 || height <= 0) return false;
            if (colorspace != TJCS_YCbCr && colorspace != TJCS_GRAY && colorspace != TJCS_RGB) return false; // CMYK・YCCK は GDI+ と色の扱いを合わせられない
            header = new Header { Width = width, Height = height, Gray = colorspace == TJCS_GRAY };
            return true;
        }

        /// <summary>
        /// 1/denominator（1・2・4・8）に縮小してデコードする。大きさは縦横とも (元 + denominator - 1) / denominator（WIC の DecodePixelWidth と同じ）。
        /// 灰色の JPEG は、grayAsIndexed なら恒等パレットの 8bppIndexed（GDI+ と同じ）、そうでなければ 24bppRgb、カラーは 24bppRgb で返す。
        /// 対応していない・読めない・壊れている（警告を含む）ときは null
        /// </summary>
        public static System.Drawing.Bitmap TryDecode(byte[] data, int offset, int length, int denominator, bool grayAsIndexed)
        {
            if (!Available || data == null || length <= 0 || offset < 0 || offset + length > data.Length) return null;
            if (denominator != 1 && denominator != 2 && denominator != 4 && denominator != 8) return null;
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            var handle = IntPtr.Zero;
            System.Drawing.Bitmap result = null;
            try
            {
                handle = tjInitDecompress();
                if (handle == IntPtr.Zero) return null;
                var ptr = pin.AddrOfPinnedObject() + offset;
                if (!readHeader(handle, ptr, length, out var header)) return null;
                var width = (header.Width + denominator - 1) / denominator;
                var height = (header.Height + denominator - 1) / denominator;
                var indexed = header.Gray && grayAsIndexed;
                result = new System.Drawing.Bitmap(width, height, indexed ? PixelFormat.Format8bppIndexed : PixelFormat.Format24bppRgb);
                if (indexed)
                {
                    var palette = result.Palette;
                    for (var i = 0; i < 256; i++) palette.Entries[i] = Color.FromArgb(i, i, i);
                    result.Palette = palette;
                }
                var bits = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, result.PixelFormat);
                int status;
                try
                {
                    // 幅・高さに縮小後の大きさを渡すと、TurboJPEG がそれ以上になる一番小さい縮小率（ここでは 1/denominator）を選ぶ
                    status = tjDecompress2(handle, ptr, (UIntPtr)(uint)length, bits.Scan0, width, bits.Stride, height, indexed ? TJPF_GRAY : TJPF_BGR, 0);
                }
                finally
                {
                    result.UnlockBits(bits);
                }
                if (status != 0) return null; // 壊れている・途中で切れているなどは、警告でも従来の読み方に任せる
                var r = result;
                result = null;
                return r;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException)
            {
                availability = -1;
                return null;
            }
            catch (ArgumentException)
            {
                return null; // 大きすぎて Bitmap を作れないなど
            }
            finally
            {
                result?.Dispose();
                if (handle != IntPtr.Zero) tjDestroy(handle);
                pin.Free();
            }
        }

        /// <summary>
        /// stream の今の位置から終わりまでを読み、TryDecode する。読めなければ stream の位置を戻して null
        /// </summary>
        public static System.Drawing.Bitmap TryDecode(Stream stream, int denominator, bool grayAsIndexed)
        {
            if (!Available) return null;
            long start;
            try { start = stream.Position; } catch { return null; }
            try
            {
                if (!readRest(stream, out var data, out var offset, out var length)) { stream.Position = start; return null; }
                var result = TryDecode(data, offset, length, denominator, grayAsIndexed);
                if (result == null) stream.Position = start;
                return result;
            }
            catch
            {
                try { stream.Position = start; } catch { }
                return null;
            }
        }

        /// <summary>
        /// stream の今の位置から終わりまでを読む（ヘッダを見てから縮小率を決める呼び出し側用）。読めなければ stream の位置を戻して false
        /// </summary>
        public static bool TryReadAll(Stream stream, out byte[] data, out int offset, out int length)
        {
            data = null; offset = 0; length = 0;
            long start;
            try { start = stream.Position; } catch { return false; }
            try
            {
                if (readRest(stream, out data, out offset, out length)) return true;
            }
            catch { }
            try { stream.Position = start; } catch { }
            return false;
        }

        /// <summary>
        /// stream の今の位置から終わりまでのバイト列。MemoryStream で中身が見えれば写さずに使う
        /// </summary>
        private static bool readRest(Stream stream, out byte[] data, out int offset, out int length)
        {
            data = null; offset = 0; length = 0;
            var position = stream.Position;
            var rest = stream.Length - position;
            if (rest <= 0 || rest > int.MaxValue) return false;
            if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment))
            {
                data = segment.Array;
                offset = segment.Offset + (int)position;
                length = (int)rest;
                stream.Position = position + rest;
                return true;
            }
            data = new byte[rest];
            var read = 0;
            while (read < rest)
            {
                var n = stream.Read(data, read, (int)rest - read);
                if (n <= 0) break;
                read += n;
            }
            length = read;
            return read > 0;
        }
    }
}
