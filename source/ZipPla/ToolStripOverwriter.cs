using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZipPla
{
    public static class ToolStripOverwriter
    {
        public static void SquarizeToolStrip(ToolStrip toolStrip)
        {
#if !DEBUG
            if (Program.DisplayMagnification == 1) return;
#endif
            // var imageScalingSize = toolStrip.ImageScalingSize;
            var height = Program.DpiScaling(16); // imageScalingSize.Height;
            var square = new Size(height, height);


            toolStrip.ImageScalingSize = square;
            var mightNeedDummyImage = !(toolStrip is MenuStrip);
            foreach (var child in toolStrip.Items)
            {
                var childToolStrip = child as ToolStripMenuItem;
                if (childToolStrip != null)
                {
                    SquarizeToolStrip(childToolStrip, ref mightNeedDummyImage);
                }
            }

            toolStrip.ItemAdded -= ToolStrip_ItemAdded;
            toolStrip.ItemAdded += ToolStrip_ItemAdded;
        }

        private static void ToolStrip_ItemAdded(object sender, ToolStripItemEventArgs e)
        {
            var toolStrip = sender as ToolStrip;
            if (toolStrip == null) return;
            var childToolStrip = e.Item as ToolStripMenuItem;
            if (childToolStrip != null)
            {
                bool mightNeedDummyImage = !(toolStrip is MenuStrip) && (from ToolStripItem item in toolStrip.Items select item).All(item => item == e.Item || !(item is ToolStripMenuItem));
                SquarizeToolStrip(childToolStrip, ref mightNeedDummyImage);
            }
        }

        private static void SquarizeToolStrip(ToolStripMenuItem toolStripMenuItem, ref bool mightNeedDummyImage)
        {
            if (mightNeedDummyImage)
            {
                if (toolStripMenuItem.Image == null)
                {
                    var dummy = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
                    toolStripMenuItem.Image = dummy;
                    toolStripMenuItem.Disposed += (sender, e) => dummy.Dispose();
                }
                mightNeedDummyImage = false;
            }
            var dropDown = toolStripMenuItem.DropDown as ToolStrip;
            if (dropDown != null)
            {
                SquarizeToolStrip(dropDown);
            }
        }

        public static void SquarizeToolStripInClass<T>(T target)
        {
#if !DEBUG
            if (Program.DisplayMagnification == 1) return;
#endif
            foreach (var info in typeof(T).GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (info.FieldType.IsSubclassOf(typeof(ToolStrip)))
                {
                    var toolStrip = info.GetValue(target) as ToolStrip;
                    if (toolStrip != null) SquarizeToolStrip(toolStrip);
                }
            }
            foreach (var info in typeof(T).GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (info.PropertyType.IsSubclassOf(typeof(ToolStrip)))
                {
                    var toolStrip = info.GetValue(target) as ToolStrip;
                    if (toolStrip != null) SquarizeToolStrip(toolStrip);
                }
            }
        }
    }
    
    public class ContextMenuStrip : System.Windows.Forms.ContextMenuStrip
    {
        public ContextMenuStrip() : base()
        {
            if (Program.DisplayMagnification == 1) return;
            ToolStripOverwriter.SquarizeToolStrip(this);
        }
    }

    public enum DisplayCheckMark { Check, Select, CheckThreeState, SelectThreeState }

    public static class CheckMarkProvider
    {

        public static void SetCheckMark(ToolStrip ts, DisplayCheckMark checkMark)
        {
            foreach (var obj in ts.Items)
            {
                var tsmi = obj as ToolStripMenuItem;
                if (tsmi != null)
                {
                    SetCheckMark(ts, tsmi, checkMark);
                    // DropDown の getter は子のない項目にも空のドロップダウンを作る（全メニューで数百個）ので、子があるときだけ（S8）
                    if (tsmi.HasDropDownItems)
                    {
                        SetCheckMark(tsmi.DropDown, checkMark);
                    }
                }
            }
        }

        public static void SetCheckMark(ToolStrip ts, ToolStripMenuItem tsmi, DisplayCheckMark checkMark)
        {
            tsmi.CheckedChanged -= checkedChangedForSelect;
            tsmi.CheckedChanged -= checkedChangedForCheck;
            tsmi.CheckStateChanged -= checkedChangedForCheckThreeState;
            tsmi.CheckStateChanged -= checkedChangedForSelectThreeState;
            createCheckMarkBitmap(ts, checkMark);
            switch (checkMark)
            {
                case DisplayCheckMark.Select:
                    tsmi.CheckedChanged += checkedChangedForSelect;
                    if (tsmi.Checked) tsmi.Image = selectImage; // ダミーを消さないために、初めは解除しない
                    break;
                case DisplayCheckMark.Check:
                    tsmi.CheckedChanged += checkedChangedForCheck;
                    if (tsmi.Checked) tsmi.Image = checkImage; // ダミーを消さないために、初めは解除しない
                    break;
                case DisplayCheckMark.CheckThreeState:
                    tsmi.CheckStateChanged += checkedChangedForCheckThreeState;
                    switch(tsmi.CheckState)
                    {
                        case CheckState.Checked: tsmi.Image = checkImage; break;
                        case CheckState.Indeterminate: tsmi.Image = dotImage; break;
                    }
                    break;
                case DisplayCheckMark.SelectThreeState:
                    tsmi.CheckStateChanged += checkedChangedForSelectThreeState;
                    switch (tsmi.CheckState)
                    {
                        case CheckState.Checked: tsmi.Image = selectImage; break;
                        case CheckState.Indeterminate: tsmi.Image = dotImage; break;
                    }
                    break;

            }
        }

        private static void checkedChangedForCheckThreeState(object sender, EventArgs e)
        {
            var tsmi = sender as ToolStripMenuItem;
            switch (tsmi.CheckState)
            {
                case CheckState.Checked: tsmi.Image = checkImage; break;
                case CheckState.Indeterminate: tsmi.Image = dotImage; break;
                case CheckState.Unchecked: tsmi.Image = null; break;
            }
        }

        private static void checkedChangedForSelectThreeState(object sender, EventArgs e)
        {
            var tsmi = sender as ToolStripMenuItem;
            switch (tsmi.CheckState)
            {
                case CheckState.Checked: tsmi.Image = selectImage; break;
                case CheckState.Indeterminate: tsmi.Image = dotImage; break;
                case CheckState.Unchecked: tsmi.Image = null; break;
            }
        }

        private static void checkedChangedForSelect(object sender, EventArgs e)
        {
            var tsmi = sender as ToolStripMenuItem;
            if (tsmi.Checked)
            {
                tsmi.Image = selectImage;
            }
            else
            {
                tsmi.Image = null;
            }
        }

        private static void checkedChangedForCheck(object sender, EventArgs e)
        {
            var tsmi = sender as ToolStripMenuItem;
            if (tsmi.Checked)
            {
                tsmi.Image = checkImage;
            }
            else
            {
                tsmi.Image = null;
            }
        }

        private static Bitmap dotImage;
        private static Bitmap selectImage;
        private static Bitmap checkImage;
        private static void createCheckMarkBitmap(ToolStrip ts, DisplayCheckMark checkMark)
        {
            switch(checkMark)
            {
                case DisplayCheckMark.Check: if (checkImage != null) return; else break;
                case DisplayCheckMark.Select: if (selectImage != null) return; else break;
                case DisplayCheckMark.CheckThreeState:
                    createCheckMarkBitmap(ts, DisplayCheckMark.Check);
                    if (dotImage != null) return; else break;
                case DisplayCheckMark.SelectThreeState:
                    createCheckMarkBitmap(ts, DisplayCheckMark.Select);
                    if (dotImage != null) return; else break;
            }
            
            var size = ts.ImageScalingSize;
            var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    switch(checkMark)
                    {
                        case DisplayCheckMark.Select:
                            {
                                g.FillPolygon(Brushes.Black, new PointF[]
                                {
                                    new PointF(0.7f * size.Width - 0.5f, 0.5f * size.Height - 0.5f),
                                    new PointF(0.3f * size.Width - 0.5f, 0.2f * size.Height - 0.5f),
                                    new PointF(0.3f * size.Width - 0.5f, 0.8f * size.Height - 0.5f),
                                });
                                selectImage = bitmap;
                                break;
                            }
                        case DisplayCheckMark.CheckThreeState:
                        case DisplayCheckMark.SelectThreeState:
                            {
                                var r = 0.15;
                                var cx = size.Width / 2.0;
                                var cy = size.Height / 2.0;
                                var rx = r * size.Width;
                                var ry = r * size.Height;
                                var rect = mkRect(cx - rx, cx + rx, cx - ry, cy + ry);

                                g.FillEllipse(Brushes.Black, rect);
                                dotImage = bitmap;
                                break;
                            }
                        case DisplayCheckMark.Check:
                            {
                                var t = 0.08;

                                using (var p = new Pen(Color.Black, (float)(t * (size.Width + size.Height))))
                                {
                                    g.DrawLines(p, new PointF[]
                                    {
                                        new PointF(0.1f * size.Width - 0.5f, 0.6f * size.Height - 0.5f),
                                        new PointF(0.4f * size.Width - 0.5f, 0.8f * size.Height - 0.5f),
                                        new PointF(0.9f * size.Width - 0.5f, 0.1f * size.Height - 0.5f),
                                    });
                                }
                                checkImage = bitmap;
                                break;
                            }
                    }
                }
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static RectangleF mkRect(double x0, double x1, double y0, double y1)
        {
            return new RectangleF((float)(x0 - 0.5), (float)(y0 - 0.5), (float)(x1 - x0), (float)(y1 - y0));
        }

    }


}
