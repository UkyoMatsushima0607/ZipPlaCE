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
    public partial class CatalogForm
    {
        private void explorerTreeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                AllowFullpower = false;

                if (cmsRightClick.Visible)
                {
                    cmsRightClick.Close();
                }

                try
                {
                    explorerTreeView.SelectedNode = e.Node;
                    var path = ((ShellItem)e.Node.Tag).Path;
                    explorerContextMenu.ShowContextMenu(new FileInfo[] { new FileInfo(path) }, explorerTreeView.PointToScreen(e.Location));
                }
                catch { }
                finally
                {
                    AllowFullpower = true;
                }
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (synchroFromTreeToBar)
                {
                    var bound = e.Node.Bounds;
                    bound.X -= bound.Height;
                    bound.Width = explorerTreeView.TreeViewWnd.Right - bound.Left;
                    if (bound.Contains(e.Location))
                    {
                        explorerTreeView.SelectedNode = e.Node;
                        var path = ((ShellItem)e.Node.Tag).Path;
                        if (IsSupportedByCatalogFormAndExists(path))
                        {
                            zabLocation.Text = path;
                            MakePreview(needToSynchroTree: false);
                        }
                    }
                }
            }
            //var node = e.Node;
            //explorerTreeView.TreeViewWnd.SelectedNode
            //MessageForm.Show($"{e.Button}\n{e.Location}\n{(e.Node.Tag as ShellItem).Path}", "cap", "OK", MessageBoxIcon.None);
        }

        private void explorerTreeView_DragEnter(object sender, DragEventArgs e)
        {
            if (explorerTreeView_TreeWndItemDrag_DragOuting)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            dropToMoveCopyControl_DragEnter(sender, e);
        }

        enum DragDropFileType { None, RealDefault, RealAlt, Virtual }

        DragDropFileType explorerTreeView_TreeWndDragEnter_FileType = DragDropFileType.None;

        string[] explorerTreeView_TreeWndDragEnter_Files = null;

        private bool explorerTreeView_AllowDropVirtualFile = true;

        private void dropToMoveCopyControl_DragEnter(object dummySender, DragEventArgs e)
        {
            explorerTreeView_TreeWndDragEnter_FileType = DragDropFileType.None;
            try
            {
                var data = e.Data;
                if (e.KeyState == DragEventKeyState_LeftButton || e.KeyState == (int)Keys.RButton)
                {
                    if (data.GetDataPresent(DataFormats.FileDrop))
                    {
                        explorerTreeView_TreeWndDragEnter_Files = data.GetData(DataFormats.FileDrop, false) as string[];
                        if (explorerTreeView_TreeWndDragEnter_Files != null && explorerTreeView_TreeWndDragEnter_Files.Length > 0)
                        {
                            explorerTreeView_TreeWndDragEnter_FileType = e.KeyState == (int)Keys.LButton ? DragDropFileType.RealDefault : DragDropFileType.RealAlt;
                        }
                    }
                    else if (explorerTreeView_AllowDropVirtualFile && e.KeyState == DragEventKeyState_LeftButton && data.GetDataPresent(NativeMethods.ZIPPLA_FILECONTENTS))
                    {
                        explorerTreeView_TreeWndDragEnter_FileType = e.KeyState == (int)Keys.LButton ? DragDropFileType.Virtual : DragDropFileType.None;
                    }
                }
            }
            catch
            {
                explorerTreeView_TreeWndDragEnter_FileType = DragDropFileType.None;
            }

            if (explorerTreeView_TreeWndDragEnter_FileType == DragDropFileType.None)
            {
                explorerTreeView_TreeWndDragEnter_Files = null;
                e.Effect = DragDropEffects.None;
            }
            else
            {
                Activate();
            }
        }

        enum SameDirEffectMode { None, Link, Ignore }

        private DragDropEffects getDragEffect(string path, DragDropEffects allowedEffect, SameDirEffectMode sameDirMode)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    if (explorerTreeView_TreeWndDragEnter_FileType == DragDropFileType.Virtual)
                    {
                        if ((allowedEffect & DragDropEffects.Copy) == DragDropEffects.Copy)
                        {
                            return DragDropEffects.Copy;
                        }
                    }
                    else
                    {
                        var pathInLower = path.ToLower();
                        if (pathInLower.Last() == ':') pathInLower += Path.DirectorySeparatorChar;
                        if (explorerTreeView_TreeWndDragEnter_Files != null)
                        {
                            var sameCheck = sameDirMode == SameDirEffectMode.Ignore;
                            if (!sameCheck)
                            {
                                sameCheck = explorerTreeView_TreeWndDragEnter_Files.All(file =>
                                {
                                    var f = file.ToLower();
                                    return Path.GetDirectoryName(f) != pathInLower && f != pathInLower && !pathInLower.StartsWith(f[f.Length - 1] == Path.DirectorySeparatorChar ? f : (f + Path.DirectorySeparatorChar));
                                });
                            }
                            if (sameCheck || sameDirMode == SameDirEffectMode.Link)
                            {
                                var dstRoot = Path.GetPathRoot(pathInLower);
                                if ((allowedEffect & DragDropEffects.Move) == DragDropEffects.Move && explorerTreeView_TreeWndDragEnter_Files.All(file => Path.GetPathRoot(file).ToLower() == dstRoot))
                                {
                                    return sameCheck ? DragDropEffects.Move : DragDropEffects.Link;
                                }
                                else if ((allowedEffect & DragDropEffects.Copy) == DragDropEffects.Copy)
                                {
                                    return sameCheck ? DragDropEffects.Copy : DragDropEffects.Link;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    return DragDropEffects.None;
                }
            }
            return DragDropEffects.None;
        }

        private void explorerTreeView_TreeWndDragOver(object sender, DragEventArgs e)
        {
            if (explorerTreeView_TreeWndItemDrag_DragOuting)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            if (explorerTreeView_TreeWndDragEnter_FileType != DragDropFileType.None)
            {
                var treeViewWnd = explorerTreeView.TreeViewWnd;
                var pos = treeViewWnd.PointToClient(Cursor.Position);
                var hit = treeViewWnd.HitTest(pos);
                var node = hit.Node;
                if (node != null)
                {
                    treeViewWnd.SelectedNode = node;
                    var path = (node.Tag as ShellItem)?.Path;
                    e.Effect = getDragEffect(path, e.AllowedEffect, SameDirEffectMode.None);
                    if (!node.IsExpanded && node.Nodes.Count > 0)
                    {
                        var bounds = node.Bounds;
                        //var imageBound = treeViewWnd.ImageList.ImageSize; // 通常の ImageList ではない
                        if (pos.X < bounds.Left - bounds.Height)
                        {
                            node.Expand();
                        }
                    }
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void dropToMoveCopyControl_DragDrop(DragEventArgs e, Func<DragEventArgs, string> pathGetter, Action finallyAction)
        {
            if (explorerTreeView_TreeWndItemDrag_DragOuting)
            {
                return;
            }
            if (explorerTreeView_TreeWndDragEnter_FileType != DragDropFileType.None)
            {
                var path = pathGetter(e);
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.Last() == ':') path += Path.DirectorySeparatorChar;
                    try
                    {
                        if (explorerTreeView_TreeWndDragEnter_FileType == DragDropFileType.Virtual)
                        {
                            if ((e.AllowedEffect & DragDropEffects.Copy) == DragDropEffects.Copy)
                            {
                                pasteToTarget(this, path, e.Data, DragDropEffects.Copy);
                            }
                        }
                        else
                        {
                            var pathInLower = path.ToLower();
                            if (explorerTreeView_TreeWndDragEnter_Files != null &&
                                explorerTreeView_TreeWndDragEnter_Files.All(file => Path.GetDirectoryName(file).ToLower() != pathInLower))
                            {
                                var dstRoot = Path.GetPathRoot(pathInLower);
                                DragDropEffects defaultEffect;
                                if ((e.AllowedEffect & DragDropEffects.Move) == DragDropEffects.Move && explorerTreeView_TreeWndDragEnter_Files.All(file => Path.GetPathRoot(file).ToLower() == dstRoot))
                                {
                                    defaultEffect = DragDropEffects.Move;
                                }
                                else if ((e.AllowedEffect & DragDropEffects.Copy) == DragDropEffects.Copy)
                                {
                                    defaultEffect = DragDropEffects.Copy;
                                }
                                else
                                {
                                    defaultEffect = DragDropEffects.None;
                                }
                                if (defaultEffect != DragDropEffects.None)
                                {
                                    if (explorerTreeView_TreeWndDragEnter_FileType == DragDropFileType.RealDefault)
                                    {
                                        pasteToTarget(this, path, e.Data, defaultEffect);
                                    }
                                    else if (explorerTreeView_TreeWndDragEnter_FileType == DragDropFileType.RealAlt)
                                    {
                                        //var name = SimpleBookmark.GetDisplayName(path);
                                        //moveHereToolStripMenuItem.Text = Message.Movehe
                                        //copyHereToolStripMenuItem.Text = Message.CopyTo1.Replace("$1", name);
                                        var req = Tuple.Create(path, e.Data, finallyAction);
                                        //Activate(); // 非アクティブ状態でコンテクストメニューを出すとそれが残る → これを実行する時点で別のメニューが表示されていると正常に動作しなくなるので MouseEnter に移動
                                        if (this != ActiveForm) Activate();
                                        cmsRightClickDrop.Show(e.X, e.Y);
                                        cmsRightClickDrop_Request = req;
                                        finallyAction = null;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                explorerTreeView_TreeWndDragEnter_FileType = DragDropFileType.None;
                explorerTreeView_TreeWndDragEnter_Files = null;
            }
            finallyAction?.Invoke();
        }

        private Tuple<string, System.Windows.Forms.IDataObject, Action> cmsRightClickDrop_Request = null;

        private void cmsRightClickDrop_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            if (e.CloseReason != ToolStripDropDownCloseReason.ItemClicked && cmsRightClickDrop_Request != null)
            {
                cmsRightClickDrop_Request.Item3?.Invoke();
                cmsRightClickDrop_Request = null;
            }
            AllowFullpower = true;
        }

        private void copyHereToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (cmsRightClickDrop_Request != null)
            {
                try
                {
                    pasteToTarget(this, cmsRightClickDrop_Request.Item1, cmsRightClickDrop_Request.Item2, DragDropEffects.Copy);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    cmsRightClickDrop_Request.Item3?.Invoke();
                    cmsRightClickDrop_Request = null;
                }
            }
        }

        private void moveHereToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (cmsRightClickDrop_Request != null)
            {
                try
                {
                    pasteToTarget(this, cmsRightClickDrop_Request.Item1, cmsRightClickDrop_Request.Item2, DragDropEffects.Move);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    cmsRightClickDrop_Request.Item3?.Invoke();
                    cmsRightClickDrop_Request = null;
                }
            }
        }

        private void explorerTreeView_TreeWndDragDrop(object sender, DragEventArgs e)
        {
            dropToMoveCopyControl_DragDrop(e, explorerTreeView_PathGetter, finallyAction: null);
        }

        private string explorerTreeView_PathGetter(DragEventArgs e)
        {
            var treeViewWnd = explorerTreeView.TreeViewWnd;
            var pos = treeViewWnd.PointToClient(Cursor.Position);
            var hit = treeViewWnd.HitTest(pos);
            var node = hit.Node;
            if (node != null)
            {
                treeViewWnd.SelectedNode = node;
                return (node.Tag as ShellItem)?.Path;
            }
            else
            {
                return null;
            }
        }

        private void dgvFileList_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex >= 0 && e.RowIndex < 0)
            {
                var column = dgvFileList.Columns[e.ColumnIndex];
                var dir = column.HeaderCell.SortGlyphDirection;
                if (dir != SortOrder.None)
                {
                    e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentBackground & ~DataGridViewPaintParts.ContentForeground);

                    const float normalizedUpper = 0.37f;
                    const float normalizedLower = 0.63f;

                    var bound = e.CellBounds;
                    var top = bound.Top;
                    var height = bound.Height;
                    var center = bound.Right - tbcIconWidth / 2f;
                    var upper = top + normalizedUpper * height;
                    var lower = top + normalizedLower * height;
                    var halfOfWidth = lower - upper;


                    if (dir == SortOrder.Descending)
                    {
                        e.Graphics.FillPolygon(SystemBrushes.ButtonShadow, new PointF[]
                        {
                            new PointF(center - halfOfWidth, upper),
                            new PointF(center + halfOfWidth, upper),
                            new PointF(center , lower),
                        });
                    }
                    else
                    {
                        e.Graphics.FillPolygon(SystemBrushes.ButtonShadow, new PointF[]
                        {
                            new PointF(center - halfOfWidth, lower),
                            new PointF(center + halfOfWidth, lower),
                            new PointF(center , upper),
                        });
                    }

                    e.Paint(e.ClipBounds, e.PaintParts & DataGridViewPaintParts.ContentForeground);

                    e.Handled = true;
                }
            }
        }

        private void explorerTreeView_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            // コントロールがフォーカスを失っても選択色を濃いままにすることが目的

            var node = e.Node;
            var treeView = (TreeView)sender;
            var g = e.Graphics;
            var bounds = node.Bounds;

            Color backColor, foreColor;
            if ((e.State & TreeNodeStates.Selected) != 0)
            {
                backColor = SystemColors.Highlight;
                foreColor = SystemColors.HighlightText;

                e.Graphics.FillRectangle(SystemBrushes.Highlight, bounds);
            }
            else
            {
                backColor = node.BackColor;
                foreColor = node.ForeColor;
                if (backColor == Color.Empty) backColor = treeView.BackColor;
                if (foreColor == Color.Empty) foreColor = treeView.ForeColor;
                using (var b = new SolidBrush(backColor))
                {
                    g.FillRectangle(b, bounds);
                }
            }

            // g.DrawString は枠内に収まらない
            // TextRenderer なら TextFormatFlags.NoPadding でなくとも枠内に収まる
            // TextFormatFlags.NoPadding にすると高解像度環境で正常に表示できない
            var text = node.Text;
            var font = node.NodeFont ?? treeView.Font;
            TextRenderer.DrawText(g, text, font, bounds, foreColor, backColor,
                 TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }

        private void cbLocation_DragEnter(object sender, DragEventArgs e)
        {

            if (zabLocation.DragOuting)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            try
            {
                if (e.KeyState == (int)Keys.LButton && zabLocation.Enabled && !zabLocation.VisibleContextMenuStripSetHasElement && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] Files = ShortcutResolver.Exec((string[])e.Data.GetData(DataFormats.FileDrop, false)).ToArray();
                    if (Files.Length == 1)
                    {
                        e.Effect = DragDropEffects.Copy;
                        CloseVisibleContextMenuStrips(); // フォルダのドロップダウンが表示されていることがある
                        cbLocation.Focus();
                    }
                    else
                    {
                        e.Effect = DragDropEffects.None;
                    }
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            }
            catch (Exception error)
            {
                e.Effect = DragDropEffects.None;
                MessageBox.Show(this, error.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cbLocation_DragLeave(object sender, EventArgs e)
        {
            focusVisibleMainList();
        }

        private void cbLocation_DragDrop(object sender, DragEventArgs e)
        {
            string path;
            try
            {
                path = ShortcutResolver.Exec(((string[])e.Data.GetData(DataFormats.FileDrop, false))[0]);
            }
            catch
            {
                return;
            }
            MoveLocation(ApplicationProviderUser.DragAndDrop, path);
        }

        private void newFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            createNewFolderToCurrentLocation();
        }

        private void createNewFolderToCurrentLocation()
        {
            createNewToCurrentLocation(Message.NewFolder + Path.DirectorySeparatorChar);
        }

        private void noneFolderTreeSynchronizingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setSynchroBetweenTreeAndBar(treeToBar: false, barToTree: false);
        }

        private void folderTreeToAddressbarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setSynchroBetweenTreeAndBar(treeToBar: true, barToTree: false);
        }

        private void addressBarToFolderTreeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setSynchroBetweenTreeAndBar(treeToBar: false, barToTree: true);
        }

        private void twowayToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setSynchroBetweenTreeAndBar(treeToBar: true, barToTree: true);
        }

        private void collapseOthersWhenSynchronizingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CollapseOthersWhenSynchronizing = !CollapseOthersWhenSynchronizing;
        }

        bool explorerTreeView_TreeWndItemDrag_DragOuting = false;

        private void createViewerShortcutFromCurrentSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AllowFullpower = false;
            viewToolStripMenuItem.DropDown.Close();
            var info = new ViewerForm.CommandLineOptionInfo();
            info.AlwaysHideUI = AlwaysHideUI;
            info.ArchivesInArchiveMode = ArchivesInArchiveMode;
            info.ReadOnMemoryMode = ReadOnMemoryMode;
            info.BackColor = backColorOfBuiltInViewer;
            info.CoverBindingMode = CoverBindingMode;
            info.DefaultBindingMode = currentBindingMode;
            info.InitialFullscreenMode = BuiltInViewerInitialFullscreenMode;
            info.OpenInPreviousImageFilterSetting = OpenInPreviousImageFilterSetting;
            ViewerForm.CreateShortcut(this, info, currentLocation);
            AllowFullpower = true;
        }

        private bool IsDisplay(SubfolderMode subfolderMode)
        {
            return subfolderMode == SubfolderMode.Display || subfolderMode == SubfolderMode.DisplayHeader || subfolderMode == SubfolderMode.DisplayAndSearch || subfolderMode == SubfolderMode.DisplayHeaderAndSearch;
        }

        // 自動でドロップダウンしない場合は空白になるのが妥当な使用と判断しコメントアウト

        private void dgvDirectoryList_Paint(object sender, PaintEventArgs e)
        {
            if (showStartHelpMessageOfDirectoryList && dgvDirectoryList.Visible && dgvDirectoryList.RowCount == 0)
            {
                var message = Message.AddBookmarkDescription.Replace(@"\n", "\n");

                var g = e.Graphics;
                var preMode = g.TextRenderingHint;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                var drawInitialMessageFont = GetDrawInitialMessageFont();

                var offset = (int)(drawInitialMessageFont.Size / 2);
                var rect = dgvDirectoryList.ClientRectangle;
                rect.X += offset;
                rect.Y += offset;
                rect.Width -= offset;
                rect.Height -= offset;

                using (var sf = new StringFormat())
                {
                    sf.FormatFlags = StringFormatFlags.DirectionVertical | StringFormatFlags.DirectionRightToLeft;
                    sf.LineAlignment = StringAlignment.Far;
                    g.DrawString(message, drawInitialMessageFont, SystemBrushes.GrayText, rect, sf);
                }
                
                g.TextRenderingHint = preMode;
            }
        }

        private void AddOnlyTheCurrentProfileColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddBookmarkWithCurrentProfileColor(null);
        }

        private void addOnlyANewProfileColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddBookmarkWithNewProfileColor(null);
        }

        private void aboutAccessKeyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AllowFullpower = false;
            MessageForm.Show(this, Message.BookmarkAccessKeyUsageDescription.Replace("\\n", "\n").Replace("&", "&&"), Message.AboutAccessKey, Message.OK, MessageBoxIcon.Information);
            AllowFullpower = true;
        }

        private void loadOnlyProfileColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedRows = dgvDirectoryList.SelectedRows;
            if (selectedRows.Count != 1) return;
            
            moveToBookmark(selectedRows[0].Index, ApplicationProviderUser.EnterKey, loadOnlyProfileColor: true);
        }

        private void explorerTreeView_TreeWndItemDrag(object sender, ItemDragEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                try
                {
                    TreeView tv = (TreeView)sender;
                    var node = (TreeNode)e.Item;
                    //tv.SelectedNode = 
                    var item = (ShellItem)node.Tag;
                    if (!item.IsFolder) return;
                    var path = item.Path;
                    if (path == "") return;
                    var files = new string[1] { path };
                    //DataObjectを作成する
                    DataObject dataObj = new DataObject(DataFormats.FileDrop, files);
                    tv.SelectedNode = node;
                    //ノードのドラッグを開始する
                    explorerTreeView_TreeWndItemDrag_DragOuting = true;
                    DragDropEffects dde = tv.DoDragDrop(dataObj, DragDropEffects.All);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    explorerTreeView_TreeWndItemDrag_DragOuting = false;
                }
            }
        }

        private void explorerTreeView_TreeWndQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            if (((Keys)e.KeyState & (Keys.LButton | Keys.RButton)) == (Keys.LButton | Keys.RButton))
            {
                e.Action = DragAction.Cancel;
            }
        }

        private void explorerTreeView_TreeWndMouseDown(object sender, MouseEventArgs e)
        {
            simplifiedKeyBoard.UpHeldKey();
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                try
                {
                    var treeWnd = explorerTreeView.TreeViewWnd;
                    var hit = treeWnd.HitTest(e.Location);
                    treeWnd.SelectedNode = hit.Node;
                }
                catch { }
            }
        }

        private void explorerTreeView_TreeWndKeyDown(object sender, KeyEventArgs e)
        {
            var mk = ModifierKeys;
            if (commonKeyAction(e, mk))
            {
                return;
            }
        }

        private static Size sizeProviderForOriginalSizeAsPossible(Fraction aspect, Size originalSize)
        {
            var pixelAspect = (Fraction)originalSize;
            if (aspect == pixelAspect) return originalSize;

            else if (aspect < pixelAspect)
            {
                return new Size((aspect * originalSize.Height).FloorOfPlusHalf(), originalSize.Height);
            }
            else
            {
                return new Size(originalSize.Width, (originalSize.Width / aspect).FloorOfPlusHalf());
            }
            // 誤り
        }

        public bool alertInvisibleItemsOnPaste(IEnumerable<string> dsts, bool checkPath, bool checkAttribute)
        {
            if (dsts == null) return false;
            int dummy;
            var archiveSkip = ArchiveLoadLevel == ItemLoadLevel.Ignore;
            var imageSkip = ImageLoadLevel == ItemLoadLevel.Ignore;
            var videoSkip = VideoLoadLevel == ItemLoadLevel.Ignore;
            var otherSkip = OtherLoadLevel == ItemLoadLevel.Ignore;
            var ffmpegExists = MovieThumbnailLoader.ffmpegExists();

            if (checkAttribute && checkPath)
            {
                bool becauseOfAtt = false;
                if (dsts.Any(path => ShouldBeSkipForFileAndDirWithCheckAttributes(path, archiveSkip, imageSkip, videoSkip, otherSkip, ffmpegExists, out becauseOfAtt)))
                {
                    if (becauseOfAtt)
                    {
                        return MessageBox.Show(this, Message.ItemsCanBeMovedCopiedButSomeOfThemWillNotBeDisplayedBecauseOfItsAttribute,
                            Message.Information, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK;
                    }
                    else
                    {
                        return MessageBox.Show(this, Message.ItemsCanBeMovedCopiedButSomeOfThemWillNotBeDisplayedBecauseOfViewSetting,
                            Message.Information, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK;
                    }
                }
                else return true;
            }
            else
            {
                if (checkPath && dsts.Any(path => ShouldBeSkipForFile(out dummy, path, archiveSkip, imageSkip, videoSkip, otherSkip, ffmpegExists)))
                {
                    return MessageBox.Show(this, Message.ItemsCanBeMovedCopiedButSomeOfThemWillNotBeDisplayedBecauseOfViewSetting,
                        Message.Information, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK;
                }
                else if (checkAttribute && dsts.Any(path => ShouldBeSkipForFileAndDirWithCheck_Only_Attributes(path, ffmpegExists)))
                {
                    return MessageBox.Show(this, Message.ItemsCanBeMovedCopiedButSomeOfThemWillNotBeDisplayedBecauseOfItsAttribute,
                        Message.Information, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK;
                }
                else return true;
            }
        }

        public static DragDropEffects GetPreferredDropEffect(System.Windows.Forms.IDataObject data)
        {
            DragDropEffects dde = DragDropEffects.None;

            if (data != null)
            {
                try
                {
                    System.IO.MemoryStream ms =
                        (System.IO.MemoryStream)data.GetData("Preferred DropEffect");
                    if (ms != null)
                    {
                        dde = (DragDropEffects)ms.ReadByte();
                    }
                }
                catch { }
            }
            return dde;
        }
    }
}
