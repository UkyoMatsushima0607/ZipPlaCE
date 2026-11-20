using System.Collections.Generic;
using System.IO;

namespace SevenZipExtractor
{
    public class Entry
    {
        private readonly IInArchive archive;
        private readonly uint index;

        internal Entry(IInArchive archive, uint index)
        {
            this.archive = archive;
            this.index = index;
        }

        public string FileName { get; internal set; }
        public bool IsFolder { get; internal set; }

        public void Extract(string fileName)
        {
            using (FileStream fileStream = File.Create(fileName))
            {
                this.Extract(fileStream);
            }
        }
        public void Extract(Stream stream)
        {
            this.archive.Extract(new[] { this.index }, 1, 0, new ArchiveStreamCallback(this.index, stream));
        }

        /// <summary>
        /// 同じ書庫の複数のエントリを1回の展開で取り出す（ZipPla CE 追記、B1）。
        /// solid 書庫では1件ずつ Extract するとブロックの先頭から毎回展開し直すため、まとめて取り出すと速い。
        /// streamFor にはエントリごとの書き込み先を返す（entries と同じ順の添字が渡される）。
        /// </summary>
        public static void ExtractMany(IList<Entry> entries, System.Func<int, Stream> streamFor)
        {
            if (entries == null || entries.Count == 0) return;
            var archive = entries[0].archive;
            var streams = new Dictionary<uint, Stream>();
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].archive != archive) throw new System.ArgumentException("All entries must belong to the same archive.");
                streams[entries[i].index] = streamFor(i);
            }
            var indices = new List<uint>(streams.Keys);
            indices.Sort(); // 7-Zip はインデックスが昇順であることを要求する
            var result = archive.Extract(indices.ToArray(), (uint)indices.Count, 0, new ArchiveMultiStreamCallback(streams));
            if (result != 0) throw new SevenZipException("Extract failed: 0x" + result.ToString("X8"));
        }

        // Rio's 追記
        public long Size
        {
            get
            {
                PropVariant value = new PropVariant();
                archive.GetProperty(index, ItemPropId.kpidSize, ref value);
                return value.longValue;
            }
        }

        public System.DateTime LastWriteTime
        {
            get
            {
                PropVariant value = new PropVariant();
                archive.GetProperty(index, ItemPropId.kpidLastWriteTime, ref value);
                return System.DateTime.FromFileTime(value.longValue);
            }
        }

        public bool IsSolid
        {
            get
            {
                PropVariant value = new PropVariant();
                archive.GetProperty(index, ItemPropId.kpidSolid, ref value);
                return value.longValue == 0;
            }
        }
    }
}