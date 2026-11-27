using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipPla
{
    public static class BitmapResizer
    {

        public static Bitmap Load(string fileName)
        {
            using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {
                return Load(fs);
            }
        }

        public static Bitmap Load(Stream stream)
        {
            return GetBitmap(LoadAsBitmapImage(stream));
        }

        private static BitmapImage LoadAsBitmapImage(Stream stream)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            return image;
        }


        /// <param name="keepGrayscale">
        /// true の場合、入力が恒等グレースケールパレット（i → (i,i,i)）の 8bppIndexed なら、8bppIndexed のまま縮小して返す。
        /// false（既定）の場合、Indexed 形式は 32bppArgb に変換して縮小する（従来どおり）。
        /// </param>
        public static Bitmap CreateNew(Bitmap bmp, Size size, bool keepGrayscale = false)
        {
            return CreateNew(bmp, size.Width, size.Height, keepGrayscale);
        }

        public static Bitmap CreateNew(Bitmap bmp, int width, int height, bool keepGrayscale = false)
        {
            var result = CreateNew(bmp, (double)width / bmp.Width, (double)height / bmp.Height, keepGrayscale);
#if !AUTOBUILD
            if (result.Width != width || result.Height != height)
            {
                System.Windows.Forms.MessageBox.Show($"{result.Width}x{result.Height} != {width}x{height}");
            }
#endif
            return result;
        }

        public static Bitmap CreateNew(Bitmap bmp, double scaleX, double scaleY, bool keepGrayscale = false)
        {
            if (keepGrayscale && IsIdentityGrayscale(bmp))
            {
                // グレースケールを Bgra32 に広げると入力のコピーと出力が 4 倍の大きさになるので、Gray8 のまま縮小する（C2）
                var graySource = GetBitmapSource(bmp, new Rectangle(Point.Empty, bmp.Size),
                    System.Drawing.Imaging.PixelFormat.Format8bppIndexed, PixelFormats.Gray8);
                BitmapSource scaled = new TransformedBitmap(graySource, new ScaleTransform(scaleX, scaleY));
                if (scaled.Format != PixelFormats.Gray8)
                {
                    scaled = new FormatConvertedBitmap(scaled, PixelFormats.Gray8, null, 0);
                }
                return GetGrayscaleBitmap(scaled);
            }

            var transformedBitmap = new TransformedBitmap(GetBitmapSource(bmp), new ScaleTransform(scaleX, scaleY));
            return GetBitmap(transformedBitmap);
        }

        /// <summary>
        /// 8bppIndexed で、パレットが i → (i,i,i)（不透明）の恒等グレースケールかどうか
        /// （PixelFormatConverter.ToBitmap8Unsafe が作る形式。インデックス値をそのまま輝度として扱える）
        /// </summary>
        internal static bool IsIdentityGrayscale(Bitmap bmp)
        {
            if (bmp.PixelFormat != System.Drawing.Imaging.PixelFormat.Format8bppIndexed) return false;
            var entries = bmp.Palette.Entries;
            if (entries.Length != 256) return false;
            for (var i = 0; i < entries.Length; i++)
            {
                var c = entries[i];
                if (c.A != 255 || c.R != i || c.G != i || c.B != i) return false;
            }
            return true;
        }

        private static Bitmap GetGrayscaleBitmap(BitmapSource source)
        {
            const System.Drawing.Imaging.PixelFormat format = System.Drawing.Imaging.PixelFormat.Format8bppIndexed;
            var bitmap = new Bitmap(source.PixelWidth, source.PixelHeight, format);
            try
            {
                var palette = bitmap.Palette;
                var entries = palette.Entries;
                for (var i = 0; i < entries.Length; i++)
                {
                    entries[i] = System.Drawing.Color.FromArgb(i, i, i);
                }
                bitmap.Palette = palette;
                SetToBitmap(source, bitmap, 0, format);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
            return bitmap;
        }
        
        const System.Drawing.Imaging.PixelFormat PixelFormat_Format32bppCMYK = ZipPla.PixelFormatConverter.PixelFormat_Format32bppCMYK;
        
        public static PixelFormat PixelFormatConverter(System.Drawing.Imaging.PixelFormat pixelFormat)
        {
            switch(pixelFormat)
            {
                case System.Drawing.Imaging.PixelFormat.Format24bppRgb: return PixelFormats.Bgr24;
                case System.Drawing.Imaging.PixelFormat.Format32bppRgb: return PixelFormats.Bgr32;
                case System.Drawing.Imaging.PixelFormat.Format32bppArgb: return PixelFormats.Bgra32;
                case System.Drawing.Imaging.PixelFormat.Format32bppPArgb: return PixelFormats.Pbgra32;
                case System.Drawing.Imaging.PixelFormat.Format16bppArgb1555: return PixelFormats.Default;
                case System.Drawing.Imaging.PixelFormat.Format16bppGrayScale: return PixelFormats.Gray16;
                case System.Drawing.Imaging.PixelFormat.Format16bppRgb555: return PixelFormats.Bgr555;
                case System.Drawing.Imaging.PixelFormat.Format16bppRgb565: return PixelFormats.Bgr565;
                case System.Drawing.Imaging.PixelFormat.Format1bppIndexed: return PixelFormats.Indexed1;
                case System.Drawing.Imaging.PixelFormat.Format48bppRgb: return PixelFormats.Rgb48;
                case System.Drawing.Imaging.PixelFormat.Format4bppIndexed: return PixelFormats.Indexed4;
                case System.Drawing.Imaging.PixelFormat.Format64bppArgb: return PixelFormats.Rgba64;
                case System.Drawing.Imaging.PixelFormat.Format64bppPArgb: return PixelFormats.Prgba64;
                case System.Drawing.Imaging.PixelFormat.Format8bppIndexed: return PixelFormats.Indexed8;
                case PixelFormat_Format32bppCMYK: return PixelFormats.Cmyk32;
                default: return PixelFormats.Default;
            }
        }

        public static System.Drawing.Imaging.PixelFormat PixelFormatConverter(PixelFormat pixelFormat)
        {
            if (pixelFormat == PixelFormats.Bgr24) return System.Drawing.Imaging.PixelFormat.Format24bppRgb;
            if (pixelFormat == PixelFormats.Bgr32) return System.Drawing.Imaging.PixelFormat.Format32bppRgb;
            if (pixelFormat == PixelFormats.Bgra32) return System.Drawing.Imaging.PixelFormat.Format32bppArgb;
            if (pixelFormat == PixelFormats.Pbgra32) return System.Drawing.Imaging.PixelFormat.Format32bppPArgb;
            if (pixelFormat == PixelFormats.Bgr555) return System.Drawing.Imaging.PixelFormat.Format16bppRgb555;
            if (pixelFormat == PixelFormats.Bgr565) return System.Drawing.Imaging.PixelFormat.Format16bppRgb565;
            if (pixelFormat == PixelFormats.Gray16) return System.Drawing.Imaging.PixelFormat.Format16bppGrayScale;
            if (pixelFormat == PixelFormats.Indexed1) return System.Drawing.Imaging.PixelFormat.Format1bppIndexed;
            if (pixelFormat == PixelFormats.Indexed4) return System.Drawing.Imaging.PixelFormat.Format4bppIndexed;
            if (pixelFormat == PixelFormats.Indexed8) return System.Drawing.Imaging.PixelFormat.Format8bppIndexed;
            if (pixelFormat == PixelFormats.Prgba64) return System.Drawing.Imaging.PixelFormat.Format64bppPArgb;
            if (pixelFormat == PixelFormats.Rgba64) return System.Drawing.Imaging.PixelFormat.Format64bppArgb;
            if (pixelFormat == PixelFormats.Cmyk32) return PixelFormat_Format32bppCMYK;
            return System.Drawing.Imaging.PixelFormat.Undefined;
        }

        private static bool IsIndexed(PixelFormat pixelFormat)
        {
            return pixelFormat == PixelFormats.Indexed1 || pixelFormat == PixelFormats.Indexed4 ||
                pixelFormat == PixelFormats.Indexed8 || pixelFormat == PixelFormats.Indexed2;
        }

        public static BitmapSource GetBitmapSource(Bitmap bitmap)
        {
            return GetBitmapSource(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        }

        public static BitmapSource GetBitmapSource(Bitmap bitmap, Rectangle rect)
        {
            var pixelFormat1 = bitmap.PixelFormat;
            var pixelFormat2 = PixelFormatConverter(pixelFormat1);
            if (pixelFormat2 == PixelFormats.Default || IsIndexed(pixelFormat2))
            {
                pixelFormat1 = System.Drawing.Imaging.PixelFormat.Format32bppArgb;
                pixelFormat2 = PixelFormats.Bgra32;
            }
            return GetBitmapSource(bitmap, rect, pixelFormat1, pixelFormat2);
        }

        private static BitmapSource GetBitmapSource(Bitmap bitmap, Rectangle rect,
            System.Drawing.Imaging.PixelFormat pixelFormat1, PixelFormat pixelFormat2)
        {
            var data = bitmap.LockBits(
                    rect,
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    pixelFormat1);
            try
            {
                return BitmapSource.Create(
                    data.Width, data.Height,
                    (int)bitmap.HorizontalResolution, (int)bitmap.VerticalResolution,
                    pixelFormat2,
                    BitmapPalettes.BlackAndWhite,
                    data.Scan0,
                    data.Height * data.Stride,
                    data.Stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public static Bitmap GetBitmap(BitmapSource source)
        {
            return GetBitmap(source, PixelFormatConverter(source.Format));
        }

        private static Bitmap GetBitmap(BitmapSource source, System.Drawing.Imaging.PixelFormat pixelFormat)
        {
            var bitmap = new Bitmap(source.PixelWidth, source.PixelHeight, pixelFormat);
            try
            {
                SetToBitmap(source, bitmap, 0, pixelFormat);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
            return bitmap;
        }

        private static void SetToBitmap(BitmapSource src, Bitmap dst, int yOffset, System.Drawing.Imaging.PixelFormat pixelFormat)
        {
            var data = dst.LockBits(new Rectangle(0, yOffset, src.PixelWidth, src.PixelHeight),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, pixelFormat);
            try
            {
                src.CopyPixels(System.Windows.Int32Rect.Empty,
                    data.Scan0 + yOffset * data.Stride, data.Height * data.Stride, data.Stride);
            }
            finally
            {
                dst.UnlockBits(data);
            }
        }
    }
}
