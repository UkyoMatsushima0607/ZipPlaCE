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
    /// <summary>
    /// 既存のコードを利用するためのクラス
    /// 新たにこのクラスを使用することを前提にコードを書くことはしない
    /// </summary>
    public class KeyMouseBinder
    {
        private ToolStripDropDown toolStripDropDown;
        private Func<ToolStripItem, bool> requiredBinding;

        public KeyMouseBinder(ToolStripDropDown toolStripDropDown, Func<ToolStripItem, bool> requiredBinding)
        {
            this.toolStripDropDown = toolStripDropDown ?? throw new ArgumentNullException(nameof(toolStripDropDown));
            this.requiredBinding = requiredBinding ?? throw new ArgumentNullException(nameof(requiredBinding));
            toolStripDropDown.PreviewKeyDown += ToolStripDropDown_PreviewKeyDown;
        }

        private void ToolStripDropDown_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                var selected = (from ToolStripItem item in toolStripDropDown.Items where item.Selected select item).FirstOrDefault();
                if (selected != null && requiredBinding(selected))
                {
                    var bounds = selected.Bounds;
                    var halfHeight = bounds.Height / 2;
                    var x = bounds.X + halfHeight;
                    var y = bounds.Y + halfHeight;
                    CatalogForm.PerformMouseDown(toolStripDropDown, new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
                }
            }
        }
    }
}
