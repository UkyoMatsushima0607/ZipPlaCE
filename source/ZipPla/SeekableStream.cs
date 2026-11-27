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
    public class SeekableStream : Stream
    {
        public static Stream Seekablize(Stream originalStream, long streamLength, bool leaveOpen = false) =>
            originalStream.CanSeek ? (leaveOpen ? new NonClosingStream(originalStream) : originalStream) : new SeekableStream(originalStream, streamLength, leaveOpen);

        /// <summary>
        /// Dispose しても元のストリームを閉じない包み（X19: シークできるストリームでも leaveOpen を効かせる）
        /// </summary>
        private sealed class NonClosingStream : Stream
        {
            private readonly Stream s;
            public NonClosingStream(Stream s) { this.s = s; }
            public override bool CanRead => s.CanRead;
            public override bool CanSeek => s.CanSeek;
            public override bool CanWrite => false;
            public override long Length => s.Length;
            public override long Position { get => s.Position; set => s.Position = value; }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => s.Read(buffer, offset, count);
            public override int ReadByte() => s.ReadByte();
            public override long Seek(long offset, SeekOrigin origin) => s.Seek(offset, origin);
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private readonly bool leaveOpen;
        private readonly Stream originalStream;
        private readonly MemoryStream buffStream;
        private readonly long streamLength;
        private long positionLookahead;
        private bool stopBuffering = false;
        private bool bufferDisposed = false;
        private SeekableStream(Stream unseekableStream, long streamLength, bool leaveOpen = false)
        {
            originalStream = unseekableStream;
            this.streamLength = streamLength;
            bufferSize = (int)Math.Min(streamLength, maxBufferSize);
            buffStream = new MemoryStream();
            this.leaveOpen = leaveOpen;
        }

        public override bool CanRead => originalStream.CanRead;
        public override bool CanSeek => !stopBuffering;
        public override bool CanWrite => false;
        public override long Length => streamLength;
        public override long Position
        {
            get => bufferDisposed ? originalStream.Position : buffStream.Position + positionLookahead;
            set => Seek(value, SeekOrigin.Begin);
        }
        public override void Flush() => originalStream.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (bufferDisposed)
            {
                return originalStream.Read(buffer, offset, count);
            }
            var buffPosition = buffStream.Position;
            var loadedPosition = buffStream.Length;
            int result;
            if (buffPosition < loadedPosition)
            {
                var fromBuffCount = Math.Min(count, (int)(loadedPosition - buffPosition));
                result = buffStream.Read(buffer, offset, fromBuffCount);
                offset += result;
                count -= result;
            }
            else
            {
                result = 0;
                if (positionLookahead > 0)
                {
                    CopyStream(originalStream, stopBuffering ? null : buffStream, positionLookahead);
                    positionLookahead = 0;
                }
            }
            if (stopBuffering)
            {
                if (buffPosition + result >= loadedPosition)
                {
                    buffStream.Dispose();
                    bufferDisposed = true;
                    if (count > 0) result += originalStream.Read(buffer, offset, count);
                }
            }
            else
            {
                if (count > 0)
                {
                    var read = originalStream.Read(buffer, offset, count);
                    buffStream.Write(buffer, offset, read);
                    result += read;
                }
            }
            return result;
        }

        private bool CopyStream_bufferEmpty = true;
        private byte[] CopyStream_buffer;
        private const int maxBufferSize = 81920;
        private readonly int bufferSize;
        private void CopyStream(Stream src, Stream dst, long count)
        {
            if (CopyStream_bufferEmpty)
            {
                CopyStream_buffer = new byte[bufferSize];
                CopyStream_bufferEmpty = false;
            }
            if (dst == null)
            {
                while (count > 0)
                {
                    var read = src.Read(CopyStream_buffer, 0, (int)Math.Min(count, bufferSize));
                    if (read > 0)
                    {
                        count -= read;
                    }
                    else
                    {
                        throw new IOException();
                    }
                }
            }
            else
            {
                while (count > 0)
                {
                    var read = src.Read(CopyStream_buffer, 0, (int)Math.Min(count, bufferSize));
                    if (read > 0)
                    {
                        dst.Write(CopyStream_buffer, 0, read);
                        count -= read;
                    }
                    else
                    {
                        throw new IOException();
                    }
                }
            }
        }
        
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (bufferDisposed)
            {
                return originalStream.Seek(offset, origin);
            }
            switch (origin)
            {
                case SeekOrigin.Begin:
                    {
                        var loadedPosition = buffStream.Length;
                        if (offset <= loadedPosition)
                        {
                            positionLookahead = 0;
                            return buffStream.Seek(offset, SeekOrigin.Begin);
                        }
                        else
                        {
                            buffStream.Seek(0, SeekOrigin.End);
                            if (offset > streamLength) offset = streamLength;
                            positionLookahead = offset - loadedPosition;
                            return offset;
                        }
                    }
                case SeekOrigin.Current: return Seek(Position + offset, SeekOrigin.Begin);
                case SeekOrigin.End: return Seek(streamLength + offset, SeekOrigin.Begin);
                default: throw new ArgumentException();
            }
        }

        public override void SetLength(long value) => throw new NotImplementedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        public void StopBuffering() => stopBuffering = true;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!leaveOpen) originalStream.Dispose();
                if (!bufferDisposed)
                {
                    buffStream.Dispose();
                    bufferDisposed = true;
                }

            }
            base.Dispose(disposing);
        }
    }
}
