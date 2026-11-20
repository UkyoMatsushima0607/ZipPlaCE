using System.Collections.Generic;
using System.IO;

namespace SevenZipExtractor
{
    // 複数のエントリをそれぞれのストリームへ書き出す展開コールバック（ZipPla CE 追記、B1）
    internal class ArchiveMultiStreamCallback : IArchiveExtractCallback
    {
        private readonly IDictionary<uint, Stream> streams;

        public ArchiveMultiStreamCallback(IDictionary<uint, Stream> streams)
        {
            this.streams = streams;
        }

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
            }
            else
            {
                outStream = null;
            }
            return 0;
        }

        public void PrepareOperation(AskMode askExtractMode)
        {
        }

        public void SetOperationResult(OperationResult resultEOperationResult)
        {
        }
    }
}
