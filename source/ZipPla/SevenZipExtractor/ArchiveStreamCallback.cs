using System.IO;

namespace SevenZipExtractor
{
    internal class ArchiveStreamCallback : IArchiveExtractCallback
    {
        private readonly uint FileNumber;
        private readonly Stream stream;
        private OutStreamWrapper FileStream;

        public Stream Stream
        {
            get
            {
                return this.stream;
            }
        }

        // 対象のエントリの展開の結果（ZipPla CE 追記）。展開全体の戻り値は CRC エラーなどでも 0 になるので、これを見る
        public OperationResult Result { get; private set; } = OperationResult.kDataError; // 結果が来ないまま終わったら失敗
        private bool extracting;

        public ArchiveStreamCallback(uint fileNumber, Stream stream)
        {
            this.FileNumber = fileNumber;
            this.stream = stream;
        }

        public void SetTotal(ulong total)
        {
        }

        public void SetCompleted(ref ulong completeValue)
        {
        }

        public int GetStream(uint index, out ISequentialOutStream outStream, AskMode askExtractMode)
        {
            if ((index == this.FileNumber) && (askExtractMode == AskMode.kExtract))
            {
                this.FileStream = new OutStreamWrapper(this.stream);
                outStream = this.FileStream;
                this.extracting = true;
            }
            else
            {
                outStream = null;
                this.extracting = false;
            }

            return 0;
        }

        public void PrepareOperation(AskMode askExtractMode)
        {
        }

        public void SetOperationResult(OperationResult resultEOperationResult)
        {
            if (this.extracting) this.Result = resultEOperationResult;
            this.extracting = false;
        }
    }
}
