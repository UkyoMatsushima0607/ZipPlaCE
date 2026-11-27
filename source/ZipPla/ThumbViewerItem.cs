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
    public class ThumbViewerItem : IDisposable
    {
        // サムネイル読み込みの待ち行列（A4/A10）。UI スレッドだけで操作する。
        // 1 件ずつ、新しく依頼されたもの（＝今表示されているもの）から順に読み込む。
        // スクロール中（読み込み範囲が直近 ScrollSettleMs 以内に変わった）は新しい読み込みを始めず、
        // 通り過ぎただけの項目のサムネイルを作らないようにする。
        private static readonly LinkedList<ThumbViewerItem> loadQueue = new LinkedList<ThumbViewerItem>();
        private static int activeLoads = 0;
        private static bool settleWaitScheduled = false;
        private const int ScrollSettleMs = 60;
        // 同時に作るサムネイルの数（A4）
        private static readonly int MaxParallelLoads = getMaxParallelLoads();
        private static int getMaxParallelLoads()
        {
#if PERFLOG
            // 計測用: 環境変数で変えられる
            if (int.TryParse(Environment.GetEnvironmentVariable("ZIPPLA_THUMB_THREADS"), out var n) && n >= 1) return n;
#endif
            // 1 本では、表示中のサムネイルが多い（大きなウィンドウ）ときに揃うまで時間がかかる。
            // 2〜3 本でも描画時間は増えなかった（07955f5 で懸念したカクつきは計測では見られない）
            return Math.Max(1, Math.Min(3, Environment.ProcessorCount / 4));
        }
        internal static int LastScrollTick = Environment.TickCount - 10000;
        private LinkedListNode<ThumbViewerItem> queueNode = null;
        private bool loading = false;          // GetThumbnail の実行中
        private bool loadCancelled = false;    // 実行中に範囲外になった（Clear された）
        private bool loadFailed = false;       // 現在のパスで読み込みに失敗した（描画のたびに読み直さない）
        private bool disposed = false;

        public CatalogForm OwnerCatalog { get; set; }
        // owner・index・selected は ThumbViewer が直接書く（以前はフォルダを開くたびに項目ごとにリフレクションで書いていた、K13）
        internal ThumbViewer owner = null;
        public ThumbViewer Owner { get { return owner; } }
        internal int index = -1;
        public int Index { get { return index; } }
        internal bool selected = false;
        public bool Selected { get { return selected; } }
        private Bitmap image;
        object imageLocker = new object();
        public Bitmap Image
        {
            get
            {
                return image;
            }
            set
            {
                lock (imageLocker)
                {
                    if (image == value)
                        return;

                    if (image != null) image.Dispose();
                    image = value;
                    if (owner != null)
                    {
                        owner.DrawItem(index, selected, onlyFrame: false);
                    }
                }
            }
        }

        string filePath = null;
        public string FilePath 
        {
            get=> filePath;
            set
            {
                if (filePath == value)
                    return;
                filePath = value;
                loadFailed = false;
                Image = null;
            }
        }

        /// <summary>サムネイルの読み込みを待ち行列に入れる（表示範囲に入ったとき PaintPart から呼ばれる）</summary>
        public Task LoadAsync()
        {
            if (disposed || filePath == null || OwnerCatalog == null || loading || queueNode != null || loadFailed || Image != null)
                return Task.CompletedTask;
            // 少し前まで表示していたサムネイルがメモリに残っていれば、作り直さずに使う（A3）
            var stamp = OwnerCatalog.GetThumbnailStamp(index, filePath);
            var cached = owner?.ThumbnailMemoryCache.Take(filePath, owner.ThumbnailSize, stamp);
            if (cached != null)
            {
                Image = cached;
                imageStamp = stamp;
                return Task.CompletedTask;
            }
            queueNode = loadQueue.AddLast(this);
#if PERFLOG
            PerfTrace.ThumbnailQueued();
#endif
            PumpLoadQueue();
            return Task.CompletedTask;
        }

        // 待ち行列から、同時実行数の上限まで読み込みを始める（UI スレッドで呼ぶ）
        private static void PumpLoadQueue()
        {
            while (activeLoads < MaxParallelLoads && loadQueue.Count > 0)
            {
                // スクロール中は、止まる（範囲が ScrollSettleMs 変わらない）まで待つ
                var wait = ScrollSettleMs - unchecked(Environment.TickCount - LastScrollTick);
                if (wait > 0)
                {
                    if (!settleWaitScheduled)
                    {
                        settleWaitScheduled = true;
                        _ = WaitAndPumpAsync(wait);
                    }
                    return;
                }
                var item = loadQueue.Last.Value; // 新しく依頼されたものから
                loadQueue.RemoveLast();
                item.queueNode = null;
                activeLoads++;
                _ = RunLoadAsync(item);
            }
        }

        private static async Task WaitAndPumpAsync(int wait)
        {
            await Task.Delay(wait);
            settleWaitScheduled = false;
            PumpLoadQueue();
        }

        private static async Task RunLoadAsync(ThumbViewerItem item)
        {
            try
            {
                await item.LoadCoreAsync();
            }
            finally
            {
                activeLoads--;
                PumpLoadQueue();
            }
        }

        private async Task LoadCoreAsync()
        {
            var path = filePath;
            var catalog = OwnerCatalog;
            if (disposed || path == null || catalog == null || Image != null) return;
            loading = true;
            loadCancelled = false;
#if PERFLOG
            PerfTrace.ThumbnailStarted();
#endif
            Bitmap result = null;
            var loadResult = CatalogForm.LoadResult.LoadError;
            var stamp = catalog.GetThumbnailStamp(index, path); // 読み込みを始めた時点のファイルの印（K26）
            try
            {
                MovieInfo info = null;
#if PERFLOG
                var perfStart = Stopwatch.GetTimestamp();
#endif
                CatalogForm.LoadResult taskLoadResult = CatalogForm.LoadResult.LoadError;
                result = await Task.Run(() => catalog.GetThumbnail(path, Directory.Exists(path), null, out _, out _, out taskLoadResult, out _, out _, ref info, false));
                loadResult = taskLoadResult;
#if PERFLOG
                PerfTrace.AddGenerate(Stopwatch.GetTimestamp() - perfStart);
#endif
            }
            catch
            {
                result = null;
            }
            finally
            {
                loading = false;
            }

            // 読み込み中に範囲外になった・パスが変わった・破棄された場合は、項目に持たせずに捨てる（A10）
            if (loadCancelled || disposed || filePath != path)
            {
                result?.Dispose();
                if (reloadAfterLoad && !disposed && filePath == path)
                {
                    reloadAfterLoad = false;
                    _ = LoadAsync(); // 読み込み中にファイルが変わった（K21）
                }
                return;
            }
            // 読み込み結果を戻す（アイコンを描くかどうかに使われる、A11）。画像をセットすると再描画されるので、その前に戻す
            catalog.ReportThumbnailLoadResult(index, path, loadResult);
            if (result == null)
            {
                loadFailed = true;
                DrawItem(onlyFrame: false); // アイコンなどの代わりの表示を描き直す
                return;
            }
            Image = result;
            imageStamp = stamp;
#if PERFLOG
            if (owner != null) PerfTrace.CheckRangeReady(owner.IsLoadRangeReady(), owner.CountLoadedImages);
#endif
        }

        private long imageStamp; // 画像を作ったときのファイルの印（K26）

        /// <summary>表示範囲外になったとき：待ち行列から外し、読み込み中なら結果を捨てさせ、画像を破棄する</summary>
        public void Clear()
        {
            if (queueNode != null)
            {
                loadQueue.Remove(queueNode);
                queueNode = null;
#if PERFLOG
                PerfTrace.ThumbnailCancelled();
#endif
            }
            if (loading) loadCancelled = true;
            reloadAfterLoad = false; // 範囲外になったので読み直さない（K21）
            // 表示範囲から外れただけなら、画像は捨てずにメモリのキャッシュへ移す（範囲に戻ったら使う、A3）
            if (!disposed && owner != null && filePath != null && image != null)
            {
                Bitmap detached;
                lock (imageLocker)
                {
                    detached = image;
                    image = null;
                }
                if (detached != null) owner.ThumbnailMemoryCache.Put(filePath, detached, imageStamp);
                return;
            }
            Image = null;
        }

        private string text;
        public string Text
        {
            get
            {
                return text;
            }
            set
            {
                text = value;
                if (owner != null)
                {
                    //owner.DrawItem(g, index, selected, onlyFrame: false); // テキストの行数が変わった時のため
                    owner.DrawItem(index, selected, onlyFrame: true); // 現在の仕様ではテキストは画像に重なることはない
                }
            }
        }
        public void DrawItem(bool onlyFrame)
        {
            if (owner != null)
            {
                owner.DrawItem(index, selected, onlyFrame);
            }
        }
        public void SilentSet(Bitmap image, string text)
        {
            if (this.image != null) this.image.Dispose();
            this.image = image;
            this.text = text;
        }
        public void SilentSet(string text)
        {
            this.text = text;
        }

        /// <summary>
        /// ファイルが変わったとき（監視イベント、K21）: 画像を捨て、次に表示範囲で描かれたときに作り直させる。
        /// 読み込みに失敗した印も消す（書き込み中で読めなかったファイルを、書き終わったあとに読み直すため）
        /// </summary>
        public void ResetForReload(string text)
        {
            this.text = text;
            loadFailed = false;
            if (loading)
            {
                // 読み込み中の結果は古い中身なので捨て、終わったら読み直す
                loadCancelled = true;
                reloadAfterLoad = true;
            }
            if (filePath != null) owner?.ThumbnailMemoryCache.Remove(filePath);
            Image = null; // 描き直される
        }
        private bool reloadAfterLoad = false;

        public void Set(Bitmap image, string text)
        {
            if (this.image != null) this.image.Dispose();
            this.image = image;
            this.text = text;

            if (owner != null)
            {
                owner.DrawItem(index, selected, onlyFrame: false);
            }
        }

        public void Dispose()
        {
            disposed = true;
            Clear(); // 待ち行列から外し、読み込み中の結果は捨てさせ、画像を破棄する（Image の setter が Dispose する）
        }
    }

    /// <summary>
    /// 表示範囲から外れたサムネイルを、容量の上限まで残しておくキャッシュ（A3）。古く使われたものから捨てる。
    /// UI スレッドだけで使う。一覧が作り直される（ThumbViewer.Clear）ときは、見た目の設定が変わっていれば丸ごと捨て、
    /// 同じならファイルの印（更新日時）が分かるものだけ残す（K26）。取り出すときに印が違えば捨てる。
    /// </summary>
    public sealed class ThumbnailMemoryCache
    {
        private struct Entry
        {
            public string Path;
            public Bitmap Image;
            public long Stamp; // 0 は「確かめられない」。一覧を作り直したら捨てる
        }
        private readonly Dictionary<string, LinkedListNode<Entry>> map = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<Entry> order = new LinkedList<Entry>(); // 先頭が古い
        private long bytes;
        private string appearanceKey;
        private static readonly long Budget = getBudget();
        private static long getBudget()
        {
            try
            {
                var total = (long)new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory;
                return Math.Max(32L << 20, Math.Min(128L << 20, total / 64)); // 物理メモリの 1/64（32MB〜128MB）
            }
            catch
            {
                return 64L << 20;
            }
        }
        private static long sizeOf(Bitmap b) => (long)b.Width * b.Height * (Image.GetPixelFormatSize(b.PixelFormat) / 8);

        public int Count => map.Count;

        public void Put(string path, Bitmap image, long stamp)
        {
            Remove(path);
            var size = sizeOf(image);
            if (size > Budget / 4) { image.Dispose(); return; }
            map[path] = order.AddLast(new Entry { Path = path, Image = image, Stamp = stamp });
            bytes += size;
            while (bytes > Budget && order.First != null)
            {
                Remove(order.First.Value.Path);
            }
        }

        /// <summary>キャッシュから取り出す（取り出したものの所有権は呼び出し側に移る）。大きさかファイルの印が違えば捨てて null</summary>
        public Bitmap Take(string path, Size requiredSize, long stamp)
        {
            LinkedListNode<Entry> node;
            if (path == null || !map.TryGetValue(path, out node)) return null;
            map.Remove(path);
            order.Remove(node);
            var image = node.Value.Image;
            bytes -= sizeOf(image);
            if (image.Size != requiredSize || node.Value.Stamp != stamp)
            {
                image.Dispose();
                return null;
            }
            return image;
        }

        public void Remove(string path)
        {
            LinkedListNode<Entry> node;
            if (path == null || !map.TryGetValue(path, out node)) return;
            map.Remove(path);
            order.Remove(node);
            bytes -= sizeOf(node.Value.Image);
            node.Value.Image.Dispose();
        }

        /// <summary>
        /// 一覧を作り直したときに呼ぶ（K26）。見た目の設定（newAppearanceKey）が変わっていれば全部捨て、
        /// 同じならファイルの印が分からないもの（書庫の中・フォルダなど）だけ捨てる
        /// </summary>
        public void OnListRebuilt(string newAppearanceKey)
        {
            if (newAppearanceKey == null || newAppearanceKey != appearanceKey)
            {
                Clear();
                appearanceKey = newAppearanceKey;
                return;
            }
            var node = order.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.Stamp == 0) Remove(node.Value.Path);
                node = next;
            }
        }

        public void Clear()
        {
            foreach (var entry in order) entry.Image.Dispose();
            order.Clear();
            map.Clear();
            bytes = 0;
        }
    }
}
