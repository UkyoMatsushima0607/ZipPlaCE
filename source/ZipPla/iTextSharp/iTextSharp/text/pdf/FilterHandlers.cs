using System;
using System.Collections.Generic;
using System.IO;
using iTextSharp.text.pdf.codec;
using iTextSharp.text.exceptions;
using iTextSharp.text.error_messages;
using System.util.collections;
/*
 * This file is part of the iText (R) project.
 * Copyright (c) 1998-2016 iText Group NV
 * Authors: Kevin Day, Bruno Lowagie, Paulo Soares, et al.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License version 3
 * as published by the Free Software Foundation with the addition of the
 * following permission added to Section 15 as permitted in Section 7(a):
 * FOR ANY PART OF THE COVERED WORK IN WHICH THE COPYRIGHT IS OWNED BY
 * ITEXT GROUP. ITEXT GROUP DISCLAIMS THE WARRANTY OF NON INFRINGEMENT
 * OF THIRD PARTY RIGHTS
 *
 * This program is distributed in the hope that it will be useful, but
 * WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
 * or FITNESS FOR A PARTICULAR PURPOSE.
 * See the GNU Affero General Public License for more details.
 * You should have received a copy of the GNU Affero General Public License
 * along with this program; if not, see http://www.gnu.org/licenses or write to
 * the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor,
 * Boston, MA, 02110-1301 USA, or download the license from the following URL:
 * http://itextpdf.com/terms-of-use/
 *
 * The interactive user interfaces in modified source and object code versions
 * of this program must display Appropriate Legal Notices, as required under
 * Section 5 of the GNU Affero General Public License.
 *
 * In accordance with Section 7(b) of the GNU Affero General Public License,
 * a covered work must retain the producer line in every PDF that is created
 * or manipulated using iText.
 *
 * You can be released from the requirements of the license by purchasing
 * a commercial license. Buying such a license is mandatory as soon as you
 * develop commercial activities involving the iText software without
 * disclosing the source code of your own applications.
 * These activities include: offering paid services to customers as an ASP,
 * serving PDFs on the fly in a web application, shipping iText with a closed
 * source product.
 *
 * For more information, please contact iText Software Corp. at this
 * address: sales@itextpdf.com
 
 */

namespace iTextSharp.text.pdf {


    /**
     * Encapsulates filter behavior for PDF streams.  Classes generally interace with this
     * using the static GetDefaultFilterHandlers() method, then obtain the desired {@link IFilterHandler}
     * via a lookup.
     * @since 5.0.4
     */
    // Dev note:  we eventually want to refactor PdfReader so all of the existing filter functionality is moved into this class
    // it may also be better to split the sub-classes out into a separate package 
    public sealed class FilterHandlers {
        
        /**
         * The main interface for creating a new {@link IFilterHandler}
         */
        public interface IFilterHandler{
            byte[] Decode(byte[] b, PdfName filterName, PdfObject decodeParams, PdfDictionary streamDictionary);
        }
        
        /** The default {@link IFilterHandler}s used by iText */
        private static IDictionary<PdfName, IFilterHandler> defaults;
        static FilterHandlers() {
            Dictionary<PdfName, IFilterHandler> map = new Dictionary<PdfName, IFilterHandler>();
            
            map[PdfName.FLATEDECODE] = new Filter_FLATEDECODE();
            map[PdfName.FL] = new Filter_FLATEDECODE();
            map[PdfName.ASCIIHEXDECODE] = new Filter_ASCIIHEXDECODE();
            map[PdfName.AHX] = new Filter_ASCIIHEXDECODE();
            map[PdfName.ASCII85DECODE] = new Filter_ASCII85DECODE();
            map[PdfName.A85] = new Filter_ASCII85DECODE();
            map[PdfName.LZWDECODE] = new Filter_LZWDECODE();
            map[PdfName.CCITTFAXDECODE] = new Filter_CCITTFAXDECODE();
            map[PdfName.CRYPT] = new Filter_DoNothing();
            map[PdfName.RUNLENGTHDECODE] = new Filter_RUNLENGTHDECODE();
            
            defaults = new ReadOnlyDictionary<PdfName, IFilterHandler>(map);
        }
        
        /**
         * @return the default {@link IFilterHandler}s used by iText
         */
        public static IDictionary<PdfName, IFilterHandler> GetDefaultFilterHandlers(){
            return defaults;
        }
        
        /**
         * Handles FLATEDECODE filter
         */
        private class Filter_FLATEDECODE : IFilterHandler{
            public byte[] Decode(byte[] b, PdfName filterName, PdfObject decodeParams, PdfDictionary streamDictionary) {
                b = PdfReader.FlateDecode(b, ExpectedInflatedLength(b, decodeParams, streamDictionary));
                b = PdfReader.DecodePredictor(b, decodeParams);
                return b;
            }

            // 画像の Flate を展開した後の長さの見込み（ZipPla CE、Y13）。分からなければ -1。
            // 見込みが合えば、その大きさの配列に直接展開して写し直さない（合わなくても結果は同じ）
            private static int ExpectedInflatedLength(byte[] b, PdfObject decodeParams, PdfDictionary dic) {
                try {
                    if (b == null || dic == null || !PdfName.IMAGE.Equals(dic.GetAsName(PdfName.SUBTYPE))) return -1;
                    // Flate の後にほかのフィルタ（DCT など）がある場合は、画素の長さにならない
                    var filter = PdfReader.GetPdfObjectRelease(dic.Get(PdfName.FILTER));
                    if (filter is PdfArray filters) {
                        if (filters.Size == 0 || !PdfName.FLATEDECODE.Equals(PdfReader.GetPdfObjectRelease(filters[filters.Size - 1]))) return -1;
                    }
                    else if (!PdfName.FLATEDECODE.Equals(filter)) return -1;
                    var heightNumber = dic.GetAsNumber(PdfName.HEIGHT);
                    if (heightNumber == null) return -1;
                    long height = heightNumber.IntValue;
                    long length;
                    var parms = PdfReader.GetPdfObjectRelease(decodeParams) as PdfDictionary;
                    var predictor = parms?.GetAsNumber(PdfName.PREDICTOR);
                    if (predictor != null && predictor.IntValue >= 10) {
                        // PNG の予測は 1 行ごとにフィルタの 1 バイトが付く
                        long columns = parms.GetAsNumber(PdfName.COLUMNS)?.IntValue ?? 1;
                        long colors = parms.GetAsNumber(PdfName.COLORS)?.IntValue ?? 1;
                        long bpc = parms.GetAsNumber(PdfName.BITSPERCOMPONENT)?.IntValue ?? 8;
                        length = height * ((colors * columns * bpc + 7) / 8 + 1);
                    }
                    else if (predictor != null && predictor.IntValue > 1) {
                        return -1; // TIFF の予測（2）は長さが変わらないが、まれなので見積もらない
                    }
                    else {
                        var widthNumber = dic.GetAsNumber(PdfName.WIDTH);
                        if (widthNumber == null) return -1;
                        long width = widthNumber.IntValue;
                        long bpc, components;
                        var mask = dic.GetAsBoolean(PdfName.IMAGEMASK);
                        if (mask != null && mask.BooleanValue) {
                            bpc = 1; components = 1;
                        }
                        else {
                            var bpcNumber = dic.GetAsNumber(PdfName.BITSPERCOMPONENT);
                            if (bpcNumber == null) return -1;
                            bpc = bpcNumber.IntValue;
                            components = ColorComponents(PdfReader.GetPdfObjectRelease(dic.Get(PdfName.COLORSPACE)));
                            if (components <= 0) return -1;
                        }
                        length = height * ((width * components * bpc + 7) / 8);
                    }
                    // deflate の圧縮率は最大でも約 1032 倍。壊れた辞書で大きすぎる配列を取らない
                    if (length <= 0 || length > int.MaxValue - 64 || length > (long)b.Length * 1100) return -1;
                    return (int)length;
                }
                catch {
                    return -1;
                }
            }

            private static int ColorComponents(PdfObject colorSpace) {
                if (colorSpace is PdfName) {
                    if (PdfName.DEVICEGRAY.Equals(colorSpace) || PdfName.CALGRAY.Equals(colorSpace)) return 1;
                    if (PdfName.DEVICERGB.Equals(colorSpace) || PdfName.CALRGB.Equals(colorSpace)) return 3;
                    if (PdfName.DEVICECMYK.Equals(colorSpace)) return 4;
                    return -1; // リソースの名前などは分からない
                }
                if (colorSpace is PdfArray array && array.Size > 0) {
                    var type = PdfReader.GetPdfObjectRelease(array[0]);
                    if (PdfName.INDEXED.Equals(type)) return 1;
                    if (PdfName.CALGRAY.Equals(type)) return 1;
                    if (PdfName.CALRGB.Equals(type)) return 3;
                    if (PdfName.ICCBASED.Equals(type) && array.Size > 1 && PdfReader.GetPdfObjectRelease(array[1]) is PdfDictionary icc) {
                        return icc.GetAsNumber(PdfName.N)?.IntValue ?? -1;
                    }
                }
                return -1;
            }
        }
        
        /**
         * Handles ASCIIHEXDECODE filter
         */
        private class Filter_ASCIIHEXDECODE : IFilterHandler{
            public byte[] Decode(byte[] b, PdfName filterName, PdfObject decodeParams, PdfDictionary streamDictionary) {
                b = PdfReader.ASCIIHexDecode(b);
                return b;
            }
        }

        /**
         * Handles ASCIIHEXDECODE filter
         */
        private class Filter_ASCII85DECODE : IFilterHandler{
            public byte[] Decode(byte[] b, PdfName filterName, PdfObject decodeParams, PdfDictionary streamDictionary) {
                b = PdfReader.ASCII85Decode(b);
                return b;
            }
        }
        
        /**
         * Handles LZWDECODE filter
         */
        private class Filter_LZWDECODE : IFilterHandler{
            public byte[] Decode(byte[] b, PdfName filterName, PdfObject decodeParams, PdfDictionary streamDictionary) {
                b = PdfReader.LZWDecode(b);
                b = PdfReader.DecodePredictor(b, decodeParams);
                return b;
            }
        }

        
        /**
         * Handles CCITTFAXDECODE filter
         */
        private class Filter_CCITTFAXDECODE : IFilterHandler{
            public byte[] Decode(byte[] b, PdfName filterName, PdfObject decodeParams, PdfDictionary streamDictionary) {
                PdfNumber wn = (PdfNumber)PdfReader.GetPdfObjectRelease(streamDictionary.Get(PdfName.WIDTH));
                PdfNumber hn = (PdfNumber)PdfReader.GetPdfObjectRelease(streamDictionary.Get(PdfName.HEIGHT));
                if (wn == null || hn == null)
                    throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("filter.ccittfaxdecode.is.only.supported.for.images"));
                int width = wn.IntValue;
                int height = hn.IntValue;
                
                PdfDictionary param = decodeParams is PdfDictionary ? (PdfDictionary)decodeParams : null;
                int k = 0;
                bool blackIs1 = false;
                bool byteAlign = false;
                if (param != null) {
                    PdfNumber kn = param.GetAsNumber(PdfName.K);
                    if (kn != null)
                        k = kn.IntValue;
                    PdfBoolean bo = param.GetAsBoolean(PdfName.BLACKIS1);
                    if (bo != null)
                        blackIs1 = bo.BooleanValue;
                    bo = param.GetAsBoolean(PdfName.ENCODEDBYTEALIGN);
                    if (bo != null)
                        byteAlign = bo.BooleanValue;
                }
                byte[] outBuf = new byte[(width + 7) / 8 * height];
                TIFFFaxDecompressor decoder = new TIFFFaxDecompressor();
                if (k == 0 || k > 0) {
                    int tiffT4Options = k > 0 ? TIFFConstants.GROUP3OPT_2DENCODING : 0;
                    tiffT4Options |= byteAlign ? TIFFConstants.GROUP3OPT_FILLBITS : 0;
                    decoder.SetOptions(1, TIFFConstants.COMPRESSION_CCITTFAX3, tiffT4Options, 0);
                    decoder.DecodeRaw(outBuf, b, width, height);
                    if (decoder.fails > 0) {
                        byte[] outBuf2 = new byte[(width + 7) / 8 * height];
                        int oldFails = decoder.fails;
                        decoder.SetOptions(1, TIFFConstants.COMPRESSION_CCITTRLE, tiffT4Options, 0);
                        decoder.DecodeRaw(outBuf2, b, width, height);
                        if (decoder.fails < oldFails) {
                            outBuf = outBuf2;
                        }
                    }
                }
                else {
                    TIFFFaxDecoder deca = new TIFFFaxDecoder(1, width, height);
                    deca.DecodeT6(outBuf, b, 0, height, 0);
                }
                if (!blackIs1) {
                    int len = outBuf.Length;
                    for (int t = 0; t < len; ++t) {
                        outBuf[t] ^= 0xff;
                    }
                }
                b = outBuf;       
                return b;
            }
        }
        
        /**
         * A filter that doesn't modify the stream at all
         */
        private class Filter_DoNothing : IFilterHandler{
            public byte[] Decode(byte[] b, PdfName filterName, PdfObject decodeParams, PdfDictionary streamDictionary) {
                return b;
            }
        }

        /**
         * Handles RUNLENGTHDECODE filter
         */
        private class Filter_RUNLENGTHDECODE : IFilterHandler{

            public byte[] Decode(byte[] b, PdfName filterName, PdfObject decodeParams, PdfDictionary streamDictionary) {
             // allocate the output buffer
                MemoryStream baos = new MemoryStream();
                sbyte dupCount = -1;
                for (int i = 0; i < b.Length; i++){
                    dupCount = (sbyte)b[i];
                    if (dupCount == -128) break; // this is implicit end of data
                    
                    if (dupCount >= 0 && dupCount <= 127){
                        int bytesToCopy = dupCount+1;
                        baos.Write(b, i, bytesToCopy);
                        i+=bytesToCopy;
                    } else {
                        // make dupcount copies of the next byte
                        i++;
                        for (int j = 0; j < 1-(int)(dupCount);j++){ 
                            baos.WriteByte(b[i]);
                        }
                    }
                }
                return baos.ToArray();
            }
        }
    }
}
