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
        private int CenterPanellMinWidth;

        private int SidePanellMinWidth;

        private int pnlRight_NormalWidth;

        private void pnlRight_SizeChanged(object sender, EventArgs e)
        {
            var splDock = getFileListDock();
            var splVertical = !(splDock == DockStyle.Top || splDock == DockStyle.Bottom);
            if (pnlCenter.Visible && pnlRight.Visible)
            {
                pnlRight_NormalWidth = splVertical ? pnlRight.Width : pnlRight.Height;
                if (splVertical)
                {
                    splLeft.MinExtra = CenterPanellMinWidth + splRight.Width + pnlRight.Width;
                }
                else
                {
                    splLeft.MinExtra = CenterPanellMinWidth;
                }
            }
            else if (pnlCenter.Visible)
            {
                splLeft.MinExtra = CenterPanellMinWidth;
            }
            else
            {
                splLeft.MinExtra = splVertical ? SidePanellMinWidth : CenterPanellMinWidth;
            }
        }

        private void setFileListDock(DockStyle dockStyle, bool keepWidth, bool fixWidth)
        {
            var nextVertical = dockStyle == DockStyle.Left || dockStyle == DockStyle.Right;
            if (!(nextVertical ||
                dockStyle == DockStyle.Top || dockStyle == DockStyle.Bottom)) return;
            var splDock = getFileListDock();
            if (splDock != dockStyle)
            {
                var currentVertical = splDock == DockStyle.Left || splDock == DockStyle.Right;
                var dualVisible = pnlRight.Visible && pnlCenter.Visible;
                keepWidth = keepWidth && dualVisible;
                if (!keepWidth || currentVertical == nextVertical)
                {
                    splRight.Dock = dockStyle;
                    if (dualVisible) pnlRight.Dock = dockStyle;
                    if (fixWidth) setMainPanelWidth();
                }
                else
                {
                    var currentWidth = currentVertical ? pnlRight.Width : pnlRight.Height;
                    splRight.Dock = dockStyle;
                    if (dualVisible) pnlRight.Dock = dockStyle;
                    if (nextVertical) pnlRight.Width = currentWidth;
                    else pnlRight.Height = currentWidth;
                    if (fixWidth) setMainPanelWidth();
                }

                verticalToolStripMenuItem.Checked = dockStyle == DockStyle.Bottom;
                alternativeVerticalToolStripMenuItem.Checked = dockStyle == DockStyle.Top;
                horizontalToolStripMenuItem.Checked = dockStyle == DockStyle.Right;
                alternativeHorizontalToolStripMenuItem.Checked = dockStyle == DockStyle.Left;
            }
        }

        private void setFileListWidthWithoutSettingWidth(int width)
        {
            switch (getFileListDock())
            {
                case DockStyle.Left:
                case DockStyle.Right:
                    pnlRight.Width = width;
                    break;
                case DockStyle.Top:
                case DockStyle.Bottom:
                    pnlRight.Height = width;
                    break;
            }
        }

        private DockStyle getFileListDock()
        {
            return splRight.Dock; // pnlRight.Dock は非表示のときに期待通りの値を返さない
        }

        private int getFileListWidth()
        {
            switch (getFileListDock())
            {
                case DockStyle.Top:
                case DockStyle.Bottom:
                    return pnlRight.Height;
                default:
                    return pnlRight.Width;
            }
        }

        private void setMainPanelWidth()
        {
            var splDock = getFileListDock();
            var verticalSeparator = splDock == DockStyle.Left || splDock == DockStyle.Right;

            var t = verticalSeparator ? pnlMain.Width : pnlMain.Height;
            int wl, wr;
            if (verticalSeparator && pnlLeft.Visible)
            {
                wl = pnlLeft.Width;
                t -= splLeft.Width;
            }
            else
            {
                wl = 0;
            }
            var pnlCenterVisible = pnlCenter.Visible;
            if (pnlCenterVisible)
            {
                t -= CenterPanellMinWidth;
            }
            if (pnlRight.Visible)
            {
                wr = verticalSeparator ? pnlRight.Width : pnlRight.Height;
                if (pnlCenterVisible) t -= verticalSeparator ? splRight.Width : splRight.Height;
            }
            else
            {
                wr = 0;
            }
            var n = wl + wr - t;
            if (n > 0)
            {
                if (wr > SidePanellMinWidth)
                {
                    if (wr - SidePanellMinWidth >= n)
                    {
                        wr -= n;
                        n = 0;
                    }
                    else
                    {
                        n -= wr - SidePanellMinWidth;
                        wr = SidePanellMinWidth;
                    }
                }
                if (wl > SidePanellMinWidth)
                {
                    if (wl - SidePanellMinWidth >= n)
                    {
                        wl -= n;
                        n = 0;
                    }
                    else
                    {
                        n -= wl - SidePanellMinWidth;
                        wl = SidePanellMinWidth;
                    }
                }
                if (verticalSeparator)
                {
                    pnlLeft.Width = wl;
                    pnlRight.Width = wr;
                }
                else
                {
                    pnlLeft.Height = wl;
                    pnlRight.Height = wr;
                }
            }
        }

        private const int MaskAlpha = 160;

        private static Brush MaskBrush = new SolidBrush(Color.FromArgb(MaskAlpha, SystemColors.Window));

        public int GetMaskAlpha(string path)
        {
            if (cutFilePathSetInLower != null)
            {
                if (cutFilePathSetInLower.Contains(path.ToLower()))
                {
                    return MaskAlpha;
                }
            }
            return 0;
        }

        // 初期状態では辞書は Cover と 表紙 しか登録されないが、言語ファイルを追加すれば増えていく
        private readonly Dictionary<string, Size> tvCatalog_ThumbnailPaint_CoverStringWidthDictionary = new Dictionary<string, Size>();

        private void tvCatalog_ThumbnailPaint(ThumbViewer sender, ThumbViewer.ThumbnailPaintEventArgs e)
        {
            requestDeferredHeader(e.DataIndex); // 見えた項目の後回しにしたヘッダを取る（K33）
            var zpa = ZipPathArray;
            var zna = ZipNameArray;
            var lra = LoadResultArray;
            var mta = ModifiedTimeArray;
            var zga = ZipPageArray;
            var fca = FileCountArray;
            var count = zpa == null ? -1 : zpa.Length;
            if (zpa != null && zna != null && lra != null && mta != null && zga != null && fca != null &&
                count == sender.Count && zna.Length == count && lra.Length == count && mta.Length == count && zga.Length == count && fca.Length == count)
            {
                var name = zna[e.DataIndex];
                if (!string.IsNullOrEmpty(name)) // && cond != LoadResult.NotYet) ファイル名と統一
                {
                    var cond = lra[e.DataIndex];
                    var lastWriteTime = mta[e.DataIndex];

                    var path = zpa[e.DataIndex];
                    if (path == null) return;

                    var isDir = name.EndsWith(Path.DirectorySeparatorChar.ToString());

                    var iconSize = new Size(0, 0);
                    var IsVirtualPath = InMovie || InDocument || path.Contains(Path.AltDirectorySeparatorChar);
                    var thumbRect = e.ThumbnailRectangle;
                    if (LoadLastViewedPageFromHistory)
                    {
                        var totalPage = fca[e.DataIndex];
                        var currentPage = zga[e.DataIndex];
                        if (totalPage > 0 && currentPage >= 0 && currentPage < totalPage)
                        {
                            var dh = Program.DpiScalingY(3);
                            var w = ((2 * currentPage + 1) * thumbRect.Width - totalPage) / (2 * totalPage);
                            e.Graphics.FillRectangle(Brushes.Chartreuse, new Rectangle(thumbRect.X, thumbRect.Bottom - dh, w, dh));
                            e.Graphics.FillRectangle(Brushes.Black, new Rectangle(thumbRect.X + w, thumbRect.Bottom - dh, thumbRect.Width - w, dh));
                            thumbRect.Height -= dh;
                        }
                    }
                    if (cond == LoadResult.FileNotFound || cond == LoadResult.LoadError || cond == LoadResult.NotYet)
                    {
                        var str = showFilenameToolStripMenuItem.Checked ? null : name;
                        var iconPath = !IsVirtualPath ? path : null;
                        if (cond != LoadResult.LoadError)
                        {
                            //using (var b = GetFileDirectoryForeBrush(path))
                            var b = GetFileDirectoryForeBrush(path);
                            {
                                drawFileImage(e.Graphics, thumbRect, tvCatalog.Font, b/*Brushes.Black*/, str, iconPath, lastWriteTime, null, isDir, isExist: InDirectoryVirtualDirectoryOrSmartDirectory);
                            }
                        }
                        else
                        {
                            drawFileImage(e.Graphics, thumbRect, tvCatalog.Font, Brushes.Blue, str, iconPath, lastWriteTime, Brushes.Yellow, isDir, isExist: InDirectoryVirtualDirectoryOrSmartDirectory);
                        }
                    }
                    else
                    {
                        if (showIcon && !IsVirtualPath)
                            iconSize = drawFileIconImage(e.Graphics, thumbRect, path, lastWriteTime, isDir, isExist: InDirectoryVirtualDirectoryOrSmartDirectory);
                    }

                    var maskAlpha = GetMaskAlpha(path);
                    if (maskAlpha > 0)
                    {
                        e.Graphics.FillRectangle(MaskBrush, e.ThumbnailRectangle); // 元の Rectangle
                    }

                    var localShowRating = showRating && InDirectoryVirtualDirectoryOrSmartDirectory;
                    if (showTags || localShowRating)
                    {
                        var zipPlaInfo = new ZipPlaInfo(name);
                        if (showTags) drawTags(e.Graphics, thumbRect, zipPlaInfo, iconSize);
                        if (localShowRating) drawRating(e.Graphics, thumbRect, zipPlaInfo); // レートが手前
                    }
                    if ((InMovie || InArchive) && LocationZipPlaInfo != null)
                    {
                        if (
                            InMovie && getMoviePostionTuple(name)?.Equals(LocationZipPlaInfo.ThumbnailInfo) == true ||
                            InArchive && e.DataIndex == LocationZipPlaInfo.ThumbnailInfo?.Item1)
                        {
                            //e.Graphics.DrawString(Message.Cover, tvCatalog.Font, Brushes.Black, thumbRect);
                            var font = Program.StyledFont(tvCatalog.Font, FontStyle.Bold); // 描くたびに作らない（X19）
                            drawSimpleString(e.Graphics, Message.Cover, font, thumbRect, Color.Black, Brushes.Pink, tvCatalog_ThumbnailPaint_CoverStringWidthDictionary);
                        }
                    }

                }
            }
        }

        int tvCatalog_MouseMove_RatingChangePart_TempIndex = -1;

        Guid tvCatalog_MouseMove_RatingChangePart_ShownTooltipGuid = Guid.Empty;

        private void tvCatalog_MouseMove_RatingChangePart()//(int mouseDataIndex)
        {
            if (!showRating) return;
            //if (mgCatalog.InGesturing) return;
            if (!InDirectoryVirtualDirectoryOrSmartDirectory) return;
            if (CreatingZipPathArray) return;
            if (drawRating_Bound.IsEmpty) return;
            var clientCursorPosition = tvCatalog.PointToClient(Cursor.Position);
            var dataIndex = mgCatalog.InGesturing || cmsRightClick.Visible ? -1 : tvCatalog.PointToIndex(clientCursorPosition);
            //var dataIndex = mgCatalog.InGesturing ? -1 : mouseDataIndex;

            Graphics g = null;

            if (tvCatalog_MouseMove_RatingChangePart_TempIndex >= 0 && dataIndex != tvCatalog_MouseMove_RatingChangePart_TempIndex) // 先頭（0）の項目も戻す
            {
                if (tvCatalog_MouseMove_RatingChangePart_TempIndex < tvCatalog.Count)
                {
                    var zna = ZipNameArray;
                    if (zna != null && zna.Length == tvCatalog.Count)
                    {
                        var name = zna[tvCatalog_MouseMove_RatingChangePart_TempIndex];
                        if (!string.IsNullOrEmpty(name))
                        {
                            var rect = tvCatalog.IndexToContainerImageRectangle(tvCatalog_MouseMove_RatingChangePart_TempIndex);
                            if (!rect.IsEmpty)
                            {
                                var zipPlaInfo = new ZipPlaInfo(name);
                                g = tvCatalog.CreateGraphics();
                                drawRating(g, rect, zipPlaInfo);
                            }
                        }
                    }
                }
                tvCatalog_MouseMove_RatingChangePart_TempIndex = -1;
                //tvCatalog_MouseMove_RatingChangePart_MouseEnterTime = DateTime.MaxValue;
                toolTip.Hide(tvCatalog);
            }

            if (dataIndex < 0)
            {
                g?.Dispose();
                return;
            }

            var imageRectangle = tvCatalog.PointToContainerImageRectangle(clientCursorPosition);
            var backRectangle = drawRating_Bound;
            backRectangle.X += imageRectangle.X;
            backRectangle.Y += imageRectangle.Y;
            if (backRectangle.Contains(clientCursorPosition))
            {
                var r = Math.Max(1, Math.Min(5, 5 * (clientCursorPosition.X - (imageRectangle.X + drawRating_StarsBound.Left)) / drawRating_StarsBound.Width + 1));
                if (g == null) g = tvCatalog.CreateGraphics();
                drawRating(g, imageRectangle, r);
                if (tvCatalog_MouseMove_RatingChangePart_TempIndex != dataIndex)
                {
                    var offset = Cursor.Current is Cursor cursor ? cursor.Size.Height - cursor.HotSpot.Y : 0; // Run の中で取得すると Wait になってしまうことがある
                    tvCatalog_MouseMove_RatingChangePart_TempIndex = dataIndex;

                    toolTip.Hide(tvCatalog);
                    // 待つ間スレッドプールのスレッドを止めないよう Thread.Sleep ではなく Task.Delay で待つ（星の上を横切るとサムネイルの読み込みと同じプールのスレッドを数秒ずつ塞いでいた）
                    Task.Run(async () =>
                    {
                        await Task.Delay(toolTip.InitialDelay);
                        Point point = default(Point);
                        try
                        {
                            Invoke((MethodInvoker)(() =>
                            {
                                point = tvCatalog.PointToClient(Cursor.Position);
                            }));
                        }
                        catch (ObjectDisposedException) { }
                        if (backRectangle.Contains(point))
                        {
                            if (offset > 0)
                            {
                                point.Y += offset;
                            }
                            if (backRectangle.Right > point.X && backRectangle.Bottom > point.Y) point.Y = backRectangle.Bottom;
                            var guid = Guid.NewGuid();
                            try
                            {
                                Invoke((MethodInvoker)(() =>
                                {
                                    toolTip.Show(Message.MiddleClickLeftFlickToResetRating, tvCatalog, point);
                                    tvCatalog_MouseMove_RatingChangePart_ShownTooltipGuid = guid;
                                }));
                            }
                            catch (ObjectDisposedException) { }
                            await Task.Delay(toolTip.AutoPopDelay);
                            try
                            {
                                Invoke((MethodInvoker)(() =>
                                {
                                    if (guid == tvCatalog_MouseMove_RatingChangePart_ShownTooltipGuid)
                                    {
                                        toolTip.Hide(tvCatalog);
                                    }
                                }));
                            }
                            catch (ObjectDisposedException) { }
                        }
                    });

                }
            }
            else
            {
                if (dataIndex == tvCatalog_MouseMove_RatingChangePart_TempIndex)
                {
                    if (tvCatalog_MouseMove_RatingChangePart_TempIndex < tvCatalog.Count)
                    {
                        var zna = ZipNameArray;
                        if (zna != null && zna.Length == tvCatalog.Count)
                        {
                            var name = zna[tvCatalog_MouseMove_RatingChangePart_TempIndex];
                            if (!string.IsNullOrEmpty(name))
                            {
                                var zipPlaInfo = new ZipPlaInfo(name);
                                g = tvCatalog.CreateGraphics();
                                drawRating(g, imageRectangle, zipPlaInfo);
                            }
                        }
                    }
                    tvCatalog_MouseMove_RatingChangePart_TempIndex = -1;
                    //tvCatalog_MouseMove_RatingChangePart_MouseEnterTime = DateTime.MaxValue;
                    toolTip.Hide(tvCatalog);
                }
            }

            g?.Dispose();
        }

        private void tagToolStripMenuItem_Paint(object sender, PaintEventArgs e)
        {
            var rect = e.ClipRectangle;
            //var rect = (sender as ToolStripMenuItem).Bounds;
            rect.Width -= rect.Height * 3 / 2;// selectedFileNameToolStripTextBox.ContentRectangle.Width;// - cmsRightClick.Left;

            rect.Y -= 1; // Windows 10 64bit 2016/09 100%, 175%, 225% でドットレベルでの一致を確認

            string strLong;
            string strShort;
            if (MiddleClickContextMenu == CatalogFormContextMenu.Tag)
            {
                strLong = Message.MiddleClickTwoFingerTap;
                strShort = Message.MiddleClick;
            }
            else if (ControlRightClickContextMenu == CatalogFormContextMenu.Tag)
            {
                strLong = "Ctrl+" + Message.MiddleClick;
                strShort = null;
            }
            else if (RightClickContextMenu == CatalogFormContextMenu.Tag)
            {
                strLong = Message.RightClickPressAndTap;
                strShort = Message.RightClick;
            }
            else return;

            foreach (var str in new string[] { strLong, strShort })
            {
                if (str == null) break;
                if (rect.Width - rect.Height * 2 >= TextRenderer.MeasureText(Message.Tag + "  " + str,
                    tagToolStripMenuItem.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix).Width)
                {
                    TextRenderer.DrawText(e.Graphics, str, tagToolStripMenuItem.Font, rect, tagToolStripMenuItem.Enabled ? tagToolStripMenuItem.ForeColor : SystemColors.GrayText,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    break;
                }
            }

        }

        private void ToggleItemLoadLevelAndUpdatePreview(ref ItemLoadLevel itemLoadLevel, ToolStripMenuItem toolStripMenuItem)
        {
            var changed = SetItemLoadLevel(ref itemLoadLevel, toolStripMenuItem,
                itemLoadLevel == ItemLoadLevel.Full ? ItemLoadLevel.Header : itemLoadLevel == ItemLoadLevel.Header ? ItemLoadLevel.Ignore : ItemLoadLevel.Full, // エクスプローラーのプロパティと同じ順
                                                                                                                                                                //itemLoadLevel == ItemLoadLevel.Ignore ? ItemLoadLevel.Full : ItemLoadLevel.Ignore,
                checkAllLoadLevel: true);
            if (changed && InDirectoryVirtualDirectoryOrSmartDirectory) UpdatePreview();
        }

        private void archivesFilesPDFFielsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleItemLoadLevelAndUpdatePreview(ref archiveLoadLevel, archivesFilesPDFFielsToolStripMenuItem);

        }

        private void imageFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleItemLoadLevelAndUpdatePreview(ref imageLoadLevel, imageFilesToolStripMenuItem);
        }

        private void otherFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleItemLoadLevelAndUpdatePreview(ref otherLoadLevel, otherFilesToolStripMenuItem);
        }

        private void videoFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleItemLoadLevelAndUpdatePreview(ref videoLoadLevel, videoFilesToolStripMenuItem);
        }

        private void displayFoldersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            switch (SubfolderMode)
            {
                case SubfolderMode.Display: SubfolderMode = SubfolderMode.DisplayHeader; break;
                case SubfolderMode.DisplayHeader: SetSubfolderMode(SubfolderMode.Ignore, checkAllLoadLevel: true); break; //SubfolderMode = SubfolderMode.Ignore; break;
                case SubfolderMode.Ignore: SubfolderMode = SubfolderMode.Display; break;
                case SubfolderMode.DisplayAndSearch: SubfolderMode = SubfolderMode.DisplayHeaderAndSearch; break;
                case SubfolderMode.DisplayHeaderAndSearch: SetSubfolderMode(SubfolderMode.Search, checkAllLoadLevel: true); break; // SubfolderMode = SubfolderMode.Search; break;
                case SubfolderMode.Search: SubfolderMode = SubfolderMode.DisplayAndSearch; break;
            }
            if (InDirectoryVirtualDirectoryOrSmartDirectory) UpdatePreview();

            // Display(Header)AndSearch を禁止する実装
        }

        private void searchSubfoldersAsWellToolStripMenuItem_Click(object sender, EventArgs e)
        {
            switch (SubfolderMode)
            {
                case SubfolderMode.Display: SubfolderMode = SubfolderMode.DisplayAndSearch; break;
                case SubfolderMode.DisplayHeader: SubfolderMode = SubfolderMode.DisplayHeaderAndSearch; break;
                case SubfolderMode.Ignore: SubfolderMode = SubfolderMode.Search; break;
                case SubfolderMode.DisplayAndSearch: SubfolderMode = SubfolderMode.Display; break;
                case SubfolderMode.DisplayHeaderAndSearch: SubfolderMode = SubfolderMode.DisplayHeader; break;
                case SubfolderMode.Search: SubfolderMode = SubfolderMode.Ignore; break;
            }
            if (InDirectoryVirtualDirectoryOrSmartDirectory) UpdatePreview();

            // Display(Header)AndSearch を禁止する実装
        }

        private readonly Dictionary<string, Size> dgvDirectoryList_CellPainting_SizeDictionary = new Dictionary<string, Size>();

        private void dgvDirectoryList_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            var cell = dgvDirectoryList.Rows[e.RowIndex].Cells[e.ColumnIndex];
            var bookmark = cell.Value as ColoredBookmark;
            if (bookmark == null) return;

            var text = bookmark.SimpleBookmark?.GetDisplayString() ?? "";
#if SCREENCAPTCHA // スクリーンショット作成用
            if (Message.CurrentLanguage.ToString() == "en-US")
            {
                switch (text)
                {
                    case "スクリーンショット": text = "Screenshot(Jpn)"; break;
                    case "動画": text = "Movie"; break;
                    case "コメディ": text = "Comedy"; break;
                    case "ゲーム攻略": text = "Playing guide"; break;
                    case "参考書": text = "Textbook"; break;
                    case "仮想フォルダ1.sor": text = "VirtualFolder1.sor"; break;
                    case "仮想フォルダ2.sor": text = "VirtualFolder2.sor"; break;
                }
            }
#endif
            var font = e.CellStyle.Font;
            //var size = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            var size = MeasureStringSizeByTextRenderer(e.Graphics, text, font, dgvDirectoryList_CellPainting_SizeDictionary);

            var rect = e.CellBounds;
            rect.Y += (rect.Height - size.Height + 1) / 2;
            var backColor = bookmark.Color;

            Color foreColor;
            bool isSelected =
                   (e.State & DataGridViewElementStates.Selected) ==
                   DataGridViewElementStates.Selected;
            if (isSelected)
            {
                foreColor = e.CellStyle.SelectionForeColor;
                e.Paint(e.CellBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);


            }
            else
            {
                foreColor = ZipTag.GetForeColor(backColor);
                using (var b = new SolidBrush(backColor))
                {
                    e.Graphics.FillRectangle(b, e.CellBounds);
                }
            }

            var simpleBookmark = bookmark.SimpleBookmark;
            var role = simpleBookmark.SpecialRole;
            if (role == SimpleBookmarkSpecialRole.None)
            {
                TextRenderer.DrawText(e.Graphics, text, font, rect, foreColor, TextFormatFlags.EndEllipsis/* | TextFormatFlags.NoPrefix*/);
            }
            else if (role == SimpleBookmarkSpecialRole.Separator)
            {
                var margin = e.CellBounds.Height / 4;
                var vCenter = (e.CellBounds.Top + e.CellBounds.Bottom) / 2;

                e.Graphics.DrawLine(/*isSelected ? Pens.White : foreColor == Color.White ? Pens.White :*/isSelected ? Pens.Gray : Pens.LightGray,
                    e.ClipBounds.Left + margin, vCenter, e.ClipBounds.Right - margin, vCenter);

                //rect.X = Math.Max(rect.X, rect.X + (e.CellBounds.Width - rect.Width) / 2) - margin;
                //rect.Width = Math.Min(rect.Width + 2 * margin, e.CellBounds.Right - rect.Left);

                rect.X = e.CellBounds.X;
                rect.Width = e.CellBounds.Width;

                TextRenderer.DrawText(e.Graphics, text, font, rect, foreColor, isSelected ? e.CellStyle.SelectionBackColor : backColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter);
            }

            e.Handled = true;

        }

        //private ToolStripMenuItem[] setMenuItemEventHandler_menuItemArray = null;
        private void setMenuItemEventHandler()
        {
            var list = new List<ToolStripMenuItem>();
            foreach (var info in typeof(CatalogForm).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (info.FieldType == typeof(ToolStripMenuItem))
                {
                    var item = info.GetValue(this) as ToolStripMenuItem;
                    if (item?.Owner == menuStrip)
                    {
                        list.Add(item);
                    }
                }
            }
            //setMenuItemEventHandler_menuItemArray = list.ToArray();


            ForeColorManager.Set(blackToolStripMenuItem, grayToolStripMenuItem, selectColorToolStripMenuItem);
            ForeColorManager.Set(list);

            //ToolStripManager.Renderer = new ToolStripProfessionalRenderer(new SuitableForeColorProfessionalColorTable());
        }

        private void addToolStripMenuItem_Click(object sender, EventArgs e)
        {
            currentLocationToolStripMenuItem_Click(null, null);
        }

        private void addToolStripMenuItem_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                addToolStripMenuItem.DropDown = addToolStripMenuItemDropDown;
                addToolStripMenuItem_DropDownOpening(null, null);

                var bounds = addToolStripMenuItem.Bounds;

                addToolStripMenuItemDropDown.Show(menuStripLeft.PointToScreen(new Point(bounds.Left, bounds.Bottom - 1)));
            }
        }

        private void openbuiltinBookReaderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(Application.ExecutablePath, "-v");
        }

        private void removeCoverSettingFromCurrentLocation()
        {
            if (!InMovie && !InArchive) return; // 対応を増やす場合 StopReloadAndRenameCurrentLocation で先読みプロセスの処理をすること
            var cl = currentLocation;
            var info = new ZipPlaInfo(cl);
            info.ThumbnailInfo = null;
            var newPath = info.GetPathOfCurrentInfo(isDir: false); // InMovie または InArchive なら親は動画または圧縮ファイルでありフォルダではない
            //StopReloadAndRenameCurrentLocation(cl, info.GetPathOfCurrentInfo(), (success) =>
            StopReloadAndRenameCurrentLocation(cl, newPath, (success) =>
            {
                string preloc;
                int prelen;
                if (success && undoBufferIndex > 0 && newPath.StartsWith(preloc = undoBuffer[undoBufferIndex - 1].Location + Path.DirectorySeparatorChar) &&
                    // cl.Substring(prelen = preloc.Length).ToLower() == undoBuffer[undoBufferIndex - 1].SelectedFileName.ToLower())
                    newPath.Substring(prelen = preloc.Length).ToLower() == undoBuffer[undoBufferIndex - 1].SelectedFileName.ToLower())
                {
                    //currentLocation = newPath;
                    //undoBuffer[undoBufferIndex - 1].SelectedFileName = newPath.Substring(prelen);
                    UpdatePreview(renamedLocation: newPath);
                }
                else if (success)
                {
                    //UpdatePreview(renamedLocation: newPath);
                    changeCurrentAndGoUpFromCurrentLocation(newPath);
                }
                else
                {
                    UpdatePreview();
                }
            });
        }

        private void setCoverToThisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!InMovie) return;
            var index = tvCatalog.SelectedIndex;
            if (index < 0) return;
            var zna = ZipNameArray;
            if (zna == null || zna.Length <= index) return;
            var timeString = zna[index];

            var cl = currentLocation;
            var info = new ZipPlaInfo(cl);

            info.ThumbnailInfo = getMoviePostionTuple(timeString);

            var newPath = info.GetPathOfCurrentInfo(isDir: false); // InMovie なら親は動画ファイルでありフォルダではない
            string oldName;
            try { oldName = Path.GetFileName(cl); } catch { oldName = cl; }
            string preloc;
            int prelen;
            //StopReloadAndRenameCurrentLocation(cl, info.GetPathOfCurrentInfo(), (success) =>
            StopReloadAndRenameCurrentLocation(cl, newPath, (success) =>
            {
                if (success && undoBufferIndex > 0 && newPath.StartsWith(preloc = undoBuffer[undoBufferIndex - 1].Location + Path.DirectorySeparatorChar) &&
                    //cl.Substring(prelen = preloc.Length).ToLower() == undoBuffer[undoBufferIndex - 1].SelectedFileName.ToLower())
                    newPath.Substring(prelen = preloc.Length).ToLower() == undoBuffer[undoBufferIndex - 1].SelectedFileName.ToLower())
                {
                    //currentLocation = newPath;
                    //undoBuffer[undoBufferIndex - 1].SelectedFileName = newPath.Substring(prelen);
                    btnGoToBack.PerformClick();
                }
                else if (success)
                {
                    //UpdatePreview(renamedLocation: newPath);
                    changeCurrentAndGoUpFromCurrentLocation(newPath);
                }
                else
                {
                    UpdatePreview();
                }
            });
        }

        private Tuple<int, double> getMoviePostionTuple(string timeString)
        {
            TimeSpan time;
            try
            {
                time = TimeSpan.Parse(timeString);
            }
            catch
            {
                time = TimeSpan.Zero;
            }

            var ts = time.TotalSeconds;
            var its = (int)ts;
            var rem = ts - its;

            return Tuple.Create(its, rem);
        }

        private void StopReloadAndRenameCurrentLocation(string oldPath, string newPath, Action<bool> leftAction)
        {
            MakePreview_Que = false;
            bwMakePreviewPrepare.CancelAsync();

            BackgroundMultiWorker.AllRunWorkerCompletedEventHandler eh = null;

            var bwBusy = bmwMakePreview.IsBusy;

            eh = (sender, e) =>
            {
                if (oldPath == currentLocation)
                {
                    var success = false;
                    if (oldPath != newPath)
                    {

                        try
                        {
                            // Archive でも使うように変更する場合先読みプロセスに気をつけること
                            var stopLookAhead = PackedImageLoader.Supports(oldPath);
                            if (stopLookAhead)
                            {
                                StopLookAheadProcess();
                            }

                            //if (bwBusy)
                            else
                            {
                                try
                                {
                                    Program.FileOrDirectoryMove(oldPath, newPath);
                                }
                                catch (IOException)
                                {
                                    Thread.Sleep(14 * MovieThumbnailLoader.FfmpegCancelCheckInterval / 10);
                                    Program.FileOrDirectoryMove(oldPath, newPath);
                                }
                            }

                            if (stopLookAhead)
                            {
                                PrepareLookAheadProcess();
                            }


                            // swapPathAndNameInSorBookmarkUndoBuff が例外を投げるのは currentLocation が sor のときだけだが
                            // ここでその条件は満たされないのでこの try は将来の変更に備えたもの
                            try
                            {
                                swapPathAndNameInSorBookmarkUndoBuff(oldPath, newPath, needToSaveBookmark: false);
                                // CurrentLocation をリネームした後 makePreview を呼び出さない使い方は想定されないので needToSaveBookmark = false
                            }
                            catch { }
                            success = true;
                        }
                        catch (Exception error)
                        {
                            Task.Run(() => Invoke((MethodInvoker)(() => CloseVisibleContextMenuStrips())));
                            MessageBox.Show(this, error.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            Task.Run(() => Invoke((MethodInvoker)(() => tvCatalog.DoMouseUp())));
                        }
                    }

                    bmwMakePreview.AllRunWorkersCompleted -= eh;
                    leftAction(success);
                }
                else
                {
                    bmwMakePreview.AllRunWorkersCompleted -= eh;
                }
            };

            //if (bmwMakePreview.IsBusy)
            if (bwBusy)
            {
                MakePreview_Que = false;
                bmwMakePreview.AllRunWorkersCompleted += eh;
                bmwMakePreview.CancelAsync();
            }
            else
            {
                eh(null, null);
            }
        }

        // 監視のバッファがあふれた（イベントを取りこぼした）ら、少し待ってから一覧を読み直す（Y16）。
        // あふれるのは大量の変更の途中なので、続けて来ても変更が落ち着いてから 1 回だけ読み直す
        // フォルダの項目の表紙・件数のためにサブフォルダの中も見るのでイベントが多く、まとめてコピーしたときなどにあふれる（以前は知らせなしに取りこぼしていた）。
        // バッファ（InternalBufferSize）を 64KB に広げると、.NET Framework の監視の IO スレッドで ArgumentException が起きてプロセスが落ちたので、既定のまま
        private System.Windows.Forms.Timer fileSystemWatcherErrorTimer;

        private void ThumbnailSize_Click(ToolStripMenuItem toolStripMenuItem)
        {
            if (!toolStripMenuItem.Checked)
            {
                ChangeThumbnailSize(toolStripMenuItem);
                UpdatePreview();
            }
        }

        int ChangeThumbnailSize_Width = 210;

        int ChangeThumbnailSize_Height = 297;

        double ChangeThumbnailSize_Padding = 2;

        private void ChangeThumbnailSize(ToolStripMenuItem toolStripMenuItem)
        {
            var items = thumbnailSizeToolStripMenuItem.DropDownItems;

            //var isPlaClip = clipModeByControl == ClipMode.PlaClip;

            var inSizeBlock = false;
            foreach (object itemObject in items)
            {
                if (!inSizeBlock)
                {
                    inSizeBlock = itemObject == thumbnailSizeTopSeparator;
                    continue;
                }
                if (itemObject == thumbnailSizeBottomSeparator) break;
                if (itemObject.GetType() != typeof(ToolStripMenuItem)) continue;
                var item = (ToolStripMenuItem)itemObject;
                if (toolStripMenuItem != null)
                {
                    item.Checked = item == toolStripMenuItem;
                }
                else if (item.Checked)
                {
                    toolStripMenuItem = item;
                }
            }

        }

        private bool ThumbnailSettingToCatalog_CoverSettingModeAtOnce = false;

        private void ThumbnailSettingToCatalog()
        {
            int size;
            if (tinyToolStripMenuItem.Checked) size = -2;
            else if (smallToolStripMenuItem.Checked) size = -1;
            else if (largeToolStripMenuItem.Checked) size = 1;
            else if (hugeToolStripMenuItem.Checked) size = 2;
            else size = 0;


            double localAspectRatio = aspectRatio;
            if (ThumbnailSettingToCatalog_CoverSettingModeAtOnce)
            {
                ThumbnailSettingToCatalog_CoverSettingModeAtOnce = false;
            }
            else if (InMovie)
            {
                var mia = MovieInfoArray;
                if (mia != null && mia.Length > 0)
                {
                    var info = mia[0];
                    var movieSize = info.SizeByPixel;
                    var sar = info.SAR;
                    if (sar != 0) movieSize = sar * (Fraction)movieSize;
                    if (movieSize.Width > 0 && movieSize.Height > 0)
                    {
                        localAspectRatio = Math.Max(0.2, Math.Min(5.0, (double)movieSize.Width / movieSize.Height));
                    }
                }
            }

            var padding = size * 2; if (padding == 2) padding = 3; else if (padding < 2) padding = 2;
            var ratioSqrt = Math.Sqrt(localAspectRatio);
            var scale = Math.Pow(2, size / 2.0);
            ChangeThumbnailSize_Width = Program.DpiScalingX(ChangeThumbnailSize_NeutralLength * scale * ratioSqrt);
            ChangeThumbnailSize_Height = Program.DpiScalingX(ChangeThumbnailSize_NeutralLength * scale / ratioSqrt);
            ChangeThumbnailSize_Padding = padding;
        }

        private int GetThumbnailCountInTvCatalogOrFileList(MovieInfo info)
        {
            if (thumbnailListToolStripMenuItem.Checked)
            {
                int size;
                if (tinyToolStripMenuItem.Checked) size = -2;
                else if (smallToolStripMenuItem.Checked) size = -1;
                else if (largeToolStripMenuItem.Checked) size = 1;
                else if (hugeToolStripMenuItem.Checked) size = 2;
                else size = 0;

                double localAspectRatio = aspectRatio;
                if (!ThumbnailSettingToCatalog_CoverSettingModeAtOnce && info != null)
                {
                    var movieSize = info.SizeByPixel;
                    var sar = info.SAR;
                    if (sar != 0) movieSize = sar * (Fraction)movieSize;
                    if (movieSize.Width > 0 && movieSize.Height > 0)
                    {
                        localAspectRatio = Math.Max(0.2, Math.Min(5.0, (double)movieSize.Width / movieSize.Height));
                    }
                }

                var padding = size * 2; if (padding == 2) padding = 3; else if (padding < 2) padding = 2;
                var ratioSqrt = Math.Sqrt(localAspectRatio);
                var scale = Math.Pow(2, size / 2.0);
                var w = Program.DpiScalingX(ChangeThumbnailSize_NeutralLength * scale * ratioSqrt);
                var h = Program.DpiScalingX(ChangeThumbnailSize_NeutralLength * scale / ratioSqrt);
                padding = Program.DpiScaling(padding * currentMinimumFrameThickness);
                var ChangeThumbnailSize_Padding = padding;

                // スクロールバーは無視するべきなので tvCatalog.ClientSize は不適切
                int textLength, textLines;
                if (numberOfLinesOfName != NumberOfLinesOfName.Full)
                {
                    GetNameLengthAndNameLines(out textLength, out textLines);
                }
                else
                {
                    textLength = 0;
                    textLines = ThumbViewer.GetTextLines(default(TimeSpan).ToString(), new Size(w, h), padding, tvCatalog.bottomPadding, currentAligning, tvCatalog.Size);
                }
                return ThumbViewer.GetThumbnailCountInScreen(new Size(w, h), padding, tvCatalog.bottomPadding, tvCatalog.ShowText, tvCatalog.GetCharHeight(), textLength, textLines, currentAligning, tvCatalog.Size);
            }
            else if (fileListToolStripMenuItem.Checked)
            {
                return (dgvFileList.ClientRectangle.Bottom - dgvFileList.ColumnHeadersHeight) / GetCellDisplayHeight(dgvFileList);
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// DPI も考慮してセルの標準の高さを取得
        /// </summary>
        private static int GetCellDisplayHeight(DataGridView dataGridView)
        {
            var i = dataGridView.FirstDisplayedScrollingRowIndex;
            int result;
            if (i >= 0)
            {
                result = dataGridView.GetRowDisplayRectangle(i, false).Height;
            }
            else
            {
                result = dataGridView.ColumnHeadersHeight + 1;
                // 2016/12 Windows10 100%, 125%, 150%, 175%, 200%, 225% で完全一致を確認
            }
            return result > 0 ? result : dataGridView.RowTemplate.Height;
        }

        // サムネイルの大きさのメニュー項目
        private void thumbnailSizeMenuItem_Click(object sender, EventArgs e)
        {
            ThumbnailSize_Click((ToolStripMenuItem)sender);
        }

        private void showIconToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showIcon = showIconToolStripMenuItem.Checked = !showIcon;
            tvCatalog.Invalidate();
        }

        private void showRatingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showRating = showRatingToolStripMenuItem.Checked = !showRating;
            setTagfilterLocationIfRatingFilterVisibleChanged();
            //UpdatePreview();
            tvCatalog.Invalidate();
        }

        private void showTagsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showTags = showTagsToolStripMenuItem.Checked = !showTags;
            tvCatalog.Invalidate();
        }

        private void showFilenameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setShowFileName(!showFilenameToolStripMenuItem.Checked);
        }

        private void setShowFileName(bool show)
        {
            showFilenameToolStripMenuItem.Checked = show;
            tvCatalog.ShowText = show;
            theNumberOfLinesOfNameToolStripMenuItem.Enabled = show;
        }

        private void setEraserAndSearchIcon()
        {
            var iconSize = cmsRightClick.ImageScalingSize;
            SelectionToFilterToolStripMenuItem.Icon = getIcon(drawInvertSearchIcon, iconSize.Width, iconSize.Height);

            SelectionToClipBoardToolStripMenuItem.Icon = getIcon(drawClipboardIcon, iconSize.Width, iconSize.Height);

            btnLocationCommands.FlatStyle = FlatStyle.Flat;
            btnLocationCommands.FlatAppearance.BorderColor = BackColor;
            btnLocationCommands.Text = null;
            btnLocationCommands.Image = getIcon(drawEraserIcon, btnLocationCommands.ClientRectangle.Width, btnLocationCommands.ClientRectangle.Height);

            btnFilterCommands.FlatStyle = FlatStyle.Flat;
            btnFilterCommands.FlatAppearance.BorderColor = BackColor;
            btnFilterCommands.Text = null;
            //btnFilterCommands.Paint += btnFilterCommands_Paint;
            btnFilterCommands.Image = getIcon(drawInvertSearchIcon, btnFilterCommands.ClientRectangle.Width, btnFilterCommands.ClientRectangle.Height);

            //pbSearch.Image = getSearchIcon(pbSearch.ClientRectangle.Width, pbSearch.ClientRectangle.Height);
        }

        private Bitmap getIcon(Action<Graphics, RectangleF> drawer, int width, int height)
        {
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                drawer(g, new Rectangle(0, 0, width, height));
            }
            return result;
        }

        private void drawEraserIcon(Graphics g, RectangleF rect)
        {
            var x = rect.X;
            var y = rect.Y;
            var width = rect.Width;
            var height = rect.Height;

            const double upperTop = 0.15;
            const double upperBottom = 0.35;
            const double upperWidth = 0.45;

            const double lowerTop = 0.45;
            const double lowerBottom = 0.85;
            const double lowerWidth = 0.55;

            var ut = (int)Math.Round(y + lowerTop * height);
            var ub = (int)Math.Round(y + lowerBottom * height);
            var ul = (int)Math.Round(x + (1 - lowerWidth) * height / 2);
            var ur = (int)Math.Round(rect.Right - (ul - rect.Left));

            var lt = (int)Math.Round(y + upperTop * height);
            var lb = (int)Math.Round(y + upperBottom * height);
            var ll = (int)Math.Round(x + (1 - upperWidth) * height / 2);
            var lr = (int)Math.Round(rect.Right - (ll - rect.Left));

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.FillRectangle(Brushes.Black, ul - 1, ut - 1, ur - ul, ub - ut);
            g.FillRectangle(Brushes.Black, ll - 1, lt - 1, lr - ll, lb - lt);

        }

        private void drawInvertSearchIcon(Graphics g, RectangleF rect)
        {
            drawSearchIcon(g, new RectangleF(rect.Right, rect.Y, -rect.Width, rect.Height));
        }

        private void drawSearchIcon(Graphics g, RectangleF rect)
        {
            var x = rect.X;
            var y = rect.Y;
            var width = rect.Width;
            var height = rect.Height;

            const double thk = 0.15;

            const double rectX0 = 0.20;
            const double rectX1 = 0.65;

            const double lineX1 = 0.8;
            const double lineY1 = 0.8;

            const double rectY0 = rectX0;
            const double rectY1 = rectX1;
            const double lineX0 = (rectX0 + rectX1) / 2 + (rectX1 - rectX0) / (2 * 1.41421356237309504880168872421);
            const double lineY0 = lineX0;

            using (var p = new Pen(Color.Black, (float)((thk / 2) * (Math.Abs(width) + Math.Abs(height)))))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                g.DrawEllipse(p, new RectangleF((float)(x + rectX0 * width) - 1, (float)(y + rectY0 * height) - 1, (float)((rectX1 - rectX0) * width), (float)((rectY1 - rectY0) * height)));

                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawLine(p, new PointF((float)(x + lineX0 * width) - 1, (float)(y + lineY0 * height) - 1), new PointF((float)(x + lineX1 * width) - 1, (float)(y + lineY1 * height) - 1));
            }
        }

        private void drawClipboardIcon(Graphics g, RectangleF rect)
        {
            var x = rect.X;
            var y = rect.Y;
            var width = rect.Width;
            var height = rect.Height;

            const double thk = 0.1;
            const double thk2 = thk / 2;

            const double rectX = 0.20;
            const double rectY = 0.10;
            const double strnX = 0.35;
            const double strnY = 0.32;

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            using (var p = new Pen(Color.Black, (float)((thk / 2) * (Math.Abs(width) + Math.Abs(height)))))
            {
                g.DrawRectangle(p, (float)(x + rectX * width) - 1, (float)(y + rectY * height) - 1, (float)((1 - 2 * rectX) * width), (float)((1 - 2 * rectY) * height));
            }

            using (var p = new Pen(Color.Black, (float)((thk2 / 2) * (Math.Abs(width) + Math.Abs(height)))))
            {
                var px1 = (float)(x + strnX * width) - 1;
                var px2 = (float)(x + (1 - strnX) * width) - 1;
                float py;

                py = (float)(y + strnY * height) - 1;
                g.DrawLine(p, new PointF(px1, py), new PointF(px2, py));

                py = (float)(y + 0.5 * height) - 1;
                g.DrawLine(p, new PointF(px1, py), new PointF(px2, py));

                py = (float)(y + (1 - strnY) * height) - 1;
                g.DrawLine(p, new PointF(px1, py), new PointF(px2, py));
            }
        }

        private void tvCatalog_Paint(object sender, PaintEventArgs e)
        {
            if (showStartHelpMessage && tvCatalog.Visible)
            {
                drawInitialMessage(e, tvCatalog.ClientRectangle, tvCatalog.BackColor);
            }
        }

        Font drawInitialMessageFont = null;

        private Font GetDrawInitialMessageFont()
        {
            if (drawInitialMessageFont == null)
            {
                drawInitialMessageFont = new Font(DefaultFont.FontFamily, DefaultFont.SizeInPoints * 1.7f);
            }
            return drawInitialMessageFont;
        }

        private void drawInitialMessage(PaintEventArgs e, Rectangle rect, Color backColor)
        {
            // ファイルリストにも表示させる場合共通設定終了時に描画を追加すること
            var drawInitialMessageFont = GetDrawInitialMessageFont();

            if (dragAndDropAction == DragAndDropAction.ShowThumbnails)
            {
                // アンチエイリアスが効かない
                //TextRenderer.DrawText(e.Graphics, Message.FolderOpenDescription.Replace("\\n", "\n"), drawInitialMessageFont, rect, SystemColors.GrayText, backColor,
                //    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

                var preMode = e.Graphics.TextRenderingHint;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                var message = Message.FolderOpenDescription.Replace("\\n", "\n");
                var size = e.Graphics.MeasureString(message, drawInitialMessageFont, rect.Size);
                var rect2 = new RectangleF(rect.X + (rect.Width - size.Width) / 2, rect.Y + (rect.Height - size.Height) / 2, size.Width, size.Height);

                e.Graphics.DrawString(message, drawInitialMessageFont, SystemBrushes.GrayText, rect2);

                e.Graphics.TextRenderingHint = preMode;
            }
            else
            {

            }

        }
    }
}
