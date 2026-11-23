using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ZipPla
{
    /// <summary>
    /// ZipPla CE: LongVectorImageResizer の core（ガンマ 1.0 の経路）のネイティブ版 lvresize.dll（source/native/lvresize）を呼ぶ。
    /// 内側の積和を、実行時に CPU を見て AVX-512・AVX2・スカラーから選ぶ。計算は C# の版とビット単位で同じ。
    /// exe と同じフォルダに lvresize.dll があるときだけ使い、ない・読めないときは C# の版を使う
    /// </summary>
    internal static class NativeResizer
    {
        private const string DllName = "lvresize.dll";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int CancelCallback();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int lvr_get_isa();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe int lvr_core(
            IntPtr dstScan0, long x0, long x1, long y0, long y1, long dstStride, long dstBytesPerPixel, long dstDataWidth, long dstDataHeight,
            IntPtr srcScan0, long txx, long tx0, long txd, long srcXStep, long tyy, long ty0, long tyd, long srcYStep,
            long srcStride, long srcBytesPerPixel, long srcDataWidth, long srcDataHeight,
            int rx, int ry, ulong* cx0, ulong* cy0, ulong* buff1d, ulong* buff2d, int interShift, CancelCallback cancel, int isa);

        /// <summary>
        /// テスト用。-2: 使わない（C# の版）、-1: CPU に合わせる（既定）、0: スカラー、1: AVX2、2: AVX-512
        /// </summary>
        internal static int ForcedIsa = -1;
        /// <summary>テスト用。exe のフォルダを見ずに使えるものとする</summary>
        internal static void ForceAvailableForTest() => availability = 1;

        // 0: 未確認、1: 使える、-1: 使えない
        private static int availability;

        public static bool Available
        {
            get
            {
                if (ForcedIsa == -2) return false;
                if (availability == 0)
                {
                    var ok = false;
                    try
                    {
                        if (File.Exists(Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), DllName)))
                        {
                            lvr_get_isa(); // 読み込めるか（32bit で 64bit の DLL など）
                            ok = true;
                        }
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

        /// <summary>使っている命令セット（0: スカラー、1: AVX2、2: AVX-512）。使えなければ -1</summary>
        public static int Isa
        {
            get
            {
                if (!Available) return -1;
                try { return lvr_get_isa(); } catch { return -1; }
            }
        }

        /// <summary>
        /// LongVectorImageResizer.core と同じ（toLinear == null の経路）。描き終われば true、cancel で止めたときも true。
        /// DLL が使えなければ false（呼び出し側は C# の版で描く）
        /// </summary>
        public static unsafe bool TryCore(
            IntPtr dstScan0, long x0, long x1, long y0, long y1, long dstStride, long dstBytesPerPixel, long dstDataWidth, long dstDataHeight,
            IntPtr srcScan0, long txx, long tx0, long txd, long srcXStep, long tyy, long ty0, long tyd, long srcYStep,
            long srcStride, long srcBytesPerPixel, long srcDataWidth, long srcDataHeight,
            int rx, int ry, ulong* cx0, ulong* cy0, ulong* buff1d, ulong* buff2d, int interShift, Func<bool> cancel)
        {
            if (!Available) return false;
            CancelCallback callback = null;
            if (cancel != null) callback = () => cancel() ? 1 : 0;
            int result;
            try
            {
                result = lvr_core(dstScan0, x0, x1, y0, y1, dstStride, dstBytesPerPixel, dstDataWidth, dstDataHeight,
                    srcScan0, txx, tx0, txd, srcXStep, tyy, ty0, tyd, srcYStep, srcStride, srcBytesPerPixel, srcDataWidth, srcDataHeight,
                    rx, ry, cx0, cy0, buff1d, buff2d, interShift, callback, ForcedIsa);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException)
            {
                availability = -1;
                return false;
            }
            finally
            {
                GC.KeepAlive(callback);
            }
            return result >= 0;
        }
    }
}
