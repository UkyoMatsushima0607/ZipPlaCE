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
        private void tagEditorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showTagEditor();
        }

        private void showTagEditor(IReadOnlyList<string> newTags = null, bool needToSaveCurrentTag = false)
        {
            if (CreatingZipPathArray) return;
            AllowFullpower = false;
            var edited = Program.ShowTagEditor(this, newTags);
            AllowFullpower = true;
            if (edited)
            {
                setTagFilteringMenuItems(needToRepairTagsFromBookmark: false, needToSaveCurrentTag: needToSaveCurrentTag);

                if (showTags)
                {
                    tvCatalog.Invalidate();
                }
                setFileListFromThumbnailViewer();
            }
        }

        //● ロッカージェスチャ
        //● private readonly System.Windows.Forms.ContextMenuStrip cbFilter_MouseDown_DummyCMS = new System.Windows.Forms.ContextMenuStrip();
        private void cbFilter_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle
                )
            {
                var currentText = cbFilter.Text;
                if (string.IsNullOrEmpty(currentText))
                {
                    var s = GetClipBoardShortString();
                    if (!string.IsNullOrEmpty(s))
                    {
                        cbFilter.Text = s;
                        cbFilter.SelectAll();
                    }
                }
                else
                {
                    cbFilter.Text = "";
                }
            }

        }

        private static string GetClipBoardShortString()
        {
            try
            {
                // ファイル優先
                var data = Clipboard.GetDataObject();
                if (data == null) return null;
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])data.GetData(DataFormats.FileDrop);
                    if (files.Length > 0)
                    {
                        return Path.GetFileName(files[0]);
                    }
                    return null;
                }

                if (data.GetDataPresent(NativeMethods.CFSTR_FILEDESCRIPTORW))
                {

                    var descriptorData = data.GetData(NativeMethods.CFSTR_FILEDESCRIPTORW) as Stream;
                    if (descriptorData == null) return null;

                    DataObjectEx.FILEDESCRIPTOR[] fileDescriptors;
                    using (descriptorData)
                    {
                        fileDescriptors = DataObjectEx.FILEDESCRIPTOR.FromStreamToArray(descriptorData);
                    }
                    if (fileDescriptors.Length == 0) return null;

                    return Path.GetFileName(fileDescriptors[0].cFileName);
                }

                if (data.GetDataPresent(DataFormats.UnicodeText))
                {
                    return GetShortString((string)data.GetData(DataFormats.UnicodeText));
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static readonly char[] GetShortString_Spaces = new char[] { ' ', '\t', '\r', '\n' };

        private static string GetShortString(string input)
        {
            const int maxLen = 1000;
            var b = new StringBuilder();
            var outOfSpace = false;
            var count = 0;
            var lastSpace = 0;
            foreach (var c in input)
            {
                if (count >= maxLen)
                {
                    if (!GetShortString_Spaces.Contains(c))
                    {
                        b.Remove(lastSpace, maxLen - lastSpace);
                        outOfSpace = true;
                    }
                    break;
                }
                if (GetShortString_Spaces.Contains(c))
                {
                    if (outOfSpace)
                    {
                        outOfSpace = false;
                        b.Append(' ');
                        lastSpace = count;
                        count++;
                    }
                }
                else
                {
                    outOfSpace = true;
                    b.Append(c);
                    count++;
                }
            }
            if (!outOfSpace)
            {
                b.Remove(lastSpace, 1);
            }
            return b.ToString();

        }

        public void MoveLocation(ApplicationProviderUser user, string path)
        {
            if (path == null) return;

            if (IsKeyboard(user))
            {
                bmwMakePreviewRunWorkerStartingFinalAction = () =>
                {
                    firstShownItemSelect();
                    bmwMakePreviewRunWorkerStartingFinalAction = null;
                };
            }

            var interval = getSubdivideInterval(path);
            if (interval == null)
            {
                if (IsSupportedByCatalogFormAndExists(path))
                {
                    zabLocation.Text = path;
                    MakePreview();
                }
                else
                {
                    var parent = Program.ItsSelfOrExistingParentDirectory(path, IsSupportedByCatalogFormAndExists);
                    string child = "";
                    try
                    {
                        child = getFileName(path); // 動画の Duration を想定
                    }
                    catch { }
                    zabLocation.Text = parent;
                    MakePreview(child);
                }
            }
            else
            {
                var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
                if (altPos < 0) return;
                var parent = path.Substring(0, altPos);
                zabLocation.Text = parent;
                MakePreview(subdivideInterval: interval);
            }
        }

        public bool Subdividable(string path)
        {
            return getSubdivideInterval(path) != null;
        }

        private Tuple<TimeSpan, TimeSpan> getSubdivideInterval(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!InMovie) return null;
            var zpa = ZipPathArray;
            if (zpa == null || zpa.Length <= 1) return null;
            var index = Array.IndexOf(zpa, path);
            if (index < 0) return null;
            var path0 = index > 0 ? zpa[index - 1] : null;
            var path1 = index < zpa.Length - 1 ? zpa[index + 1] : null;
            var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
            if (altPos < 0 || altPos >= path.Length - 1 || path0 != null && altPos >= path0.Length - 1 || path1 != null && altPos >= path1.Length - 1) return null;
            TimeSpan t; if (!TimeSpan.TryParse(path.Substring(altPos + 1), out t)) return null;
            TimeSpan t0; if (path0 == null) t0 = TimeSpan.FromSeconds(-1); else if (!TimeSpan.TryParse(path0.Substring(altPos + 1), out t0)) return null;
            TimeSpan t1; if (path1 == null) t1 = TimeSpan.MaxValue; else if (!TimeSpan.TryParse(path1.Substring(altPos + 1), out t1)) return null;
            t0 += TimeSpan.FromSeconds(1); if (t0 > t) t0 = t;
            t1 -= TimeSpan.FromSeconds(1); if (t1 < t) t1 = t;
            return t - t0 > TimeSpan.FromSeconds(0.5) || t1 - t > TimeSpan.FromSeconds(0.5) ? Tuple.Create(t0, t1) : null;
        }

        private void setTagFilteringMenuItems(bool needToRepairTagsFromBookmark, bool needToSaveCurrentTag, bool loadFromIni = false)
        {
            var config = new ZipTagConfig();
            if (config.ProfileColor == currentProfileColor)
            {
                Tags = config.Tags;
                synchronizeCurrentProfile();
                if (!loadFromIni)
                {
                    saveBookmarkToIni_bookmarkChanged = true;
                    saveBookmarkToConfig();
                }
            }
            else
            {
                if (needToRepairTagsFromBookmark)
                {
                    Tags = null;
                    foreach (DataGridViewRow row in dgvDirectoryList.Rows)
                    {
                        var bookmark = row.Cells[tbcDirectoryName.Index].Value as ColoredBookmark;
                        if (bookmark.SimpleBookmark.SpecialRole == SimpleBookmarkSpecialRole.None && bookmark?.Color == currentProfileColor) // セパレーターなら無視される
                        {
                            var tags = bookmark?.Profile?.Tags;
                            if (tags != null)
                            {
                                Tags = tags;
                                break;
                            }
                        }
                    }
                }

                if (Tags == null) Tags = new ZipTag[0];
                if (needToSaveCurrentTag)
                {
                    config.Tags = Tags;
                    config.ProfileColor = currentProfileColor;
                    config.Save();
                }
            }
            setTagFilteringMenuItems(Tags);
        }

        private ToolStripDropDown[] setTagFilteringMenuItems_DropDown;

        private void setTagFilteringMenuItems(ZipTag[] tags)
        {
            if (tags == null) tags = new ZipTag[0];

            var items = menuStripForTagFilter.Items;
            var newCount = tags.Length;


            if (newCount == items.Count - 1)
            {
                var same = true;
                for (var i = 0; i < newCount; i++)
                {
                    var tag = tags[i];
                    var item = items[i + 1];
                    if (tag.Name != getTextForTagToolStripMenuItem(item) || tag.BackColor != item.BackColor)
                    {
                        same = false;
                        break;
                    }
                }
                if (same)
                {
                    setTagFilterLocation();
                    return;
                }
            }

            var newItems = new ToolStripItem[newCount + 1];
            newItems[0] = ratingFilterToolStripMenuItem;

            var oldDropDowns = setTagFilteringMenuItems_DropDown;
            var oldItems = new ToolStripItem[items.Count];
            items.CopyTo(oldItems, 0);
            setTagFilteringMenuItems_DropDown = new ToolStripDropDown[newCount];

            for (var i = 0; i < newCount; i++)
            {
                var tag = tags[i];
                var tagName = tag.Name.Replace("&", "&&");
                var tsmi = new ToolStripMenuItem(tagName);
                tsmi.BackColor = tag.BackColor;
                tsmi.ForeColor = tag.ForeColor;
                tsmi.Overflow = ToolStripItemOverflow.AsNeeded;
                tsmi.MouseDown += Tsmi_MouseDown;
                tsmi.Click += Tsmi_Click;

                var tsmiItems = new ToolStripMenuItem[6];
                tsmiItems[0] = new ToolStripMenuItem(Message.AndInclude1.Replace("$1", tagName));
                tsmiItems[0].Click += tagAndInclude1_Click;
                tsmiItems[1] = new ToolStripMenuItem(Message.AndExclude1.Replace("$1", tagName));
                tsmiItems[1].Click += tagAndExclude1_Click;
                tsmiItems[2] = new ToolStripMenuItem(Message.Include1.Replace("$1", tagName));
                tsmiItems[2].Click += tagInclude1_Click;
                tsmiItems[3] = new ToolStripMenuItem(Message.Exclude1.Replace("$1", tagName));
                tsmiItems[3].Click += tagExclude1_Click;
                tsmiItems[4] = new ToolStripMenuItem(Message.OrInclude1.Replace("$1", tagName));
                tsmiItems[4].Click += tagOrInclude1_Click;
                tsmiItems[5] = new ToolStripMenuItem(Message.OrExclude1.Replace("$1", tagName));
                tsmiItems[5].Click += tagOrExclide1_Click;
                tsmi.DropDownItems.AddRange(tsmiItems);

                tsmi.ToolTipText = Message.RightClickDownFlickToOpenContextMenu;

                tsmi.DropDownClosed += Tsmi_DropDownClosed;

                var normalColor = tag.ForeColor;
                if (ColorEquals(normalColor, Color.White))
                {
                    tsmi.ForeColor = normalColor;
                    ForeColorManager.Set(tsmi);
                }

                var dropDown = tsmi.DropDown;
                dropDown.Opened += Tsmi_DropDownOpened;
                setTagFilteringMenuItems_DropDown[i] = dropDown;
                tsmi.DropDown = null;

                ToolStripOverwriter.SquarizeToolStrip(dropDown);

                newItems[i + 1] = tsmi;
            }

            items.Clear();
            menuStripForTagFilter.AutoSize = true;
            items.AddRange(newItems);
            menuStripForTagFilter.AutoSize = false;

            // 作り直す前の項目とドロップダウンを破棄する（X19）。開いているものは閉じたときに
            if (oldDropDowns != null)
            {
                foreach (var dropDown in oldDropDowns)
                {
                    if (dropDown == null || dropDown.IsDisposed) continue;
                    if (dropDown.Visible) dropDown.Closed += (s2, e2) => BeginInvoke(new MethodInvoker(dropDown.Dispose));
                    else dropDown.Dispose();
                }
            }
            foreach (var item in oldItems)
            {
                if (item != null && item != ratingFilterToolStripMenuItem) item.Dispose();
            }

            setTagFilterLocation_WidthMemory = (from item in newItems select item.Width).ToArray();

            setTagFilterLocation();
        }

        private void Tsmi_Click(object sender, EventArgs e)
        {
            var mk = ModifierKeys;
            if ((mk & (Keys.Control | Keys.Shift)) == (Keys.Control | Keys.Shift))
            {
                tagOrExclide1_Click(sender, e);
            }
            else if ((mk & Keys.Control) == Keys.Control)
            {
                tagOrInclude1_Click(sender, e);
            }
            else if ((mk & Keys.Shift) == Keys.Shift)
            {
                tagAndExclude1_Click(sender, e);
            }
            else
            {
                tagAndInclude1_Click(sender, e);
            }
        }

        private void Tsmi_MouseDown(object sender, MouseEventArgs e)
        {
            var mk = ModifierKeys;
            if (e.Button == MouseButtons.Middle)
            {
                if ((mk & Keys.Shift) == Keys.Shift)
                {
                    tagExclude1_Click(sender, e);
                }
                else
                {
                    tagInclude1_Click(sender, e);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                OpenTagButtonDropdown(sender as ToolStripMenuItem, byTouch: false);
            }
        }

        private void OpenTagButtonDropdown(ToolStripMenuItem tsmi, bool byTouch)
        {
            var items = menuStripForTagFilter.Items;
            var dropDown = setTagFilteringMenuItems_DropDown[items.IndexOf(tsmi) - 1];
            tsmi.DropDown = dropDown;
            tagFilterToolStripMenuItem_DropDownOpening(tsmi);

            setTagOrRatingButtonShortcutKeyDisplayString(dropDown, byTouch);

            dropDown.Show();
        }

        private void setTagOrRatingButtonShortcutKeyDisplayString(ToolStripDropDown dropDown, bool byTouch)
        {
            var items = dropDown.Items;
            var os = items.Count - 6;
            if (os >= 0)
            {
                if (byTouch)
                {
                    (items[os + 0] as ToolStripMenuItem).ShortcutKeyDisplayString = Message.Tap;
                    (items[os + 1] as ToolStripMenuItem).ShortcutKeyDisplayString = Message.UpFlick;
                    (items[os + 2] as ToolStripMenuItem).ShortcutKeyDisplayString = Message.LeftFlick;
                    (items[os + 3] as ToolStripMenuItem).ShortcutKeyDisplayString = Message.LeftUpFlick;
                    (items[os + 4] as ToolStripMenuItem).ShortcutKeyDisplayString = Message.RightFlick;
                    (items[os + 5] as ToolStripMenuItem).ShortcutKeyDisplayString = Message.RightUpFlick;
                }
                else
                {
                    (items[os + 0] as ToolStripMenuItem).ShortcutKeyDisplayString = Message.LeftClick;
                    (items[os + 1] as ToolStripMenuItem).ShortcutKeyDisplayString = "Shift+" + Message.LeftClick;
                    (items[os + 2] as ToolStripMenuItem).ShortcutKeyDisplayString = Message.MiddleClick;
                    (items[os + 3] as ToolStripMenuItem).ShortcutKeyDisplayString = "Shift+" + Message.MiddleClick;
                    (items[os + 4] as ToolStripMenuItem).ShortcutKeyDisplayString = "Ctrl+" + Message.LeftClick;
                    (items[os + 5] as ToolStripMenuItem).ShortcutKeyDisplayString = "Ctrl+Shift+" + Message.LeftClick;
                }
            }
        }

        private void tagOrExclide1_Click(object sender, EventArgs e)
        {
            filterAdd("+-" + SearchManager.TrimAndPutQuotationIfNeeded(getTextForTagToolStripMenuItem(sender), stopwords: null), weak: false);
        }

        private void tagOrInclude1_Click(object sender, EventArgs e)
        {
            filterAdd("+" + SearchManager.TrimAndPutQuotationIfNeeded(getTextForTagToolStripMenuItem(sender), stopwords: null), weak: false);
        }

        private void tagAndExclude1_Click(object sender, EventArgs e)
        {
            filterAdd("-" + SearchManager.TrimAndPutQuotationIfNeeded(getTextForTagToolStripMenuItem(sender), stopwords: null), weak: true);
        }

        private void tagAndInclude1_Click(object sender, EventArgs e)
        {
            filterAdd(SearchManager.TrimAndPutQuotationIfNeeded(getTextForTagToolStripMenuItem(sender),
                string.IsNullOrEmpty(cbFilter.Text) ? aliasesToFilteringStrings?.Keys : null // 別名に空白は含まれないので。
                ), weak: true);
        }

        private void tagExclude1_Click(object sender, EventArgs e)
        {
            cbFilter.Text = "-" + SearchManager.TrimAndPutQuotationIfNeeded(getTextForTagToolStripMenuItem(sender), stopwords: null);
            cbFilter.SelectAll();
        }

        private void tagInclude1_Click(object sender, EventArgs e)
        {
            cbFilter.Text = SearchManager.TrimAndPutQuotationIfNeeded(getTextForTagToolStripMenuItem(sender), aliasesToFilteringStrings?.Keys);
            cbFilter.SelectAll();
        }

        private string getTextForTagToolStripMenuItem(object sender)
        {
            var tsmi = sender as ToolStripMenuItem;
            if (tsmi == null) return "";
            var owner = tsmi.OwnerItem;
            string result;
            if (owner == null)
            {
                result = tsmi.Text.Replace("&&", "&");
            }
            else
            {
                result = owner.Text.Replace("&&", "&");
            }
            return result;
        }

        private void Tsmi_DropDownOpened(object sender, EventArgs e)
        {
            setFilterDropDownLocation(sender as ToolStripDropDownMenu);
        }

        private void Tsmi_DropDownClosed(object sender, EventArgs e)
        {
            AllowFullpower = true;
            var tsmi = sender as ToolStripMenuItem;
            tsmi.DropDown = null;
        }

        private void tagFilterToolStripMenuItem_DropDownOpening(ToolStripMenuItem tsmi)
        {
            var str = getTextForTagToolStripMenuItem(tsmi);
            var addableWeak = filterAddable(str, weak: true);
            var addableStrong = filterAddable(str, weak: false);
            var items = tsmi.DropDownItems;
            items[0].Enabled = addableWeak;
            items[1].Enabled = addableWeak;
            items[4].Enabled = addableStrong;
            items[5].Enabled = addableStrong;

            AllowFullpower = false;
        }

        private FormWindowState CatalogForm_SizeChanged_PreState = FormWindowState.Normal;

        private FormWindowState CatalogForm_SizeChanged_BeforeMinimizedState = FormWindowState.Normal;

        private void CatalogForm_SizeChanged(object sender, EventArgs e)
        {
            var state = WindowState;
            if (state != FormWindowState.Minimized)
            {
                setCbFilterWidth();
                setTagFilterLocation();
                setMainPanelWidth();
            }
            else if (CatalogForm_SizeChanged_PreState != FormWindowState.Minimized)
            {
                CatalogForm_SizeChanged_BeforeMinimizedState = CatalogForm_SizeChanged_PreState;
            }

            if (CatalogForm_SizeChanged_PreState != state)
            {
                if (CatalogForm_SizeChanged_PreState == FormWindowState.Minimized && state != FormWindowState.Minimized)
                {
                    CatalogForm_Activated(this, e);
                }

                CatalogForm_SizeChanged_PreState = state;
            }
        }

        private bool getShouldBeShowTagFilter()
        {
            // visible のチェック方法は saveSetting に倣う
            return showRating && thumbnailListToolStripMenuItem.Checked || tbcRating.Visible && fileListToolStripMenuItem.Checked;
        }

        private void setTagfilterLocationIfRatingFilterVisibleChanged()
        {
            var newRatingFilterVisible = getShouldBeShowTagFilter();
            if (ratingFilterToolStripMenuItem.Visible != newRatingFilterVisible)
            {
                setTagFilterLocation(newRatingFilterVisible);
            }
        }

        private int[] setTagFilterLocation_WidthMemory = new int[1]; // rating の分

        private ToolStripMenuItem setTagFilterLocation_rightSideToolStripMenuItem = null;

        private void setTagFilterLocation(bool? ratingFilterVisible = null)
        {
            if (!IsShown) return;

            if (setTagFilterLocation_rightSideToolStripMenuItem == null)
            {
                setTagFilterLocation_rightSideToolStripMenuItem = (ToolStripMenuItem)(from ToolStripItem item in menuStrip.Items select item).Last(item => item.Visible);
            }

            var ratingFilterVisible_ = ratingFilterVisible != null ? (bool)ratingFilterVisible : getShouldBeShowTagFilter();
            ratingFilterToolStripMenuItem.Visible = ratingFilterVisible_;

            var ofb = menuStripForTagFilter.OverflowButton;
            var ofbWidth = ofb.Width + 1;
            var allowedWidth = menuStrip.Right - setTagFilterLocation_rightSideToolStripMenuItem.Bounds.Right - ofbWidth;
            var items = menuStripForTagFilter.Items;
            var count = items.Count;
            if (setTagFilterLocation_WidthMemory.Length != count) return;
            var padding = menuStripForTagFilter.Padding;
            var width = padding.Left + padding.Right;
            var left = items[0].Bounds.Left;
            var fullShow = false;
            for (var i = 0; i < count; i++)
            {
                var testWidth = width + (i == 0 ? ratingFilterVisible_ ? ratingFilterToolStripMenuItem.Width : 0 : setTagFilterLocation_WidthMemory[i]);
                if (testWidth <= allowedWidth)
                {
                    width = testWidth;
                }
                else
                {
                    if (i == count - 1 && testWidth <= allowedWidth + ofbWidth)
                    {
                        width = testWidth;
                        fullShow = true;
                    }
                    break;
                }
                if (i == count - 1) fullShow = true;
            }
            if (!fullShow)
            {
                menuStripForTagFilter.CanOverflow = true;
                width += ofbWidth;
            }
            else
            {
                menuStripForTagFilter.CanOverflow = false;
            }

            SuspendLayout();
            menuStripForTagFilter.Width = width;
            menuStripForTagFilter.Left = menuStrip.Right - width;
            ResumeLayout();
        }

        // これは実際には登録していない
        // Show で開いた場合 Opening は呼ばれない
        private void ratingFilterToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            const string rateStrPrototype = "r=5";
            var addableWeak = filterAddable(rateStrPrototype, weak: true);
            var addableStrong = filterAddable(rateStrPrototype, weak: false);
            var stars = getRatingReverenceStars();
            equalToXToolStripMenuItem.Text = Message.EqualTo1.Replace("$1", stars);
            greaterThanOrEqualToXToolStripMenuItem.Text = Message.GreaterThanOrEqualTo1.Replace("$1", stars);
            andEqualToXToolStripMenuItem.Text = Message.AndEqualTo1.Replace("$1", stars);
            andGreaterThanOrEqualToXToolStripMenuItem.Text = Message.AndGreaterThanOrEqualTo1.Replace("$1", stars);
            orEqualToXToolStripMenuItem.Text = Message.OrEqualTo1.Replace("$1", stars);
            orGreaterThanOrEqualToXToolStripMenuItem.Text = Message.OrGreaterThanOrEqualTo1.Replace("$1", stars);
            andEqualToXToolStripMenuItem.Enabled = addableWeak;
            andGreaterThanOrEqualToXToolStripMenuItem.Enabled = addableWeak;
            orEqualToXToolStripMenuItem.Enabled = addableStrong;
            orGreaterThanOrEqualToXToolStripMenuItem.Enabled = addableStrong;
            ratingFilterToolStripMenuItemDropDown.AutoClose = true;
            var value = ratingReferenceValue;
            increaseReferenceValueToolStripMenuItem.Enabled = value < 5;
            decreaseReferenceValueToolStripMenuItem.Enabled = value > 1;
            AllowFullpower = false;
        }

        private void ratingFilterToolStripMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            AllowFullpower = true;
            ratingFilterToolStripMenuItem_Dropdowned = false;
            ratingFilterToolStripMenuItem.DropDown = null;
        }

        private void ratingFilterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var mk = ModifierKeys;
            if ((mk & (Keys.Control | Keys.Shift)) == (Keys.Control | Keys.Shift))
            {
                orEqualToXToolStripMenuItem_Click(null, null);
            }
            else if ((mk & Keys.Control) == Keys.Control)
            {
                orGreaterThanOrEqualToXToolStripMenuItem_Click(null, null);
            }
            else if ((mk & Keys.Shift) == Keys.Shift)
            {
                andEqualToXToolStripMenuItem_Click(null, null);
            }
            else
            {
                andGreaterThanOrEqualToXToolStripMenuItem_Click(null, null);
            }
        }

        private void ratingFilterToolStripMenuItem_MouseDown(object sender, MouseEventArgs e)
        {
            var mk = ModifierKeys;
            if (e.Button == MouseButtons.Middle)
            {
                if ((mk & Keys.Shift) == Keys.Shift)
                {
                    equalToXToolStripMenuItem_Click(null, null);
                }
                else
                {
                    greaterThanOrEqualToXToolStripMenuItem_Click(null, null);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                OpenRateButtonDropdown(byTouch: false);
            }
        }

        private void OpenRateButtonDropdown(bool byTouch)
        {
            ratingFilterToolStripMenuItem.DropDown = ratingFilterToolStripMenuItemDropDown;
            ratingFilterToolStripMenuItem_DropDownOpening(null, null);

            setTagOrRatingButtonShortcutKeyDisplayString(ratingFilterToolStripMenuItemDropDown, byTouch);

            ratingFilterToolStripMenuItemDropDown.Show();
        }

        private void increaseReferenceValueToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ratingReferenceValue++;
            ratingFilterToolStripMenuItem_DropDownOpening(null, null);
            setFilterDropDownLocation(ratingFilterToolStripMenuItemDropDown);
        }

        private void decreaseReferenceValueToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ratingReferenceValue--;
            ratingFilterToolStripMenuItem_DropDownOpening(null, null);
            setFilterDropDownLocation(ratingFilterToolStripMenuItemDropDown);
        }

        private int ratingReferenceValue_field = 3;

        private int ratingReferenceValue
        {
            get
            {
                return Math.Max(1, Math.Min(5, ratingReferenceValue_field));
            }
            set
            {
                ratingReferenceValue_field = Math.Max(1, Math.Min(5, value));

                ratingFilterToolStripMenuItem.Invalidate();
            }
        }

        private string getRatingReverenceStars()
        {
            if (ratingReferenceValue_field <= 1) return "★";
            else if (ratingReferenceValue_field == 2) return "★★";
            else if (ratingReferenceValue_field == 3) return "★★★";
            else if (ratingReferenceValue_field == 4) return "★★★★";
            else return "★★★★★";
        }

        private bool filterAddable(string str, bool weak, out bool isRate, out string[] currents, out string trim, out string[] trims) =>
            filterAddable(str, weak, true, out isRate, out currents, out trim, out trims);

        private bool filterAddable(string str, bool weak) =>
            filterAddable(str, weak, false, out var dummy0, out var dummy1, out var dummy2, out var dummy3);

        private bool filterAddable(string str, bool weak, bool currentsRateAndTrimsAreNeededIfReturnTrue, out bool isRate, out string[] currents, out string trim, out string[] trims)
        {
            isRate = false;
            trim = null;
            currents = null;
            trims = null;
            var text = cbFilter.Text;
            if (text == null) text = "";
            text = text.Trim();
            if (SearchManager.IsRegex.IsMatch(text)) return false;
            if (weak)
            {
                if (currentsRateAndTrimsAreNeededIfReturnTrue && str != null)
                {
                    isRate = SearchManager.IsRatingFilterWithOrWithoutOperator.IsMatch(str);
                    trim = SearchManager.TrimOperatorAndDoubleQuotations(str);
                    currents = SearchManager.Split(text, out trims);
                }
                return true;
            }
            else
            {
                if (string.IsNullOrEmpty(text))
                {
                    return false;
                }
                else
                {
                    isRate = SearchManager.IsRatingFilterWithOrWithoutOperator.IsMatch(str);
                    var trim_ = trim = SearchManager.TrimOperatorAndDoubleQuotations(str);
                    currents = SearchManager.Split(text, out trims);
                    return isRate ? trims.Any(t => !SearchManager.IsRatingFilterWithOrWithoutOperator.IsMatch(t)) : trims.Any(t => t != trim_);
                }
            }
        }

        private void filterAdd(string str, bool weak)
        {
            string trim;
            string[] trims;
            string[] currents;
            bool strIsRate;
            if (str == null) str = "";
            if (filterAddable(str, weak, out strIsRate, out currents, out trim, out trims))
            {
                var text = cbFilter.Text;
                if (text == null) text = "";



                var b = new StringBuilder(Math.Max(1, text.Length + str.Length + 1));
                var replaced = false;
                var sleStart = -1;
                var selLength = 0;
                for (var i = 0; i < currents.Length; i++)
                {
                    var current = currents[i];
                    if (strIsRate ? !SearchManager.IsRatingFilterWithOrWithoutOperator.IsMatch(current) : trims[i] != trim)
                    {
                        b.Append(current);
                        b.Append(' ');
                    }
                    else
                    {
                        replaced = true;
                        if (current != str)
                        {
                            if (sleStart < 0)
                            {
                                sleStart = b.Length;
                                selLength = str.Length;
                                b.Append(str);
                                b.Append(' ');
                            }
                        }
                    }
                }

                if (replaced)
                {
                    cbFilter.Text = b.ToString();
                    if (sleStart >= 0)
                    {
                        cbFilter.Select(sleStart, selLength);
                    }
                }
                else
                {
                    if (text.EndsWith(" ") || text == "") cbFilter.Text = text + str;
                    else cbFilter.Text = text + " " + str;
                    cbFilter.Select(cbFilter.Text.Length - str.Length, str.Length);
                }


            }
        }

        // 評価の基準値の増減ではドロップダウンを閉じない
        private void ratingReferenceValueMenuItem_MouseDown(object sender, MouseEventArgs e)
        {
            ratingFilterToolStripMenuItemDropDown.AutoClose = false;
        }

        private void setFilterDropDownLocation(ToolStripDropDown dropDown)
        {
            if (dropDown == null || !dropDown.Visible) return;
            var owner = dropDown.OwnerItem;
            var bounds = owner.Bounds;
            var owner2 = owner.Owner;
            var overflowDropDown = owner2?.OverflowButton?.DropDown;
            Point point;
            bool overflow;
            if (overflowDropDown != null && overflowDropDown.Visible)
            {
                overflow = true;
                point = overflowDropDown.PointToScreen(new Point(bounds.Left, bounds.Bottom - 1));
            }
            else
            {
                overflow = false;
                point = owner2.PointToScreen(new Point(bounds.Left, bounds.Bottom - 1));
            }
            var width = dropDown.Width;
            var height = dropDown.Height;
            var x = point.X;
            var y = point.Y;

            var wa = Screen.FromControl(this).WorkingArea;

            // フィルタ領域を避ける処理
            if (!overflow)
            {
                if (x < wa.Left) x = wa.Left;
                else if (x + width > wa.Right) x = wa.Right - width;
                var filterRectangle = cbFilter.RectangleToScreen(cbFilter.DisplayRectangle);
                if (x + width > filterRectangle.Left && x < filterRectangle.Right)
                {
                    y = filterRectangle.Bottom - 1;
                }
            }

            if (x < wa.Left) x = wa.Left;
            else if (x + width > wa.Right) x = wa.Right - width;
            if (y < wa.Top) y = wa.Top;
            else if (y + height > wa.Bottom) y = wa.Bottom - height;

            NativeMethods.MoveWindow(dropDown.Handle, x, y, width, height, false);
        }

        private void ratingFilterToolStripMenuItem_DropDownOpened(object sender, EventArgs e)
        {
            ratingFilterToolStripMenuItem_Dropdowned = true;
            setFilterDropDownLocation(ratingFilterToolStripMenuItemDropDown);
        }

        private void equalToXToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbFilter.Text = "r=" + ratingReferenceValue;
            cbFilter.SelectAll();
        }

        private void greaterThanOrEqualToXToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cbFilter.Text = "r>=" + ratingReferenceValue;
            cbFilter.SelectAll();
        }

        private void andEqualToXToolStripMenuItem_Click(object sender, EventArgs e)
        {
            filterAdd("r=" + ratingReferenceValue, weak: true);
        }

        private void andGreaterThanOrEqualToXToolStripMenuItem_Click(object sender, EventArgs e)
        {
            filterAdd("r>=" + ratingReferenceValue, weak: true);
        }

        private void orEqualToXToolStripMenuItem_Click(object sender, EventArgs e)
        {
            filterAdd("+r=" + ratingReferenceValue, weak: true);
        }

        private void orGreaterThanOrEqualToXToolStripMenuItem_Click(object sender, EventArgs e)
        {
            filterAdd("+r>=" + ratingReferenceValue, weak: true);
        }

        private void editDropdownOfFiltersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFilterEditForm();
        }

        private void openFilterEditForm()
        {
            var temp = AllowFullpower;
            AllowFullpower = false;
            var filteringList = (from object item in cbFilter.Items select item.ToString()).ToList();
            if (aliasesToFilteringStrings == null) aliasesToFilteringStrings = new Dictionary<string, string>();
            using (var filterEditForm = new FilterEditForm(filteringList, aliasesToFilteringStrings, invariantFilteringList, currentProfileColor))
            {
                filterEditForm.Icon = Icon;
                filterEditForm.ShowDialog(this);

                if (filterEditForm.Edited)
                {
                    var cbFilterText = cbFilter.Text;
                    if (!filteringList.Contains(cbFilterText))
                    {
                        if (aliasesToFilteringStrings.ContainsValue(cbFilterText))
                        {
                            cbFilter.Text = aliasesToFilteringStrings.FirstOrDefault(pair => pair.Value == cbFilterText).Key;
                        }
                        else
                        {
                            cbFilter.Text = "";
                        }
                    }
                    var array = new object[filteringList.Count + 1];
                    array[0] = "";
                    for (var i = 1; i < array.Length; i++) array[i] = filteringList[i - 1];
                    cbFilter.Items.Clear();
                    cbFilter.Items.AddRange(array);
                    invariantFilteringList = filterEditForm.invariantOut;
                }
            }
            AllowFullpower = temp;
        }

        private double[] setAspectRatioOption_Ratios;

        private void setAspectRatioOption(IReadOnlyList<Tuple<string, double>> options, IReadOnlyList<Tuple<string, double>> allOptions = null)
        {
            var items = thumbnailSizeToolStripMenuItem.DropDownItems;
            var start = items.IndexOf(thumbnailSizeBottomSeparator) + 1;
            while (items[start] != allRatiosToolStripMenuItem)
            {
                items.RemoveAt(start);
            }
            var count = 0;
            setAspectRatioOption_Ratios = new double[options.Count + (allOptions == null ? 0 : allOptions.Count)];

            var optionsCount = options.Count;
            var itemsCount = items.Count;
            var copy = new ToolStripItem[itemsCount + optionsCount];
            items.CopyTo(copy, 0);
            items.Clear();
            for (var i = itemsCount - 1; i >= start; i--) copy[i + optionsCount] = copy[i];

            foreach (var option in options)
            {
                copy[start++] = setAspectRatioOption_GetToolStripMenuItem(option, count++);
            }
            items.AddRange(copy);
            var allItems = allRatiosToolStripMenuItem.DropDownItems;
            allItems.Clear();
            if (allOptions != null)
            {
                var toolStripItems = new ToolStripItem[allOptions.Count];
                var k = 0;
                foreach (var option in allOptions)
                {
                    toolStripItems[k++] = setAspectRatioOption_GetToolStripMenuItem(option, count++);
                }
                allItems.AddRange(toolStripItems);
                allRatiosToolStripMenuItem.Visible = true;
            }
            else
            {
                allRatiosToolStripMenuItem.Visible = false;
            }
        }

        private ToolStripMenuItem setAspectRatioOption_GetToolStripMenuItem(Tuple<string, double> option, int count)
        {
            var cmsi = new ToolStripMenuItem(option.Item1);
            var aspect = option.Item2;

            setAspectRatioOption_Ratios[count] = aspect;


            cmsi.Checked = aspect == aspectRatio;

            cmsi.Click += (sender, e) =>
            {
                var toolStripMenuItem = sender as ToolStripMenuItem;
                if (!toolStripMenuItem.Checked)
                {
                    aspectRatio = aspect;
                    setAspectRatioCheckState();
                    ChangeThumbnailSize(null);
                    UpdatePreview();
                }
            };

            return cmsi;
        }

        private void setAspectRatioCheckState()
        {
            var items = getAspectRatioToolStripMenuItems();
            if (setAspectRatioOption_Ratios != null && setAspectRatioOption_Ratios.Length == items.Length)
            {
                var found1 = false;
                var found2 = false;
                var count = items.Length - allRatiosToolStripMenuItem.DropDownItems.Count;
                for (var i = 0; i < items.Length; i++)
                {
                    if (Math.Abs(setAspectRatioOption_Ratios[i] - aspectRatio) < 0.00000000001)
                    {
                        items[i].Checked = true;
                        if (i < count)
                        {
                            found1 = true;
                        }
                        else
                        {
                            found2 = true;
                        }
                    }
                    else
                    {
                        items[i].Checked = false;
                    }
                }
                allRatiosToolStripMenuItem.Checked = !found1 && found2;
            }
        }

        private ToolStripMenuItem[] getAspectRatioToolStripMenuItems()
        {
            var items = thumbnailSizeToolStripMenuItem.DropDownItems;
            var start = items.IndexOf(thumbnailSizeBottomSeparator) + 1;
            var result = new List<ToolStripMenuItem>();
            while (items[start] != allRatiosToolStripMenuItem)
            {
                result.Add(items[start++] as ToolStripMenuItem);
            }
            foreach (ToolStripMenuItem item in allRatiosToolStripMenuItem.DropDownItems)
            {
                result.Add(item);
            }
            return result.ToArray();
        }

        private void ratingFilterToolStripMenuItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (ratingFilterToolStripMenuItem_Paint_StarRectangle == Rectangle.Empty) return;

            var s = e.X - ratingFilterToolStripMenuItem_Paint_StarRectangle.Left;
            var r = (5 * s / ratingFilterToolStripMenuItem_Paint_StarRectangle.Width) + 1;
            ratingReferenceValue = r;

            ratingFilterToolStripMenuItem_DropDownOpening(null, null);
            setFilterDropDownLocation(ratingFilterToolStripMenuItemDropDown); // ratingReferenceValue の書き換えで位置が戻る＋フィルタ避け必要性の有無変化の可能性

        }

        // 呼び出し回数が少なく、また標準ライブラリの機能を多用しているので SizeDictionary は使わない
        Rectangle ratingFilterToolStripMenuItem_Paint_StarRectangle = Rectangle.Empty;

        bool ratingFilterToolStripMenuItem_Paint_SizeFixed = false;

        bool ratingFilterToolStripMenuItem_Dropdowned = false;

        private void ratingFilterToolStripMenuItem_Paint(object sender, PaintEventArgs e)
        {
            //var rectP = e.ClipRectangle; // この方法だと他のウインドウに隠れた部分がなくなる
            var rectP = new Rectangle(0, 0, ratingFilterToolStripMenuItem.Width, ratingFilterToolStripMenuItem.Height);


            var r = ratingReferenceValue;
            var p = "★★★★★".Substring(0, r);
            var s = "★★★★★".Substring(r);
            var size5 = TextRenderer.MeasureText(e.Graphics, "★★★★★", ratingFilterToolStripMenuItem.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding);
            var w5 = size5.Width;
            var h = size5.Height;
            var wr = TextRenderer.MeasureText(e.Graphics, p, ratingFilterToolStripMenuItem.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width;

            rectP.X += (rectP.Width - w5) / 2;
            rectP.Y += (rectP.Height - h) / 2;
            var rectS = rectP;
            rectS.X += wr;

            var pw = TextRenderer.MeasureText(e.Graphics, ".", ratingFilterToolStripMenuItem.Font, new Size(int.MaxValue, int.MinValue), TextFormatFlags.NoPadding).Width;
            ratingFilterToolStripMenuItem_Paint_StarRectangle = rectP;
            ratingFilterToolStripMenuItem_Paint_StarRectangle.X += pw;
            ratingFilterToolStripMenuItem_Paint_StarRectangle.Size = new Size(w5, h);

            Color gold, gray;
            if (ratingFilterToolStripMenuItem_Dropdowned || ratingFilterToolStripMenuItem.Selected)
            {
                gold = Color.DarkGoldenrod;
                gray = Color.LightGray;
            }
            else
            {
                gold = Color.Gold;
                gray = Color.LightSlateGray;

                if (!ratingFilterToolStripMenuItem_Paint_SizeFixed)
                {
                    // ハイライト状態でなければ塗りつぶすメリットの方が大きい
                    e.Graphics.FillRectangle(Brushes.Black, rectP);
                }
            }


            if (!ratingFilterToolStripMenuItem_Paint_SizeFixed)
            {
                ratingFilterToolStripMenuItem.AutoSize = false;
                ratingFilterToolStripMenuItem.Text = "";
                ratingFilterToolStripMenuItem_Paint_SizeFixed = true;
            }

            TextRenderer.DrawText(e.Graphics, p, ratingFilterToolStripMenuItem.Font, rectP, gold, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, s, ratingFilterToolStripMenuItem.Font, rectS, gray, TextFormatFlags.NoPadding);
        }
    }
}
