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
        private long ShowStatusBar_NextID = 0;

        private long ShowStatusBar_CurrentID = 0;

        private void ShowStatusBar() { ShowStatusBar(tvCatalog.SelectedIndices); }

        // 項目の読み込みが終わるたびに呼ぶ。複数選択中の ShowStatusBar は選択項目をすべて走査し、フォルダのサイズの集計もやり直すので、
        // 全選択したまま読み込みが進むと O(n^2) になる。複数選択中はまとめて、更新にかかった時間の 4 倍（100ms〜1 秒）に 1 回にする（K4）。
        // UI スレッドから呼ぶこと
        private System.Windows.Forms.Timer ShowStatusBarAfterItemLoaded_Timer;

        private void ShowStatusBarAfterItemLoaded()
        {
            if (tvCatalog.SelectedIndicesCount <= 1)
            {
                ShowStatusBarAfterItemLoaded_Timer?.Stop();
                ShowStatusBar();
                return;
            }
            if (ShowStatusBarAfterItemLoaded_Timer == null)
            {
                ShowStatusBarAfterItemLoaded_Timer = new System.Windows.Forms.Timer { Interval = 100 };
                ShowStatusBarAfterItemLoaded_Timer.Tick += (sender, e) =>
                {
                    ShowStatusBarAfterItemLoaded_Timer.Stop();
                    if (IsDisposed) return;
                    var start = Environment.TickCount;
                    ShowStatusBar();
                    var elapsed = unchecked(Environment.TickCount - start);
                    ShowStatusBarAfterItemLoaded_Timer.Interval = Math.Max(100, Math.Min(1000, elapsed * 4));
                };
            }
            if (!ShowStatusBarAfterItemLoaded_Timer.Enabled) ShowStatusBarAfterItemLoaded_Timer.Start();
        }

        private void ShowStatusBar(IEnumerable<int> selectedIndices)
        {
            var id = ShowStatusBar_NextID;
            ShowStatusBar_CurrentID = id;
            ShowStatusBar_NextID = ShowStatusBar_NextID < long.MaxValue ? ShowStatusBar_NextID + 1 : 0;

            var itemsCount = -1;
            var selectedCount = 0;
            long fileSize = 0;
            bool? ffmpegExists = null;
            List<string> dirPathList = null;
            if (selectedIndices != null && selectedIndices.Any(i => i >= 0))
            {
                lock (ZipPathArrayLocker)
                {
                    if (ZipPathArray != null)
                    {
                        itemsCount = ZipPathArray.Length;
                        foreach (var selectedIndex in selectedIndices)
                        {
                            if (0 <= selectedIndex && selectedIndex < itemsCount)
                            {
                                selectedCount++;
                                var name = ZipNameArray[selectedIndex];
                                var isDir = name.EndsWith(Path.DirectorySeparatorChar.ToString());
                                if (selectedCount == 1)
                                {
                                    var path = ZipPathArray[selectedIndex];
                                    if (selectedFileNameToolStripTextBox.Text != path) selectedFileNameToolStripTextBox.Text = path;
                                    if (ImageLoader.SupportsGetImageInfo(path, ref ffmpegExists))
                                    {
                                        var info = ImageInfoArray[selectedIndex];
                                        if (info == null)
                                        {
                                            selectedFileContainsCountToolStripStatusLabel.Text = "?x?";
                                        }
                                        else
                                        {
                                            var size = info.Size;
                                            selectedFileContainsCountToolStripStatusLabel.Text = $"{size.Width}x{size.Height}";
                                        }
                                    }
                                    else if (isDir || PackedImageLoader.Supports(path))
                                    {
                                        selectedFileContainsCountToolStripStatusLabel.Text = Program.GetNPageString(IfNegativeThenQuestion(FileCountArray[selectedIndex]));
                                    }
                                    else if (MovieThumbnailLoader.Supports(path))
                                    {
                                        var info = MovieInfoArray[selectedIndex];
                                        if (info == null)
                                        {
                                            selectedFileContainsCountToolStripStatusLabel.Text = "";
                                        }
                                        else
                                        {
                                            selectedFileContainsCountToolStripStatusLabel.Text = info.ToShortString();
                                        }
                                    }
                                    else
                                    {
                                        selectedFileContainsCountToolStripStatusLabel.Text = "";
                                    }
                                    if (ffmpegExists != null) RecentFfmpegExists = (bool)ffmpegExists;
                                }

                                if (fileSize >= 0)
                                {
                                    var thisFileSize = ZipSizeArray[selectedIndex];
                                    if (thisFileSize >= 0)
                                    {
                                        fileSize += thisFileSize;
                                    }
                                    else if (isDir)
                                    {
                                        if (dirPathList == null) dirPathList = new List<string>();
                                        dirPathList.Add(ZipPathArray[selectedIndex]);
                                    }
                                    else
                                    {
                                        fileSize = -1;
                                        dirPathList = null;
                                    }
                                }

                            }
                        }
                    }
                }
            }
            {
                if (fileSize >= 0)
                {
                    if (dirPathList == null)
                    {
                        fileSizeToolStripStatusLabel.Text = Program.GetFormatSizeString(fileSize, Message.Bytes, simpleForm: true);
                    }
                    else
                    {
                        fileSizeToolStripStatusLabel.Text = Program.GetFormatSizeString(fileSize, Message.Bytes, simpleForm: true) + "+";
                    }
                }
                else
                {
                    fileSizeToolStripStatusLabel.Text = $"? {Message.Bytes}";
                }
            }
            tvCatalog_ShowIndexToDataIndexChanged(null, null);

            if (selectedCount != 1)
            {
                var selectedIndicesCount = tvCatalog.SelectedIndicesCount;
                if (selectedIndicesCount > 1)
                {
                    selectedFileNameToolStripTextBox.Text = Message._1ItemsAreSelected.Replace("$1", selectedIndicesCount.ToString());
                }
                else
                {
                    selectedFileNameToolStripTextBox.Text = "";
                }
                selectedFileContainsCountToolStripStatusLabel.Text = "";
                if (selectedCount < 1) fileSizeToolStripStatusLabel.Text = "";
            }

            if (fileSize >= 0 && dirPathList != null)
            {
                var lastCheck = (Environment.TickCount & int.MaxValue); // 最も高速だがループで大小関係が狂うので注意 http://d.hatena.ne.jp/saiya_moebius/20100819/1282201466
                Task.Run(() =>
                {
                    foreach (var dirPath in dirPathList)
                    {
                        try
                        {
                            foreach (var file in new DirectoryInfo(dirPath).EnumerateFiles("*", SearchOption.AllDirectories))
                            {
                                fileSize += file.Length;
                                const int span = 50;
                                if (id != ShowStatusBar_CurrentID) return;
                                var now = (Environment.TickCount & int.MaxValue);
                                bool over;
                                if (lastCheck <= int.MaxValue - span)
                                {
                                    over = now >= lastCheck + span || now < lastCheck;
                                }
                                else
                                {
                                    over = now >= lastCheck - (int.MaxValue - (span - 1)) && now < lastCheck;
                                }
                                if (over)
                                {
                                    lastCheck = now;
                                    Invoke(((MethodInvoker)(() =>
                                    {
                                        if (id != ShowStatusBar_CurrentID) return;
                                        fileSizeToolStripStatusLabel.Text = Program.GetFormatSizeString(fileSize, Message.Bytes, simpleForm: true) + "+";
                                    })));
                                }
                                if (id != ShowStatusBar_CurrentID) return;
                            }
                            Invoke(((MethodInvoker)(() =>
                            {
                                if (id != ShowStatusBar_CurrentID) return;
                                fileSizeToolStripStatusLabel.Text = Program.GetFormatSizeString(fileSize, Message.Bytes, simpleForm: true);
                            })));
                        }
                        catch
                        {
                            Invoke(((MethodInvoker)(() =>
                            {
                                if (id != ShowStatusBar_CurrentID) return;
                                fileSizeToolStripStatusLabel.Text = fileSizeToolStripStatusLabel.Text = $"? {Message.Bytes}";
                            })));
                            return;
                        }
                    }
                });

            }
        }
    }
}
