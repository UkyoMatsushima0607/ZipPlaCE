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
        bool cbSortBy_SelectedValueChanged_Stop = false;

        SortMode cbSortBy_SelectedValueChanged_prevSortByIndex = 0;

        private void cbSortBy_SelectedValueChanged(object sender, EventArgs e)
        {
            if (cbSortBy_SelectedValueChanged_Stop) return;
            var sortModeIndex = cbSortBy.SelectedIndex;
            if (sortModeIndex < 0 || sortModeIndex > (int)SortMode.Random) return;

            if (cbSortBy_SelectedValueChanged_prevSortByIndex == SortMode.Random)
            {
                prevSortMode = 0;
            }
            else if (cbSortBy_SelectedValueChanged_prevSortByIndex != SortMode.RatingInAsc && cbSortBy_SelectedValueChanged_prevSortByIndex != SortMode.RatingInDesc)
            {
                prevSortMode = cbSortBy_SelectedValueChanged_prevSortByIndex;
            }
            cbSortBy_SelectedValueChanged_prevSortByIndex = sortMode = (SortMode)sortModeIndex;
            pendingVolatileSort = null; // 列での並べ替えを待っていたらやめる（K33）

            // ここで先にファイルリストのソート矢印の向きを変更することもできるがエクスプローラーに合わせてそれは行わない
            var success = false;
            if (sortMode != SortMode.Random)
            {
                lock (ZipPathArrayLocker)
                {
                    if (ZipPathArray != null)
                    {
                        tvCatalog.ShowIndexToDataIndex = GetSortArray(preSortArray: tvCatalog.ShowIndexToDataIndex);
                        success = true;
                    }
                }
                if (success)
                {
                    setFileListFromThumbnailViewer();
                    var si = tvCatalog.SelectedIndex;
                    if (si >= 0)
                    {
                        //tvCatalog.ScrollBarToIndex(si);
                        tvCatalog.ScrollBarToIndexWithMinimalMove(tvCatalog.DataIndexToShowIndex[si]);
                    }
                    else
                    {
                        var s2d = tvCatalog.ShowIndexToDataIndex;
                        if (s2d.Length > 0)
                        {
                            tvCatalog.ScrollBarToIndex(s2d[0]);
                        }
                    }
                }
            }
            else// if (!cbSortBy_DropDownClosed_Resorted)
            {
                lock (ZipPathArrayLocker)
                {
                    if (ZipPathArray != null)
                    {
                        tvCatalog.SelectedIndex = -1; // プロパティの set の処理において冗長な描画が入るが、影響の小ささから可読性を優先。
                        GetSortArray_RandomSeed = null;
                        //GetSortArray_RandomOrder = null;
                        tvCatalog.ShowIndexToDataIndex = GetSortArray(preSortArray: null); // ランダムは安定ソート不要
                        success = true;
                    }
                }
                if (success)
                {
                    setFileListFromThumbnailViewer();
                    var s2d = tvCatalog.ShowIndexToDataIndex;
                    if (s2d.Length > 0)
                    {
                        tvCatalog.ScrollBarToIndex(s2d[0]);
                    }
                }
            }
            //cbSortBy_DropDownClosed_Resorted = false;
        }

        private bool notNeedToIgnoreMouseMoveOnRating() { return !mgFileList.InGesturing; }// 右クリックメニュー対応はクラス内で && (MouseButtons & MouseButtons.Right) != MouseButtons.Right && !cmsRightClick.Visible; }

        // K6: ファイル一覧は仮想モード。セルは値を持たず、表示する行の文字列だけをデータのインデックスごとに作って持つ（fileListRowInfos）
        private sealed class FileListRowInfo
        {
            public readonly string[] Texts; // 列の Index ごとの文字列
            public int Rating;
            public string Path;
            public bool IsDir;
            public DateTime LastWriteTime;
            public bool IconResolved;
            public Bitmap Icon;
            public string IconToolTip;
            public string TagName;
            public Tuple<int, int, string>[] TagTrios;
            public string NameToolTip;
            public FileListRowInfo(int columnCount) { Texts = new string[columnCount]; }
        }

        private sealed class FileListRatingHover
        {
            public int Row = -1;
            public int TemporaryRating = -1;
            public Rectangle StarsRectangle = Rectangle.Empty;
            public bool ToolTipMaking = false;
            public int ToolTipRow = -1;
        }

        private readonly FileListRatingHover fileListRatingHover = new FileListRatingHover();

        private FileListRowInfo[] fileListRowInfos = new FileListRowInfo[0];

        private string[] fileListRowInfosSource = null; // fileListRowInfos を作ったときの ZipPathArray

        private int fileListEditingShowIndex = -1; // 名前の変更中に、ファイル名の列に拡張子を除いた名前を出す行

        private string fileListEditingText = null;

        // 揮発性の列（ページ数・解像度など）で並べ替えたときの、その列の値。値が変わったら並べ替えの矢印を消す（表示していない行の判定用）
        private DataGridViewColumn fileListVolatileSortColumn = null;

        private string[] fileListVolatileSortSnapshot = null;

        private FileListRowInfo[] getFileListRowInfoArray(string[] zpa)
        {
            if (fileListRowInfosSource != zpa || fileListRowInfos.Length != zpa.Length)
            {
                fileListRowInfos = new FileListRowInfo[zpa.Length];
                fileListRowInfosSource = zpa;
            }
            return fileListRowInfos;
        }

        private void clearFileListRowInfos()
        {
            fileListRowInfos = new FileListRowInfo[0];
            fileListRowInfosSource = null;
        }

        /// <summary>表示上の行の情報。まだ作っていなければ作る。行がデータと対応していなければ null</summary>
        private FileListRowInfo getFileListRowInfo(int showIndex)
        {
            if (CreatingZipPathArray) return null;
            var s2d = tvCatalog.ShowIndexToDataIndex;
            var zpa = ZipPathArray;
            if (zpa == null || s2d == null || showIndex < 0 || showIndex >= s2d.Length) return null;
            var index = s2d[showIndex];
            if (index < 0 || index >= zpa.Length) return null;
            var infos = getFileListRowInfoArray(zpa);
            var info = infos[index];
            if (info == null)
            {
                info = new FileListRowInfo(dgvFileList.Columns.Count);
                try
                {
                    setFileListRowInLock(info, index, dynamicOnly: false, localOnly: false, cultureForOnlyStatic: Message.CurrentLanguage, volatilitySortCancelIfChanged: false);
                }
                catch (IndexOutOfRangeException)
                {
                    return null; // 配列の差し替えの途中
                }
                infos[index] = info;
            }
            return info;
        }

        private int getFileListRating(int showIndex)
        {
            var info = getFileListRowInfo(showIndex);
            return info == null ? 0 : info.Rating;
        }

        private void dgvFileList_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            var columnIndex = e.ColumnIndex;
            if (columnIndex == tbcRating.Index)
            {
                e.Value = RatingGridViewTextBoxCell.StarsText; // 幅の自動調整用。描画は RatingGridViewTextBoxCell が行う
                return;
            }
            if (columnIndex == tbcFileName.Index && e.RowIndex == fileListEditingShowIndex && fileListEditingText != null)
            {
                e.Value = fileListEditingText;
                return;
            }
            var info = getFileListRowInfo(e.RowIndex);
            if (info == null) return;
            if (columnIndex == tbcFileName.Index) // 表示する行の後回しにしたヘッダを取る（K33）。名前の列は常に表示されている
            {
                var s2d = tvCatalog.ShowIndexToDataIndex;
                if (s2d != null && e.RowIndex >= 0 && e.RowIndex < s2d.Length) requestDeferredHeader(s2d[e.RowIndex]);
            }
            if (columnIndex == tbcIcon.Index) e.Value = info.Path;
            else if (columnIndex >= 0 && columnIndex < info.Texts.Length) e.Value = info.Texts[columnIndex];
        }

        private void dgvFileList_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == tbcIcon.Index)
            {
                e.ToolTipText = getFileListRowInfo(e.RowIndex)?.IconToolTip ?? "";
            }
            else if (e.ColumnIndex == tbcFileName.Index)
            {
                e.ToolTipText = getFileListRowInfo(e.RowIndex)?.NameToolTip;
            }
        }

        /// <summary>表示上の行の 1 つの列の文字列を差し替える。行の情報をまだ作っていなければ、表示するときに配列から作られるので何もしない</summary>
        private void setFileListText(int showIndex, DataGridViewColumn column, string text)
        {
            var s2d = tvCatalog.ShowIndexToDataIndex;
            var zpa = ZipPathArray;
            if (zpa == null || s2d == null || showIndex < 0 || showIndex >= s2d.Length || showIndex >= dgvFileList.RowCount) return;
            var index = s2d[showIndex];
            if (index < 0 || index >= zpa.Length) return;
            var info = getFileListRowInfoArray(zpa)[index];
            if (info != null) info.Texts[column.Index] = text;
            dgvFileList.InvalidateCell(column.Index, showIndex);
        }

        private bool isDynamicFileListColumn(DataGridViewColumn column)
        {
            return column == tbcPage || column == tbcResolution || column == tbcRatio || column == tbcWidth || column == tbcHeight || column == tbcLength || column == tbcFPS;
        }

        private bool setFileListRoDynamicPartForIndex(FileListRowInfo row, int index, bool volatilitySortCancelIfChanged)
        {
            return setFileListRoDynamicPart(row, ZipNameArray[index], ZipPageArray[index], FileCountArray[index], ImageInfoArray[index], MovieInfoArray[index], volatilitySortCancelIfChanged);
        }

        private void makeFileListVolatileSortSnapshot()
        {
            fileListVolatileSortColumn = null;
            fileListVolatileSortSnapshot = null;
            DataGridViewColumn column = null;
            foreach (DataGridViewColumn c in dgvFileList.Columns)
            {
                if (c.HeaderCell.SortGlyphDirection != SortOrder.None && isDynamicFileListColumn(c))
                {
                    column = c;
                    break;
                }
            }
            var zpa = ZipPathArray;
            if (column == null || zpa == null || CreatingZipPathArray) return;
            var snapshot = new string[zpa.Length];
            var temp = new FileListRowInfo(dgvFileList.Columns.Count);
            try
            {
                for (var i = 0; i < snapshot.Length; i++)
                {
                    setFileListRoDynamicPartForIndex(temp, i, volatilitySortCancelIfChanged: false);
                    snapshot[i] = temp.Texts[column.Index];
                }
            }
            catch (IndexOutOfRangeException)
            {
                return;
            }
            fileListVolatileSortColumn = column;
            fileListVolatileSortSnapshot = snapshot;
        }

        /// <summary>行の情報を作っていない行の揮発性の値が、並べ替えたときから変わっていたら並べ替えの矢印を消す</summary>
        private void checkFileListVolatileSortSnapshot(int index)
        {
            var column = fileListVolatileSortColumn;
            var snapshot = fileListVolatileSortSnapshot;
            if (column == null || snapshot == null) return;
            if (column.HeaderCell.SortGlyphDirection == SortOrder.None || index >= snapshot.Length)
            {
                fileListVolatileSortColumn = null;
                fileListVolatileSortSnapshot = null;
                return;
            }
            var temp = new FileListRowInfo(dgvFileList.Columns.Count);
            setFileListRoDynamicPartForIndex(temp, index, volatilitySortCancelIfChanged: false);
            if ((temp.Texts[column.Index] ?? "") != (snapshot[index] ?? ""))
            {
                column.HeaderCell.SortGlyphDirection = SortOrder.None;
                fileListVolatileSortColumn = null;
                fileListVolatileSortSnapshot = null;
            }
        }

        private void setFileListFromThumbnailViewer(bool tryToKeepScroll = false, bool volatilitySort = false)
        {
#if PERFLOG
            var perfFileListStart = Stopwatch.GetTimestamp();
#endif
            var temp = selectionSynchronizing;
            selectionSynchronizing = true;
            var itemsCount = tvCatalog.ShowIndexToDataIndex.Length;
            var lastItemsCount = dgvFileList.RowCount;
            clearFileListRowInfos();
            var val = dgvFileList.FirstDisplayedScrollingRowIndex;
            if (itemsCount != lastItemsCount)
            {
                // 仮想モードで行数を減らすと 1 行ずつ消すので遅い（3 万行から 5000 行減らして 0.4 秒）。いったん空にしてから足す
                if (itemsCount < lastItemsCount) dgvFileList.Rows.Clear();
                dgvFileList.RowCount = itemsCount;
            }
            else
            {
                dgvFileList.Invalidate();
            }

            if (val >= 0 && val < itemsCount)
            {
                dgvFileList.FirstDisplayedScrollingRowIndex = val;
            }
            if (!volatilitySort)
            {
                fileListVolatileSortColumn = null;
                fileListVolatileSortSnapshot = null;
                setNonVolatilitySort();
            }
            else
            {
                makeFileListVolatileSortSnapshot();
            }

            selectionSynchronizing = temp;

            selectionSynchronizingFromThumbnailToFileList(changeScroll: !tryToKeepScroll);

            // サムネイル非表示の場合にヘッダ読み込みが終わるまで表示が更新されない問題の解消
            dgvFileList.Update();
#if PERFLOG
            PerfTrace.Write($"fileList rebuild items={itemsCount} last={lastItemsCount} visible={dgvFileList.Visible} {(Stopwatch.GetTimestamp() - perfFileListStart) * 1000.0 / Stopwatch.Frequency:F1} ms");
#endif
        }

        private bool setFileListRowInLock(int index, bool dynamicOnly, bool localOnly, CultureInfo cultureForOnlyStatic, bool volatilitySortCancelIfChanged)
        {
            if (index < 0) return false;
            if (CreatingZipPathArray) return false;
            var d2s = tvCatalog.DataIndexToShowIndex;
            if (index >= d2s.Length) return false;
            var showIndex = d2s[index];
            if (showIndex < 0 || showIndex >= dgvFileList.Rows.Count) return false;
            var zpa = ZipPathArray;
            if (zpa == null || index >= zpa.Length) return false;
            var info = getFileListRowInfoArray(zpa)[index];
            if (info == null)
            {
                // 表示したことのない行は表示するときに配列から作る。並べ替えの矢印を消すかの判定だけ行う
                if (volatilitySortCancelIfChanged && !localOnly) checkFileListVolatileSortSnapshot(index);
                return false;
            }
            if (cultureForOnlyStatic == null) cultureForOnlyStatic = Message.CurrentLanguage;
            var result = setFileListRowInLock(info, index, dynamicOnly, localOnly, cultureForOnlyStatic, volatilitySortCancelIfChanged);
            dgvFileList.InvalidateRow(showIndex);
            return result;
        }

        private bool setFileListRowInLock(FileListRowInfo row, int index, bool dynamicOnly, bool localOnly, CultureInfo cultureForOnlyStatic, bool volatilitySortCancelIfChanged)
        {
            var name = ZipNameArray[index];
            var lwt = ModifiedTimeArray[index];
            var result = false;
            if (!dynamicOnly)
            {
                if (!localOnly)
                {
                    var path = ZipPathArray[index];
                    if (setFileListRowStaticGlobalPart(row, path, name, lwt))
                    {
                        result = true;
                    }
                }
                if (setFileListRowStaticLocalPart(row, name, ZipSizeArray[index], CreateTimeArray[index], LastAccessTimeArray[index], ModifiedTimeArray[index], cultureForOnlyStatic))
                {
                    result = true;
                }
            }

            if (!localOnly)
            {

                if (setFileListRoDynamicPart(row, name, ZipPageArray[index], FileCountArray[index], ImageInfoArray[index], MovieInfoArray[index], volatilitySortCancelIfChanged))
                {
                    result = true;
                }
            }
            return result;
        }

        private bool setFileListRowStaticGlobalPart(FileListRowInfo row, string path, string name, DateTime lastWriteTime)
        {
            row.IsDir = name != null && name.Length > 0 && name[name.Length - 1] == Path.DirectorySeparatorChar;
            row.LastWriteTime = lastWriteTime;
            row.Path = path;
            row.IconResolved = false; // アイコンと種類の名前は描画するときに取得する
            row.Icon = null;
            row.IconToolTip = null;
            row.Texts[tbcFileName.Index] = name;
            row.TagName = null;
            row.TagTrios = null;
            row.Rating = Math.Max(0, Math.Min(5, ZipPlaInfo.GetOnlyRating(name)));
            return false;
        }

        private bool setFileListRowStaticLocalPart(FileListRowInfo row, string name, long size, DateTime cTime, DateTime aTime, DateTime mTime, CultureInfo culture) // size ソートを揮発性にしたくないので static 扱い
        {
            row.Texts[tbcCreateTime.Index] = getShortTimeString(cTime, culture);
            row.Texts[tbcAccessTime.Index] = getShortTimeString(aTime, culture);
            row.Texts[tbcWriteTime.Index] = getShortTimeString(mTime, culture);
            row.Texts[tbcSize.Index] = getFileSizeStringInFileListLocal(name, size);
            return false;
        }

        private string getFileSizeStringInFileListLocal(string name, long size)
        {
            if (name[name.Length - 1] != Path.DirectorySeparatorChar && size >= 0)
            // 一つ目の条件がないとフォルダサイズ取得後に言語設定を変更するとサイズが表示されてしまう
            {
                return Program.GetFormatSizeString(size, Message.Bytes, simpleForm: true);
            }
            else
            {
                return EmptyPageString;
            }
        }

        private const string setFileListRoDynamicPart_LengthFormat = @"h\:mm\:ss";

        private delegate void setFileListRoDynamicPart_SetterDelegate(ref bool changed, FileListRowInfo row, DataGridViewColumn column, string src);

        private bool setFileListRoDynamicPart(FileListRowInfo row, string name, int cPage, int tPage, ImageInfo iInfo, MovieInfo mInfo, bool volatilitySortCancelIfChanged)
        {
            var changed = false;

            var setter = volatilitySortCancelIfChanged ? (setFileListRoDynamicPart_SetterDelegate)substitutionWithCheckForCellAndRemoveSortGlyphDirection : substitutionWithCheckForCell;

            setter(ref changed, row, tbcPage, getPageString(cPage, tPage));
            //if(InArchive || ImageLoader.SupportsAtLeastThumbnailReading(name))
            var ffmpegExists = null as bool?;
            var extensionWithPeriodInLower = null as string;
            if (InArchive || ImageLoader.SupportsGetImageInfoByExtensionWithPeriodInLower(extensionWithPeriodInLower = ImageLoader.GetExtensionWithPeriodInLowerSafety(name), ref ffmpegExists))
            {
                Size size;
                if (iInfo != null && !(size = iInfo.Size).IsEmpty)
                {
                    setter(ref changed, row, tbcResolution, size.Width + "x" + size.Height);
                    setter(ref changed, row, tbcRatio, getRatioString(size));
                    setter(ref changed, row, tbcWidth, size.Width.ToString());
                    setter(ref changed, row, tbcHeight, size.Height.ToString());
                }
                else if (InArchive || ImageLoader.SupportsImageAtLeastThumbnailReadingByExtension(
                    extensionWithPeriodInLower ?? ImageLoader.GetExtensionWithPeriodInLowerSafety(name), ref ffmpegExists))
                {
                    setter(ref changed, row, tbcResolution, QuestionPageString);
                    setter(ref changed, row, tbcRatio, QuestionPageString);
                    setter(ref changed, row, tbcWidth, QuestionPageString);
                    setter(ref changed, row, tbcHeight, QuestionPageString);
                }
                else
                {
                    setter(ref changed, row, tbcResolution, EmptyPageString);
                    setter(ref changed, row, tbcRatio, EmptyPageString);
                    setter(ref changed, row, tbcWidth, EmptyPageString);
                    setter(ref changed, row, tbcHeight, EmptyPageString);
                }
                setter(ref changed, row, tbcLength, EmptyPageString);
                setter(ref changed, row, tbcFPS, EmptyPageString);
            }
            else if (InMovie || MovieThumbnailLoader.SupportsByExtension(extensionWithPeriodInLower ?? ImageLoader.GetExtensionWithPeriodInLowerSafety(name)))
            {
                if (mInfo != null)
                {
                    var size = mInfo.SizeByPixel;
                    if (!size.IsEmpty)
                    {
                        setter(ref changed, row, tbcResolution, size.Width + "x" + size.Height);
                        setter(ref changed, row, tbcRatio, getRatioString(size, mInfo.SAR));
                        setter(ref changed, row, tbcWidth, size.Width.ToString());
                        setter(ref changed, row, tbcHeight, size.Height.ToString());
                    }
                    else
                    {
                        setter(ref changed, row, tbcResolution, QuestionPageString);
                        setter(ref changed, row, tbcRatio, QuestionPageString);
                        setter(ref changed, row, tbcWidth, QuestionPageString);
                        setter(ref changed, row, tbcHeight, QuestionPageString);
                    }
                    var length = mInfo.Duration;
                    if (length != default(TimeSpan))
                    {
                        setter(ref changed, row, tbcLength, length.ToString(setFileListRoDynamicPart_LengthFormat));
                    }
                    else
                    {
                        setter(ref changed, row, tbcLength, QuestionPageString);
                    }
                    var fps = mInfo.FPSString;
                    if (!string.IsNullOrEmpty(fps))
                    {
                        setter(ref changed, row, tbcFPS, fps);
                    }
                    else
                    {
                        setter(ref changed, row, tbcFPS, QuestionPageString);
                    }
                }
                else
                {
                    setter(ref changed, row, tbcResolution, QuestionPageString);
                    setter(ref changed, row, tbcRatio, QuestionPageString);
                    setter(ref changed, row, tbcWidth, QuestionPageString);
                    setter(ref changed, row, tbcHeight, QuestionPageString);
                    setter(ref changed, row, tbcLength, QuestionPageString);
                    setter(ref changed, row, tbcFPS, QuestionPageString);
                }
            }
            else
            {
                setter(ref changed, row, tbcResolution, EmptyPageString);
                setter(ref changed, row, tbcRatio, EmptyPageString);
                setter(ref changed, row, tbcWidth, EmptyPageString);
                setter(ref changed, row, tbcHeight, EmptyPageString);
                setter(ref changed, row, tbcLength, EmptyPageString);
                setter(ref changed, row, tbcFPS, EmptyPageString);
            }
            if (ffmpegExists != null) RecentFfmpegExists = (bool)ffmpegExists;
            return changed;
        }

        private void substitutionWithCheckForCellAndRemoveSortGlyphDirection(ref bool changed, FileListRowInfo row, DataGridViewColumn column, string src)
        {
            var dstString = row.Texts[column.Index];
            if (dstString == null) dstString = "";
            if (src == null) src = "";
            if (dstString != src)
            {
                var header = column.HeaderCell;
                if (header.SortGlyphDirection != SortOrder.None)
                {
                    header.SortGlyphDirection = SortOrder.None;
                    if (column == fileListVolatileSortColumn)
                    {
                        fileListVolatileSortColumn = null;
                        fileListVolatileSortSnapshot = null;
                    }
                }

                changed = true;
                row.Texts[column.Index] = src;
            }
        }

        private void substitutionWithCheckForCell(ref bool changed, FileListRowInfo row, DataGridViewColumn column, string src)
        {
            var dstString = row.Texts[column.Index];
            if (dstString == null) dstString = "";
            if (src == null) src = "";
            if (dstString != src)
            {
                changed = true;
                row.Texts[column.Index] = src;
            }
        }

        private const string getRatioString_MaxWidthTemplate = "1.11:1"; // 比が 1000:1 以上になるとこの範疇では収まらないが無視する

        private string getRatioString(Size size)
        {
            var w = Math.Max(0, size.Width);
            var h = Math.Max(0, size.Height);
            if (w == 0 && h == 0) return "0:0";
            if (h == 0) return "1:0";
            if (w == 0) return "0:1";
            if (w == h) return "1:1";
            if (w > h)
            {
                return toFourChars(Math.Max((double)w / h, 1.01)) + ":1";
            }
            else
            {
                return $"1:" + toFourChars(Math.Max((double)h / w, 1.01));
            }
        }

        private string toFourChars(double x)
        {
            // 小数点記号の利用を避ける
            if (x < 100)
            {
                return x.ToString("F2").Substring(0, 4);
            }
            else
            {
                return ((int)Math.Round(x)).ToString();
            }
        }

        private string getRatioString(Size size, Fraction SAR)
        {
            if (SAR == 0)
            {
                return getRatioString(size);
            }
            if (size.Height != 0)
            {
                return getRatioString((Fraction)size * SAR);
            }
            else if (size.Width != 0)
            {
                return "1:0";
            }
            else
            {
                return "0:0";
            }
        }

        private const string EmptyPageString = "-";

        private const string QuestionPageString = "?";

        private string getPageString(int page, int total)
        {
            if (total == MetaPageCount_NotBook)
            {
                return EmptyPageString;
            }
            else if (total == MetaPageCount_NotLoaded)
            {
                return QuestionPageString;
            }
            else if (page >= 0 && total >= 0)
            {
                return $"{page + 1}/{total}";
            }
            else if (page >= 0)
            {
                return $"{page + 1}/{QuestionPageString}";
            }
            else if (total >= 0)
            {
                return total.ToString();
            }
            else
            {
                return QuestionPageString;
            }
        }

        private void RatingGridViewTextBoxCell_RatingClick(RatingGridViewTextBoxCell sender, RatingClickEventArgs e)
        {
            var index = e.RowIndex;
            if (index < 0) return;
            var s2d = tvCatalog.ShowIndexToDataIndex;
            if (index >= s2d.Length) return;
            index = s2d[index];
            if (1 <= e.NewRating && e.NewRating <= 5)
            {
                SetRating(e.NewRating, index);
            }
            else
            {
                SetRating(null, index);
            }
        }

        class DataGridViewIconCell : DataGridViewTextBoxCell
        {
            // K6: 行は共有されるので、セルは行ごとの状態を持たない。アイコンと種類の名前は FileListRowInfo に持ち、描画するときに取得する

            private static string fileFolderString = null;
            private static string GetFileFolderString()
            {
                if (fileFolderString == null)
                {
                    try
                    {
                        fileFolderString = FileTypeManager.GetTypeName(SmartStaticIconProvider.DefaultDirectoryPath, useFileAttrinutes: false);
                    }
                    catch
                    {
                        fileFolderString = "";
                    }
                }
                return fileFolderString;
            }

            protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
            {
                var form = DataGridView?.FindForm() as CatalogForm;
                var info = form?.getFileListRowInfo(rowIndex);
                if (info != null && !info.IconResolved)
                {
                    info.IconResolved = true;
                    var path = info.Path;
                    if (path != null)
                    {
                        try
                        {
                            var typeName = info.IsDir ? GetFileFolderString() : FileTypeManager.GetTypeName(path, useFileAttrinutes: true);
                            info.IconToolTip = typeName ?? "";
                            info.Icon = SmartStaticIconProvider.GetSmall(path, info.LastWriteTime, info.IsDir);
                        }
                        catch { }
                    }
                }

                base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts & ~DataGridViewPaintParts.ContentForeground);

                var iconImage = info?.Icon;
                if (iconImage != null)
                {
                    DrawImageUnscaledCenteringAndCliped(graphics, iconImage, cellBounds);

                    var maskAlpha = info.Path == null ? 0 : form.GetMaskAlpha(info.Path);
                    if (maskAlpha > 0)
                    {
                        if ((elementState & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected)
                        {
                            graphics.FillRectangle(getColorMask(maskAlpha, cellStyle.SelectionBackColor), cellBounds);
                        }
                        else
                        {
                            graphics.FillRectangle(getWhiteMask(maskAlpha, cellStyle.BackColor), cellBounds);
                        }
                    }
                }
            }


            private static Brush WhiteMask;
            private static Brush getWhiteMask(int alpha, Color color)
            {
                if (WhiteMask == null)
                {
                    WhiteMask = new SolidBrush(Color.FromArgb(alpha, color));
                }
                return WhiteMask;
            }

            private static Brush ColorMask;
            private static Brush getColorMask(int alpha, Color color)
            {
                if (ColorMask == null)
                {
                    ColorMask = new SolidBrush(Color.FromArgb(alpha, color));
                }
                return ColorMask;
            }

            private static void DrawImageUnscaledCenteringAndCliped(Graphics g, Bitmap src, Rectangle dstRect)
            {
                var innerRect = new Rectangle(
                        dstRect.X + (int)Math.Floor((dstRect.Width - src.Width + 1) / 2.0),
                        dstRect.Y + (int)Math.Floor((dstRect.Height - src.Height + 1) / 2.0),
                        src.Width, src.Height);
                if (dstRect.Contains(innerRect))
                {
                    g.DrawImageUnscaled(src, innerRect);
                }
                else
                {
                    var temp = g.InterpolationMode;
                    var notNearestNeighbor = temp != System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    if (notNearestNeighbor)
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    }

                    var width = Math.Min(src.Width, dstRect.Width);
                    var height = Math.Min(src.Height, dstRect.Height);

                    var srcRect = new Rectangle(
                        (src.Width - width + 1) / 2,
                        (src.Height - height + 1) / 2,
                        width, height);

                    dstRect.Intersect(innerRect);

                    g.DrawImage(src, dstRect, srcRect, GraphicsUnit.Pixel);

                    if (notNearestNeighbor)
                    {
                        g.InterpolationMode = temp;
                    }
                }

            }

        }

        class RatingClickEventArgs : EventArgs { public int NewRating; public int RowIndex; }

        delegate void RatingClickEventHandler(RatingGridViewTextBoxCell sender, RatingClickEventArgs e);

        class RatingGridViewTextBoxCell : DataGridViewTextBoxCell
        {
            public const string StarsText = "★★★★★";

            // K6: 行は共有され、セルは行ごとに作られないので、マウスを乗せている行と仮の評価はフォームの fileListRatingHover に持つ
            private CatalogForm OwnerForm => DataGridView?.FindForm() as CatalogForm;

            private static bool IsActive(CatalogForm form) => form != null && form.InDirectoryVirtualDirectoryOrSmartDirectory;

            private void InvalidateRowCell(int rowIndex)
            {
                var dataGridView = DataGridView;
                if (dataGridView != null && rowIndex >= 0 && rowIndex < dataGridView.RowCount && ColumnIndex >= 0) dataGridView.InvalidateCell(ColumnIndex, rowIndex);
            }

            protected override void OnMouseMove(DataGridViewCellMouseEventArgs e)
            {
                var form = OwnerForm;
                if (IsActive(form))
                {
                    var h = form.fileListRatingHover;
                    if (form.notNeedToIgnoreMouseMoveOnRating())
                    {
                        if (!h.StarsRectangle.IsEmpty)
                        {
                            if (h.Row != e.RowIndex)
                            {
                                var oldRow = h.Row;
                                h.Row = e.RowIndex;
                                if (h.TemporaryRating >= 0)
                                {
                                    h.TemporaryRating = -1;
                                    InvalidateRowCell(oldRow);
                                }
                            }
                            var rating = form.getFileListRating(e.RowIndex);
                            var s = e.X - h.StarsRectangle.Left;
                            var r = Math.Max(1, Math.Min(5, (5 * s / h.StarsRectangle.Width) + 1));
                            if (h.TemporaryRating < 0 && r != rating || h.TemporaryRating >= 0 && r != h.TemporaryRating)
                            {
                                h.TemporaryRating = r;
                                InvalidateRowCell(e.RowIndex);
                            }
                            else
                            {
                                h.TemporaryRating = r;
                            }
                        }
                    }
                    else if (h.TemporaryRating != -1)
                    {
                        h.TemporaryRating = -1;
                        InvalidateRowCell(h.Row);
                    }
                }

                base.OnMouseMove(e);
            }

            protected override void OnMouseDown(DataGridViewCellMouseEventArgs e)
            {
                var form = OwnerForm;
                if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
                {
                    if (IsActive(form) && form.notNeedToIgnoreMouseMoveOnRating())
                    {
                        var h = form.fileListRatingHover;
                        var temporaryRating = h.Row == e.RowIndex ? h.TemporaryRating : -1;
                        var newRating = e.Button == MouseButtons.Left ? temporaryRating : 0;
                        if (DataGridView != null && newRating >= 0 && newRating != form.getFileListRating(e.RowIndex))
                        {
                            form.RatingGridViewTextBoxCell_RatingClick(this, new RatingClickEventArgs { NewRating = newRating, RowIndex = e.RowIndex });
                        }
                    }
                }
                else if ((e.Button & MouseButtons.Right) == MouseButtons.Right)
                {
                    if (form != null)
                    {
                        form.fileListRatingHover.TemporaryRating = -1;
                        InvalidateRowCell(e.RowIndex);
                    }
                }
                base.OnMouseDown(e);
            }

            protected override void OnMouseLeave(int rowIndex)
            {
                var form = OwnerForm;
                if (IsActive(form))
                {
                    var h = form.fileListRatingHover;
                    if (form.notNeedToIgnoreMouseMoveOnRating())
                    {
                        if (form.getFileListRating(rowIndex) != h.TemporaryRating)
                        {
                            h.TemporaryRating = -1;
                            InvalidateRowCell(rowIndex);
                        }
                        else
                        {
                            h.TemporaryRating = -1;
                        }
                    }
                    else if (h.TemporaryRating != -1)
                    {
                        h.TemporaryRating = -1;
                        InvalidateRowCell(rowIndex);
                    }
                    if (h.Row == rowIndex) h.Row = -1;
                }
                closeToolTip(form, DataGridView, ColumnIndex, onlyIfMouseLeave: true);
                base.OnMouseLeave(rowIndex);
            }

            protected override async void OnMouseEnter(int rowIndex)
            {
                base.OnMouseEnter(rowIndex);

                var dataGridView = DataGridView;
                var cf = dataGridView?.FindForm() as CatalogForm;
                if (cf == null) return;
                var h = cf.fileListRatingHover;
                if (!h.ToolTipMaking)
                {
                    h.ToolTipMaking = true;
                    h.ToolTipRow = rowIndex;
                    var columnIndex = ColumnIndex;
                    var toolTip = cf.toolTip;
                    await Task.Delay(toolTip.InitialDelay);
                    if (h.ToolTipMaking && rowIndex < dataGridView.RowCount)
                    {
                        var CursorPosition = Cursor.Position;
                        var target = cf.pnlRight;
                        var rectangle = target.RectangleToClient(dataGridView.RectangleToScreen(dataGridView.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true)));
                        var point = target.PointToClient(CursorPosition);
                        if (Cursor.Current is Cursor cursor)
                        {
                            point.Y += cursor.Size.Height - cursor.HotSpot.Y; // 予め縦方向にずらす
                        }
                        if (rectangle.Contains(point))
                        {
                            point.Y = rectangle.Bottom;
                        }
                        toolTip.Show(Message.MiddleClickLeftFlickToResetRating, target, point);
                        await Task.Delay(toolTip.AutoPopDelay);
                        closeToolTip(cf, dataGridView, columnIndex, onlyIfMouseLeave: false);
                    }
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    try
                    {
                        closeToolTip(OwnerForm, DataGridView, ColumnIndex, onlyIfMouseLeave: false);
                    }
                    catch { }
                }
                base.Dispose(disposing);
            }

            private static void closeToolTip(CatalogForm cf, DataGridView dataGridView, int columnIndex, bool onlyIfMouseLeave)
            {
                if (cf == null) return;
                var h = cf.fileListRatingHover;
                if (onlyIfMouseLeave)
                {
                    if (dataGridView == null)
                    {
                        h.ToolTipMaking = false;
                        return;
                    }
                    var row = h.ToolTipRow;
                    onlyIfMouseLeave = row >= 0 && row < dataGridView.RowCount && columnIndex >= 0 &&
                        dataGridView.GetCellDisplayRectangle(columnIndex, row, cutOverflow: true).Contains(dataGridView.PointToClient(Cursor.Position));
                }
                if (!onlyIfMouseLeave && h.ToolTipMaking)
                {
                    h.ToolTipMaking = false;
                    cf.toolTip.Hide(cf.pnlRight);
                }
            }

            protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates cellState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
            {
                var form = OwnerForm;
                var h = form?.fileListRatingHover;
                var r = h != null && h.Row == rowIndex && h.TemporaryRating >= 0 ? h.TemporaryRating : form?.getFileListRating(rowIndex) ?? 0;
                if (r < 0) r = 0;


                //境界線の内側に範囲を取得する
                Rectangle borderRect = this.BorderWidths(advancedBorderStyle);
                Rectangle paintRect = new Rectangle(
                    cellBounds.Left + borderRect.Left,
                    cellBounds.Top + borderRect.Top,
                    cellBounds.Width - borderRect.Right,
                    cellBounds.Height - borderRect.Bottom);

                //Paddingを差し引く
                paintRect.Offset(cellStyle.Padding.Right, cellStyle.Padding.Top);
                paintRect.Width -= cellStyle.Padding.Horizontal;
                paintRect.Height -= cellStyle.Padding.Vertical;


                base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts & ~DataGridViewPaintParts.ContentForeground);


                //テキストを表示する
                if ((paintParts & DataGridViewPaintParts.ContentForeground) ==
                    DataGridViewPaintParts.ContentForeground)
                {
                    bool isSelected =
                       (cellState & DataGridViewElementStates.Selected) ==
                       DataGridViewElementStates.Selected;
                    Color fColor = isSelected ? cellStyle.SelectionForeColor : cellStyle.ForeColor;
                    var isDarkFore = fColor.G < 128;
                    var gold = isDarkFore ? Color.DarkGoldenrod : Color.Gold;
                    var gray = isDarkFore ? Color.LightGray : Color.LightSlateGray;

                    if (sizeNotSet)
                    {
                        sizeNotSet = false;
                        periodSize = TextRenderer.MeasureText(graphics, ".", cellStyle.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding);
                        fullWidth = TextRenderer.MeasureText(graphics, "★★★★★", cellStyle.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width;
                    }

                    var periodWidth = periodSize.Width;// TextRenderer.MeasureText(graphics, ".", cellStyle.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width;
                    //var fullSize = TextRenderer.MeasureText(graphics, "★★★★★", cellStyle.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding);
                    var preStr = "★★★★★".Substring(0, r);
                    var sufStr = "★★★★★".Substring(r);
                    //var height = paintRect.Height;
                    //paintRect.Inflate(-2, -2);
                    paintRect.X += (paintRect.Width - fullWidth) / 2;
                    paintRect.Y += (paintRect.Height - periodSize.Height + 1) / 2;
                    var starsRectangle = paintRect;
                    starsRectangle.X += periodWidth - cellBounds.X;
                    starsRectangle.Width = fullWidth;
                    if (h != null) h.StarsRectangle = starsRectangle;
                    TextRenderer.DrawText(graphics, preStr, cellStyle.Font, paintRect, gold, TextFormatFlags.NoPadding);
                    paintRect.X += TextRenderer.MeasureText(graphics, preStr, cellStyle.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width;
                    TextRenderer.DrawText(graphics, sufStr, cellStyle.Font, paintRect, gray, TextFormatFlags.NoPadding);
                }

                //base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
            }

            private bool sizeNotSet = true;
            private Size periodSize;
            private int fullWidth;
        }

        class RainbowDataGridViewTextBoxCell : DataGridViewTextBoxCell
        {
            // K6: 行は共有されるので、タグの位置と省略したときのツールチップは FileListRowInfo に持つ

            protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates cellState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
            {
                var name = value == null ? "" : value.ToString();
                var form = DataGridView?.FindForm() as CatalogForm;
                var Tags = form?.Tags;
                var info = form?.getFileListRowInfo(rowIndex);
                Tuple<int, int, string>[] indexLengthTagTorios = null;
                if (Tags != null)
                {
                    if (info == null)
                    {
                        indexLengthTagTorios = (new ZipPlaInfo(name)).GetIndexLengthTagTorios();
                    }
                    else
                    {
                        if (info.TagName != name)
                        {
                            info.TagName = name;
                            info.TagTrios = (new ZipPlaInfo(name)).GetIndexLengthTagTorios();
                        }
                        indexLengthTagTorios = info.TagTrios;
                    }
                }

                //境界線の内側に範囲を取得する
                Rectangle borderRect = this.BorderWidths(advancedBorderStyle);
                Rectangle paintRect = new Rectangle(
                    cellBounds.Left + borderRect.Left,
                    cellBounds.Top + borderRect.Top,
                    cellBounds.Width - borderRect.Right,
                    cellBounds.Height - borderRect.Bottom);

                //Paddingを差し引く
                paintRect.Offset(cellStyle.Padding.Right, cellStyle.Padding.Top);
                paintRect.Width -= cellStyle.Padding.Horizontal;
                paintRect.Height -= cellStyle.Padding.Vertical;

                base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts & ~DataGridViewPaintParts.ContentForeground);
                //return;

                //テキストを表示する
                if ((paintParts & DataGridViewPaintParts.ContentForeground) ==
                    DataGridViewPaintParts.ContentForeground)
                {
                    //本来は、cellStyleによりTextFormatFlagsを決定すべき
                    //TextFormatFlags flags = TextFormatFlags.HorizontalCenter |
                    //  TextFormatFlags.VerticalCenter;
                    TextFormatFlags flags = 0 | TextFormatFlags.NoPrefix;//  TextFormatFlags.EndEllipsis;// | TextFormatFlags.VerticalCenter
                                                                         //色を決定
                    bool isSelected =
                        (cellState & DataGridViewElementStates.Selected) ==
                        DataGridViewElementStates.Selected;
                    Color fColor = isSelected ? cellStyle.SelectionForeColor : cellStyle.ForeColor;

                    var periodSize = TextRenderer.MeasureText(graphics, ".", cellStyle.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    //var periodWidth = 0 * periodSize.Width;// TextRenderer.MeasureText(graphics, ".", cellStyle.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;

                    //var charHeight = periodSize.Height;
                    //var hShift = 1;
                    var charHeight = getCharHeight(cellStyle.Font, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    //charHeight = periodSize.Height;

                    var vPadding = Math.Max(0, (paintRect.Height - charHeight) / 4);

                    var paintRectYForBG = paintRect.Y + vPadding;
                    var height = paintRect.Height - vPadding * 2;
                    paintRect.Y += (paintRect.Height - charHeight + 1) / 2;

                    var nameTrimed = ThumbViewer.GetTextBaseNameEndEllipsis(name, cellStyle.Font, paintRect.Width, noPadding: false);
                    if (info != null) info.NameToolTip = nameTrimed == name ? null : name;
                    var tLen = nameTrimed.Length;
                    var tLen0 = Math.Min(nameTrimed.Length, tLen); // 文字幅が均一でないので逆転もありうる
                    var tLen2 = 0;
                    while (tLen2 < tLen0 && name[tLen2] == nameTrimed[tLen2]) tLen2++;
                    if (Tags == null || indexLengthTagTorios == null)
                    {
                        //TextRenderer.DrawText(graphics, name, cellStyle.Font, paintRect, fColor, flags);
                        TextRenderer.DrawText(graphics, nameTrimed, cellStyle.Font, paintRect, fColor, flags);

                    }
                    else
                    {
                        var index = 0;
                        var rectangles = new Rectangle[indexLengthTagTorios.Length * 2];
                        var strs = new string[indexLengthTagTorios.Length * 2];
                        var colors = new Color[indexLengthTagTorios.Length];
                        var i = 0;
                        foreach (var torio in indexLengthTagTorios)
                        {
                            var tagName = torio.Item3;
                            var tag = Tags.FirstOrDefault(t => t.Name == tagName);
                            if (tag == default(ZipTag)) continue;
                            // if( そのタグが描画対象でなければコンティニュー)


                            //var str = name.Substring(index, torio.Item1 - index);
                            var str = nameTrimed.Substring(Math.Min(tLen, index), Math.Max(0, Math.Min(tLen, torio.Item1) - index));
                            var width = TextRenderer.MeasureText(graphics, str, cellStyle.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                            //TextRenderer.DrawText(graphics, str, cellStyle.Font, paintRect, fColor, flags);
                            strs[2 * i] = str;
                            rectangles[2 * i] = paintRect;
                            paintRect.X += width;
                            paintRect.Width -= width;
                            //str = name.Substring(torio.Item1, torio.Item2);
                            //str = nameTrimed.Substring(Math.Min(tLen, torio.Item1), Math.Max(0, Math.Min(tLen - torio.Item1, torio.Item2)));
                            str = nameTrimed.Substring(Math.Min(tLen2, torio.Item1), Math.Max(0, Math.Min(tLen2 - torio.Item1, torio.Item2)));
                            width = TextRenderer.MeasureText(graphics, str, cellStyle.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                            //var tagWidth = TextRenderer.MeasureText(graphics, tagPart, cellStyle.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width;
                            using (var b = new SolidBrush(tag.BackColor))
                            {
                                //graphics.FillRectangle(b, new Rectangle(paintRect.X + hShift, paintRectYForBG, tagWidth, height));

                                var baseRect = new Rectangle(paintRect.X, paintRectYForBG, width, height);
                                var drawRect = GetHShift(baseRect, str, cellStyle.Font, flags);
                                if (!drawRect.IsEmpty) graphics.FillRectangle(b, drawRect);
                            }
                            //TextRenderer.DrawText(graphics, str, cellStyle.Font, paintRect, tag.ForeColor, flags);
                            colors[i] = tag.ForeColor;
                            strs[2 * i + 1] = str;
                            rectangles[2 * i + 1] = paintRect;
                            paintRect.X += width;
                            paintRect.Width -= width;
                            index = torio.Item1 + str.Length;

                            i++;
                        }
                        for (var j = 0; j < i; j++)
                        {
                            TextRenderer.DrawText(graphics, strs[2 * j], cellStyle.Font, rectangles[2 * j], fColor, flags);
                            TextRenderer.DrawText(graphics, strs[2 * j + 1], cellStyle.Font, rectangles[2 * j + 1], colors[j], flags);
                        }
                        //TextRenderer.DrawText(graphics, name.Substring(index), cellStyle.Font, paintRect, fColor, flags);
                        TextRenderer.DrawText(graphics, nameTrimed.Substring(Math.Min(tLen, index)), cellStyle.Font, paintRect, fColor, flags);

                    }
                }
                //base.Paint(graphics, clipBounds, cellBounds, rowIndex, cellState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
            }

            static int getCharHeight_charHeight = -1;
            static int getCharHeight(Font font,
                TextFormatFlags flags)
            {
                if (getCharHeight_charHeight < 0)
                {
                    //getDrawRainbowParams_hShift = GetHShift("A", font, flagsForShift);

                    getCharHeight_charHeight = TextRenderer.MeasureText("A", font, new Size(int.MaxValue, int.MinValue), flags).Height;
                }
                return getCharHeight_charHeight;
            }

            // 与えられた長方形を横にシフトし、必要最小限だけ横に伸ばし返す
            static readonly Dictionary<string, Rectangle> GetHShift_CharRectangle = new Dictionary<string, Rectangle>();
            static Rectangle GetHShift(Rectangle baseRect, string testStr, Font font, TextFormatFlags flags)
            {
                Rectangle charRectangle;
                if (!GetHShift_CharRectangle.TryGetValue(testStr, out charRectangle))
                {
                    charRectangle = GetCharRectangle(testStr, font, flags, onlyHorizontal: true);
                }

                if (charRectangle.IsEmpty) return Rectangle.Empty;

                var resultShift2 = charRectangle.Left + charRectangle.Right - baseRect.Width;
                if ((resultShift2 & 1) == 1)
                {
                    baseRect.Width++;
                }

                baseRect.X += resultShift2 / 2;

                return baseRect;

            }

            public static Rectangle GetCharRectangle(string text, Font font, TextFormatFlags flags, bool onlyHorizontal)
            {
                if (string.IsNullOrEmpty(text)) return Rectangle.Empty;

                var mesuredSize = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MinValue), flags);

                if (mesuredSize.Width <= 0 || mesuredSize.Height <= 0) return Rectangle.Empty;

                var hOffset = (mesuredSize.Width + 1) / 2;
                var vOffset = (mesuredSize.Height + 1) / 2;

                var canvasWidth = mesuredSize.Width + 2 * hOffset;
                var canvasHeight = mesuredSize.Height + 2 * vOffset;

                using (var canvas = new Bitmap(canvasWidth, canvasHeight))
                {
                    var canvasRect = new Rectangle(0, 0, canvasWidth, canvasHeight);
                    using (var g = Graphics.FromImage(canvas))
                    {
                        g.FillRectangle(Brushes.White, canvasRect);
                        TextRenderer.DrawText(g, text, font, new Point(hOffset, vOffset), Color.Black, flags);
                    }

                    var img = canvas.LockBits(canvasRect, ImageLockMode.ReadOnly, canvas.PixelFormat);
                    try
                    {
                        var stride = img.Stride;
                        var bytesPerPixel = Image.GetPixelFormatSize(canvas.PixelFormat) / 8;
                        unsafe
                        {
                            var adr = (byte*)img.Scan0;

                            var top = 0;
                            if (!onlyHorizontal)
                            {
                                while (top < canvasHeight)
                                {
                                    var white = true;
                                    var localAdr = adr + (top * stride);
                                    for (var x = 0; x < canvasWidth; x++)
                                    {
                                        if (localAdr[bytesPerPixel * x] < 255)
                                        {
                                            white = false;
                                            break;
                                        }
                                    }
                                    if (!white) break;
                                    top++;
                                }
                                if (top == canvasHeight) return Rectangle.Empty;
                            }

                            var buttomMinus1 = canvasHeight - 1;
                            if (!onlyHorizontal)
                            {
                                while (buttomMinus1 > top)
                                {
                                    var white = true;
                                    var localAdr = adr + (buttomMinus1 * stride);
                                    for (var x = 0; x < canvasWidth; x++)
                                    {
                                        if (localAdr[bytesPerPixel * x] < 255)
                                        {
                                            white = false;
                                            break;
                                        }
                                    }
                                    if (!white) break;
                                    buttomMinus1--;
                                }
                            }

                            var left = 0;
                            while (left < canvasWidth)
                            {
                                var white = true;
                                var localAdr = adr + left * bytesPerPixel;
                                for (var y = 0; y < canvasHeight; y++)
                                {
                                    if (localAdr[y * stride] < 255)
                                    {
                                        white = false;
                                        break;
                                    }
                                }
                                if (!white) break;

                                left++;
                            }
                            if (left == canvasWidth) return Rectangle.Empty;


                            var rightMinus1 = canvasWidth - 1;
                            while (rightMinus1 > left)
                            {
                                var white = true;
                                var localAdr = adr + rightMinus1 * bytesPerPixel;
                                for (var y = 0; y < canvasHeight; y++)
                                {
                                    if (localAdr[y * stride] < 255)
                                    {
                                        white = false;
                                        break;
                                    }
                                }
                                if (!white) break;
                                rightMinus1--;
                            }

                            return new Rectangle(left - hOffset, top - vOffset, rightMinus1 + 1 - left, buttomMinus1 + 1 - top);
                        }
                    }
                    finally
                    {
                        canvas.UnlockBits(img);
                    }
                }
            }
        }

        private string getShortTimeString(DateTime dateTime, CultureInfo culture)
        {
            try
            {
                return dateTime > DateTime.MinValue ? dateTime.ToString("g", culture) : EmptyPageString;
            }
            catch (ArgumentOutOfRangeException)
            {
                try
                {
                    return dateTime > DateTime.MinValue ? dateTime.ToString("s") : EmptyPageString;
                }
                catch
                {
                    return "<ERROR>";
                }
            }
            catch
            {
                return "<ERROR>";
            }
        }

        private readonly Dictionary<DataGridViewColumn, int> dgvFileList_ColumnHeaderMouseClick_LastClickedTime = new Dictionary<DataGridViewColumn, int>();

        /// <summary>ファイル一覧の揮発性の列（ページ数・解像度など）で並べ替える</summary>
        private void applyVolatileSort(DataGridViewColumn tbc, SortOrder dir, SortMode sortMode, bool scrollToTopIfNoSelection = true)
        {
            var success = false;
            lock (ZipPathArrayLocker)
            {
                if (ZipPathArray != null && !CreatingZipPathArray)
                {
                    success = true;
                    foreach (DataGridViewColumn c in dgvFileList.Columns) c.HeaderCell.SortGlyphDirection = c != tbc ? SortOrder.None : dir;
                    tvCatalog.ShowIndexToDataIndex = GetSortArray(preSortArray: tvCatalog.ShowIndexToDataIndex, sortMode: sortMode);
                }
            }
            if (success)
            {
                var temp = cbSortBy_SelectedValueChanged_Stop;
                cbSortBy_SelectedValueChanged_Stop = true;
                cbSortBy.SelectedIndex = -1;
                cbSortBy_SelectedValueChanged_Stop = temp;
                setFileListFromThumbnailViewer(volatilitySort: true);
                var si = tvCatalog.SelectedIndex;
                if (si >= 0)
                {
                    //tvCatalog.ScrollBarToIndex(si);
                    tvCatalog.ScrollBarToIndexWithMinimalMove(tvCatalog.DataIndexToShowIndex[si]);
                }
                else if (scrollToTopIfNoSelection)
                {
                    var s2d = tvCatalog.ShowIndexToDataIndex;
                    if (s2d.Length > 0)
                    {
                        tvCatalog.ScrollBarToIndex(s2d[0]);
                    }
                }
            }
        }

        private void dgvFileList_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (e.ColumnIndex == tbcIcon.Index)
                {
                    var header = dgvFileList.Columns[tbcIcon.Index].HeaderCell;
                    if (header.SortGlyphDirection != SortOrder.Ascending)
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.TypeInAsc;
                    }
                    else
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.TypeInDesc;
                    }
                }
                else if (e.ColumnIndex == tbcFileName.Index)
                {
                    var header = dgvFileList.Columns[tbcFileName.Index].HeaderCell;
                    if (header.SortGlyphDirection != SortOrder.Ascending)
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.NameInAsc;
                    }
                    else
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.NameInDesc;
                    }
                }
                else if (e.ColumnIndex == tbcRating.Index)
                {
                    var header = dgvFileList.Columns[tbcRating.Index].HeaderCell;
                    if (header.SortGlyphDirection != SortOrder.Descending)
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.RatingInDesc;
                    }
                    else
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.RatingInAsc;
                    }
                }
                else if (e.ColumnIndex == tbcCreateTime.Index)
                {
                    var header = dgvFileList.Columns[tbcCreateTime.Index].HeaderCell;
                    if (header.SortGlyphDirection != SortOrder.Descending)
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.CreatedInDesc;
                    }
                    else
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.CreatedInAsc;
                    }
                }
                else if (e.ColumnIndex == tbcAccessTime.Index)
                {
                    var header = dgvFileList.Columns[tbcAccessTime.Index].HeaderCell;
                    if (header.SortGlyphDirection != SortOrder.Descending)
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.AccessedInDesc;
                    }
                    else
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.AccessedInAsc;
                    }
                }
                else if (e.ColumnIndex == tbcWriteTime.Index)
                {
                    var header = dgvFileList.Columns[tbcWriteTime.Index].HeaderCell;
                    if (header.SortGlyphDirection != SortOrder.Descending)
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.ModifiedInDesc;
                    }
                    else
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.ModifiedInAsc;
                    }
                }
                else if (e.ColumnIndex == tbcSize.Index)
                {
                    var header = dgvFileList.Columns[tbcSize.Index].HeaderCell;
                    if (header.SortGlyphDirection != SortOrder.Descending)
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.SizeInDesc;
                    }
                    else
                    {
                        cbSortBy.SelectedIndex = (int)SortMode.SizeInAsc;
                    }
                }
                else if (e.ColumnIndex >= 0)
                {
                    var tbc = dgvFileList.Columns[e.ColumnIndex];
                    if (tbc.SortMode == DataGridViewColumnSortMode.Programmatic)
                    {
                        var dir = tbc.HeaderCell.SortGlyphDirection;
                        if (dir == SortOrder.Ascending) dir = SortOrder.Descending;
                        else if (dir == SortOrder.Descending) dir = SortOrder.Ascending;
                        else
                        {
                            if (dgvFileList_ColumnHeaderMouseClick_LastClickedTime.TryGetValue(tbc, out var lastClick))
                            {
                                var now = Environment.TickCount;
                                if (InDoubleClickTime(now, lastClick))
                                {
                                    dir = SortOrder.Ascending;
                                }
                                else
                                {
                                    dir = SortOrder.Descending;
                                }
                            }
                            else
                            {
                                dir = SortOrder.Descending;
                            }
                        }

                        var dirString = dir == SortOrder.Ascending ? "InAsc" : "InDesc";
                        SortMode sortMode;
                        if (Enum.TryParse("V" + tbc.Name.Substring(3) + dirString, out sortMode))
                        {
                            //if (cbSortBy_SelectedValueChanged_Stop) return;
                            // 後回しにしたヘッダ（K33）を全部取り、揃ったらもう一度並べ替える。それまでは今ある値で並べる
                            pendingVolatileSort = requestAllDeferredHeaders() ? new PendingVolatileSort { Column = tbc, Direction = dir, SortMode = sortMode, Guid = loadingGuid } : null;
                            applyVolatileSort(tbc, dir, sortMode);
                        }
                        if (dir == SortOrder.Descending) dgvFileList_ColumnHeaderMouseClick_LastClickedTime[tbc] = Environment.TickCount;
                    }
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                iconToolStripMenuItem.Checked = tbcIcon.Visible;
                pageToolStripMenuItem.Checked = tbcPage.Visible;
                ratingHeaderRightClickToolStripMenuItem.Checked = tbcRating.Visible;
                dateCreatedToolStripMenuItem.Checked = tbcCreateTime.Visible;
                accessTimeToolStripMenuItem.Checked = tbcAccessTime.Visible;
                writeTimeToolStripMenuItem.Checked = tbcWriteTime.Visible;
                sizeToolStripMenuItem.Checked = tbcSize.Visible;
                resolutionToolStripMenuItem.Checked = tbcResolution.Visible;
                ratioToolStripMenuItem.Checked = tbcRatio.Visible;
                widthToolStripMenuItem.Checked = tbcWidth.Visible;
                heightToolStripMenuItem.Checked = tbcHeight.Visible;
                lengthToolStripMenuItem.Checked = tbcLength.Visible;

                fPSToolStripMenuItem.Checked = tbcFPS.Visible;

                setFont(iconToolStripMenuItem, bold: e.ColumnIndex == tbcIcon.Index);
                setFont(fileNameToolStripMenuItem, bold: e.ColumnIndex == tbcFileName.Index);
                setFont(pageToolStripMenuItem, bold: e.ColumnIndex == tbcPage.Index);
                setFont(ratingHeaderRightClickToolStripMenuItem, bold: e.ColumnIndex == tbcRating.Index);
                setFont(dateCreatedToolStripMenuItem, bold: e.ColumnIndex == tbcCreateTime.Index);
                setFont(accessTimeToolStripMenuItem, bold: e.ColumnIndex == tbcAccessTime.Index);
                setFont(writeTimeToolStripMenuItem, bold: e.ColumnIndex == tbcWriteTime.Index);
                setFont(sizeToolStripMenuItem, bold: e.ColumnIndex == tbcSize.Index);
                setFont(resolutionToolStripMenuItem, bold: e.ColumnIndex == tbcResolution.Index);
                setFont(ratioToolStripMenuItem, bold: e.ColumnIndex == tbcRatio.Index);
                setFont(widthToolStripMenuItem, bold: e.ColumnIndex == tbcWidth.Index);
                setFont(heightToolStripMenuItem, bold: e.ColumnIndex == tbcHeight.Index);
                setFont(lengthToolStripMenuItem, bold: e.ColumnIndex == tbcLength.Index);
                setFont(fPSToolStripMenuItem, bold: e.ColumnIndex == tbcFPS.Index);


                var count = cmsFileListHeader.Items.Count;
                var tsmiArray = new ToolStripMenuItem[count];
                foreach (DataGridViewColumn column in dgvFileList.Columns)
                {
                    ToolStripMenuItem tsmi;
                    if (column == tbcIcon) tsmi = iconToolStripMenuItem;
                    else if (column == tbcFileName) tsmi = fileNameToolStripMenuItem;
                    else if (column == tbcPage) tsmi = pageToolStripMenuItem;
                    else if (column == tbcRating) tsmi = ratingHeaderRightClickToolStripMenuItem;
                    else if (column == tbcCreateTime) tsmi = dateCreatedToolStripMenuItem;
                    else if (column == tbcAccessTime) tsmi = accessTimeToolStripMenuItem;
                    else if (column == tbcWriteTime) tsmi = writeTimeToolStripMenuItem;
                    else if (column == tbcSize) tsmi = sizeToolStripMenuItem;
                    else if (column == tbcResolution) tsmi = resolutionToolStripMenuItem;
                    else if (column == tbcRatio) tsmi = ratioToolStripMenuItem;
                    else if (column == tbcWidth) tsmi = widthToolStripMenuItem;
                    else if (column == tbcHeight) tsmi = heightToolStripMenuItem;
                    else if (column == tbcLength) tsmi = lengthToolStripMenuItem;
                    else tsmi = fPSToolStripMenuItem;

                    tsmiArray[column.DisplayIndex] = tsmi;
                }
                cmsFileListHeader.Items.Clear();
                cmsFileListHeader.Items.AddRange(tsmiArray);

                if (this != ActiveForm) Activate();
                cmsFileListHeader.Show(Cursor.Position);
            }
        }

        private void dgvFileList_KeyDown(object sender, KeyEventArgs e)
        {
            var mk = ModifierKeys;
            if (e.KeyCode == Keys.Enter)
            {
                OpenSelectedArchiveInDefaultViewer(ApplicationProviderUser.EnterKey, null);
                e.Handled = true;
                LatestControledControl = sender as Control;
            }
            else if (e.KeyCode == Keys.F2)
            {
                renameToolStripMenuItem_Click(sender, e);
                LatestControledControl = sender as Control;
            }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                LatestControledControl = sender as Control;
            }
            else if (e.KeyCode == Keys.Home)
            {
                LatestControledControl = dgvFileList;
                if (SelectItem(0, dgvFileList.CurrentRow?.Index ?? -1, mk))
                {
                    e.Handled = true;
                    if (tvCatalog.SelectedIndicesCount > 1) dgvFileList.FirstDisplayedScrollingRowIndex = 0; // 単数選択なら自動的にスクロールされる
                    else tvCatalog.ScrollBarPercentage = 0; // 単数の場合のみサムネイル側もスクロール
                }
            }
            else if (e.KeyCode == Keys.End)
            {
                LatestControledControl = dgvFileList;
                if (SelectItem(-1, dgvFileList.CurrentRow?.Index ?? -1, mk))
                {
                    e.Handled = true;
                    if (tvCatalog.SelectedIndicesCount > 1) dgvFileList.FirstDisplayedScrollingRowIndex = dgvFileList.Rows.Count - 1;
                    else tvCatalog.ScrollBarPercentage = 100;
                }
            }
            else if (commonKeyAction(e, mk))
            {
                return;
            }
            else if (e.Alt)
            {
                e.Handled = true;
            }
        }

        private bool SelectItem(int showIndex, int currentShowIndex, Keys modifiedKeys)
        {
            if (CreatingZipPathArray) return false;
            var s2d = tvCatalog.ShowIndexToDataIndex;
            if (s2d == null) return false;
            var count = s2d.Length;
            if (count <= 0) return false;
            if (showIndex < 0) showIndex += count;
            if (showIndex < 0 || showIndex >= count) return false;
            if ((modifiedKeys & Keys.Shift) != Keys.Shift) currentShowIndex = -1;
            if (currentShowIndex < 0 || currentShowIndex >= count || currentShowIndex == showIndex)
            {
                tvCatalog.SelectedIndex = s2d[showIndex];
            }
            else
            {
                var selectedShowTable = new bool[count];
                var alreadySelected = tvCatalog.SelectedIndices;
                var d2s = tvCatalog.DataIndexToShowIndex;
                var selectedCount = alreadySelected.Length;
                for (var i = 0; i < alreadySelected.Length; i++) selectedShowTable[d2s[alreadySelected[i]]] = true;
                int start, stop;
                if (showIndex < currentShowIndex)
                {
                    start = showIndex;
                    stop = currentShowIndex + 1;
                }
                else
                {
                    start = currentShowIndex;
                    stop = showIndex + 1;
                }
                for (var i = start; i < stop; i++)
                {
                    if (!selectedShowTable[i])
                    {
                        selectedShowTable[i] = true;
                        selectedCount++;
                    }
                }
                var selectedIndices = new int[selectedCount];
                var k = 0;
                for (var i = 0; i < selectedShowTable.Length; i++)
                {
                    if (selectedShowTable[i])
                    {
                        selectedIndices[k++] = s2d[i];
                    }
                }
                tvCatalog.SelectedIndices = selectedIndices;
            }
            return true;
        }

        private void cmsRightClickPrepareAndShowForKeyOnFileList(bool onlyTag, Keys modifierKeys)
        {
            var mk = modifierKeys;
            //var mk = ModifierKeys;
            if (tvCatalog.SelectedIndicesCount > 0)
            {
                var selectedRow = dgvFileList_FirstDisplayedSelectedRow();
                if (selectedRow == null) return;
                var rect = dgvFileList.GetRowDisplayRectangle(selectedRow.Index, cutOverflow: true);
                var m2 = Math.Min(rect.Height, rect.Width) / 2;
                var p = dgvFileList.PointToScreen(new Point(rect.X + m2, rect.Y + m2));

                //cmsRightClickPrepareAndShow(p, onlyTag);
                if (onlyTag)
                {
                    cmsRightClickPrepareAndShow_(p, true);
                }
                else
                {
                    rightClickContextMenuPrepareAndShow(p, mk);
                }
            }
            else if (!onlyTag && dgvFileList.Visible && (RightClickContextMenu != CatalogFormConetxtMenu.Tag))
            {
                var p = dgvFileList.PointToScreen(dgvFileList.Location);
                p.Y += dgvFileList.ColumnHeadersHeight;
                //cmsRightClickPrepareAndShow_(p);

                rightClickContextMenuPrepareAndShow(p, mk);
            }
        }

        private DataGridViewRow dgvFileList_FirstDisplayedSelectedRow()
        {
            return getFirstDisplayedSelectedRow(dgvFileList);
        }

        private static DataGridViewRow getFirstDisplayedSelectedRow(DataGridView dataGridView)
        {
            try
            {
                var start = dataGridView.FirstDisplayedScrollingRowIndex;
                var stop = start + dataGridView.DisplayedRowCount(includePartialRow: true);
                for (var i = start; i < stop; i++)
                {
                    if ((dataGridView.Rows.GetRowState(i) & DataGridViewElementStates.Selected) != 0) return dataGridView.Rows[i];
                }
            }
            catch
            {
            }
            return null;
        }

        string renameToolStripMenuItem_Click_OldName = null;

        string renameToolStripMenuItem_Click_OldPath = null;

        int renameToolStripMenuItem_Click_Index = -1;

        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!dgvFileList.Focused) return;
            if (!fileListToolStripMenuItem.Checked || !dgvFileList.Visible) return;

            if (renameToolStripMenuItem_Click_OldName != null) return;
            if (CreatingZipPathArray) return;

            try
            {
                if (!InDirectoryVirtualDirectoryOrSmartDirectory) return;
            }
            catch
            {
                return;
            }

            if (!dgvFileList.Focused) dgvFileList.Focus();

            var selectedIndex = tvCatalog.SelectedIndex;
            if (selectedIndex < 0) return;

            string currentPath;
            bool isDirectory;
            lock (ZipPathArrayLocker)
            {
                if (ZipPathArray == null || ZipPathArray.Length <= selectedIndex) return;
                currentPath = ZipPathArray[selectedIndex];
                isDirectory = ZipNameArray[selectedIndex].EndsWith(Path.DirectorySeparatorChar.ToString());
            }
            if (isDirectory && !Directory.Exists(currentPath) || !isDirectory && !File.Exists(currentPath))
            {
                var exception = new FileNotFoundException(null, currentPath);
                MessageBox.Show(this, exception.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var d2s = tvCatalog.DataIndexToShowIndex;
            if (selectedIndex >= d2s.Length) return;
            var showIndex = d2s[selectedIndex];
            if (showIndex < 0 || showIndex >= dgvFileList.RowCount) return;
            var info = getFileListRowInfo(showIndex);
            if (info == null) return;
            var cell = dgvFileList[tbcFileName.Index, showIndex];

            // 仮想モードなのでセルに値を入れられない。編集中は CellValueNeeded で拡張子を除いた名前を返す
            var name = info.Texts[tbcFileName.Index] ?? "";
            fileListEditingShowIndex = showIndex;
            fileListEditingText = getBaseName(name);
            dgvFileList.InvalidateCell(tbcFileName.Index, showIndex);

            renameToolStripMenuItem_Click_OldName = name;
            renameToolStripMenuItem_Click_Index = selectedIndex;
            renameToolStripMenuItem_Click_OldPath = currentPath;

            dgvFileList.CurrentCell = cell;
            dgvFileList.ReadOnly = false;
            dgvFileList.BeginEdit(true);
        }
    }
}
