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
        Graphics cbFilter_Graphic = null;

        private int cbLocationMinimumWidth;

        private void setCbFilterWidth()
        {
            // 縮小中にフィルタが書き換わる実装に備えて
            if (WindowState == FormWindowState.Minimized) return;

            // 長さの調整
            if (cbFilter_Graphic == null) cbFilter_Graphic = cbFilter.CreateGraphics();

            var text = cbFilter.Text;
            var start = cbFilter.SelectionStart;
            var length = cbFilter.SelectionLength;

            var textWidth = TextRenderer.MeasureText(cbFilter_Graphic, text, cbFilter.Font, new Size(int.MaxValue, int.MinValue),
                TextFormatFlags.NoPrefix).Width + cbFilter.Height * 3 / 2;

            var widthFromText = Math.Min(cbFilter.Width + cbLocation.Width - cbLocationMinimumWidth, Math.Max(cbFilter.MinimumSize.Width, textWidth));
            var delta = widthFromText - cbFilter.Width;
            if (delta != 0)
            {
                // サイズ変更時にオートコンプリートが働いて意図しない動作をするのを回避
                // AutoCompleteMode を None にするのでは効果がない
                var cbFilterItems = (from object item in cbFilter.Items select item).ToArray();
                var cbLocationItems = (from object item in cbLocation.Items select item).ToArray();
                cbFilter.Items.Clear();
                cbLocation.Items.Clear();

                cbLocation.Width -= delta;
                btnLocationCommands.Left -= delta;
                cbSortBy.Left -= delta;
                cbFilter.Left -= delta;
                cbFilter.Width = widthFromText;

                // クリアした Items の復元
                cbFilter.Items.AddRange(cbFilterItems);
                cbLocation.Items.AddRange(cbLocationItems);

                // 連続実行時に一部のcontrolの表示が遅れて不格好になるのを回避
                cbLocation.Update();
                btnLocationCommands.Update();
                cbSortBy.Update(); ;
                cbFilter.Update();

                if (cbFilter.SelectionStart != start) cbFilter.SelectionStart = start;
                if (cbFilter.SelectionLength != length) cbFilter.SelectionLength = length;

                // オートコンプリート問題の旧対策法 
                //cbFilter.AutoCompleteMode = cbFilterACM;
                //cbLocation.AutoCompleteMode = cbLocationACM;
            }
        }

        private SearchManager cbFilter_SearchManager;

        private bool cbFilter_TextChanged_StopResort = false;

        private string cbFilter_TextChanged_PrevText = null;

        private bool cbFilter_Valid = true;

        private void cbFilter_TextChanged(object sender, EventArgs e)
        {
            var text = cbFilter.Text;

            // TextChanged は過剰に呼び出される事がある
            if (cbFilter_TextChanged_PrevText == text) return;
            cbFilter_TextChanged_PrevText = text;


            setCbFilterWidth();

            if (!cbFilter_Refresh(text)) return;

            if (cbFilter_TextChanged_StopResort) return; // ブックマーク読み込み用

            // ユーザーが入力しているあいだ（欄にフォーカスがある）は、最後の入力から少し待ってまとめて 1 回だけ絞り込む（K3）。
            // 以前は 1 文字ごとに全件の並べ替えと一覧の作り直しをしていた（3 万件で 1 文字 100〜180 ms）。
            // プログラムから文字列を設定した場合（タグのクリックなど）は従来どおりすぐ
            if (cbFilter.Focused)
            {
                if (cbFilter_TextChanged_Timer == null)
                {
                    cbFilter_TextChanged_Timer = new System.Windows.Forms.Timer { Interval = 150 };
                    cbFilter_TextChanged_Timer.Tick += (s2, e2) =>
                    {
                        cbFilter_TextChanged_Timer.Stop();
                        ResortForFilter();
                    };
                }
                cbFilter_TextChanged_Timer.Stop();
                cbFilter_TextChanged_Timer.Start();
                return;
            }
            ResortForFilter();
        }

        private System.Windows.Forms.Timer cbFilter_TextChanged_Timer;

        private void ResortForFilter()
        {
            cbFilter_TextChanged_Timer?.Stop();
            lock (ZipPathArrayLocker)
            {
                if (ZipPathArray != null && ZipPathArray.Length > 0)
                {
                    {
                        tvCatalog.ShowIndexToDataIndex = GetSortArray(preSortArray: tvCatalog.ShowIndexToDataIndex);
                    }

                    setFileListFromThumbnailViewer();
                    var si = tvCatalog.SelectedIndex;
                    if (si >= 0)
                    {
                        // スクロール量の変化を伴う（誤：伴ない）ので MinimalMove は使わない
                        tvCatalog.ScrollBarToIndex(si);
                    }
                }
            }
        }

        private bool cbFilter_Refresh(string filter)
        {
            var invalid = InArchive || InMovie; // フィルタが無効の場合でもエラーの有無はチェックする
            if (aliasesToFilteringStrings != null && aliasesToFilteringStrings.ContainsKey(filter))
            {
                try
                {
                    cbFilter_SearchManager = new SearchManager(aliasesToFilteringStrings[filter]);
                }
                catch
                {
                    cbFilter_SearchManager = null;
                    cbFilter.BackColor = invalid ? InvalidColor : ErrorColor;
                    cbFilter_Valid = false;
                    return false;
                }
                cbFilter.BackColor = invalid ? InvalidColor : AliasColor;
                cbFilter_Valid = true;
                return true;
            }
            else
            {
                try
                {
                    cbFilter_SearchManager = new SearchManager(filter);
                }
                catch
                {
                    cbFilter_SearchManager = null;
                    cbFilter.BackColor = invalid ? InvalidColor : ErrorColor;
                    cbFilter_Valid = false;
                    return false;
                }
                cbFilter.BackColor = invalid ? InvalidColor : WindowColor;
                cbFilter_Valid = true;
                return true;
            }
        }

        private void cbFilter_Leave(object sender, EventArgs e)
        {
            if (cbFilter_TextChanged_Timer?.Enabled == true) ResortForFilter(); // 入力中で待っていた絞り込みを済ませる（K3）
            SetBackgroundMode(false, 0);
            setCurrentFilterToHistory(bringToTop: true);
        }

        private void setCurrentFilterToHistory(bool bringToTop)
        {
            if (invariantFilteringList) return;
            var text = cbFilter.Text;
            if (!string.IsNullOrEmpty(text) && filterValid())
            {
                if (cbFilter.Items.Contains(text))
                {
                    if (!bringToTop) return;
                    cbFilter.Items.Remove(text);
                    cbFilter.Text = text;
                }
                cbFilter.Items.Insert(1, text);
                TrimHistoryItems(cbFilter.Items, FilterHistoryLimit + 1); // 先頭の空欄のぶん +1。古いものから捨てる（S1）
            }
        }

        // 場所・絞り込みの履歴の上限（S1）。以前は無制限で、使うほど config.xml が大きくなり起動も遅くなっていた
        private const int LocationHistoryLimit = 200;

        private const int FilterHistoryLimit = 200;

        private static void TrimHistoryItems(ComboBox.ObjectCollection items, int limit)
        {
            var count = items.Count;
            if (count <= limit) return;
            for (var i = count - 1; i >= limit; i--) items.RemoveAt(i);
        }

        private void btnFilterCommands_Click(object sender, EventArgs e)
        {
            clearAllHistoryOfFiltersToolStripMenuItem.Enabled = !invariantFilteringList && cbFilter.Items.Count > 1;
            deleteCurrentFilterFromHistoryToolStripMenuItem.Enabled = !invariantFilteringList && !string.IsNullOrEmpty(cbFilter.Text) && cbFilter.Items.Contains(cbFilter.Text);
            cmsFilterCommands.Show(Cursor.Position);
        }

        private void btnFilterCommands_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                btnFilterCommands_Click(null, null);
            }
            else if (e.Button == MouseButtons.Middle)
            {
                // enabled でないと使えない
                //deleteCurrentFilterFromHistoryToolStripMenuItem.PerformClick();

                deleteCurrentFilterFromHistoryToolStripMenuItem_Click(null, null);
            }
        }

        private void deleteCurrentFilterFromHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (invariantFilteringList) return;
            if (!string.IsNullOrEmpty(cbFilter.Text))
            {
                cbFilter.Items.Remove(cbFilter.Text);
                cbFilter.Text = "";
            }
        }

        private void clearAllHistoryOfFiltersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (invariantFilteringList) return;
            if (MessageBox.Show(Message.DoYouClearFilterHistory, Message.Information, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
            {
                cbFilter.Items.Clear();
                cbFilter.Items.Add("");
            }
        }

        private void cbFilter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !cbFilter.DroppedDown)
            {
                e.SuppressKeyPress = true;
                FocusLatestControledControl();
            }
        }

        private void cbFilter_DropDown(object sender, EventArgs e)
        {
            setCurrentFilterToHistory(bringToTop: false);
        }

        private readonly Dictionary<string, SizeF> cbFilter_DrawItem_SizeDictionary = new Dictionary<string, SizeF>();

        private void cbFilter_DrawItem(object sender, DrawItemEventArgs e)
        {
            var index = e.Index;
            if (index < 0) return;
            var items = cbFilter.Items;
            if (index >= items.Count) return;
            var text = items[index].ToString();
            var size = MeasureStringSizeByGraphics(e.Graphics, text, cbFilter.Font, cbFilter_DrawItem_SizeDictionary); //e.Graphics.MeasureString(text, cbFilter.Font);
            var rect = e.Bounds;
            Color color;
            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            if (selected)
            {
                color = SystemColors.Highlight;
            }
            else if (aliasesToFilteringStrings != null && aliasesToFilteringStrings.ContainsKey(text))
            {
                color = AliasColor;
            }
            else
            {
                color = SystemColors.Window;
            }
            using (var b = new SolidBrush(color))
            {
                e.Graphics.FillRectangle(b, rect);
            }
            rect.Y += (int)Math.Ceiling((rect.Height - size.Height) / 2);
            e.Graphics.DrawString(text, cbFilter.Font, selected ? SystemBrushes.HighlightText : SystemBrushes.WindowText, rect);
        }
    }
}
