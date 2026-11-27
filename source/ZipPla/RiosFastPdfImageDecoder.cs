using iTextSharp.text.error_messages;
using iTextSharp.text.exceptions;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.codec;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZipPla
{
    public static class RiosFastPdfImageDecoder
    {
        public enum Type { PNG, JPG, JP2, CCITT, JBIG2 }
        private static Type convert(PdfImageObject.ImageBytesType type)
        {
            if (type == PdfImageObject.ImageBytesType.PNG) return Type.PNG;
            if (type == PdfImageObject.ImageBytesType.JPG) return Type.JPG;
            if (type == PdfImageObject.ImageBytesType.JP2) return Type.JP2;
            if (type == PdfImageObject.ImageBytesType.CCITT) return Type.CCITT;
            if (type == PdfImageObject.ImageBytesType.JBIG2) return Type.JBIG2;
            throw new Exception();
        }

        public static MemoryStream Decode(PRStream stream, out Type type)
        {
            var image = new PdfImageObject(stream);
            type = convert(image.GetImageBytesType());
            return new MemoryStream(image.GetImageAsBytes());
        }

        /// <summary>
        /// TryDecodeToBitmap と同じく Bitmap を直接作り、作れなければ Decode と同じ結果（PNG・TIFF などのバイト列）を bytes に返す（Y12）。
        /// 以前は Bitmap を作れなかった画像（CMYK・16 ビットなど）で、TryDecodeToBitmap が展開した結果を捨てて Decode が最初から展開し直していた
        /// </summary>
        public static Bitmap DecodeToBitmapOrStream(PRStream stream, out MemoryStream bytes, out Type type)
        {
            PdfImageObject image = null;
            try
            {
                image = new PdfImageObject(stream, directBitmap: true, continueIfNoBitmap: true);
            }
            catch
            {
                image = null; // 従来の Decode で読み直す（例外も従来どおりそちらで出る）
            }
            if (image?.DirectBitmap != null)
            {
                bytes = null;
                type = Type.PNG;
                return image.DirectBitmap;
            }
            if (image == null || image.GetImageBytesType() == null)
            {
                // JPEG・JPX・JBIG2（展開する前に除いた）や例外の場合は、従来どおり最初から読む
                bytes = Decode(stream, out type);
                return null;
            }
            type = convert(image.GetImageBytesType());
            bytes = new MemoryStream(image.GetImageAsBytes());
            return null;
        }


        private class PdfImageObject
        {

            /**
             * Different types of data that can be stored in the bytes of a {@link PdfImageObject}
             * @since 5.0.4
             */
            public sealed class ImageBytesType
            {
                public static readonly ImageBytesType PNG = new ImageBytesType("png"); // the stream contains png encoded data
                public static readonly ImageBytesType JPG = new ImageBytesType("jpg"); // the stream contains jpg encoded data
                public static readonly ImageBytesType JP2 = new ImageBytesType("jp2"); // the stream contains jp2 encoded data
                public static readonly ImageBytesType CCITT = new ImageBytesType("tif"); // the stream contains ccitt encoded data
                public static readonly ImageBytesType JBIG2 = new ImageBytesType("jbig2"); // the stream contains JBIG2 encoded data

                /**
                 * the recommended file extension for streams of this type
                 */
                private String fileExtension;

                /**
                 * @param fileExtension the recommended file extension for use with data of this type (for example, if the bytes were just saved to a file, what extension should the file have)
                 */
                private ImageBytesType(String fileExtension)
                {
                    this.fileExtension = fileExtension;
                }

                /**
                 * @return the file extension registered when this type was created
                 */
                public String FileExtension
                {
                    get
                    {
                        return fileExtension;
                    }
                }
            }

            /**
             * A filter that does nothing, but keeps track of the filter type that was used
             * @since 5.0.4 
             */
            private sealed class TrackingFilter : FilterHandlers.IFilterHandler
            {
                public PdfName lastFilterName = null;

                public byte[] Decode(byte[] b, PdfName filterName, PdfObject decodeParams, PdfDictionary streamDictionary)
                {
                    lastFilterName = filterName;
                    return b;
                }
            }

            /** The image dictionary. */
            private PdfDictionary dictionary;
            /** The decoded image bytes (after applying filters), or the raw image bytes if unable to decode */
            private byte[] imageBytes;
            private PdfDictionary colorSpaceDic;

            private int pngColorType = -1;
            private int pngBitDepth;
            private int width;
            private int height;
            private int bpc;
            private byte[] palette;
            private byte[] icc;
            private int stride;

            /**
             * Tracks the type of data that is actually stored in the streamBytes member
             */
            private ImageBytesType streamContentType = null;

            virtual public string GetFileType()
            {
                return streamContentType.FileExtension;
            }

            /**
             * @return the type of image data that is returned by getImageBytes()
             */
            virtual public ImageBytesType GetImageBytesType()
            {
                return streamContentType;
            }

            /**
             * Creates a PdfImage object.
             * @param stream a PRStream
             * @throws IOException
             */
            public PdfImageObject(PRStream stream) : this(stream, PdfReader.GetStreamBytesRaw(stream), null)
            {
            }

            // directBitmap が true なら、PNG に作り直さずに Bitmap を直接作る（作れなければ DirectBitmap は null で、それ以外は何もしない）（D4）
            // continueIfNoBitmap が true なら、展開した後で Bitmap を作れなかったとき、同じ展開結果から従来どおり PNG・TIFF を作る（Y12）
            public PdfImageObject(PRStream stream, bool directBitmap, bool continueIfNoBitmap)
            {
                this.directBitmap = directBitmap;
                this.continueIfNoBitmap = continueIfNoBitmap;
                Initialize(stream, PdfReader.GetStreamBytesRaw(stream), null);
            }
            private readonly bool directBitmap;
            private readonly bool continueIfNoBitmap;
            public Bitmap DirectBitmap { get; private set; }

            /**
             * Creates a PdfImage object.
             * @param stream a PRStream
             * @param colorSpaceDic a color space dictionary
             * @throws IOException
             */
            public PdfImageObject(PRStream stream, PdfDictionary colorSpaceDic) : this(stream, PdfReader.GetStreamBytesRaw(stream), colorSpaceDic)
            {
            }

            /**
             * Creats a PdfImage object using an explicitly provided dictionary and image bytes
             * @param dictionary the dictionary for the image
             * @param samples the samples
             * @since 5.0.3
             */
            protected internal PdfImageObject(PdfDictionary dictionary, byte[] samples, PdfDictionary colorSpaceDic)
            {
                Initialize(dictionary, samples, colorSpaceDic);
            }

            private void Initialize(PdfDictionary dictionary, byte[] samples, PdfDictionary colorSpaceDic)
            {
                if (directBitmap)
                {
                    // JPEG・JPX・JBIG2 は対象外。展開する前に除く
                    var filter = PdfReader.GetPdfObject(dictionary.Get(PdfName.FILTER));
                    var filters = filter is PdfArray array ? array.ArrayList.Select(f => PdfReader.GetPdfObject(f)).ToArray() : new[] { filter };
                    if (filters.Any(f => PdfName.DCTDECODE.Equals(f) || PdfName.JPXDECODE.Equals(f) || PdfName.JBIG2DECODE.Equals(f))) return;
                }
                this.dictionary = dictionary;
                this.colorSpaceDic = colorSpaceDic;
                TrackingFilter trackingFilter = new TrackingFilter();
                IDictionary<PdfName, FilterHandlers.IFilterHandler> handlers = new Dictionary<PdfName, FilterHandlers.IFilterHandler>(FilterHandlers.GetDefaultFilterHandlers());
                handlers[PdfName.JBIG2DECODE] = trackingFilter;
                handlers[PdfName.DCTDECODE] = trackingFilter;
                handlers[PdfName.JPXDECODE] = trackingFilter;

                imageBytes = PdfReader.DecodeBytes(samples, dictionary, handlers);

                if (trackingFilter.lastFilterName != null)
                {
                    if (PdfName.JBIG2DECODE.Equals(trackingFilter.lastFilterName))
                        streamContentType = ImageBytesType.JBIG2;
                    else if (PdfName.DCTDECODE.Equals(trackingFilter.lastFilterName))
                        streamContentType = ImageBytesType.JPG;
                    else if (PdfName.JPXDECODE.Equals(trackingFilter.lastFilterName))
                        streamContentType = ImageBytesType.JP2;
                }
                else
                {
                    DecodeImageBytes();
                }
            }

            /**
             * Returns an entry from the image dictionary.
             * @param key a key
             * @return the value
             */
            virtual public PdfObject Get(PdfName key)
            {
                return dictionary.Get(key);
            }

            /**
             * Returns the image dictionary.
             * @return the dictionary
             */
            virtual public PdfDictionary GetDictionary()
            {
                return dictionary;
            }

            /**
             * Sets state of this object according to the color space 
             * @param colorspace the colorspace to use
             * @param allowIndexed whether indexed color spaces will be resolved (used for recursive call)
             * @throws IOException if there is a problem with reading from the underlying stream  
             */
            private void FindColorspace(PdfObject colorspace, bool allowIndexed)
            {
                if (colorspace == null && bpc == 1)
                { // handle imagemasks
                    stride = (width * bpc + 7) / 8;
                    pngColorType = 0;
                }
                else if (PdfName.DEVICEGRAY.Equals(colorspace))
                {
                    stride = (width * bpc + 7) / 8;
                    pngColorType = 0;
                }
                else if (PdfName.DEVICERGB.Equals(colorspace))
                {
                    if (bpc == 8 || bpc == 16)
                    {
                        stride = (width * bpc * 3 + 7) / 8;
                        pngColorType = 2;
                    }
                }
                else if (colorspace is PdfArray)
                {
                    PdfArray ca = (PdfArray)colorspace;
                    PdfObject tyca = ca.GetDirectObject(0);
                    if (PdfName.CALGRAY.Equals(tyca))
                    {
                        stride = (width * bpc + 7) / 8;
                        pngColorType = 0;
                    }
                    else if (PdfName.CALRGB.Equals(tyca))
                    {
                        if (bpc == 8 || bpc == 16)
                        {
                            stride = (width * bpc * 3 + 7) / 8;
                            pngColorType = 2;
                        }
                    }
                    else if (PdfName.ICCBASED.Equals(tyca))
                    {
                        PRStream pr = (PRStream)ca.GetDirectObject(1);
                        int n = pr.GetAsNumber(PdfName.N).IntValue;
                        if (n == 1)
                        {
                            stride = (width * bpc + 7) / 8;
                            pngColorType = 0;
                            icc = PdfReader.GetStreamBytes(pr);
                        }
                        else if (n == 3)
                        {
                            stride = (width * bpc * 3 + 7) / 8;
                            pngColorType = 2;
                            icc = PdfReader.GetStreamBytes(pr);
                        }
                    }
                    else if (allowIndexed && PdfName.INDEXED.Equals(tyca))
                    {
                        // パレットの色は画素のビット数によらず 1 成分 8 bit。基底の色空間は 8 bit として調べる
                        // （以前は画素のビット数で調べたので、4 bit などのインデックスの画像を DeviceRGB と認めず読めなかった）
                        var indexBpc = bpc;
                        bpc = 8;
                        try
                        {
                            FindColorspace(ca.GetDirectObject(1), false);
                        }
                        finally
                        {
                            bpc = indexBpc;
                        }
                        if (pngColorType == 2 || pngColorType == 0)
                        {
                            PdfObject id2 = ca.GetDirectObject(3);
                            if (id2 is PdfString)
                            {
                                palette = ((PdfString)id2).GetBytes();
                            }
                            else if (id2 is PRStream)
                            {
                                palette = PdfReader.GetStreamBytes(((PRStream)id2));
                            }
                            if (pngColorType == 0)
                            {
                                // 基底がグレーのパレットは 1 色 1 バイトなので、PNG のパレット（RGB）に広げる。
                                // 以前は stride が 8 bit のまま残り、インデックスの値をそのままグレーとして扱っていた
                                if (palette != null)
                                {
                                    var rgb = new byte[palette.Length * 3];
                                    for (int i = 0, j = 0; i < palette.Length; i++, j += 3)
                                    {
                                        rgb[j] = rgb[j + 1] = rgb[j + 2] = palette[i];
                                    }
                                    palette = rgb;
                                }
                                icc = null; // グレーの ICC プロファイルはパレットの PNG には付けられない
                            }
                            stride = (width * bpc + 7) / 8;
                            pngColorType = 3;
                        }
                    }
                }
            }

            /**
             * decodes the bytes currently captured in the streamBytes and replaces it with an image representation of the bytes
             * (this will either be a png or a tiff, depending on the color depth of the image)
             * @throws IOException
             */
            private void DecodeImageBytes()
            {
                if (streamContentType != null)
                    throw new ArgumentException(MessageLocalization.GetComposedMessage("Decoding.can't.happen.on.this.type.of.stream.(.1.)", streamContentType.FileExtension));
                pngColorType = -1;
                PdfArray decode = dictionary.GetAsArray(PdfName.DECODE);
                width = dictionary.GetAsNumber(PdfName.WIDTH).IntValue;
                height = dictionary.GetAsNumber(PdfName.HEIGHT).IntValue;
                bpc = dictionary.GetAsNumber(PdfName.BITSPERCOMPONENT).IntValue;
                pngBitDepth = bpc;
                PdfObject colorspace = dictionary.GetDirectObject(PdfName.COLORSPACE);
                if (colorspace is PdfName && colorSpaceDic != null)
                {
                    PdfObject csLookup = colorSpaceDic.GetDirectObject((PdfName)colorspace);
                    if (csLookup != null)
                        colorspace = csLookup;
                }

                palette = null;
                icc = null;
                stride = 0;
                FindColorspace(colorspace, true);
                if (directBitmap && pngColorType < 0 && !continueIfNoBitmap) return; // CMYK などは従来どおり（D4）
                MemoryStream ms = new MemoryStream();
                if (pngColorType < 0)
                {
                    if (bpc != 8)
                        throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("the.color.depth.1.is.not.supported", bpc));
                    if (PdfName.DEVICECMYK.Equals(colorspace))
                    {
                    }
                    else if (colorspace is PdfArray)
                    {
                        PdfArray ca = (PdfArray)colorspace;
                        PdfObject tyca = ca.GetDirectObject(0);
                        if (!PdfName.ICCBASED.Equals(tyca))
                            throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("the.color.space.1.is.not.supported", colorspace));
                        PRStream pr = (PRStream)ca.GetDirectObject(1);
                        int n = pr.GetAsNumber(PdfName.N).IntValue;
                        if (n != 4)
                        {
                            throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("N.value.1.is.not.supported", n));
                        }
                        icc = PdfReader.GetStreamBytes(pr);
                    }
                    else
                        throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("the.color.space.1.is.not.supported", colorspace));
                    stride = 4 * width;
                    TiffWriter wr = new TiffWriter();
                    wr.AddField(new TiffWriter.FieldShort(TIFFConstants.TIFFTAG_SAMPLESPERPIXEL, 4));
                    wr.AddField(new TiffWriter.FieldShort(TIFFConstants.TIFFTAG_BITSPERSAMPLE, new int[] { 8, 8, 8, 8 }));
                    wr.AddField(new TiffWriter.FieldShort(TIFFConstants.TIFFTAG_PHOTOMETRIC, TIFFConstants.PHOTOMETRIC_SEPARATED));
                    wr.AddField(new TiffWriter.FieldLong(TIFFConstants.TIFFTAG_IMAGEWIDTH, width));
                    wr.AddField(new TiffWriter.FieldLong(TIFFConstants.TIFFTAG_IMAGELENGTH, height));
                    wr.AddField(new TiffWriter.FieldShort(TIFFConstants.TIFFTAG_COMPRESSION, TIFFConstants.COMPRESSION_NONE));
                    wr.AddField(new TiffWriter.FieldShort(TIFFConstants.TIFFTAG_PREDICTOR, TIFFConstants.PREDICTOR_HORIZONTAL_DIFFERENCING));
                    wr.AddField(new TiffWriter.FieldLong(TIFFConstants.TIFFTAG_ROWSPERSTRIP, height));
                    wr.AddField(new TiffWriter.FieldRational(TIFFConstants.TIFFTAG_XRESOLUTION, new int[] { 300, 1 }));
                    wr.AddField(new TiffWriter.FieldRational(TIFFConstants.TIFFTAG_YRESOLUTION, new int[] { 300, 1 }));
                    wr.AddField(new TiffWriter.FieldShort(TIFFConstants.TIFFTAG_RESOLUTIONUNIT, TIFFConstants.RESUNIT_INCH));
                    wr.AddField(new TiffWriter.FieldAscii(TIFFConstants.TIFFTAG_SOFTWARE, iTextSharp.text.Version.GetInstance().GetVersion));
                    byte[] buf = imageBytes;
                    wr.AddField(new TiffWriter.FieldImage(buf));
                    wr.AddField(new TiffWriter.FieldLong(TIFFConstants.TIFFTAG_STRIPBYTECOUNTS, buf.Length));
                    if (icc != null)
                        wr.AddField(new TiffWriter.FieldUndefined(TIFFConstants.TIFFTAG_ICCPROFILE, icc));
                    wr.WriteFile(ms);
                    streamContentType = ImageBytesType.CCITT;
                    imageBytes = ms.ToArray();
                    return;
                }
                else
                {
                    var inverted = false;
                    if (directBitmap)
                    {
                        // 1 ビットの /Decode [1 0] は従来どおり反転する
                        if (decode != null && pngBitDepth == 1 && decode.GetAsNumber(0).IntValue == 1 && decode.GetAsNumber(1).IntValue == 0)
                        {
                            for (int t = 0; t < imageBytes.Length; ++t) imageBytes[t] ^= 0xff;
                            inverted = true;
                        }
                        DirectBitmap = CreateBitmap();
                        if (DirectBitmap != null || !continueIfNoBitmap) return;
                        // Bitmap を作れない形式（16 ビット・2 ビットなど）は、同じ展開結果から PNG を作る（Y12）
                    }
                    PngWriter png = new PngWriter(ms);
                    if (decode != null && !inverted)
                    {
                        if (pngBitDepth == 1)
                        {
                            // if the decode array is 1,0, then we need to invert the image
                            if (decode.GetAsNumber(0).IntValue == 1 && decode.GetAsNumber(1).IntValue == 0)
                            {
                                int len = imageBytes.Length;
                                for (int t = 0; t < len; ++t)
                                {
                                    imageBytes[t] ^= 0xff;
                                }
                            }
                            else
                            {
                                // if the decode array is 0,1, do nothing.  It's possible that the array could be 0,0 or 1,1 - but that would be silly, so we'll just ignore that case
                            }
                        }
                        else
                        {
                            // todo: add decode transformation for other depths
                        }
                    }
                    png.WriteHeader(width, height, pngBitDepth, pngColorType);
                    if (icc != null)
                        png.WriteIccProfile(icc);
                    if (palette != null)
                        png.WritePalette(palette);
                    png.WriteData(imageBytes, stride);
                    png.WriteEnd();
                    streamContentType = ImageBytesType.PNG;
                    imageBytes = ms.ToArray();
                }
            }

            /**
             * @return the bytes of the image (the format will be as specified in {@link PdfImageObject#getImageBytesType()}
             * @throws IOException
             * @since 5.0.4
             */
            virtual public byte[] GetImageAsBytes()
            {
                return imageBytes;
            }

            // 展開した画素（PNG の行と同じ並び）から Bitmap を作る。対象外なら null（D4）
            private unsafe Bitmap CreateBitmap()
            {
                System.Drawing.Imaging.PixelFormat format;
                if (pngColorType == 0)
                {
                    if (bpc == 8) format = System.Drawing.Imaging.PixelFormat.Format8bppIndexed;
                    else if (bpc == 1) format = System.Drawing.Imaging.PixelFormat.Format1bppIndexed;
                    else return null;
                }
                else if (pngColorType == 2)
                {
                    if (bpc != 8) return null;
                    format = System.Drawing.Imaging.PixelFormat.Format24bppRgb;
                }
                else if (pngColorType == 3)
                {
                    if (palette == null) return null;
                    if (bpc == 8) format = System.Drawing.Imaging.PixelFormat.Format8bppIndexed;
                    else if (bpc == 4) format = System.Drawing.Imaging.PixelFormat.Format4bppIndexed;
                    else if (bpc == 1) format = System.Drawing.Imaging.PixelFormat.Format1bppIndexed;
                    else return null;
                }
                else return null;
                if (width <= 0 || height <= 0 || stride <= 0 || imageBytes == null || (long)stride * height > imageBytes.Length) return null;

                var bmp = new Bitmap(width, height, format);
                try
                {
                    if (pngColorType != 2)
                    {
                        var pal = bmp.Palette;
                        var entries = pal.Entries;
                        for (var i = 0; i < entries.Length; i++)
                        {
                            if (pngColorType == 0)
                            {
                                var v = bpc == 1 ? (i == 0 ? 0 : 255) : i;
                                entries[i] = Color.FromArgb(v, v, v);
                            }
                            else
                            {
                                entries[i] = 3 * i + 2 < palette.Length ? Color.FromArgb(palette[3 * i], palette[3 * i + 1], palette[3 * i + 2]) : Color.Black;
                            }
                        }
                        bmp.Palette = pal;
                    }
                    var data = bmp.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, format);
                    try
                    {
                        fixed (byte* src0 = imageBytes)
                        {
                            for (var y = 0; y < height; y++)
                            {
                                var src = src0 + (long)y * stride;
                                var dst = (byte*)data.Scan0 + (long)y * data.Stride;
                                if (pngColorType == 2)
                                {
                                    for (var x = 0; x < width; x++)
                                    {
                                        dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0]; // RGB → BGR
                                        src += 3; dst += 3;
                                    }
                                }
                                else
                                {
                                    Buffer.MemoryCopy(src, dst, data.Stride, stride);
                                }
                            }
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(data);
                    }
                    return bmp;
                }
                catch
                {
                    bmp.Dispose();
                    throw;
                }
            }
            
#if DRAWING
#endif// DRAWING
        }
    }
}
