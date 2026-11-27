using CSharpImageLibrary;
using LibAPNG;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Collections;

namespace ZipPla
{
    public class VirtualBitmapEx : IDisposable
    {
        public BitmapEx GetSourceForLock() { return source; }
        private readonly BitmapEx source;
        public readonly bool isEntity;
        public readonly int Width, Height;
        public readonly Size Size;
        public readonly long DataSizeInBytes;
        /// <summary>source が原寸など別の画像と画素のメモリを共有しているか（X1）。共有していると、相手を Dispose したあとは使えない</summary>
        public readonly bool SharesOtherMemory;
        /// <summary>ビューアの読み込みジョブが JPEG などの縮小デコードで作った画像か（C5）。完了イベントに結果と一緒に渡す（Y21）</summary>
        public bool ReducedDecoded;
        private readonly bool downScaleNearestNeighbor, upScaleNearestNeighbor;
        private VirtualBitmapEx(BitmapEx source, bool isEntity, long otherDataSize, Size size, bool downScaleNearestNeighbor, bool upScaleNearestNeighbor)
        {
            this.source = source;
            this.isEntity = isEntity;
            this.downScaleNearestNeighbor = downScaleNearestNeighbor;
            this.upScaleNearestNeighbor = upScaleNearestNeighbor;
            DataSizeInBytes = otherDataSize + source.GetDataSizeInBytes();
            FrameCount = source.FrameCount;
            Size = size;
            Width = size.Width;
            Height = size.Height;
        }
        public VirtualBitmapEx(BitmapEx source, long otherDataSize) : this(source, true, otherDataSize, source.Size, true, true) { }
        public VirtualBitmapEx(BitmapEx source, long otherDataSize, bool sharesOtherMemory) : this(source, true, otherDataSize, source.Size, true, true)
        {
            SharesOtherMemory = sharesOtherMemory;
        }
        public VirtualBitmapEx(BitmapEx source, Size virtualSize, bool downScaleNearestNeighbor, bool upScaleNearestNeighbor) : this (source, false, 0, virtualSize, downScaleNearestNeighbor, upScaleNearestNeighbor) { }

        public static VirtualBitmapEx GetErrorImage(Size size)
        {
            return new VirtualBitmapEx(BitmapEx.ConvertToBitmapEx(Program.GetErrorImage(size.Width, size.Height)), 0);
        }

        public readonly int FrameCount;

        public System.Drawing.Imaging.PixelFormat PixelFormat { get { return source.PixelFormat; } }

        public void DrawFrame(System.Drawing.Imaging.BitmapData dst, int dstX, int dstY, DateTime now)
        {
            DrawFrame(dst, dstX, dstY, new Rectangle(0, 0, Width, Height), now, canUseParallel: true);
        }

        public void DrawFrame(System.Drawing.Imaging.BitmapData dst, int dstX, int dstY, Rectangle rect, DateTime now, bool canUseParallel)
        {
            var srcSize = source.Size;
            if (srcSize == Size)
            {
                if (rect.Width <= 0 || rect.Height <= 0) return;
                var bitmap = source.GetBitmap(source.GetCurrentIndex(now));
                if (dst.PixelFormat == System.Drawing.Imaging.PixelFormat.Format24bppRgb && bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format8bppIndexed)
                {
                    // 灰色のページは 8bpp のまま持っている（C2）。24bpp でロックすると描くたびに GDI+ がロックした範囲全体を変換するので、
                    // 8bpp のままロックして見えている範囲だけをパレットで引いて描く
                    var drawn = false;
                    ViewerForm.lockBits(bitmap, source, rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format8bppIndexed,
                        "zoomed image (source, noscaling, 8bpp)", src =>
                        {
                            var palette = ViewerForm.GetOpaquePaletteEntries(bitmap); // GDI+ のビットマップに触るのでロックの中で
                            if (palette == null) return;
                            ViewerForm.DrawIndexedBitmapDataUnscaled(dst, dstX, dstY, src, palette);
                            drawn = true;
                        });
                    if (drawn) return;
                }
                ViewerForm.lockBits(bitmap, source, rect,
                    System.Drawing.Imaging.ImageLockMode.ReadOnly, "zoomed image (source, noscaling)", src =>
                    {
                        ViewerForm.DrawBitmapDataUnscaled(dst, dstX, dstY, src);
                    });
            }
            else
            {
                rect.Intersect(new Rectangle(rect.X - dstX, rect.Y - dstY, dst.Width, dst.Height));
                if (rect.Width <= 0 || rect.Height <= 0) return;
                var virtualWidth = (long)Size.Width;
                var virtualHeight = (long)Size.Height;
                var srcWidth = (long)srcSize.Width;
                var srcHeight = (long)srcSize.Height;
                var onSrcX = (int)(srcWidth * (2L * rect.X + 1) / (2L * virtualWidth));
                var onSrcY = (int)(srcHeight * (2L * rect.Y + 1) / (2L * virtualHeight));
                var onSrcWidth = (int)Math.Max(1, (srcWidth * rect.Width - virtualWidth / 2) / virtualWidth);
                var onSrcHeight = (int)Math.Max(1, (srcHeight * rect.Height - virtualHeight / 2) / virtualHeight);
                var onSrcRect = new Rectangle(onSrcX, onSrcY, onSrcWidth, onSrcHeight);

                onSrcRect.Intersect(new Rectangle(0, 0, srcSize.Width, srcSize.Height));
                var dstRect = new Rectangle(dstX, dstY, rect.Width, rect.Height);
                dstRect.Intersect(new Rectangle(0, 0, dst.Width, dst.Height));

                var scaleUp = dstRect.Width >= 2 * onSrcRect.Width && dstRect.Height >= 2 * onSrcRect.Height;


                // Parallel はスレッドの譲渡が発生するので Paint イベントが二重起動しないようにコントロールに特別な実装が必要
                // さもなければ並列化は使ってはいけない
                
                if (scaleUp && upScaleNearestNeighbor || !scaleUp && downScaleNearestNeighbor)
                {
                    ViewerForm.lockBits(source.GetBitmap(source.GetCurrentIndex(now)), source, onSrcRect,
                    System.Drawing.Imaging.ImageLockMode.ReadOnly, "zoomed image (source, nearest neightbor)", src =>
                    {
                        LongVectorImageResizer.NearestNeighbor(src, new Rectangle(0, 0, onSrcRect.Width, onSrcRect.Height), dst, dstRect, canUseParallel);
                    });
                }
                else
                {
                    ViewerForm.lockBits(source.GetBitmap(source.GetCurrentIndex(now)), source, onSrcRect,
                    System.Drawing.Imaging.ImageLockMode.ReadOnly, "zoomed image (source, quick graphic)", src =>
                    {
                        QuickGraphic.DrawImage(dst, dstRect, src, canUseParallel, backgroundWorker: null);
                    });
                }
            }
        }

        public void Start(DateTime dateTime) { source.Start(dateTime); }
        public DateTime GetNextChangeTime(DateTime dateTime) { return source.GetNextChangeTime(dateTime); }

        private bool disposed = false;
        public void Dispose()
        {
            if (!disposed)
            {
                if (isEntity) source.Dispose();
                disposed = true;
            }
        }
    }

    public class BitmapEx : Bitmap
    {
        private readonly Bitmap[] bitmaps;
        public readonly TimeSpan[] Delays;
        public readonly TimeSpan Period;
        private IEnumerator<Bitmap> bitmapsPrivider;
        // http://qiita.com/razokulover/items/34962844e314bb4bfd04
        private static readonly TimeSpan DelayMinimum = TimeSpan.FromSeconds(1.0 / 150.0); // 144Hz に対応するように
        private static readonly TimeSpan DelayDefault = TimeSpan.FromSeconds(0.1);
        public BitmapEx(IEnumerator<Bitmap> bitmapsPrivider, IEnumerable<TimeSpan> delays) : base(null as System.Drawing.Bitmap)
        {
            
            // http://qiita.com/razokulover/items/34962844e314bb4bfd04
            Delays = (from delay in delays select delay >= DelayMinimum ? delay : DelayDefault).ToArray();

            FrameCount = Delays.Length;
            Period = TimeSpan.FromTicks((from d in Delays select d.Ticks).Sum());
            
            this.bitmapsPrivider = bitmapsPrivider;
            bitmaps = new Bitmap[FrameCount];
            ApplyList = new Queue<Func<Bitmap, Bitmap>>[FrameCount];
        }

        public static BitmapEx ConvertToBitmapEx(Bitmap bitmap) => bitmap as BitmapEx ??
                new BitmapEx((new Bitmap[1] { bitmap } as IEnumerable<Bitmap>).GetEnumerator(), new TimeSpan[1] { TimeSpan.Zero });

        public Bitmap GetBitmap(int index)
        {
            var result = bitmaps[index];
            if (result == null)
            {
                var firstNull = index - 1;
                while (firstNull >= 0 && bitmaps[firstNull] == null) firstNull--;
                firstNull++;
                var fc1 = FrameCount - 1;
                for (var i = firstNull; i <= index; i++)
                {
                    result = null;
                    try
                    {
                        bitmapsPrivider.MoveNext();
                        //var x = (new string[1] { "a" } as IEnumerable<string>).GetEnumerator().Current;
                        result = bitmapsPrivider.Current;
                    }
                    catch
                    {
                        if (bitmapsPrivider != null)
                        {
                            bitmapsPrivider.Dispose();
                            bitmapsPrivider = null;
                        }

                        //new Size(-210, -297);
                        int width, height;
                        if (i > 0)
                        {
                            var bitmap = bitmaps[0];
                            width = bitmap.Width;
                            height = bitmap.Height;
                        }
                        else
                        {
                            width = 210;
                            height = 297;
                        }

                        result?.Dispose();
                        result = Program.GetErrorImage(width, height);
                    }
                    bitmaps[i] = result;
                    if (i == fc1)
                    {
                        if (bitmapsPrivider != null)
                        {
                            bitmapsPrivider.Dispose();
                            bitmapsPrivider = null;
                        }
                    }
                }
            }

            var que = ApplyList[index];
            if (que != null)
            {
                var size = result;
                try
                {
                    while (que.Count > 0)
                    {
                        result = que.Dequeue()(result);
                    }
                }
#if DEBUG
                catch (Exception e)
                {
                    var message = e.ToString();
#else
                catch
                {
#endif
                    result.Dispose();
                    result = Program.GetErrorImage(size.Width, size.Height);
                }
                ApplyList[index] = null;
                bitmaps[index] = result;
            }

            return result;
        }

        protected override System.Drawing.Bitmap BaseBitmap { get => (System.Drawing.Bitmap)GetBitmap(0); set { } } // setter は base のコンストラクタでしか使わないので不要
        public readonly int FrameCount;

        private Queue<Func<Bitmap, Bitmap>>[] ApplyList;

        /// <summary>
        /// replacer が引数と異なるものを返す場合引数の dispose は呼び出し側で行うこと
        /// </summary>
        /// <param name="replacer"></param>
        public void ApplyToAll(Func<Bitmap, Bitmap> replacer)
        {
            for (var i = 0; i < FrameCount; i++)
            {
                var que = ApplyList[i] ?? new Queue<Func<Bitmap, Bitmap>>();
                que.Enqueue(replacer);
                ApplyList[i] = que;
                //bitmaps[i] = replacer(GetBitmap(i));
            }
            //BaseBitmap = (System.Drawing.Bitmap)(Image)GetBitmap(0);
        }

        /// <summary>
        /// CreateNew と Apply 開始時に false に設定される。その関数内で true にすると処理がキャンセルされる。CreateNew に限りキャンセル時はキャンセル前に作成されたインスタンスが Dispose される。
        /// </summary>
        public bool CancelApply;

        public long GetDataSizeInBytes()
        {
            // 遅延読み込みが無駄になるので廃止

            var bytesPerPixel = Image.GetPixelFormatSize(PixelFormat) >> 3;
            var stride = (Width * bytesPerPixel + 3) & ~3L;
            return stride * Height * FrameCount;
        }

        public static ulong GetDataSizeInBytes(BitmapEx bmpEx, Size newSize)
        {
            var result = 0UL;
            var newWidth = (ulong)newSize.Width;
            var newHeight = (ulong)newSize.Height;
            var fileCount = bmpEx.FrameCount;
            for (var i = 0; i < fileCount; i++)
            {
                var bmp = bmpEx.GetBitmap(i);
                var bytesPerPixel = (ulong)Image.GetPixelFormatSize(bmp.PixelFormat) >> 3;
                var stride = (newWidth * bytesPerPixel + 3) & ~3UL;
                result += stride * newHeight;
            }
            return result;
        }

        /// <summary>
        /// remaker は必ず新しいインスタンスを返すこと
        /// remaker が例外を投げた場合、それ以前に作られたインスタンスは自動的に破棄される
        /// </summary>
        /// <param name="remaker"></param>
        public BitmapEx CreateNew(Func<Bitmap, Bitmap> remaker)
        {
            var bitmaps = new Bitmap[FrameCount];
            //var delays = new TimeSpan[FrameCount];
            //var frames = new Tuple<Bitmap, TimeSpan>[FrameCount];
            try
            {
                CancelApply = false;
                for (var i = 0; i < FrameCount; i++)
                {
                    var oldBitmap = GetBitmap(i);
                    var newBitmap = remaker(oldBitmap);
                    if (CancelApply)
                    {
                        CancelApply = false;
                        newBitmap?.Dispose();
                        for (var j = 0; j < i; j++) bitmaps[j]?.Dispose();
                        return null;
                    }
                    if (newBitmap == oldBitmap) throw new Exception("remaker must create new instance.");
                    bitmaps[i] = newBitmap ?? throw new Exception("remaker must not return null.");
                }
            }
            catch
            {
                for (var i = 0; i < FrameCount; i++)
                {
                    bitmaps[i]?.Dispose();
                    //if (frame != null) frame.Item1.Dispose();
                }
                throw;
            }
            return new BitmapEx((bitmaps as IEnumerable<Bitmap>).GetEnumerator(), Delays);
        }

        private DateTime startTime;
        public void Start(DateTime dateTime)
        {
            startTime = dateTime;
        }
        public int GetCurrentIndex(DateTime dateTime)
        {
            if (FrameCount == 1 || startTime == default(DateTime)) return 0;
            var subtract = (dateTime - startTime).Ticks % Period.Ticks;
            for (var i = 0; i < FrameCount; i++)
            {
                subtract -= Delays[i].Ticks;
                if (subtract < 0) return i;
            }
            return 0;
        }
        public DateTime GetNextChangeTime(DateTime dateTime)
        {
            if (FrameCount == 1 || startTime == default(DateTime)) return dateTime;
            var subtract = (dateTime - startTime).Ticks % Period.Ticks;
            for (var i = 0; i < FrameCount; i++)
            {
                subtract -= Delays[i].Ticks;
                if (subtract < 0) return dateTime + TimeSpan.FromTicks(-subtract);
            }
            return dateTime;
        }

        private bool disposed = false;
        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                base.Dispose(disposing);
                for (var i = 1; i < bitmaps.Length; i++)
                {
                    bitmaps[i]?.Dispose();
                }
                if (bitmapsPrivider != null)
                {
                    bitmapsPrivider.Dispose();
                    bitmapsPrivider = null;
                }
                disposed = true;
            }
        }
    }

    // シール型の System.Drawing.Bitmap を置き換える
    public class Bitmap : IDisposable, ICloneable
    {
        protected virtual System.Drawing.Bitmap BaseBitmap { get; set; }
        public Bitmap(System.Drawing.Bitmap systemBitmap)
        {
            BaseBitmap = systemBitmap;
        }

        public Bitmap(Image image)
        {
            BaseBitmap = new System.Drawing.Bitmap(image);
        }

        public Bitmap(Bitmap bitmap)
        {
            BaseBitmap = new System.Drawing.Bitmap(bitmap.BaseBitmap);
        }
        
        public Bitmap(Stream stream)
        {
            BaseBitmap = new System.Drawing.Bitmap(stream);
        }

        public Bitmap(string filename)
        {
            BaseBitmap = new System.Drawing.Bitmap(filename);
        }


        public Bitmap(int width, int height)
        {
            BaseBitmap = new System.Drawing.Bitmap(width, height);
        }

        public Bitmap(int width, int height, System.Drawing.Imaging.PixelFormat format)
        {
            BaseBitmap = new System.Drawing.Bitmap(width, height, format);
        }

        public Bitmap(int width, int height, int stride, System.Drawing.Imaging.PixelFormat format, IntPtr scan0)
        {
            BaseBitmap = new System.Drawing.Bitmap(width, height, stride, format, scan0);
        }
        
        public static implicit operator System.Drawing.Image(Bitmap bitmap)
        {
            return bitmap.BaseBitmap;
        }
        
        public static explicit operator Bitmap(System.Drawing.Bitmap systemBitmap)
        {
            return new Bitmap(systemBitmap);
        }
        
        public object Clone()
        {
            return new Bitmap(BaseBitmap.Clone() as System.Drawing.Bitmap);
        }

        public int Width { get { return BaseBitmap.Width; } }
        public int Height { get { return BaseBitmap.Height; } }
        public Size Size { get { return BaseBitmap.Size; } }
        public System.Drawing.Imaging.PixelFormat PixelFormat { get { return BaseBitmap.PixelFormat; } }
        public System.Drawing.Imaging.ColorPalette Palette { get { return BaseBitmap.Palette; } set { BaseBitmap.Palette = value; } }
        public float HorizontalResolution { get { return BaseBitmap.HorizontalResolution; } }
        public float VerticalResolution { get { return BaseBitmap.VerticalResolution; } }
        public System.Drawing.Imaging.BitmapData LockBits(Rectangle rect, System.Drawing.Imaging.ImageLockMode flags, System.Drawing.Imaging.PixelFormat format)
        {
            return BaseBitmap.LockBits(rect, flags, format);
        }
        public System.Drawing.Imaging.BitmapData LockBits(Rectangle rect, System.Drawing.Imaging.ImageLockMode flags, System.Drawing.Imaging.PixelFormat format, System.Drawing.Imaging.BitmapData bitmapData)
        {
            return BaseBitmap.LockBits(rect, flags, format, bitmapData);
        }
        public void UnlockBits(System.Drawing.Imaging.BitmapData bitmapdata)
        {
            BaseBitmap.UnlockBits(bitmapdata);
        }
        public Color GetPixel(int x, int y)
        {
            return BaseBitmap.GetPixel(x, y);
        }
        public void SetPixel(int x, int y, Color color)
        {
            BaseBitmap.SetPixel(x, y, color);
        }
        public void Save(string filename)
        {
            BaseBitmap.Save(filename);
        }
        public void Save(string filename, System.Drawing.Imaging.ImageFormat format)
        {
            BaseBitmap.Save(filename, format);
        }
        public void Save(Stream stream, System.Drawing.Imaging.ImageFormat format)
        {
            BaseBitmap.Save(stream, format);
        }
        public IntPtr GetHbitmap()
        {
            return BaseBitmap.GetHbitmap();
        }
        public void SetResolution(float xDpi, float yDpi)
        {
            BaseBitmap.SetResolution(xDpi, yDpi);
        }

#region IDisposable Support
        private bool disposedValue = false; // 重複する呼び出しを検出するには

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: マネージ状態を破棄します (マネージ オブジェクト)。
                    BaseBitmap.Dispose();
                }

                // TODO: アンマネージ リソース (アンマネージ オブジェクト) を解放し、下のファイナライザーをオーバーライドします。
                // TODO: 大きなフィールドを null に設定します。

                disposedValue = true;
            }
        }

        // TODO: 上の Dispose(bool disposing) にアンマネージ リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします。
        // ~Bitmap() {
        //   // このコードを変更しないでください。クリーンアップ コードを上の Dispose(bool disposing) に記述します。
        //   Dispose(false);
        // }

        // このコードは、破棄可能なパターンを正しく実装できるように追加されました。
        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを上の Dispose(bool disposing) に記述します。
            Dispose(true);
            // TODO: 上のファイナライザーがオーバーライドされる場合は、次の行のコメントを解除してください。
            // GC.SuppressFinalize(this);
        }

#endregion
    }
}
