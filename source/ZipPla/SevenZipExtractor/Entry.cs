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
            var callback = new ArchiveStreamCallback(this.index, stream);
            this.archive.Extract(new[] { this.index }, 1, 0, callback);
            // CRC エラー・データの破損などでも Extract の戻り値は 0 なので、項目の結果を見る（ZipPla CE 追記）
            if (callback.Result != OperationResult.kOK) throw new SevenZipException("Extract failed: " + callback.Result + ": " + this.FileName);
        }

        /// <summary>
        /// 同じ書庫の複数のエントリを1回の展開で取り出す（ZipPla CE 追記、B1）。
        /// solid 書庫では1件ずつ Extract するとブロックの先頭から毎回展開し直すため、まとめて取り出すと速い。
        /// streamFor にはエントリごとの書き込み先を返す（entries と同じ順の添字が渡される）。
        /// </summary>
        /// onFailed には、展開に失敗した（CRC エラー・データの破損など）エントリの添字が渡される。null なら例外にする。
        public static void ExtractMany(IList<Entry> entries, System.Func<int, Stream> streamFor, System.Action<int> onFailed = null)
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
            var callback = new ArchiveMultiStreamCallback(streams);
            var result = archive.Extract(indices.ToArray(), (uint)indices.Count, 0, callback);
            if (result != 0) throw new SevenZipException("Extract failed: 0x" + result.ToString("X8"));
            // CRC エラー・データの破損などのエントリは、展開全体の戻り値が 0 でも途中までの・壊れたバイト列になっている（ZipPla CE 追記）
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    if (!callback.Succeeded(entries[i].index))
                    {
                        if (onFailed == null) throw new SevenZipException("Extract failed: " + entries[i].FileName);
                        onFailed(i);
                    }
                }
            }
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

        /// <summary>
        /// 項目が暗号化されているか（ZipPla CE 追記）。VT_BOOL の VARIANT_TRUE（-1）なら true
        /// </summary>
        public bool IsEncrypted
        {
            get
            {
                PropVariant value = new PropVariant();
                archive.GetProperty(index, ItemPropId.kpidEncrypted, ref value);
                return value.VarType == System.Runtime.InteropServices.VarEnum.VT_BOOL && (short)(value.longValue & 0xFFFF) != 0;
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