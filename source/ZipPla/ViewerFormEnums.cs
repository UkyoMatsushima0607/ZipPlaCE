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
    class ExtendedToolStripMenuItem : ToolStripMenuItem
    {
        private ExtendedToolStripMenuItem(ToolStripMenuItem toolStripMenuItem)
        {
            var ownerItems = toolStripMenuItem.Owner.Items;

            var index = ownerItems.IndexOf(toolStripMenuItem);

            DropDownItems.AddRange((from ToolStripItem item in toolStripMenuItem.DropDownItems select item).ToArray());

            ownerItems.RemoveAt(index);
            ownerItems.Insert(index, this);
            
        }

        public static void Replace(ref ToolStripMenuItem toolStripMenuItem)
        {
            toolStripMenuItem = new ExtendedToolStripMenuItem(toolStripMenuItem);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool MoveWindow( IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        protected override void OnDropDownOpened(EventArgs e)
        {
            var bounds = Bounds;
            var point = Owner.PointToScreen(new Point(bounds.Left, bounds.Bottom - 1));
            var dropDownLocation = DropDownLocation;

            if (point != dropDownLocation)
            {
                var dropDown = DropDown;
                
                MoveWindow(DropDown.Handle, point.X, point.Y, dropDown.Width, dropDown.Height, true);
            }
            
            base.OnDropDownOpened(e);
        }
    }

    public enum SeekBarMode { LeftToRight, RightToLeft, SameAsPage, ReverseOfPage }

    public enum ZoomMode { LeftToRight, RightToLeft, SameAsPage, ReverseOfPage }

    public enum ViewSettingHintMode { ShowWhenChangingSettings, NeverShow }

    public enum ReadOnMemoryMode { None, ExceptLookAhead, Always }

    //public enum MagnifierScrollMode { Location, Variation }

    public enum ScalingAlgorithm { Default, HighSpeed, NearestNeighbor, AreaAverage, Lanczos1, Lanczos2, Lanczos3, Lanczos4,
        Spline4, Spline16, Spline36, Spline64
#if !AUTOBUILD
            //, AntiMoire1, AntiMoire2, AntiMoire3, AntiMoire4
#endif
    }

    public struct ScalingAlgorithmPair
    {
        public ScalingAlgorithm ScaleUp;
        public ScalingAlgorithm ScaleDown;
    }
}
