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
        private void zabLocation_SubControlDragEnter(ZipPlaAddressBar sender, ZipPlaAddressBarDragEventArgs e)
        {
            var e2 = new DragEventArgs(e.Data, e.KeyState, e.X, e.Y, e.AllowedEffect, e.AllowedEffect);
            dropToMoveCopyControl_DragEnter(sender, e2);
            if (e2.Effect != DragDropEffects.None)
            {
                if (string.IsNullOrEmpty(e.Path))
                {
                    e.Effect = DragDropEffects.All;
                }
                else
                {
                    e.Effect = getDragEffect(e.Path, e.AllowedEffect, SameDirEffectMode.Link);
                }
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void zabLocation_SubControlDragDrop(ZipPlaAddressBar sender, ZipPlaAddressBarDragEventArgs e)
        {
            dropToMoveCopyControl_DragDrop(e, zabLocation_PathGetter, e.FinallyAction);
        }

        private static string zabLocation_PathGetter(DragEventArgs e)
        {
            return ((ZipPlaAddressBarDragEventArgs)e).Path;

        }

        private void zabLocation_TextChangedByBaseControl(object sender, EventArgs e)
        {
            if (!cbLocation.Focused) cbLocation.Focus();
        }

        private void zabLocation_ZipPlaAddressBarMakeButtonStarting(ZipPlaAddressBar sender, ZipPlaAddressBarMakeButtonStartingEventArgs e)
        {
            if (e.CommandSender == ZipPlaAddressBarCommandSender.Leave && !CreatingZipPathArray && currentLocation == preCurrentLocation && IsSupportedByCatalogFormAndExists(currentLocation)) e.Text = currentLocation;
        }

        private void zabLocation_DragDropFinished(object sender, MouseEventArgs e)
        {
            dragOuting = false;
        }

        private void zabLocation_DragDropStarting(object sender, MouseEventArgs e)
        {
            dragOuting = true;
        }

        private void zabLocation_CurrentLocationButtonClick(object sender, EventArgs e)
        {
            var newLocation = zabLocation.Text;
            if (newLocation != null && currentLocation?.ToLower() != newLocation.ToLower())
            {
                MakePreview();
            }
            else
            {
                UpdatePreview(stopResetFilter: false);
            }
        }

        private void setUndoButton(Button button, bool goBack)
        {
            if (goBack) button.Paint += ControlPainter.FillLeftArrowControlHandler;
            else button.Paint += ControlPainter.FillRightArrowControlHandler;
            button.Text = null;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = BackColor;
        }

        private bool zabLocation_DirectoryButtonContextMenuStripOpening_first = true;

        private string zabLocation_DirectoryButtonContextMenuStripOpening_Path = null;

        private ToolStripMenuItem zabLocation_DirectoryButtonContextMenuStripOpening_SelectTreeNode = null;

        private ToolStripSeparator zabLocation_DirectoryButtonContextMenuStripOpening_Separator = null;

        private ToolStripMenuItem zabLocation_DirectoryButtonContextMenuStripOpening_EditSmartFolder = null;

        private void zabLocation_DirectoryButtonContextMenuStripOpening(ZipPlaAddressBar sender, ZipPlaAddressBarDirectoryButtonContextMenuStripOpeningEventArgs e)
        {
            if (zabLocation_DirectoryButtonContextMenuStripOpening_first)
            {
                zabLocation_DirectoryButtonContextMenuStripOpening_first = false;

                e.ContextMenuStrip.Closed += contextMenu_ClosedAllowFullpower;
                var items = e.ContextMenuStrip.Items;
                items[0].Image = ExplorerIcon;// ExplorerProvider.Icon;
                items[1].Image = SelectionToClipBoardToolStripMenuItem.Icon;

                zabLocation_DirectoryButtonContextMenuStripOpening_SelectTreeNode = new ToolStripMenuItem(Message.SelectInFolderTree);
                zabLocation_DirectoryButtonContextMenuStripOpening_SelectTreeNode.Click += zabLocation_DirectoryButtonContextMenuStripOpening_SelectTreeNode_Click;
                try
                {
                    zabLocation_DirectoryButtonContextMenuStripOpening_SelectTreeNode.Image = FileTypeManager.GetSmallIconBitmap(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        useFileAttrinutes: false);
                }
                catch { }
                items.Add(zabLocation_DirectoryButtonContextMenuStripOpening_SelectTreeNode);

                items.Add(zabLocation_DirectoryButtonContextMenuStripOpening_Separator = new ToolStripSeparator());

                zabLocation_DirectoryButtonContextMenuStripOpening_EditSmartFolder = new ToolStripMenuItem(Message.EditSmartFolder + "...");
                zabLocation_DirectoryButtonContextMenuStripOpening_EditSmartFolder.Click += zabLocation_DirectoryButtonContextMenuStripOpening_EditSmartFolder_Click;
                items.Add(zabLocation_DirectoryButtonContextMenuStripOpening_EditSmartFolder);
            }
            else
            {
                zabLocation_DirectoryButtonContextMenuStripOpening_EditSmartFolder.Text = Message.EditSmartFolder + "...";
                zabLocation_DirectoryButtonContextMenuStripOpening_SelectTreeNode.Text = Message.SelectInFolderTree;
            }

            zabLocation_DirectoryButtonContextMenuStripOpening_Path = e.Path;

            zabLocation_DirectoryButtonContextMenuStripOpening_Separator.Visible =
            zabLocation_DirectoryButtonContextMenuStripOpening_EditSmartFolder.Visible = e.IsFile && SmartFolder.IsSmartFolderPath(e.Path);

            //zabLocation_DirectoryButtonContextMenuStripOpening_SelectTreeNode.Visible = e.IsDir || isNetworkRoot(e.Path);

            // ApplicationProvider の内容を追加する場合
            // ただしこのままでは ExplorerProvider が重複し、
            // また明らかに不適切な MoveItemsProvider やあまり使われないであろう BuiltInViewerProveder まで表示されるので
            // 有効にする場合 ExternalEpplicationProvider だけを表示するような変更を加えるのが望ましいと思われる
            if (this != ActiveForm) Activate();
            AllowFullpower = false; // これは cms 自体のものなので必要
        }

        private void zabLocation_DirectoryButtonContextMenuStripOpening_EditSmartFolder_Click(object sender, EventArgs e)
        {
            if (zabLocation_DirectoryButtonContextMenuStripOpening_Path != null)
            {
                openSmartFolderSettingForm(zabLocation_DirectoryButtonContextMenuStripOpening_Path); // AllowFullpower = true; 込み
            }
            else
            {
                AllowFullpower = true;
            }
        }

        private void zabLocation_DirectoryButtonContextMenuStripOpening_SelectTreeNode_Click(object sender, EventArgs e)
        {
            if (zabLocation_DirectoryButtonContextMenuStripOpening_Path != null)
            {
                setToTree(zabLocation_DirectoryButtonContextMenuStripOpening_Path, CollapseOthersWhenSynchronizing);
            }
        }

        // コンテキストメニューを閉じたら全力の処理を許す
        private void contextMenu_ClosedAllowFullpower(object sender, ToolStripDropDownClosedEventArgs e)
        {
            AllowFullpower = true;
        }

        private void mgFileList_MouseGestureStarting(MouseGesture sender, MouseGestureStartingEventArgs e)
        {
            var hit = dgvFileList.HitTest(e.X, e.Y);
            if (hit.Type == DataGridViewHitTestType.ColumnHeader)
            {
                e.Cancel = true;
            }
            else
            {
                if (e.Sender == MouseGestureSender.Mouse)
                {
                    if (AllowRightDragFromDetail)
                    {
                        if (hit.RowIndex >= 0)
                        {
                            if (dgvFileList.IsRowSelected(hit.RowIndex)) e.Cancel = true;
                        }
                    }
                }
            }
        }

        private void MgCatalog_MouseGestureStarting(MouseGesture sender, MouseGestureStartingEventArgs e)
        {
            if (e.Sender == MouseGestureSender.Mouse)
            {
                if (AllowRightDragFromThumbnail)
                {
                    var index = tvCatalog.PointToIndex(e.Location);
                    if (index >= 0 && tvCatalog[index].Selected)
                    {
                        e.Cancel = true;
                    }
                }
            }
        }

        private void mgCatalog_EnabledChanged(object sender, EventArgs e)
        {
            if (mgCatalog.Enabled)
            {
                tvCatalog.SubMouseButtons &= ~MouseButtons.Right;
            }
            else
            {
                tvCatalog.SubMouseButtons |= MouseButtons.Right;
            }
        }

        private void mgFileList_MouseGestureCompleted(MouseGesture sender, MouseGestureCompletedEventArgs e)
        {
            if (e.Sender != MouseGestureSender.Mouse) return; // 仮想キーも離さない
            var mk = ModifierKeys;
            if (e.PerformedActionIndex < 0)
            {
                var hit = dgvFileList.HitTest(e.X, e.Y);
                if (hit.RowIndex >= 0)
                {
                    var rect = dgvFileList.GetRowDisplayRectangle(hit.RowIndex, cutOverflow: true);
                    if (!rect.IsEmpty && e.MouseOrbit.All(p => rect.Contains(p)))
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
                            //cmsRightClickPrepareAndShow(Cursor.Position);
                            rightClickContextMenuPrepareAndShow(Cursor.Position, mk);
                        }
                        if (hit.RowIndex < 0 && hit.Type != DataGridViewHitTestType.ColumnHeader)
                        {
                            tvCatalog.SelectedIndices = null;
                        }
                    }
                }
                else
                {
                    var cl = dgvFileList.ClientRectangle;
                    if (e.MouseOrbit.All(p =>
                    {
                        if (!cl.Contains(p)) return false;
                        var h = dgvFileList.HitTest(p.X, p.Y);
                        return h.ColumnIndex < 0 && h.Type == DataGridViewHitTestType.None;
                    }))
                    {
                        //cmsRightClickPrepareAndShow(Cursor.Position);
                        tvCatalog.SelectedIndex = -1;
                        rightClickContextMenuPrepareAndShow(Cursor.Position, mk);
                    }
                }
            }
        }

        private void MgCatalog_MouseGestureCompleted(MouseGesture sender, MouseGestureCompletedEventArgs e)
        {
            if (e.Sender != MouseGestureSender.Mouse) return; // 仮想キーも離さない
            var mk = ModifierKeys;
            if (e.PerformedActionIndex < 0)
            {
                var rect = tvCatalog.PointToIndex(e.MouseOrbit[0]) < 0 ? Rectangle.Empty :
                    tvCatalog.PointToContainerGridRectangle(e.MouseOrbit[0], cutOverflow: true);
                if (!rect.IsEmpty && e.MouseOrbit.All(p => rect.Contains(p)))
                {
                    var index = tvCatalog.PointToIndex(e.MouseOrbit[0]);
                    if (index >= 0)
                    {
                        if (!tvCatalog.IsSelected(index)) tvCatalog.SelectedIndex = index;
                        //cmsRightClickPrepareAndShow(Cursor.Position);
                        rightClickContextMenuPrepareAndShow(Cursor.Position, mk);
                    }
                }
                else
                {
                    //var showCount = tvCatalog.DataIndexToShowIndex.Length;
                    var cl = tvCatalog.ClientRectangle;
                    if (e.MouseOrbit.All(p => cl.Contains(p) && tvCatalog.PointToIndex(p) < 0))
                    {
                        //cmsRightClickPrepareAndShow(Cursor.Position);
                        tvCatalog.SelectedIndex = -1;
                        rightClickContextMenuPrepareAndShow(Cursor.Position, mk);
                    }
                }
            }
        }

        private void DgvSorter_UserSorted(object sender, EventArgs e)
        {
            saveBookmarkToIni_bookmarkChanged = true;
            saveBookmarkToConfig();
        }

        private void zabLocation_TextChangedByButton(object sender, ZipPlaAddressBarTextChangedByButtonEventArgs e)
        {
            var text = zabLocation.Text;
            if (text == null) return;
            if (e.DropDown)
            {
                MakePreview();
            }
            else
            {
                goUpFromCurrentLocation(text);
            }
        }

        private void DgvFileList_MouseWheel(object sender, MouseEventArgs e)
        {
            if (MouseButtons == MouseButtons.Left)
            {
                var oldIndex = dgvFileList.FirstDisplayedScrollingRowIndex;
                var newIndex = Math.Max(0, Math.Min(dgvFileList.Rows.Count - 1, oldIndex - e.Delta / 120 * SystemInformation.MouseWheelScrollLines));
                if (oldIndex != newIndex)
                {
                    dgvFileList.FirstDisplayedScrollingRowIndex = newIndex;
                    var temp = Cursor.Position;
                    Cursor.Position = new Point(temp.X + 1, temp.Y);
                    Cursor.Position = temp;
                }
            }
        }

        class CharSelectRefiner : NativeWindow
        {
            private TextBoxBase TextBoxBase;

            private int lastDoubleClickTime;
            private Point? lastDoubleClickPoint = null;

            public Func<int, string, Tuple<int, int>> DoubleClickSelectionTextRangeProvider = null;

            public CharSelectRefiner(TextBoxBase textBox)
            {
                if (textBox == null) throw new ArgumentNullException(nameof(textBox));
                TextBoxBase = textBox;
                textBox.HandleCreated += TextBox_HandleCreated;
                textBox.HandleDestroyed += TextBox_HandleDestroyed;
                if (textBox.IsHandleCreated) AssignHandle();
            }

            private void AssignHandle()
            {
                AssignHandle(TextBoxBase.Handle);
            }

            private void TextBox_HandleCreated(object sender, EventArgs e)
            {
                AssignHandle();
            }

            private void TextBox_HandleDestroyed(object sender, EventArgs e)
            {
                ReleaseHandle();
            }

            protected override void WndProc(ref System.Windows.Forms.Message m)
            {
                const int WM_LBUTTONDBLCLK = 0x0203;
                const int WM_LBUTTONDOWN = 0x0201;

                if (m.Msg == WM_LBUTTONDBLCLK)
                {
                    var lParam = (int)m.LParam;
                    var xPos = lParam & 0xFFFF;         // カーソルx座標
                    var yPos = (lParam >> 16) & 0xFFFF;  // カーソルy座標

                    lastDoubleClickTime = Environment.TickCount;
                    lastDoubleClickPoint = new Point(xPos, yPos);

                    var clickedIndex = TextBoxBase.GetCharIndexFromPosition(new Point(xPos, yPos));

                    var range = DoubleClickSelectionTextRangeProvider == null ?
                        GetDoubleClickSelectionTextRange(clickedIndex, TextBoxBase.Text) :
                        DoubleClickSelectionTextRangeProvider(clickedIndex, TextBoxBase.Text);

                    if (range != null)
                    {
                        if (range.Item1 != TextBoxBase.SelectionStart || range.Item2 != TextBoxBase.SelectionLength)
                        {
                            TextBoxBase.Select(range.Item1, range.Item2);
                        }
                        return;
                    }
                }
                else if (m.Msg == WM_LBUTTONDOWN)
                {
                    if (lastDoubleClickPoint is Point p)
                    {
                        lastDoubleClickPoint = null;

                        var lParam = (int)m.LParam;
                        var xPos = lParam & 0xFFFF;         // カーソルx座標
                        var yPos = (lParam >> 16) & 0xFFFF;  // カーソルy座標

                        var size = SystemInformation.DoubleClickSize;
                        var w = size.Width;
                        var h = size.Height;
                        var rect = new Rectangle(p.X - w / 2, p.Y - h / 2, w | 1, h | 1);
                        if (rect.Contains(xPos, yPos))
                        {
                            var delta = Environment.TickCount - lastDoubleClickTime; // オーバーフローで剰余算を実現
                            if (0 <= delta && delta <= SystemInformation.DoubleClickTime)
                            {
                                TextBoxBase.SelectAll();
                                return;
                            }
                        }
                    }
                }

                base.WndProc(ref m);
            }

            private static Tuple<int, int> GetDoubleClickSelectionTextRange(int clickedIndex, string text)
            {
                if (text == null) return null;

                if (clickedIndex < 0) return null;

                var len = text.Length;

                if (clickedIndex > len) return null;

                if (clickedIndex == len) return Tuple.Create(clickedIndex, 0);

                var c = text[clickedIndex];

                var bracketedLength = GetBracketedLength(text, clickedIndex, reverse: false);
                if (bracketedLength > 0)
                {
                    return Tuple.Create(clickedIndex + 1, Math.Min(len - clickedIndex, bracketedLength - 2));
                }
                bracketedLength = GetBracketedLength(text, clickedIndex, reverse: true);
                if (bracketedLength > 0)
                {
                    var start = Math.Max(0, clickedIndex - (bracketedLength - 2));
                    return Tuple.Create(start, clickedIndex - start);
                }


                Tuple<int, int> result = null; ;

                bool test(Func<char, bool> tester)
                {
                    if (tester(c))
                    {
                        int start = clickedIndex, stop = clickedIndex + 1;
                        while (start > 0 && tester(text[start - 1])) start--;
                        while (stop < len && tester(text[stop])) stop++;
                        result = Tuple.Create(start, stop - start);
                        return true;
                    }
                    return false;
                }

                if (test(cc => char.IsLetterOrDigit(cc) || char.IsSymbol(cc))) return result;


                return Tuple.Create(clickedIndex, 0);
            }

            private static int GetBracketedLength(string text, int start, bool reverse)
            {
                if (start < 0 || text == null) return 0;
                var len = text.Length;
                if (start >= len) return 0;
                var delta = reverse ? -1 : 1;
                var stop = reverse ? -1 : len;
                char partner;
                if (reverse ? TryGetOpenBracket(text[start], out partner) : TryGetCloseBracket(text[start], out partner))
                {
                    for (var i = start + delta; i != stop; i += delta)
                    {
                        var c = text[i];
                        if (c == partner)
                        {
                            return (reverse ? start - i : i - start) + 1;
                        }
                        else
                        {
                            var subResult = GetBracketedLength(text, i, reverse);
                            if (subResult == int.MaxValue) return int.MaxValue;
                            if (subResult > 0) i += reverse ? 1 - subResult : subResult - 1;
                        }
                    }
                    return int.MaxValue;
                }
                else return 0;
            }

            private static readonly char[] brackets =
                (
                // http://xahlee.info/comp/unicode_matching_brackets.html
                "()[]{}“”‘’‹›«»（）［］｛｝｟｠⦅⦆〚〛⦃⦄「」〈〉《》【】〔〕⦗⦘『』〖〗〘〙｢｣⟦⟧⟨⟩⟪⟫⟮⟯⟬⟭⌈⌉⌊⌋⦇⦈⦉⦊❛❜❝❞❨❩❪❫❴❵❬❭❮❯❰❱❲❳﴾﴿〈〉⦑⦒⧼⧽﹙﹚﹛﹜﹝﹞⁽⁾₍₎⦋⦌⦍⦎⦏⦐⁅⁆⸢⸣⸤⸥⟅⟆⦓⦔⦕⦖⸦⸧⸨⸩⧘⧙⧚⧛⸜⸝⸌⸍⸂⸃⸄⸅⸉⸊᚛᚜༺༻༼༽" +
                // その他
                "" //"≪≫＜＞" これらは不等号
                ).ToCharArray();
            private static bool TryGetCloseBracket(char open, out char close)
            {
#if DEBUG
                if (brackets.Length % 2 != 0) throw new Exception($"{nameof(brackets)} has odd length.");
#endif
                for (var i = 0; i < brackets.Length; i += 2)
                {
                    if (open == brackets[i])
                    {
                        close = brackets[i + 1];
                        return true;
                    }
                }
                close = default(char);
                return false;
            }
            private static bool TryGetOpenBracket(char close, out char open)
            {
#if DEBUG
                if (brackets.Length % 2 != 0) throw new Exception($"{nameof(brackets)} has odd length.");
#endif
                for (var i = 1; i < brackets.Length; i += 2)
                {
                    if (close == brackets[i])
                    {
                        open = brackets[i - 1];
                        return true;
                    }
                }
                open = default(char);
                return false;
            }
        }

        MessageForwarder msDirectoryList, msCatalog, msFileList, msSortBy, msFilter;

        ActivateManager activateManager;

        ToolStripSpringTextBox selectedFileNameToolStripTextBox = new ToolStripSpringTextBox();

        ToolStripTextBox rightClickFileNameToolStripTextBox = new ToolStripTextBox();

        SimplifiedKeyBoard simplifiedKeyBoard;

        private bool IsLoaded = false;

        private void CatalogForm_Load(object sender, EventArgs e)
        {
#if RUNTIME
            Program.RunTimeMeasure?.Block("LoadEventHandlerStarted");
#endif

            selectedFileNameToolStripTextBox.ReadOnly = true;
            if (MenuTextFont != null)
            {
                selectedFileNameToolStripTextBox.Font = MenuTextFont;
            }
            statusStrip.Items.Insert(1, selectedFileNameToolStripTextBox);
            selectedFileNameToolStripTextBox.MouseEnter += SelectedFileNameToolStripTextBox_MouseEnter;
            selectedFileNameToolStripTextBox.MouseLeave += SelectedFileNameToolStripTextBox_MouseLeave;
            selectedFileNameToolStripTextBox.TextChanged += selectedFileNameToolStripTextBox_ShowRight;

#if RUNTIME
            Program.RunTimeMeasure?.Block("PrepareMessageForwarder");
#endif

            msDirectoryList = new MessageForwarder(dgvDirectoryList, ForwardedMessage.MouseWheel);
            new MessageForwarder(explorerTreeView.TreeViewWnd, ForwardedMessage.MouseWheel);
            msCatalog = new MessageForwarder(tvCatalog, ForwardedMessage.MouseWheel);
            msFileList = new MessageForwarder(dgvFileList, ForwardedMessage.MouseWheel);
            new MessageForwarder(cbLocation, ForwardedMessage.MouseWheel);
            msSortBy = new MessageForwarder(cbSortBy, ForwardedMessage.MouseWheel);
            msFilter = new MessageForwarder(cbFilter, ForwardedMessage.MouseWheel);

#if RUNTIME
            Program.RunTimeMeasure?.Block("PrepareSimplifiedKeyBoard");
#endif

            simplifiedKeyBoard = new SimplifiedKeyBoard(statusStrip, -1);
            simplifiedKeyBoard.Add("Ctrl", Keys.ControlKey);
            simplifiedKeyBoard.Add("Shift", Keys.ShiftKey);
            simplifiedKeyBoard.Add("Apps", cmsRightClickPrepareAndShowForKey);
            simplifiedKeyBoard.Add(Message.Tag, cmsRightClickOnlyTagPrepareAndShowForKey);
            simplifiedKeyBoard.Add("Del", Keys.Delete);
            tvCatalog.GetModifierKeys = simplifiedKeyBoard.GetModifierKeysWithoutUpHeldKeys;
#if RUNTIME
            Program.RunTimeMeasure?.Block("PrepareSimplifiedKeyBoard1");
#endif
            setTinyOnScreenKeyboardVisible(tinyOnScreenKeyboardVisible);
#if RUNTIME
            Program.RunTimeMeasure?.Block("PrepareSimplifiedKeyBoard");
#endif

            dgvFileList.SimplifiedKeyBoard = simplifiedKeyBoard;
            dgvDirectoryList.SimplifiedKeyBoard = simplifiedKeyBoard;

#if RUNTIME
            Program.RunTimeMeasure?.Block("SetRightClickFileNameEvents");
#endif

            rightClickFileNameToolStripTextBox.ReadOnly = true;
            cmsRightClick.Items.Insert(0, rightClickFileNameToolStripTextBox);
            //rightClickFileNameToolStripTextBox.SelectedTextChanged += rightClickFileNameToolStripTextBox_SelectedTextChanged;
            rightClickFileNameToolStripTextBox.MouseMove += RightClickFileNameToolStripTextBox_MouseMove;
            rightClickFileNameToolStripTextBox.MouseEnter += RightClickFileNameToolStripTextBox_MouseEnter;
            rightClickFileNameToolStripTextBox.MouseLeave += RightClickFileNameToolStripTextBox_MouseLeave;
            //rightClickFileNameToolStripTextBox.MouseDown += RightClickFileNameToolStripTextBox_MouseDown;
            new CharSelectRefiner(rightClickFileNameToolStripTextBox.TextBox);
            rightClickFileNameToolStripTextBox.KeyDown += RightClickFileNameToolStripTextBox_KeyDown;
            rightClickFileNameToolStripTextBox.KeyUp += RightClickFileNameToolStripTextBox_KeyUp;
            rightClickFileNameToolStripTextBox.TextChanged += RightClickFileNameToolStripTextBox_TextChanged;
            rightClickFileNameToolStripTextBox.GotFocus += rightClickFileNameToolStripTextBox_GotFocus;
            rightClickFileNameToolStripTextBox.LostFocus += rightClickFileNameToolStripTextBox_LostFocus;

#if RUNTIME
            Program.RunTimeMeasure?.Block("SquarizeToolStripInClass");
#endif

            ToolStripOverwriter.SquarizeToolStripInClass(this);

            CursorIsOnControllField = mouseOnWindow = Bounds.Contains(Cursor.Position);

#if RUNTIME
            Program.RunTimeMeasure?.Block("PrepareActivateManager");
#endif
            activateManager = new ActivateManager(this, tvCatalog, dgvFileList);

#if DEBUG
            //setColors();
#endif
            //setControlEvent(this);
#if RUNTIME
            Program.RunTimeMeasure?.Block("AfterLoad");
#endif
            IsLoaded = true;
        }

        private void rightClickFileNameToolStripTextBox_GotFocus(object sender, EventArgs e)
        {
            setRightClickMenuShortcutKeys(false);
        }

        private void rightClickFileNameToolStripTextBox_LostFocus(object sender, EventArgs e)
        {
            setRightClickMenuShortcutKeys(true);
        }

        private void setRightClickMenuShortcutKeys(bool enabled)
        {
            switch (enabled)
            {
                case true:
                    deleteonlyForListFileToolStripMenuItem.ShortcutKeys = Keys.Delete;
                    cutToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.X;
                    copyToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.C;
                    pasteToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.V;
                    break;
                case false:
                    {
                        deleteonlyForListFileToolStripMenuItem.ShortcutKeys = Keys.None;
                        cutToolStripMenuItem.ShortcutKeys = Keys.None;
                        copyToolStripMenuItem.ShortcutKeys = Keys.None;
                        pasteToolStripMenuItem.ShortcutKeys = Keys.None;
                    }
                    break;
            }
        }

        private void cmsRightClickPrepareAndShowForKey()
        {
            cmsRightClickPrepareAndShowForKey(ModifierKeys);
        }

        private void cmsRightClickPrepareAndShowForKey(Keys modifierKeys)
        {
            cmsRightClickPrepareAndShowForKey(onlyTag: false, modifierKeys: modifierKeys);
        }

        private void cmsRightClickOnlyTagPrepareAndShowForKey()
        {
            cmsRightClickOnlyTagPrepareAndShowForKey(ModifierKeys);
        }

        private void cmsRightClickOnlyTagPrepareAndShowForKey(Keys modifierKeys)
        {
            cmsRightClickPrepareAndShowForKey(onlyTag: true, modifierKeys: modifierKeys);
        }

        private void cmsRightClickPrepareAndShowForKey(bool onlyTag, Keys modifierKeys)
        {
            var lcc = LatestControledControl;
            if (lcc == tvCatalog)
            {
                cmsRightClickPrepareAndShowForKeyOnTvCatalog(onlyTag, modifierKeys);
            }
            else if (lcc == dgvFileList)
            {
                cmsRightClickPrepareAndShowForKeyOnFileList(onlyTag, modifierKeys);
            }
            else if (!onlyTag)
            {
                if (lcc == dgvDirectoryList)
                {
                    dgvDirectoryList_KeyDown(dgvDirectoryList, new KeyEventArgs(Keys.Apps));
                }
                else if (lcc == explorerTreeView.TreeViewWnd)
                {
                    cmsRightClickPrepareAndShowForKeyOnTreeView();
                }
            }

        }

        private void cmsRightClickPrepareAndShowForKeyOnTreeView()
        {
            var targetNode = explorerTreeView.SelectedNode;
            if (targetNode != null)
            {

                if (cmsRightClick.Visible)
                {
                    cmsRightClick.Close();
                }

                AllowFullpower = false;
                try
                {
                    var item = (ShellItem)targetNode.Tag;
                    var path = item.Path;
                    var bound = targetNode.Bounds;
                    var halfHeight = bound.Height / 2;
                    var point = new Point(bound.X + halfHeight, bound.Y + halfHeight);
                    explorerContextMenu.ShowContextMenu(new FileInfo[] { new FileInfo(path) }, explorerTreeView.PointToScreen(point));
                }
                catch { }
                finally
                {
                    AllowFullpower = true;
                }
            }
        }

        bool RightClickFileNameToolStripTextBox_TextChanged_SetDefaultValues;

        bool RightClickFileNameToolStripTextBox_TextChanged_DefaultRatingEnabled;

        bool RightClickFileNameToolStripTextBox_TextChanged_DefaultTagEnabled;

        bool RightClickFileNameToolStripTextBox_TextChanged_DefaultPageEnabled;

        bool RightClickFileNameToolStripTextBox_TextChanged_DefaultThumbEnabled;

        private bool RightClickFileNameToolStripTextBox_TextChanged_CheckFileNameChar = true;

        private void RightClickFileNameToolStripTextBox_TextChanged(object sender, EventArgs e)
        {
            var text = rightClickFileNameToolStripTextBox.Text;
            if (RightClickFileNameToolStripTextBox_TextChanged_CheckFileNameChar)
            {
                var start = rightClickFileNameToolStripTextBox.SelectionStart;
                var length = rightClickFileNameToolStripTextBox.SelectionLength;
                var sb = new StringBuilder(text.Length);
                var ifnc = Path.GetInvalidFileNameChars();
                var index = 0;
                foreach (var c in text)
                {
                    if (ifnc.Contains(c))
                    {
                        if (index < start)
                        {
                            start--;
                        }
                        else if (index < start + length)
                        {
                            length--;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                        index++;
                    }
                }
                var text2 = sb.ToString();
                if (text != text2)
                {
                    text = text2;
                    rightClickFileNameToolStripTextBox.Text = text2;
                    rightClickFileNameToolStripTextBox.Select(start, length);
                }
            }

            if (!RightClickFileNameToolStripTextBox_TextChanged_SetDefaultValues)
            {
                RightClickFileNameToolStripTextBox_TextChanged_DefaultRatingEnabled = ratingToolStripMenuItem.Enabled;
                RightClickFileNameToolStripTextBox_TextChanged_DefaultTagEnabled = tagToolStripMenuItem.Enabled;
                RightClickFileNameToolStripTextBox_TextChanged_DefaultPageEnabled = pageSequenceToolStripMenuItem.Enabled;
                RightClickFileNameToolStripTextBox_TextChanged_DefaultThumbEnabled = thumbnailToolStripMenuItem.Enabled;
                RightClickFileNameToolStripTextBox_TextChanged_SetDefaultValues = true;
            }

            var changed = cmsRightClickPrepareAndShow_InitialTextBoxText != text;
            if (changed)
            {
                ratingToolStripMenuItem.DropDown.Close();
                tagToolStripMenuItem.DropDown.Close();
                pageSequenceToolStripMenuItem.DropDown.Close();
                thumbnailToolStripMenuItem.DropDown.Close();

                ratingToolStripMenuItem.Enabled = false;
                tagToolStripMenuItem.Enabled = false;
                pageSequenceToolStripMenuItem.Enabled = false;
                thumbnailToolStripMenuItem.Enabled = false;
                SetAppInContextMenuEnabled(false);
            }
            else
            {
                ratingToolStripMenuItem.Enabled = RightClickFileNameToolStripTextBox_TextChanged_DefaultRatingEnabled;
                tagToolStripMenuItem.Enabled = RightClickFileNameToolStripTextBox_TextChanged_DefaultTagEnabled;
                pageSequenceToolStripMenuItem.Enabled = RightClickFileNameToolStripTextBox_TextChanged_DefaultPageEnabled;
                thumbnailToolStripMenuItem.Enabled = RightClickFileNameToolStripTextBox_TextChanged_DefaultThumbEnabled;
                SetAppInContextMenuEnabled(true);
            }

        }

        // 選択中のファイル名の欄は右端（ファイル名）が見えるようにする
        private void selectedFileNameToolStripTextBox_ShowRight(object sender, EventArgs e)
        {
            Program.TextBoxShowRight(selectedFileNameToolStripTextBox);
        }
    }
}
