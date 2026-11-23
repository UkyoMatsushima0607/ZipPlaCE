using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace ZipPla
{
    /// <summary>
    /// mp4 / mov の動画の情報（大きさ・SAR・fps・長さ）を、ffmpeg を起動せずにファイルのヘッダから読む（K32）。
    /// MovieInfo.FromFile（ffmpeg の起動 1 回、約 30ms）と同じ値になる場合だけ結果を返し、
    /// 少しでも確かでないとき（H.264・HEVC 以外、VFR、断片化 mp4、表紙の画像が先にある、など）は false を返して ffmpeg に任せる。
    /// ffmpeg の表示の規則（av_dump_format・avcodec_string・print_fps）と、FromFile の読み取りの癖（小数部の 2 桁をミリ秒として読む）を再現している
    /// </summary>
    internal static class Mp4MovieInfoReader
    {
        private const long MaxMoovSize = 64L * 1024 * 1024;

        public static bool TryRead(string path, out MovieInfo info)
        {
            info = null;
            try
            {
                byte[] moov;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess))
                {
                    moov = readMoov(stream);
                }
                return moov != null && tryParseMoov(moov, out info);
            }
            catch
            {
                info = null;
                return false;
            }
        }

        // 最上位の箱をたどって moov の中身を読む。断片化（moof）なら null
        private static byte[] readMoov(Stream stream)
        {
            var length = stream.Length;
            var header = new byte[16];
            long pos = 0;
            byte[] moov = null;
            var first = true;
            while (pos + 8 <= length)
            {
                stream.Position = pos;
                if (!readFully(stream, header, 8)) return null;
                long size = readU32(header, 0);
                var type = Encoding.ASCII.GetString(header, 4, 4);
                var headerSize = 8;
                if (size == 1)
                {
                    if (!readFully(stream, header, 8, 8)) return null;
                    size = (long)readU64(header, 8);
                    headerSize = 16;
                }
                else if (size == 0) size = length - pos;
                if (size < headerSize || pos + size > length) return null;
                if (first)
                {
                    // mp4 / mov でないものは ffmpeg に任せる
                    if (type != "ftyp" && type != "moov" && type != "free" && type != "skip" && type != "wide" && type != "mdat") return null;
                    first = false;
                }
                if (type == "moof" || type == "mfra") return null;
                if (type == "moov")
                {
                    if (moov != null) return null; // moov が 2 つ
                    var bodySize = size - headerSize;
                    if (bodySize > MaxMoovSize) return null;
                    moov = new byte[bodySize];
                    if (!readFully(stream, moov, (int)bodySize)) return null;
                }
                pos += size;
            }
            return moov;
        }

        private sealed class Track
        {
            public string Handler;
            public byte[] SampleEntry;    // stsd の最初の項目（大きさと種類を含む）
            public int SampleEntryCount;
            public List<KeyValuePair<long, long>> Stts; // (count, delta)
            public long MediaTimeScale;
            public int EditCount;
            public long EditMediaTime;      // elst の最初の項目（版 0 は 32 ビットの符号付き）
            public ulong EditSegmentDuration; // ムービーの時間単位
            public bool EditRateIsOne = true;
            public List<KeyValuePair<long, long>> Ctts; // (count, offset)。なければ null
        }

        /// <summary>
        /// ffmpeg のストリームの終わり（µs）。ffmpeg の全体の長さは、各ストリームの「開始＋長さ」の最大から開始の最小を引いたもの。
        /// 開始がちょうど 0 になる作りだけ扱う。編集リストが 1 項目なら、その前のサンプルは捨てられて開始は 0（音声のプライミング、切り出し）。
        /// 編集リストがなければ、最初の表示時刻（ctts）が 0 のときだけ。
        /// ストリームの長さは「stts の合計」と「編集の長さ」の小さいほう。mvhd の長さは使われない（1/1000 秒に丸められていてずれる）。
        /// ffmpeg 8.0 の出力と、B フレーム・音声のプライミング・ストリームコピーでの切り出し・他のソフトで作られた mp4 で突き合わせて決めた規則
        /// </summary>
        private static bool tryGetTrackEndMicroseconds(Track t, long movieTimeScale, out long endUs)
        {
            endUs = 0;
            if (t.MediaTimeScale <= 0 || t.Stts == null || t.Stts.Count == 0) return false;
            if (t.EditCount > 1 || !t.EditRateIsOne) return false;
            var mediaTime = t.EditCount == 1 ? t.EditMediaTime : 0;
            if (mediaTime < 0) return false; // 空の編集（開始を遅らせる）

            long dtsEnd = 0, samples = 0;
            foreach (var e in t.Stts)
            {
                if (e.Key > int.MaxValue || e.Value > int.MaxValue) return false;
                dtsEnd += e.Key * e.Value;
                samples += e.Key;
                if (dtsEnd > long.MaxValue / 4000000) return false;
            }
            if (samples == 0) return false;
            long firstOffset = 0;
            if (t.Ctts != null)
            {
                long cttsSamples = 0;
                foreach (var c in t.Ctts) cttsSamples += c.Key;
                if (cttsSamples != samples) return false; // 表の数が合わない
                firstOffset = t.Ctts.Count > 0 ? t.Ctts[0].Value : 0;
            }
            if (t.EditCount == 0 && firstOffset != 0) return false; // 開始が 0 でない
            var duration = dtsEnd;
            if (t.EditCount == 1)
            {
                if (t.EditSegmentDuration == 0 || t.EditSegmentDuration > (ulong)(long.MaxValue / 2000000)) return false;
                var segment = ((long)t.EditSegmentDuration * t.MediaTimeScale + movieTimeScale / 2) / movieTimeScale; // av_rescale
                if (segment < duration) duration = segment;
            }
            if (duration <= 0) return false;
            endUs = (duration * 1000000 + t.MediaTimeScale / 2) / t.MediaTimeScale;
            return true;
        }

        private static bool tryParseMoov(byte[] moov, out MovieInfo info)
        {
            info = null;
            long movieTimeScale = 0;
            ulong movieDuration = 0;
            var mvhdFound = false;
            var tracks = new List<Track>();
            var coverBeforeTrack = false;
            foreach (var box in children(moov, 0, moov.Length))
            {
                if (box.Type == "mvex") return false; // 断片化
                if (box.Type == "mvhd")
                {
                    if (mvhdFound) return false;
                    mvhdFound = true;
                    var p = box.BodyStart;
                    var version = moov[p];
                    if (version == 1)
                    {
                        if (box.BodyLength < 32) return false;
                        movieTimeScale = readU32(moov, p + 20);
                        movieDuration = readU64(moov, p + 24);
                    }
                    else if (version == 0)
                    {
                        if (box.BodyLength < 20) return false;
                        movieTimeScale = readU32(moov, p + 12);
                        movieDuration = readU32(moov, p + 16);
                    }
                    else return false;
                }
                else if (box.Type == "trak")
                {
                    var track = parseTrack(moov, box.BodyStart, box.BodyLength);
                    if (track == null) return false;
                    tracks.Add(track);
                }
                else if (tracks.Count == 0 && containsAscii(moov, box.BodyStart, box.BodyLength, "covr"))
                {
                    // 表紙の画像（attached pic）は udta を読んだ時点で動画のストリームとして足されるので、トラックより前にあると最初の Video の行が変わる
                    coverBeforeTrack = true;
                }
            }
            if (!mvhdFound || coverBeforeTrack || movieTimeScale <= 0 || movieDuration == 0) return false;

            Track video = null;
            foreach (var t in tracks)
            {
                if (t.Handler != "vide") continue;
                if (video != null) return false; // 動画のトラックが 2 つ以上
                video = t;
            }
            if (video == null || video.SampleEntry == null || video.SampleEntryCount != 1 || video.Stts == null || video.MediaTimeScale <= 0) return false;

            // 長さ: ffmpeg は各ストリームの終わり（µs、四捨五入）の最大を全体の長さにし、表示で 1/100 秒に丸める。
            // 映像と音声以外のトラック（タイムコード・字幕・メタデータなど）があると計算に入る可能性があるので任せる
            long us = 0;
            foreach (var t in tracks)
            {
                if (t.Handler != "vide" && t.Handler != "soun") return false;
                if (!tryGetTrackEndMicroseconds(t, movieTimeScale, out var endUs)) return false;
                if (endUs > us) us = endUs;
            }
            if (us <= 0) return false;
            var d = us + 5000;
            var secs = d / 1000000;
            var centi = (int)((d % 1000000) * 100 / 1000000);
            var hours = secs / 3600;
            if (hours >= 100) return false; // FromFile の正規表現は 2 桁の時間しか読まない
            var duration = new TimeSpan(0, (int)hours, (int)(secs / 60 % 60), (int)(secs % 60), centi); // FromFile と同じく、1/100 秒の 2 桁をミリ秒として入れる

            // fps（tbr）: ffmpeg の mov は、stts の項目が 1 つ（または 2 つで最後が 1 サンプル）のとき時間単位 ÷ 1 フレームの長さにする
            var stts = video.Stts;
            if (!(stts.Count == 1 || (stts.Count == 2 && stts[1].Key == 1))) return false;
            var delta = stts[0].Value;
            if (delta <= 0 || stts[0].Key <= 0) return false;
            if (!tryFormatFps(video.MediaTimeScale, delta, out var fpsString)) return false;
            if (!double.TryParse(fpsString, out var fps)) return false; // FromFile と同じ読み方

            // 大きさと SAR: H.264・HEVC の SPS から（ffmpeg はデコーダの値を表示する）。SPS に SAR がなければ pasp
            var entry = video.SampleEntry;
            var format = Encoding.ASCII.GetString(entry, 4, 4);
            var isAvc = format == "avc1" || format == "avc3";
            var isHevc = format == "hvc1" || format == "hev1";
            if (!isAvc && !isHevc) return false;
            const int visualEntryHeader = 8 + 78;
            if (entry.Length < visualEntryHeader) return false;
            byte[] sps = null;
            Fraction? pasp = null;
            foreach (var box in children(entry, visualEntryHeader, entry.Length - visualEntryHeader))
            {
                if (isAvc && box.Type == "avcC") sps = firstSps(entry, box.BodyStart, box.BodyLength);
                else if (isHevc && box.Type == "hvcC") sps = firstHevcSps(entry, box.BodyStart, box.BodyLength);
                else if (box.Type == "pasp" && box.BodyLength >= 8)
                {
                    var h = readU32(entry, box.BodyStart);
                    var v = readU32(entry, box.BodyStart + 4);
                    if (h > int.MaxValue || v > int.MaxValue) return false;
                    if (h != 0 && v != 0) pasp = (Fraction)(int)h / (int)v;
                }
            }
            if (sps == null) return false;
            Size size;
            Fraction? spsSar;
            if (!(isAvc ? H264Sps.TryParse(sps, out size, out spsSar) : HevcSps.TryParse(sps, out size, out spsSar))) return false;

            info = new MovieInfo { SizeByPixel = size, FPS = fps, FPSString = fpsString, Duration = duration };
            if (spsSar.HasValue) info.SAR = spsSar.Value;
            else if (pasp.HasValue) info.SAR = pasp.Value;
            // どちらもなければ FromFile と同じく MovieInfo の初期値（0）のまま
            return true;
        }

        private static Track parseTrack(byte[] data, int start, int length)
        {
            var track = new Track();
            foreach (var box in children(data, start, length))
            {
                if (box.Type == "edts")
                {
                    foreach (var ed in children(data, box.BodyStart, box.BodyLength))
                    {
                        if (ed.Type != "elst") continue;
                        if (ed.BodyLength < 8) return null;
                        var p = ed.BodyStart;
                        var version = data[p];
                        var count = readU32(data, p + 4);
                        var entrySize = version == 1 ? 20 : 12;
                        if (version > 1 || count > (ulong)(ed.BodyLength - 8) / (ulong)entrySize) return null;
                        track.EditCount = (int)count;
                        if (count >= 1)
                        {
                            var q = p + 8;
                            if (version == 1) { track.EditSegmentDuration = readU64(data, q); track.EditMediaTime = (long)readU64(data, q + 8); }
                            else { track.EditSegmentDuration = readU32(data, q); track.EditMediaTime = (int)readU32(data, q + 4); }
                            track.EditRateIsOne = readU32(data, q + entrySize - 4) == 0x00010000;
                        }
                    }
                    continue;
                }
                if (box.Type != "mdia") continue;
                foreach (var m in children(data, box.BodyStart, box.BodyLength))
                {
                    if (m.Type == "mdhd")
                    {
                        var p = m.BodyStart;
                        var version = data[p];
                        if (version == 1) { if (m.BodyLength < 32) return null; track.MediaTimeScale = readU32(data, p + 20); }
                        else if (version == 0) { if (m.BodyLength < 20) return null; track.MediaTimeScale = readU32(data, p + 12); }
                        else return null;
                    }
                    else if (m.Type == "hdlr")
                    {
                        if (m.BodyLength < 12) return null;
                        track.Handler = Encoding.ASCII.GetString(data, m.BodyStart + 8, 4);
                    }
                    else if (m.Type == "minf")
                    {
                        foreach (var mi in children(data, m.BodyStart, m.BodyLength))
                        {
                            if (mi.Type != "stbl") continue;
                            foreach (var s in children(data, mi.BodyStart, mi.BodyLength))
                            {
                                if (s.Type == "stsd")
                                {
                                    if (s.BodyLength < 8) return null;
                                    var count = readU32(data, s.BodyStart + 4);
                                    track.SampleEntryCount = (int)Math.Min(count, int.MaxValue);
                                    var entries = children(data, s.BodyStart + 8, s.BodyLength - 8);
                                    if (entries.Count > 0)
                                    {
                                        var e = entries[0];
                                        var entry = new byte[e.BodyStart + e.BodyLength - e.Start];
                                        Array.Copy(data, e.Start, entry, 0, entry.Length);
                                        track.SampleEntry = entry;
                                    }
                                }
                                else if (s.Type == "stts")
                                {
                                    if (s.BodyLength < 8) return null;
                                    var count = readU32(data, s.BodyStart + 4);
                                    if (count > (s.BodyLength - 8) / 8) return null;
                                    track.Stts = readPairs(data, s.BodyStart + 8, (int)count, signedSecond: false);
                                }
                                else if (s.Type == "ctts")
                                {
                                    if (s.BodyLength < 8) return null;
                                    var count = readU32(data, s.BodyStart + 4);
                                    if (count > (s.BodyLength - 8) / 8) return null;
                                    track.Ctts = readPairs(data, s.BodyStart + 8, (int)count, signedSecond: true); // ffmpeg は版 0 でも符号付きで読む
                                    foreach (var c in track.Ctts) if (c.Value < 0) return null; // 負の表示時刻のずれ（dts_shift）は任せる
                                }
                            }
                        }
                    }
                }
            }
            return track;
        }

        private static List<KeyValuePair<long, long>> readPairs(byte[] data, int start, int count, bool signedSecond)
        {
            var list = new List<KeyValuePair<long, long>>(count);
            for (var i = 0; i < count; i++)
            {
                var q = start + i * 8;
                var second = readU32(data, q + 4);
                list.Add(new KeyValuePair<long, long>(readU32(data, q), signedSecond ? (int)second : (long)second));
            }
            return list;
        }

        // ffmpeg の print_fps（"%3.2f"、"%1.0f"）と同じ文字列にする。値は timeScale / delta
        private static bool tryFormatFps(long timeScale, long delta, out string text)
        {
            text = null;
            var d = (double)timeScale / delta;
            var f = (float)(d * 100); // lrintf(d * 100)
            var v = (long)Math.Round((double)f, MidpointRounding.ToEven);
            if (v <= 0) return false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (v % 100 != 0)
            {
                // 小数第 2 位に丸める（ちょうど中間のときは double の誤差で決まるので任せない）
                var num = (decimal)timeScale * 100;
                var q = Math.Floor(num / delta);
                var rem = num - q * delta;
                if (rem * 2 == delta) return false;
                if (rem * 2 > delta) q++;
                text = (q / 100).ToString("0.00", inv);
            }
            else if (v % (100 * 1000) != 0)
            {
                text = Math.Round((decimal)timeScale / delta, 0, MidpointRounding.ToEven).ToString("0", inv);
            }
            else return false; // "1k" などは FromFile が別の欄を読む
            return true;
        }

        private static byte[] firstHevcSps(byte[] data, int start, int length)
        {
            // HEVCDecoderConfigurationRecord: 22 バイトの見出し、numOfArrays、[種類(下位 6 ビット), numNalus 2 バイト, [長さ 2 バイト, NAL]...]...
            if (length < 23 || data[start] != 1) return null;
            var end = start + length;
            var p = start + 23;
            var arrays = data[start + 22];
            for (var a = 0; a < arrays; a++)
            {
                if (p + 3 > end) return null;
                var type = data[p] & 0x3F;
                var count = (data[p + 1] << 8) | data[p + 2];
                p += 3;
                for (var n = 0; n < count; n++)
                {
                    if (p + 2 > end) return null;
                    var nalLength = (data[p] << 8) | data[p + 1];
                    p += 2;
                    if (p + nalLength > end) return null;
                    if (type == 33)
                    {
                        if (nalLength < 3) return null;
                        var sps = new byte[nalLength];
                        Array.Copy(data, p, sps, 0, nalLength);
                        return sps;
                    }
                    p += nalLength;
                }
            }
            return null;
        }

        private static byte[] firstSps(byte[] data, int start, int length)
        {
            // AVCDecoderConfigurationRecord: version, profile, compat, level, lengthSizeMinusOne, numOfSPS(下位 5 ビット), [長さ 2 バイト, SPS]...
            if (length < 8 || data[start] != 1) return null;
            var count = data[start + 5] & 0x1F;
            if (count < 1) return null;
            var spsLength = (data[start + 6] << 8) | data[start + 7];
            if (spsLength < 4 || 8 + spsLength > length) return null;
            var sps = new byte[spsLength];
            Array.Copy(data, start + 8, sps, 0, spsLength);
            return sps;
        }

        private struct Box
        {
            public string Type;
            public int Start;
            public int BodyStart;
            public int BodyLength;
        }

        private static List<Box> children(byte[] data, int start, int length)
        {
            var list = new List<Box>();
            var end = start + length;
            var pos = start;
            while (pos + 8 <= end)
            {
                long size = readU32(data, pos);
                var headerSize = 8;
                if (size == 1)
                {
                    if (pos + 16 > end) throw new InvalidDataException();
                    size = (long)readU64(data, pos + 8);
                    headerSize = 16;
                }
                else if (size == 0) size = end - pos;
                if (size < headerSize || pos + size > end) throw new InvalidDataException();
                list.Add(new Box { Type = Encoding.ASCII.GetString(data, pos + 4, 4), Start = pos, BodyStart = pos + headerSize, BodyLength = (int)size - headerSize });
                pos += (int)size;
            }
            return list;
        }

        private static bool containsAscii(byte[] data, int start, int length, string text)
        {
            var end = start + length - text.Length;
            for (var i = start; i <= end; i++)
            {
                var j = 0;
                while (j < text.Length && data[i + j] == text[j]) j++;
                if (j == text.Length) return true;
            }
            return false;
        }

        private static bool readFully(Stream stream, byte[] buffer, int count, int offset = 0)
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

        private static uint readU32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        private static ulong readU64(byte[] b, int o) => (ulong)readU32(b, o) << 32 | readU32(b, o + 4);

        /// <summary>H.264 の SPS から、ffmpeg のデコーダと同じ表示の大きさ（切り抜き後）と SAR（VUI）を読む</summary>
        private static class H264Sps
        {
            public static bool TryParse(byte[] nal, out Size size, out Fraction? sar)
            {
                size = Size.Empty;
                sar = null;
                if ((nal[0] & 0x1F) != 7) return false;
                var r = new BitReader(removeEmulationPrevention(nal, 1));
                var profile = r.Bits(8);
                r.Bits(16); // constraint_set、level
                r.Ue(); // sps_id
                var chromaFormat = 1;
                var separateColourPlane = false;
                var bitDepthLuma = 8;
                if (profile == 100 || profile == 110 || profile == 122 || profile == 244 || profile == 44 || profile == 83 ||
                    profile == 86 || profile == 118 || profile == 128 || profile == 138 || profile == 139 || profile == 134 || profile == 135)
                {
                    chromaFormat = r.Ue();
                    if (chromaFormat > 3) return false;
                    if (chromaFormat == 3) separateColourPlane = r.Bit();
                    bitDepthLuma = r.Ue() + 8;
                    r.Ue(); // bit_depth_chroma
                    r.Bit(); // qpprime_y_zero_transform_bypass
                    if (r.Bit()) // seq_scaling_matrix_present
                    {
                        for (var i = 0; i < (chromaFormat == 3 ? 12 : 8); i++)
                        {
                            if (r.Bit()) skipScalingList(r, i < 6 ? 16 : 64);
                        }
                    }
                }
                r.Ue(); // log2_max_frame_num
                var pocType = r.Ue();
                if (pocType == 0) r.Ue();
                else if (pocType == 1)
                {
                    r.Bit(); r.Se(); r.Se();
                    var n = r.Ue();
                    if (n > 255) return false;
                    for (var i = 0; i < n; i++) r.Se();
                }
                else if (pocType != 2) return false;
                r.Ue(); // max_num_ref_frames
                r.Bit(); // gaps_in_frame_num_allowed
                var widthMbs = r.Ue() + 1;
                var heightMapUnits = r.Ue() + 1;
                var frameMbsOnly = r.Bit();
                if (!frameMbsOnly) r.Bit(); // mb_adaptive_frame_field
                r.Bit(); // direct_8x8_inference
                int cropLeft = 0, cropRight = 0, cropTop = 0, cropBottom = 0;
                if (r.Bit())
                {
                    cropLeft = r.Ue(); cropRight = r.Ue(); cropTop = r.Ue(); cropBottom = r.Ue();
                }
                var width = widthMbs * 16;
                var height = (frameMbsOnly ? 1 : 2) * heightMapUnits * 16;
                var chromaArrayType = separateColourPlane ? 0 : chromaFormat;
                var subWidth = chromaArrayType == 1 || chromaArrayType == 2 ? 2 : 1;
                var subHeight = chromaArrayType == 1 ? 2 : 1;
                var cropUnitX = chromaArrayType == 0 ? 1 : subWidth;
                var cropUnitY = (chromaArrayType == 0 ? 1 : subHeight) * (frameMbsOnly ? 1 : 2);
                long cl = (long)cropLeft * cropUnitX, cr = (long)cropRight * cropUnitX, ct = (long)cropTop * cropUnitY, cb = (long)cropBottom * cropUnitY;
                if (cl + cr >= width || ct + cb >= height) return false;
                // ffmpeg は左の切り抜きを、配置をそろえるために減らすことがある。その場合は任せる
                if ((cl & (0x1F >> (bitDepthLuma > 8 ? 1 : 0))) != 0) return false;
                size = new Size((int)(width - cl - cr), (int)(height - ct - cb));

                if (r.Bit()) // vui_parameters_present
                {
                    if (!tryReadVuiSar(r, out sar)) return false;
                }
                return true;
            }

            private static void skipScalingList(BitReader r, int size)
            {
                int last = 8, next = 8;
                for (var j = 0; j < size; j++)
                {
                    if (next != 0) next = (last + r.Se() + 256) % 256;
                    last = next == 0 ? last : next;
                }
            }

        }

        /// <summary>
        /// HEVC の SPS から、ffmpeg のデコーダと同じ表示の大きさ（conformance window の切り抜き後）と SAR（VUI）を読む。
        /// VUI の先頭（aspect_ratio_info）まで読めばよいが、その前の参照ピクチャセットなどは読み飛ばす必要がある
        /// </summary>
        private static class HevcSps
        {
            private static readonly int[] subWidthC = { 1, 2, 2, 1 };
            private static readonly int[] subHeightC = { 1, 2, 1, 1 };

            public static bool TryParse(byte[] nal, out Size size, out Fraction? sar)
            {
                size = Size.Empty;
                sar = null;
                if (nal.Length < 3 || ((nal[0] >> 1) & 0x3F) != 33) return false;
                var r = new BitReader(removeEmulationPrevention(nal, 2));
                r.Bits(4); // sps_video_parameter_set_id
                var maxSubLayersMinus1 = r.Bits(3);
                if (maxSubLayersMinus1 > 6) return false;
                r.Bit(); // temporal_id_nesting
                skipProfileTierLevel(r, maxSubLayersMinus1);
                if (r.Ue() > 15) return false; // sps_seq_parameter_set_id
                var chromaFormat = r.Ue();
                if (chromaFormat > 3) return false;
                if (chromaFormat == 3 && r.Bit()) chromaFormat = 0; // separate_colour_plane（ffmpeg も 0 として扱う）
                var width = r.Ue();
                var height = r.Ue();
                int left = 0, right = 0, top = 0, bottom = 0;
                if (r.Bit()) // conformance_window
                {
                    left = r.Ue(); right = r.Ue(); top = r.Ue(); bottom = r.Ue();
                }
                var bitDepth = r.Ue() + 8;
                r.Ue(); // bit_depth_chroma
                var log2MaxPocLsb = r.Ue() + 4;
                if (log2MaxPocLsb > 16) return false;
                var subLayerOrderingInfo = r.Bit();
                for (var i = subLayerOrderingInfo ? 0 : maxSubLayersMinus1; i <= maxSubLayersMinus1; i++) { r.Ue(); r.Ue(); r.Ue(); }
                r.Ue(); r.Ue(); r.Ue(); r.Ue(); // 符号化ブロック・変換ブロックの大きさ
                r.Ue(); r.Ue(); // max_transform_hierarchy_depth
                if (r.Bit() && r.Bit()) skipScalingListData(r); // scaling_list_enabled && sps_scaling_list_data_present
                r.Bit(); r.Bit(); // amp、sample_adaptive_offset
                if (r.Bit()) // pcm_enabled
                {
                    r.Bits(4); r.Bits(4); r.Ue(); r.Ue(); r.Bit();
                }
                var numShortTermRefPicSets = r.Ue();
                if (numShortTermRefPicSets > 64) return false;
                var numDeltaPocs = new int[numShortTermRefPicSets];
                for (var i = 0; i < numShortTermRefPicSets; i++)
                {
                    if (!skipShortTermRefPicSet(r, i, numDeltaPocs)) return false;
                }
                if (r.Bit()) // long_term_ref_pics_present
                {
                    var n = r.Ue();
                    if (n > 32) return false;
                    for (var i = 0; i < n; i++) { r.Bits(log2MaxPocLsb); r.Bit(); }
                }
                r.Bit(); r.Bit(); // temporal_mvp、strong_intra_smoothing

                long sw = subWidthC[chromaFormat], sh = subHeightC[chromaFormat];
                long cl = left * sw, cr = right * sw, ct = top * sh, cb = bottom * sh;
                if (width <= 0 || height <= 0 || cl + cr >= width || ct + cb >= height) return false;
                // ffmpeg は左の切り抜きを、配置をそろえるために減らすことがある。その場合は任せる
                if ((cl & (0x1F >> (bitDepth > 8 ? 1 : 0))) != 0) return false;
                size = new Size((int)(width - cl - cr), (int)(height - ct - cb));

                if (r.Bit()) // vui_parameters_present
                {
                    if (!tryReadVuiSar(r, out sar)) return false;
                }
                return true;
            }

            private static void skipProfileTierLevel(BitReader r, int maxSubLayersMinus1)
            {
                r.Skip(88); // general_profile_space 〜 general_inbld/reserved
                r.Bits(8); // general_level_idc
                var profilePresent = new bool[maxSubLayersMinus1];
                var levelPresent = new bool[maxSubLayersMinus1];
                for (var i = 0; i < maxSubLayersMinus1; i++) { profilePresent[i] = r.Bit(); levelPresent[i] = r.Bit(); }
                if (maxSubLayersMinus1 > 0) for (var i = maxSubLayersMinus1; i < 8; i++) r.Bits(2);
                for (var i = 0; i < maxSubLayersMinus1; i++)
                {
                    if (profilePresent[i]) r.Skip(88);
                    if (levelPresent[i]) r.Bits(8);
                }
            }

            private static void skipScalingListData(BitReader r)
            {
                for (var sizeId = 0; sizeId < 4; sizeId++)
                {
                    for (var matrixId = 0; matrixId < 6; matrixId += sizeId == 3 ? 3 : 1)
                    {
                        if (!r.Bit()) r.Ue(); // scaling_list_pred_matrix_id_delta
                        else
                        {
                            var coefNum = Math.Min(64, 1 << (4 + (sizeId << 1)));
                            if (sizeId > 1) r.Se(); // dc
                            for (var i = 0; i < coefNum; i++) r.Se();
                        }
                    }
                }
            }

            private static bool skipShortTermRefPicSet(BitReader r, int index, int[] numDeltaPocs)
            {
                if (index != 0 && r.Bit()) // inter_ref_pic_set_prediction
                {
                    r.Bit(); // delta_rps_sign
                    r.Ue(); // abs_delta_rps_minus1
                    var refCount = numDeltaPocs[index - 1]; // SPS の中では delta_idx_minus1 は 0
                    var count = 0;
                    for (var j = 0; j <= refCount; j++)
                    {
                        var used = r.Bit();
                        var useDelta = used || r.Bit();
                        if (useDelta) count++;
                    }
                    numDeltaPocs[index] = count;
                }
                else
                {
                    var negative = r.Ue();
                    var positive = r.Ue();
                    if (negative > 16 || positive > 16) return false;
                    for (var i = 0; i < negative + positive; i++) { r.Ue(); r.Bit(); }
                    numDeltaPocs[index] = negative + positive;
                }
                return true;
            }
        }

        // H.264・HEVC の VUI の先頭の aspect_ratio_info を読む（ffmpeg の ff_h2645_decode_common_vui_params と同じ表）。
        // 表にない番号は ffmpeg が警告だけ出して読み進めるが、ここでは任せる
        private static readonly int[,] pixelAspect = {
            { 0, 1 }, { 1, 1 }, { 12, 11 }, { 10, 11 }, { 16, 11 }, { 40, 33 }, { 24, 11 }, { 20, 11 }, { 32, 11 },
            { 80, 33 }, { 18, 11 }, { 15, 11 }, { 64, 33 }, { 160, 99 }, { 4, 3 }, { 3, 2 }, { 2, 1 } };

        private static bool tryReadVuiSar(BitReader r, out Fraction? sar)
        {
            sar = null;
            if (!r.Bit()) return true; // aspect_ratio_info_present
            var idc = r.Bits(8);
            int num, den;
            if (idc == 255) { num = r.Bits(16); den = r.Bits(16); if (num == 0 || den == 0) return false; }
            else if (idc < pixelAspect.GetLength(0)) { num = pixelAspect[idc, 0]; den = pixelAspect[idc, 1]; }
            else return false;
            if (num != 0) sar = (Fraction)num / den;
            return true;
        }

        private static byte[] removeEmulationPrevention(byte[] nal, int start)
        {
            var result = new List<byte>(nal.Length);
            var zeros = 0;
            for (var i = start; i < nal.Length; i++)
            {
                var b = nal[i];
                if (zeros >= 2 && b == 3) { zeros = 0; continue; }
                zeros = b == 0 ? zeros + 1 : 0;
                result.Add(b);
            }
            return result.ToArray();
        }

        private sealed class BitReader
        {
            private readonly byte[] data;
            private long pos;
            public BitReader(byte[] data) { this.data = data; }
            public bool Bit()
            {
                if (pos >= (long)data.Length * 8) throw new InvalidDataException();
                var b = (data[pos >> 3] >> (7 - (int)(pos & 7))) & 1;
                pos++;
                return b != 0;
            }
            public int Bits(int n)
            {
                var v = 0;
                for (var i = 0; i < n; i++) v = (v << 1) | (Bit() ? 1 : 0);
                return v;
            }
            public void Skip(int n)
            {
                if (pos + n > (long)data.Length * 8) throw new InvalidDataException();
                pos += n;
            }
            public int Ue()
            {
                var zeros = 0;
                while (!Bit()) { if (++zeros > 31) throw new InvalidDataException(); }
                if (zeros == 0) return 0;
                var v = ((1L << zeros) - 1) + Bits(zeros);
                if (v > int.MaxValue) throw new InvalidDataException();
                return (int)v;
            }
            public int Se()
            {
                var k = Ue();
                return (k & 1) != 0 ? (k + 1) / 2 : -(k / 2);
            }
        }
    }
}
