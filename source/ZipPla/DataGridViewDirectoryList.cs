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
    public class DataGridViewDirectoryList : DataGridView
    {
        public SimplifiedKeyBoard SimplifiedKeyBoard;
        protected override void OnMouseDown(MouseEventArgs e)
        {
            DataGridViewFileList.MyOnMouseDown(this, mouseDown, base.OnMouseDown, SetCurrentCellAddressCore, SimplifiedKeyBoard, e);
        }

        public DataGridViewDirectoryList() : base()
        {
            DoubleBuffered = true;
        }

        private event MouseEventHandler mouseDown;
        public new event MouseEventHandler MouseDown
        {
            add
            {
                mouseDown += value;
                base.MouseDown += value;
            }
            remove
            {
                mouseDown -= value;
                base.MouseDown -= value;
            }
        }
    }
}
