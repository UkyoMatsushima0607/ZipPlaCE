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
        private void tvCatalog_DisplayedChanged(object sender, EventArgs e)
        {
            SetBackgroundMode(true, 0);
        }

        private void dgvFileList_Scroll(object sender, ScrollEventArgs e)
        {
            //if (!tvCatalog.Visible)
            {
                SetBackgroundMode(true, 0);
            }
        }

        private void OpenSelectedArchiveInDefaultViewer(ApplicationProviderUser user, MouseEventArgs e)
        {
            try
            {
                var selectedIndex = tvCatalog.SelectedIndex;
                if (selectedIndex < 0) return;
                var zpa = ZipPathArray;
                if (zpa == null || zpa.Length != tvCatalog.Count) return;
                var zna = ZipNameArray;
                if (zna == null || zna.Length != zpa.Length) return;
                var path = zpa[selectedIndex];
                var name = zna[selectedIndex];
                if (CoverSettingMode)
                {
                    setCoverToThisToolStripMenuItem_Click(null, null);
                    return;
                }
                RecentFfmpegExists = MovieThumbnailLoader.ffmpegExists();
                var isDir = name.EndsWith(Path.DirectorySeparatorChar.ToString());
                if (ApplicationArray != null)
                {
                    foreach (var app in ApplicationArray)
                    {
                        if (app.IsDefaultFor(path, isDir))
                        {
                            activateManager.CancelAcrivatorReady(e);
                            app.Exec(user, path);
                            return;
                        }
                    }
                }
                throw new Exception(Message.ApplicationIsNotSpecified);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Task.Run(() => Invoke((MethodInvoker)(() => tvCatalog.DoMouseUp())));
            }
        }

        public void OpenInBuiltInViewer(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var extendedPath = path;

            var altSepPos = path.IndexOf(Path.AltDirectorySeparatorChar);
            var originalPath = path;
            if (altSepPos >= 0)
            {
                path = path.Substring(0, altSepPos);
            }

            var zpa = ZipPathArray;
            if (zpa == null)
            {
                OpenWaitingProcess(path, 0, false);
                return;
            }
            var index = Array.IndexOf(zpa, originalPath);
            if (index < 0)
            {
                OpenWaitingProcess(path, 0, false);
                return;
            }
            var lra = LoadResultArray;
            if (lra == null || lra.Length != zpa.Length)
            {
                OpenWaitingProcess(path, 0, false);
                return;
            }
            var lr = lra[index];
            if (lr == LoadResult.FileNotFound)
            {
                MessageBox.Show(this, Message.NoReadableFile, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            else if (lr == LoadResult.LoadError)
            {
                MessageBox.Show(this, Message.FailedToLoadFile, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (altSepPos < 0)
            {
                var zga = ZipPageArray;
                if (zga == null || zpa.Length != zpa.Length)
                {
                    OpenWaitingProcess(path, 0, false);
                    return;
                }
                try
                {
                    OpenWaitingProcess(path, zga[index], false);
                }
                catch
                {
                }
            }
            else
            {
                //OpenWaitingProcess(path, index, coverSettingMode: false);
                OpenWaitingProcess(extendedPath, -1, coverSettingMode: false);
            }
        }

        public bool IsNotEmptyAndIsNotLoadError(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var zpa = ZipPathArray;
            if (zpa == null) return true;
            var index = Array.IndexOf(zpa, path);
            if (index < 0) return true;
            var lra = LoadResultArray;
            if (lra == null || lra.Length != zpa.Length) return true;
            var lr = lra[index];
            return lr != LoadResult.FileNotFound && lr != LoadResult.LoadError;
        }

        private bool OpenInBuiltInViewer(int index)
        {
            if (index < 0) return false;
            var zpa = ZipPathArray;
            if (zpa == null || index != zpa.Length) return false;
            var path = zpa[index];
            var lra = LoadResultArray;
            if (lra == null || lra.Length != zpa.Length) return false;
            var lr = lra[index];
            if (lr == LoadResult.FileNotFound || lr == LoadResult.LoadError) return false;
            var zga = ZipPageArray;
            if (zga == null || zpa.Length != zpa.Length) return false;
            try
            {
                OpenWaitingProcess(path, zga[index], false);
            }
            catch
            {
                return false;
            }
            return true;
        }

        private bool setDropDownList()
        {
            if (CloseVisibleContextMenuStrips_ContextMenuStripCollection == null)
            {
                if (addToolStripMenuItemDropDown == null || ratingFilterToolStripMenuItemDropDown == null) return false;

                var result = new HashSet<ToolStripDropDown>();
                foreach (var field in typeof(CatalogForm).GetFields(BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var type = field.FieldType;
                    if (type == typeof(System.Windows.Forms.ContextMenuStrip))
                    {
                        var cms = field.GetValue(this) as System.Windows.Forms.ToolStripDropDown;
                        if (cms == null)
                        {
                            return false;
                        }
                        result.Add(cms);
                    }
                    else if (type == typeof(System.Windows.Forms.ToolStripMenuItem))
                    {
                        // 例外
                        switch (field.Name)
                        {
                            // 閉じる必要がなく、初めからは存在しないもの
                            case nameof(zabLocation_DirectoryButtonContextMenuStripOpening_SelectTreeNode): continue;
                            case nameof(zabLocation_DirectoryButtonContextMenuStripOpening_EditSmartFolder): continue;

                            // all でなければ閉じないもの
                            case nameof(viewToolStripMenuItem): continue;
                            case nameof(thumbnailSizeToolStripMenuItem): continue;
                        }

                        var tsmi = field.GetValue(this) as System.Windows.Forms.ToolStripMenuItem;
                        if (tsmi == null)
                        {
                            return false;
                        }
                        var ddm = tsmi.DropDown;
                        if (ddm != null) result.Add(ddm);
                    }
                }

                // 実体は null にされているので
                if (!result.Contains(addToolStripMenuItemDropDown)) result.Add(addToolStripMenuItemDropDown);
                if (!result.Contains(ratingFilterToolStripMenuItemDropDown)) result.Add(ratingFilterToolStripMenuItemDropDown);

                CloseVisibleContextMenuStrips_ContextMenuStripCollection = result.ToArray();
            }

            return true;
        }

        private System.Windows.Forms.ToolStripDropDown[] CloseVisibleContextMenuStrips_ContextMenuStripCollection;

        private void CloseVisibleContextMenuStrips(bool all = true)
        {
            if (!setDropDownList()) return;

            foreach (var cms in CloseVisibleContextMenuStrips_ContextMenuStripCollection)
            {
                cms.Close();
            }

            foreach (var cms in zabLocation.VisibleContextMenuStripSet)
            {
                try
                {
                    cms.Close();
                }
                catch { } // 破棄済みに備える
            }

            var temp = setTagFilteringMenuItems_DropDown;
            if (temp != null)
            {
                foreach (var cms in temp)
                {
                    try
                    {
                        cms.Close();
                    }
                    catch { } // 破棄済みに備える
                }
            }

            if (all)
            {
                try
                {
                    viewToolStripMenuItem.DropDown.Close();
                }
                catch { } // 破棄済みに備える
                try
                {
                    thumbnailSizeToolStripMenuItem.DropDown.Close();
                }
                catch { } // 破棄済みに備える
            }
        }

        private bool VisibleCobtextMenuStripExists()
        {
            if (!setDropDownList()) return false;

            if (CloseVisibleContextMenuStrips_ContextMenuStripCollection.Any(cms => cms.Visible) ||
            zabLocation.VisibleContextMenuStripSet.Any(cms =>
            {
                try
                {
                    return cms.Visible;
                }
                catch { return false; }  // 破棄済みに備える
            })) return true;

            var temp = setTagFilteringMenuItems_DropDown;
            if (temp != null)
            {
                foreach (var cms in temp)
                {
                    try
                    {
                        if (cms.Visible) return true;
                    }
                    catch { } // 破棄済みに備える
                }
            }

            return false;
        }

        private new Keys ModifierKeys
        {
            get
            {
                return simplifiedKeyBoard.GetModifierKeys();
            }
        }

        private static Rectangle ClickRectangle(Point center)
        {
            var size = SystemInformation.DoubleClickSize;
            var w = size.Width;
            var h = size.Height;
            return new Rectangle(center.X - w / 2, center.Y - h / 2, w | 1, h | 1);
        }

        private static bool InDoubleClickTime(int now, int lastClickedTime)
        {
            var delta = now - lastClickedTime;
            return 0 <= delta && delta <= SystemInformation.DoubleClickTime;
        }

        //private static readonly TimeSpan DoubleClickTime = TimeSpan.FromMilliseconds(SystemInformation.DoubleClickTime);

        private int tvCatalog_MouseDown_ClickCount = 0;

        private int tvCatalog_MouseDown_LastClickTime = 0;

        private int tvCatalog_MouseDown_LastClickIndex = -1;

        private Rectangle tvCatalog_MouseDown_LastClickRectangle = Rectangle.Empty;

        private void tvCatalog_MouseDown(object sender, MouseEventArgs e2)
        {
            // ActivateManager 側で対応

            LatestControledControl = sender as Control;

            var e = e2 as HandlableMouseEventArgs;

            if (e.Button == MouseButtons.Right)
            {
                if (AllowRightDragFromThumbnail)
                {
                    var index = tvCatalog.PointToIndex(e.Location);
                    if (index >= 0)
                    {
                        var dragStart = !mgCatalog.Enabled;
                        if (!dragStart)
                        {
                            var selected = index == tvCatalog.SelectedIndexDelay;
                            if (!selected)
                            {
                                selected = tvCatalog.SelectedIndicesCount >= 2 && tvCatalog.IsSelected(index);
                            }
                            dragStart = selected;
                        }
                        if (!dragStart)
                        {
                            simplifiedKeyBoard.GetModifierKeys(upHeldKeys: (e2.Button & mgCatalog.MouseButtons) == 0 || !mgCatalog.Enabled);
                            e.Handled = true;
                        }
                    }
                    else
                    {
                        e.Handled = true;
                        if (!mgCatalog.Enabled)
                        {
                            rightClickContextMenuPrepareAndShow(Cursor.Position, ModifierKeys); // この時点で ModifierKeys のホールド解除
                        }
                    }
                }
                else
                {
                    var mk = simplifiedKeyBoard.GetModifierKeys(upHeldKeys: (e2.Button & mgCatalog.MouseButtons) == 0 || !mgCatalog.Enabled); // こちらが後なので upHeldKeys = true で良い
                    if (!mgCatalog.Enabled)
                    {
                        rightClickContextMenuPrepareAndShow(Cursor.Position, mk);
                    }
                    e.Handled = true;
                }
            }
            else
            {
                var mk = simplifiedKeyBoard.GetModifierKeys(upHeldKeys: (e2.Button & mgCatalog.MouseButtons) == 0 || !mgCatalog.Enabled); // こちらが後なので upHeldKeys = true で良い
                if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
                {
                    var onStars = !drawRating_Bound.IsEmpty && showRating && InDirectoryVirtualDirectoryOrSmartDirectory;
                    Rectangle imageRectangle = Rectangle.Empty;
                    if (onStars)
                    {
                        imageRectangle = tvCatalog.PointToContainerImageRectangle(e.Location);
                        var backRectangle = drawRating_Bound;
                        backRectangle.X += imageRectangle.X;
                        backRectangle.Y += imageRectangle.Y;
                        onStars = backRectangle.Contains(e.Location);
                    }
                    if (onStars)
                    {
                        int? r;
                        if (e.Button == MouseButtons.Left)
                        {
                            r = Math.Max(1, Math.Min(5, 5 * (e.Location.X - (imageRectangle.X + drawRating_StarsBound.Left)) / drawRating_StarsBound.Width + 1));
                        }
                        else
                        {
                            r = null;
                        }
                        SetRating(r, tvCatalog.PointToIndex(e.Location));
                        e.Handled = true;
                    }
                    else
                    {
                        if (e.Button == MouseButtons.Left)
                        {
                            if ((mk & (Keys.Control | Keys.Shift)) == 0)
                            {
                                var dcm = DoubleClickFileOpenMode;
                                var clickedIndex = dcm ==
                                    DoubleClickMode.SelectAndClick ? tvCatalog.SelectedIndexDelay : tvCatalog.PointToIndex(e.Location);
                                if (clickedIndex >= 0)
                                {
                                    var now = Environment.TickCount;
                                    if (
                                        tvCatalog_MouseDown_LastClickIndex == clickedIndex &&
                                        InDoubleClickTime(now, tvCatalog_MouseDown_LastClickTime) &&
                                        (dcm != DoubleClickMode.Standard ||
                                        tvCatalog_MouseDown_ClickCount == 0 ||
                                        tvCatalog_MouseDown_LastClickRectangle.Contains(e.Location))
                                        )
                                    {
                                        tvCatalog_MouseDown_ClickCount++;
                                    }
                                    else
                                    {
                                        tvCatalog_MouseDown_ClickCount = 1;
                                        tvCatalog_MouseDown_LastClickRectangle = ClickRectangle(e.Location);
                                    }
                                    tvCatalog_MouseDown_LastClickIndex = clickedIndex;
                                    tvCatalog_MouseDown_LastClickTime = now;

                                    if (clickedIndex == tvCatalog.SelectedIndex)
                                    {
                                        if (dcm == DoubleClickMode.SelectAndClick && tvCatalog_MouseDown_ClickCount == 1
                                            || dcm != DoubleClickMode.SelectAndClick && tvCatalog_MouseDown_ClickCount == 2)
                                        {
                                            tvCatalog.DoMouseUp();
                                            OpenSelectedArchiveInDefaultViewer(ApplicationProviderUser.MouseClick, e2);
                                            e.Handled = true;
                                        }
                                    }
                                }
                                else
                                {
                                    tvCatalog_MouseDown_ClickCount = 0;
                                    tvCatalog_MouseDown_LastClickIndex = -1;
                                }
                            }

                        }
                        else
                        {
                            //cmsRightClickPrepareAndShow(Cursor.Position, onlyTag: true);
                            middleClickContextMenuPrepareAndShow(Cursor.Position);
                        }
                    }
                }
                else
                {
                    CloseVisibleContextMenuStrips();

                    if (e.Button == MouseButtons.XButton1)
                    {
                        btnGoToBack_Click(null, null);
                    }
                    else if (e.Button == MouseButtons.XButton2)
                    {
                        btnGoToForward_Click(null, null);
                    }
                }
            }
        }

        private void FocusLatestControledControl()
        {
            // LatestControledControl は null にならないように工夫されている
            LatestControledControl.Focus();
        }

        //private int dgvFileList_MouseDown_LastExcuseIndex = -1;
        //private DateTime dgvFileList_MouseDown_LastExcuseTime = DateTime.MinValue;

        private int dgvFileList_MouseDown_ClickCount = 0;

        private int dgvFileList_MouseDown_LastClickTime = 0;

        private int dgvFileList_MouseDown_LastClickIndex = -1;

        private Rectangle dgvFileList_MouseDown_LastClickRectangle = Rectangle.Empty;

        private void dgvFileList_MouseDown(object sender, MouseEventArgs e2)
        {
            // ActivateManager 側で対応

            LatestControledControl = sender as Control;

            var e = e2 as HandlableMouseEventArgs;

            var hit = dgvFileList.HitTest(e.X, e.Y);
            if ((e.Button == MouseButtons.Right && !AllowRightDragFromDetail) || e.Button == MouseButtons.Middle)
            {
                if (!dgvFileList.ReadOnly && !(hit.RowIndex == dgvFileList.CurrentCell.RowIndex && hit.ColumnIndex == dgvFileList.CurrentCell.ColumnIndex))
                {
                    dgvFileList.EndEdit();
                }

                Keys mk = Keys.None;
                if ((e.Button & mgFileList.MouseButtons) == 0 || !mgFileList.Enabled)
                {
                    mk = ModifierKeys;
                }

                if (e.Button == MouseButtons.Middle || !mgFileList.Enabled)
                {
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
                            dgvFileList.CurrentCell = clickedRow.Cells[tbcFileName.Index];
                        }
                    }

                    if (hit.RowIndex < 0 && hit.Type != DataGridViewHitTestType.ColumnHeader)
                    {
                        tvCatalog.SelectedIndices = null;
                    }

                    if (e.Button == MouseButtons.Right)
                    {
                        if (!mgFileList.Enabled)
                        {
                            if (hit.RowIndex >= 0 || hit.Type == DataGridViewHitTestType.None)
                            {
                                rightClickContextMenuPrepareAndShow(Cursor.Position, mk);
                            }
                        }
                    }
                    else if (hit.RowIndex >= 0 && hit.ColumnIndex != tbcRating.Index)
                    {
                        //cmsRightClickPrepareAndShow(Cursor.Position, onlyTag: true);
                        middleClickContextMenuPrepareAndShow(Cursor.Position);
                    }
                }
            }
            else if (e.Button == MouseButtons.Right) // この条件で AllowRightDragFromDetail 確定
            {
                if (!dgvFileList.ReadOnly && !(hit.RowIndex == dgvFileList.CurrentCell.RowIndex && hit.ColumnIndex == dgvFileList.CurrentCell.ColumnIndex))
                {
                    dgvFileList.EndEdit();
                }

                bool selected;
                if (hit.RowIndex >= 0)
                {
                    var clickedRow = dgvFileList.Rows[hit.RowIndex];
                    if (!(selected = clickedRow.Selected) && !mgFileList.Enabled)
                    {
                        var temp = selectionSynchronizing;
                        selectionSynchronizing = true;
                        dgvFileList.ClearSelection(); // SelectedRows を使うと選択中の行が共有されなくなる
                        selectionSynchronizing = temp;
                        clickedRow.Selected = true;
                        dgvFileList.CurrentCell = clickedRow.Cells[tbcFileName.Index];
                    }
                }
                else
                {
                    selected = false;
                }

                var s2d = tvCatalog.ShowIndexToDataIndex;
                var index = hit.RowIndex >= 0 && hit.RowIndex < s2d.Length ? s2d[hit.RowIndex] : -1;
                if (index >= 0)
                {
                    var dragStart = !mgFileList.Enabled || selected;
                    if (!dragStart)
                    {
                        simplifiedKeyBoard.GetModifierKeys(upHeldKeys: (e2.Button & mgFileList.MouseButtons) == 0 || !mgFileList.Enabled);
                        e.Handled = true;
                    }
                }
                else
                {
                    e.Handled = true;
                    if (hit.Type == DataGridViewHitTestType.None)
                    {
                        if (!mgFileList.Enabled)
                        {
                            tvCatalog.SelectedIndex = -1;
                            rightClickContextMenuPrepareAndShow(Cursor.Position, ModifierKeys); // この時点で ModifierKeys のホールド解除
                        }
                    }
                }
            }
            else if (e.Button == MouseButtons.Left)
            {
                var md = ModifierKeys;

                if (hit.RowIndex >= 0)
                {
                    var onRating = hit.ColumnIndex == tbcRating.Index;
                    e.Handled = onRating;
                    if (!onRating && dgvFileList.ReadOnly)
                    {
                        var s2d = tvCatalog.ShowIndexToDataIndex;
                        if ((md & (Keys.Control | Keys.Shift)) == 0 && s2d != null && s2d.Length > hit.RowIndex)
                        {
                            var dcm = DoubleClickFileOpenMode;
                            var clickedIndex = dcm == DoubleClickMode.SelectAndClick && !dgvFileList.IsRowSelected(hit.RowIndex) ? -1 : s2d[hit.RowIndex];
                            if (clickedIndex >= 0)
                            {
                                var now = Environment.TickCount;
                                if (
                                    dgvFileList_MouseDown_LastClickIndex == clickedIndex &&
                                    InDoubleClickTime(now, dgvFileList_MouseDown_LastClickTime) &&
                                    (dcm != DoubleClickMode.Standard ||
                                    dgvFileList_MouseDown_ClickCount == 0 ||
                                    dgvFileList_MouseDown_LastClickRectangle.Contains(e.Location))
                                    )
                                {
                                    dgvFileList_MouseDown_ClickCount++;
                                }
                                else
                                {
                                    dgvFileList_MouseDown_ClickCount = 1;
                                    dgvFileList_MouseDown_LastClickRectangle = ClickRectangle(e.Location);
                                }
                                dgvFileList_MouseDown_LastClickIndex = clickedIndex;
                                dgvFileList_MouseDown_LastClickTime = now;

                                if (dcm == DoubleClickMode.SelectAndClick && dgvFileList_MouseDown_ClickCount == 1
                                    || dcm != DoubleClickMode.SelectAndClick && dgvFileList_MouseDown_ClickCount == 2)
                                {
                                    if (tvCatalog.SelectedIndex >= 0)
                                    {
                                        OpenSelectedArchiveInDefaultViewer(ApplicationProviderUser.MouseClick, e2);
                                        e.Handled = true;
                                    }
                                }
                            }
                            else
                            {
                                dgvFileList_MouseDown_ClickCount = 0;
                                dgvFileList_MouseDown_LastClickIndex = -1;
                            }
                        }
                    }
                }
                if (hit.RowIndex < 0 && hit.Type != DataGridViewHitTestType.ColumnHeader)
                {
                    tvCatalog.SelectedIndices = null;
                    dgvFileList.EndEdit();
                }
            }

            //Thread.Sleep(1);
            //Application.DoEvents();
            try
            {
                Task.Run(() => Invoke((MethodInvoker)(() => dgvFileList.Update())));
            }
            catch (ObjectDisposedException) { }
        }

        private int pointToIndex(Point screenLocation)
        {
            Point localLocation;
            if (thumbnailListToolStripMenuItem.Checked)
            {
                localLocation = tvCatalog.PointToClient(screenLocation);
                if (tvCatalog.ClientRectangle.Contains(localLocation))
                {
                    return tvCatalog.PointToIndex(localLocation);
                }
            }

            localLocation = dgvFileList.PointToClient(screenLocation);
            if (dgvFileList.ClientRectangle.Contains(localLocation))
            {
                var showIndex = dgvFileList.HitTest(localLocation.X, localLocation.Y).RowIndex;
                var s2d = tvCatalog.ShowIndexToDataIndex;
                return 0 <= showIndex && s2d != null && showIndex < s2d.Length ? s2d[showIndex] : -1;
            }
            else return -1;
        }

        public bool RecentFfmpegExists;

        private void rightClickContextMenuPrepareAndShow(Point point, Keys mk)
        {
            if ((mk & Keys.Control) == Keys.Control)
            {
                contextMenuPrepareAndShow(point, ControlRightClickContextMenu);
            }
            else
            {
                contextMenuPrepareAndShow(point, RightClickContextMenu);
            }
        }

        private void rightClickContextMenuPrepareAndShow(Point point)
        {
            contextMenuPrepareAndShow(point, RightClickContextMenu);
        }

        private void middleClickContextMenuPrepareAndShow(Point point)
        {
            contextMenuPrepareAndShow(point, MiddleClickContextMenu);
        }

        private void contextMenuPrepareAndShow(Point point, CatalogFormConetxtMenu menu)
        {
            if (menu == CatalogFormConetxtMenu.ZipPla)
            {
                cmsRightClickPrepareAndShow_(point, false);
            }
            else if (menu == CatalogFormConetxtMenu.Tag)
            {
                cmsRightClickPrepareAndShow_(point, true);
            }
            else
            {
                explorerContextMenuPrepareAndShow(point);
            }
        }

        private void explorerContextMenuPrepareAndShow(Point point)
        {
            if (CreatingZipPathArray) return;

            AllowFullpower = false;

            var clickedIndex = pointToIndex(point);
            if (clickedIndex < 0)
            {
                AllowFullpower = true;
                cmsRightClickPrepareAndShow_(point, false);
                return;
            }

            string[] pathArray;
            bool[] isDirArray;
            getSelectedPathArray(clickedIndex, out pathArray, out isDirArray);
            if (pathArray == null || isDirArray == null)
            {
                AllowFullpower = true;
                return;
            }

            var infoArray = (from path in pathArray select new FileInfo(path)).ToArray();
            pathArray = null;
            isDirArray = null;

            if (cmsRightClick.Visible)
            {
                cmsRightClick.Close();
            }

            explorerContextMenu.ShowContextMenu(infoArray, point);

            AllowFullpower = true;
        }

        readonly Peter.ShellContextMenu explorerContextMenu = new Peter.ShellContextMenu();

        private CheckState[] cmsRightClickPrepareAndShow_TagInitialCheckState = null;

        private string[] cmsRightClickPrepareAndShow_SelectedPathList = null;

        private ToolStripItem[] cmsRightClickPrepareAndShow_tagToolStripMenuItemDropDownItems = null;

        //private string[] cmsRightClickPrepareAndShow_TagNames = null;
        bool cmsRightClickPrepareAndShow_ZipPlaInfoReady = false;

        private void CatalogForm_KeyDown(object sender, KeyEventArgs e)
        {
            SetLookAheadArchive_BlackPrefix = null;

            if (e.Alt)
            {
                if (e.KeyCode == Keys.Left)
                {
                    gotToUnderBuffer(undoBufferIndex - 1, ApplicationProviderUser.EnterKey);
                }
                else if (e.KeyCode == Keys.Right)
                {
                    gotToUnderBuffer(undoBufferIndex + 1, ApplicationProviderUser.EnterKey);
                }
                else if (e.KeyCode == Keys.Up)
                {
                    goUpFromCurrentLocation();
                }
                //e.Handled = true; // 1.3.3.5 で Alt+← 等でカーソルキー移動が発生しないように追記したが、
                // これにより メニュー表示や終了などの Alt の標準的な機能が使えなくなっていた。    
                // 1.6.4.6 においては ThumbView 側でそれが回避されているので不要
            }
            else if (e.KeyCode == Keys.Back)
            {
                if (NotInEditiongAnything())
                {
                    if (btnGoToBack.Enabled)
                    {
                        gotToUnderBuffer(undoBufferIndex - 1, ApplicationProviderUser.EnterKey);
                    }
                    else
                    {
                        goUpFromCurrentLocation();
                    }
                }
            }
            else if (e.KeyCode == Keys.Escape)
            {
                clearCutItemInClipBoard();
            }
            else if (e.KeyCode == Keys.Q && (e.Modifiers & Keys.Control) == Keys.Control) // 仮想キーボードは使用しない
            {
                Close();
            }
        }

        private void addToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            AllowFullpower = false;
            var validLocation = locationValid();
            currentLocationToolStripMenuItem.Enabled = validLocation;
            currentLocationAndFilterToolStripMenuItem.Enabled = validLocation;// && filterValid();
        }

        private bool locationValid()
        {
            try
            {
                return !string.IsNullOrEmpty(currentLocation) && !CreatingZipPathArray;// && VirtualFolder.DirectoryOrBookmarkExists(currentLocation);
            }
            catch
            {
                return false;
            }
        }

        private bool filterValid()
        {
            return cbFilter_Valid;
            //return cbFilter.BackColor == WindowColor;
        }

        private void currentLocationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!locationValid()) return;
            AddBookmarkWithCurrentProfileColor(currentLocation);
        }

        private void AddBookmarkWithCurrentProfileColor(string location)
        {
            addCatalogBookmarkToList(currentConditionToColoredBookmark(currentProfileColor, location));
            saveBookmarkToIni_bookmarkChanged = true;
            saveBookmarkToConfig();
        }

        private void currentLocationAndFilterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!locationValid()) return;

            AddBookmarkWithNewProfileColor(currentLocation);
        }

        private void AddBookmarkWithNewProfileColor(string location)
        {
            AllowFullpower = false;
            var newColor = getNewProfileColorFromDialog(useCurrentAsDefault: false);
            if (!newColor.IsEmpty)

            {
                synchronizeCurrentProfile();

                var currentConditionBookmark = currentConditionToColoredBookmark(newColor, location);

                addCatalogBookmarkToList(currentConditionBookmark);
                saveBookmarkToIni_bookmarkChanged = true;
                saveBookmarkToConfig();

                var prevProfileInfo = Tuple.Create(currentProfileColor, currentConditionToProfile());

                currentProfileColor = newColor;

                // if (Tags == null) Tags = new ZipTag[0]; // currentConditionToColoredBookmark で行われる
                var config = new ZipTagConfig();
                config.Tags = Tags;
                config.ProfileColor = currentProfileColor;
                config.Save();

                undoBufferUpdateForMovingLocation(0, prevProfileInfo);

            }
            AllowFullpower = true;
        }
    }
}
