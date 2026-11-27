using Alteridem.WinTouch;
using Common;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZipPla
{
    public partial class ViewerForm
    {
        /// <param name="closing">
        /// この保存のあとウィンドウを閉じるなら true。履歴への記録に続けて書庫のアクセス日時を更新するために、書庫を閉じる。
        /// 閉じないとき（ウィンドウの複製）は false。書庫は開いたままにし、アクセス日時の更新は次に書庫を閉じるときまで待つ
        /// （true のままだと書庫が閉じたままになり、まだ読んでいないページがエラー画像になった）
        /// </param>
        private bool SaveSettings(bool closing = true)
        {
            try
            {
                var fullscreen = FullScreen;
                var windowState = WindowState;
                var maxmized = (fullscreen ? prevFormWindowState : windowState == FormWindowState.Minimized ? ViewerForm_SizeChanged_BeforeMinimizedState : windowState) == FormWindowState.Maximized;

                var config = new ViewerFormConfig();
                config.FullScreen = fullscreen;
                config.Maximized = maxmized;
                if (!fullscreen && !maxmized && windowState == FormWindowState.Normal)
                {
                    config.Size  = Size;
                    config.Point = Location;
                }
                else
                {
                    {
                        var bounds = BetterFormRestoreBounds.GetMovedWindowBounds(this, normalStateLocation ?? Location, prevSize, betterFormRestoreBound.BoundsLastNoMinimized);
                        config.Point = bounds.Location;
                        config.Size = bounds.Size;


                    }

                }

                if (!fullscreen && (/*WindowState == FormWindowState.Normal ||*/ WindowState == FormWindowState.Maximized))
                {
                    var pbViewSize = pbView.Bounds;
                    var bounds = Screen.FromControl(this).Bounds;

                    config.MaximizedSizeWidthMargin = bounds.Width - pbViewSize.Width;
                    if (HideUI)
                    {
                        // 非表示のコントロールのサイズは取得できないので menuStrip_Bottom などを使う
                        //var mtbPage_Top = pbView.Bottom + mtbPageTop_As_Reference_pbViewBottom;
                        config.MaximizedSizeHeightMargin = bounds.Height - pbViewSize.Height + (menuStrip_Bottom /*+ pbViewSize.Height*/ - (pbViewSize.Top + mtbPageTop_As_Reference_pbViewBottom));
                        config.MaximizedSizeHeightMarginInHideMode = bounds.Height - pbViewSize.Height;
                    }
                    else
                    {
                        config.MaximizedSizeHeightMargin = bounds.Height - pbViewSize.Height;
                        config.MaximizedSizeHeightMarginInHideMode = bounds.Height - pbViewSize.Height - (menuStrip.Bottom + statusStrip.Bottom - pnlSeekbar.Top);
                    }
                }

                config.NormalScalingAlgorithm = StandardScalingAlgorithm.ScaleDown;
                config.UseAreaAverageWhenNormalUpsizing = useAreaAverageWhenUpsizingNormalToolStripMenuItem.Checked;
                config.ResizeGammaNormal = convert(ResizeGammaNormal);
                config.MagnifierScalingAlgorithm = MagnifierScalingAlgorithm.ScaleDown;
                config.UseAreaAverageWhenMagnifierUpsizing = useAreaAverageWhenUpsizingToolStripMenuItem.Checked;
                config.ResizeGammaMagnifier = convert(ResizeGammaMagnifier);

                config.MouseGestureEnabled = mgView.Enabled;
                config.MouseGestureLineWidth = mgView.GetWidth();
                config.MouseGestureLineColor = mgView.GetColor();
                config.MouseGestureCommands = mgView.Actions != null && mgView.Actions.Length > 0 ?
                    (from a in mgView.Actions select new MouseGestureCommand() { Gesture = a.Gesture, Command = (Command)a.Key }).ToArray() : null;
                config.KeyboardShortcutCommands = ksViewForm.Actions != null && ksViewForm.Actions.Length > 0 ?
                     (from a in ksViewForm.Actions select new KeyboardShortcutCommand(a)).ToArray() : null;
                config.SeekBarMode = SeekBarMode;
                config.SlideshowInterval = tmSlideshow.Interval;
                config.SlideshowRepeat = SlideshowRepeat;
                config.SlideshowGlobal = SlideshowGlobal;
                config.ImageFilter = imageFilter;

                config.MaxDivision = MaxDivision;
                config.MinPageCountInWindow = MinPageCountInWindow;

                config.CoverBindingMode = CoverBindingMode;
                config.ArchivesInArchiveMode = ArchivesInArchiveMode;
                config.BackColor = pbView.BackColor;

                config.AlwaysHideUI = AlwaysHideUI;

                config.ViewSettingHintMode = ViewSettingHintMode;

                config.Save();

                if (saveToHistory)
                {
                    trySaveCurrentConditionToHistory(closing ? SelfLastAccessTimeUpdateMode.Force : SelfLastAccessTimeUpdateMode.Reserve);
                }

                return true;
            }
            catch (Exception error)
            {
                Program.AlertError(error);
                return false;
            }
        }

        private static double convert(GammaConversion gamma)
        {
            switch (gamma)
            {
                case GammaConversion.Value2_2: return 2.2;
                default: return 1.0;
            }
        }

        private static GammaConversion convert(double gamma)
        {
            const double sqrt22 = 1.4832396974191325897422794881601426121959808638195;
            return gamma < sqrt22 ? GammaConversion.Value1_0 : GammaConversion.Value2_2;
        }

        private bool checkNgen = false;

        private bool PseudoMaximized = false;

        private Size PseudoMaximizedSize;

        private enum LoadSettings_Result { ConfigFileNotFound, ConfigLoadError, Completed }

        FormWindowState? LoadSettings_FormWindowStateFromSetteingFile = null;

        DateTime LoadSettings_LoadedFileTimeStampForLookAhead = DateTime.MinValue;

        DateTime? LoadSettings_LoadedFileTimeStampForLookAhead_Setter = null;

        private ViewerFormConfig LoadSettings_LastLoadedConfig = null;

        private GeneralConfig LoadSettings_LastLoadedGeneralConfig = null;

        private LoadSettings_Result LoadSettings(CommandLineOptionInfo info, bool reloadConfig, bool reloadLang)// InitialFullscreenMode initialFullscreenMode, bool? initialAlwaysHide, Color? backColor, ArchivesInArchiveMode? aia)
        {
            bool fullscreen;
            bool maximized;
            Point? point;
            Size size, maximizedSize;
            bool mouseGestureEnabled;
            bool alwaysHide;
            double mouseGestureLineWidth;
            Color mouseGestureLineColor;
            SeekBarMode seekBarMode;
            MouseGestureCommand[] mgCommands;
            KeyboardShortcutCommand[] ksCommands;
            Rectangle? currentScreen = null;
            try
            {
                var independent = ipcLookAheadInfo == null;
                if (!independent)
                {
                    LoadSettings_LoadedFileTimeStampForLookAhead = LoadSettings_LoadedFileTimeStampForLookAhead_Setter ?? Configuration.GetLastWriteTime();
                    LoadSettings_LoadedFileTimeStampForLookAhead_Setter = null;
                }
                ViewerFormConfig config;
                if (reloadConfig || LoadSettings_LastLoadedConfig == null)
                {
                    config = new ViewerFormConfig();
                    LoadSettings_LastLoadedConfig = config;
                }
                else
                {
                    config = LoadSettings_LastLoadedConfig;
                }
                var initialFullscreenMode = info?.InitialFullscreenMode ?? InitialFullscreenMode.Default;
                fullscreen = initialFullscreenMode == InitialFullscreenMode.ForceFullscreen ? true : initialFullscreenMode == InitialFullscreenMode.ForceWindow ? false : config.FullScreen;
                var initialAlwaysHide = info?.AlwaysHideUI;
                alwaysHide = initialAlwaysHide != null ? (bool)initialAlwaysHide: config.AlwaysHideUI;
                maximized = config.Maximized;
                point = config.Point;
                size = config.Size; if (size == Size.Empty) size = Size;
                if (config.MaximizedSizeWidthMargin <= 0 || config.MaximizedSizeHeightMargin <= 0 || config.MaximizedSizeHeightMarginInHideMode <= 0)
                {
                    maximizedSize = pbView.Size;
                }
                else
                {
                    currentScreen = point != null ? Screen.FromRectangle(new Rectangle((Point)point, size)).Bounds : Screen.FromControl(this).Bounds;
                    var bounds = (Rectangle)currentScreen;
                    int maximizedHeight;
                    if (!alwaysHide)
                    {
                        maximizedHeight = bounds.Height - config.MaximizedSizeHeightMargin;
                    }
                    else
                    {
                        maximizedHeight = bounds.Height - config.MaximizedSizeHeightMarginInHideMode;
                    }
                    maximizedSize = new Size(bounds.Width - config.MaximizedSizeWidthMargin, maximizedHeight);
                    if (maximizedSize.Width <= 0 || maximizedSize.Height <= 0) maximizedSize = pbView.Size;
                }

                StandardScalingAlgorithm.ScaleDown = config.NormalScalingAlgorithm;
                if (config.UseAreaAverageWhenNormalUpsizing)
                {
                    useAreaAverageWhenUpsizingNormalToolStripMenuItem.Checked = true;
                    StandardScalingAlgorithm.ScaleUp = ScalingAlgorithm.AreaAverage;
                }
                else
                {
                    useAreaAverageWhenUpsizingNormalToolStripMenuItem.Checked = false;
                    StandardScalingAlgorithm.ScaleUp = config.NormalScalingAlgorithm;
                }
                MagnifierScalingAlgorithm.ScaleDown = config.MagnifierScalingAlgorithm;
                if (config.UseAreaAverageWhenNormalUpsizing)
                {
                    useAreaAverageWhenUpsizingToolStripMenuItem.Checked = true;
                    MagnifierScalingAlgorithm.ScaleUp = ScalingAlgorithm.AreaAverage;
                }
                else
                {
                    useAreaAverageWhenUpsizingToolStripMenuItem.Checked = false;
                    MagnifierScalingAlgorithm.ScaleUp = config.MagnifierScalingAlgorithm;
                }
                ResizeGammaNormal = convert(config.ResizeGammaNormal);
                ResizeGammaMagnifier = convert(config.ResizeGammaMagnifier);

                mouseGestureEnabled = config.MouseGestureEnabled;
                mouseGestureLineWidth = config.MouseGestureLineWidth;
                mouseGestureLineColor = config.MouseGestureLineColor;
                mgCommands = config.MouseGestureCommands;
                ksCommands = config.KeyboardShortcutCommands;
                seekBarMode = config.SeekBarMode;
                tmSlideshow.Interval = config.SlideshowInterval;
                SlideshowRepeat = config.SlideshowRepeat;
                SlideshowGlobal = config.SlideshowGlobal;
                if(info?.OpenInPreviousImageFilterSetting != false) imageFilter.Set(config.ImageFilter);
                initialViewerFormImageFilter = config.ImageFilter;

                requestedBindingMode = info?.DefaultBindingMode;
                
                pbView.BackColor = info?.BackColor ?? config.BackColor;
                
                ArchivesInArchiveMode = info?.ArchivesInArchiveMode ?? config.ArchivesInArchiveMode;

                ReadOnMemoryMode = info?.ReadOnMemoryMode ?? config.ReadOnMemoryMode;
                
                CoverBindingMode = info?.CoverBindingMode ?? config.CoverBindingMode;

                ViewSettingHintMode = config.ViewSettingHintMode;

                MaxDivision = Math.Max(1, Math.Min(2, config.MaxDivision));
                MinPageCountInWindow = Math.Max(1, Math.Min(2, config.MinPageCountInWindow));
                
                GeneralConfig generalConfig;
                if (reloadConfig || LoadSettings_LastLoadedGeneralConfig == null)
                {
                    generalConfig = new GeneralConfig();
                    LoadSettings_LastLoadedGeneralConfig = generalConfig;
                }
                else
                {
                    generalConfig = LoadSettings_LastLoadedGeneralConfig;
                }
                
                BuiltInViewerMemoryLimit = generalConfig.BuiltInViewerMemoryLimit;
                checkNgen = generalConfig.CheckNgen;

                if (reloadLang)
                {
                    try
                    {
                        var newLanguage = Message.CultureInfoParse(generalConfig.Language);
                        if (Message.CurrentLanguage != newLanguage)
                        {
                            Message.CurrentLanguage = newLanguage;
                        }
                    }
                    catch
                    {
                        try
                        {
                            if (Message.CurrentLanguage != Message.SystemLanguage)
                            {
                                Message.CurrentLanguage = Message.SystemLanguage;
                            }
                        }
                        catch
                        {
                            if (Message.CurrentLanguage != Message.DefaultLanguage)
                            {
                                Message.CurrentLanguage = Message.DefaultLanguage;
                            }
                        }
                    }

                }
            }
            catch (Exception error)
            {
                Program.AlertError(error);
                return LoadSettings_Result.ConfigLoadError;
            }

            // AlwaysHideUI の setter は fullscreen ではなく FullScreen を見るのでここでは使えない
            alwaysHideUI = alwaysHide;
            alwaysAutoHideUIToolStripMenuItem.Checked = alwaysHide;
            if (!alwaysHide && !fullscreen)
            {
                SetHideUI();
            }
            
            if (fullscreen) LoadSettings_FormWindowStateFromSetteingFile = maximized ? FormWindowState.Maximized : FormWindowState.Normal;


            LoadSettingToLoad_FormLocation = null;
            ViewerForm_SizeChanged_CurrentSize = Size.Empty;
            if (!fullscreen)
            {
                //normalStateSize = 


                SetSizeWithPbSizeMemoryForMinimize(size);
                prevSize = size;

                if (!maximized) ViewerForm_SizeChanged_CurrentSize = size;
                if (point != null)
                {
                    var p = (Point)point;

                    // 最も近いスクリーンにパックする
                    var settingRect = new Rectangle(p, size);
                    settingRect = BetterFormRestoreBounds.Pack(settingRect, Screen.FromRectangle(settingRect).WorkingArea);
                    p = settingRect.Location;
                    normalStateLocation = p;

                    if (ViewerForm_Load_Called)
                    {
                        Location = p;
                    }
                    else
                    {
                        LoadSettingToLoad_FormLocation = p;
                    }
                }
            }
            else
            {
                prevSize = size;
                SetSizeWithPbSizeMemoryForMinimize(size);
                if (point != null)
                {
                    var p = (Point)point;
                    var settingRect = new Rectangle(p, size);
                    var rect = currentScreen ?? Screen.FromRectangle(settingRect).Bounds;
                    if (rect.IntersectsWith(settingRect))
                    {
                        normalStateLocation = point;
                        if (ViewerForm_Load_Called)
                        {
                            Location = p;
                        }
                        else
                        {
                            LoadSettingToLoad_FormLocation = p;
                        }
                    }
                }

            }

            // ピクチャーボックスの位置調整などがあるので常に行う
            if (ViewerForm_Shown_Called)
            {
                LoadSettingToShown(fullscreen, maximized, maximizedSize, size);
            }
            else
            {
                LoadSettingToShown_Reserved = true;
                LoadSettingToShown_fullscreen = fullscreen;
                LoadSettingToShown_maximized = maximized;
                LoadSettingToShown_maximizedSize = maximizedSize;
                LoadSettingToShown_size = size;
            }

            mgView.Enabled = mouseGestureEnabled;
            mgView.Width = mouseGestureLineWidth;
            mgView.Color = mouseGestureLineColor;
            if (mgCommands != null)
            {
                var actions = new List<MouseGestureAction>();
                var template = getMouseGestureSettingTemplate(CommandSender.MouseGesture);
                foreach (var c in mgCommands)
                {
                    var gesture = c.Gesture;
                    if (gesture == null || gesture.Length == 0) continue;
                    var key = (int)c.Command;
                    var action = template.FirstOrDefault(t => t.Key == key);
                    if (action == default(MouseGestureSettingTemplate)) continue;
                    actions.Add(new MouseGestureAction(key, gesture, action.Action));
                }
                mgView.Actions = actions.ToArray();
            }
            else
            {
                mgView.Actions = null;
            }

            SeekBarMode = seekBarMode;

            // メニューへの反映は LoadSetting に引き続き呼び出される SetMessages で行われる
            if (ksCommands != null)
            {
                var actions = new List<KeyboardShortcutAction>();
                var template = getMouseGestureSettingTemplate(CommandSender.KeyboardShortcut);
                foreach (var c in ksCommands)
                {
                    var shortcut = c.GetShortcut();
                    if (shortcut == null) continue;
                    var command = c.Command;
                    var key = (int)command;
                    var action = template.FirstOrDefault(t => t.Key == key);
                    if (action == default(MouseGestureSettingTemplate)) continue;
                    actions.Add(new KeyboardShortcutAction(key, action.Action, ContinuousExecutionCommands.Contains(command), shortcut));
                }
                ksViewForm.Actions = actions.ToArray();
            }
            else
            {
                ksViewForm.Actions = null;
            }

            return LoadSettings_Result.Completed;
        }

        private void SetSizeWithPbSizeMemoryForMinimize(Size size)
        {
            if (Size != size)
            {
                Size = size;
            }
            else
            {
                ViewerForm_SizeChanged_pbBoundsMemoryForMinimize = pbView.Bounds;
            }
        }

        private Point? LoadSettingToLoad_FormLocation = null;

        private bool LoadSettingToShown_Reserved = false;

        private bool LoadSettingToShown_fullscreen;

        private bool LoadSettingToShown_maximized;

        private Size LoadSettingToShown_maximizedSize;

        private Size LoadSettingToShown_size;

        private void LoadSettingToShown(bool fullscreen, bool maximized, Size maximizedSize, Size size)
        {
            fullScreenModeChange(fullscreen);
            if (maximized && !fullscreen)
            {
                if (ipcLookAheadInfo != null)
                {
                    PseudoMaximized = true;
                    PseudoMaximizedSize = maximizedSize;
                }
                else
                {
                    WindowState = FormWindowState.Maximized;
                }
            }
            else
            {
                if (ipcLookAheadInfo != null)
                {
                    PseudoMaximized = false;
                    PseudoMaximizedSize = default(Size);
                }
            }

            if (fullscreen && ipcLookAheadInfo == null)
            {
                prevSize = size;
            }
            else
            {
                if (fullscreen || maximized)
                {
                    prevSize = size;
                }
                else
                {
                    prevSize = Size.Empty;
                }
            }
        }

        private BetterFormRestoreBounds betterFormRestoreBound;

        bool ViewerForm_Shown_Called = false;

        bool ViewerForm_Shown_VisibleToFalse = false;

        private void ViewerForm_Shown(object sender, EventArgs e)
        {
            if (ViewerForm_Shown_VisibleToFalse)
            {
                Visible = false;
            }
            if (LoadSettingToShown_Reserved)
            {
                LoadSettingToShown_Reserved = false;
                LoadSettingToShown(
                    LoadSettingToShown_fullscreen,
                    LoadSettingToShown_maximized,
                    LoadSettingToShown_maximizedSize,
                    LoadSettingToShown_size);
            }

            betterFormRestoreBound = new BetterFormRestoreBounds(this);

            Configuration.ClosePrepareMode(); // Visible = false の先読みプロセスでも起動時にここに到達することを確認済み
            

            ViewerForm_Shown_Called = true;
        }

        public static void SetOneTimeEventHandler(EventHandler eventHandler, Action<EventHandler> adder, Action<EventHandler> remover) 
        {
            EventHandler withRemover = null;
            withRemover = (sender, e) =>
            {
                eventHandler(sender, e);
                remover(withRemover);
            };
            adder(withRemover);
        }

        public static void SetOneTimeEventHandler(Action eventHandler, Action<EventHandler> adder, Action<EventHandler> remover)
        {
            EventHandler withRemover = null;
            withRemover = (sender, e) =>
            {
                eventHandler();
                remover(withRemover);
            };
            adder(withRemover);
        }

        private void ViewerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            FlushReportsToCatalog(); // 最後のページの選択変更の依頼を確実に届ける（V7）
            if (ipcLookAheadInfo == null && !DoNotSaveSetting)
            {
                SaveSettings();
            }
            FlushHistoryWrites(10000); // 背景で書いている履歴を書き終えてから終わる（V26）
        }

        // 履歴（History.sor）への書き込みを、UI スレッドではなく背景で直列に行う（V26）。
        // 本を切り替えるたびに、今の本の保存と新しい本の追加で履歴を 2 回読み書きし、そのたびに UI が止まっていた（1 万件で 1 回 15〜70ms）。
        // たまっている分は 1 回の AddBookmarkData にまとめる（後のものほど新しいページ）。カタログへの報告は従来どおりすぐ送る
        // （カタログは報告のページをそのまま使い、History.sor を読み直さない）
        private readonly object historyWriteLock = new object();

        private readonly List<string> historyWritePaths = new List<string>();

        private readonly List<int> historyWritePages = new List<int>();

        private int historyWriteLimit = int.MaxValue;

        private Task historyWriteTask = Task.CompletedTask;

        private void AddHistoryInBackground(string path, int page)
        {
            if (string.IsNullOrEmpty(path)) return;
            var limit = Program.GetLimitOfHistoryCount(); // 設定の読み込みは UI スレッドで
            lock (historyWriteLock)
            {
                historyWritePaths.Add(path);
                historyWritePages.Add(page);
                historyWriteLimit = limit;
                if (historyWritePaths.Count == 1)
                {
                    historyWriteTask = historyWriteTask.ContinueWith(_ => WritePendingHistory(), TaskScheduler.Default);
                }
            }
        }

        private void WritePendingHistory()
        {
            string[] paths;
            int[] pages;
            int limit;
            lock (historyWriteLock)
            {
                if (historyWritePaths.Count == 0) return;
                paths = historyWritePaths.ToArray();
                pages = historyWritePages.ToArray();
                limit = historyWriteLimit;
                historyWritePaths.Clear();
                historyWritePages.Clear();
            }
            try
            {
                VirtualFolder.AddBookmarkData(Program.HistorySorPath, paths, pages, limitOfItemsCount: limit, deleteLostPath: true);
            }
            catch { }
        }

        private void FlushHistoryWrites(int millisecondsTimeout)
        {
            Task task;
            lock (historyWriteLock) task = historyWriteTask;
            try { task.Wait(millisecondsTimeout); } catch { }
        }

        private void trySaveCurrentConditionToHistory(SelfLastAccessTimeUpdateMode m)
        {
            try
            {


                var page = loader == null || loader.Type != PackedImageLoader.PackType.Directory ? Math.Max(0, currentPage) : -1;
                var targetPath = loader != null && loader.Type != PackedImageLoader.PackType.Directory ? currentArchiveFilePath : currentStartingFilePath;
                AddHistoryInBackground(targetPath, page); // V26
                SendSorMessageToCatalogForm(targetPath, Program.HistorySorPath, page, requestToUpdateLastAccessTime: true, m: m);
            }
            catch { }
        }


        bool pbView_SizeChanged_SizeChanging = false;

        private void pbView_SizeChanged(object sender, EventArgs e)
        {

            var size = pbView.Bounds;
            if (size.Width > 0 && size.Height > 0)
            {
                ViewerForm_SizeChanged_pbBoundsMemoryForMinimize = size;

                if (!pbView_SizeChanged_SizeChanging && /*EntryNumberArray*/EntryArray != null && /*pbView.Width > 0 && pbView.Height > 0 &&*/
                    !FullScreen_pseudoFullScreen && !PseudoMaximized) // 最小化時にはリロードしない、擬似状態からの復帰時にはリロードしない
                {
                    // ウィンドウの枠をドラッグしている間は読み直さない（途中の大きさごとに全ページを読み直していた）。離したときに読み直す（V4）
                    if (pbView_SizeChanged_UserResizing)
                    {
                        pbView_SizeChanged_ReloadPending = true;
                    }
                    else
                    {
                        Reload(keepSameSizePages: true);
                    }
                }
            }
        }
    }
}
