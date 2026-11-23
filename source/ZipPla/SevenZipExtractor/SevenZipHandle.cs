using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SevenZipExtractor
{
    internal class SevenZipHandle : IDisposable
    {
        // ZipPla CE 追記: 7z.dll は書庫を開くたびに LoadLibrary し、閉じるたびに FreeLibrary していた。
        // カタログで書庫のサムネイルを作るときのように書庫を次々に開くと、ほかに使っている書庫がなければ毎回 DLL の読み込みと初期化をやり直すことになる。
        // パスごとに 1 回だけ読み込み、CreateObject の関数ポインターのデリゲートと一緒にプロセスの終わりまで持つ
        // （解放しないので、COM オブジェクトが残っている間に DLL が外れることもない）
        private sealed class Library
        {
            public SafeLibraryHandle Handle;
            public CreateObjectDelegate CreateObject;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Library> Libraries =
            new System.Collections.Generic.Dictionary<string, Library>(StringComparer.OrdinalIgnoreCase);

        private Library library;

        public SevenZipHandle(string sevenZipLibPath)
        {
            lock (Libraries)
            {
                if (!Libraries.TryGetValue(sevenZipLibPath, out var lib))
                {
                    var handle = Kernel32Dll.LoadLibrary(sevenZipLibPath);
                    if (handle.IsInvalid)
                    {
                        throw new Win32Exception();
                    }

                    IntPtr functionPtr = Kernel32Dll.GetProcAddress(handle, "GetHandlerProperty");

                    // Not valid dll
                    if (functionPtr == IntPtr.Zero)
                    {
                        handle.Close();
                        throw new ArgumentException();
                    }

                    IntPtr procAddress = Kernel32Dll.GetProcAddress(handle, "CreateObject");
                    lib = new Library
                    {
                        Handle = handle,
                        CreateObject = procAddress == IntPtr.Zero ? null : (CreateObjectDelegate)Marshal.GetDelegateForFunctionPointer(procAddress, typeof(CreateObjectDelegate)),
                    };
                    Libraries.Add(sevenZipLibPath, lib);
                }
                this.library = lib;
            }
        }

        public void Dispose()
        {
            // DLL はほかの書庫でも使い回すので解放しない
            this.library = null;
        }

        public IInArchive CreateInArchive(Guid classId)
        {
            var lib = this.library;
            if (lib == null)
            {
                throw new ObjectDisposedException("SevenZipHandle");
            }

            CreateObjectDelegate createObject = lib.CreateObject;

            if (createObject != null)
            {
                object result;
                Guid interfaceId = typeof (IInArchive).GUID;
                createObject(ref classId, ref interfaceId, out result);

                return result as IInArchive;
            }

            return null;
        }
    }
}