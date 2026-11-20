using System;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ZipPla
{
    /// <summary>
    /// libwebp（Google、BSD 3 条項ライセンス）のデコーダ DLL で WebP を読む。
    /// exe と同じフォルダに libwebpdecoder.dll があるときだけ使い、ない・読めないときは null を返す（呼び出し側は ffmpeg で読む）。
    /// アニメーション WebP は、さらに libwebpdemux.dll・libwebp.dll・libsharpyuv.dll があるときだけ読める。
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

        #region アニメーション WebP（libwebpdemux.dll。libwebp.dll・libsharpyuv.dll に依存する）

        private const string DemuxDllName = "libwebpdemux.dll";
        private const int WEBP_DEMUX_ABI_VERSION = 0x0107; // libwebp 1.6.0 の demux.h
        private const int MODE_BGRA = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct WebPData
        {
            public IntPtr Bytes;
            public UIntPtr Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WebPAnimDecoderOptions
        {
            public int ColorMode;
            public int UseThreads;
            public uint Padding0, Padding1, Padding2, Padding3, Padding4, Padding5, Padding6;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WebPAnimInfo
        {
            public uint CanvasWidth;
            public uint CanvasHeight;
            public uint LoopCount;
            public uint BgColor;
            public uint FrameCount;
            public uint Pad0, Pad1, Pad2, Pad3;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WebPIterator
        {
            public int FrameNum;
            public int NumFrames;
            public int XOffset, YOffset;
            public int Width, Height;
            public int Duration;
            public int DisposeMethod;
            public int Complete;
            public WebPData Fragment;
            public int HasAlpha;
            public int BlendMethod;
            public uint Pad0, Pad1;
            public IntPtr Private;
        }

        [DllImport(DemuxDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WebPAnimDecoderOptionsInitInternal(ref WebPAnimDecoderOptions options, int version);

        [DllImport(DemuxDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WebPAnimDecoderNewInternal(ref WebPData data, ref WebPAnimDecoderOptions options, int version);

        [DllImport(DemuxDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WebPAnimDecoderGetInfo(IntPtr decoder, out WebPAnimInfo info);

        [DllImport(DemuxDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WebPAnimDecoderGetNext(IntPtr decoder, out IntPtr buffer, out int timestamp);

        [DllImport(DemuxDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WebPAnimDecoderGetDemuxer(IntPtr decoder);

        [DllImport(DemuxDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WebPAnimDecoderDelete(IntPtr decoder);

        [DllImport(DemuxDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WebPDemuxGetFrame(IntPtr demuxer, int frameNumber, ref WebPIterator iterator);

        [DllImport(DemuxDllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WebPDemuxReleaseIterator(ref WebPIterator iterator);

        // 0: 未確認、1: 使える、-1: 使えない
        private static int animationAvailability;

        public static bool AnimationAvailable
        {
            get
            {
                if (animationAvailability == 0)
                {
                    bool ok;
                    try
                    {
                        var dir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                        ok = File.Exists(Path.Combine(dir, DemuxDllName)) && File.Exists(Path.Combine(dir, "libwebp.dll"));
                    }
                    catch
                    {
                        ok = false;
                    }
                    animationAvailability = ok ? 1 : -1;
                }
                return animationAvailability > 0;
            }
        }

        /// <summary>
        /// アニメーション WebP をデコードする。animation が true なら全フレーム（BitmapEx、フレームは必要になったときに順に作る）、
        /// false なら最初のフレームだけを返す。フレームは重ね合わせ済みで、透明部分は APNG と同じく取り除く。
        /// DLL がない、アニメーションでない、読めない場合は null。
        /// </summary>
        public static Bitmap TryDecodeAnimation(byte[] data, int length, bool animation)
        {
            if (data == null || length <= 0 || length > data.Length || !AnimationAvailable) return null;
            WebPAnimProvider provider = null;
            try
            {
                provider = WebPAnimProvider.Create(data, length, out var delays);
                if (provider == null) return null;
                if (animation && delays.Length > 1)
                {
                    var result = new BitmapEx(provider, delays);
                    provider = null; // BitmapEx が最後のフレームを作ったあとに破棄する
                    return result;
                }
                if (!provider.MoveNext()) return null;
                return provider.Current;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException)
            {
                animationAvailability = -1;
                return null;
            }
            finally
            {
                provider?.Dispose();
            }
        }

        private sealed class WebPAnimProvider : System.Collections.Generic.IEnumerator<Bitmap>
        {
            private IntPtr decoder;
            private IntPtr nativeData; // デコーダが参照し続けるので、破棄するまで解放しない
            private readonly int width, height, frameCount;
            private int index = -1;
            private IntPtr canvas;

            private WebPAnimProvider(IntPtr decoder, IntPtr nativeData, int width, int height, int frameCount)
            {
                this.decoder = decoder;
                this.nativeData = nativeData;
                this.width = width;
                this.height = height;
                this.frameCount = frameCount;
            }

            public static WebPAnimProvider Create(byte[] data, int length, out TimeSpan[] delays)
            {
                delays = null;
                var nativeData = Marshal.AllocHGlobal(length);
                var decoder = IntPtr.Zero;
                try
                {
                    Marshal.Copy(data, 0, nativeData, length);
                    var webpData = new WebPData { Bytes = nativeData, Size = (UIntPtr)(uint)length };
                    var options = new WebPAnimDecoderOptions();
                    if (WebPAnimDecoderOptionsInitInternal(ref options, WEBP_DEMUX_ABI_VERSION) == 0) return null;
                    options.ColorMode = MODE_BGRA;
                    options.UseThreads = 0;
                    decoder = WebPAnimDecoderNewInternal(ref webpData, ref options, WEBP_DEMUX_ABI_VERSION);
                    if (decoder == IntPtr.Zero) return null;
                    if (WebPAnimDecoderGetInfo(decoder, out var info) == 0) return null;
                    int width = (int)info.CanvasWidth, height = (int)info.CanvasHeight, frameCount = (int)info.FrameCount;
                    if (width <= 0 || height <= 0 || frameCount <= 0) return null;

                    // 各フレームの表示時間（ミリ秒）。デコードせずに読める
                    var demuxer = WebPAnimDecoderGetDemuxer(decoder);
                    delays = new TimeSpan[frameCount];
                    for (var i = 0; i < frameCount; i++)
                    {
                        var iterator = new WebPIterator();
                        var duration = 0;
                        if (demuxer != IntPtr.Zero && WebPDemuxGetFrame(demuxer, i + 1, ref iterator) != 0)
                        {
                            duration = iterator.Duration;
                            WebPDemuxReleaseIterator(ref iterator);
                        }
                        delays[i] = TimeSpan.FromMilliseconds(duration); // 短すぎる値は BitmapEx が既定値に置き換える
                    }

                    var result = new WebPAnimProvider(decoder, nativeData, width, height, frameCount);
                    decoder = IntPtr.Zero;
                    nativeData = IntPtr.Zero;
                    return result;
                }
                finally
                {
                    if (decoder != IntPtr.Zero) WebPAnimDecoderDelete(decoder);
                    if (nativeData != IntPtr.Zero) Marshal.FreeHGlobal(nativeData);
                }
            }

            public bool MoveNext()
            {
                if (decoder == IntPtr.Zero || index + 1 >= frameCount) return false;
                if (WebPAnimDecoderGetNext(decoder, out canvas, out var timestamp) == 0 || canvas == IntPtr.Zero)
                {
                    throw new InvalidDataException("WebPAnimDecoderGetNext failed.");
                }
                index++;
                return true;
            }

            // 現在のフレーム（キャンバス全体、BGRA 非乗算）を Bitmap にする。キャンバスは次の MoveNext まで有効
            public Bitmap Current
            {
                get
                {
                    if (canvas == IntPtr.Zero) throw new InvalidOperationException();
                    var argb = new System.Drawing.Bitmap(width, height, PixelFormat.Format32bppArgb);
                    try
                    {
                        var bd = argb.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                        try
                        {
                            var rowBytes = width * 4;
                            var row = new byte[rowBytes];
                            for (var y = 0; y < height; y++)
                            {
                                Marshal.Copy(canvas + y * rowBytes, row, 0, rowBytes);
                                Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, rowBytes);
                            }
                        }
                        finally
                        {
                            argb.UnlockBits(bd);
                        }
                        // APNG と同じく透明部分を取り除く（グレースケールなら 8bpp、それ以外は 24bpp）。
                        // 32bppArgb からは必ず別の画像が作られる（leaveOpenOriginal）ので、argb は破棄してよい
                        return ImageLoader.CreateNewBitmap(argb, removeAlpha: true);
                    }
                    finally
                    {
                        argb.Dispose();
                    }
                }
            }

            object System.Collections.IEnumerator.Current => Current;

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
                if (decoder != IntPtr.Zero)
                {
                    WebPAnimDecoderDelete(decoder);
                    decoder = IntPtr.Zero;
                }
                if (nativeData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(nativeData);
                    nativeData = IntPtr.Zero;
                }
                canvas = IntPtr.Zero;
            }
        }

        #endregion
    }
}
