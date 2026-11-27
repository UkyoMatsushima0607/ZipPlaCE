using Alteridem.WinTouch;
using Common;
using GenerarClasses;
using SharpCompress.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Permissions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WilsonProgramming;
using WindowsFormUtilities;

namespace ZipPla
{
#if PERFLOG
    // 調査用（PERFLOG 定数を付けたビルドのみ）。カタログのサムネイル読み込みの範囲と件数を exe のフォルダの perf-trace.log に書く
    internal static class PerfTrace
    {
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static int queued, started, cancelled;
        private static int lastStart = -1, lastStop = -1;
        private static readonly object fileLock = new object();
        public static void Write(string line)
        {
            lock (fileLock)
            {
                PerfLog.Append("perf-trace.log", $"{clock.ElapsedMilliseconds,7}\t{line}");
            }
        }
        private static long rangeChangedAt;
        private static bool readyLogged = true;
        // 読み込み範囲が変わったときだけ記録する
        public static void LoadRange(int start, int stop, string detail)
        {
            if (start == lastStart && stop == lastStop) return;
            lastStart = start; lastStop = stop;
            rangeChangedAt = clock.ElapsedMilliseconds; readyLogged = false;
            Write($"range [{start}, {stop}) count={Math.Max(0, stop - start)} {detail} queued={queued} started={started} cancelled={cancelled}");
        }
        // 読み込み範囲のサムネイルが全部揃ったら、範囲が変わってからの時間を1回だけ記録する
        public static void CheckRangeReady(bool ready, Func<int> countLoadedImages)
        {
            if (!ready || readyLogged) return;
            readyLogged = true;
            Write($"range ready {clock.ElapsedMilliseconds - rangeChangedAt} ms after range change [{lastStart}, {lastStop}) queued={queued} started={started} cancelled={cancelled} imagesHeld={countLoadedImages()}");
            WriteDrawSummary();
        }
        // UI スレッドでの描画時間（DrawItem を直接呼んだもの＝サムネイル到着時など、OnPaint）
        private static long directDraws, directDrawTicks, maxDirectDrawTicks, paints, paintTicks, maxPaintTicks, generates, generateTicks;
        public static void AddGenerate(long ticks) { generates++; generateTicks += ticks; }
        // サムネイル生成の段階ごとの時間（スレッドをまたぐので lock する）
        private static readonly Dictionary<string, long[]> stageTicks = new Dictionary<string, long[]>();
        public static void AddStage(string name, long ticks)
        {
            lock (stageTicks)
            {
                long[] v;
                if (!stageTicks.TryGetValue(name, out v)) stageTicks[name] = v = new long[2];
                v[0]++; v[1] += ticks;
            }
        }
        public static string StageSummary()
        {
            lock (stageTicks) return string.Join(" ", stageTicks.Select(p => $"{p.Key}={p.Value[0]}x{Ms(p.Value[1]) / Math.Max(1, p.Value[0]):F1}ms"));
        }
        public static long Now() => Stopwatch.GetTimestamp();
        public static void AddDirectDraw(long ticks) { directDraws++; directDrawTicks += ticks; if (ticks > maxDirectDrawTicks) maxDirectDrawTicks = ticks; }
        public static void AddPaint(long ticks) { paints++; paintTicks += ticks; if (ticks > maxPaintTicks) maxPaintTicks = ticks; }
        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        public static void WriteDrawSummary()
        {
            Write($"draw summary: directDraws={directDraws} total={Ms(directDrawTicks):F1}ms avg={(directDraws > 0 ? Ms(directDrawTicks) / directDraws : 0):F3}ms max={Ms(maxDirectDrawTicks):F2}ms" +
                $" | paints={paints} total={Ms(paintTicks):F1}ms avg={(paints > 0 ? Ms(paintTicks) / paints : 0):F3}ms max={Ms(maxPaintTicks):F2}ms" +
                $" | generates={generates} avg={(generates > 0 ? Ms(generateTicks) / generates : 0):F1}ms | stages: {StageSummary()}");
        }
        public static void ThumbnailQueued() { Interlocked.Increment(ref queued); }
        public static void ThumbnailCancelled() { Interlocked.Increment(ref cancelled); }
        public static void ThumbnailStarted()
        {
            var s = Interlocked.Increment(ref started);
            if (s % 100 == 0) Write($"thumbnails started={s} queued={queued} cancelled={cancelled}");
        }
    }
#endif
}
