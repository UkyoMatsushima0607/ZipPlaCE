using System.Collections.Generic;
using System.IO;

namespace SevenZipExtractor
{
    // 複数のエントリをそれぞれのストリームへ書き出す展開コールバック（ZipPla CE 追記、B1）
    internal class ArchiveMultiStreamCallback : IArchiveExtractCallback
    {
        private readonly IDictionary<uint, Stream> streams;
        // 展開に成功したエントリ。CRC エラー・データの破損などでも展開全体の戻り値は 0 になり、壊れた位置より後ろの
        // エントリは結果が来ないまま終わるので、成功の結果が来たものだけを記録する
        private readonly HashSet<uint> succeeded = new HashSet<uint>();
        private long currentIndex = -1;

        public ArchiveMultiStreamCallback(IDictionary<uint, Stream> streams)
        {
            this.streams = streams;
        }

        public bool Succeeded(uint index) => this.succeeded.Contains(index);

        public void SetTotal(ulong total)
        {
        }

        public void SetCompleted(ref ulong completeValue)
        {
        }

        public int GetStream(uint index, out ISequentialOutStream outStream, AskMode askExtractMode)
        {
            Stream stream;
            if (askExtractMode == AskMode.kExtract && this.streams.TryGetValue(index, out stream) && stream != null)
            {
                // OutStreamWrapper の Dispose は元のストリームを閉じるので、ここでは Dispose しない
                outStream = new OutStreamWrapper(stream);
                this.currentIndex = index;
            }
            else
            {
                outStream = null;
                this.currentIndex = -1;
            }
            return 0;
        }

        public void PrepareOperation(AskMode askExtractMode)
        {
        }

        public void SetOperationResult(OperationResult resultEOperationResult)
        {
            if (resultEOperationResult == OperationResult.kOK && this.currentIndex >= 0)
            {
                this.succeeded.Add((uint)this.currentIndex);
            }
            this.currentIndex = -1;
        }
    }
}
