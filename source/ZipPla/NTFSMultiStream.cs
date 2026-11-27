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
