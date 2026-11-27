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
    public class DataObjectEx : DataObject, System.Runtime.InteropServices.ComTypes.IDataObject
    {
        private static readonly TYMED[] ALLOWED_TYMEDS =
            new TYMED[] {
                TYMED.TYMED_HGLOBAL,
                TYMED.TYMED_ISTREAM,
                TYMED.TYMED_ENHMF,
                TYMED.TYMED_MFPICT,
                TYMED.TYMED_GDI};

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct FILEDESCRIPTOR
        {
            public UInt32 dwFlags;
            public Guid clsid;
            public System.Drawing.Size sizel;
            public System.Drawing.Point pointl;
            public UInt32 dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public UInt32 nFileSizeHigh;
            public UInt32 nFileSizeLow;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public String cFileName;

            public static readonly int FileDescriptorSize = Marshal.SizeOf(typeof(FILEDESCRIPTOR));

            private static bool FromStream(Stream stream, out FILEDESCRIPTOR fileDescriptor)
            {
                var data = new Byte[FileDescriptorSize];
                var read = stream.Read(data, 0, FileDescriptorSize);
                if (read == 0)
                {
                    fileDescriptor = default(FILEDESCRIPTOR);
                    return false;
                }
                if (read != FileDescriptorSize)
                {
                    throw new ArgumentException(null, "data");
                }
                IntPtr FileDescriptorPointer = Marshal.AllocHGlobal(FileDescriptorSize);
                Marshal.Copy(data, 0, FileDescriptorPointer, FileDescriptorSize);
                fileDescriptor = Marshal.PtrToStructure<FILEDESCRIPTOR>(FileDescriptorPointer);
                Marshal.FreeHGlobal(FileDescriptorPointer);
                return true;
            }

            public static FILEDESCRIPTOR[] FromStreamToArray(Stream stream)
            {
                var count = stream.ReadByte() | stream.ReadByte() << 8 | stream.ReadByte() << 16 | stream.ReadByte() << 24;
                var result = new FILEDESCRIPTOR[count];
                for (var i = 0; i < count; i++)
                {
                    FILEDESCRIPTOR fileDescriptor;
                    if (FromStream(stream, out fileDescriptor))
                    {
                        result[i] = fileDescriptor;
                    }
                    else
                    {
                        throw new ArgumentException(null, "stream");
                    }
                }
                return result;
            }
        }

        public struct SelectedItem
        {
            public String FileName;
            public DateTime? WriteTime;
            public Int64 FileSize;
        }

        private SelectedItem[] m_SelectedItems;
        private Int32 m_lindex;

        private Int32 indexForZipPla = 0;

        public DataObjectEx(SelectedItem[] selectedItems)
        {
            m_SelectedItems = selectedItems;
        }

        public override object GetData(string format, bool autoConvert)
        {
            if (String.Compare(format, NativeMethods.CFSTR_FILEDESCRIPTORW, StringComparison.OrdinalIgnoreCase) == 0 && m_SelectedItems != null)
            {
                base.SetData(NativeMethods.CFSTR_FILEDESCRIPTORW, GetFileDescriptor(m_SelectedItems));
            }
            else if (String.Compare(format, NativeMethods.CFSTR_FILECONTENTS, StringComparison.OrdinalIgnoreCase) == 0)
            {
                base.SetData(NativeMethods.CFSTR_FILECONTENTS, GetFileContents_Private(m_SelectedItems, m_lindex));
            }
            else if (String.Compare(format, NativeMethods.CFSTR_PERFORMEDDROPEFFECT, StringComparison.OrdinalIgnoreCase) == 0)
            {
                //TODO: Cleanup routines after paste has been performed
            }
            else if (String.Compare(format, NativeMethods.ZIPPLA_FILECONTENTS, StringComparison.OrdinalIgnoreCase) == 0)
            {
                base.SetData(NativeMethods.CFSTR_FILECONTENTS, GetFileContents_Private(m_SelectedItems, indexForZipPla++));
                format = NativeMethods.CFSTR_FILECONTENTS;
            }
            else if (String.Compare(format, NativeMethods.ZIPPLA_PERFORMEDDROPEFFECT, StringComparison.OrdinalIgnoreCase) == 0)
            {
                indexForZipPla = 0;
                format = NativeMethods.CFSTR_PERFORMEDDROPEFFECT;
            }
            return base.GetData(format, autoConvert);
        }

        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        void System.Runtime.InteropServices.ComTypes.IDataObject.GetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC formatetc, out System.Runtime.InteropServices.ComTypes.STGMEDIUM medium)
        {
            if (formatetc.cfFormat == (Int16)DataFormats.GetFormat(NativeMethods.CFSTR_FILECONTENTS).Id)
                m_lindex = formatetc.lindex;

            medium = new System.Runtime.InteropServices.ComTypes.STGMEDIUM();
            if (GetTymedUseable(formatetc.tymed))
            {
                if ((formatetc.tymed & TYMED.TYMED_HGLOBAL) != TYMED.TYMED_NULL)
                {
                    medium.tymed = TYMED.TYMED_HGLOBAL;
                    medium.unionmember = NativeMethods.GlobalAlloc(NativeMethods.GHND | NativeMethods.GMEM_DDESHARE, 1);
                    if (medium.unionmember == IntPtr.Zero)
                    {
                        throw new OutOfMemoryException();
                    }
                    try
                    {
                        ((System.Runtime.InteropServices.ComTypes.IDataObject)this).GetDataHere(ref formatetc, ref medium);
                        return;
                    }
                    catch
                    {
                        NativeMethods.GlobalFree(new HandleRef((STGMEDIUM)medium, medium.unionmember));
                        medium.unionmember = IntPtr.Zero;
                        throw;
                    }
                }
                medium.tymed = formatetc.tymed;
                ((System.Runtime.InteropServices.ComTypes.IDataObject)this).GetDataHere(ref formatetc, ref medium);
            }
            else
            {
                Marshal.ThrowExceptionForHR(NativeMethods.DV_E_TYMED);
            }
        }

        private static Boolean GetTymedUseable(TYMED tymed)
        {
            for (Int32 i = 0; i < ALLOWED_TYMEDS.Length; i++)
            {
                if ((tymed & ALLOWED_TYMEDS[i]) != TYMED.TYMED_NULL)
                {
                    return true;
                }
            }
            return false;
        }

        public Action<SelectedItem[]> RecalcItems;

        private MemoryStream GetFileDescriptor(SelectedItem[] SelectedItems)
        {
            if (SelectedItems != null && RecalcItems != null)
            {
                RecalcItems(SelectedItems);
            }

            MemoryStream FileDescriptorMemoryStream = new MemoryStream();
            // Write out the FILEGROUPDESCRIPTOR.cItems value
            FileDescriptorMemoryStream.Write(BitConverter.GetBytes(SelectedItems.Length), 0, sizeof(UInt32));

            FILEDESCRIPTOR FileDescriptor = new FILEDESCRIPTOR();
            foreach (SelectedItem si in SelectedItems)
            {
                FileDescriptor.cFileName = si.FileName;
                Int64? FileTime;
                FileTime = si.WriteTime?.ToFileTime();
                if (FileTime != null)
                {
                    FileDescriptor.ftLastWriteTime.dwHighDateTime = (Int32)(FileTime >> 32);
                    FileDescriptor.ftLastWriteTime.dwLowDateTime = (Int32)(FileTime & 0xFFFFFFFF);
                }
                FileDescriptor.nFileSizeHigh = (UInt32)(si.FileSize >> 32);
                FileDescriptor.nFileSizeLow = (UInt32)(si.FileSize & 0xFFFFFFFF);
                FileDescriptor.dwFlags = NativeMethods.FD_WRITESTIME | NativeMethods.FD_FILESIZE | NativeMethods.FD_PROGRESSUI;

                // Marshal the FileDescriptor structure into a byte array and write it to the MemoryStream.
                Int32 FileDescriptorSize = Marshal.SizeOf(FileDescriptor);
                IntPtr FileDescriptorPointer = Marshal.AllocHGlobal(FileDescriptorSize);
                Marshal.StructureToPtr(FileDescriptor, FileDescriptorPointer, true);
                Byte[] FileDescriptorByteArray = new Byte[FileDescriptorSize];
                Marshal.Copy(FileDescriptorPointer, FileDescriptorByteArray, 0, FileDescriptorSize);
                Marshal.FreeHGlobal(FileDescriptorPointer);
                FileDescriptorMemoryStream.Write(FileDescriptorByteArray, 0, FileDescriptorByteArray.Length);
            }
            return FileDescriptorMemoryStream;
        }

        private MemoryStream GetFileContents_Private(SelectedItem[] SelectedItems, Int32 FileNumber)
        {
            MemoryStream FileContentMemoryStream = null;
            if (SelectedItems != null && FileNumber < SelectedItems.Length)
            {

                FileContentMemoryStream = GetFileContents(SelectedItems[FileNumber], FileNumber);
                if (FileContentMemoryStream == null) return null;
                if (FileContentMemoryStream.Length == 0) FileContentMemoryStream.WriteByte(0); // Must send at least one byte for a zero length file to prevent stoppages.
            }
            return FileContentMemoryStream;
        }

        public Func<SelectedItem, int, MemoryStream> GetFileContents;
    }
}
