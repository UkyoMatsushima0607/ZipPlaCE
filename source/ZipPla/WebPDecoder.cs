using System;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ZipPla
{
    /// <summary>
    /// libwebp（Google、BSD 3 条項ライセンス）のデコーダ DLL で WebP を読む。
    /// exe と同じフォルダに libwebpdecoder.dll があるときだけ使い、ない・読めないときは null を返す（呼び出し側は ffmpeg で読む）。
    /// DLL は tools/build-libwebp.ps1 で公式ソースからビルドできる。
    /// </summary>
    public static class WebPDecoder
    {
        private const string DllName = "libwebpdecoder.dll";
        private const int WEBP_DECODER_ABI_VERSION = 0x0210; // libwebp 1.6.0 の decode.h

        [StructLayout(LayoutKind.Sequential)]
        private struct WebPBitstreamFeatures
        {
            public int Width;
            public int Height;
            public int HasAlpha;
            public int HasAnimation;
            public int Format;
            public uint Pad0, Pad1, Pad2, Pad3, Pad4;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WebPGetFeaturesInternal(IntPtr data, UIntPtr dataSize, ref WebPBitstreamFeatures features, int version);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WebPDecodeBGRInto(IntPtr data, UIntPtr dataSize, IntPtr outputBuffer, UIntPtr outputBufferSize, int outputStride);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WebPDecodeBGRAInto(IntPtr data, UIntPtr dataSize, IntPtr outputBuffer, UIntPtr outputBufferSize, int outputStride);

        // 0: 未確認、1: 使える、-1: 使えない
        private static int availability;

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

        /// <summary>
        /// WebP をデコードする。DLL がない、アニメーション WebP、デコードに失敗した場合は null。
        /// 透明度があれば 32bppArgb（非乗算）、なければ 24bppRgb で返す。
        /// </summary>
        public static Bitmap TryDecode(byte[] data, int length)
        {
            if (data == null || length <= 0 || length > data.Length || !Available) return null;
            try
            {
                return decode(data, length);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException)
            {
                // 32bit で動いているときや DLL が壊れているときは以後使わない
                availability = -1;
                return null;
            }
        }

        private static Bitmap decode(byte[] data, int length)
        {
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject();
                var size = (UIntPtr)(uint)length;
                var features = new WebPBitstreamFeatures();
                if (WebPGetFeaturesInternal(ptr, size, ref features, WEBP_DECODER_ABI_VERSION) != 0) return null;
                if (features.HasAnimation != 0 || features.Width <= 0 || features.Height <= 0) return null;

                var alpha = features.HasAlpha != 0;
                var result = new System.Drawing.Bitmap(features.Width, features.Height, alpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
                var ok = false;
                try
                {
                    var bd = result.LockBits(new System.Drawing.Rectangle(0, 0, features.Width, features.Height), ImageLockMode.WriteOnly, result.PixelFormat);
                    try
                    {
                        var bufferSize = (UIntPtr)(ulong)((long)bd.Stride * features.Height);
                        var decoded = alpha ?
                            WebPDecodeBGRAInto(ptr, size, bd.Scan0, bufferSize, bd.Stride) :
                            WebPDecodeBGRInto(ptr, size, bd.Scan0, bufferSize, bd.Stride);
                        ok = decoded != IntPtr.Zero;
                    }
                    finally
                    {
                        result.UnlockBits(bd);
                    }
                }
                finally
                {
                    if (!ok) result.Dispose();
                }
                return ok ? new Bitmap(result) : null;
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
