using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZipPla
{
    public static class MovieThumbnailLoader
    {
        public const int FfmpegCancelCheckInterval = 10;

        /// <param name="nextKeyframe">
        /// true なら、指定した時刻ちょうどのコマではなく、その時刻以降の最初のキーフレームを返す（カタログの自動のサムネイル用）。
        /// 目的の時刻までデコードしないので速い（1080p の長い動画を 3 本同時に読んで 1 本あたり約半分）。
        /// 30 秒以内にキーフレームがない・読めないときは、時刻ちょうどのコマで読み直す
        /// </param>
        public static Bitmap GetThumbnail(string filePath, Func<Fraction, Size, Size> sizeProvider, Func<TimeSpan, TimeSpan> positionProvider, BackgroundWorker backgroundWorker, TimeSpan timeOut, ref MovieInfo movieInfo, bool nextKeyframe = false)
        {
            if(movieInfo == null) movieInfo = MovieInfo.FromFileCached(filePath);
            var size = movieInfo.SizeByPixel;
            if (size.IsEmpty) throw new Exception();
            bool originalSize;
            {
                Fraction fraction;
                if (movieInfo.SAR == 1 || movieInfo.SAR == 0) fraction = (Fraction)size;
                else fraction = (Fraction)size * movieInfo.SAR;
                if(sizeProvider == null)
                {
                    originalSize = true;
                }
                else
                {
                    var temp = size;
                    size = sizeProvider(fraction, size);
                    originalSize = size == temp;
                }
            }
            TimeSpan position;
            if (positionProvider == null)
            {
                position = TimeSpan.MaxValue;
            }
            else
            {
                var duration = movieInfo.Duration;
                position = positionProvider(duration);

                // 指定時刻が終了時刻に極端に近いと期待通りに画像が表示されないことがある
                duration = duration.Subtract(TimeSpan.FromSeconds(1));
                if (position > duration) position = duration;
                if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            }
            // 短い動画はキーフレームが先頭の 1 つだけのことが多く、探しても見つからずに読み直しになる（ffmpeg の起動 1 回ぶん遅くなる）ので、最初から時刻ちょうどで読む
            if (nextKeyframe && position < TimeSpan.MaxValue && movieInfo.Duration >= TimeSpan.FromSeconds(KeyframeMinDurationSeconds))
            {
                var keyframe = tryGetKeyframeThumbnail(filePath, size, originalSize, position, backgroundWorker);
                if (keyframe != null) return keyframe;
                if (backgroundWorker != null && backgroundWorker.CancellationPending) throw new OperationCanceledException(); // 中断。従来どおり、呼び出し側の例外処理が中断として扱う
#if PERFLOG
                PerfTrace.AddStage("movieKeyframeFallback", 0);
#endif
            }
            return GetThumbnail(filePath, size, originalSize, position, backgroundWorker);
        }

        // キーフレームが見つからないとき（キーフレームの間隔が極端に長い動画など）に、ファイルの終わりまで読み続けないための上限
        private const int KeyframeSearchSeconds = 30;
        // これより短い動画にはキーフレーム方式を使わない
        private const int KeyframeMinDurationSeconds = 20;

        /// <summary>
        /// position 以降の最初のキーフレームを取り出す。-skip_frame nokey でキーフレーム以外をデコードせずに捨てる。
        /// ffmpeg は position の直前のキーフレームから読み始め、position より前のコマは出力しないので、出てくるのは position 以降のキーフレームになる
        /// （直前のキーフレームを使う -noaccurate_seek だと、キーフレームの少ない動画でずっと前のコマになる。速さは変わらなかった）。
        /// デコードするのは 1 枚なので -threads 1（複数の ffmpeg を同時に動かしたときに取り合わない）。
        /// 読めない・大きさが違うときは null（呼び出し側が時刻ちょうどのコマで読み直す）。古い ffmpeg が引数を知らない場合も同じ
        /// </summary>
        private static Bitmap tryGetKeyframeThumbnail(string filePath, Size size, bool originalSize, TimeSpan position, BackgroundWorker backgroundWorker)
        {
            try
            {
                var cmd = $"-threads 1 -skip_frame nokey -ss {position.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -t {KeyframeSearchSeconds} -i \"{filePath}\"" +
                    (originalSize ? "" : $" -s {size.Width}x{size.Height}") + " -vframes 1 -an -vcodec bmp -f image2 pipe:1";
#if PERFLOG
                var perfFfmpeg = PerfTrace.Now();
#endif
                int outputSize;
                var buf = readStdOutBinary(ffmpegPath, cmd, backgroundWorker, size.Width * size.Height * 4 + 124, out outputSize);
#if PERFLOG
                PerfTrace.AddStage("movieFfmpegKeyframe", PerfTrace.Now() - perfFfmpeg);
#endif
                if (buf == null || outputSize < 54) return null; // 中断された、または 1 枚も出なかった
                using (var ms = new MemoryStream(buf, 0, outputSize, writable: false, publiclyVisible: false))
                {
                    var result = new Bitmap(ms);
                    if (result.Size != size)
                    {
                        result.Dispose();
                        return null;
                    }
                    return result;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is System.Runtime.InteropServices.ExternalException || e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                return null;
            }
        }

        public static Bitmap GetImageByFfmpeg(string filePath)
        {
            int outputSize;
            var cmd = $"-i \"{filePath}\" -vframes 1 -an -vcodec bmp -f image2 pipe:1";
            
            var buf = readStdOutBinary(ffmpegPath, cmd, null, null, out outputSize);

            using (var ms = new MemoryStream(buf, 0, outputSize, writable: false, publiclyVisible: false))
            {
                try
                {
                    return new Bitmap(ms);
                }
                finally
                {
                    ms.Close();
                }
            }
        }

        public static Bitmap GetImageByFfmpeg(Stream stream)
        {
            // Don't use a pipe
            // http://stackoverflow.com/questions/23002316/ffmpeg-pipe0-could-not-find-codec-parameters

            var tempFileName = Path.GetTempFileName(); // この時点でファイルは作られる
            try
            {
                using (var s = new FileStream(tempFileName, FileMode.Open, FileAccess.Write))
                {
                    stream.CopyTo(s);
                }
                return GetImageByFfmpeg(tempFileName);
            }
            finally
            {
                File.Delete(tempFileName);
            }
        }

        /// <summary>
        /// JPEG 2000（jp2・j2k など、PDF の JPXDecode を含む）を ffmpeg で読む。一時ファイルに書き出さず、標準入力で渡す（D6）。
        /// 標準入力から読めなければ、従来どおり一時ファイルで読む
        /// </summary>
        public static Bitmap GetJpeg2000ImageByFfmpeg(Stream stream, long streamLength = -1)
        {
            return getImageByFfmpegPipe(stream, streamLength, "-f j2k_pipe -i pipe:0 -vframes 1 -an -vcodec bmp -f image2 -update 1 pipe:1");
        }

        public static Bitmap GetWebPImageByFfmpeg(Stream stream, long streamLength = -1)
        {
            return getImageByFfmpegPipe(stream, streamLength, "-f webp_pipe -i pipe:0 -vframes 1 -an -vcodec bmp -f image2 pipe:1");
        }

        /// <summary>
        /// 画像を ffmpeg の標準入力に渡して BMP で受け取る（D6）。標準入力から読めなければ（古い ffmpeg など）一時ファイルで読む。
        /// その読み直しのために、シークできないストリームは先にメモリへ写す
        /// </summary>
        private static Bitmap getImageByFfmpegPipe(Stream stream, long streamLength, string arguments)
        {
            if (stream.CanSeek) return getImageByFfmpegPipeSeekable(stream, arguments);
            using (var source = StreamUtil.CopyToMemory(stream, streamLength))
            {
                return getImageByFfmpegPipeSeekable(source, arguments);
            }
        }

        private static Bitmap getImageByFfmpegPipeSeekable(Stream source, string arguments)
        {
            var position = source.Position;
            try
            {
                int outputSize;
                var buf = readStdOutBinary(ffmpegPath, "-hide_banner -loglevel error " + arguments, source, null, out outputSize);
                using (var imageStream = new MemoryStream(buf, 0, outputSize, writable: false, publiclyVisible: false))
                {
                    return new Bitmap(imageStream);
                }
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException || e is System.ComponentModel.Win32Exception || e is System.Runtime.InteropServices.ExternalException)
            {
                source.Position = position;
                return GetImageByFfmpeg(source);
            }
        }

        private static Bitmap GetThumbnail(string filePath, Size size, bool originalSize, TimeSpan position, BackgroundWorker backgroundWorker)
        {
            int outputSize;

            string cmd;
            if (position < TimeSpan.MaxValue)
            {
                // ZipPla CE: 小数点がカンマの地域（de-DE など）でも ffmpeg が読める形にする
                cmd = $"-ss {position.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{filePath}\"" + (originalSize ? "" : $" -s {size.Width}x{size.Height}") + " -vframes 1 -an -vcodec bmp -f image2 pipe:1";
            }
            else
            {
                cmd = $"-i \"{filePath}\" -vf " + (originalSize ? "" : $"scale={size.Width}x{size.Height},") + "thumbnail -vframes 1 -an -vcodec bmp -f image2 pipe:1";
            }

            byte[] buf;
#if PERFLOG
            var perfFfmpeg = PerfTrace.Now();
#endif
            {
                buf = readStdOutBinary(ffmpegPath, cmd,
                backgroundWorker, size.Width * size.Height * 4 + 124, out outputSize); // http://www.ruche-home.net/program/bmp/struct 実際には * 3 + 54 が多いが、 * 4 + 54 のケースもある
            }
#if PERFLOG
            PerfTrace.AddStage("movieFfmpeg", PerfTrace.Now() - perfFfmpeg); // ffmpeg の起動から、標準出力の BMP を読み終えるまで
            var perfBitmap = PerfTrace.Now();
#endif

            using (var ms = new MemoryStream(buf, 0, outputSize, writable: false, publiclyVisible: false))
            {
                try
                {
                    var result = new Bitmap(ms);
#if PERFLOG
                    PerfTrace.AddStage("movieBitmap", PerfTrace.Now() - perfBitmap); // BMP を Bitmap にする
#endif
                    if(result.Size != size)
                    {
                        result.Dispose();
                        throw new Exception();
                    }
                    return result;
                }
                finally
                {
                    ms.Close();
                }
            }
        }
        
        private static byte[] readStdOutBinary(string fileName, string arguments, BackgroundWorker backgroundWorker, int buffSize, out int outputSize)
        {
            var psi = new ProcessStartInfo(fileName, arguments);
            psi.CreateNoWindow = true; // コンソール・ウィンドウを開かない
            psi.UseShellExecute = false; // シェル機能を使用しない
            psi.RedirectStandardOutput = true; // 標準出力をリダイレクト
            byte[] buf = new byte[buffSize];
            outputSize = 0;
            using (var p = Process.Start(psi)) // アプリの実行開始
            {
                try
                {
                    var stream = p.StandardOutput.BaseStream;
                    while (true)
                    {
                        int read;
                        if (backgroundWorker != null)
                        {
                            using (var readTask = stream.ReadAsync(buf, outputSize, buffSize - outputSize))
                            {
                                do
                                {
                                    if (backgroundWorker.CancellationPending)
                                    {
                                        p.Kill();
                                        return null;
                                    }
                                }
                                while (!readTask.Wait(FfmpegCancelCheckInterval));
                                read = readTask.Result;
                            }
                        }
                        else
                        {
                            read = stream.Read(buf, outputSize, buffSize - outputSize);
                        }
                        if (read > 0) outputSize += read;
                        else break;
                    }
                }
                catch
                {
                    p.Kill(); // using (var p ... ではこの効果はない
                    throw;
                }
            }
            return buf;
        }

        private static byte[] readStdOutBinary(string fileName, string arguments, Stream input, BackgroundWorker backgroundWorker, out int outputSize)
        {
            var psi = new ProcessStartInfo(fileName, arguments);
            psi.CreateNoWindow = true; // コンソール・ウィンドウを開かない
            psi.UseShellExecute = false; // シェル機能を使用しない
            psi.RedirectStandardOutput = true; // 標準出力をリダイレクト
            if(input != null)
            {
                psi.RedirectStandardInput = true;
            }
            const int subBufSize = 1024 * 1024;
            var subBuf = new byte[subBufSize];
            var buf = new byte[subBufSize];
            outputSize = 0;
            using (var p = Process.Start(psi)) // アプリの実行開始
            {
                Task inputTask = null;
                try
                {
                    if (input != null)
                    {
                        inputTask = Task.Run(() =>
                        {
                            try
                            {
                                input.CopyTo(p.StandardInput.BaseStream);
                            }
                            finally
                            {
                                p.StandardInput.Close();
                            }
                        });
                    }
                    var stream = p.StandardOutput.BaseStream;
                    while (true)
                    {
                        int read;
                        if (backgroundWorker != null)
                        {
                            using (var readTask = stream.ReadAsync(subBuf, 0, subBufSize))
                            {
                                do
                                {
                                    if (backgroundWorker.CancellationPending)
                                    {
                                        p.Kill();
                                        return null;
                                    }
                                }
                                while (!readTask.Wait(FfmpegCancelCheckInterval));
                                read = readTask.Result;
                            }
                        }
                        else
                        {
                            read = stream.Read(subBuf, 0, subBufSize);
                        }
                        if (read > 0)
                        {
                            var nextOutputSize = outputSize + read;
                            if(buf.Length < nextOutputSize)
                            {
                                var nextBuf = new byte[nextOutputSize * 2];
                                Array.Copy(buf, nextBuf, outputSize);
                                buf = null;
                                buf = nextBuf;
                            }
                            Array.Copy(subBuf, 0, buf, outputSize, read);
                            outputSize = nextOutputSize;
                        }
                        else break;
                    }
                    if (inputTask != null)
                    {
                        p.WaitForExit();
                        try
                        {
                            inputTask.GetAwaiter().GetResult();
                        }
                        catch (IOException) when (p.ExitCode == 0 && outputSize > 0)
                        {
                            // ffmpeg may close stdin after it has decoded the first frame.
                        }
                        if (p.ExitCode != 0) throw new InvalidDataException("ffmpeg could not decode the piped image.");
                    }
                }
                catch
                {
                    if (!p.HasExited) p.Kill(); // using (var p ... ではこの効果はない
                    if (inputTask != null)
                    {
                        try { inputTask.Wait(); } catch { }
                    }
                    throw;
                }
            }
            return buf;
        }
        public static readonly string ffmpegPath = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "ffmpeg.exe");
        private static readonly string[] supportedExtensions = new string[] {".mp4", ".wmv", ".mov", ".avi", ".mpg", ".mpeg", ".qt", ".asf", ".3gp", ".3gpp", ".3g2", ".rmvb", ".f4v", ".ts", ".mts", ".m2ts", ".m2v", ".m4v", ".vob", ".mkv", ".ogv", ".ogm", ".flv", ".webm", ".divx" };
        public static readonly string[] SupportedImageExtensionsWithPeriodInLowerCase = new string[] { ".webp", ".jp2", ".j2k", ".jpf", ".jpx", ".jpm", ".mj2" };
        public static readonly string SupportedVideoFileFilter; // = "*.mp4;*.wmv;*.mov;*.avi;*.mpg;*.mpeg;*.3gp;*.f4v;*.m2ts;*.m2v;*.m4v;*.mkv;*.ogv;*.ogm;*.flv;*.webm";
        static MovieThumbnailLoader()
        {
            SupportedVideoFileFilter = "*" + string.Join(";*", supportedExtensions);
        }

        // ヘッダ取得・書庫の一覧・監視イベントなど多くの箇所から呼ばれるので、File.Exists の結果を短い時間だけ使い回す（K14）。
        // 実行中に ffmpeg.exe を置いたり消したりしても、この時間が過ぎれば反映される。
        // 上位を確認した時刻（Environment.TickCount）、下位 2 ビットを結果（1: あり、2: なし）にして 1 つの long で持つ（0 は未確認）
        private const int ffmpegExistsCacheMilliseconds = 2000;
        private static long ffmpegExists_State = 0;
        public static bool ffmpegExists()
        {
            var now = Environment.TickCount;
            var state = System.Threading.Interlocked.Read(ref ffmpegExists_State);
            if (state != 0 && unchecked(now - (int)(state >> 2)) < ffmpegExistsCacheMilliseconds) return (state & 3) == 1;
            var exists = File.Exists(ffmpegPath);
            System.Threading.Interlocked.Exchange(ref ffmpegExists_State, ((long)now << 2) | (exists ? 1L : 2L));
            return exists;
        }

        public static bool Supports(string path)
        {
            try
            {
                return path != null && supportedExtensions.Contains(Path.GetExtension(path).ToLower());
            }
            catch
            {
                return false;
            }
        }
        
        public static bool SupportsByExtension(string extensionWithPeriodInLowerCase)
        {
            return extensionWithPeriodInLowerCase != null && supportedExtensions.Contains(extensionWithPeriodInLowerCase);
        }


    }

}
