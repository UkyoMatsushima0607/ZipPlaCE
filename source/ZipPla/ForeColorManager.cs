using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Common;
using System.Reflection;
using System.Diagnostics;

namespace ZipPla
{
    public class ForeColorManager : IDisposable
    {
        public static ForeColorManager Set(ToolStripItem toolStripItem)
        {
            return new ForeColorManager(toolStripItem);
        }

        public static ForeColorManager[] Set(params ToolStripItem[] toolStripItems)
        {
            return (from toolStripItem in toolStripItems select new ForeColorManager(toolStripItem)).ToArray();
        }

        public static ForeColorManager[] Set(IEnumerable<ToolStripItem> toolStripItems)
        {
            return (from toolStripItem in toolStripItems select new ForeColorManager(toolStripItem)).ToArray();
        }


        private ToolStripItem toolStripItem;
        private bool dropDowned = false;
        private Color originalForeColor;
        private bool managerForeColorChanging = false;
        private ForeColorManager(ToolStripItem toolStripItem)
        {
            this.toolStripItem = toolStripItem;
            originalForeColor = toolStripItem.ForeColor;
            toolStripItem.ForeColorChanged += toolStripItem_ForeColorChanged;
            toolStripItem.Paint += toolStripItem_Paint;
            if (toolStripItem is ToolStripDropDownItem toolStripDropDownItem)
            {
                toolStripDropDownItem.DropDownOpened += toolStripDropDownItem_DropDownOpened;
                toolStripDropDownItem.DropDownClosed += toolStripDropDownItem_DropDownClosed;
            }
        }
        
        private void toolStripDropDownItem_DropDownOpened(object sender, EventArgs e)
        {
            dropDowned = true;
        }

        private void toolStripDropDownItem_DropDownClosed(object sender, EventArgs e)
        {
            dropDowned = false;
        }

        private void toolStripItem_ForeColorChanged(object sender, EventArgs e)
        {
            if (!managerForeColorChanging)
            {
                originalForeColor = toolStripItem.ForeColor;
            }
        }

        private void toolStripItem_Paint(object sender, PaintEventArgs e)
        {
            if (!managerForeColorChanging)
            {
                var foreColor = dropDowned || toolStripItem.Selected ? SystemColors.WindowText : ZipTag.GetForeColor(toolStripItem.BackColor);
                if (foreColor != toolStripItem.ForeColor)
                {
                    managerForeColorChanging = true;
                    toolStripItem.ForeColor = foreColor;
                    managerForeColorChanging = false;
                }
            }
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
                    toolStripItem.ForeColor = originalForeColor;
                    toolStripItem.ForeColorChanged -= toolStripItem_ForeColorChanged;
                    toolStripItem.Paint -= toolStripItem_Paint;
                    if (toolStripItem is ToolStripDropDownItem toolStripDropDownItem)
                    {
                        toolStripDropDownItem.DropDownOpened -= toolStripDropDownItem_DropDownOpened;
                        toolStripDropDownItem.DropDownClosed -= toolStripDropDownItem_DropDownClosed; ;
                    }
                }

                // TODO: アンマネージ リソース (アンマネージ オブジェクト) を解放し、下のファイナライザーをオーバーライドします。
                // TODO: 大きなフィールドを null に設定します。

                disposedValue = true;
            }
        }

        // TODO: 上の Dispose(bool disposing) にアンマネージ リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします。
        // ~ForeColorManager() {
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
