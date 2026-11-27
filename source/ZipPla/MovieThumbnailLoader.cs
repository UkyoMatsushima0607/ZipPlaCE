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

        public static Bitmap GetThumbnail(string filePath, Func<Fraction, Size, Size> sizeProvider, Func<TimeSpan, TimeSpan> positionProvider, BackgroundWorker backgroundWorker, TimeSpan timeOut, ref MovieInfo movieInfo)
        {
            if(movieInfo == null) movieInfo = MovieInfo.FromFileCached(filePath);
            var size = movieInfo.SizeByPixel;
            if (size.IsEmpty) throw new Exception();
            bool originalSize;
            //if(!size.IsEmpty)
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
            return GetThumbnail(filePath, size, originalSize, position, backgroundWorker);
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

        //private static readonly object GetThumbnail_ffmpegLocker = new object();
        private static Bitmap GetThumbnail(string filePath, Size size, bool originalSize, TimeSpan position, BackgroundWorker backgroundWorker)
        {
            int outputSize;
            //var buf = readStdOutBinary(ffmpegPath, $"-ss {position.TotalSeconds} -i \"{filePath}\"" + (size.IsEmpty ? "" : $" -sws_flags fast_bilinear -s {size.Width}x{size.Height}") + " -vframes 1 -vcodec bmp -f image2 pipe:1",

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
            //lock (GetThumbnail_ffmpegLocker)
            {
                buf = readStdOutBinary(ffmpegPath, cmd,
                backgroundWorker, size.Width * size.Height * 4 + 124, out outputSize); // http://www.ruche-home.net/program/bmp/struct 実際には * 3 + 54 が多いが、 * 4 + 54 のケースもある
            }

            using (var ms = new MemoryStream(buf, 0, outputSize, writable: false, publiclyVisible: false))
            {
                try
                {
                    var result = new Bitmap(ms);
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
#if SCREENCAPTCHA
        public static readonly string ffmpegPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath)), "General\\ffmpeg.exe");
#else
        public static readonly string ffmpegPath = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "ffmpeg.exe");
#endif
        private static readonly string[] supportedExtensions = new string[] {".mp4", ".wmv", ".mov", ".avi", ".mpg", ".mpeg", ".qt", ".asf", ".3gp", ".3gpp", ".3g2", ".rmvb", ".f4v", ".ts", ".mts", ".m2ts", ".m2v", ".m4v", ".vob", ".mkv", ".ogv", ".ogm", ".flv", ".webm", ".divx" };
        public static readonly string[] SupportedImageExtensionsWithPeriodInLowerCase = new string[] { ".webp", ".jp2", ".j2k", ".jpf", ".jpx", ".jpm", ".mj2" };
        //private static readonly Regex reSupports; // = new Regex(@"\.(mp4|wmv|mov|avi|mpe?g|3gp|f4v|m2ts|m2v|m4v|mkv|ogv|ogm|flv|webm)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public static readonly string SupportedVideoFileFilter; // = "*.mp4;*.wmv;*.mov;*.avi;*.mpg;*.mpeg;*.3gp;*.f4v;*.m2ts;*.m2v;*.m4v;*.mkv;*.ogv;*.ogm;*.flv;*.webm";
        static MovieThumbnailLoader()
        {
            //reSupports = new Regex(@"\.(?:" + string.Join("|", supportedExtensions) + ")$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
            //return path != null && reSupports.IsMatch(path);
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
