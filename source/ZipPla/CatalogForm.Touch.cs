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
    public partial class CatalogForm
    {
        private void gestureListener_TwoFingerTap(object sender, TwoFingerTapEventArgs e)
        {
            CloseVisibleContextMenuStrips();
            e.Handled = true;
            if (!e.Begin) return;
            if (TryPerformMiddleMouseClickOnTvCatalog(e.Location)) return;
            if (TryPerformMiddleMouseClickOnDgvFileList(e.Location)) return;

            simplifiedKeyBoard.UpHeldKey();
        }

        private void gestureListener_PressAndTap(object sender, PressAndTapEventArgs e)
        {
            CloseVisibleContextMenuStrips();
            e.Handled = true;
            if (!e.Begin) return;
            if (TryPerformRightMouseClickOnTvCatalog(e.Location)) return;
            if (TryPerformRightMouseClickOnDgvFileList(e.Location)) return;
            if (TryPerformMouseClick(dgvDirectoryList, MouseButtons.Right, e.Location)) return;
            if (TryPerformRightMouseClickOnTreeView(e.Location)) return;

            simplifiedKeyBoard.UpHeldKey();
        }

        private bool TryPerformRightMouseClickOnTreeView(Point screenPoint)
        {
            var treeWnd = explorerTreeView.TreeViewWnd;
            if (!treeWnd.Visible) return false;
            var clientLocation = treeWnd.PointToClient(screenPoint);
            if (!treeWnd.ClientRectangle.Contains(clientLocation)) return false;
            var hit = treeWnd.HitTest(clientLocation);

            if (hit.Node != null)
            {
                simplifiedKeyBoard.UpHeldKey();
                explorerTreeView_NodeMouseClick(null, new TreeNodeMouseClickEventArgs(hit.Node, MouseButtons.Right, 1, clientLocation.X, clientLocation.Y));
            }

            return true; // ヒットしなくとも終わる

        }

        private void middleClickContextMenuPrepareAndShow(Point screenPoint, Keys dummy)
        {
            middleClickContextMenuPrepareAndShow(screenPoint);
        }

        private bool TryPerformMiddleMouseClickOnTvCatalog(Point screenPoint)
        {
            return TryPerformMouseClickOnTvCatalog(middleClickContextMenuPrepareAndShow, screenPoint);
        }

        private bool TryPerformRightMouseClickOnTvCatalog(Point screenPoint)
        {
            return TryPerformMouseClickOnTvCatalog(rightClickContextMenuPrepareAndShow, screenPoint);
        }

        private bool TryPerformMouseClickOnTvCatalog(Action<Point, Keys> prepareAndShow, Point screenPoint)
        {
            if (!tvCatalog.Visible) return false;
            var clientLocation = tvCatalog.PointToClient(screenPoint);
            if (!tvCatalog.ClientRectangle.Contains(clientLocation)) return false;

            var index = tvCatalog.PointToIndex(clientLocation);
            if (index >= 0)
            {
                if (!tvCatalog.IsSelected(index)) tvCatalog.SelectedIndex = index;
                prepareAndShow(screenPoint, ModifierKeys);
                return true;
            }
            else
            {
                tvCatalog.SelectedIndex = -1;
                prepareAndShow(screenPoint, ModifierKeys);
                return true;
            }
        }

        private bool TryPerformMiddleMouseClickOnDgvFileList(Point screenPoint)
        {
            return TryPerformMouseClickOnDgvFileList(middleClickContextMenuPrepareAndShow, screenPoint);
        }

        private bool TryPerformRightMouseClickOnDgvFileList(Point screenPoint)
        {
            return TryPerformMouseClickOnDgvFileList(rightClickContextMenuPrepareAndShow, screenPoint);
        }

        private bool TryPerformMouseClickOnDgvFileList(Action<Point, Keys> prepareAndShow, Point screenPoint)
        {
            if (!dgvFileList.Visible) return false;
            var clientLocation = dgvFileList.PointToClient(screenPoint);
            if (!dgvFileList.ClientRectangle.Contains(clientLocation)) return false;

            var hit = dgvFileList.HitTest(clientLocation.X, clientLocation.Y);
            if (hit.RowIndex >= 0)
            {
                var clickedRow = dgvFileList.Rows[hit.RowIndex];
                if (!clickedRow.Selected)
                {
                    var temp = selectionSynchronizing;
                    selectionSynchronizing = true;
                    dgvFileList.ClearSelection(); // SelectedRows を使うと選択中の行が共有されなくなる
                    selectionSynchronizing = temp;
                    clickedRow.Selected = true;
                }
                prepareAndShow(screenPoint, ModifierKeys);
                return true;
            }
            else
            {
                tvCatalog.SelectedIndex = -1;
                prepareAndShow(screenPoint, ModifierKeys);
                return true;
            }
        }

        private bool TryPerformMouseClick(Control control, MouseButtons button, Point screenPoint)
        {
            if (!control.Visible) return false;
            var clientLocation = control.PointToClient(screenPoint);
            if (control.ClientRectangle.Contains(clientLocation))
            {
                control.Focus();
                Application.DoEvents();
                PerformMouseDown(control, new MouseEventArgs(button, 1, clientLocation.X, clientLocation.Y, 0));
                //PerformMouseMove(control, new MouseEventArgs(button, 1, clientLocation.X, clientLocation.Y, 0));
                Application.DoEvents();
                PerformMouseUp(control, new MouseEventArgs(button, 1, clientLocation.X, clientLocation.Y, 0));
                return true;
            }
            else
            {
                return false;
            }
        }

        private void gestureListener_Rotate(object sender, RotateEventArgs e)
        {
            CloseVisibleContextMenuStrips();
            if (showThumbnailMenuForTouch(e))
            {
                var items = thumbnailSizeToolStripMenuItem.DropDownItems;
                var index = items.IndexOf(thumbnailSizeBottomSeparator) + 1;
                ToolStripMenuItem item;
                while ((item = (ToolStripMenuItem)items[index++]) != allRatiosToolStripMenuItem)
                {
                    if (item.Checked)
                    {
                        item.Select();
                        return;
                    }
                }
                allRatiosToolStripMenuItem.Select();
            }

            e.Handled = true;
        }

        private void gestureListener_Zoom(object sender, ZoomEventArgs e)
        {
            CloseVisibleContextMenuStrips();
            if (showThumbnailMenuForTouch(e))
            {
                var items = thumbnailSizeToolStripMenuItem.DropDownItems;
                var index = items.IndexOf(thumbnailSizeTopSeparator) + 1;
                ToolStripItem item;
                while ((item = items[index++]) != thumbnailSizeBottomSeparator)
                {
                    var item1 = (ToolStripMenuItem)item;
                    if (item1.Checked)
                    {
                        item1.Select();
                        return;
                    }
                }
            }

            e.Handled = true;
        }

        private bool showThumbnailMenuForTouch(GestureEventArgs e)
        {
            var ok = false;
            if (!thumbnailSizeToolStripMenuItem.DropDown.Visible)
            {
                var loc = e.Location;
                ok = tvCatalog.Visible && tvCatalog.ClientRectangle.Contains(tvCatalog.PointToClient(loc));
                if (!ok && dgvFileList.Visible)
                {
                    var cloc = dgvFileList.PointToClient(loc);
                    if (dgvFileList.ClientRectangle.Contains(cloc))
                    {
                        ok = dgvFileList.HitTest(cloc.X, cloc.Y).Type != DataGridViewHitTestType.ColumnHeader;
                    }
                }
                if (ok)
                {
                    thumbnailSizeToolStripMenuItem.ShowDropDown();
                }
            }
            return ok;
        }

        Control TouchListener_Pan_HoldedControl = null;

        private static MethodInfo OnMouseDownMethodInfo = typeof(Control).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance);

        private static MethodInfo OnMouseMoveMethodInfo = typeof(Control).GetMethod("OnMouseMove", BindingFlags.NonPublic | BindingFlags.Instance);

        private static MethodInfo OnMouseUpMethodInfo = typeof(Control).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void PerformMouseDown(Control control, MouseEventArgs e) { OnMouseDownMethodInfo.Invoke(control, new object[1] { e }); }

        public static void PerformMouseMove(Control control, MouseEventArgs e) { OnMouseMoveMethodInfo.Invoke(control, new object[1] { e }); }

        private static void PerformMouseUp(Control control, MouseEventArgs e) { OnMouseUpMethodInfo.Invoke(control, new object[1] { e }); }

        private static HashSet<Control> PerformMouseAction_HoldedControls = new HashSet<Control>();

        private static void PerformMouseAction(Control control, PanEventArgs pe, Point clientPoint)
        {
            var me = new MouseEventArgs(MouseButtons.Left, 1, clientPoint.X, clientPoint.Y, 0);
            if (pe.Begin)
            {
                if (PerformMouseAction_HoldedControls.Add(control))
                {
                    PerformMouseDown(control, me);
                }
            }
            else if (!(pe.End || pe.Inertia))
            {
                PerformMouseAction_HoldedControls.Add(control);
                PerformMouseMove(control, me);
            }
            else
            {
                if (PerformMouseAction_HoldedControls.Remove(control))
                {
                    PerformMouseUp(control, me);
                }
            }
        }

        //private bool TouchListener_Pan_Gesturing = false;
        private void gestureListener_Pan(object sender, PanEventArgs e)
        {
            // DataGridViewScrollBarTouchFixer が先行することが前提
            if (e.Handled) return;

            CloseVisibleContextMenuStrips();

            Control control;
            Point clientPoint;

            // 順番が重要：水平セパレータ→垂直セパレータ→その他
            if (TouchListener_Pan_Test(control = splBookmark, e, out clientPoint))
            {
                PerformMouseAction(control, e, clientPoint);
                if (e.Inertia) control = null;
                e.Handled = true;
            }
            else if (TouchListener_Pan_Test(control = splRight, e, out clientPoint))
            {
                PerformMouseAction(control, e, clientPoint);
                if (e.Inertia) control = null;
                e.Handled = true;
            }
            else if (TouchListener_Pan_Test(control = splLeft, e, out clientPoint))
            {
                PerformMouseAction(control, e, clientPoint);
                if (e.Inertia) control = null;
                e.Handled = true;
            }
            else if (TouchListener_Pan_Test(control = tvCatalog, e, out clientPoint))
            {
                TouchListener_Pan_MouseGesture(e, mgCatalog, clientPoint);
            }
            else if (TouchListener_Pan_Test(control = dgvFileList, e, out clientPoint))
            {
                TouchListener_Pan_MouseGesture(e, mgFileList, clientPoint);
            }
            else if (TouchListener_Pan_Test(control = dgvDirectoryList, e, out clientPoint))
            {
                e.Handled = false;
            }
            else if (TouchListener_Pan_Test(control = explorerTreeView.TreeViewWnd, e, out clientPoint))
            {
                e.Handled = false;
            }
            else
            {
                control = null;
                e.Handled = false;
            }

            TouchListener_Pan_HoldedControl = e.End ? null : control;
            //MessageBox.Show($"{control}\n{OnMouseDownMethodInfo}\n{OnMouseMoveMethodInfo}\n{OnMouseUpMethodInfo}");
        }

        private bool TouchListener_Pan_Test(Control control, PanEventArgs e, out Point clientPoint)
        {
            clientPoint = control.PointToClient(e.Location);
            if (!control.Visible) return false;
            if (TouchListener_Pan_HoldedControl == null)
            {
                var rect = control.ClientRectangle;
                var sp = control as Splitter;
                if (sp != null)
                {
                    var dock = sp.Dock;
                    if (dock == DockStyle.Left || dock == DockStyle.Right)
                    {
                        rect.X -= rect.Width;
                        rect.Width *= 3;
                    }
                    else if (dock == DockStyle.Top || dock == DockStyle.Bottom)
                    {
                        rect.Y -= rect.Height;
                        rect.Height *= 3;
                    }
                }
                else
                {
                    var dgv = control as DataGridView;

                    if (dgv != null)
                    {
                        var totalWidth = 0;
                        foreach (DataGridViewColumn c in dgv.Columns)
                        {
                            if (c.Visible) totalWidth += c.Width;
                        }

                        if (totalWidth < rect.Width) rect.Width = totalWidth;
                    }
                }

                return e.Begin && rect.Contains(clientPoint);
            }
            else
            {
                return !e.Begin && TouchListener_Pan_HoldedControl == control;
            }
        }

        private int TouchListener_Pan_MouseGesture_StartClientPointY;

        private int TouchListener_Pan_MouseGesture_DataGridViewStartIndex = -1;

        private bool TouchListener_Pan_MouseGesture_GestureOrAfterGesture = false;

        private void TouchListener_Pan_MouseGesture(PanEventArgs e, MouseGesture mg, Point clientPoint)
        {
            e.Handled = true;
            if (e.Begin)
            {
                if (mg.Enabled)
                {
                    TouchListener_Pan_MouseGesture_StartClientPointY = e.Location.Y;
                    TouchListener_Pan_MouseGesture_DataGridViewStartIndex = -1;
                    TouchListener_Pan_MouseGesture_GestureOrAfterGesture = true;
                    mg.GestureBegin(clientPoint);
                }
            }
            else
            {
                if (!mg.Enabled)
                {
                    TouchListener_Pan_MouseGesture_GestureOrAfterGesture = false;
                }
                else if (!(e.End || e.Inertia))
                {
                    if (mg.GesturingOrbitCount > 1 || mg.InGesturing && Math.Abs(e.PanOffset.X) > Math.Abs(e.PanOffset.Y))
                    {
                        mg.GestureContinue(clientPoint);
                    }
                    else
                    {
                        mg.Clear();
                        TouchListener_Pan_MouseGesture_GestureOrAfterGesture = false;
                    }
                }
                else
                {
                    if (mg.GesturingOrbitCount > 1)
                    {
                        mg.GestureEnd(clientPoint, inNativeThread: true);
                    }
                    else if (mg.InGesturing)
                    {
                        mg.Clear();
                    }
                }
                if (!TouchListener_Pan_MouseGesture_GestureOrAfterGesture)
                {
                    if (mg.Owner is ScrollableControl scrollableControl)
                    {
                        var autoScrollPosition = scrollableControl.AutoScrollPosition;
                        var offset = e.PanOffset;
                        scrollableControl.AutoScrollPosition = new Point(-offset.X - autoScrollPosition.X, -offset.Y - autoScrollPosition.Y);
                    }
                    else
                    {
                        int count;
                        if (mg.Owner is DataGridView dataGridView && (count = dataGridView.RowCount) > 0)
                        {
                            var firstCell = dataGridView.FirstDisplayedCell;
                            if (firstCell != null)
                            {
                                int startIndex;
                                if (TouchListener_Pan_MouseGesture_DataGridViewStartIndex < 0)
                                {
                                    startIndex = firstCell.RowIndex;
                                    TouchListener_Pan_MouseGesture_DataGridViewStartIndex = startIndex;
                                }
                                else
                                {
                                    startIndex = TouchListener_Pan_MouseGesture_DataGridViewStartIndex;
                                }
                                var delta = e.Location.Y - TouchListener_Pan_MouseGesture_StartClientPointY;
                                var abs = Math.Abs(delta);
                                var den = Math.Max(1, firstCell.Size.Height);
                                var absDeltaIndex = (abs - den / 2) / den;
                                var newIndex = TouchListener_Pan_MouseGesture_DataGridViewStartIndex + (delta > 0 ? -absDeltaIndex : absDeltaIndex);
                                newIndex = Math.Max(0, Math.Min(count, newIndex));
                                if (firstCell.RowIndex != newIndex)
                                {
                                    dataGridView.FirstDisplayedCell = dataGridView.Rows[newIndex].Cells[firstCell.ColumnIndex];
                                }
                            }
                        }
                    }
                }
            }
        }

        // SetKeepOpen 実装２
        private static void SetKeepOpen(ToolStripMenuItem tsmi)
        {
            if (tsmi.HasDropDown)
            {
                tsmi.DropDown.Closing += SetKeepOpen_Closing;
                foreach (var item in tsmi.DropDownItems)
                {
                    var childTsmi = item as ToolStripMenuItem;
                    if (childTsmi != null)
                    {
                        SetKeepOpen(childTsmi);
                    }
                }
            }
        }

        private static void SetKeepOpen_Closing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
        }

        // SetKeepOpen 実装１

        private static readonly Bitmap ExplorerIcon = getExplorerIcon();

        private static Bitmap getExplorerIcon()
        {
            return ExplorerProvider.Icon;
        }

        private void focusVisibleMainList()
        {
            if (tvCatalog.Visible) tvCatalog.Focus();
            else dgvFileList.Focus();
        }
    }
}
