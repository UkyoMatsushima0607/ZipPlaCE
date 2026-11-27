using Alteridem.WinTouch;
using Common;
using GenerarClasses;
using SharpCompress.Common;
//using SharpCompress.Reader;
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
    class FileInfoEx : FileSystemInfo
    {
        private FileInfo fileInfo;
        private string fullPath = null;
        private string entityPath;
        private string name = null;

        public FileInfoEx(string path)
        {
            var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
            if (altPos >= 0)
            {
                name = path.Substring(altPos + 1);
                fullPath = path;
                entityPath = path.Substring(0, altPos);
            }
            else
            {
                entityPath = path;
            }
            fileInfo = new FileInfo(entityPath);
        }


        public override bool Exists
        {
            get
            {
                return fileInfo.Exists;
            }
        }

        public override string Name
        {
            get
            {
                return name == null ? fileInfo.Name : name;
            }
        }

        public override void Delete()
        {
            fileInfo.Delete();
        }

        public long Length
        {
            get
            {
                return fileInfo.Length;
            }
        }


        public override string FullName
        {
            get
            {
                return fullPath == null ? fileInfo.FullName : fullPath;
            }
        }

        public static FileInfo GetEntityFileInfo(FileSystemInfo fileInfo)
        {
            var fileInfo2 = fileInfo as FileInfo;
            if (fileInfo2 != null) return fileInfo2;
            else return (fileInfo as FileInfoEx).fileInfo;
        }

    }
}
