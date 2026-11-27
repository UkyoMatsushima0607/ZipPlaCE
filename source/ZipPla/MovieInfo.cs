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
    public class MovieInfo
    {
        private static readonly Regex sizePattern = new Regex(@"\bVideo\s*:(?:[^:]|\d:)*?([1-9]\d{0,8})x([1-9]\d{0,8})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex sarPattern = new Regex(@"\bVideo\s*:(?:[^:]|\d:)*?\b(?:SAR\s*(\d+):(\d+)|DAR\s*(\d+):(\d+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex fpsPattern = new Regex(@"\bVideo\s*:(?:(?:[^:]|\d:)*?(?:(\d+|\d+\.\d*|\d*\.\d+)\s*tbr|(\d+|\d+\.\d*|\d*\.\d+)\s*fps)\b)+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex duraPattern = new Regex(@"\bDuration\s*:\s*(\d\d):(\d\d):(\d\d)(?:\.(\d*))?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        public static MovieInfo FromData(byte[] data)
        {
            using (var m = new MemoryStream(data))
            using (var r = new BinaryReader(m, Encoding.ASCII))
            {
                if (!r.ReadChars(5).SequenceEqual("MOVIE")) throw new Exception("Not movie data");
                return new MovieInfo() { SAR = new Fraction(r.ReadInt32(), r.ReadInt32()), SizeByPixel = new Size(r.ReadInt32(), r.ReadInt32()), FPS = r.ReadDouble(), FPSString = ReadString(r), Duration = new TimeSpan(r.ReadInt64()) };
            }
        }

        private static string ReadString(BinaryReader r)
        {
            var result = "";
            while(true) // EndOfStreamException が投げられるので無限ループはない
            {
                var c = r.ReadChar();
                if (c == '\0') return result;
                result += c;
            }
        }

        public byte[] ToData()
        {
            using (var m = new MemoryStream())
            using (var w = new BinaryWriter(m, Encoding.ASCII))
            {
                w.Write("MOVIE".ToArray());

                w.Write(SAR.Numerator);
                w.Write(SAR.Denominator);
                w.Write(SizeByPixel.Width);
                w.Write(SizeByPixel.Height);
                w.Write(FPS);
                w.Write(FPSString.ToArray());
                w.Write('\0');
                w.Write(Duration.Ticks);
                return m.ToArray();
            }
        }

        // FromFile（ffmpeg の起動 1 回、約 30〜40ms）の結果を、ファイルのサイズと更新日時が変わらない間だけ覚えておく。
        // カタログはヘッダ取得とサムネイル作成でそれぞれ動画の情報を取っていたので、2 回目を省く
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Tuple<long, DateTime, MovieInfo>> fromFileCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Tuple<long, DateTime, MovieInfo>>(StringComparer.OrdinalIgnoreCase);
        private const int FromFileCacheCapacity = 4096;

        public static MovieInfo FromFileCached(string path)
        {
            long length;
            DateTime lastWrite;
            try
            {
                var fileInfo = new FileInfo(path);
                length = fileInfo.Length;
                lastWrite = fileInfo.LastWriteTimeUtc;
            }
            catch
            {
                return FromFile(path);
            }
            if (fromFileCache.TryGetValue(path, out var cached) && cached.Item1 == length && cached.Item2 == lastWrite)
            {
                return cached.Item3.Clone();
            }
            // mp4 / mov の H.264・HEVC なら、ffmpeg を起動せずにヘッダから同じ値を読む（K32）。確かでないときは ffmpeg
            if (!Mp4MovieInfoReader.TryRead(path, out var result)) result = FromFile(path);
            if (fromFileCache.Count >= FromFileCacheCapacity) fromFileCache.Clear();
            fromFileCache[path] = Tuple.Create(length, lastWrite, result.Clone());
            return result;
        }

        public MovieInfo Clone() => new MovieInfo { SAR = SAR, SizeByPixel = SizeByPixel, FPS = FPS, FPSString = FPSString, Duration = Duration };

        public static MovieInfo FromFile(string path)
        {
            var psi = new ProcessStartInfo(MovieThumbnailLoader.ffmpegPath, $"-i \"{path}\"");
            
            psi.CreateNoWindow = true; // コンソール・ウィンドウを開かない
            psi.UseShellExecute = false; // シェル機能を使用しない

            psi.RedirectStandardError = true; // 標準エラー出力をリダイレクト
            psi.StandardErrorEncoding = Encoding.UTF8;

            using (var p = Process.Start(psi)) // アプリの実行開始
            {
                try
                {
                    var output = p.StandardError.ReadToEnd(); // 標準出力の読み取り

                    var result = new MovieInfo();
                    var sizeMatch = sizePattern.Match(output);
                    if (sizeMatch.Success)
                    {
                        var g = sizeMatch.Groups;
                        result.SizeByPixel = new Size(int.Parse(g[1].Value), int.Parse(g[2].Value));
                    }
                    var sarMatch = sarPattern.Match(output);
                    if(sarMatch.Success)
                    {
                        int hori, vert;
                        var g = sarMatch.Groups;
                        if (int.TryParse(g[1].Value, out hori) && int.TryParse(g[2].Value, out vert)) result.SAR = (Fraction)hori / vert;
                        else if (int.TryParse(g[3].Value, out hori) && int.TryParse(g[4].Value, out vert)) result.SAR = (Fraction)(hori * result.SizeByPixel.Height) / (vert * result.SizeByPixel.Width);
                    }
                    var fpsMatch = fpsPattern.Match(output);
                    if(fpsMatch.Success)
                    {
                        double d;
                        // ZipPla CE: ffmpeg は小数点に常に . を使うので、地域の設定によらずに読む
                        if(double.TryParse(fpsMatch.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
                        {
                            result.FPSString = fpsMatch.Groups[1].Value;
                            result.FPS = d;
                        }
                        else if(double.TryParse(fpsMatch.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
                        {
                            result.FPSString = fpsMatch.Groups[2].Value;
                            result.FPS = d;
                        }
                    }
                    var duraMatch = duraPattern.Match(output);
                    if (duraMatch.Success)
                    {
                        var g = duraMatch.Groups;
                        var g4 = g[4].Value;
                        int miliSec;
                        if (string.IsNullOrEmpty(g4)) miliSec = 0;
                        else miliSec = int.Parse((g4 + "00").Substring(0, 3));
                        result.Duration = new TimeSpan(0, int.Parse(g[1].Value), int.Parse(g[2].Value), int.Parse(g[3].Value), int.Parse(g[4].Value));
                    }

                    return result;
                }
                catch
                {
                    p.Kill(); // using (var p ... ではこの効果はない
                    throw;
                }
            }
        }

        public static readonly string ToShortStringTemplate = "WQVGA / " + TimeSpan.Zero.ToString(@"mm\:ss");
        public string ToShortString()
        {
            if (Duration != default(TimeSpan))
            {
                if(SizeByPixel == default(Size))
                {
                    return Duration.ToString(@"hh\:mm\:ss");
                }
                else
                {
                    var ssn = GetSizeShortName(SizeByPixel);
                    if(string.IsNullOrEmpty(ssn))
                    {
                        return Duration.ToString(@"h\:mm\:ss");
                    }
                    else if(Duration < TimeSpan.FromHours(1))
                    {
                        return $"{ssn} / " + Duration.ToString(@"mm\:ss");
                    }
                    else
                    {
                        return $"{ssn} / " + Duration.ToString(@"h\:mm");
                    }
                }
            }
            else if (SizeByPixel != default(Size))
            {
                return $"{SizeByPixel.Width}x{SizeByPixel.Height}";
            }
            else
            {
                return "";
            }
        }

        public string ToLongString()
        {
            var result = "";
            if(SizeByPixel != default(Size))
            {
                if (result != "") result += ", ";
                var ssn = GetSizeShortName(SizeByPixel);
                if(!(SizeByPixel.Width < 100 && SizeByPixel.Height < 100) && !string.IsNullOrEmpty(ssn))
                {
                    result += $"{ssn}({SizeByPixel.Width}x{SizeByPixel.Height})";
                }
                else
                {
                    result += $"{SizeByPixel.Width}x{SizeByPixel.Height}";
                }
            }
            if (SAR != 0)
            {
                if (result != "") result += ", ";
                result += $"SAR {SAR.Numerator}:{SAR.Denominator}";
            }
            if (FPS >= 0)
            {
                if (result != "") result += ", ";
                result += FPSString;
            }
            if (Duration != default(TimeSpan))
            {
                if (result != "") result += ", ";
                result += Duration.ToString(@"h\:mm\:ss\.") + Duration.Milliseconds;
            }
            return result;
        }

        private static readonly Tuple<Size, string>[] MajorSizeTable = new Tuple<Size, string>[]
        {
            Tuple.Create(new Size(320, 240), "QVGA"),
            Tuple.Create(new Size(480, 320), "HVGA"),
            Tuple.Create(new Size(640, 480), "VGA"),
            Tuple.Create(new Size(800, 600), "SVGA"),
            Tuple.Create(new Size(1024, 768), "XGA"),
            Tuple.Create(new Size(1280, 720), "HD"),
            Tuple.Create(new Size(1280, 720), "HD"),
            Tuple.Create(new Size(1600, 1200), "UXGA"),
            Tuple.Create(new Size(1920, 1080), "FHD"),
            Tuple.Create(new Size(2560, 1440), "WQHD"),
            Tuple.Create(new Size(3840, 2160), "4K"),
            Tuple.Create(new Size(8192, 4320), "8K"),
        };
        private string GetSizeShortName(Size size)
        {
            // http://pen64.com/document/resolution.html
            // https://ja.wikipedia.org/wiki/%E7%94%BB%E9%9D%A2%E8%A7%A3%E5%83%8F%E5%BA%A6
            // https://jp.gopro.com/support/articles/video-resolution-settings-and-format
            // http://www.quel.jp/etc/monitor-size/
            if (size.Width == 160 && size.Height == 144) return "GB";
            else if (size.Width == 240 && size.Height == 160) return "GBA";
            else if (size.Width == 256 && size.Height == 192) return "DS";
            else if (size.Width == 320 && size.Height == 240) return "QVGA";
            else if (size.Width == 400 && size.Height == 240) return "WQVGA";
            else if (size.Width == 480 && size.Height == 272) return "PSP";
            else if (size.Width == 352 && size.Height == 240) return "SIF";
            else if (size.Width == 352 && size.Height == 288) return "CIF";
            else if (size.Width == 480 && size.Height == 320) return "HVGA";
            else if (size.Width == 640 && size.Height == 480) return "VGA";
            else if (size.Width == 720 && size.Height == 480) return "SD";
            else if (size.Width == 720 && size.Height == 486) return "NTSC";
            else if (size.Width == 720 && size.Height == 540) return "NTSC"; // 同名
            else if (size.Width == 720 && size.Height == 576) return "PAL";
            else if (size.Width == 800 && size.Height == 480) return "WVGA";
            else if (size.Width == 848 && size.Height == 480) return "WVGA"; // 同名
            else if (size.Width == 854 && size.Height == 480) return "WiiU";
            else if (size.Width == 960 && size.Height == 544) return "Vita";
            else if (size.Width == 800 && size.Height == 600) return "SVGA";
            else if (size.Width == 940 && size.Height == 640) return "iPhn4";
            else if (size.Width == 1136 && size.Height == 640) return "iPhn5";
            else if (size.Width == 1024 && size.Height == 768) return "XGA";
            else if (size.Width == 1280 && size.Height == 720) return "HD";
            else if (size.Width == 1366 && size.Height == 768) return "FWXGA";
            else if (size.Width == 1024 && size.Height == 576) return "WSVGA";
            else if (size.Width == 1024 && size.Height == 600) return "WSVGA"; // 同名
            else if (size.Width == 1200 && size.Height == 600) return "WSVGA"; // 同名
            else if (size.Width == 1280 && size.Height == 800) return "WXGA";
            else if (size.Width == 1440 && size.Height == 900) return "WXGA+";
            else if (size.Width == 1440 && size.Height == 1080) return "DTTB";
            else if (size.Width == 1600 && size.Height == 1200) return "UXGA";
            else if (size.Width == 1920 && size.Height == 1080) return "FHD";
            else if (size.Width == 1920 && size.Height == 1200) return "WUXGA";
            else if (size.Width == 2560 && size.Height == 1440) return "WQHD";
            else if (size.Width == 2560 && size.Height == 1600) return "WQXGA";
            else if (size.Width == 3840 && size.Height == 2160) return "4K";
            else if (size.Width == 8192 && size.Height == 4320) return "8K";

            else if (size.Width == 8192 / 2 && size.Height == 2160) return "4K'";
            else if (size.Width == 3840 * 2 && size.Height == 4320) return "8K'";

            else if (size.Width < 100 && size.Height < 100) return $"{size.Width}x{size.Height}";

            else if (size.Width <= 320 && size.Height <= 320) return "Watch";

            else if (size.Width < 1000 && size.Width == size.Height) return $"{size.Width}^2";

            else if (size.Width >= 8192 * 3 / 2 && size.Height >= 4320 * 3 / 2) return $"{(int)Math.Round(Math.Sqrt((double)size.Width * size.Height) / (8192 * 4320 / 64))}K";

            else
            {
                double minLogDistance = double.PositiveInfinity;
                string nearestSize = "";
                foreach(var tpl in MajorSizeTable)
                {
                    var dw = Math.Log((double)tpl.Item1.Width / size.Width);
                    var dh = Math.Log((double)tpl.Item1.Height / size.Height);
                    var logDistance = dw * dw + dh * dh;
                    if(logDistance < minLogDistance)
                    {
                        minLogDistance = logDistance;
                        nearestSize = tpl.Item2;
                    }
                }
                return "~" + nearestSize;
            }
        }

        public Fraction SAR = 0;
        public Size SizeByPixel;
        public double FPS = -1;
        public string FPSString = "";
        public TimeSpan Duration;
    }
}
