using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZipPla
{
    public class Exif
    {
        private ushort tag;
        public ushort Tag { get { return tag; } }
        private uint value;
        public uint Value { get { return value; } }

        // APP1 より前に置けるセグメント（APP0 の JFIF など）の数の上限
        private const int MaxSegmentsBeforeApp1 = 16;

        /// <summary>
        /// JPEG の Exif（APP1）の 0 番目の IFD のタグを読む。値は型に合わせて読む（SHORT はエンディアンに合わせた 2 バイト）（X14）
        /// </summary>
        public static Exif[] GetAll(Stream stream)
        {
            try
            {
                var app1 = ReadApp1(stream);
                if (app1 == null) return null;
                return ParseTiff(app1, 6); // "Exif\0\0" の後ろが TIFF のヘッダ
            }
            catch
            {
                return null;
            }
        }

        private static byte[] ReadApp1(Stream stream)
        {
            var head = new byte[4];
            if (!StreamUtil.ReadExactly(stream, head, 2) || head[0] != 0xff || head[1] != 0xd8) return null;
            for (var n = 0; n < MaxSegmentsBeforeApp1; n++)
            {
                if (!StreamUtil.ReadExactly(stream, head, 4) || head[0] != 0xff) return null;
                var marker = head[1];
                var length = head[2] << 8 | head[3];
                if (length < 2) return null;
                if (marker == 0xda || marker == 0xd9) return null; // 画像データの開始・終了
                if (marker == 0xe1)
                {
                    var data = new byte[length - 2];
                    if (!StreamUtil.ReadExactly(stream, data, data.Length)) return null;
                    if (data.Length >= 14 && data[0] == 'E' && data[1] == 'x' && data[2] == 'i' && data[3] == 'f' && data[4] == 0 && data[5] == 0)
                    {
                        return data;
                    }
                    continue; // XMP などの APP1
                }
                if (stream.CanSeek) stream.Seek(length - 2, SeekOrigin.Current);
                else
                {
                    var skip = new byte[length - 2];
                    if (!StreamUtil.ReadExactly(stream, skip, skip.Length)) return null;
                }
            }
            return null;
        }

        private static Exif[] ParseTiff(byte[] d, int tiff)
        {
            bool bigEndian;
            if (d[tiff] == 'M' && d[tiff + 1] == 'M') bigEndian = true;
            else if (d[tiff] == 'I' && d[tiff + 1] == 'I') bigEndian = false;
            else return null;
            if (u16(d, tiff + 2, bigEndian) != 0x002a) return null;
            var ifd = tiff + (long)u32(d, tiff + 4, bigEndian);
            if (ifd + 2 > d.Length) return null;
            var tagCount = u16(d, (int)ifd, bigEndian);
            var p = (int)ifd + 2;
            if (p + 12L * tagCount > d.Length) tagCount = (ushort)((d.Length - p) / 12);
            var result = new Exif[tagCount];
            for (var i = 0; i < tagCount; i++, p += 12)
            {
                var tag = u16(d, p, bigEndian);
                var type = u16(d, p + 2, bigEndian);
                uint value;
                switch (type)
                {
                    case 1: // BYTE
                    case 7: // UNDEFINED
                        value = d[p + 8];
                        break;
                    case 3: // SHORT
                        value = u16(d, p + 8, bigEndian);
                        break;
                    default:
                        value = u32(d, p + 8, bigEndian);
                        break;
                }
                result[i] = new Exif { tag = tag, value = value };
            }
            return result;
        }

        private static ushort u16(byte[] d, int p, bool bigEndian)
        {
            return bigEndian ? (ushort)(d[p] << 8 | d[p + 1]) : (ushort)(d[p] | d[p + 1] << 8);
        }

        private static uint u32(byte[] d, int p, bool bigEndian)
        {
            return bigEndian
                ? (uint)(d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3])
                : (uint)(d[p] | d[p + 1] << 8 | d[p + 2] << 16 | d[p + 3] << 24);
        }

    }
}
