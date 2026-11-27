using System.IO;

namespace ZipPla
{
    /// <summary>ストリームの読み取りの小さな補助</summary>
    internal static class StreamUtil
    {
        /// <summary>
        /// buffer[offset..offset+count) に count バイトを読み切る。途中で終わりに達したら false（Stream.Read は要求より少なく返すことがある）
        /// </summary>
        public static bool ReadExactly(Stream stream, byte[] buffer, int count, int offset = 0)
        {
            var read = 0;
            while (read < count)
            {
                var n = stream.Read(buffer, offset + read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        /// <summary>
        /// source の残りを MemoryStream に写し、先頭に戻して返す。長さ length（負なら不明）が分かれば、その大きさで確保して
        /// 倍々に伸ばし直す無駄を省く（D7）
        /// </summary>
        public static MemoryStream CopyToMemory(Stream source, long length)
        {
            var result = length >= 0 && length <= int.MaxValue ? new MemoryStream((int)length) : new MemoryStream();
            try
            {
                source.CopyTo(result);
                result.Position = 0;
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
    }
}
