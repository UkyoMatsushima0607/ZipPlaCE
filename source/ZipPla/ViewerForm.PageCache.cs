using Alteridem.WinTouch;
using Common;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZipPla
{
    public partial class ViewerForm
    {
        private Size GetResizedSize(Size originalSize)
        {
            var viewSize = ViewSize;

            var error = originalSize.Width <= 0;

            var oWidth = Math.Abs(originalSize.Width);
            var oHeight = Math.Abs(originalSize.Height);

            // 充填率を最大化する実装
            var sw = (ulong)oWidth;
            var sh = (ulong)oHeight;
            var dw = (ulong)ViewSize.Width;
            var dh = (ulong)ViewSize.Height;
            if (sw * dh > sh * dw)
            {
                if (ViewerMode != ViewerModeEnum.CoverSetting)
                {
                    // 分割
                    if (BindingMode != BindingMode.SinglePage && BindingMode != BindingMode.SinglePageWithoutScalingUp && MaxDivision > 1)
                    {
                        var n = sw * dh;
                        var d = dw * sh;
                        var r = n / d;
                        if (d * d * r * (r + 1) <= n * n)
                        {
                            r++;
                        }

                        viewSize.Width *= Math.Min(MaxDivision, (int)r);
                    }
                }
            }
            else
            {
                // 見開き
                var minPage = Math.Min(MinPageCountInWindow, MaxPageCountInWindow);
                if (minPage > 1)
                {
                    var n = dw * sh;
                    var d = sw * dh;
                    var r = n / d;
                    if (d * d * r * (r + 1) <= n * n)
                    {
                        r++;
                    }

                    viewSize.Width /= Math.Min(minPage, (int)r);
                }
            }

            // 強制見開きページ用
            

            Size result;

            if (oWidth * viewSize.Height > oHeight * viewSize.Width)
            {
                result = new Size(viewSize.Width, Math.Max(1, ((viewSize.Width * oHeight) + (oWidth >> 1)) / oWidth));
            }
            else
            {
                result = new Size(Math.Max(1, ((viewSize.Height * oWidth) + (oHeight >> 1)) / oHeight), viewSize.Height);
            }

            if (error || NoScaling(BindingMode))
            {
                return result;
            }
            else
            {
                if (result.Width >= oWidth && result.Height >= oHeight)
                {
                    result = new Size(oWidth, oHeight);
                }

                return result;
            }
        }

        int bmwLoadEachPage_EachRunWorkerCompleted_PrevPage = -1;

        // 作業セットが片付けられた後に終わったジョブの結果（完了イベントは来ない）を捨てる（X7）
        private void bmwLoadEachPage_InconclusiveWorkerDisposing(object sender, GenerarClasses.BackgroundMultiWorker.EachRunWorkerCompletedEventArgs e)
        {
            (e.Result as VirtualBitmapEx)?.Dispose();
        }

        private void bmwLoadEachPage_EachRunWorkerCompleted(object sender, GenerarClasses.BackgroundMultiWorker.EachRunWorkerCompletedEventArgs e)
        {
#if PERFLOG
            var perfHandlerStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                bmwLoadEachPage_EachRunWorkerCompletedCore(sender, e);
            }
            finally
            {
                var perfHandlerEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                PerfLog.Append("viewer-load.log", $"handler={e.WorkNumber}\thandlerStart={PerfMs(perfHandlerStart):F2}\thandlerEnd={PerfMs(perfHandlerEnd):F2}");
            }
        }
        private void bmwLoadEachPage_EachRunWorkerCompletedCore(object sender, GenerarClasses.BackgroundMultiWorker.EachRunWorkerCompletedEventArgs e)
        {
#endif
            loadedPagesSinceGc++;
            lastPageLoadedTick = Environment.TickCount;
            Program.AlertError(e.Error);
            VirtualBitmapEx result;
            if (!e.Cancelled && (result = e.Result as VirtualBitmapEx) != null)
            {
                if (currentPage >= 0 && bmwLoadEachPage_EachRunWorkerCompleted_PrevPage != currentPage)
                {
                    SetBackgroundMode();
                }
                else
                {
                    SetBackgroundModeIfPausing();
                }
                bmwLoadEachPage_EachRunWorkerCompleted_PrevPage = currentPage;
                if (SetNewResizedImage(e.WorkNumber, result))
                {
                    if (currentPage <= e.WorkNumber && e.WorkNumber < currentPage + (onePageModeForNext ? 1 : MaxPageCountInWindow))
                    {
                        showCurrentPage(false);
                    }
                }
                else
                {
                    var stop = Math.Min(ResizedImageArray.Length, currentPage + MaxPageCountInWindow);
                    var loadedRequired = true;
                    for (var i = Math.Max(0, currentPage - MaxPageCountInWindow); i < stop; i++)
                    {
                        if (ResizedImageArray[i] == null)
                        {
                            loadedRequired = false;
                            break;
                        }
                    }
                    if (loadedRequired) bmwLoadEachPage.ThreadCount = 0;
                }
            }
            else
            {
                bmwLoadEachPage_EachRunWorkerCompleted_PrevPage = -1;
                // キャンセルされたジョブの縮小画像（1 枚 2〜6MB）はどこにも持たれないので捨てる（X7）
                if (e.Cancelled) (e.Result as VirtualBitmapEx)?.Dispose();
            }

            if (SetCurrentImageLongPath_Que) SetCurrentImageLongPath();
        }

        private ulong usedMemory = 0;

        ulong BuiltInViewerMemoryLimit = ulong.MaxValue;

        ulong usedMemoryUBound = 0;

        Microsoft.VisualBasic.Devices.ComputerInfo computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        }

        [System.Security.SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll")]
        private static extern bool K32GetProcessMemoryInfo(IntPtr hProcess, out PROCESS_MEMORY_COUNTERS counters, uint cb);

        [System.Security.SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        // 自分のワーキングセット。Process.GetCurrentProcess().WorkingSet64 は全プロセスの情報を集めるので 1 回 0.7ms 前後かかる。
        // こちらは 1µs 未満（V5）。1 ページ読むたびと 0.5 秒ごとに呼ばれる
        private static long GetCurrentWorkingSet()
        {
            if (K32GetProcessMemoryInfo(GetCurrentProcess(), out var counters, (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS))))
            {
                return (long)counters.WorkingSetSize.ToUInt64();
            }
            return Environment.WorkingSet;
        }

        private void SetMemoryUBound()
        {
            var memory = Math.Max(1, GetCurrentWorkingSet());
            var available = computerInfo.AvailablePhysicalMemory;
            var systemAvailable = available;
            var userUbound = GetUserMemoryUBound();
            if (available > userUbound) available = userUbound;
            var coef = ActiveForm == this ? 0.7 : 0.3;

            // 以前の式は使用量そのものに比例するので、使用量が 0 だと上限も 0 になり（自己参照、B6）、整数除算で空きの比も切り捨てていた。
            // 比は実数で求め、下限（物理メモリの 1/16 × 係数）と上限（物理メモリの 1/4。ユーザー設定の上限が小さければそちら）で挟む（V5）
            var total = computerInfo.TotalPhysicalMemory;
            var dynamicBound = coef * usedMemory * (1 + (double)available / memory);
            var floor = coef * Math.Min(userUbound, total / 16);
            // 下限も空き物理メモリで抑える（Y17）。以前は空きがなくても下限（16GB の PC で前面なら約 700MB）までは画像を持ち続け、
            // ほかのアプリがメモリを使っているときにページングを招いていた。いま持っている分と空きの合計の半分までにする
            floor = Math.Min(floor, 0.5 * ((double)usedMemory + systemAvailable));
            var ceiling = (double)Math.Min(userUbound, total / 4);
            usedMemoryUBound = (ulong)Math.Min(Math.Max(dynamicBound, floor), ceiling);
        }

        private ulong GetUserMemoryUBound_SystemUBound = 0;

        private ulong GetUserMemoryUBound()
        {
            if (GetUserMemoryUBound_SystemUBound <= 0)
            {
                GetUserMemoryUBound_SystemUBound = Math.Min(Environment.Is64BitProcess  ? (1UL << 60) : (3UL << 30), computerInfo.TotalPhysicalMemory);
            }
            return Math.Min(GetUserMemoryUBound_SystemUBound, BuiltInViewerMemoryLimit);
        }

        private bool SetNewResizedImage(int idx, VirtualBitmapEx image)
        {
            SetMemoryUBound();
            var size = GetImageSizeInByte(image);
            var existingImageCount = ReduceUsingMemory(usedMemoryUBound - Math.Min(usedMemoryUBound, size), idx); // 符号なしなので値が負になる計算を含まないように注意
            //var existingImageCount = ReduceUsingMemory(0, idx); // テスト用

            // 原寸を読み直したページ（C1）では縮小画像が既にあるので差し替える。差し替えは常に受け入れる（拒否すると読み直しが繰り返される）
            var old = ResizedImageArray?[idx];
            if (ResizedImageArray != null && (old != null || usedMemory + size <= usedMemoryUBound || existingImageCount < MaxPageCountInWindow * 4)) // 今＋前後それぞれ 1 ページ分は読んでおかないとページ送りチェックができない。後トラックバー。
            {
                if (old != null)
                {
                    usedMemory -= Math.Min(usedMemory, GetAccountedSize(idx, old));
                    old.Dispose();
                }
                ResizedImageArray[idx] = image;
                usedMemory += size;
                if (AccountedSizeArray != null) AccountedSizeArray[idx] = size;
                // solid の書庫で展開済みのバイト列を持ち続けないよう、書庫に使用済みと伝える（D9。ロックは要らない）
                var entries = EntryArray;
                if (entries != null && idx < entries.Length)
                {
                    var l = loader;
                    l?.MarkEntryConsumed(entries[idx]);
                    var cPage = currentPage;
                    if (l != null && cPage >= 0 && cPage < entries.Length) l.SetViewPosition(entries[cPage]);
                }
                // 縮小デコードしたページは原寸を持っていないので、原寸を捨てたのと同じ扱いにする（表示の近くに来たら原寸を読み直す）（C5）
                // 縮小デコードしたかは結果と一緒に受け取る（Y21。以前は共有の配列から読んだので、完了イベントの前に始まった同じページの次のジョブが
                // false に戻すと、縮小デコードの画質のまま原寸ありの扱いになり、原寸で読み直されなかった）
                var reduced = image.ReducedDecoded;
                if (OriginalStateArray != null) OriginalStateArray[idx] = reduced ? OriginalState_Trimmed : OriginalState_Kept;
                TrimOriginals();
                // 読んでいる間に表示の近くに来ていた場合は、すぐに原寸を読み直させる
                if (reduced && TryGetOriginalProtectedRange(out var protectStart, out var protectStop) && protectStart <= idx && idx < protectStop)
                {
                    ReloadTrimmedOriginalsNearCurrent();
                }
                return true;
            }
            else
            {
                image.Dispose();
                ReworkPage(idx);
                return false;
            }
        }

        private int ReduceUsingMemory(ulong uBound, int protectedIndex = -1)
        {
            if (ResizedImageArray == null) return 0;
            var worksOrder = bmwLoadEachPage.WorksOrder;
            if (worksOrder == null) return ResizedImageArray != null ? ResizedImageArray.Length : 0;
            var protectedOrder = Array.IndexOf(worksOrder, protectedIndex);
            var disposeTarget = worksOrder.Length;
            var existingImageCount = ResizedImageArray.Count(img => img != null);
            var existingImageCountBound = (protectedIndex >= 0 ? -1 : 0) + MaxPageCountInWindow * 4;  // 今＋前後それぞれ 1 ページ分は読んでおかないとページ送りチェックができない。後トラックバー。

            // 先読みの順番に入っていないページ（先読みの範囲の外で、まだ捨てる距離に達していないもの）は順番の中のどのページよりも優先度が低い。
            // 以前はこれらを捨てる候補にしなかったので、上限を超えると先読みの範囲の中のページが捨てられ、遠いページが残っていた。
            // 先に、表示中のページから遠い順に捨てる
            if (usedMemory > uBound && existingImageCount > existingImageCountBound)
            {
                var length = ResizedImageArray.Length;
                var inOrder = new bool[length];
                foreach (var i in worksOrder) if (0 <= i && i < length) inOrder[i] = true;
                List<int> outside = null;
                for (var i = 0; i < length; i++)
                {
                    if (!inOrder[i] && i != protectedIndex && ResizedImageArray[i] != null) (outside ?? (outside = new List<int>())).Add(i);
                }
                if (outside != null)
                {
                    var center = Math.Max(0, currentPage);
                    outside.Sort((a, b) => Math.Abs(b - center) - Math.Abs(a - center));
                    foreach (var i in outside)
                    {
                        if (!(usedMemory > uBound && existingImageCount > existingImageCountBound)) break;
                        DiscardLoadedPage(i);
                        existingImageCount--;
                    }
                }
            }

            while (usedMemory > uBound && existingImageCount > existingImageCountBound)
            {
                // 先読みの順番は表示中のページの前後だけなので、順番の中に捨てられるページが残っていないこともある
                do { disposeTarget--; } while (disposeTarget >= 0 && ResizedImageArray[worksOrder[disposeTarget]] == null);
                if (disposeTarget < 0) return existingImageCount;
                // protected というのは、次に挿入しようとしている画像。
                // これを消すとその分再び挿入することができて処理が繰り返しになってしまうことがある。
                // しかし、これを消してもなお挿入できないこともあって、その場合は protected よりも更に優先順位が高いものも消さないといけない
                // つまり、protected が disposeTarget になった場合、それを消した場合に再挿入されるかどうかを評価して、
                // 再挿入されない場合だけ実際に消す必要がある

                var idx = worksOrder[disposeTarget];


                var image = ResizedImageArray[idx];
                if (image == null) return existingImageCount;

                var newUsedMemory = usedMemory - Math.Min(usedMemory, GetAccountedSize(idx, image));

                if (disposeTarget <= protectedOrder)
                {
                    // 外側の while 文より、ここで消すと再挿入の判定にページ数は使われない。
                    if (newUsedMemory <= uBound) return existingImageCount;


                    if (existingImageCount == existingImageCountBound + 1) return existingImageCount;
                }


                DiscardLoadedPage(idx);
                existingImageCount--;
            }
            return existingImageCount;
        }

        // 読み込み済みのページ（縮小画像と原寸）を捨て、もう一度読める状態に戻す
        private void DiscardLoadedPage(int idx)
        {
            var image = ResizedImageArray[idx];
            if (image == null) return;
            usedMemory -= Math.Min(usedMemory, GetAccountedSize(idx, image));
            image.Dispose();
            ResizedImageArray[idx] = null;
            // 書庫の使用済みの印を外し、戻ったときにまとめて展開されるようにする（D9。ロックは要らない）
            var entries = EntryArray;
            if (entries != null && idx < entries.Length) loader?.UnmarkEntryConsumed(entries[idx]);
            ResizedSizeArray[idx] = default(Size);
            if (AccountedSizeArray != null) AccountedSizeArray[idx] = 0;
            if (OriginalStateArray != null) OriginalStateArray[idx] = OriginalState_Kept;
            ReworkPage(idx);

            LockManager.ForIReadOnlyList(PreFilteredImageArray, idx, item =>
            {
                if (item != null)
                {
                    item.Dispose();
                    PreFilteredImageArray[idx] = null;
                    OriginalImageInfoArray[idx] = null;
                }
            });
        }

        private ulong GetAccountedSize(int idx, VirtualBitmapEx image)
        {
            var accounted = AccountedSizeArray;
            return accounted != null && idx < accounted.Length ? accounted[idx] : GetImageSizeInByte(image);
        }

        /// <summary>
        /// このページを JPEG の縮小デコードで読んでよいか（C5）。原寸を残す範囲の外（読んでもすぐ原寸を捨てるページ）だけ。
        /// 原寸に作用するフィルタ（PreFilter）がある場合と表紙設定モードでは使わない。ワーカースレッドから呼ばれる。
        /// </summary>
        private bool CanDecodeReduced(int idx)
        {
            if (ViewerMode == ViewerModeEnum.CoverSetting) return false;
            if (imageFilter.PreFilterExists()) return false;
            var states = OriginalStateArray;
            var flags = ReducedDecodedArray;
            if (states == null || flags == null || idx >= states.Length || idx >= flags.Length) return false;
            if (!TryGetOriginalProtectedRange(out var start, out var stop)) return false;
            return idx < start || stop <= idx;
        }

        // 原寸からデコードして縮小するページの範囲と、原寸を残す範囲（計算は PrefetchScheduler）。NextPage は currentPage が負でないときだけ求める
        private bool TryGetOriginalProtectedRange(out int start, out int stop)
        {
            var cPage = currentPage;
            if (cPage < 0)
            {
                start = stop = 0;
                return false;
            }
            return PrefetchScheduler.TryGetOriginalProtectedRange(cPage, NextPage, MaxPageCountInWindow, out start, out stop);
        }

        private bool TryGetOriginalKeepRange(out int start, out int stop)
        {
            var cPage = currentPage;
            if (cPage < 0)
            {
                start = stop = 0;
                return false;
            }
            return PrefetchScheduler.TryGetOriginalKeepRange(cPage, NextPage, MaxPageCountInWindow, magnifierWanted, out start, out stop);
        }

        /// <summary>
        /// 縮小画像ができていて、表示の近くにないページの原寸を捨てる（C1）。
        /// 先読みしたページの原寸（例: 3000x4300 のカラーで 38MB）を全部持たないようにする。
        /// 原寸を共有する等倍表示のページ（Clone）や、原寸を参照して描画するアニメーションなどは対象外。
        /// </summary>
        private void TrimOriginals()
        {
            var resized = ResizedImageArray;
            var originals = PreFilteredImageArray;
            var accounted = AccountedSizeArray;
            var states = OriginalStateArray;
            if (resized == null || originals == null || accounted == null || states == null) return;
            if (resized.Length != originals.Length || resized.Length != accounted.Length || resized.Length != states.Length) return;
            if (ViewerMode == ViewerModeEnum.CoverSetting) return;
            // 拡大画像の作成はロックなしで原寸の参照を取るので、その間は捨てない（完了時にもう一度呼ばれる）
            if (bwMagnifierMaker.IsBusy) return;
            if (!TryGetOriginalKeepRange(out var protectStart, out var protectStop)) return;

            for (var i = 0; i < resized.Length; i++)
            {
                if (protectStart <= i && i < protectStop) continue;
                var image = resized[i];
                if (image == null || !image.isEntity || image.FrameCount != 1) continue;
                if (originals[i] == null) continue;
                // 原寸を読み直している途中のページは捨てない。読み込みジョブは原寸を登録してから自分のロックを取るまでに隙間があり、
                // そこで捨てると読み込み中の原寸を壊す（V2 で原寸を残す範囲を空にしてから起こりうるようになった）。登録後（SetNewResizedImage）にもう一度呼ばれる
                if (states[i] == OriginalState_Reloading) continue;
                LockManager.ForIReadOnlyList(originals, i, item =>
                {
                    if (item == null || item.FrameCount != 1 || item.Size == image.Size) return;
                    var bytes = (ulong)item.GetDataSizeInBytes();
                    item.Dispose();
                    originals[i] = null;
                    var reduce = Math.Min(bytes, accounted[i]);
                    accounted[i] -= reduce;
                    usedMemory -= Math.Min(usedMemory, reduce);
                    states[i] = OriginalState_Discarded; // 縮小画像は原寸から作ってあるので、画質のための読み直しは要らない（V2）
                });
            }
        }

        /// <summary>
        /// 原寸を捨てたページが表示の近くに来たら、原寸を読み直させる（C1）。
        /// 縮小画像はそのまま表示に使い、読み直しが終わったら SetNewResizedImage で差し替える。
        /// 縮小デコードで読んだページ（Trimmed）は、ルーペを使っていなければ表示中の見開きだけを、
        /// そのページに 300ms 留まってから読み直す（V22。以前は前後の見開きも含めてすぐ読み直し、大きな JPEG をほぼ全ページ 2 回デコードしていた）
        /// </summary>
        private void ReloadTrimmedOriginalsNearCurrent() => ReloadTrimmedOriginalsNearCurrent(displayedTrimmedNow: false);

        private void ReloadTrimmedOriginalsNearCurrent(bool displayedTrimmedNow)
        {
            var resized = ResizedImageArray;
            var states = OriginalStateArray;
            if (resized == null || states == null || resized.Length != states.Length) return;
            if (!TryGetOriginalProtectedRange(out var start, out var stop)) return;
            if (!TryGetOriginalKeepRange(out var keepStart, out var keepStop)) return;
            var cPage = currentPage;
            var window = Math.Max(1, MaxPageCountInWindow);
            var requested = false;
            var displayedTrimmedPending = false;
            for (var i = Math.Max(0, start); i < Math.Min(stop, resized.Length); i++)
            {
                if (resized[i] == null) continue;
                bool reload;
                if (states[i] == OriginalState_Trimmed)
                {
                    // 縮小デコードのページは画質のため。ルーペを使うなら原寸が要るので従来どおり範囲全体をすぐ
                    if (magnifierWanted) reload = true;
                    else if (i < cPage || cPage + window <= i) reload = false;
                    else if (displayedTrimmedNow) reload = true;
                    else
                    {
                        reload = false;
                        displayedTrimmedPending = true;
                    }
                }
                else
                {
                    // 原寸を捨てたページ（Discarded）はルーペのために原寸が要る範囲だけ（V2）
                    reload = states[i] == OriginalState_Discarded && keepStart <= i && i < keepStop;
                }
                if (reload)
                {
                    // 読み直しが終わる（SetNewResizedImage）まで Reloading のままにする。
                    // ReworkOrder は前回の結果を消さないので、GetWorkState では読み直し中かどうか判定できない
                    states[i] = OriginalState_Reloading;
                    ReworkPage(i);
                    requested = true;
                }
            }
            if (displayedTrimmedPending) ScheduleDisplayedOriginalsReload(cPage);
            // 読み込みが止まっている（ThreadCount == 0 や全ワーカー終了）場合もあるので、実行順を作り直して再開させる
            if (requested) SetBackgroundMode();
        }

        // 表示中の見開きの縮小デコードのページを、留まってから原寸で読み直すためのタイマー（V22）
        private const int DisplayedOriginalsReloadDelayMs = 300;

        private System.Windows.Forms.Timer displayedOriginalsReloadTimer;

        private int displayedOriginalsReloadPage = -1;

        private void ScheduleDisplayedOriginalsReload(int page)
        {
            if (displayedOriginalsReloadTimer == null)
            {
                displayedOriginalsReloadTimer = new System.Windows.Forms.Timer { Interval = DisplayedOriginalsReloadDelayMs };
                displayedOriginalsReloadTimer.Tick += (sender, e) =>
                {
                    displayedOriginalsReloadTimer.Stop();
                    if (IsDisposed || displayedOriginalsReloadPage != currentPage) return; // 別のページへ移った
                    if (SeekBarDragging) return; // 離したときにもう一度呼ばれる（V24）
                    ReloadTrimmedOriginalsNearCurrent(displayedTrimmedNow: true);
                };
                Disposed += (sender, e) => displayedOriginalsReloadTimer.Dispose();
            }
            if (displayedOriginalsReloadTimer.Enabled && displayedOriginalsReloadPage == page) return; // 待っている途中
            displayedOriginalsReloadPage = page;
            displayedOriginalsReloadTimer.Stop();
            displayedOriginalsReloadTimer.Start();
        }

        // 表示中のページで、原寸を捨てたあと読み直し中のものがあるか（拡大画像はそれが終わってから作る）
        private bool IsCurrentOriginalReloading(int startPage, int stopPage)
        {
            var originals = PreFilteredImageArray;
            var resized = ResizedImageArray;
            var states = OriginalStateArray;
            if (originals == null || resized == null || resized.Length != originals.Length) return false;
            for (var i = Math.Max(0, startPage); i < Math.Min(stopPage, originals.Length); i++)
            {
                // 縮小画像はあるのに原寸がなく、捨てたか読み直し中のページ（読み込みエラーのページは原寸が最初から無いが、状態は Kept のまま）
                if (resized[i] != null && originals[i] == null && states != null && i < states.Length && states[i] != OriginalState_Kept) return true;
            }
            return false;
        }

        private ulong GetImageSizeInByte(VirtualBitmapEx image)
        {
            return (ulong)image.DataSizeInBytes;
        }

        private void SetBackgroundModeIfPausing()
        {
            if (bmwLoadEachPage.ThreadCount == 0)
            {
                SetBackgroundMode();
            }
        }

        private int prefetchOrderPage = int.MinValue;

        private int SetBackgroundMode_CallCount = 0;

        // 読み込む順番を作り直し、表示から離れたページを捨てる（順番と捨てる範囲は PrefetchScheduler）
        private void SetBackgroundMode()
        {
            SetBackgroundMode_CallCount++;
            if (ResizedImageArray == null) return;
            var length = ResizedImageArray.Length;
            var clientCursorPosition = mtbPage.PointToClient(Cursor.Position);
            var onTrackbar = mtbPage.ClientRectangle.Contains(clientCursorPosition);
            var centerValue = onTrackbar ? mtbPage.PointToValue(clientCursorPosition) : currentPage;
            var cPage = currentPage;
            var pagesInWindow = MaxPageCountInWindow;
            prefetchOrderPage = cPage;

            // 表示中のページの前後と、トラックバー上のカーソル位置の近くだけを読む
            var order = PrefetchScheduler.GetLoadOrder(length, cPage, pagesInWindow, onTrackbar, centerValue);

            for (var i = 0; i < length; i++)
            {
                if (ResizedImageArray[i] != null && PrefetchScheduler.ShouldDiscard(i, cPage, pagesInWindow, onTrackbar, centerValue))
                {
                    DiscardLoadedPage(i);
                }
            }

            bmwLoadEachPage.SetWorksOrder(order);
            ReduceUsingMemory(usedMemoryUBound);
            bmwLoadEachPage.ThreadCount = LoadThreadCount;
        }

        private void bmwLoadEachPage_AllRunWorkersCompleted(object sender, GenerarClasses.BackgroundMultiWorker.AllRunWorkerCompletedEventArgs e)
        {
            Program.AlertError(e.Error);
        }

        private void fullScreenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            fullScreenModeChange(!FullScreen);
        }

        bool alwaysHideUI = false;

        bool AlwaysHideUI
        {
            get { return alwaysHideUI; }
            set
            {
                if (value != alwaysHideUI)
                {
                    alwaysHideUI = value;
                    alwaysAutoHideUIToolStripMenuItem.Checked = value;

                    if (!FullScreen)
                    {
                        HideUI = value;
                    }
                }
            }
        }

        bool hideUI = false;

        bool HideUI
        {
            get { return hideUI; }
            set
            {
                if (value != hideUI)
                {
                    hideUI = value;
                    SetHideUI();
                }
            }
        }

        void SetHideUI()
        {
            var temp = pbView_SizeChanged_SizeChanging;
            pbView_SizeChanged_SizeChanging = true;
            if (hideUI)
            {
                menuStrip_Bottom = menuStrip.Bounds.Bottom;
                menuStrip.Visible = false;
                mtbPageTop_As_Reference_pbViewBottom = pnlSeekbar.Bounds.Top - pbView.Bottom;
                pnlSeekbar.Visible = false;
                statusStrip.Visible = false;


                var clientSize = ClientSize;
                pbView.Top = 0;
                pbView.Height = clientSize.Height;
                pbView.Left = 0;
                pbView.Width = clientSize.Width;
            }
            else
            {
                menuStrip_Bottom = 0;
                menuStrip.Visible = true;
                mtbPageTop_As_Reference_pbViewBottom = 0;
                pnlSeekbar.Visible = true;
                pnlSeekbar_PreStatusStripVisibleTrue();
                statusStrip.Visible = true;
                mtbPage.ValueCanChangedByMouseButton = true;

                pbView.Top = menuStrip.Bottom;
                pbView.Height = pnlSeekbar.Top - pbView.Top;
            }
            pbView_SizeChanged_SizeChanging = temp;
            pbView_SizeChanged(null, null);
        }
    }
}
