using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ZipPla
{
    /// <summary>
    /// ZipPla CE: パスのあるドライブがシークに時間のかかるもの（HDD）かを調べる。
    /// IOCTL_STORAGE_QUERY_PROPERTY の StorageDeviceSeekPenaltyProperty を使う（管理者の権限はいらない。Windows 7 以降）。
    /// ネットワークのドライブ・UNC パス・複数のディスクにまたがるボリュームなど、分からないときは null。結果はドライブごとに覚える
    /// </summary>
    public static class StorageInfo
    {
        private static readonly ConcurrentDictionary<string, bool?> seekPenaltyByRoot = new ConcurrentDictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

        public static bool? HasSeekPenalty(string path)
        {
#if PERFLOG
            // 計測用: 環境変数 ZIPPLA_ASSUME_HDD=1 ですべて HDD とみなす、0 でみなさない（CatalogForm の並べ替えの判定と同じ変数）
            var forced = Environment.GetEnvironmentVariable("ZIPPLA_ASSUME_HDD");
            if (forced == "1") return true;
            if (forced == "0") return false;
#endif
            string root;
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                root = Path.GetPathRoot(path);
            }
            catch { return null; }
            // "C:\" の形だけを扱う（UNC・\\?\ などは分からないものとする）
            if (root == null || root.Length < 2 || root[1] != ':' || !char.IsLetter(root[0])) return null;
            var drive = root.Substring(0, 2).ToUpperInvariant();
            return seekPenaltyByRoot.GetOrAdd(drive, query);
        }

        private static bool? query(string drive)
        {
            try
            {
                if (GetDriveType(drive + "\\") != DRIVE_FIXED && GetDriveType(drive + "\\") != DRIVE_REMOVABLE) return null;
                using (var handle = CreateFile(@"\\.\" + drive, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (handle.IsInvalid) return null;
                    var query = new STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceSeekPenaltyProperty, QueryType = PropertyStandardQuery };
                    var result = new DEVICE_SEEK_PENALTY_DESCRIPTOR();
                    if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, ref query, Marshal.SizeOf(typeof(STORAGE_PROPERTY_QUERY)),
                        out result, Marshal.SizeOf(typeof(DEVICE_SEEK_PENALTY_DESCRIPTOR)), out var returned, IntPtr.Zero)) return null;
                    if (returned < Marshal.SizeOf(typeof(DEVICE_SEEK_PENALTY_DESCRIPTOR))) return null;
                    return result.IncursSeekPenalty != 0;
                }
            }
            catch
            {
                return null;
            }
        }

        private const uint DRIVE_REMOVABLE = 2;
        private const uint DRIVE_FIXED = 3;
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const int StorageDeviceSeekPenaltyProperty = 7;
        private const int PropertyStandardQuery = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public int PropertyId;
            public int QueryType;
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            public byte IncursSeekPenalty; // BOOLEAN
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetDriveType(string lpRootPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, ref STORAGE_PROPERTY_QUERY lpInBuffer, int nInBufferSize,
            out DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);
    }
}
