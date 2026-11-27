using iTextSharp.text.pdf;
using SevenZipExtractor;
using SharpCompress.Archives;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ZipPla
{
    public class ReleasableStream : Stream
    {
        // MemoryStream の上限の 2GB より少し小さく（PackedImageLoader.OnMemoryLimit と同じ）（V18）
        private const long ReleaseLimit = int.MaxValue - 64 * 1024 * 1024;
        // 写すときに 1 回でロックを持つ大きさ
        private const int ReleaseChunkSize = 4 * 1024 * 1024;
        readonly object thisLock = new object();
        volatile bool isReleased = false;
        public bool IsReleased => isReleased;
        Stream stream;
        public ReleasableStream(Stream originalStream)
        {
            stream = originalStream;
        }

        public void Release()
        {
            if (!isReleased)
            {
                // 長さぶんの配列を先に確保し、少しずつロックを取って写す。以前は容量なしの MemoryStream に
                // ロックを持ったまま全体を写していたので、倍々の拡張で約 2 倍を確保し、その間ほかのスレッドの読み込みが止まり、
                // 2GB を超える書庫では例外になっていた（OnMemory 側の B8・V18 と同じ扱いにする）
                byte[] buffer;
                long length;
                try
                {
                    lock (thisLock)
                    {
                        if (isReleased || stream == null) return;
                        length = stream.Length;
                    }
                    // 大きすぎる書庫は MemoryStream にできないので、ファイルから読み続ける（V18）
                    if (length > ReleaseLimit) return;
                    buffer = new byte[length];
                    long done = 0;
                    while (done < length)
                    {
                        lock (thisLock)
                        {
                            if (isReleased || stream == null) return;
                            var position = stream.Position;
                            try
                            {
                                stream.Seek(done, SeekOrigin.Begin);
                                var count = (int)Math.Min(ReleaseChunkSize, length - done);
                                var read = stream.Read(buffer, (int)done, count);
                                if (read <= 0) return; // 途中でファイルが縮んだ。写すのをやめてファイルから読み続ける
                                done += read;
                            }
                            finally
                            {
                                stream.Seek(position, SeekOrigin.Begin);
                            }
                        }
                    }
                }
                catch
                {
                    // Task.Run の中で呼ばれるので、失敗したらファイルから読み続ける
                    return;
                }

                var temp = null as Stream;
                lock (thisLock)
                {
                    if (!isReleased && stream != null)
                    {
                        var memoryStream = new MemoryStream(buffer, 0, (int)length, writable: false, publiclyVisible: true);
                        try
                        {
                            memoryStream.Seek(stream.Position, SeekOrigin.Begin);
                        }
                        catch
                        {
                            memoryStream.Dispose();
                            return;
                        }
                        temp = stream;
                        stream = memoryStream;
                        isReleased = true;
                    }
                }
                temp?.Dispose();
            }
        }

        public override bool CanRead{ get { if (isReleased) return stream.CanRead; lock(thisLock) return stream.CanRead; } }

        public override bool CanSeek { get { if(isReleased) return stream.CanSeek; lock(thisLock) return stream.CanSeek; } }

        public override bool CanWrite { get { if (isReleased) return stream.CanWrite; lock (thisLock) return stream.CanWrite; } }

        public override long Length { get { if (isReleased) return stream.Length; lock (thisLock) return stream.Length; } }

        public override long Position
        {
            get { if (isReleased) return stream.Position; lock (thisLock) return stream.Position; }
            set { if (isReleased) stream.Position = value; else lock (thisLock) stream.Position = value; }
        }

        public override void Flush() { if (isReleased) stream.Flush(); else lock(thisLock) stream.Flush(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (isReleased) return stream.Read(buffer, offset, count); lock (thisLock) return stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (isReleased) return stream.Seek(offset, origin); lock (thisLock) return stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            if (isReleased) stream.SetLength(value); else lock (thisLock) stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (isReleased) stream.Write(buffer, offset, count); else lock (thisLock) stream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (stream != null)
                {
                    if (isReleased)
                    {
                        stream?.Dispose();
                        stream = null;
                    }
                    else
                    {
                        lock (thisLock)
                        {
                            stream?.Dispose();
                            stream = null;
                            isReleased = true;
                        }
                    }
                }
            }
            base.Dispose(disposing);
        }
    }
}
