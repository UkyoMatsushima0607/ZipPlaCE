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
    public enum ThumbViewerAligningMode { TotallyUniformedGrid, HorizontallyUniformedGrid, LeftAlignedGrid, CenteredGrid }

    public enum ThumbViewerScrollUnit { AbsolutePixel, GridCeiling }

    public class ThumbViewer : ScrollableControl, IGraphicalList
    {

        readonly List<ThumbViewerItem> items = new List<ThumbViewerItem>(); // サムネイル保存用
        // 表示範囲から外れたサムネイルを残しておくキャッシュ（A3）
        internal readonly ThumbnailMemoryCache ThumbnailMemoryCache = new ThumbnailMemoryCache();
        /// <summary>サムネイルの見た目を決める設定。Clear の前に設定する。変わっていればメモリキャッシュを捨てる（K26）</summary>
        internal string ThumbnailAppearanceKey;

        public int MouseWheelScrollAmount = 120;
        public ThumbViewerScrollUnit MouseWheelScrollUnit = ThumbViewerScrollUnit.AbsolutePixel;

        public event EventHandler DisplayedChanged;
        public event EventHandler ShowIndexToDataIndexChanged;

        public enum DrawModeEnum { None = 0, User = 1, System, Full = 3 }
        public DrawModeEnum DrawMode = DrawModeEnum.Full;

        public int HorizontalLoopLevel = 1;

        private ThumbViewerAligningMode layoutMode = ThumbViewerAligningMode.TotallyUniformedGrid;
        public ThumbViewerAligningMode LayoutMode
        {
            get
            {
                return layoutMode;
            }
            set
            {
                if (layoutMode != value)
                {
                    layoutMode = value;
                    updateRowsCols();
                    Invalidate();
                }
            }
        }

        private byte unselectedItemAlphaChannel = 255;
        private Brush unselectedItemMaskBrush = null;
        public byte UnselectedItemAlphaChannel
        {
            get
            {
                return unselectedItemAlphaChannel;
            }
            set
            {
                if (unselectedItemAlphaChannel != value)
                {
                    unselectedItemAlphaChannel = value;
                    if (unselectedItemMaskBrush != null) unselectedItemMaskBrush.Dispose();
                    if (unselectedItemAlphaChannel == 255)
                    {
                        unselectedItemMaskBrush = null;
                    }
                    else
                    {
                        unselectedItemMaskBrush = new SolidBrush(Color.FromArgb(255 - unselectedItemAlphaChannel, BackColor));
                    }
                    Refresh();
                }
            }
        }

        Bitmap dummyImage = new Bitmap(1, 1);

        public int[] DataIndexToShowIndex
        {
            get
            {
                return dataIndexToShowIndex;
            }
        }

        public int[] ShowIndexToDataIndex
        {
            get
            {
                return showIndexToDataIndex;
            }
            set
            {
                var lenChanged = showIndexToDataIndex.Length != value.Length;
                showIndexToDataIndex = value;
                itemsVersion++;

                dataIndexToShowIndex = new int[items.Count];
                for (var i = 0; i < dataIndexToShowIndex.Length; i++)
                {
                    dataIndexToShowIndex[i] = -1;
                }
                var focusedLeft = false;
                for (var i = 0; i < showIndexToDataIndex.Length; i++)
                {
                    var dataIndex = showIndexToDataIndex[i];
                    dataIndexToShowIndex[dataIndex] = i;
                    if (dataIndex == focusedIndex) focusedLeft = true;
                }
                if (!focusedLeft) focusedIndex = -1;
                if (lenChanged) updateRowsCols();
                Invalidate();
                DisplayedChanged?.Invoke(this, null);

                var oldIndex = -2;

                for (var i = selectedIndices.Count - 1; i >= 0; i--)
                {
                    var index = selectedIndices[i];
                    if (index >= dataIndexToShowIndex.Length || dataIndexToShowIndex[index] < 0)
                    {
                        if (oldIndex == -2) oldIndex = SelectedIndex;
                        removeSelectionAt(i);
                    }
                }

                if (oldIndex >= 0)
                {
                    ClearAllSelection();
                    var se = new SelectedIndexChangedEventArgs(oldIndex, -1);
                    SelectedIndexChanged?.Invoke(this, se);
                }

                ShowIndexToDataIndexChanged?.Invoke(this, null);
            }

        }
        private int[] dataIndexToShowIndex = new int[0];
        private int[] showIndexToDataIndex = new int[0];

        public void ClearAllSelection()
        {
            if (selectedIndices != null)
            {
                foreach (var index in selectedIndices)
                {
                    var item = items[index];
                    if (item != null) item.selected = false;
                }
                selectedIndices.Clear();
                selectedSet.Clear();
            }
        }

        private void addSelection(int index) // 重複確認しない
        {
            if (selectedIndices != null)
            {
                selectedIndices.Add(index);
                selectedSet.Add(index);
                var item = items[index];
                if (item != null) item.selected = true;
            }
        }

        private void removeSelectionAt(int indexOnIndices)
        {
            if (selectedIndices != null)
            {
                var index = selectedIndices[indexOnIndices];
                selectedIndices.RemoveAt(indexOnIndices);
                selectedSet.Remove(index);
                var item = items[index];
                if (item != null) item.selected = false;
            }
        }

        // デザイナでエラーが出るのでここでは設定しない
        int topPadding; // Program.DpiScaling(2); // グリッド内でのサムネイルの余白
        int sidePadding; // Program.DpiScaling(2);
        /// <summary>
        /// プログラムの最初以外で変更する場合ファイル名に合わせた行数を求める処理で不整合が起こらないように注意する
        /// </summary>
        public int bottomPadding; // = Program.DpiScaling(2);
        public int ThumbnailPadding
        {
            set
            {
                if (topPadding != value || sidePadding != value)
                {
                    topPadding = sidePadding = value;
                    updateRowsCols();
                    Invalidate();
                }
            }
        }

        int thumbWidth; // 表示中のサムネイルのサイズ
        int thumbHeight; // 表示中のサムネイルのサイズ
        int textHeight; // 表示中のテキスト用に確保する高さ
        int cols; // グリッドの列数
        int rows; // グリッドの行数
        public int gridOffsetH { get; private set; } // グリッドのサイズ
        public int gridSizeH { get; private set; } // グリッドのサイズ
        public int gridSizeV { get; private set; } // グリッドのサイズ
        bool showText = true;
        //int selectedIndex = -1; // 現在選択されているサムネイル、複数選択時は負に
        private readonly List<int> selectedIndices = new List<int>();
        // selectedIndices と同じ内容の集合。選択されているかの判定を O(1) にする（K4）。selectedIndices を変えるときは必ず一緒に変える
        private readonly HashSet<int> selectedSet = new HashSet<int>();

        /// <summary>選択されているか（データのインデックス）。SelectedIndices の配列を作らずに判定する</summary>
        public bool IsSelected(int index) => selectedSet.Contains(index);

        // selectedIndices の中での位置。選択されていなければ線形探索せずに -1 を返す
        private int IndexOnSelection(int index) => selectedSet.Contains(index) ? selectedIndices.IndexOf(index) : -1;

        public Color FrameColor
        {
            get
            {
                return frameColor;
            }
            set
            {
                if (frameColor != value)
                {
                    frameColor = value;
                    frameBrush.Dispose();
                    frameBrush = new SolidBrush(frameColor);
                    selectedForeColor = ZipTag.GetForeColor(frameColor);
                }
            }
        }
        Color frameColor = Color.LightSalmon;
        Color selectedForeColor;
        Brush frameBrush; // 選択用のブラシ
        Brush backBrush;

        /// <summary>
        /// 必要なデザイナ変数です。
        /// </summary>
        private System.ComponentModel.Container components = null;

        public ThumbViewer()
        {

            // この呼び出しは、Windows.Forms フォーム デザイナで必要です。
            InitializeComponent();

            // TODO: InitComponent 呼び出しの後に初期化処理を追加してください。

            // ダブルバッファリングによる描画の高速化
            this.SetStyle(ControlStyles.DoubleBuffer, true);
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);

            this.BackColor = SystemColors.Window;

            frameBrush = new SolidBrush(frameColor);//new SolidBrush(Color.LightSalmon);
            backBrush = new SolidBrush(BackColor);
            selectedForeColor = ZipTag.GetForeColor(frameColor);

            // デフォルトのサムネイルのサイズを設定
            thumbWidth = thumbHeight = 64;

            // 他は CatalogForm が設定する
            //bottomPadding = Program.DpiScaling(2);

            gridSizeH = thumbWidth + 2 * sidePadding;

            //SetTextHeightFromSquareCharCount(16);
            SetTextHeight(refresh: false);

            // 選択されているサムネイルなし
            if (selectedIndices.Count > 0)
            {
                var se = new SelectedIndexChangedEventArgs(SelectedIndex, -1);
                ClearAllSelection();
                SelectedIndexChanged?.Invoke(this, se);
            }
            updateRowsCols();
        }

        /// <summary>
        /// 使用されているリソースに後処理を実行します。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                    components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region コンポーネント デザイナで生成されたコード 
        /// <summary>
        /// デザイナ サポートに必要なメソッドです。このメソッドの内容を 
        /// コード］エディタで変更しないでください。
        /// </summary>
        private void InitializeComponent()
        {
            // 
            // ThumbViewer
            // 
            this.Name = "ThumbViewer";
        }
        #endregion

        public double ScrollBarPercentage
        {
            get
            {
                return scrollBarPercentage;
            }
            set
            {
                if (value != ScrollBarPercentage)
                {
                    setScrollBarPercentage(value);
                }
            }
        }

        static double getOriginOffset(double height) => 0;

        double scrollBarPercentage;
        void setScrollBarPercentage(double v)
        {
            var height = Height;
            var top = VerticalScroll.Minimum;
            var bottom = VerticalScroll.Maximum;
            var fullHeight = bottom - top;
            var val = Math.Max(top, Math.Min(bottom - height, (v / 100 * fullHeight + top - getOriginOffset(height))));

            AutoScrollPosition = new Point(-AutoScrollPosition.X, (int)Math.Round(val)); // これで OnPaint → setScrollBarPercentageOnPaint が実行される
        }
        void setScrollBarPercentageOnPaint(double height, double top, double val, double fullHeight)
            => scrollBarPercentage = fullHeight > 0 ? 100 * (val - top + getOriginOffset(height)) / fullHeight : 0;

        public void ScrollBarToIndex(int dataIndex)
        {
            int showIndex = dataIndexToShowIndex[dataIndex];

            int val = Math.Max(VerticalScroll.Minimum, Math.Min(VerticalScroll.Maximum - Height, (showIndex / cols) * gridSizeV));
            AutoScrollPosition = new Point(-AutoScrollPosition.X, val);

            // ここで val を代入すると DisplayChanged が発動しないため代入してはならない。
            //OnPaint_VerticalScrollVal = val;
        }

        public void ScrollBarToIndexWithMinimalMove(int showIndex)
        {
            int val;

            int fullRowStart = (-this.AutoScrollPosition.Y + gridSizeV - 1) / gridSizeV;
            int fullStart = fullRowStart * cols;
            if (showIndex < fullStart)
            {
                val = Math.Max(VerticalScroll.Minimum, Math.Min(VerticalScroll.Maximum - Height, (showIndex / cols) * gridSizeV));
            }
            else
            {
                int fullRowEnd = (-this.AutoScrollPosition.Y + Height - gridSizeV + 1) / gridSizeV + 1;
                int fullStop = Math.Min(fullRowEnd * cols, showIndexToDataIndex.Length);
                if (showIndex >= fullStop)
                {
                    val = Math.Max(VerticalScroll.Minimum, Math.Min(VerticalScroll.Maximum - Height, (showIndex / cols) * gridSizeV - Height + gridSizeV - 1));
                }
                else
                {
                    return;
                }
            }

            AutoScrollPosition = new Point(-AutoScrollPosition.X, val);

            // ここで val を代入すると DisplayChanged が発動しないため代入してはならない。
            //OnPaint_VerticalScrollVal = val;
        }

        public void ItemBuffSizeChange(int newLength)
        {
            items.AddRange(new ThumbViewerItem[newLength - items.Count]);
            itemsVersion++;
            var newD2S = new int[newLength];
            Array.Copy(dataIndexToShowIndex, newD2S, dataIndexToShowIndex.Length);
            for (var i = dataIndexToShowIndex.Length; i < newLength; i++) newD2S[i] = -1;
            dataIndexToShowIndex = newD2S;
        }

        public int Count
        {
            get
            {
                return items.Count;
            }
        }

        public ThumbViewerItem this[int index]
        {
            get
            {
                return items[index];
            }
            set
            {
                if (index < items.Count)
                {
                    var item = items[index];
                    if (item != null)
                    {
                        // 項目の差し替え（ファイルの変更による再読み込みなど）なので、残しておいた古いサムネイルも捨てる（A3）
                        ThumbnailMemoryCache.Remove(item.FilePath);
                        item.Dispose();
                    }
                    if (value != null)
                    {
                        value.owner = this;
                        value.index = index;
                        value.selected = selectedSet.Contains(index);
                    }
                    items[index] = value;
                    itemsVersion++;
                    // 追加されたサムネイルを表示
                    using (Graphics g = this.CreateGraphics())
                    {
                        DrawItem(g, index, selectedSet.Contains(index));
                    }
                }
                else
                {
                    throw new IndexOutOfRangeException();
                }
            }
        }

        public void SilentSet(int index, ThumbViewerItem item)
        {
            if (index < items.Count)
            {
                var oldItem = items[index];
                if (oldItem != null)
                {
                    ThumbnailMemoryCache.Remove(oldItem.FilePath); // 差し替えなので古いサムネイルも捨てる（A3）
                    oldItem.Dispose();
                }
                if (item != null)
                {
                    item.owner = this;
                    item.index = index;
                }
                items[index] = item;
                itemsVersion++;
            }
            else
            {
#if DEBUG
                throw new IndexOutOfRangeException($"SilentSet({index}, itemIndex = {item?.Index}), itemsCount = {items.Count}");
#else
                throw new IndexOutOfRangeException();
#endif
            }
        }

        public class SelectedIndexChangedEventArgs
        {
            public int NewIndex { get; private set; }
            public int OldIndex { get; private set; }
            public SelectedIndexChangedEventArgs(int oldIndex, int newIndex)
            {
                NewIndex = newIndex;
                OldIndex = oldIndex;
            }
        }
        public delegate void SelectedIndexChangedEventHandler(object sender, SelectedIndexChangedEventArgs e);
        public event SelectedIndexChangedEventHandler SelectedIndexChanged;

        // 指定された点を左上にして画像を描画
        void drawImageUnscaled(Graphics g, Bitmap image, int x, int y)
        {
            g.DrawImageUnscaled(image, x, y);
        }

        // 指定された領域の中央に画像を描画

        public void DoMouseUp()
        {
            var pos = PointToClient(Cursor.Position);
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, pos.X, pos.Y, 0));
        }

        //public event PaintEventHandler PaintOne;
        public class ThumbnailPaintEventArgs : EventArgs { public Graphics Graphics; public int DataIndex; public Rectangle ThumbnailRectangle; }
        public delegate void ThumbnailPaintEventHandler(ThumbViewer sender, ThumbnailPaintEventArgs e);
        public event ThumbnailPaintEventHandler ThumbnailPaint;

        private Tuple<int, int> getFrameWidthPrimary()
        {
            var w = (gridSizeH - thumbWidthCurrent) / 2;
            return Tuple.Create(w, layoutMode == ThumbViewerAligningMode.TotallyUniformedGrid ? w : topPadding);
        }

        private Rectangle getThumbnailRectangle(int col, int row, int AutoScrollPositionY)
        {
            var frameWidthPrimary = getFrameWidthPrimary();
            int x = gridOffsetH + col * gridSizeH + frameWidthPrimary.Item1;
            int y = row * gridSizeV + frameWidthPrimary.Item2 + AutoScrollPositionY;
            int w = thumbWidthCurrent;
            int h = thumbHeight;
            return new Rectangle(x, y, w, h);
        }

        private Rectangle getGridRectangle(int col, int row, bool cutOverflow)
        {
            int x = gridOffsetH + col * gridSizeH;
            int y = row * gridSizeV + this.AutoScrollPosition.Y;
            int w = gridSizeH;
            int h = gridSizeV;
            var result = new Rectangle(x, y, w, h);
            if (cutOverflow)
            {
                result.Intersect(ClientRectangle);
            }
            return result;
        }

        public bool DrawStop = false;

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);

            if (BackColorBrush != null) BackColorBrush.Dispose();
            BackColorBrush = new SolidBrush(BackColor);
            preRenderScroll_AutoScrollPositionY = int.MinValue;
        }

        // 指定されたインデックス番号のサムネイルを表示
        private Bitmap DrawItem_canvas;
        private Bitmap DrawItem_slicedCanvas;
        private Brush BackColorBrush;
        public void DrawItem(int index, bool selected, bool onlyFrame = false, bool alreadyCheckedVisibleRegion = false, Rectangle clipRectangle = default(Rectangle), int AutoScrollPositionY = int.MinValue)
        {
#if PERFLOG
            var perfStart = Stopwatch.GetTimestamp();
            DrawItem(null, index, selected, onlyFrame, alreadyCheckedVisibleRegion, clipRectangle, AutoScrollPositionY);
            PerfTrace.AddDirectDraw(Stopwatch.GetTimestamp() - perfStart);
#else
            DrawItem(null, index, selected, onlyFrame, alreadyCheckedVisibleRegion, clipRectangle, AutoScrollPositionY);
#endif
        }
        public void DrawItem(Graphics g, int index, bool selected, bool onlyFrame = false, bool alreadyCheckedVisibleRegion = false, Rectangle clipRectangle = default(Rectangle), int AutoScrollPositionY = int.MinValue)
        {
            if (DrawStop) return;
            var showIndex = dataIndexToShowIndex[index];
            if (showIndex < 0) return;


            if (!alreadyCheckedVisibleRegion)
            {
                if (showIndex < DisplayedStartShowIndex || showIndex >= DisplayedStopShowIndex) return;
            }
            var item = items[index];
            Bitmap image = item?.Image;
            if (image == null) image = dummyImage;
            int col = showIndex % cols;
            int row = showIndex / cols;

            if (AutoScrollPositionY == int.MinValue) AutoScrollPositionY = AutoScrollPosition.Y;

            if (preRenderScroll_AutoScrollPositionY != int.MinValue)
            {
                var gRect = new Rectangle(col * gridSizeH, row * gridSizeV + AutoScrollPositionY, gridSizeH, gridSizeV);
                var abs1 = new Rectangle(preRenderScroll_ClipRectangle.X, preRenderScroll_ClipRectangle.Y - preRenderScroll_AutoScrollPositionY, preRenderScroll_ClipRectangle.Width, preRenderScroll_ClipRectangle.Height);
                var abs2 = new Rectangle(gRect.X, gRect.Y - AutoScrollPositionY, gRect.Width, gRect.Height);
                if (abs1.IntersectsWith(abs2))
                {
                    preRenderScroll_AutoScrollPositionY = int.MinValue;
                }
            }

            var tRect = getThumbnailRectangle(col, row, AutoScrollPositionY);

            // 枠以外で手動ダブルバッファリングする実装
            var gMustBeDisposed = false;
            if (!onlyFrame)
            {
                if (clipRectangle.IsEmpty)
                {
                    clipRectangle = ClientRectangle;
                }
                if (clipRectangle.Contains(tRect))
                {
                    if (DrawItem_canvas != null && !(DrawItem_canvas.Width == tRect.Width && DrawItem_canvas.Height == tRect.Height))
                    {
                        DrawItem_canvas.Dispose();
                        DrawItem_canvas = null;
                    }

                    if (DrawItem_canvas == null) DrawItem_canvas = new Bitmap(tRect.Width, tRect.Height);

                    using (var g2 = Graphics.FromImage(DrawItem_canvas))
                    {
                        g2.FillRectangle(BackColorBrush, 0, 0, tRect.Width, tRect.Height);

                        var tRect2 = new Rectangle(0, 0, tRect.Width, tRect.Height);

                        drawImageUnscaled(g2, image, tRect2.X, tRect2.Y);
                        if (ThumbnailPaint != null)
                        {
                            var e = new ThumbnailPaintEventArgs() { Graphics = g2, DataIndex = index, ThumbnailRectangle = tRect2 };
                            ThumbnailPaint(this, e);
                        }

                        if (!selected && unselectedItemMaskBrush != null)
                        {
                            g2.FillRectangle(unselectedItemMaskBrush, tRect2);
                        }
                    }
                    if (g == null)
                    {
                        gMustBeDisposed = true;
                        g = CreateGraphics();
                    }
                    g.DrawImageUnscaled(DrawItem_canvas, tRect.X, tRect.Y);
                }
                else
                {
                    var interSection = tRect;
                    interSection.Intersect(clipRectangle);
                    if (interSection.Width > 0 && interSection.Height > 0)
                    {
                        var dX = interSection.X - tRect.X;
                        var dY = interSection.Y - tRect.Y;

                        if (DrawItem_slicedCanvas != null && !(DrawItem_slicedCanvas.Width == interSection.Width && DrawItem_slicedCanvas.Height == interSection.Height))
                        {
                            DrawItem_slicedCanvas.Dispose();
                            DrawItem_slicedCanvas = null;
                        }

                        if (DrawItem_slicedCanvas == null) DrawItem_slicedCanvas = new Bitmap(interSection.Width, interSection.Height);

                        using (var g2 = Graphics.FromImage(DrawItem_slicedCanvas))
                        {
                            g2.FillRectangle(BackColorBrush, 0, 0, DrawItem_slicedCanvas.Width, DrawItem_slicedCanvas.Height);

                            var tRect2 = new Rectangle(-dX, -dY, tRect.Width, tRect.Height);

                            drawImageUnscaled(g2, image, tRect2.X, tRect2.Y);
                            if (ThumbnailPaint != null)
                            {
                                var e = new ThumbnailPaintEventArgs() { Graphics = g2, DataIndex = index, ThumbnailRectangle = tRect2 };
                                ThumbnailPaint(this, e);
                            }

                            if (!selected && unselectedItemMaskBrush != null)
                            {
                                g2.FillRectangle(unselectedItemMaskBrush, tRect2);
                            }

                        }

                        if (g == null)
                        {
                            gMustBeDisposed = true;
                            g = CreateGraphics();
                        }
                        g.DrawImageUnscaled(DrawItem_slicedCanvas, interSection.X, interSection.Y);
                    }
                }
            }
            if (g == null)
            {
                gMustBeDisposed = true;
                g = CreateGraphics();
            }
            drawFilledFrameGlobal(g, index, col, row, AutoScrollPositionY, tRect, selected, item?.Text);

            if (gMustBeDisposed) g.Dispose();

            // 枠ごと手動ダブルバッファリングする実装
            // 枠だけ描画する場合だけテキストを直接描画することになり
            // 外字のアンチエイリアスの一貫性が保たれなかったため廃止

        }

        // 指定されたインデックス番号の枠を表示／消去
        void drawFilledFrameGlobal(Graphics g, int index, int col, int row, int AutoScrollPositionY, Rectangle thumbRect, bool draw, string text = null)
        {
            drawFilledFrame(g, index, col, row, gridOffsetH + col * gridSizeH, row * gridSizeV + AutoScrollPositionY, thumbRect.X, thumbRect.Y, thumbRect.Width, thumbRect.Height, draw, text);
        }
        void drawFilledFrame(Graphics g, int index, int col, int row, Point gridLocation, Rectangle thumbRect, bool draw, string text = null)
        {
            drawFilledFrame(g, index, col, row, gridLocation.X, gridLocation.Y, thumbRect.X, thumbRect.Y, thumbRect.Width, thumbRect.Height, draw, text);
        }
        void drawFilledFrame(Graphics g, int index, int col, int row, int x0, int y0, int x, int y, int w, int h, bool draw, string text)
        {
            // x0 = col * gridSizeH;
            //y0 = row * gridSizeV + this.AutoScrollPosition.Y;
            var x1 = x0 + gridSizeH;
            var y1 = y0 + gridSizeV;

            var frameRectangles = new Rectangle[]
                {
                    new Rectangle(x0, y0, x - x0, y1 - y0),
                    new Rectangle(x + w, y0, x1 - x - w, y1 - y0),
                    new Rectangle(x, y0, w, y - y0),
                    new Rectangle(x, y + h, w, y1 - y - h)
                };

            // フレーム幅を固定しない方法
            if (draw)
            {
                Brush b = frameBrush;
                g.FillRectangles(b, frameRectangles);

                if (showText && text != null)
                {
                    var textRect = new Rectangle(x0 + bottomPadding, y + h + bottomPadding, gridSizeH - bottomPadding, y1 - (y + h) - bottomPadding); // 左も bottom でパディング、右はパディングしない
                    //TextRenderer.DrawText(g, text, Font, textRect, Color.Black, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    //g.DrawString(text, Font, Brushes.Black, textRect, new StringFormat { Trimming = StringTrimming.EllipsisCharacter });
                    wrapAndEndAllipsis(g, text, DefaultFont, textRect, selectedForeColor, FrameColor);
                }
            }
            else
            {
                Brush b = backBrush;
                g.FillRectangles(b, frameRectangles);


                if (showText && text != null)
                {
                    var textRect = new Rectangle(x0 + bottomPadding, y + h + bottomPadding, gridSizeH - bottomPadding, y1 - (y + h) - bottomPadding); // 左も bottom でパディング、右はパディングしない
                    //TextRenderer.DrawText(g, text, Font, textRect, Color.Black, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    //g.DrawString(text, Font, Brushes.Black, textRect, new StringFormat { Trimming = StringTrimming.EllipsisCharacter });
                    wrapAndEndAllipsis(g, text, DefaultFont, textRect, ForeColor, BackColor);
                }

                if (unselectedItemMaskBrush != null) g.FillRectangle(unselectedItemMaskBrush, new Rectangle(x0, y + h, gridSizeH, y1 - y - h));
            }


            // フレーム幅を固定する方法
            // ウインドウサイズのモジュロに依ってスキマができてしまう
        }

        private void wrapAndEndAllipsis(Graphics g, string text, Font font, Rectangle rect, Color foreColor, Color backColor)
        {
#if PERFLOG
            var perfT = PerfTrace.Now();
            try
            {
                wrapAndEndAllipsisCore(g, text, font, rect, foreColor, backColor);
            }
            finally
            {
                PerfTrace.AddStage("nameText", PerfTrace.Now() - perfT);
            }
        }

        private void wrapAndEndAllipsisCore(Graphics g, string text, Font font, Rectangle rect, Color foreColor, Color backColor)
        {
#endif
            foreach (var line in getNameLayout(text, font, rect.Width, rect.Height))
            {
                var r = new Rectangle(rect.X, rect.Y + line.OffsetY, rect.Width, rect.Height - line.OffsetY);
                TextRenderer.DrawText(g, line.Text, font, r, foreColor, backColor, line.Flags);
            }
        }

        // 名前の折り返しと省略の結果（K12）。以前は項目を描くたびに TextRenderer.MeasureText を十数回呼んでいた（長い名前で 1 項目 0.7〜0.9ms）
        private struct NameLayoutLine
        {
            public string Text;
            public int OffsetY;
            public TextFormatFlags Flags;
        }
        private readonly Dictionary<string, NameLayoutLine[]> nameLayoutCache = new Dictionary<string, NameLayoutLine[]>();
        private Font nameLayoutCacheFont;
        private int nameLayoutCacheWidth = -1, nameLayoutCacheHeight = -1;
        private const int NameLayoutCacheLimit = 20000;

        private NameLayoutLine[] getNameLayout(string text, Font font, int width, int height)
        {
            if (string.IsNullOrEmpty(text)) return new NameLayoutLine[0];
            lock (nameLayoutCache)
            {
                // 幅・高さ・フォントが変わったら全部作り直す（サムネイルの大きさや行数の設定を変えたとき）
                if (!ReferenceEquals(nameLayoutCacheFont, font) || nameLayoutCacheWidth != width || nameLayoutCacheHeight != height || nameLayoutCache.Count >= NameLayoutCacheLimit)
                {
                    nameLayoutCache.Clear();
                    nameLayoutCacheFont = font;
                    nameLayoutCacheWidth = width;
                    nameLayoutCacheHeight = height;
                }
                if (nameLayoutCache.TryGetValue(text, out var cached)) return cached;
            }
            var layout = makeNameLayout(text, font, width, height);
            lock (nameLayoutCache)
            {
                if (ReferenceEquals(nameLayoutCacheFont, font) && nameLayoutCacheWidth == width && nameLayoutCacheHeight == height) nameLayoutCache[text] = layout;
            }
            return layout;
        }

        // 以前の wrapAndEndAllipsis と同じ手順で、描く文字列・位置・書式だけを求める
        private NameLayoutLine[] makeNameLayout(string text, Font font, int width, int height)
        {
            var lines = new List<NameLayoutLine>();
            var offsetY = 0;
            var first = true;
            var lineCount = 0;
            while (!string.IsNullOrEmpty(text))
            {
                lineCount++;
                var len = widthToStringLength(text, font, width);
                if (first && len == text.Length)
                {
                    lines.Add(new NameLayoutLine { Text = text, OffsetY = offsetY, Flags = TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix });
                    break;
                }
                first = false;
                var current = text.Substring(0, len);
                var currentHeight = TextRenderer.MeasureText(current, font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPrefix).Height;
                if (currentHeight * 2 + bottomPadding <= height - offsetY)
                {
                    lines.Add(new NameLayoutLine { Text = current, OffsetY = offsetY, Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix });
                    text = text.Substring(len);
                    offsetY += currentHeight + bottomPadding;
                }
                else
                {
                    lines.Add(new NameLayoutLine { Text = GetTextBaseNameEndEllipsis(text, font, width, noPadding: true), OffsetY = offsetY,
                        Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | (lineCount == 1 ? TextFormatFlags.HorizontalCenter : 0) });
                    break;
                }
            }
            return lines.ToArray();
        }

        private int GetTextLines(string text)
        {
            // サムネイルサイズ設定後、updateRowsCols を呼び出す前に実行される可能性があるので
            // gridSizeH は自前で計算する必要あり

            // 現在の幅で決める方法
            // 最悪のケースを想定する方法
            var gridSizeH = thumbWidth + 0 * 2;

            return GetTextLines(text, gridSizeH - bottomPadding);
        }

        private int GetTextLines(string text, int gridSizeH)
        {
            return GetTextLines(text, DefaultFont, gridSizeH - bottomPadding);
        }
        public static int GetTextLines(string text, Size thumbSize, int padding, int bottomPadding, ThumbViewerAligningMode align, Size screenSize)
        {
            // サムネイルサイズ設定後、updateRowsCols を呼び出す前に実行される可能性があるので
            // gridSizeH は自前で計算する必要あり

            // 最悪のケースを想定する方法
            var gridSizeH = thumbSize.Width + 0 * 2;

            return GetTextLines(text, DefaultFont, gridSizeH - bottomPadding);
        }
        private static int GetTextLines(string text, Font font, int width)
        {
            var first = true;
            var lineCount = 0;
            while (!string.IsNullOrEmpty(text))
            {
                lineCount++;
                var len = widthToStringLength(text, font, width);
                if (first && len == text.Length) break;
                first = false;
                var current = text.Substring(0, len);
                var currentHeight = TextRenderer.MeasureText(current, font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPrefix).Height;
                text = text.Substring(len);
            }
            return lineCount;
        }

        public static string GetTextBaseNameEndEllipsis(string text, Font font, int width, bool noPadding)
        {
            var f = (noPadding ? TextFormatFlags.NoPadding : 0) | TextFormatFlags.NoPrefix;

            var size = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MinValue), f);
            if (size.Width <= width)
            {
                return text;
            }

            string bas = null;
            string ext = null;
            for (var i = text.Length - 1; i >= 0; i--)
            {
                var c = text[i];
                if (c == '\\' || c == '/')
                {
                    if (i == text.Length - 1)
                    {
                        bas = text.Substring(0, i);
                        ext = text.Substring(i);
                    }
                    break;
                }
                if (c == '.')
                {
                    if (i < text.Length - 1)
                    {
                        bas = text.Substring(0, i);
                        ext = text.Substring(i + 1);
                    }
                    break;
                }
            }
            if (ext == null)
            {
                var dotsWidth = TextRenderer.MeasureText("...", font, new Size(int.MaxValue, int.MinValue), f).Width;
                return text.Substring(0, widthToStringLength(text, font, width - dotsWidth)) + "...";
            }
            ext = "..." + ext;
            var sizeExt = TextRenderer.MeasureText(ext, font, new Size(int.MaxValue, int.MinValue), f);
            var lenB = widthToStringLength(text, font, width - sizeExt.Width);
            return bas.Substring(0, lenB) + ext;
        }

        private static int widthToStringLength(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return 0;
            var a = 0;
            var c = text.Length;
            var b = c + 1;
            var preWidth = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MinValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            while (a < b - 1)
            {
                c = c * width / preWidth;
                if (c <= a) c = a + 1; else if (c >= b) c = b - 1;
                preWidth = TextRenderer.MeasureText(text.Substring(0, c), font, new Size(int.MaxValue, int.MinValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                if (preWidth <= width)
                {
                    a = c;
                }
                else
                {
                    b = c;
                }
            }
            return a;
        }

        // グリッドの列数と行数を計算
        private int thumbWidthCurrent;
        public bool StopUpdateRowsCols = false;
        void updateRowsCols()
        {
            if (StopUpdateRowsCols) return;
            if (this.ClientSize.Width <= 0) // 最小化時など
            {
                return;
            }

            cols = this.ClientSize.Width / (thumbWidth + sidePadding * 2);
            if (cols == 0) cols = 1;

            var hUniformed = layoutMode == ThumbViewerAligningMode.TotallyUniformedGrid || layoutMode == ThumbViewerAligningMode.HorizontallyUniformedGrid;
            if (hUniformed)
            {
                gridSizeH = this.ClientSize.Width / cols;
            }
            else
            {
                gridSizeH = thumbWidth + sidePadding * 2;
            }

            rows = showIndexToDataIndex.Length / cols;
            if (showIndexToDataIndex.Length % cols > 0)
                rows++;

            if (thumbWidth > gridSizeH - sidePadding * 2)
            {
                thumbWidthCurrent = gridSizeH - sidePadding * 2;
            }
            else
            {
                thumbWidthCurrent = thumbWidth;
            }

            var frameWidth2 = hUniformed ? gridSizeH - thumbWidthCurrent : sidePadding * 2;

            thumbWidthCurrent = thumbWidth;

            //SetTextHeightFromSquareCharCount(16);
            SetTextHeight(refresh: false);

            if (layoutMode == ThumbViewerAligningMode.TotallyUniformedGrid)
            {
                if (showText)
                {
                    gridSizeV = frameWidth2 / 2 + Math.Max(textHeight + 2 * bottomPadding, (frameWidth2 + 1) / 2) + thumbHeight; // bottom が 2 つ

                }
                else
                {
                    gridSizeV = frameWidth2 + thumbHeight;
                }
            }
            else
            {
                if (showText)
                {
                    gridSizeV = topPadding + Math.Max(textHeight + 2 * bottomPadding, topPadding) + thumbHeight; // bottom が 2 つ

                }
                else
                {
                    gridSizeV = 2 * topPadding + thumbHeight;
                }
            }

            gridOffsetH = layoutMode == ThumbViewerAligningMode.LeftAlignedGrid ? 0 : (ClientSize.Width - cols * gridSizeH) / 2;

            // サムネイルの表示領域を設定
            AutoScrollMinSize = new Size(0, rows * gridSizeV);
        }

        public static int GetThumbnailCountInScreen(Size thumbSize, int padding, int bottomPadding, bool showText, int charHeight, int charCount, int textLines, ThumbViewerAligningMode align, Size screenSize)
        {
            if (screenSize.Width <= 0) // 最小化時など
            {
                return 0;
            }

            var cols = screenSize.Width / (thumbSize.Width + padding * 2);
            if (cols == 0) cols = 1;

            int gridSizeH;
            var hUniformed = align == ThumbViewerAligningMode.TotallyUniformedGrid || align == ThumbViewerAligningMode.HorizontallyUniformedGrid;
            if (hUniformed)
            {
                gridSizeH = screenSize.Width / cols;
            }
            else
            {
                gridSizeH = thumbSize.Width + padding * 2;
            }


            int thumbWidthCurrent;
            if (thumbSize.Width > gridSizeH - padding * 2)
            {
                thumbWidthCurrent = gridSizeH - padding * 2;
            }
            else
            {
                thumbWidthCurrent = thumbSize.Width;
            }

            var frameWidth2 = hUniformed ? gridSizeH - thumbWidthCurrent : padding * 2;

            thumbWidthCurrent = thumbSize.Width;

            //SetTextHeightFromSquareCharCount(16);


            var w = gridSizeH - padding * 2;
            //const int charCount = 16;
            int textHeight;
            if (charCount > 0)
            {
                textHeight = (charCount * charHeight + w - 1) / w * (charHeight + bottomPadding) - bottomPadding; // top は使わない
            }
            else
            {
                textHeight = textLines * (charHeight + bottomPadding) - bottomPadding; // top は使わない
            }
            int gridSizeV;
            if (align == ThumbViewerAligningMode.TotallyUniformedGrid)
            {
                if (showText)
                {
                    gridSizeV = frameWidth2 / 2 + Math.Max(textHeight + 2 * bottomPadding, (frameWidth2 + 1) / 2) + thumbSize.Height; // bottom が 2 つ

                }
                else
                {
                    gridSizeV = frameWidth2 + thumbSize.Height;
                }
            }
            else
            {
                if (showText)
                {
                    gridSizeV = padding + Math.Max(textHeight + 2 * bottomPadding, padding) + thumbSize.Height; // bottom が 2 つ

                }
                else
                {
                    gridSizeV = 2 * padding + thumbSize.Height;
                }
            }

            return screenSize.Height / gridSizeV * cols;
        }


        // 現在選択されているデータインデックス番号の取得（未選択時：-1）
        // set 時は範囲外のものを代入しようとするとエラーとはせず -1 を入れたものとみなされる
        public int SelectedIndex
        {
            get { return selectedIndices.Count == 1 ? selectedIndices[0] : -1; }
            set
            {
                var newSelected = value;

                //var drawCount = selectedIndices.Count;

                // サムネイルの存在しない場所がクリックされた場合
                if (newSelected < 0 || newSelected > items.Count - 1)
                {
                    if (selectedIndices.Any())
                    {
                        //if (drawCount == 1)
                        {
                            using (Graphics g = this.CreateGraphics())
                            {
                                foreach (var selected in selectedIndices)
                                {
                                    if (selected >= 0 && dataIndexToShowIndex != null && selected < dataIndexToShowIndex.Length)
                                    {
                                        DrawItem(g, selected, selected: false, onlyFrame: true);

                                    }
                                }
                            }
                        }

                        var se = new SelectedIndexChangedEventArgs(SelectedIndex, -1);
                        ClearAllSelection();

                        //if (drawCount > 1) Invalidate();

                        SelectedIndexChanged?.Invoke(this, se);
                    }

                    return;
                }

                // 選択されているサムネイルを変更
                if (newSelected != SelectedIndex)
                {
                    //drawCount++;
                    //if (drawCount == 1)
                    {
                        using (Graphics g = CreateGraphics())
                        {
                            var needDrawSelection = true;
                            foreach (var selectedIndex in selectedIndices)
                            {
                                if (selectedIndex == newSelected)
                                {
                                    needDrawSelection = false;
                                }
                                else
                                {
                                    DrawItem(g, selectedIndex, selected: false, onlyFrame: true); // 枠のみ描画
                                }

                            }
                            if (needDrawSelection) DrawItem(g, newSelected, selected: true, onlyFrame: true); // 枠のみ描画
                        }
                    }


                    var se = new SelectedIndexChangedEventArgs(SelectedIndex, newSelected);
                    ClearAllSelection();
                    addSelection(newSelected);


                    SelectedIndexChanged?.Invoke(this, se);
                }
            }
        }

        public int FirstDisplayedSelectedShowIndex
        {
            get
            {
                if (selectedIndices.Count == 0) return -1;
                var topShowIndex = showIndexToDataIndex.Length;
                var displayedStartShowIndex = DisplayedStartShowIndex;
                var displayedStopShowIndex = DisplayedStopShowIndex;
                foreach (var index in selectedIndices)
                {
                    var showIndex = dataIndexToShowIndex[index];
                    if (showIndex < topShowIndex && displayedStartShowIndex <= showIndex && showIndex < displayedStopShowIndex)
                    {
                        topShowIndex = showIndex;
                    }
                }
                if (topShowIndex == showIndexToDataIndex.Length) return -1;
                return topShowIndex;
            }
        }

        public int SelectedIndicesCount
        {
            get
            {
                return selectedIndices.Count;
            }
        }

        public int[] SelectedIndices
        {
            get
            {
                return selectedIndices.ToArray();
            }
            set
            {
                if (value == null) value = new int[0];
                var oldSelectedIndex = SelectedIndex;
                //var selectedToNoselected = (from s in selectedIndices where !value.Contains(s) select s).ToArray();
                //var noselectedToSelected = (from s in value where !selectedIndices.Contains(s) select s).ToArray();
                GetChangeSelect(out var selectedToNoselected, out var noselectedToSelected, selectedIndices, value);
                var changed = selectedToNoselected.Any() || noselectedToSelected.Any();
                if (changed)
                {
                    changed = false;
                    using (Graphics g = this.CreateGraphics())
                    {
                        foreach (var index in selectedToNoselected)
                        {
                            if (setSelectedWithoutEvent(index, false, IndexOnSelection(index), g)) changed = true;
                        }
                        foreach (var index in noselectedToSelected)
                        {
                            if (setSelectedWithoutEvent(index, true, IndexOnSelection(index), g)) changed = true;
                        }
                    }
                }
                //if (length > 1) Invalidate();
                if (changed)
                {
                    var se = new SelectedIndexChangedEventArgs(oldSelectedIndex, SelectedIndex);
                    SelectedIndexChanged?.Invoke(this, se);
                }
            }
        }

        bool[] GetChangeSelect_Table = null;
        private void GetChangeSelect(out int[] selectedToNoselected, out int[] noselectedToSelected, IEnumerable<int> oldSelected, int[] newSelected, bool firstOutputRequired = true)
        {
            var tableSize = items.Count;
            {
                if (GetChangeSelect_Table == null || GetChangeSelect_Table.Length < tableSize)
                {
                    GetChangeSelect_Table = new bool[tableSize];
                }
                if (firstOutputRequired)
                {
                    foreach (var s in newSelected) GetChangeSelect_Table[s] = true;
                    selectedToNoselected = (from s in oldSelected where !GetChangeSelect_Table[s] select s).ToArray();
                    foreach (var s in newSelected) GetChangeSelect_Table[s] = false;
                }
                else
                {
                    selectedToNoselected = null;
                }

                foreach (var s in oldSelected) GetChangeSelect_Table[s] = true;
                noselectedToSelected = (from s in newSelected where !GetChangeSelect_Table[s] select s).ToArray();
                foreach (var s in oldSelected) GetChangeSelect_Table[s] = false;
            }
        }

        public bool setSelected(int index, bool selected, int indexOnIndices, Graphics graphics)
        {
            var oldSelectedIndex = SelectedIndex;
            if (setSelectedWithoutEvent(index, selected, indexOnIndices, graphics))
            {
                var se = new SelectedIndexChangedEventArgs(oldSelectedIndex, SelectedIndex);
                SelectedIndexChanged?.Invoke(this, se);
                return true;
            }
            return false;
        }

        private bool setSelectedWithoutEvent(int index, bool selected, int indexOnIndices, Graphics graphics)
        {
            if (index < 0 || index > items.Count - 1) return false;
            var oldSelected = indexOnIndices >= 0;
            if (oldSelected && !selected)
            {

                var showIndex = dataIndexToShowIndex[index];
                if (showIndex >= 0)
                {
                    if (graphics == null)
                    {
                        using (Graphics g = this.CreateGraphics())
                        {
                            DrawItem(g, index, selected: false, onlyFrame: true);
                        }
                    }
                    else
                    {
                        DrawItem(graphics, index, selected: false, onlyFrame: true);
                    }
                }


                removeSelectionAt(indexOnIndices);

                return true;
            }
            else if (!oldSelected && selected)
            {
                if (graphics == null)
                {
                    using (Graphics g = this.CreateGraphics())
                    {
                        DrawItem(g, index, selected: true, onlyFrame: true); // 枠のみ描画
                    }
                }
                else
                {
                    DrawItem(graphics, index, selected: true, onlyFrame: true); // 枠のみ描画
                }

                addSelection(index);

                return true;
            }
            else return false;
        }

        public int PointToIndex(Point clientPoint)
        {
            var showIndex = PointToShowIndex(clientPoint, getNearestIndex: false);
            return showIndex >= 0 ? showIndexToDataIndex[showIndex] : -1;
        }

        public Rectangle PointToContainerGridRectangle(Point clientPoint, bool cutOverflow)
        {
            if (!ClientRectangle.Contains(clientPoint)) return Rectangle.Empty;
            int col2 = Math.Max(0, Math.Min(cols - 1, (clientPoint.X - gridOffsetH) / gridSizeH));
            int row2 = (clientPoint.Y - AutoScrollPosition.Y) / gridSizeV;
            return getGridRectangle(col2, row2, cutOverflow);
        }

        public Rectangle PointToContainerImageRectangle(Point clientPoint)
        {
            if (!ClientRectangle.Contains(clientPoint)) return Rectangle.Empty;
            int col2 = Math.Max(0, Math.Min(cols - 1, (clientPoint.X - gridOffsetH) / gridSizeH));
            int row2 = (clientPoint.Y - AutoScrollPosition.Y) / gridSizeV;
            return getThumbnailRectangle(col2, row2, AutoScrollPosition.Y);
        }

        public Rectangle IndexToContainerImageRectangle(int dataIndex)
        {
            if (dataIndex >= dataIndexToShowIndex.Length) return Rectangle.Empty;
            var showIndex = dataIndexToShowIndex[dataIndex];
            int col2 = showIndex % cols;
            int row2 = showIndex / cols;
            return getThumbnailRectangle(col2, row2, AutoScrollPosition.Y);
        }

        private int PointToShowIndex(Point clientPoint, bool getNearestIndex)
        {
            if (!getNearestIndex && !ClientRectangle.Contains(clientPoint)) return -1;

            var slen = showIndexToDataIndex.Length;
            if (slen <= 0) return -1;
            var rows = (slen - 1) / cols + 1;

            int colNum = clientPoint.X - gridOffsetH;
            if (colNum < 0)
            {
                if (getNearestIndex)
                {
                    colNum = 0;
                }
                else
                {
                    return -1;
                }
            }
            int col = colNum / gridSizeH;
            if (col >= cols)
            {
                if (getNearestIndex)
                {
                    col = cols - 1;
                }
                else
                {
                    return -1;
                }
            }
            int row = (clientPoint.Y - AutoScrollPosition.Y) / gridSizeV;
            if (row < 0)
            {
                if (getNearestIndex)
                {
                    row = 0;
                }
                else
                {
                    return -1;
                }
            }
            else if (row >= rows)
            {
                if (getNearestIndex)
                {
                    row = rows - 1;
                }
                else
                {
                    return -1;
                }
            }
            var showIndex = row * cols + col; // 負にはならない
            if (showIndex < 0)
            {
                return -1; // ここへの到達は想定されない
            }
            else if (showIndex >= slen)
            {
                if (getNearestIndex)
                {
                    showIndex -= cols;
                    if (showIndex < 0) return slen - 1; // 一行に満たない場合
                    else if (showIndex < slen) return showIndex; // 上と左があれば常に上を選択
                    else return -1; // ここへの到達は想定されない
                }
                else
                {
                    return -1;
                }
            }
            else return showIndex;
        }

        public int FullDisplayedStartShowIndex
        {
            get
            {
                int rowStart = (-AutoScrollPosition.Y + gridSizeV - 1) / gridSizeV;
                return rowStart * cols;
            }
        }

        public int FullDisplayedStopShowIndex
        {
            get
            {
                int rowEnd = (-AutoScrollPosition.Y + Height - gridSizeV + 1) / gridSizeV + 1;
                return Math.Min(rowEnd * cols, showIndexToDataIndex.Length);
            }
        }

        public int DisplayedStartShowIndex
        {
            get
            {
                int rowStart = (-AutoScrollPosition.Y) / gridSizeV;
                return rowStart * cols;
            }
        }

        public int DisplayedStopShowIndex
        {
            get
            {
                int rowEnd = (-AutoScrollPosition.Y + Height) / gridSizeV + 1;
                return Math.Min(rowEnd * cols, showIndexToDataIndex.Length);
            }
        }

        // サムネイルを描画するサイズの設定と取得
        public int ThumbnailWidth
        {
            get { return thumbWidth; }
            set
            {
                thumbWidth = value;

                if (showIndexToDataIndex.Length == 0)
                    return;

                updateRowsCols();
                Invalidate();
            }
        }

        // サムネイルを描画するサイズの設定と取得
        public int ThumbnailHeight
        {
            get { return thumbHeight; }
            set
            {
                thumbHeight = value;

                if (showIndexToDataIndex.Length == 0)
                    return;

                updateRowsCols();
                Invalidate();
            }
        }

        // サムネイルを描画するサイズの設定と取得
        public Size ThumbnailSize
        {
            get { return new Size(thumbWidth, thumbHeight); }
            set
            {
                thumbWidth = value.Width;
                thumbHeight = value.Height;

                if (showIndexToDataIndex.Length == 0)
                    return;

                updateRowsCols();
                Invalidate();
            }
        }

        public bool ShowText
        {
            get { return showText; }
            set
            {
                showText = value;

                if (showIndexToDataIndex.Length == 0)
                    return;

                updateRowsCols();
                Invalidate();
            }
        }

        private int textLines = 0;
        public void SetTextLines(int textLines, bool refresh)
        {
            if (this.textLines != textLines || textLength != 0)
            {
                this.textLines = textLines;
                textLength = 0;
                SetTextHeightFromLines(refresh);
            }
        }

        public void SetTextLinesFromText(string text, bool refresh)
        {
            SetTextLines(Math.Max(1, GetTextLines(text)), refresh);
        }

        private int textLength = 16;
        public void SetTextLength(int textLength, bool refresh)
        {
            if (textLength != this.textLength || textLines != 0)
            {
                this.textLength = textLength;
                textLines = 0;
                SetTextHeightFromSquareCharCount(refresh);
            }
        }

        private void SetTextHeightFromLines(bool refresh)
        {
            var charHeight = GetCharHeight();
            var newHeight = GetTextHeightFromLines(textLines); // top は使わない
            if (textHeight != newHeight)
            {
                textHeight = newHeight;
                if (refresh)
                {
                    updateRowsCols();
                    Invalidate();
                }
            }
        }

        private bool SetTextHeightFromSquareCharCount_CharHeightNotSet = true;
        private int SetTextHeightFromSquareCharCount_CharHeight;
        public int GetCharHeight()
        {
            if (SetTextHeightFromSquareCharCount_CharHeightNotSet)
            {
                SetTextHeightFromSquareCharCount_CharHeightNotSet = false;
                SetTextHeightFromSquareCharCount_CharHeight = TextRenderer.MeasureText(".", DefaultFont).Height;
            }
            return SetTextHeightFromSquareCharCount_CharHeight;
        }
        private void SetTextHeightFromSquareCharCount(bool refresh)
        {
            var oldHeight = textHeight;
            textHeight = GetTextHeightFromSquareCharCount(textLength);
            if (refresh && oldHeight != textHeight)
            {
                updateRowsCols();
                Invalidate();
            }
        }

        private void SetTextHeight(bool refresh)
        {
            if (textLength > 0)
            {
                SetTextHeightFromSquareCharCount(refresh);
            }
            else
            {
                SetTextHeightFromLines(refresh);
            }
        }


        private int GetTextHeightFromSquareCharCount(int charCount)
        {
            var charHeight = GetCharHeight();
            var w = gridSizeH - sidePadding * 2;
            return (charCount * charHeight + w - 1) / w * (charHeight + bottomPadding) - bottomPadding; // top は使わない
        }

        private int GetTextHeightFromLines(int textLines)
        {
            var charHeight = GetCharHeight();
            return textLines * (charHeight + bottomPadding) - bottomPadding; // top は使わない
        }


        // サムネイル画像と画面のクリア
        bool Clearing = false;
        public void Clear(int initialImageCount, int initialSelectedIndex, double initialScroolbarPercentage, int[] showIndexToDataIndex, int tWidth, int tHeight, int tPadding)
        {

            Clearing = true;
            var imagesCount = items.Count;
            for (var i = 0; i < imagesCount; i++)
            {
                var item = items[i];
                if (item != null)
                {
                    item.Dispose();
                    items[i] = null;
                }
            }

            //sw.Stop(); ss += $"disEnd\t{sw.Elapsed}\n"; sw.Restart();

            // 表示していた項目の画像もキャッシュに入った後で、見た目の設定が変わっていれば全部捨て、
            // 同じなら更新日時で確かめられるものだけ残す（K26。以前は丸ごと捨てていた。A3）
            ThumbnailMemoryCache.OnListRebuilt(ThumbnailAppearanceKey);

            Refresh();
            items.Clear();
            focusedIndex = -1;
            SelectedIndexChangedEventArgs se = null;
            if (selectedIndices.Count > 0)
            {
                se = new SelectedIndexChangedEventArgs(SelectedIndex, -1);
                //ClearAllSelection();
                selectedIndices.Clear(); // items はクリア済みで item.Index を変える必要がなく、また変えようとすると IndexOutOfRangeExeption
                selectedSet.Clear();
            }
            dataIndexToShowIndex = new int[initialImageCount];
            this.showIndexToDataIndex = showIndexToDataIndex;
            itemsVersion++;
            for (var i = 0; i < initialImageCount; i++)
            {
                items.Add(null);
                dataIndexToShowIndex[i] = -1;
            }
            //sw.Stop(); ss += $"pred2sEnd\t{sw.Elapsed}\n"; sw.Restart();
            for (var i = 0; i < showIndexToDataIndex.Length; i++)
            {
                dataIndexToShowIndex[showIndexToDataIndex[i]] = i;
            }
            //sw.Stop(); ss += $"d2sEnd\t{sw.Elapsed}\n"; sw.Restart();

            thumbWidth = tWidth;
            thumbHeight = tHeight;
            topPadding = sidePadding = tPadding;
            if (0 <= initialSelectedIndex && initialSelectedIndex < initialImageCount)
            {
                if (se == null) se = new SelectedIndexChangedEventArgs(SelectedIndex, -1);
                else se = new SelectedIndexChangedEventArgs(se.OldIndex, initialSelectedIndex);
                //ClearAllSelection();
                addSelection(initialSelectedIndex);
            }

            if (se != null) SelectedIndexChanged?.Invoke(this, se);

            updateRowsCols();

            setScrollBarPercentage(initialScroolbarPercentage);

            Invalidate();

            Clearing = false;

            //sw.Stop(); ss += $"evStart\t{sw.Elapsed}\n"; sw.Restart();
            ShowIndexToDataIndexChanged?.Invoke(this, null);

            //sw.Stop(); ss += $"fin\t{sw.Elapsed}\n";
            //MessageBox.Show(ss);
        }

        Form ownerForm = null;
        FormWindowState prevFormWindowState = FormWindowState.Normal;
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            if (ownerForm == null)
            {
                ownerForm = FindForm();
            }
            var correctPosition = false;
            if (ownerForm != null)
            {
                var formWindowState = ownerForm.WindowState;
                correctPosition = formWindowState != FormWindowState.Minimized && prevFormWindowState != FormWindowState.Minimized;
                prevFormWindowState = formWindowState;
            }

            setScrollBarPercentage(scrollBarPercentage);
        }

        double OnPaint_VerticalScrollTop = 0;
        double OnPaint_VerticalScrollBottom = 0;
        double OnPaint_VerticalScrollVal = 0;
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Clearing) return;

            var top = VerticalScroll.Minimum;
            var bottom = VerticalScroll.Maximum;
            var val = VerticalScroll.Value;
            var fullHeight = bottom - top;
            var changed = fullHeight > 0 && (OnPaint_VerticalScrollTop != top || OnPaint_VerticalScrollBottom != bottom || OnPaint_VerticalScrollVal != val);
            OnPaint_VerticalScrollTop = top;
            OnPaint_VerticalScrollBottom = bottom;
            OnPaint_VerticalScrollVal = val;
            setScrollBarPercentageOnPaint(Height, top, val, fullHeight);

            if (changed) DisplayedChanged?.Invoke(this, null);

            base.OnPaint(e);

#if PERFLOG
            var perfStart = Stopwatch.GetTimestamp();
#endif
            var sub = tryRenderWithPrerender(e.Graphics, e.ClipRectangle, AutoScrollPosition.Y);
            if (!sub.IsEmpty)
            {
                PaintPart(e.Graphics, sub, AutoScrollPosition.Y);
            }
#if PERFLOG
            PerfTrace.AddPaint(Stopwatch.GetTimestamp() - perfStart);
#endif
        }

        // サムネイルを読み込む範囲（表示中＋前後1行、表示順のインデックス）と、そのときの項目数
        private int loadRangeStart, loadRangeStop, loadRangeItemCount = -1;

        // 範囲外の項目を捨てるとき、以前は描くたびに一覧の全項目の Clear を呼んでいた（10 万項目なら 1 回のスクロールで 10 万回）。
        // 項目が画像を持つ・待ち行列に入るのは PaintPart の LoadAsync からなので、項目の差し替え・並べ替えがなければ、
        // 前回までに LoadAsync を呼んだ項目のうち範囲から外れたものだけを Clear すればよい。
        // 差し替え・並べ替え・作り直しで itemsVersion が変わったら、従来どおり全項目を見る
        private int itemsVersion = 0;
        private int sweptItemsVersion = -1;
        private readonly HashSet<int> loadRequestedDataIndices = new HashSet<int>();
        private readonly HashSet<int> loadRangeDataIndices = new HashSet<int>();
        private readonly List<int> leftRangeDataIndices = new List<int>();
#if PERFLOG
        // 調査用: 読み込み範囲（表示中＋前後1行）の項目がすべてサムネイルを持っているか
        internal bool IsLoadRangeReady()
        {
            var s2d = ShowIndexToDataIndex;
            if (s2d == null) return false;
            for (var i = loadRangeStart; i < Math.Min(loadRangeStop, s2d.Length); i++)
            {
                var item = items[s2d[i]];
                if (item != null && item.FilePath != null && item.Image == null) return false;
            }
            return true;
        }
        // 調査用: サムネイル画像を持っている項目の数（範囲外に残っているものも含む）
        internal int CountLoadedImages() => items.Count(item => item != null && item.Image != null);
#endif

        private void PaintPart(Graphics g, Rectangle clipRectangle, int autoScrollPositionY)
        {
            int rowStart = (-autoScrollPositionY + clipRectangle.Y) / gridSizeV;
            int rowEnd = (-autoScrollPositionY + clipRectangle.Y + clipRectangle.Height) / gridSizeV + 1;

            for (int i = rowStart * cols; i < Math.Min(rowEnd * cols, showIndexToDataIndex.Length); i++)
            {
                var dataIndex = ShowIndexToDataIndex[i];

                var selected = selectedSet.Contains(dataIndex);
                DrawItem(g, dataIndex, selected, alreadyCheckedVisibleRegion: true, clipRectangle: clipRectangle, AutoScrollPositionY: autoScrollPositionY);
            }

            //表示中のアイテム+前後1列のアイテムのBitmapを読み込み、それ以外を破棄する
            var clearBefore = -AutoScrollPosition.Y / gridSizeV - 1;
            var clearAfter = (-AutoScrollPosition.Y + Height) / gridSizeV + 2;
            var rangeStart = Math.Max(0, clearBefore * cols);
            var rangeStop = Math.Min(clearAfter * cols, ShowIndexToDataIndex.Length);
            // 同じ一覧で範囲が動いた＝スクロール中。読み込みの待ち行列は、止まるまで新しい読み込みを始めない（A4）
            if ((rangeStart != loadRangeStart || rangeStop != loadRangeStop) && loadRangeStop > loadRangeStart && loadRangeItemCount == ShowIndexToDataIndex.Length)
            {
                ThumbViewerItem.LastScrollTick = Environment.TickCount;
            }
            loadRangeStart = rangeStart;
            loadRangeStop = rangeStop;
            loadRangeItemCount = ShowIndexToDataIndex.Length;
#if PERFLOG
            PerfTrace.LoadRange(rangeStart, rangeStop,
                $"scrollY={AutoScrollPosition.Y} height={Height} gridV={gridSizeV} cols={cols} clip={clipRectangle} paintY={autoScrollPositionY} items={ShowIndexToDataIndex.Length}");
            PerfTrace.CheckRangeReady(IsLoadRangeReady(), CountLoadedImages);
#endif
            var s2d = ShowIndexToDataIndex;
            if (sweptItemsVersion != itemsVersion)
            {
                for (int i = 0; i < clearBefore * cols; i++)
                {
                    items[s2d[i]]?.Clear();
                }
                for (int i = clearAfter * cols; i < s2d.Length; i++)
                {
                    items[s2d[i]]?.Clear();
                }
                loadRequestedDataIndices.Clear();
                sweptItemsVersion = itemsVersion;
            }
            else if (loadRequestedDataIndices.Count > 0)
            {
                loadRangeDataIndices.Clear();
                for (int i = rangeStart; i < rangeStop; i++) loadRangeDataIndices.Add(s2d[i]);
                leftRangeDataIndices.Clear();
                foreach (var dataIndex in loadRequestedDataIndices)
                {
                    if (!loadRangeDataIndices.Contains(dataIndex)) leftRangeDataIndices.Add(dataIndex);
                }
                foreach (var dataIndex in leftRangeDataIndices)
                {
                    if (dataIndex < items.Count) items[dataIndex]?.Clear();
                    loadRequestedDataIndices.Remove(dataIndex);
                }
            }
            // 待ち行列は新しく依頼されたものから処理するので、下から依頼して上から読み込まれるようにする
            for(int i = rangeStop - 1; i >= rangeStart; i--)
            {
                var dataIndex = s2d[i];
                var item = items[dataIndex];
                _ = item.LoadAsync();
                loadRequestedDataIndices.Add(dataIndex);
            }

        }

        private int scrollByFloorGrid(MouseEventArgs e, int line)
        {
            var i = getScrollCount(e) * line;
            if (i > 0)
            {
                return (-AutoScrollPosition.Y + Height) / gridSizeV * gridSizeV - Height + i * gridSizeV;
            }
            else if (i < 0)
            {
                return (-AutoScrollPosition.Y - 1) / gridSizeV * gridSizeV + (i + 1) * gridSizeV;
            }
            else return -AutoScrollPosition.Y;
        }

        private int scrollByAbsolutePixel(MouseEventArgs e, int amount)
        {
            return -AutoScrollPosition.Y - e.Delta * amount / 120;
        }

        private static int getScrollCount(MouseEventArgs e)
        {
            var delta = -e.Delta;
            if (delta == 0) return 0;
            if (delta > 0) return (delta + 119) / 120;
            return -((-delta + 119) / 120);
        }

        private Bitmap preRenderScroll_Canvas;
        private Bitmap preRenderScroll_Canvas030;
        private Bitmap preRenderScroll_Canvas060;
        private Bitmap preRenderScroll_Canvas120;
        private Bitmap preRenderScroll_Canvas240;
        private Bitmap preRenderScroll_Canvas480;
        private Rectangle preRenderScroll_ClipRectangle;
        private int preRenderScroll_AutoScrollPositionY = int.MinValue;
        private void preRenderScroll(int newY, int preRenderHeightUBound)
        {
            var oldY = -AutoScrollPosition.Y;

            if (newY < 0) newY = 0;
            else
            {
                // 複数のサイズ（偶数と奇数を含む）で確認済み
                var AutoScrollPositionYAbsMaxValue = VerticalScroll.Maximum - Height + 1;
                if (newY > AutoScrollPositionYAbsMaxValue) newY = AutoScrollPositionYAbsMaxValue;
            }

            var d = newY - oldY;
            if (d == 0)
            {
                return;
            }

            var w = Width;
            var h = Math.Abs(d);
            if (preRenderHeightUBound > 480) preRenderHeightUBound = 480;
            if (h <= preRenderHeightUBound && preRenderScroll_AutoScrollPositionY == int.MinValue)
            {
                Bitmap canvas;
                if (h <= 30)
                {
                    preRenderScroll_Canvas030 = UpdateCanvas(preRenderScroll_Canvas030, w, h);
                    canvas = preRenderScroll_Canvas030;
                }
                else if (h <= 60)
                {
                    preRenderScroll_Canvas060 = UpdateCanvas(preRenderScroll_Canvas060, w, h);
                    canvas = preRenderScroll_Canvas060;
                }
                else if (h <= 120)
                {
                    preRenderScroll_Canvas120 = UpdateCanvas(preRenderScroll_Canvas120, w, h);
                    canvas = preRenderScroll_Canvas120;
                }
                else if (h <= 240)
                {
                    preRenderScroll_Canvas240 = UpdateCanvas(preRenderScroll_Canvas240, w, h);
                    canvas = preRenderScroll_Canvas240;
                }
                else
                {
                    preRenderScroll_Canvas480 = UpdateCanvas(preRenderScroll_Canvas480, w, h);
                    canvas = preRenderScroll_Canvas480;
                }


                if (d > 0)
                {
                    var o = Height - h;
                    preRenderScroll_ClipRectangle = new Rectangle(0, o, w, h);
                    var oClip = preRenderScroll_ClipRectangle;
                    oClip.Y = 0;
                    using (var g = Graphics.FromImage(canvas))
                    {
                        g.FillRectangle(backBrush, oClip);
                        PaintPart(g, oClip, -newY - o);
                    }
                }
                else
                {
                    preRenderScroll_ClipRectangle = new Rectangle(0, 0, w, h);
                    using (var g = Graphics.FromImage(canvas))
                    {
                        g.FillRectangle(backBrush, preRenderScroll_ClipRectangle);
                        PaintPart(g, preRenderScroll_ClipRectangle, -newY);
                    }
                }

                preRenderScroll_Canvas = canvas;
                preRenderScroll_AutoScrollPositionY = -newY;
            }

            AutoScrollPosition = new Point(-AutoScrollPosition.X, newY);

        }

        private Rectangle tryRenderWithPrerender(Graphics g, Rectangle clipRectangle, int autoScrollPositionY)
        {
            if (preRenderScroll_AutoScrollPositionY == int.MinValue)
            {
                return clipRectangle;
            }
            var dstAbs = new Rectangle(clipRectangle.X, clipRectangle.Y - autoScrollPositionY, clipRectangle.Width, clipRectangle.Height);
            var srcAbs = new Rectangle(preRenderScroll_ClipRectangle.X, preRenderScroll_ClipRectangle.Y - preRenderScroll_AutoScrollPositionY, preRenderScroll_ClipRectangle.Width, preRenderScroll_ClipRectangle.Height);
            Rectangle sub;
            if (srcAbs.Contains(dstAbs)) sub = Rectangle.Empty;
            else
            {
                sub = Substruct(dstAbs, srcAbs);
                if (sub.IsEmpty)
                {
                    preRenderScroll_AutoScrollPositionY = int.MinValue;
                    return clipRectangle;
                }
            }
            var intersect = dstAbs;
            intersect.Intersect(srcAbs);
            var dstRect = new Rectangle(intersect.X, intersect.Y + autoScrollPositionY, intersect.Width, intersect.Height);
            var srcRect = new Rectangle(intersect.X - preRenderScroll_ClipRectangle.X, intersect.Y - preRenderScroll_ClipRectangle.Y + preRenderScroll_AutoScrollPositionY, intersect.Width, intersect.Height);
            var temp = g.InterpolationMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.DrawImage(preRenderScroll_Canvas, dstRect, srcRect, GraphicsUnit.Pixel);
            //g.FillRectangle(Brushes.Black, dstRect);
            g.InterpolationMode = temp;
            preRenderScroll_AutoScrollPositionY = int.MinValue;
            if (!sub.IsEmpty) sub.Y += autoScrollPositionY;
            return sub;
        }

        // 差が長方形の場合のみそれを返す。さもなければ Empty
        // ただし必要な分しか実装していないので他の用途には使わないこと
        public static Rectangle Substruct(Rectangle rect, Rectangle other)
        {
            if (rect.Left < other.Left || rect.Right > other.Right) return Rectangle.Empty;
            if (rect.Top < other.Top)
            {
                if (rect.Bottom <= other.Bottom)
                {
                    rect.Height = other.Top - rect.Top;
                    return rect;
                }
                else return Rectangle.Empty;
            }
            else if (rect.Bottom > other.Bottom)
            {
                rect.Height = rect.Bottom - other.Bottom;
                rect.Y = other.Bottom;
                return rect;
            }
            else return Rectangle.Empty;
        }

        private static Bitmap UpdateCanvas(Bitmap bmp, int width, int height)
        {
            var bmpn = bmp == null;
            if (bmpn || bmp.Width < width || bmp.Height < height)
            {
                var w = bmpn ? width : Math.Max(bmp.Width, width);
                var h = bmpn ? height : Math.Max(bmp.Height, height);
                bmp?.Dispose();
                return new Bitmap(w, h);
            }
            else return bmp;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            //GuiUtil.BeginUpdate(this);
            //base.OnMouseWheel(e);

            switch (MouseWheelScrollUnit)
            {
                case ThumbViewerScrollUnit.AbsolutePixel:
                    preRenderScroll(scrollByAbsolutePixel(e, MouseWheelScrollAmount), preRenderHeightUBound: MouseWheelScrollAmount);
                    break;
                case ThumbViewerScrollUnit.GridCeiling:
                    preRenderScroll(scrollByFloorGrid(e, MouseWheelScrollAmount), preRenderHeightUBound: gridSizeV); // 天井の値が指定されてるならその床が移動量
                    break;
            }

            // スクロール量実験
            // → 結果、DPI にかかわらず e.Delta の単位は 120 で、
            // OnMouseWheel はその数をそのままスクロール量漁に使うと判明

            //scrollByGrid(-e.Delta / 120);
            //GuiUtil.EndUpdate(this);
            if (0 <= mouseDownShowIndex && showIndexToDataIndex != null && mouseDownShowIndex < showIndexToDataIndex.Length)
            {
                var clientPosition = PointToClient(Cursor.Position);
                var e2 = new MouseEventArgs(MouseButtons.None, 0, clientPosition.X, clientPosition.Y, 0);
                OnMouseMove(e2);
            }
        }


        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            updateRowsCols();
            Invalidate();
        }

        public int SelectedIndexDelay { get; private set; }

        private int focusedIndex = -1;
        public int FocusedIndex
        {
            get
            {
                if (0 <= focusedIndex && focusedIndex < items.Count)
                {
                    return focusedIndex;
                }
                else
                {
                    return -1;
                }
            }
            set
            {
                focusedIndex = value;
                focusedIndex = FocusedIndex;
            }
        }

        public MouseButtons SubMouseButtons = MouseButtons.Middle | MouseButtons.Right;

        public Func<Keys> GetModifierKeys;
        private new Keys ModifierKeys
        {
            get
            {
                return GetModifierKeys == null ? Control.ModifierKeys : GetModifierKeys();
            }
        }

        public bool DragOutSelection = false;
        public bool DragOutSingle = false;


        private int mouseDownShowIndex = -1;
        private Rectangle OnMouseDown_DragOutStartRectangle = Rectangle.Empty;
        private int OnMouseDown_DragOutStartIndex = -1;
        protected override void OnMouseDown(MouseEventArgs e)
        {
            int newSelected = PointToIndex(e.Location);

            var indexOnIndices = newSelected < 0 ? -1 : IndexOnSelection(newSelected);
            var alreadySelected = indexOnIndices >= 0;

            if (!DragOutSelection || !alreadySelected || !(e.Button == MouseButtons.Left || e.Button == MouseButtons.Right))
            {
                SelectedIndexDelay = SelectedIndex;

                if (e.Button == MouseButtons.Left || ((e.Button & SubMouseButtons) != 0 &&  //(e.Button != MouseButtons.XButton1 && e.Button != MouseButtons.XButton2 &&
                    indexOnIndices < 0))
                //if (e.Button == MouseButtons.Left || (e.Button == MouseButtons.Right && !selectedIndices.Contains(newSelected)))
                {
                    var modifierKeys = ModifierKeys;
                    var focusShouldBeChanged = true;
                    if (modifierKeys == Keys.Control && e.Button == MouseButtons.Left)
                    {
                        if (indexOnIndices < 0)
                        {
                            var focused = FocusedIndex;
                            var showFocused = focused >= 0 ? dataIndexToShowIndex[focused] : -1;
                            var showNew = newSelected >= 0 ? dataIndexToShowIndex[newSelected] : -1;

                            setSelected(newSelected, true, -1, graphics: null);

                            if (e.Button == MouseButtons.Left)
                            {
                                mouseDownShowIndex = showNew;
                                OnMouseMove_PrevMousePoint = e.Location;
                            }
                        }
                        else
                        {
                            setSelected(newSelected, false, indexOnIndices, graphics: null);
                        }
                    }
                    else
                    {
                        var focused = FocusedIndex;
                        var showFocused = focused >= 0 ? dataIndexToShowIndex[focused] : -1;
                        var showNew = newSelected >= 0 ? dataIndexToShowIndex[newSelected] : -1;
                        if (showFocused >= 0 && showNew >= 0 && (modifierKeys & Keys.Shift) == Keys.Shift)
                        {
                            focusShouldBeChanged = selectedIndices.Count == 0;

                            var oldSelectedIndex = SelectedIndex;
                            var minShow = Math.Min(showFocused, showNew);
                            var maxShow = Math.Max(showFocused, showNew);
                            var clear = (modifierKeys & Keys.Control) != Keys.Control;

                            //var drawCount = (clear ? selectedIndices.Count : 0) + maxShow - minShow + 1;
                            //var drawSingle = drawCount == 1;
                            var newSelecedIndices = new int[maxShow - minShow + 1];
                            for (var i = minShow; i <= maxShow; i++) newSelecedIndices[i - minShow] = showIndexToDataIndex[i];
                            //var selectedToNoselected = clear ? (from s in selectedIndices where !newSelecedIndices.Contains(s) select s).ToArray() : null;
                            //var noselectedToSelected = (from s in newSelecedIndices where !selectedIndices.Contains(s) select s).ToArray();
                            GetChangeSelect(out var selectedToNoselected, out var noselectedToSelected, selectedIndices, newSelecedIndices, firstOutputRequired: clear);
                            if (clear && selectedToNoselected.Any() || noselectedToSelected.Any())
                            {
                                using (Graphics g = this.CreateGraphics())
                                {
                                    if (clear)
                                    {
                                        foreach (var index in selectedToNoselected)
                                        {
                                            setSelectedWithoutEvent(index, false, IndexOnSelection(index), g);
                                        }
                                    }

                                    foreach (var dataIndex in noselectedToSelected)
                                    {
                                        //var dataIndex = showIndexToDataIndex[i];
                                        setSelectedWithoutEvent(dataIndex, true, IndexOnSelection(dataIndex), g);
                                    }
                                }
                            }

                            //if (drawCount > 1) Invalidate();

                            var se = new SelectedIndexChangedEventArgs(oldSelectedIndex, SelectedIndex);
                            SelectedIndexChanged?.Invoke(this, se);
                        }
                        else
                        {
                            SelectedIndex = newSelected; // ここはフィールドへの代入ではなくプロパティーへの代入でなければならない
                        }
                        if (e.Button == MouseButtons.Left)
                        {
                            mouseDownShowIndex = showNew;
                            OnMouseMove_PrevMousePoint = e.Location;
                        }
                    }
                    if (focusShouldBeChanged) focusedIndex = newSelected;
                }

                // インデックス変更後にイベント・ハンドラが呼び出されるようにする
                var e2 = new HandlableMouseEventArgs(e);
                base.OnMouseDown(e2);

                if (DragOutSingle && !alreadySelected && !e2.Handled)
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        // 領域を出るまでは複数選択
                        OnMouseDown_DragOutStartIndex = newSelected;
                    }
                    else
                    {
                        // 初めからドラッグ
                        OnMouseDown_DragOutStartRectangle = PointToContainerGridRectangle(e.Location, cutOverflow: true);
                    }
                }

                SelectedIndexDelay = SelectedIndex;
            }
            else
            {
                var e2 = new HandlableMouseEventArgs(e);
                SelectedIndexDelay = SelectedIndex;
                base.OnMouseDown(e2);
                if (!e2.Handled)
                {
                    //OnMouseDown_DragOutStartRectangle = GetRectangleWithCenter(e.Location, SystemInformation.DragSize);
                    OnMouseDown_DragOutStartRectangle = PointToContainerGridRectangle(e.Location, cutOverflow: true);
                }
            }
        }

        private static Rectangle GetRectangleWithCenter(Point center, Size size)
        {
            var w = size.Width;
            var h = size.Height;
            return new Rectangle(center.X - w / 2, center.Y - h / 2, w, h);
        }

        private HashSet<int> selectedIndexByDrag = null;
        private Point OnMouseMove_PrevMousePoint = Point.Empty;
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (OnMouseDown_DragOutStartIndex >= 0 && !ActivateManager.InVisibleRegion(this, e.Location)) //!ClientRectangle.Contains(e.Location))
            {
                mouseDownShowIndex = -1;
                selectedIndexByDrag = null;
                OnMouseMove_PrevMousePoint = Point.Empty;

                var d2s = dataIndexToShowIndex;
                if (d2s.Length > OnMouseDown_DragOutStartIndex)
                {
                    SelectedIndex = OnMouseDown_DragOutStartIndex;
                    DragReady?.Invoke(this, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                }
                OnMouseDown_DragOutStartIndex = -1;
            }
            else if (0 <= mouseDownShowIndex && showIndexToDataIndex != null && mouseDownShowIndex < showIndexToDataIndex.Length)
            {
                var dX = e.X - OnMouseMove_PrevMousePoint.X;
                var dY = e.Y - OnMouseMove_PrevMousePoint.Y;

                var showIndex = PointToShowIndex(e.Location, getNearestIndex: true);
                if (showIndex >= 0)
                {
                    var oldSelectedIndex = SelectedIndex;
                    var selectionChanged = false;
                    if (selectedIndexByDrag == null) selectedIndexByDrag = new HashSet<int>();
                    var minShow = Math.Min(mouseDownShowIndex, showIndex);
                    var maxShow = Math.Max(mouseDownShowIndex, showIndex);

                    if (maxShow >= minShow || selectedIndexByDrag.Any())
                    {
                        using (var g = CreateGraphics())
                        {
                            for (var i = minShow; i <= maxShow; i++)
                            {
                                //selectArray[i - minShow] = showIndexToDataIndex[i];
                                var dataIndex = showIndexToDataIndex[i];

                                if (setSelectedWithoutEvent(dataIndex, true, IndexOnSelection(dataIndex), g))
                                {
                                    selectedIndexByDrag.Add(dataIndex);
                                    selectionChanged = true;
                                }

                            }
                            // var noselectList = new List<int>();
                            foreach (var i in selectedIndexByDrag.ToArray())
                            {
                                var iShow = dataIndexToShowIndex[i];
                                if (iShow < minShow || maxShow < iShow)
                                {
                                    selectedIndexByDrag.Remove(i);
                                    //noselectList.Add(i);
                                    //drawNeeded = true;
                                    if (setSelectedWithoutEvent(i, false, IndexOnSelection(i), g))
                                    {
                                        selectionChanged = true;
                                    }
                                }
                            }
                        }
                    }

                    //if (drawNeeded && !drawSingle) Invalidate();

                    if (selectionChanged)
                    {
                        var se = new SelectedIndexChangedEventArgs(oldSelectedIndex, SelectedIndex);
                        SelectedIndexChanged?.Invoke(this, se);
                    }
                }

                if (e.Y < ClientRectangle.Top)
                {
                    AutoScrollPosition = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y - Math.Abs(dX) - Math.Abs(dY));
                }
                else if (e.Y >= ClientRectangle.Bottom)
                {
                    AutoScrollPosition = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y + Math.Abs(dX) + Math.Abs(dY));
                }

                OnMouseMove_PrevMousePoint = e.Location;
            }
            else if (!OnMouseDown_DragOutStartRectangle.IsEmpty) // !OnMouseDown_DragOutStartRectangle.Contains(e.Location))
            {
                if (!OnMouseDown_DragOutStartRectangle.Contains(e.Location))
                //if (!ActivateManager.InVisibleRegion(this, e.Location))
                {
                    OnMouseDown_DragOutStartRectangle = Rectangle.Empty;
                    DragReady?.Invoke(this, e);
                }
            }

            base.OnMouseMove(e);
        }

        public event MouseEventHandler DragReady;
        public event MouseEventHandler DragAttempted;

        protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
        {
            if (((Keys)e.KeyState & (Keys.LButton | Keys.RButton)) == (Keys.LButton | Keys.RButton))
            {
                e.Action = DragAction.Cancel;
            }
            base.OnQueryContinueDrag(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            mouseDownShowIndex = -1;
            selectedIndexByDrag = null;
            OnMouseMove_PrevMousePoint = Point.Empty;
            OnMouseDown_DragOutStartIndex = -1;


            int newSelected = PointToIndex(e.Location);


            var modifierKeys = ModifierKeys;
            if (!OnMouseDown_DragOutStartRectangle.IsEmpty)
            {
                OnMouseDown_DragOutStartRectangle = Rectangle.Empty;

                //var modifierKeys = ModifierKeys;
                if (e.Button == MouseButtons.Left)
                {
                    switch (modifierKeys)
                    {
                        case Keys.Control:
                            {
                                var indexOnIndices = newSelected < 0 ? -1 : IndexOnSelection(newSelected);
                                if (indexOnIndices < 0)
                                {
                                    var focused = FocusedIndex;
                                    var showFocused = focused >= 0 ? dataIndexToShowIndex[focused] : -1;
                                    var showNew = newSelected >= 0 ? dataIndexToShowIndex[newSelected] : -1;

                                    setSelected(newSelected, true, -1, graphics: null);

                                    if (e.Button == MouseButtons.Left)
                                    {
                                        mouseDownShowIndex = showNew;
                                        OnMouseMove_PrevMousePoint = e.Location;
                                    }
                                }
                                else
                                {
                                    setSelected(newSelected, false, indexOnIndices, graphics: null);
                                }
                            }
                            break;
                        default:
                            {
                                var focused = FocusedIndex;
                                var showFocused = focused >= 0 ? dataIndexToShowIndex[focused] : -1;
                                var showNew = newSelected >= 0 ? dataIndexToShowIndex[newSelected] : -1;
                                if (showFocused >= 0 && showNew >= 0 && (modifierKeys & Keys.Shift) == Keys.Shift)
                                {
                                    var oldSelectedIndex = SelectedIndex;

                                    if ((modifierKeys & Keys.Control) != Keys.Control)
                                    {
                                        var minShow = Math.Min(showFocused, showNew);
                                        var maxShow = Math.Max(showFocused, showNew);
                                        //var drawCount = selectedIndices.Count + maxShow - minShow + 1;
                                        //var drawSingle = drawCount == 1;

                                        var newSelecedIndices = new int[maxShow - minShow + 1];
                                        for (var i = minShow; i <= maxShow; i++) newSelecedIndices[i - minShow] = showIndexToDataIndex[i];
                                        //var selectedToNoselected = (from s in selectedIndices where !newSelecedIndices.Contains(s) select s).ToArray();
                                        //var noselectedToSelected = (from s in newSelecedIndices where !selectedIndices.Contains(s) select s).ToArray();
                                        GetChangeSelect(out var selectedToNoselected, out var noselectedToSelected, selectedIndices, newSelecedIndices);

                                        if (selectedToNoselected.Any() || noselectedToSelected.Any())
                                        {
                                            using (var g = CreateGraphics())
                                            {
                                                foreach (var index in selectedToNoselected)
                                                {
                                                    setSelectedWithoutEvent(index, false, IndexOnSelection(index), g);
                                                }

                                                foreach (var dataIndex in noselectedToSelected)
                                                {
                                                    //var dataIndex = showIndexToDataIndex[i];
                                                    setSelectedWithoutEvent(dataIndex, true, IndexOnSelection(dataIndex), g);
                                                }
                                            }
                                        }

                                        //if (drawCount > 1) Invalidate();
                                    }
                                    else
                                    {
                                        var minShow = Math.Min(showFocused, showNew);
                                        var maxShow = Math.Max(showFocused, showNew);
                                        //var drawCount = maxShow - minShow + 1;
                                        //var drawSingle = drawCount == 1;
                                        if (maxShow >= minShow)
                                        {
                                            using (var g = CreateGraphics())
                                            {
                                                for (var i = minShow; i <= maxShow; i++)
                                                {
                                                    var dataIndex = showIndexToDataIndex[i];
                                                    var indexindex = IndexOnSelection(dataIndex);
                                                    //setSelectedWithoutEvent(dataIndex, indexindex < 0, indexindex); // 反転
                                                    //setSelectedWithoutEvent(dataIndex, true, indexindex); // 追加
                                                    setSelectedWithoutEvent(dataIndex, false, indexindex, g); // 取り除き
                                                }
                                            }
                                        }

                                        //if (drawCount > 1) Invalidate();
                                    }
                                    var se = new SelectedIndexChangedEventArgs(oldSelectedIndex, SelectedIndex);
                                    SelectedIndexChanged?.Invoke(this, se);
                                }
                                else
                                {
                                    SelectedIndex = newSelected; // ここはフィールドへの代入ではなくプロパティーへの代入でなければならない
                                }
                            }
                            break;
                    }
                }
                DragAttempted?.Invoke(this, e);

            }

            if ((modifierKeys & Keys.Shift) == 0) focusedIndex = newSelected;

            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            mouseDownShowIndex = -1;
            selectedIndexByDrag = null;
            OnMouseMove_PrevMousePoint = Point.Empty;
            OnMouseDown_DragOutStartIndex = -1;

            base.OnMouseLeave(e);
        }

        public Point GetUpperLeftClientcoordinateOf(int showIndex)
        {
            return GetCoordinateOf(showIndex, semiCenter: false);
        }

        public Point GetSemiCenterClientcoordinateOf(int showIndex)
        {
            return GetCoordinateOf(showIndex, semiCenter: true);
        }

        private Point GetCoordinateOf(int showIndex, bool semiCenter)
        {
            int col = showIndex % cols;
            int row = showIndex / cols;

            var x = gridOffsetH + col * gridSizeH;
            var y = row * gridSizeV + AutoScrollPosition.Y;

            if (semiCenter)
            {
                var m2 = Math.Min(gridSizeH, gridSizeV) / 2;
                x += m2;
                y += m2;
            }

            var rect = ClientRectangle;
            if (x < rect.Left) x = rect.Left;
            else if (x >= rect.Right) x = rect.Right - 1;
            if (y < rect.Top) y = rect.Top;
            else if (y >= rect.Bottom) y = rect.Bottom - 1;

            return new Point(x, y);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
        }


        protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
        {
            if (e.Alt) return;

            if (Clearing) return;

            if (focusedIndex < 0 || focusedIndex >= dataIndexToShowIndex.Length || dataIndexToShowIndex[focusedIndex] < 0)
            {
                focusedIndex = SelectedIndex;
            }

            if ((e.KeyCode == Keys.A && e.Modifiers == Keys.Control))
            {
                var showCount = showIndexToDataIndex.Length;
                if (e.Modifiers == Keys.Control && selectedIndices.Count < showCount)
                {
                    var oldSelectedIndex = SelectedIndex;
                    e.IsInputKey = true; // フォーカスの移動を禁止
                    //var drawSingle = showCount == 1;
                    if (showCount > 0)
                    {
                        using (var g = CreateGraphics())
                        {
                            for (var i = 0; i < showCount; i++)
                            {
                                var dataIndex = showIndexToDataIndex[i];
                                setSelectedWithoutEvent(dataIndex, true, IndexOnSelection(dataIndex), g);
                            }
                        }
                    }
                    //if (showCount > 1) Invalidate();
                    var se = new SelectedIndexChangedEventArgs(oldSelectedIndex, SelectedIndex);
                    SelectedIndexChanged?.Invoke(this, se);
                }
            }
            else
            {
                int currentShowIndex;
                var fi = FocusedIndex;
                if (fi >= 0)
                {
                    currentShowIndex = dataIndexToShowIndex[fi];
                }
                else
                {
                    currentShowIndex = PointToShowIndex(new Point(0, 0), getNearestIndex: true);
                }
                if (currentShowIndex >= 0)
                {
                    int nextShowIndex = -1;
                    bool operated = true;

                    switch (e.KeyCode)
                    {
                        case Keys.Down:
                            nextShowIndex = currentShowIndex + cols;
                            if (HorizontalLoopLevel >= 1 && nextShowIndex >= showIndexToDataIndex.Length)
                            {
                                if (HorizontalLoopLevel >= 2 || currentShowIndex / cols < (showIndexToDataIndex.Length - 1) / cols)
                                {
                                    nextShowIndex = showIndexToDataIndex.Length - 1;
                                }
                            }
                            break;
                        case Keys.Up:
                            nextShowIndex = currentShowIndex - cols;
                            if (HorizontalLoopLevel >= 2 && nextShowIndex < 0)
                            {
                                nextShowIndex = 0;
                            }
                            break;
                        case Keys.Left:
                            if (HorizontalLoopLevel >= 1 || currentShowIndex % cols != 0)
                            {
                                nextShowIndex = currentShowIndex - 1;
                            }
                            break;
                        case Keys.Right:
                            if (HorizontalLoopLevel >= 1 || (currentShowIndex + 1) % cols != 0)
                            {
                                nextShowIndex = currentShowIndex + 1;
                            }
                            break;
                        case Keys.Home:
                            nextShowIndex = 0;
                            break;
                        case Keys.End:
                            nextShowIndex = showIndexToDataIndex.Length - 1;
                            break;
                        case Keys.PageUp:
                            int fullStart = FullDisplayedStartShowIndex;
                            if (currentShowIndex > fullStart) nextShowIndex = fullStart;
                            else nextShowIndex = Math.Max(0, currentShowIndex - (Height / gridSizeV) * cols);
                            break;
                        case Keys.PageDown:
                            int fullEnd = FullDisplayedStopShowIndex - 1;
                            if (currentShowIndex < fullEnd) nextShowIndex = fullEnd;
                            else nextShowIndex = Math.Min(showIndexToDataIndex.Length - 1, currentShowIndex + (Height / gridSizeV) * cols);
                            break;
                        default:
                            operated = false;
                            break;
                    }

                    if (operated)
                    {
                        e.IsInputKey = true; // フォーカスの移動を禁止
                        if (nextShowIndex != currentShowIndex && 0 <= nextShowIndex && nextShowIndex < showIndexToDataIndex.Length)
                        {

                            if (e.Modifiers == Keys.Shift)
                            {
                                var oldSelectedIndex = SelectedIndex;

                                // 選択解除を行う実装
                                var dir = nextShowIndex > currentShowIndex ? 1 : -1;
                                var stop = nextShowIndex + dir;
                                var nextDataIndex = showIndexToDataIndex[currentShowIndex];
                                var nextIndexIndex = IndexOnSelection(nextDataIndex);
                                var deletePhase = nextIndexIndex >= 0;
                                using (var g = CreateGraphics())
                                {
                                    for (var i = currentShowIndex; i != stop; i += dir)
                                    {
                                        var dataIndex = nextDataIndex;
                                        //var indexIndex = nextIndexIndex; // 一つ前でデリートされている可能性がある
                                        var indexIndex = deletePhase && nextIndexIndex > 0 && selectedIndices[nextIndexIndex - 1] == dataIndex ? nextIndexIndex - 1 : nextIndexIndex;
                                        var i2 = i + dir;
                                        if (i2 != stop)
                                        {
                                            nextDataIndex = showIndexToDataIndex[i2];
                                            nextIndexIndex = IndexOnSelection(nextDataIndex);
                                            deletePhase = nextIndexIndex >= 0;
                                        }
                                        else
                                        {
                                            deletePhase = false;
                                        }
                                        setSelectedWithoutEvent(dataIndex, !deletePhase, indexIndex, g);
                                    }
                                }

                                // 選択解除を行わない実装

                                var se = new SelectedIndexChangedEventArgs(oldSelectedIndex, SelectedIndex);
                                SelectedIndexChanged?.Invoke(this, se);
                            }
                            else
                            {
                                SelectedIndex = showIndexToDataIndex[nextShowIndex];
                            }
                            ScrollBarToIndexWithMinimalMove(nextShowIndex);


                            focusedIndex = showIndexToDataIndex[nextShowIndex];
                        }
                    }
                }
            }
            base.OnPreviewKeyDown(e);
        }

        public event GraphicalListDragEventHandler GraphicalListDragEnter;
        public event GraphicalListDragEventHandler GraphicalListDragOver;
        public event EventHandler GraphicalListDragLeave;
        public event GraphicalListDragEventHandler GraphicalListDragDrop;

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            if (GraphicalListDragEnter != null)
            {
                var e = new GraphicalListDragEventArgs(drgevent);
                GraphicalListDragEnter(this, e);
                e.SetTo(drgevent);
            }
            base.OnDragEnter(drgevent);
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            if (GraphicalListDragOver != null)
            {
                var e = new GraphicalListDragEventArgs(drgevent, PointToShowIndex(PointToClient(new Point(drgevent.X, drgevent.Y)), getNearestIndex: false));
                GraphicalListDragOver(this, e);
                e.SetTo(drgevent);
            }
            base.OnDragOver(drgevent);
        }

        protected override void OnDragLeave(EventArgs e)
        {
            GraphicalListDragLeave?.Invoke(this, e);
            base.OnDragLeave(e);
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            if (GraphicalListDragDrop != null)
            {
                var e = new GraphicalListDragEventArgs(drgevent, PointToShowIndex(PointToClient(new Point(drgevent.X, drgevent.Y)), getNearestIndex: false));
                GraphicalListDragDrop(this, e);
                e.SetTo(drgevent);
            }
            base.OnDragDrop(drgevent);
        }

        public void DrawHighlight(int index)
        {
            if (index >= 0 && showIndexToDataIndex != null && showIndexToDataIndex.Length > index)
            {
                var dIndex = showIndexToDataIndex[index];
                if (dIndex >= 0)
                {
                    var tempForeColor = selectedForeColor;
                    var tempFrameColor = frameColor;
                    var tempBrush = frameBrush;
                    selectedForeColor = SystemColors.HighlightText;
                    frameColor = SystemColors.Highlight;
                    frameBrush = SystemBrushes.Highlight;
                    DrawItem(dIndex, selected: true, onlyFrame: true);
                    selectedForeColor = tempForeColor;
                    frameColor = tempFrameColor;
                    frameBrush = tempBrush;
                }
            }
        }

        public void Invalidate(int index)
        {
            if (index >= 0 && showIndexToDataIndex != null && showIndexToDataIndex.Length > index)
            {
                var dIndex = showIndexToDataIndex[index];
                if (dIndex >= 0)
                {
                    items[dIndex].DrawItem(onlyFrame: true);
                }
            }
        }
    }
}
