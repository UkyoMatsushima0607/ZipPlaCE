using System;
using System.Collections.Generic;

namespace ZipPla
{
    /// <summary>
    /// ビューアの先読みの順番と、縮小画像・原寸を残す範囲を決める（ViewerForm から状態を引数で受け取る、副作用のない計算だけ）。
    /// pagesInWindow は 1 画面に表示するページ数（見開きなら 2。読み込み前などは 0 のこともある）
    /// </summary>
    internal static class PrefetchScheduler
    {
        // 先読みするのは表示中のページの前後この枚数まで。外れたページは DiscardRange より離れたら捨てる。
        // 以前は空き物理メモリの約 7 割まで書庫の全ページを読んでいた（1722 ページの書庫を 1080p で開くと 3.1GB・CPU 20 秒）。
        // 1 ページの読み込みは 10ms 前後なので、遠くへ移動したときはその場で読めば足りる
        public const int PrefetchRange = 64;

        public const int DiscardRange = 96;

        /// <summary>center から前に range、後ろに（表示中の枚数＋range）の範囲に page が入るか</summary>
        public static bool IsInRange(int page, int center, int range, int pagesInWindow)
        {
            var spread = Math.Max(1, pagesInWindow);
            return center - range <= page && page < center + spread + range;
        }

        /// <summary>シークバー上のカーソルの近くとみなす範囲（表示中の枚数の 2 倍）</summary>
        public static int HoverRange(int pagesInWindow) => Math.Max(1, pagesInWindow) * 2;

        /// <summary>
        /// 読み込む順番。表示中のページの前後（PrefetchRange）と、シークバー上のカーソルの近く（onTrackbar のとき）のページを、
        /// 優先度（PriorityLevel）、同じ優先度なら centerValue からの距離の順に並べる
        /// </summary>
        public static int[] GetLoadOrder(int length, int currentPage, int pagesInWindow, bool onTrackbar, int centerValue)
        {
            var current = Math.Max(0, currentPage);
            var hoverRange = HoverRange(pagesInWindow);
            var orderList = new List<int>();
            for (var i = 0; i < length; i++)
            {
                if (IsInRange(i, current, PrefetchRange, pagesInWindow) || (onTrackbar && IsInRange(i, centerValue, hoverRange, pagesInWindow))) orderList.Add(i);
            }
            var order = orderList.ToArray();
            var stopPage = currentPage + pagesInWindow;
            Array.Sort(order, (a, b) =>
            {
                var aLevel = PriorityLevel(a, currentPage, stopPage, pagesInWindow);
                var bLevel = PriorityLevel(b, currentPage, stopPage, pagesInWindow);
                if (aLevel == bLevel)
                {
                    return Math.Abs(a - centerValue) - Math.Abs(b - centerValue);
                }
                else
                {
                    return aLevel - bLevel;
                }
            });
            return order;
        }

        /// <summary>
        /// 読み込んだページを捨てるか。表示から DiscardRange より離れていて、シークバー上のカーソルの近くでもないページ。
        /// シークバーのドラッグ中も捨てる（離すまで待つと、通過したページの縮小画像がたまって最大メモリが 154→564MB に増えた。V24）
        /// </summary>
        public static bool ShouldDiscard(int page, int currentPage, int pagesInWindow, bool onTrackbar, int centerValue)
        {
            var current = Math.Max(0, currentPage);
            return !IsInRange(page, current, DiscardRange, pagesInWindow) &&
                !(onTrackbar && IsInRange(page, centerValue, HoverRange(pagesInWindow), pagesInWindow));
        }

        /// <summary>読み込みの優先度。0: 表示中、1: 次の見開き、2: 前の見開き、3: それ以外</summary>
        public static int PriorityLevel(int x, int currentPage, int stopPage, int pagesInWindow)
        {
            var geCurrent = currentPage <= x;
            var lStop = x < stopPage;
            if (geCurrent && lStop)
            {
                return 0;
            }
            else if (!lStop && x < stopPage + pagesInWindow)
            {
                return 1;
            }
            else if (currentPage - pagesInWindow <= x && !geCurrent)
            {
                return 2;
            }
            else
            {
                return 3;
            }
        }

        /// <summary>
        /// 原寸からデコードして縮小するページの範囲 [start, stop)（表示中＋前後 1 見開き分）。この範囲では縮小デコード（C5）を使わず、
        /// 縮小デコードで読んだページが入ってきたら原寸から読み直す（表示の画質を保つ）（C1・C5）。currentPage が負なら false
        /// </summary>
        public static bool TryGetOriginalProtectedRange(int currentPage, int nextPage, int pagesInWindow, out int start, out int stop)
        {
            start = stop = 0;
            if (currentPage < 0) return false;
            var window = Math.Max(1, pagesInWindow);
            start = currentPage - window;
            stop = Math.Max(nextPage, currentPage + window) + window;
            return true;
        }

        /// <summary>
        /// 原寸を残すページの範囲（V2）。原寸は縮小画像を作ったあとは拡大画像（ルーペ）にしか使わないので、
        /// ルーペを一度も開いていなければ空（縮小したらすぐ捨てる）。開いたあとは TryGetOriginalProtectedRange と同じ
        /// </summary>
        public static bool TryGetOriginalKeepRange(int currentPage, int nextPage, int pagesInWindow, bool magnifierWanted, out int start, out int stop)
        {
            if (!TryGetOriginalProtectedRange(currentPage, nextPage, pagesInWindow, out start, out stop)) return false;
            if (!magnifierWanted) stop = start;
            return true;
        }
    }
}
