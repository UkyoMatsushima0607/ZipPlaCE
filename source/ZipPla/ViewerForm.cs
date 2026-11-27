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
    public partial class ViewerForm : Form, IMultipleLanguages
    {
        class ViewerFormImageInfo : ImageInfo
        {
            public int? MaxColorDiff;

            public ViewerFormImageInfo(ImageInfo imageInfo, int? maxColorDiff) : base(imageInfo.Size, imageInfo.BitPerPixel)
            {
                MaxColorDiff = maxColorDiff;
            }
        }

        //private int[] EntryNumberArray = null;
        private PackedImageLoader.PackedImageEntry[] EntryArray = null;
        private string[] EntryLongPathArray = null;
        private BitmapEx[] PreFilteredImageArray = null;
        private ViewerFormImageInfo[] OriginalImageInfoArray = null;
        private Size[] ResizedSizeArray = null;
        private VirtualBitmapEx[] ResizedImageArray = null;
        // ResizedImageArray[i] を登録したときに usedMemory へ計上した量（原寸を捨てたら減らす）（C1）
        private ulong[] AccountedSizeArray = null;
        // 原寸（PreFilteredImageArray[i]）の状態（C1）。Trimmed: 原寸を持たず、縮小画像は縮小デコードから作った（表示の近くに来たら読み直す）。
        // Reloading: 読み直しを依頼済みで、新しい縮小画像が登録されるまで（拡大画像はそれまで作らない）。
        // Discarded: 縮小画像は原寸から作ったが、原寸は捨てた（画質のための読み直しは不要。ルーペで原寸が要るときだけ読み直す）（V2）
        private byte[] OriginalStateArray = null;
        private const byte OriginalState_Kept = 0, OriginalState_Trimmed = 1, OriginalState_Reloading = 2, OriginalState_Discarded = 3;
        // 直近の読み込みで JPEG を縮小デコードし、原寸を持っていないページ（C5）。登録時に原寸の状態を Trimmed にする
        private bool[] ReducedDecodedArray = null;
        // 縮小画像を作ったときに縮小の基にした大きさ（GetResizedSize の引数）。ウィンドウの大きさを変えたとき、縮小後の大きさが変わらないページを残すのに使う（V4）
        private Size[] LayoutSourceSizeArray = null;
        // ウィンドウの大きさを変えたときに縮小画像を残したページ。読み込みジョブを 1 回だけ読み飛ばす（V4）
        private bool[] KeptAfterResizeArray = null;
        private int currentPage_field = -1;
        private bool onePageModeForNext = false;
        private int pageDirection = -1;

        private bool TopMostInFullscreen = false;

        private ReadOnMemoryMode readOnMemoryMode = ReadOnMemoryMode.None;
        private ReadOnMemoryMode ReadOnMemoryMode
        {
            get => readOnMemoryMode;
            set
            {
                if (value != readOnMemoryMode)
                {
                    readOnMemoryMode = value;
                    readOnMemoryNoneToolStripMenuItem.Checked = value == ReadOnMemoryMode.None;
                    readOnMemoryExceptLookAheadToolStripMenuItem.Checked = value == ReadOnMemoryMode.ExceptLookAhead;
                    readOnMemoryAlwaysToolStripMenuItem.Checked = value == ReadOnMemoryMode.Always;
                }
            }
        }

        private ViewSettingHintMode viewSettingHintMode = ViewSettingHintMode.NeverShow;
        private ViewSettingHintMode ViewSettingHintMode
        {
            get => viewSettingHintMode;
            set
            {
                if (value != viewSettingHintMode)
                {
                    viewSettingHintMode = value;
                    showAHintWhenChangingSettingsToolStripMenuItem.Checked = value == ViewSettingHintMode.ShowWhenChangingSettings;
                }
            }
        }

        private int MaxPageCountInWindow = 2;
        
        private CoverBindingMode coverBindingMode = CoverBindingMode.Default;
        private CoverBindingMode CoverBindingMode
        {
            get
            {
                return coverBindingMode;
            }
            set
            {
                if (value != coverBindingMode)
                {
                    coverBindingMode = value;
                    forceFirstPageToBeSingleToolStripMenuItem.Checked = coverBindingMode == CoverBindingMode.ForceSingle;
                }
            }
        }
        
        ArchivesInArchiveMode archivesInArchiveMode = ZipPla.ArchivesInArchiveMode.Ignore;
        private ArchivesInArchiveMode ArchivesInArchiveMode
        {
            get
            {
                return archivesInArchiveMode;
            }
            set
            {
                if (value != archivesInArchiveMode)
                {
                    archivesInArchiveMode = value;
                    nonerecommendedToolStripMenuItem.Checked = value == ArchivesInArchiveMode.Ignore;
                    //onlyIfThereIsNoOtherImageToolStripMenuItem.Checked = value == ArchivesInArchiveMode.IfNoOther1Level;
                    untilFoundToolStripMenuItem.Checked = value == ArchivesInArchiveMode.UntilFound2Level;
                    alwaysLoadArchivesInArchiveToolStripMenuItem.Checked = value == ArchivesInArchiveMode.Always1Level;
                }
            }
        }

        //private MagnifierScrollMode MagnifierScrollMode = MagnifierScrollMode.Location;

        private int minPageCountInWindow = 1;
        private int MinPageCountInWindow
        {
            get { return minPageCountInWindow; }
            set
            {
                if (value != minPageCountInWindow)
                {
                    minPageCountInWindow = value;
                    forceTwoPageSpreadToolStripMenuItem.Checked = value > 1;
                }
            }
        }

        private int maxDivision = 1;
        private int MaxDivision
        {
            get { return maxDivision; }
            set
            {
                if (maxDivision != value)
                {
                    maxDivision = value;
                    if (value <= CurrentDividedPosition) CurrentDividedPosition = value - 1;
                    allowPageDivisionToolStripMenuItem.Checked = value > 1;
                }
            }
        }
        private int CurrentDividedPosition = 0;

        private GammaConversion resizeGammaNormal = GammaConversion.Value1_0;
        private GammaConversion ResizeGammaNormal
        {
            get { return resizeGammaNormal; }
            set
            {
                if (value != resizeGammaNormal)
                {
                    resizeGammaNormal = value;
                    linearizeColorSpaceforGamma22NormalToolStripMenuItem.Checked = value == GammaConversion.Value2_2;
                }
            }
        }

        private GammaConversion resizeGammaMagnifier = GammaConversion.Value1_0;
        private GammaConversion ResizeGammaMagnifier
        {
            get { return resizeGammaMagnifier; }
            set
            {
                if (value != resizeGammaMagnifier)
                {
                    resizeGammaMagnifier = value;
                    linearizeColorSpaceforGamma22MagnifierToolStripMenuItem.Checked = value == GammaConversion.Value2_2;
                }
            }
        }

        private struct MagnifyingInfo
        {
            private enum Mode { Value, Circumscribe, FitHorizontal, FitVertical }
            Mode mode;
            double value;

            public double GetPower(Size viewSize, Size imageSize)
            {
                switch(mode)
                {
                    case Mode.Circumscribe: return Math.Max((double)viewSize.Width / imageSize.Width, (double)viewSize.Height / imageSize.Height);
                    case Mode.FitHorizontal: return (double)viewSize.Width / imageSize.Width;
                    case Mode.FitVertical: return (double)viewSize.Height / imageSize.Height;

                    default: return value;
                }
            }

            public double? GetValue()
            {
                return mode == Mode.Value ? value as double? : null;
            }

            public static MagnifyingInfo FromValue(double value)
            {
                return new MagnifyingInfo() { mode = Mode.Value, value = value };
            }

            public static readonly MagnifyingInfo Circumscribe = new MagnifyingInfo() { mode = Mode.Circumscribe };
            public static readonly MagnifyingInfo FitHorizontal = new MagnifyingInfo() { mode = Mode.FitHorizontal };
            public static readonly MagnifyingInfo FitVertical = new MagnifyingInfo() { mode = Mode.FitVertical };

            public static bool operator ==(MagnifyingInfo a, MagnifyingInfo b)
            {
                var mode = a.mode;
                return mode == b.mode && (mode != Mode.Value || a.value == b.value);
            }
            public static bool operator !=(MagnifyingInfo a, MagnifyingInfo b)
            {
                var mode = a.mode;
                return mode != b.mode || (mode == Mode.Value && a.value != b.value);
            }

            public override bool Equals(object obj)
            {
                var info = obj as MagnifyingInfo?;
                if (info == null) return false;
                else return this == (MagnifyingInfo)obj;
            }

            public override int GetHashCode()
            {
                return mode == Mode.Value ? value.GetHashCode() : mode.GetHashCode(); 
            }
        }

        private MagnifyingInfo magnifyingPower = MagnifyingInfo.FromValue(2);
        private MagnifyingInfo MagnifyingPower
        {
            get { return magnifyingPower; }
            set
            {
                resetMagnifyingPower(value, checkChange: true, redraw: true);
            }
        }

#if DEBUG
        bool _DrawMeasure = true;
#else
        bool _DrawMeasure = false;
#endif
        bool DrawMeasure
        {
            get { return _DrawMeasure; }
            set
            {
                if (value != _DrawMeasure)
                {
                    _DrawMeasure = value;
                    pbPaintDrawOnlyMeasure();
                }
            }
        }


        private void resetMagnifyingPower(MagnifyingInfo value, bool checkChange = false, bool redraw = true)
        {
            if (!checkChange || value != magnifyingPower)
            {
                magnifyingPower = value;
                Magnifier_currentPageReady = -1;
                if (Magnifier_ZoomedPage != null)
                {
                    Magnifier_ZoomedPage.Dispose();
                    Magnifier_ZoomedPage = null;
                }
                if (redraw)
                {
                    if (ViewerMode == ViewerModeEnum.MagnifierOpening || ViewerMode == ViewerModeEnum.MagnifierClosing) ShwoMagnifierRectangle(invalidiate: false);
                    showCurrentPage();
                }
            }
        }

        ScalingAlgorithmPair StandardScalingAlgorithm;
        ScalingAlgorithmPair MagnifierScalingAlgorithm;

        private bool SlideshowForward = true;
        private bool SlideshowMove1Page = false;
        private bool SlideshowRepeat = true;
        private bool SlideshowGlobal = false;

        private readonly ViewerFormImageFilter imageFilter = new ViewerFormImageFilter();

        private int currentPage
        {
            get { return currentPage_field; }
            set
            {
                if (value != currentPage_field)
                {
                    currentPage_field = value;
                }
                onePageModeForNext = false;
                CurrentDividedPosition = 0;
            }
        }

        private SeekBarMode SeekBarMode
        {
            get
            {
                return orientSeekBarDirectionToPageSequenceToolStripMenuItem.Checked ? SeekBarMode.SameAsPage : SeekBarMode.LeftToRight;
            }
            set
            {
                var changed = value != SeekBarMode;
                if (changed)
                {
                    orientSeekBarDirectionToPageSequenceToolStripMenuItem.Checked = value == SeekBarMode.SameAsPage;
                    setSeekBarDirection();
                }
            }
        }


        private BindingMode bindingModeField = BindingMode.RightToLeft;
        private BindingMode BindingMode
        {
            get
            {
                return bindingModeField;
            }
            set
            {
                ChangeBindingModeCore(value, redraw: true);
            }
        }

        private void ChangeBindingModeCore(BindingMode bindingMode, bool redraw)
        {
            var changed = bindingMode != bindingModeField;
            leftToRightToolStripMenuItem.Checked = bindingMode == BindingMode.LeftToRight;
            rightToLeftToolStripMenuItem.Checked = bindingMode == BindingMode.RightToLeft;
            singlePageToolStripMenuItem.Checked = bindingMode == BindingMode.SinglePage;
            singlePageWithoutScalingUpToolStripMenuItem.Checked = bindingMode == BindingMode.SinglePageWithoutScalingUp;
            if (changed)
            {
                bindingModeField = bindingMode;
                //MaxDivision = bindingMode == BindingMode.LeftToRight || bindingMode == BindingMode.RightToLeft ? 2 : 1;
                if (bindingModeField == BindingMode.SinglePage || bindingModeField == BindingMode.SinglePageWithoutScalingUp)
                {
                    MaxPageCountInWindow = 1;
                }
                else
                {
                    MaxPageCountInWindow = 2;
                }
                //SetForwerdBackMessage(value);
                SetCurrentImageLongPath();
                Magnifier_ZoomedPage?.Dispose();
                Magnifier_ZoomedPage = null;
                Magnifier_currentPageReady = -1;
                if (redraw) showCurrentPage();
                setSeekBarDirection();

            }
        }


        private static bool NoScaling(BindingMode bindingMode)
        {
            return bindingMode != BindingMode.SinglePageWithoutScalingUp;
        }

        public static CommandLineOptionInfo CommandLineOptionAnalyzer(string[] args, int start)
        {
            var result = new CommandLineOptionInfo();

            var position = start;
            var lastRead = start - 1;
            for (; position < args.Length; position++)
            {
                var name = args[position];
                if (string.IsNullOrEmpty(name)) continue;
                string value;
                var eq = name.IndexOf(commandLineEqual);
                if (eq <= 0)
                {
                    break;
                }
                var position0 = position;
                if (eq > 0)
                {
                    if (eq < name.Length - commandLineEqual.Length)
                    {
                        value = name.Substring(eq + commandLineEqual.Length);
                        name = name.Substring(0, eq);
                    }
                    else if (++position < args.Length)
                    {
                        name = name.Substring(0, eq);
                        value = args[position];
                    }
                    else
                    {
                        position = position0;
                        break;
                    }
                }
                else if (++position < args.Length)
                {
                    value = args[position];
                    if (string.IsNullOrEmpty(value)) continue;
                    if (value == commandLineEqual)
                    {
                        if (++position < args.Length)
                        {
                            value = args[position];
                        }
                        else
                        {
                            position = position0;
                            break;
                        }
                    }
                    else if (value.Length > commandLineEqual.Length && value.StartsWith(commandLineEqual))
                    {
                        value = value.Substring(commandLineEqual.Length);
                    }
                    else
                    {
                        position = position0;
                        break;
                    }
                }
                else
                {
                    position = position0;
                    break;
                }
                if (string.IsNullOrEmpty(value) || value.Contains(commandLineEqual))
                {
                    position = position0;
                    break;
                }

                bool read;
                if (string.Compare(name, "c", ignoreCase: true) == 0)
                {
                    CoverBindingMode? r;
                    if (read = TryParse(value, ignoreCase: true, result: out r)) result.CoverBindingMode = r;
                }
                else if (string.Compare(name, "i", ignoreCase: true) == 0)
                {
                    bool? r;
                    if (read = TryParse(value, ignoreCase: true, result: out r) && r != null) result.OpenInPreviousImageFilterSetting = (bool)r;
                }
                else if (string.Compare(name, "f", ignoreCase: true) == 0)
                {
                    InitialFullscreenMode r;
                    if (read = TryParse(value, ignoreCase: true, result: out r)) result.InitialFullscreenMode = r;
                }
                else if (string.Compare(name, "b", ignoreCase: true) == 0)
                {
                    Color r;
                    if (read = ColorTryParse(value, result: out r)) result.BackColor = r;
                }
                else if (string.Compare(name, "a", ignoreCase: true) == 0)
                {
                    ArchivesInArchiveMode? r;
                    if (read = TryParse(value, ignoreCase: true, result: out r)) result.ArchivesInArchiveMode = r;
                }
                else if (string.Compare(name, "h", ignoreCase: true) == 0)
                {
                    bool? r;
                    if (read = TryParse(value, ignoreCase: true, result: out r)) result.AlwaysHideUI = r;
                }
                else if (string.Compare(name, "d", ignoreCase: true) == 0)
                {
                    BindingMode? r;
                    if (read = TryParse(value, ignoreCase: true, result: out r)) result.DefaultBindingMode = r;
                }
                else if (string.Compare(name, "w", ignoreCase: true) == 0)
                {
                    if (read = TryParse(value, ignoreCase: true, result: out ReadOnMemoryMode? r)) result.ReadOnMemoryMode = r;
                }
                else read = false;
                if (read) lastRead = position;
            }

            // Path の解釈
            var p = lastRead + 1;
            if (p < args.Length)
            {
                result.Path = args[p];
            }

            return result;
        }

        IpcLookAheadInfo ipcLookAheadInfo = null;
        IpcReportFromViewerToCatalog ipcReportFromViewerToCatalog = null;
        public ViewerForm(IpcLookAheadInfo ipcLookAheadInfo, IpcReportFromViewerToCatalog ipcReportFromViewerToCatalog, InitialFullscreenMode initialFullscreenMode, bool? alwaysHideUI)
        {
            //Program.SetPriority(ProcessPriorityClass.BelowNormal);
            this.ipcLookAheadInfo = ipcLookAheadInfo;
            this.ipcReportFromViewerToCatalog = ipcReportFromViewerToCatalog;

            var info = new CommandLineOptionInfo();
            info.InitialFullscreenMode = initialFullscreenMode;
            info.AlwaysHideUI = alwaysHideUI;
            info.BackColor = Color.Black;
            info.ArchivesInArchiveMode = ArchivesInArchiveMode.Ignore;
            info.ReadOnMemoryMode = ReadOnMemoryMode.None;
            CommandLineOptionInfoForLookAhead = info;
            Constructor(info);
            //Constructor(initialFullscreenMode, alwaysHideUI, backColor: Color.Black, aia: ArchivesInArchiveMode.Ignore);
            // 構築に使った値を「依頼された値」の初期値にする。null のままだと最初の依頼で必ず設定と文言を適用し直していた（S5、5〜30ms）
            requestedInitialFullscreenMode = initialFullscreenMode;
            requestedAlwaysHideUI = alwaysHideUI;

            StartWatchIpcWakeEvent();
            tmWatchIpc.Start();
            ScheduleWaitingMemoryTrim();

            //tmMemoryReducer.Enabled = true; // Start() と同じ
        }
        private CommandLineOptionInfo CommandLineOptionInfoForLookAhead = null;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        /// <summary>
        /// 待機ビューア（-LookAheadMode）は何も表示していないのに、起動の一時的なメモリ（WS 約 100MB）を抱えたまま待っていた（S4）。
        /// 起動して 1.5 秒たっても待機中なら、GC（LOH の圧縮つき）をしてワーキングセットを OS に返す
        /// </summary>
        private void ScheduleWaitingMemoryTrim()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 1500 };
            timer.Tick += (sender, e) =>
            {
                timer.Stop();
                timer.Dispose();
                if (IsDisposed || !tmWatchIpc_Waiting) return; // もう表示を始めている
                if (waitingRequestArrived) return; // 先読みの依頼を受けて読んでいる（Y20。返すと先読みしたページを表示するときにページフォールトが起きる）
                try
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    {
                        SetProcessWorkingSetSize(process.Handle, (IntPtr)(-1), (IntPtr)(-1));
                    }
                }
                catch { }
            };
            timer.Start();
        }

        //private ContextMenuStripForMenu startContextMenuStripForMenu;

        MouseGesture mgView;
        KeyboardShortcut ksViewForm;

        Font MenuTextFont = null;

        private GestureListener gestureListener, gestureListenerEditMenu;

        private void Constructor(/*InitialFullscreenMode initialFullscreenMode = InitialFullscreenMode.Default,*/ CommandLineOptionInfo info)//, bool? alwaysHideUI = null, Color? backColor = null, ArchivesInArchiveMode? aia = null)
        {
            Configuration.PrepareXmlSerializer(
                typeof(ViewerFormConfig),
                //typeof(ColoredBookmarkConfig),
                typeof(GeneralConfig)
                //typeof(ZipTagConfig)
                );

            if (ipcLookAheadInfo == null && Program.CheckConfigException()) CloseWithoutSave();

            InitializeComponent();
            InitializeScalingAlgorithmMenuItems();
            // 先読みは表示中のページの前後だけ（SetBackgroundMode）
            bmwLoadEachPage.AllowPartialWorksOrder = true;
            ResizeBegin += ViewerForm_ResizeBegin_V4;
            ResizeEnd += ViewerForm_ResizeEnd_V4;
            // 完了イベント（縮小画像の登録・表示）を待たずに次のページの読み込みを始める（V1）
            bmwLoadEachPage.StartNextWorkBeforeCompletedEvent = true;
#if PERFLOG
            // 計測用: 環境変数 ZIPPLA_V1_OFF=1 で従来の順番（完了イベントのあとに次のジョブ）にする
            if (Environment.GetEnvironmentVariable("ZIPPLA_V1_OFF") == "1") bmwLoadEachPage.StartNextWorkBeforeCompletedEvent = false;
#endif

            Configuration.WaitPrepareTask();

            closeWindowtoolStripMenuItem.Text = " "; // 特別な状況下で表示が乱れるのを防ぐ


            try
            {
                gestureListener = new GestureListener(this, new GestureConfig[] {
                    new GestureConfig(3, 1, 0), // ズーム
                    //new GestureConfig(4, 2 | 4 , 8 | 16), // パン、向き拘束と慣性なし
                    new GestureConfig(4, 2 | 4 | 16 , 8 ), // パン、向き拘束なし
                    
                    new GestureConfig(5, 1, 0), // 回転
                    new GestureConfig(6, 1, 0), // ツーフィンガータップ
                    //new GestureConfig(7, 1, 0), // プレスアンドタップ

                });
                gestureListener.Zoom += gestureListener_Zoom;
                gestureListener.Pan += TouchListener_Pan;
                gestureListener.Rotate += gestureListener_Rotate;
                gestureListener.TwoFingerTap += gestureListener_TwoFingerTap;
                //gestureListener.PressAndTap += gestureListener_PressAndTap;

                gestureListenerEditMenu = MiniControlTouchGesture.GetGestureListener(editToolStripMenuItem.DropDown);
                var menu = new MiniControlTouchGesture(editToolStripMenuItem.DropDown, gestureListenerEditMenu, onlyHorizontalStart: true);
                menu.Targets.Add(editToolStripMenuItem.DropDown);
                menu.TouchGestureCompleted += CatalogForm.TouchGestureCompletedForTagMenu;
                menu.TouchGestureStarting += CatalogForm.TouchGestureStartingForTagMenu;
            }
            catch { }

            // キー入力の暴発を防ぐ
            ActivateManager.SetSelectable(btnOpenLeft, false);
            ActivateManager.SetSelectable(btnOpenRight, false);

            new KeyMouseBinder(editToolStripMenuItem.DropDown, requiredBindingEditMenu);

            if (CatalogForm.MenuTextFontShouldBeReplaced())
            {
                MenuTextFont = new Font(menuStrip.Font.FontFamily, DefaultFont.SizeInPoints);
            }

            if (MenuTextFont != null)
            {
                menuStrip.Font = MenuTextFont;
                statusStrip.Font = MenuTextFont;
            }

            ZipPlaAddressBar.AddDrawToolStripMenuItemTriangleEventHandler(menuStrip);

            //Program.SetDoubleBuffered(pbView);

            btnOpenLeft.FlatStyle = FlatStyle.Flat;
            btnOpenLeft.FlatAppearance.BorderColor = BackColor;
            btnOpenRight.FlatStyle = FlatStyle.Flat;
            btnOpenRight.FlatAppearance.BorderColor = BackColor;

            openNewThumbnailWindowToolStripMenuItem.Image = MoveLocationProvider.Icon;
            switchToThumbnailWindowToolStripMenuItem.Image = MoveLocationProvider.Icon;
            cloneTheWindowToolStripMenuItem.Image = BuiltInViewerProvider.Icon;

            // 拡大鏡使用中のキー入力がカスタマイズされたショートカットキーに先行するように
            PreviewKeyDown += Control_PreviewKeyDown;
            foreach (Control control in Controls) control.PreviewKeyDown += Control_PreviewKeyDown;
            //mtbPage.PreviewKeyDown += Control_PreviewKeyDown; // pbView 外でカーソルが動いても意味がない

            ksViewForm = new KeyboardShortcut(this, mtbPage);
            ksViewForm.UseLButton = true;
            ksViewForm.UseMButton = true;
            ksViewForm.UseRButton = true;
            ksViewForm.UseX1Button = true;
            ksViewForm.UseX2Button = true;
            //ksViewForm.MouseAcceptControls = ksViewForm.LButtonAcceptControls = ksViewForm.WheelAcceptControls = new Control[] { pbView };
            //ksViewForm.RButtonAcceptControls = new Control[] { }; // mgView.Enabled の初期値に合わせて設定
            ksViewForm.MouseAcceptControls = ksViewForm.LButtonAcceptControls = ksViewForm.WheelAcceptControls = ksViewForm.RButtonAcceptControls = new Control[] { pbView };
            ksViewForm.WheelAccepted += ksViewForm_WheelAccepted;
            ksViewForm.KeyboardShortcutStarting += ksViewForm_KeyboardShortcutStarting;

            mgView = new MouseGesture(pbView);
            //mgView.EnabledChanged += mgView_EnabledChanged;
            mgView.MouseGestureCompleted += mgView_MouseGestureCompleted;
            mgView.MouseGestureStarting += mgView_MouseGestureStarting;

            ToolStripOverwriter.SquarizeToolStripInClass(this);

            Shown += (sender, e) =>
            {
                CheckMarkProvider.SetCheckMark(loadWholeArchiveIntoMemoryToolStripMenuItem.DropDown, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(loadArchivesInArchiveToolStripMenuItem.DropDown, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(startToolStripMenuItem.DropDown, fullScreenInStartToolStripMenuItem, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(startToolStripMenuItem.DropDown, windowToolStripMenuItem, DisplayCheckMark.Select);
                
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, magnifierToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, leftToRightToolStripMenuItem, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, rightToLeftToolStripMenuItem, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, singlePageToolStripMenuItem, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, singlePageWithoutScalingUpToolStripMenuItem, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, forceFirstPageToBeSingleToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, forceTwoPageSpreadToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, allowPageDivisionToolStripMenuItem, DisplayCheckMark.Check);
                
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, orientSeekBarDirectionToPageSequenceToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, rotateLeftToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, rotateRightToolStripMenuItem, DisplayCheckMark.Check);
                //CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, removeMoireToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, pseudoColoringToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, alwaysAutoHideUIToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(viewToolStripMenuItem.DropDown, showAHintWhenChangingSettingsToolStripMenuItem, DisplayCheckMark.Check);

                CheckMarkProvider.SetCheckMark(scalingAlgorithmNormalToolStripMenuItem.DropDown, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(scalingAlgorithmNormalToolStripMenuItem.DropDown, linearizeColorSpaceforGamma22NormalToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(scalingAlgorithmNormalToolStripMenuItem.DropDown, useAreaAverageWhenUpsizingNormalToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(scalingAlgorithmMagnifierToolStripMenuItem.DropDown, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(scalingAlgorithmMagnifierToolStripMenuItem.DropDown, linearizeColorSpaceforGamma22MagnifierToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(scalingAlgorithmMagnifierToolStripMenuItem.DropDown, useAreaAverageWhenUpsizingToolStripMenuItem, DisplayCheckMark.Check);

                CheckMarkProvider.SetCheckMark(ratioToolStripMenuItem.DropDown, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(slideshowToolStripMenuItem.DropDown, DisplayCheckMark.Select);
                CheckMarkProvider.SetCheckMark(slideshowToolStripMenuItem.DropDown, repeatToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(slideshowToolStripMenuItem.DropDown, openNextOnTerminalToolStripMenuItem, DisplayCheckMark.Check);
                CheckMarkProvider.SetCheckMark(editToolStripMenuItem.DropDown, DisplayCheckMark.Select);
            };

            //ExtendedToolStripMenuItem.Replace(ref startToolStripMenuItem);

            ToolStripDropDownScroller.Enscrollable(virtualFoldersToolStripMenuItem.DropDown, ToolStripDropDownScroller.GetGestureListener(virtualFoldersToolStripMenuItem.DropDown));
            ToolStripDropDownScroller.Enscrollable(editToolStripMenuItem.DropDown, gestureListenerEditMenu);

            // DPI 変更時用の処理
            //mtbPage.Location = new Point(0, statusStrip.Location.Y - mtbPage.Height);
            pnlSeekbar.Location = new Point(0, statusStrip.Location.Y - pnlSeekbar.Height);

            Text = Program.Name;
            //currentImageLongPathToolStripStatusLabel.Text = "";
            pageToolStripStatusLabel.Text = "";
            imageSizeToolStripStatusLabel.Text = "";
            showCurrentPageToStatusBar_Shown = false;

            LoadSettingsAndSetMessages(info);

            pbView.MouseWheel += pbView_MouseWheel;
            statusStrip.MouseWheel += aroundSeekBar_MouseWheel;
            mtbPage.MouseWheel += aroundSeekBar_MouseWheel;
            mtbPage.MouseUp += (sender, e) => EndSeekBarDrag(); // V24
            mtbPage.MouseCaptureChanged += (sender, e) => EndSeekBarDrag(); // ボタンを離さずにフォーカスが移った場合など（V24）

            //PrepareMemoryCheck();

        }

        private static bool requiredBindingEditMenu(ToolStripItem toolStripItem)
        {
            if (toolStripItem is PrefixEscapedToolStripMenuItem) return true;
            var items = toolStripItem.Owner.Items;
            var nextIndex = items.IndexOf(toolStripItem) + 1;
            return nextIndex < items.Count && items[nextIndex] is PrefixEscapedToolStripMenuItem;
        }

        private void LoadSettingsAndSetMessages(CommandLineOptionInfo info, bool reroadConfig = true, bool reloadLang = false)
        {
            // ショートカットキーの設定に合わせてメニューの文字列を変更するのでこの順で無ければならない
            LoadSettings(/*initialFullscreenMode, */info, reroadConfig, reloadLang);//?.AlwaysHideUI, info?.BackColor, info?.ArchivesInArchiveMode);
            SetMessages();
        }

        private void CloseAllMenu()
        {
            foreach (ToolStripMenuItem tsmi in menuStrip.Items)
            {
                if (tsmi.HasDropDown)
                {
                    var dd = tsmi.DropDown;
                    if (dd.Visible) dd.Close();
                }
            }
            if (openToolStripMenuItemAsContextMenuStrip_Frame != null)
            {
                if (openToolStripMenuItemAsContextMenuStrip_Frame.Visible)
                {
                    openToolStripMenuItemAsContextMenuStrip_Frame.Close();
                }
            }
        }

        double? gestureListener_Rotate_Degrees = null;
        private void gestureListener_Rotate(object sender, RotateEventArgs e)
        {
            CloseAllMenu();
            if (ViewerMode == ViewerModeEnum.Normal || ViewerMode == ViewerModeEnum.Magnifier)
            //if (ViewerMode == ViewerModeEnum.Normal || ViewerMode == ViewerModeEnum.Magnifier || ViewerMode == ViewerModeEnum.MagnifierOpening)
            {
                double deg;
                if (e.Begin)
                {
                    gestureListener_Rotate_Degrees = 0;
                    deg = 0;
                }
                else if (e.End)
                {
                    gestureListener_Rotate_Degrees = null;
                    return;
                }
                else
                {
                    if (gestureListener_Rotate_Degrees == null)
                    {
                        gestureListener_Rotate_Degrees = 0;
                        deg = e.Degrees;
                    }
                    else
                    {
                        deg = (double)gestureListener_Rotate_Degrees - e.Degrees;// フィルタと符号が逆
                        gestureListener_Rotate_Degrees = deg;
                    }
                }

                const double roteDegree = 45;
                var currentDegrees = imageFilter.RotationAngle;
                if (currentDegrees == 270 && deg < 0 || currentDegrees == 90 && deg > 0)
                {
                    gestureListener_Rotate_Degrees = 0;
                }
                else if (deg <= -roteDegree)
                {
                    imageFilter.RotationAngle = currentDegrees == 0 ? 270 : 0;
                    reloadForChangeFilter(forOnlyPostFilter: false);
                    gestureListener_Rotate_Degrees = deg + 90;
                }
                else if (gestureListener_Rotate_Degrees >= roteDegree)
                {
                    imageFilter.RotationAngle = currentDegrees == 0 ? 90 : 0;
                    reloadForChangeFilter(forOnlyPostFilter: false);
                    gestureListener_Rotate_Degrees = deg - 90;
                }
            }
            else
            {
                gestureListener_Rotate_Degrees = null;
            }
        }

        
        private void gestureListener_TwoFingerTap(object sender, TwoFingerTapEventArgs e)
        {
            CloseAllMenu();
            if (ViewerMode == ViewerModeEnum.Magnifier)
            {
                MagnifierPhase3(toPhase4: true);
                MagnifierPhase4(toPhase1: false);
            }
            else if (ViewerMode == ViewerModeEnum.Normal)
            {
                MagnifierPhase1(pbView.PointToClient(e.Location));
                MagnifierPhase2();
            }
            else if (ViewerMode == ViewerModeEnum.MagnifierOpening)
            {
                var value = magnifyingPower.GetValue();
                if (value == null || value > 1)
                {
                    MagnifierPhase2();
                }
                else
                {
                    MagnifierPhase2();
                    MagnifierPhase3(toPhase4: true);
                    MagnifierPhase4(toPhase1: false);
                }
            }
        }
        
        private void gestureListener_Zoom(object sender, ZoomEventArgs e)
        {
            CloseAllMenu();
            TouchListener_Pan_Delta = null;
            if (ViewerMode == ViewerModeEnum.CoverSetting) return;

            if (e.Begin)
            {
                if (gestureListener_Rotate_Degrees == null && (ViewerMode == ViewerModeEnum.Normal || ViewerMode == ViewerModeEnum.Magnifier))
                {
                    if (ViewerMode == ViewerModeEnum.Magnifier)
                    {
                        MagnifierPhase3(toPhase4: true);
                        MagnifierPhase4(toPhase1: false);
                    }
                    else
                    {
                        //resetMagnifyingPower(1, checkChange: true, redraw: false);
                    }

                    // 係数の設定
                    // 0.7 を基本とし、
                    // GetCenterOfMagnifierRectangle_DistanceCoef * longer / Math.Max(1, e.Distance) が 4 を上回るならそうならないように更に小さくする
                    var pbViewSize = pbView.Size;
                    var longer = Math.Max(1, Math.Max(pbViewSize.Width, pbViewSize.Height));
                    GetCenterOfMagnifierRectangle_DistanceCoef = Math.Min(0.7, 4.0 / longer * e.Distance);

                    MagnifyingInfo newMagPower;
                    MagnifierPhase1(GetCenterOfMagnifierRectangle(e, out newMagPower));

                    if (ViewerMode == ViewerModeEnum.MagnifierOpening)
                    {
                        resetMagnifyingPower(newMagPower, checkChange: true, redraw: true);
                    }

                }
            }
            else if (!e.End)
            {
                if (ViewerMode == ViewerModeEnum.MagnifierOpening)
                {
                    MagnifyingInfo newMagPower;
                    GetMagnifierRectangle_LastCursorPosition = GetCenterOfMagnifierRectangle(e, out newMagPower);
                    resetMagnifyingPower(newMagPower, checkChange: true, redraw: true);
                }
            }
            else
            {
                var value = magnifyingPower.GetValue();
                if (value == null || value > 1)
                {
                    MagnifierPhase2();
                }
                else
                {
                    MagnifierPhase2();
                    MagnifierPhase3(toPhase4: true);
                    MagnifierPhase4(toPhase1: false);
                }
            }
        }

        private double GetCenterOfMagnifierRectangle_DistanceCoef;
        private Point GetCenterOfMagnifierRectangle(ZoomEventArgs e, out MagnifyingInfo newMagnifyingPower)
        {
            var center = pbView.PointToClient(e.Location);
            var distance = e.Distance;
            var pbViewSize = getCurrentPbViewSize();
            var dicreasedLonger = GetCenterOfMagnifierRectangle_DistanceCoef * Math.Max(pbViewSize.Width, pbViewSize.Height);

            var imageSize_ = getCurrentVisibleImageSize(pbViewSize);

            var value = dicreasedLonger / Math.Max(1, distance);

            if (imageSize_ == null)
            {
                value = Math.Min(4, Math.Max(1, value));
                newMagnifyingPower = MagnifyingInfo.FromValue(value);

            }
            else
            {
                var imageSize = (Size)imageSize_;
                var fits = new List<double>();
                var mh = MagnifyingInfo.FitHorizontal.GetPower(pbViewSize, imageSize);
                if (mh < 4 && mh > 1) fits.Add(mh);
                var mv = MagnifyingInfo.FitVertical.GetPower(pbViewSize, imageSize);
                if (mv < 4 && mv > 1) fits.Add(mv);
                fits.Sort();
                var margin = 0.1;
                for (var i = fits.Count - 1; i >= 0; i--)
                {
                    var m = fits[i];
                    var mInv = 1 / m;
                    var vInv = 1 / value;
                    if (vInv <= mInv) break;
                    else if (vInv - margin <= mInv)
                    {
                        value = m;
                        break;
                    }
                    else
                    {
                        value = 1 / (vInv - margin);
                    }
                }

                // GetValue が 1 以下になる場合はその値で FromValue し直すことで拡大を行わないことを知らせる
                // 等倍の場合、他のページも等倍の可能性が高いのでフィットの必要性は低く、これを行う必要は認められない。
                if (value <= 1)
                {
                    newMagnifyingPower = MagnifyingInfo.FromValue(1);
                }
                else if (value == mh)
                {
                    newMagnifyingPower = MagnifyingInfo.FitHorizontal;
                }
                else if (value == mv)
                {
                    newMagnifyingPower = MagnifyingInfo.FitVertical;
                }
                else if (value < 4)
                {
                    newMagnifyingPower = MagnifyingInfo.FromValue(value);
                }
                else
                {
                    newMagnifyingPower = MagnifyingInfo.FromValue(4);
                }

            }

            // サイズの設定
            // 現在表示中の画像サイズが取得できなければ常に FromValue で。

            return center;
        }

        private int NextPage
        {
            get
            {
                if (ResizedImageArray == null)
                {
                    return 0;
                }

                var totalWidth = 0;
                var stopPage = currentPage;
                var MaxPageCountInWindowForNext = onePageModeForNext ? 1 : MaxPageCountInWindow;
                var screenWidth = null as int?;
                for (var i = currentPage; i < currentPage + MaxPageCountInWindowForNext && i < ResizedImageArray.Length; i++)
                {
                    var img = ResizedImageArray[i];
                    if (img == null)
                    {
                        var imgSize = ResizedSizeArray[i];
                        if (imgSize.IsEmpty)
                        {
                            return currentPage;
                        }
                        totalWidth += imgSize.Width;
                    }
                    else
                    {
                        totalWidth += img.Width;
                    }
                    stopPage++;
                    var pbViewWidth = FullScreen_pseudoFullScreen ? screenWidth ?? (int)(screenWidth = Screen.FromControl(this).Bounds.Width) :
                        PseudoMaximized ? PseudoMaximizedSize.Width : ViewerForm_SizeChanged_pbBoundsMemoryForMinimize.Width; // pbView.Width;
                    if (totalWidth >= pbViewWidth)
                    {
                        if (totalWidth != pbViewWidth)
                        {
                            stopPage--;
                            break; // img == null でも stopPage を返す唯一のパターン
                        }
                        if (img == null) return currentPage;
                        break;
                    }
                    if (img == null) return currentPage;
                }
                return stopPage;
            }
        }

        private int PreviousPage
        {
            get
            {
                if (ResizedImageArray == null)
                {
                    return -1;
                }
                var totalWidth = 0;
                var stopPage = currentPage;
                var screenWidth = null as int?;
                for (var i = currentPage - 1; i >= currentPage - MaxPageCountInWindow && i >= 0; i--)
                {
                    var img = ResizedImageArray[i];
                    if (img == null)
                    {
                        var imgSize = ResizedSizeArray[i];
                        if (imgSize.IsEmpty)
                        {
                            if (MaxPageCountInWindow > 1)
                            {
                                return currentPage;
                            }
                            else
                            {
                                stopPage--;
                                break;
                            }
                        }
                        totalWidth += imgSize.Width;
                    }
                    else
                    {
                        totalWidth += img.Width;
                    }
                    stopPage--;
                    var pbViewWidth = FullScreen_pseudoFullScreen ? screenWidth ?? (int)(screenWidth = Screen.FromControl(this).Bounds.Width) :
                        PseudoMaximized ? PseudoMaximizedSize.Width : ViewerForm_SizeChanged_pbBoundsMemoryForMinimize.Width;
                    if (totalWidth >= pbViewWidth)
                    {
                        if (totalWidth != pbViewWidth)
                        {
                            stopPage++;
                            break; // img == null でも stopPage を返す唯一のパターン
                        }
                        if (img == null) return currentPage;
                        break;
                    }
                    if (img == null) return currentPage;
                }
                return stopPage;
            }
        }

        private bool FullScreen_pseudoFullScreen = false;
        private bool FullScreen
        {
            get
            {
                return FormBorderStyle == FormBorderStyle.None && WindowState == FormWindowState.Maximized;
            }
            set
            {
                // 中途半端な状態の修復効果を得るため、フルスクリーン化は現在の状態にかからわず実行する
                if (value)
                {
                    var tempPrevSize = prevSize;

                    if (LoadSettings_FormWindowStateFromSetteingFile == null)
                    {
                        prevFormWindowState = WindowState;
                    }
                    else
                    {
                        prevFormWindowState = (FormWindowState)LoadSettings_FormWindowStateFromSetteingFile;
                        LoadSettings_FormWindowStateFromSetteingFile = null;
                    }
                    prevFormBorderStyle = FormBorderStyle;
                    var unvisibled = false;
                    var temp = pbView_SizeChanged_SizeChanging;
                    pbView_SizeChanged_SizeChanging = true;
                    var needToCareMoving = false;
                    if (WindowState == FormWindowState.Maximized && ipcLookAheadInfo == null)
                    {
                        if (Visible)
                        {
                            Visible = false;
                            unvisibled = true;
                        }
                        //var temp2 = betterFormRestoreBound.StopMemoryNormalStateBound;
                        //betterFormRestoreBound.StopMemoryNormalStateBound = true;
                        WindowState = FormWindowState.Normal; // タスクバーが残る問題の回避
                        //betterFormRestoreBound.StopMemoryNormalStateBound = temp2;
                        needToCareMoving = true;
                    }

                    if (ipcLookAheadInfo != null)
                    {
                        pbView_SizeChanged_SizeChanging = temp;

                        FullScreen_pseudoFullScreen = true;
                    }
                    else
                    {
                        var currentDisplay = needToCareMoving ? Screen.FromControl(this).WorkingArea : Rectangle.Empty;
                        FormBorderStyle = FormBorderStyle.None;
                        if (needToCareMoving)
                        {
                            var memoriedDisplay = Screen.FromControl(this).WorkingArea;
                            if (currentDisplay != memoriedDisplay)
                            {
                                Bounds = BetterFormRestoreBounds.GetMovedWindowBounds(currentDisplay, Bounds, memoriedDisplay);
                            }
                        }
                        pbView_SizeChanged_SizeChanging = temp;
                        WindowState = FormWindowState.Maximized;

                        // ViewerForm_SizeChanged による書き換えを元に戻す
                        if (prevFormWindowState == FormWindowState.Maximized && prevSize.IsEmpty) prevSize = tempPrevSize;

                        if (unvisibled) Visible = true;
                        if (TopMostInFullscreen) TopMost = true;
                    }
                }

                // フルスクリーンでない場合、全画面かどうかが無意味に変更されないように条件をつける
                else if (FormBorderStyle == FormBorderStyle.None)
                {
                    //if (!prevClientSize.IsEmpty) ClientSize = prevClientSize;
                    if (prevFormWindowState == FormWindowState.Maximized && !prevSize.IsEmpty)
                    {
                        //var temp2 = betterFormRestoreBound.StopMemoryNormalStateBound;
                        //betterFormRestoreBound.StopMemoryNormalStateBound = true;
                        Size = prevSize;
                        //betterFormRestoreBound.StopMemoryNormalStateBound = temp2;
                    }
                    if (prevFormWindowState == FormWindowState.Normal && ipcLookAheadInfo == null) prevSize = Size.Empty;

                    var temp = pbView_SizeChanged_SizeChanging;
                    pbView_SizeChanged_SizeChanging = true;

                    if (FormBorderStyle == FormBorderStyle.None)
                    {
                        //var temp2 = betterFormRestoreBound.StopMemoryNormalStateBound;
                        //betterFormRestoreBound.StopMemoryNormalStateBound = true;
                        FormBorderStyle = prevFormBorderStyle;
                        //betterFormRestoreBound.StopMemoryNormalStateBound = temp2;
                    }

                    pbView_SizeChanged_SizeChanging = temp;
                    WindowState = prevFormWindowState;

                    TopMost = false;
                }
            }
        }
        private FormWindowState prevFormWindowState;
        private FormBorderStyle prevFormBorderStyle;
        private Size prevSize = Size.Empty;

        //Size? normalStateSize = null;

        //Point? ViewerForm_SizeChanged_Point = null;

        private Size ViewerForm_SizeChanged_CurrentSize = Size.Empty;
        private FormWindowState ViewerForm_SizeChanged_CurrentWindowState;
        private Rectangle ViewerForm_SizeChanged_pbBoundsMemoryForMinimize = Rectangle.Empty;
        private FormWindowState ViewerForm_SizeChanged_BeforeMinimizedState = FormWindowState.Normal;
        private void ViewerForm_SizeChanged(object sender, EventArgs e)
        {
            var ws = WindowState;
            var pre = ViewerForm_SizeChanged_CurrentWindowState;


            //if (ws != FormWindowState.Minimized) ViewerForm_SizeChanged_pbSizeMemoryForMinimize = pbView.Size;
            
            if (pre == FormWindowState.Maximized && ws == FormWindowState.Normal && normalStateLocation != null)
            {
                Location = (Point)normalStateLocation;
            }
            else if (ws == FormWindowState.Normal)
            {
                normalStateLocation = Location;
            }

            if (ws == FormWindowState.Minimized && pre != FormWindowState.Minimized)
            {
                ViewerForm_SizeChanged_BeforeMinimizedState = pre;
            }

            if (ws != ViewerForm_SizeChanged_CurrentWindowState)
            {
                ViewerForm_SizeChanged_CurrentWindowState = ws;
                if (ws != FormWindowState.Normal)
                {
                    if (prevSize.IsEmpty && !ViewerForm_SizeChanged_CurrentSize.IsEmpty)
                    {
                        prevSize = ViewerForm_SizeChanged_CurrentSize;
                    }
                    ViewerForm_SizeChanged_CurrentSize = Size.Empty;
                }
            }
            if (ws == FormWindowState.Normal && ipcLookAheadInfo == null)
            {
                //ViewerForm_SizeChanged_CurrentSize = ClientSize;
                if (!pbView_SizeChanged_SizeChanging)
                {
                    ViewerForm_SizeChanged_CurrentSize = Size;
                }
                prevSize = Size.Empty;

            }
        }

        const int DragEventKeyState_LeftButton = 1;
        private void ViewerForm_DragEnter(object sender, DragEventArgs e)
        {
            if (ExitAfterCoverSetting)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            try
            {
                if (e.KeyState == DragEventKeyState_LeftButton && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] Files = (string[])e.Data.GetData(DataFormats.FileDrop, false);
                    if (Files.Length == 1)
                    {
                        var path = ShortcutResolver.Exec(Files[0]);
                        if (Directory.Exists(path))
                        {
                            e.Effect = DragDropEffects.Copy;
                        }
                        else if (File.Exists(path))
                        {
                            if (PackedImageLoader.Supports(path) || ImageLoader.IndividualSupportFullReading(path))
                            {
                                e.Effect = DragDropEffects.Copy;
                            }
                            else
                            {
                                var ffmpegExists = null as bool?;
                                ImageLoader.SusieReset();
                                if (ImageLoader.SupportsFullReading(path, ref ffmpegExists))
                                {
                                    e.Effect = DragDropEffects.Copy;
                                }
                                else
                                {
                                    e.Effect = DragDropEffects.None;
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
            }
            catch (Exception error)
            {
                Program.AlertError(error);
            }
        }

        private void ViewerForm_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                var path = ShortcutResolver.Exec(((string[])e.Data.GetData(DataFormats.FileDrop, false))[0]);
                removeHiddenConfigFromCatalogForm();
                OpenFile(path);
            }
            catch (Exception error)
            {
                Program.AlertError(error);
            }
        }

        private PackedImageLoader loader = null;
        private Size ViewSize;
        private string currentArchiveFilePath = null;
        private string currentStartingFilePath = null;
        private SortModeDetails currentSortModeDetails = null;
        class NextData : IDisposable
        {
            public readonly string[] EntriesPaths;
            public readonly PackedImageLoader.PackedImageEntry[] Entries;
            public readonly PackedImageLoader Loader;
            public NextData(string[] entriesPaths, PackedImageLoader.PackedImageEntry[] entries, PackedImageLoader loader)
            {
                EntriesPaths = entriesPaths;
                Entries = entries;
                Loader = loader;
            }

            bool disposed = false;
            public void Dispose()
            {
                if (!disposed)
                {
                    disposed = true;
                    Loader.Dispose();
                }
            }
        }
        NextData nextData; 
        private bool DoNotSaveSetting = false;
        private OpenResult OpenFile(string path) { return OpenFile(path, -1); }
        private bool OpenFile_UserOpen = false;
        //private bool OpenFileFinishedButStartingIsNotCalled = false;
        private static readonly SortMode[] OpenFile_SuportedSortModes = new SortMode[] {
            SortMode.TypeInAsc, SortMode.TypeInDesc, SortMode.NameInAsc, SortMode.NameInDesc,
            SortMode.RatingInAsc, SortMode.RatingInDesc,
            SortMode.CreatedInAsc, SortMode.CreatedInDesc, SortMode.AccessedInAsc, SortMode.AccessedInDesc,
            SortMode.ModifiedInAsc, SortMode.ModifiedInDesc, SortMode.SizeInAsc, SortMode.SizeInDesc,
        };
        void fullScreenModeChange(bool fullScreen)
        {
            // fullscreen = true の二重がけは想定されていない。fullscreen = false は pbView の位置調整のために多重に呼び出されることがある
            if (fullScreen == true && FullScreen == true) return;

            var temp = pbView_SizeChanged_SizeChanging;
            pbView_SizeChanged_SizeChanging = true;
            if (fullScreen)
            {
                FullScreen = true;

                HideUI = true;
                
                closeWindowtoolStripMenuItem.Visible = true;
                menuStrip.Location = new Point(0, -closeWindowtoolStripMenuItem.Bounds.Y);
                fullScreenToolStripMenuItem.Text = Message.Window;
                
                pbView_SizeChanged_SizeChanging = temp;
                pbView_SizeChanged(null, null);
            }
            else
            {
                HideUI = AlwaysHideUI;

                if (ipcLookAheadInfo == null) FullScreen = false; // FullScreen = false が非表示を解除してしまうので
                else FullScreen_pseudoFullScreen = false;
                
                //menuStrip.Location = new Point(0, 0); // TopMost == true ならこちらでもよい
                menuStrip.Bounds = new Rectangle(0, 0, ClientRectangle.Width, menuStrip.Bounds.Height);
                
                pbView.Left = 0;
                //pbView.Width = Width; // 起動直後は Width の値が正しくないのでこれは不適切

                // 実装変更により不要に
                //pbView.Width = ClientRectangle.Width;

                closeWindowtoolStripMenuItem.Visible = false;


                fullScreenToolStripMenuItem.Text = Message.Fullscreen;
                pbView_SizeChanged_SizeChanging = temp;
                pbView_SizeChanged(null, null);

                ViewerForm_SizeChanged_CurrentSize = Size;


                // if (ShowIconRequired)// ウインドウの状況によっては毎回必要なので条件はコメントアウト
                if (ipcLookAheadInfo == null) // 代わりの条件
                {
                    //ShowIconRequired = false;
                    ShowIcon = false;
                    ShowIcon = true;
                }
            }
        }

        private bool pbView_SizeChanged_UserResizing = false;
        private bool pbView_SizeChanged_ReloadPending = false;
        private void ViewerForm_ResizeBegin_V4(object sender, EventArgs e)
        {
            pbView_SizeChanged_UserResizing = true;
        }
        private void ViewerForm_ResizeEnd_V4(object sender, EventArgs e)
        {
            pbView_SizeChanged_UserResizing = false;
            if (pbView_SizeChanged_ReloadPending)
            {
                pbView_SizeChanged_ReloadPending = false;
                pbView_SizeChanged(pbView, EventArgs.Empty);
            }
        }

        bool mtbPage_ValueChanged_SendRequestToChangeSelection_Stop = false;
        private void mtbPage_ValueChanged(object sender, EventArgs e)
        {
            if (mtbPage.Value != currentPage)
            {
                currentPage = mtbPage.Value;
                if (!mtbPage_ValueChanged_SendRequestToChangeSelection_Stop) showCurrentPage();
            }

            if (!mtbPage_ValueChanged_SendRequestToChangeSelection_Stop)
            {
                if (SeekBarDragging) mtbPage_DragDeferred = true; // カタログへの選択の依頼はドラッグを終えたときに 1 回だけ（V24）
                else SendRequestToChangeSelectionForCurrentPage();
            }
            
            //ShowCurrentPageToStatusBar();
            if (ipcLookAheadInfo == null) SetCurrentImageLongPath(); // ページ番号の描画よりも後で呼び出す
        }

        private void SendRequestToChangeSelectionForCurrentPage()
        {
            {
                var elp = EntryLongPathArray;
                var ld = loader;
                if (elp != null && currentPage >= 0 && currentPage < elp.Length && ld != null)
                {
                    var path = elp[currentPage];
                    if (ld.Type == PackedImageLoader.PackType.Directory)
                    {
                        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    }

                    if (!string.IsNullOrEmpty(path))
                    {
                        SendRequestToChangeSelection(path);
                    }
                }
            }
        }

        // シークバーをドラッグしている間は、1 ページ変わるたびの先読みの組み直し・原寸の読み直しと破棄・
        // カタログへの選択の依頼をやめ、離したときに 1 回だけ行う（V24）。ドラッグ中の先読みは、トラックバーの上のカーソルに合わせる
        // 処理（mtbPage_MouseMove、50ms に 1 回まで）が組み直す
        private bool mtbPage_Dragging = false;
        private bool mtbPage_DragDeferred = false;

        // マウスを捕まえている間だけドラッグ中とみなす。離したことが届かなかった場合も、後回しにした処理を必ず行う
        private bool SeekBarDragging
        {
            get
            {
                if (!mtbPage_Dragging) return false;
                if (mtbPage.Capture) return true;
                BeginInvoke((MethodInvoker)EndSeekBarDrag);
                return false;
            }
        }

        private void EndSeekBarDrag()
        {
            if (!mtbPage_Dragging) return;
            mtbPage_Dragging = false;
            if (!mtbPage_DragDeferred) return;
            mtbPage_DragDeferred = false;
            if (ResizedImageArray == null) return;
            prefetchOrderPage = int.MinValue; // 先読みの範囲を必ず作り直させる
            showCurrentPage();
            if (!mtbPage_ValueChanged_SendRequestToChangeSelection_Stop) SendRequestToChangeSelectionForCurrentPage();
        }
        
        private void ShowCurrentPageToStatusBar()
        {
            var oiia = OriginalImageInfoArray;
            int i;
            if (oiia != null && (i = mtbPage.Value) >= 0 && i < oiia.Length)
            //if (!string.IsNullOrEmpty(currentArchiveFilePath))
            {
                var imgInfo = oiia[i];
                Size s;
                if (imgInfo != null)
                {
                    try
                    {
                        s = imgInfo.Size;
                    }
                    catch // 関数が呼び出された後、ここに到達する前に img が Dispose された場合
                    {
                        s = Size.Empty;
                    }
                }
                else
                {
                    s = Size.Empty;
                }
                // 描画のたびに呼ばれるので、表示する値が変わったときだけ文字列を作る（V19）
                var max = mtbPage.Maximum;
                if (showCurrentPageToStatusBar_Shown && showCurrentPageToStatusBar_Page == i && showCurrentPageToStatusBar_Max == max && showCurrentPageToStatusBar_Size == s) return;
                showCurrentPageToStatusBar_Shown = true;
                showCurrentPageToStatusBar_Page = i;
                showCurrentPageToStatusBar_Max = max;
                showCurrentPageToStatusBar_Size = s;
                pageToolStripStatusLabel.Text =$" {i + 1} / {max + 1}";
                imageSizeToolStripStatusLabel.Text = s.Width <= 0 || s.Height <= 0 ? "?x?" : $"{s.Width}x{s.Height}";
            }
            else
            {
                showCurrentPageToStatusBar_Shown = false;
                pageToolStripStatusLabel.Text = null;
                imageSizeToolStripStatusLabel.Text = null;
            }
        }
        private bool showCurrentPageToStatusBar_Shown = false;
        private int showCurrentPageToStatusBar_Page, showCurrentPageToStatusBar_Max;
        private Size showCurrentPageToStatusBar_Size;

        Tuple<int, int, int, int, Size[]> bwMagnifierMaker_Q = null;
        bool showCurrentPage_PagePrepared = false;
        Size showCurrentPage_PrevDrawSize = Size.Empty;
        int shownImegesPage = -1;
        private void showCurrentPage(bool setBackgroundModeIfPausing = true)
        {
            if (ResizedImageArray == null) return;

            // ページが移ったら先読みの範囲を作り直す（読み終えて止まっているワーカーもここで再開する）
            if (currentPage >= 0 && currentPage != prefetchOrderPage)
            {
                if (SeekBarDragging) mtbPage_DragDeferred = true; // ドラッグ中はトラックバーの処理に任せ、離したときに作り直す（V24）
                else SetBackgroundMode();
            }
            else if (setBackgroundModeIfPausing) SetBackgroundModeIfPausing();


            if (pbView_SizeChanged_SizeChanging) return;

            // 表示の近くに来たページの原寸を読み直し、遠ざかったページの原寸を捨てる（C1）。ドラッグ中は離したときに行う（V24）
            if (SeekBarDragging)
            {
                mtbPage_DragDeferred = true;
            }
            else
            {
                ReloadTrimmedOriginalsNearCurrent();
                TrimOriginals();
            }

            var magNotMaked = !MagnifierCanvasPrepared();
            
            if (MagnifierPhase1_Q)
            {
                MagnifierPhase1_Q = false;
                MagnifierPhase1(MagnifierPhase1_QValue, ifNoopThenQ: true);
                //optionToolStripMenuItem.Text = $"phase1 by Q {MagnifierPhase1_Q}";
            }
            else if (ViewerMode == ViewerModeEnum.Magnifier && magNotMaked)
            {
                pbView_Paint_Rectangle = Rectangle.Empty;
                ViewerMode = ViewerModeEnum.Normal;
                MagnifierPhase1(GetMagnifierRectangle_LastCursorPosition, ifNoopThenQ: true);
                MagnifierPhase2_Q = true;
            }

            //if (ViewerMode == ViewerModeEnum.CoverSetting && pbView_Paint_Rectangle == Rectangle.Empty)
            if (ViewerMode == ViewerModeEnum.CoverSetting)
            {
                pbView_Paint_Rectangle = GetCoverRectangleForDisplay();
            }

            // 読み込みの表示を行うため画像が準備されているかどうかにかかわらず Paint する
            //if (currentPage < NextPage)
            {
                showCurrentPage_PagePrepared = true;
                //pbView.Invalidate(false); // 描画は Paint イベントハンドラで行われる
                pbPaintInvalidate();
            }

            //shownImegesPath = currentArchiveFilePath;
            shownImegesPath = currentStartingFilePath;

            // ルーペを使わずにページを送り続けたら、ルーペを使っていない状態に戻す（V25）
            if (magnifierWanted && currentPage != shownImegesPage && shownImegesPage >= 0 && ViewerMode == ViewerModeEnum.Normal)
            {
                if (++magnifierUnusedPageTurns >= MagnifierUnusedPageTurnsLimit)
                {
                    magnifierWanted = false;
                    magnifierUnusedPageTurns = 0;
                    TrimOriginals(); // 残していた原寸を捨てる
                }
            }
            shownImegesPage = currentPage;
            
            // 拡大モードの作成。ルーペを一度も使っていなければ作らない（ページ送りのたびに全コアで数十 ms 使い、見開きで 20〜80MB 持っていた）（V2）
            if (magNotMaked && magnifierWanted)
            {
                resetMagnifier();
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        public const int WM_SETREDRAW = 0x000B;

        /// <summary>
        /// コントロール(子コントロールも含む)の描画を停止します。
        /// </summary>
        /// <param name="control">対象コントロール</param>
        public static void BeginUpdate(Control control)
        {
            SendMessage(control.Handle, WM_SETREDRAW, 0, 0);
        }

        /// <summary>
        /// コントロール(子コントロールも含む)の描画を開始します。
        /// </summary>
        /// <param name="control">対象コントロール</param>
        public static void EndUpdate(Control control)
        {
            SendMessage(control.Handle, WM_SETREDRAW, 1, 0);
            control.Refresh();
        }
        private void MyActivate(bool finallyTopMost)
        {
            //Thread.Sleep(100);

            /////
            //showWithoutActivation = true;
            /////

            Visible = true;
            TopMost = true;
            Activate();

            if (!(finallyTopMost && TopMostInFullscreen)) TopMost = false;
            
        }
        

        private void closeWindowtoolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        int menuStrip_Bottom = 0;
        int mtbPageTop_As_Reference_pbViewBottom = 0;
        private void menuStrip_MouseLeave(object sender, EventArgs e)
        {
            if (HideUI && !ContextMenuForMenuStrip_Opend && menuStrip_Bottom == 0)// && !ContextMenuForMenuStripPostProcessing_worked)
            {
                menuStrip_Bottom = menuStrip.Bounds.Bottom;
                menuStrip.Visible = false;
                
                ksViewForm.IgnoreAnyMouseEventOnce = MouseButtons.Left;
            }
            //ContextMenuForMenuStripPostProcessing_worked = false;
        }
        
        private void hideUnderUI(object sender, EventArgs e)
        {
            if (HideUI && /*mtbPage_Top*/mtbPageTop_As_Reference_pbViewBottom == 0)
            {
                // mtbPage_Top = pnlSeekbar.Bounds.Top;
                mtbPageTop_As_Reference_pbViewBottom = pnlSeekbar.Bounds.Top - pbView.Bottom;
                pnlSeekbar.Visible = false;
                statusStrip.Visible = false;
                
                ksViewForm.IgnoreAnyMouseEventOnce = MouseButtons.Left;
            }
        }
        
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            base.WndProc(ref m);
            const int WM_NCMOUSELEAVE = 0x02A2;
            //const int WM_MOUSELEAVE = 0x02A3;

            //if ((m.Msg & WM_MOUSELEAVE) == WM_MOUSELEAVE) hideUnderUI(null, null);
            // return;

            switch (m.Msg)
            {
                case WM_NCMOUSELEAVE:
                    //case WM_MOUSELEAVE:
                    // hideUnderUI(null, null);
                    hideUnderUIForMouseLeave(null, null); // 右下のウィンドウサイズ変更の箇所をクリックしても WM_NCMOUSELEAVE が発生するため。
                    break;
            }
        }
        
        
        private void currentImageLongPathToolStripTextBox_MouseLeave(object sender, EventArgs e)
        {
            if (HideUI && mtbPageTop_As_Reference_pbViewBottom == 0)
            {
                var cursorPosition = Cursor.Position;
                
                // 境界上に乗っていた場合にその後 MouseLeave が発生しなくなる現象への対策
                // ポインタが境界上に飛び込んだ場合の対処はタイマーに任せる
                var positionOnClient = statusStrip.PointToClient(cursorPosition);
                var bounds = currentImageLongPathToolStripTextBox.Bounds;
                if (bounds.Contains(positionOnClient))
                {
                    var dl = positionOnClient.X - bounds.Left + 1;
                    var dr = bounds.Right - positionOnClient.X;
                    var dt = positionOnClient.Y - bounds.Top + 1;
                    var db = bounds.Bottom - positionOnClient.Y;
                    var dm = Math.Min(Math.Min(Math.Min(dl, dr), dt), db);
                    if (dm == db)
                    {
                        cursorPosition.Y += db;
                        Cursor.Position = cursorPosition;
                    }
                }

                if (!pbView.ClientRectangle.Contains(pbView.PointToClient(Cursor.Position)))
                {
                    hideUnderUI(null, null);
                }
            }
        }
        
        private void hideUnderUIForMouseLeave(object sender, EventArgs e)
        {
            if (HideUI && mtbPageTop_As_Reference_pbViewBottom == 0)
            {
                var cursorPosition = Cursor.Position;
                var clientPoint = PointToClient(cursorPosition);
                var x = pnlSeekbar.Left;
                var y = pnlSeekbar.Top;
                if (!new Rectangle(x, y, statusStrip.Right - x, statusStrip.Bottom - y).Contains(clientPoint) ||
                    !ActivateManager.InVisibleRegion(this, clientPoint, cursorPosition, checkParentsCount: 3))
                {
                    hideUnderUI(null, null);
                }
            }
        }

        // 上が期待通りに働かないので下で対処
        private bool MouseInPbView = false;
        private void pbView_MouseEnter(object sender, EventArgs e)
        {
            MouseInPbView = true;
            hideUnderUI(null, null);
            //pbView.Focus();
            focusIfRightness(pbView);

            if (ViewerMode == ViewerModeEnum.MagnifierOpening || ViewerMode == ViewerModeEnum.Magnifier || ViewerMode == ViewerModeEnum.MagnifierClosing)
            {
                Program.HideCursor();
            }
        }

        Point pbView_MouseMove_prevPos;
        Point? pbView_MouseMove_prevContPos = null;
        bool pbView_PreviewKeyDown_To_pbView_MouseMove_StopShwoMagnifierRectangle = false;
        private void pbView_MouseMove(object sender, MouseEventArgs e)
        {
            var stopShowMag = pbView_PreviewKeyDown_To_pbView_MouseMove_StopShwoMagnifierRectangle;
            pbView_PreviewKeyDown_To_pbView_MouseMove_StopShwoMagnifierRectangle = false;
            if (pbView_MouseMove_prevPos == e.Location) return;
            var afterEnter = pbView_MouseMove_prevContPos == null;
            pbView_MouseMove_prevContPos = pbView_MouseMove_prevPos = e.Location;
            TouchListener_Pan_Delta = null;

            if (!afterEnter) ksViewForm.IgnoreAnyMouseEventOnce = MouseButtons.None;

            if (!(ViewerMode == ViewerModeEnum.MagnifierOpening || ViewerMode == ViewerModeEnum.Magnifier || ViewerMode == ViewerModeEnum.MagnifierClosing))
            {
                Program.ShowCursor();
            }
            //setMouseButtonShape(GetPositionOnView(e.Location));

            //var y = pbView.PointToScreen(e.Location).Y;
            if (!CursorBounded())
            {
                var y = e.Location.Y;
                if (menuStrip_Bottom > 0 && y < menuStrip_Bottom)
                {
                    menuStrip.Visible = true;
                    menuStrip_Bottom = 0;

                    hideUnderUI(null, null); // currentImageLongPathToolStripTextBox_MouseLeave の仕様が期待通りでないので
                }
                else if (showUnderUI(e.Location)) { }
            }

            if (ViewerMode == ViewerModeEnum.MagnifierOpening || ViewerMode == ViewerModeEnum.MagnifierClosing)
            {
                ShwoMagnifierRectangle();
            }
            else if (ViewerMode == ViewerModeEnum.Magnifier)
            {
                var showMag = TouchListener_Pan_Delta == null && GetMagnifierRectangle_LastCursorPosition == null;

                if (showMag)
                {
                    //pbView.Invalidate(false); // 描画は Paint イベントハンドラで行われる
                    pbPaintInvalidate();
                }
            }
            else if (ViewerMode == ViewerModeEnum.CoverSetting)
            {
                if (!stopShowMag) ShwoCoverRectangle();
            }
            else if (DrawMeasure) pbPaintDrawOnlyMeasure();
        }

        private bool showUnderUI(Point pbViewClientPoint)
        {
            //if (mtbPage_Top > 0 && pbViewClientPoint.Y >= mtbPage_Top)
            if (mtbPageTop_As_Reference_pbViewBottom != 0 && pbViewClientPoint.Y >= pbView.Bottom + mtbPageTop_As_Reference_pbViewBottom)
            {
                pnlSeekbar.Visible = true;
                pnlSeekbar_PreStatusStripVisibleTrue();
                statusStrip.Visible = true;
                if (SetCurrentImageLongPathRequired) SetCurrentImageLongPath();
                //mtbPage_Top = 0;
                mtbPageTop_As_Reference_pbViewBottom = 0;
                return true;
            }
            return false;
        }

        // 画面のちらつきを回避する
        private void pnlSeekbar_PreStatusStripVisibleTrue()
        {
            if (statusStrip_VisibleChanged_FirstVisible)
            {
                var currentBound = statusStrip.Bounds;
                var statusStripBound = new Rectangle(pnlSeekbar.Left, statusStrip.Parent.Bottom - currentBound.Height, pnlSeekbar.Width, currentBound.Height); // 二回目以降と完全に一致
                statusStrip.Bounds = statusStripBound;
            }
        }


        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (
                KeyboardShortcut.GetKeyState((int)Keys.D) < 0 &&
                KeyboardShortcut.GetKeyState((int)Keys.E) < 0 &&
                KeyboardShortcut.GetKeyState((int)Keys.B) < 0 &&
                KeyboardShortcut.GetKeyState((int)Keys.U) < 0 &&
                KeyboardShortcut.GetKeyState((int)Keys.G) < 0)
            {
                var i = bmwLoadEachPage_DoWork_DEBUG_WorkNumber;
                var index = i >= 0 ? (i + 1).ToString() : "<EMPTY>";
                Program.ShowCursor();
                MessageBox.Show(this,
                    $"This is \"Debug information\" for the developer.\n" +
                    $"Loading index = {index}\n" +
                    $"Phase number = {bmwLoadEachPage_DoWork_DEBUG_Phase}\n" +
                    $"\n" +
                    $"Hold down '7' key and 'Z' key, and click \"OK\" to prioritize 7z.dll."
                    ,
                    "Debug information", MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (
                KeyboardShortcut.GetKeyState((int)Keys.D7) < 0 &&
                KeyboardShortcut.GetKeyState((int)Keys.Z) < 0)
                {
                    var sevenZipPath = PackedImageLoader.GetSevenZipPath();
                    if (sevenZipPath != null)
                    {
                        PackedImageLoader.PrioritizeSevenZip = true;
                        Program.ShowCursor();
                        MessageBox.Show(this,
                            $"Following dll will be preferentially used until the window is closed.\n" +
                            $"\n" +
                            $"{sevenZipPath}."
                            ,
                            "Debug information", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        var prevArchiveFilePath = getCurrentRenameTarget();
                        DisposeLoader(); // 読み込みジョブが取り出し中でないときに（V1）

                        OpenFile(prevArchiveFilePath, currentPage, currentSortModeDetails);
                    }
                    else
                    {
                        Program.ShowCursor();
                        MessageBox.Show(this,
                            $"7z.dll is not found."
                            ,
                            "Debug information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            else
            {
                Program.ShowAbout(this);
            }
        }
        
        private int mtbPage_MouseMove_LastValue = int.MinValue;
        private int mtbPage_MouseMove_LastCallCount = -1;
        private int mtbPage_MouseMove_LastTick;
        private System.Windows.Forms.Timer mtbPage_MouseMove_Timer;
        private void mtbPage_MouseMove(object sender, MouseEventArgs e)
        {
            mtbPage.ValueCanChangedByMouseButton = true;
            if (ResizedImageArray == null) return;
            if (mtbPage.PointToValue(e.Location) == mtbPage_MouseMove_LastValue && SetBackgroundMode_CallCount == mtbPage_MouseMove_LastCallCount) return;
            var elapsed = unchecked(Environment.TickCount - mtbPage_MouseMove_LastTick);
            if (0 <= elapsed && elapsed < mtbPage_MouseMove_Interval)
            {
                if (mtbPage_MouseMove_Timer == null)
                {
                    mtbPage_MouseMove_Timer = new System.Windows.Forms.Timer(components) { Interval = mtbPage_MouseMove_Interval };
                    mtbPage_MouseMove_Timer.Tick += (s, ev) => SetBackgroundModeForTrackbarHover();
                }
                if (!mtbPage_MouseMove_Timer.Enabled) mtbPage_MouseMove_Timer.Start();
                return;
            }
            SetBackgroundModeForTrackbarHover();
        }

        private void SetBackgroundModeForTrackbarHover()
        {
            mtbPage_MouseMove_Timer?.Stop();
            mtbPage_MouseMove_LastTick = Environment.TickCount;
            mtbPage_MouseMove_LastValue = mtbPage.PointToValue(mtbPage.PointToClient(Cursor.Position));
            SetBackgroundMode();
            mtbPage_MouseMove_LastCallCount = SetBackgroundMode_CallCount;
        }

        private bool MouseDownForCoverSettingExit = false;
        private void pbView_MouseDown(object sender, MouseEventArgs e)
        {
            // キーボードショートカットに置き換え
            if (e.Button == MouseButtons.Left)
            {
                if (ksViewForm.IgnoreAnyMouseEventOnce != MouseButtons.Left)
                {
                    if (ViewerMode == ViewerModeEnum.CoverSetting)
                    {
                        SetCoverForThumbnail();
                        ksViewForm.IgnoreMouseEventOnce = e;
                    }
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (ViewerMode == ViewerModeEnum.CoverSetting)
                {
                    ExitCoverSetting();
                    if (mgView.Enabled)
                    {
                        MouseDownForCoverSettingExit = true;
                    }
                    else
                    {
                        ksViewForm.IgnoreMouseEventOnce = e;
                    }
                }
            }
            //setMouseButtonShape(position);
        }


        private void closeWindowtoolStripMenuItem_MouseEnter(object sender, EventArgs e)
        {
            // メニューバー上のボタンの色の変化をマウスカーソルの位置と正確に一致させるための処理
            if (FullScreen)
            {
                var tsmi = (ToolStripMenuItem)sender;
                tsmi.Select();
            }
            
            // 古い方法
        }

        private void ChangePageDirection(BindingMode binding)
        {
            if(BindingMode != binding)
            {
                var sizeChanged = getNeedToResizeImage(binding, bindingModeField);
                //BindingMode = binding;
                ChangeBindingModeCore(binding, redraw: !sizeChanged);
                if (sizeChanged) Reload(force: true);
                ShowViewSettingHint();
            }
            
            //PageLeftToRight = pageLeftToRight;
        }

        private bool getNeedToResizeImage(BindingMode a, BindingMode b)
        {
            var aIsSpread = a == BindingMode.LeftToRight || a == BindingMode.RightToLeft;
            var bIsSpread = b == BindingMode.LeftToRight || b == BindingMode.RightToLeft;
            if (aIsSpread && bIsSpread) return false;
            if (MaxDivision > 1 || MinPageCountInWindow > 1) return true;
            return !((aIsSpread || a == BindingMode.SinglePage) && (bIsSpread || b == BindingMode.SinglePage));
        }
        

        private static readonly int Control_PreviewKeyDown_Speed = Program.DpiScaling(20);
        private bool KeysUpState = false;
        private bool KeysDownState = false;
        private bool KeysLeftState = false;
        private bool KeysRightState = false;
        private void Control_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                var preUp = KeysUpState;
                var preDown = KeysDownState;
                var preLeft = KeysLeftState;
                var preRight = KeysRightState;

                var up = e.KeyCode == Keys.Up; ;
                var down = e.KeyCode == Keys.Down;
                var left  = e.KeyCode == Keys.Left;
                var right = e.KeyCode == Keys.Right;

                KeysUpState = KeysUpState || up;
                KeysDownState = KeysDownState || down;
                KeysLeftState = KeysLeftState || left;
                KeysRightState = KeysRightState || right;

                var vm = ViewerMode;
                if (vm == ViewerModeEnum.MagnifierOpening || vm == ViewerModeEnum.MagnifierClosing)
                {
                    var r = BoolToInt(KeysRightState);
                    var l = BoolToInt(KeysLeftState);
                    var d = BoolToInt(KeysDownState);
                    var u = BoolToInt(KeysUpState);

                    var sum = r + l + d + u;

                    if (sum == 1 || sum == 2 && (KeysRightState ^ KeysLeftState))
                    {
                        var xu = r - l;
                        var yu = d - u;

                        var currentPosition = Cursor.Position;
                        Cursor.Position = new Point(currentPosition.X + Control_PreviewKeyDown_Speed * xu, currentPosition.Y + Control_PreviewKeyDown_Speed * yu);
                    }
                    else if (sum == 2)
                    {
                        // 直前の操作をキャンセル
                        var xu = !preRight && right ? +1 : !preLeft && left ? -1 : 0;
                        var yu = !preDown && down ? +1 : !preUp && up ? -1 : 0;
                        if (xu != 0 || yu != 0)
                        {
                            var currentPosition = Cursor.Position;
                            pbView_PreviewKeyDown_To_pbView_MouseMove_StopShwoMagnifierRectangle = true;
                            Cursor.Position = new Point(currentPosition.X + Control_PreviewKeyDown_Speed * xu, currentPosition.Y + Control_PreviewKeyDown_Speed * yu);
                        }

                        // 上下で拡大、左右で縮小
                        changeMagnifyingPowerInMagnifierOpeningOrClosing(zoomUp: KeysUpState);
                    }
                }
            }
            else if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey)
            {
                changeMagnifyingPowerInMagnifierOpeningOrClosing(zoomUp: true);
            }
            else if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey)
            {
                changeMagnifyingPowerInMagnifierOpeningOrClosing(zoomUp: false);
            }
        }

        private void ViewerForm_KeyUp(object sender, KeyEventArgs e)
        {
            KeysUpState = KeysUpState && e.KeyCode != Keys.Up;
            KeysDownState = KeysDownState && e.KeyCode != Keys.Down;
            KeysLeftState = KeysLeftState && e.KeyCode != Keys.Left;
            KeysRightState = KeysRightState && e.KeyCode != Keys.Right;
        }

        private static int BoolToInt(bool b)
        {
            return b ? 1 : 0;
        }


        private void openFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileWithDialog();
        }

        private void openFileWithDialog()
        {
            if (ExitAfterCoverSetting) return;
            var path = Program.ItsSelfOrExistingParentDirectory(currentArchiveFilePath);
            if (!string.IsNullOrEmpty(path))
            {
                openFileDialog.InitialDirectory = path;
            }
            openFileDialog.FileName = "";
            var supportedImageFileFilter = ImageLoader.GetSupportedFileFilter();
            PackedImageLoader.CheckSevenZipExistence(this);
            var supportedArchiveFileFilter = PackedImageLoader.GetSupportedArchiveFileFilter();
            openFileDialog.Filter = Message.AllSupportedFiles + "|" + supportedArchiveFileFilter + ";" + supportedImageFileFilter + "|"
                + Message.ArchiveFilesPdfFiles + "|" + supportedArchiveFileFilter + "|"
                + Message.ImageFiles + "|" + supportedImageFileFilter;

            if (openFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                removeHiddenConfigFromCatalogForm();
                OpenFile(openFileDialog.FileName);
            }
        }

        private void openNewThumbnailWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openThumbnailWindow();
        }
        
        private void switchToThumbnailWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            switchToThumbnailWindow();
        }

        private void switchToThumbnailWindow()
        {
            SaveSettings();
            openThumbnailWindow(cancelFullscreen: false);
            CloseWithoutSave();
        }

        private void openThumbnailWindow() { openThumbnailWindow(cancelFullscreen: true); }
        private void openThumbnailWindow(bool cancelFullscreen)
        {
            if (ExitAfterCoverSetting) return;
            try
            {
                var c = currentPage;
                var ea = EntryLongPathArray;
                if (ea != null && c >= 0 && c < ea.Length)
                {
                    if (currentSortModeDetails != null)
                    {
                        var sort = currentSortModeDetails.SortMode;
                        if (sort == SortMode.NameInAsc || sort == SortMode.NameInDesc)
                        {
                            System.Diagnostics.Process.Start(Application.ExecutablePath, $"-c \"{ea[c]}\" -{sort}");
                        }
                        else if(sort == SortMode.Random && !string.IsNullOrEmpty(currentSortModeDetails.RandomSeed))
                        {
                            System.Diagnostics.Process.Start(Application.ExecutablePath, $"-c \"{ea[c]}\" -{sort} \"{currentSortModeDetails.RandomSeed}\"");
                        }
                        else
                        {
                            System.Diagnostics.Process.Start(Application.ExecutablePath, $"-c \"{ea[c]}\" -{currentSortModeDetails.PreSortMode} -{sort}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Process.Start(Application.ExecutablePath, $"-c \"{ea[c]}\"");
                    }
                }
                else if (string.IsNullOrEmpty(currentArchiveFilePath))
                {
                    System.Diagnostics.Process.Start(Application.ExecutablePath, "-c");
                }
                else
                {
                    System.Diagnostics.Process.Start(Application.ExecutablePath, "-c \"" + currentArchiveFilePath + "\"");
                }
                if (cancelFullscreen && FullScreen)
                {
                    fullScreenModeChange(false);
                }
            }
            catch (Exception error)
            {
                Program.AlertError(error);
            }
        }

        private void cloneTheWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            cloneWindow();
        }

        private void cloneWindow()
        {
            if (ExitAfterCoverSetting) return;
            try
            {
                var longPath = null as string;
                var value = mtbPage.Value;
                if (value >= 0)
                {
                    var elpa = EntryLongPathArray;
                    if (elpa != null && value < elpa.Length)
                    {
                        longPath = elpa[value];
                        if (longPath == "") longPath = null;
                    }
                }

                var info = CreateCommandLineOptionInfo();
                info.OpenInPreviousImageFilterSetting = true;
                info.InitialFullscreenMode = InitialFullscreenMode.Default;
                var option = GetCommandLineOption(info, minimize: true);
                if (longPath != null) option += $" \"{longPath}\"";

                SaveSettings();

                Process.Start(Application.ExecutablePath, option);
            }
            catch (Exception error)
            {
                Program.AlertError(error);
            }
        }

        private void fullScreenInStartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            fullScreenModeChangeToFullScreen();
        }

        private void fullScreenModeChangeToFullScreen()
        {
            fullScreenModeChange(true);
        }

        private void windowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            fullScreenModeChangeToWindow();
        }

        private void fullScreenModeChangeToWindow()
        {
            fullScreenModeChange(false);
        }

        private void toggleFullScreenModeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            toggleFullScreenMode();
        }

        private void toggleFullScreenMode()
        {
            fullScreenModeChange(!FullScreen);
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }


        private void magnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleMagnifierWithoutScreenEffects();
        }


        private void ToggleMagnifierWithoutScreenEffects()
        {
            // if は省略できないので注意
            if (ViewerMode == ViewerModeEnum.Normal)
            {
                enableMagnifier();
            }
            else if (ViewerMode == ViewerModeEnum.Magnifier)
            {
                disableMagnifier();
            }
        }

        private void enableMagnifier()
        {
            if (ViewerMode == ViewerModeEnum.Normal)
            {
                MagnifierPhase1(null);
                MagnifierPhase2();
            }
        }

        private void disableMagnifier()
        {
            if (ViewerMode == ViewerModeEnum.Magnifier)
            {
                MagnifierPhase3(toPhase4: true);
                MagnifierPhase4(toPhase1: false);
            }
        }

        private void toggleMagnifierForKeyboardWithOperatingGuide() => toggleMagnifierForKeyboard(withOperatingGuide: true);
        private void toggleMagnifierForKeyboard() => toggleMagnifierForKeyboard(withOperatingGuide: false);
        private void toggleMagnifierForKeyboard(bool withOperatingGuide)
        {
            // if は省略できないので注意
            if (ViewerMode == ViewerModeEnum.Normal)
            {
                enableMagnifierForKeyboard(withOperatingGuide);
            }
            else if (ViewerMode == ViewerModeEnum.Magnifier)
            {
                disableMagnifierForKeyboard(withOperatingGuide);
            }
        }


        private void toggleMagnifierForKeyboardWithMinimalScreenEffects()
        {
            // if は省略できないので注意
            if (ViewerMode == ViewerModeEnum.Normal)
            {
                enableMagnifierForKeyboard();
            }
            else if (ViewerMode == ViewerModeEnum.Magnifier)
            {
                disableMagnifier();
                //disableMagnifierForKeyboard();
            }
        }

        bool userMagnifierOpeningOrClosing = false;
        bool UserMagnifierOpeningOrClosing
        {
            get { return userMagnifierOpeningOrClosing; }
            set
            {
                if (value != userMagnifierOpeningOrClosing)
                {
                    if (value)
                    {
                        userMagnifierOpeningOrClosing = true;
                        ksViewForm.WheelAcceptControls = new Control[0];
                    }
                    else
                    {
                        userMagnifierOpeningOrClosing = false;
                        ksViewForm.WheelAcceptControls = new Control[1] { pbView };
                    }
                }
            }
        }

        private void enableMagnifierForKeyboardWithOperatingGuide() => enableMagnifierForKeyboard(withOperatingGuide: true);
        private void enableMagnifierForKeyboard() => enableMagnifierForKeyboard(withOperatingGuide: false);
        private void enableMagnifierForKeyboard(bool withOperatingGuide)
        {
            var vm = ViewerMode;
            if (vm == ViewerModeEnum.Normal)
            {
                //if (ksViewForm.KeyboardShortcutExcutingWithKeyOrMouseDown()) UserMagnifierOpeningOrClosing = true;
                UserMagnifierOpeningOrClosing = true;
                if (withOperatingGuide) pbView_Paint_RectangleMessage = Message.MagnifierOperatingGuide.Replace("\\n", "\n");
                MagnifierPhase1(null);
                KeyUp += MagnifierPhase2_KeyUpEventHandler;
                MouseUp += MagnifierPhase2_KeyUpEventHandler;
                pbView.SizeChanged += MagnifierPhase2_KeyUpEventHandler;
                foreach (Control control in Controls)
                {
                    control.MouseUp += MagnifierPhase2_KeyUpEventHandler;
                }
            }
            else if(vm == ViewerModeEnum.Magnifier)
            {
                //if (ksViewForm.KeyboardShortcutExcutingWithKeyOrMouseDown()) UserMagnifierOpeningOrClosing = true;
                UserMagnifierOpeningOrClosing = true;
                MagnifierPhase3(toPhase4: true);
                MagnifierPhase4(toPhase1: true);
                if (withOperatingGuide) pbView_Paint_RectangleMessage = Message.MagnifierOperatingGuide.Replace("\\n", "\n");
                MagnifierPhase1(null);
                KeyUp += MagnifierPhase2_KeyUpEventHandler;
                pbView.MouseUp += MagnifierPhase2_KeyUpEventHandler;
                pbView.SizeChanged += MagnifierPhase2_KeyUpEventHandler;
                foreach (Control control in Controls)
                {
                    control.MouseUp += MagnifierPhase2_KeyUpEventHandler;
                }
            }
        }

        private void MagnifierPhase2_KeyUpEventHandler(object sender, MouseEventArgs e)
        {
            var key = ksViewForm.MouseButtonsToKeys(e.Button);
            if (key != Keys.None)
            {
                MagnifierPhase2_KeyUpEventHandler(sender, new KeyEventArgs(key));
            }
        }

        private void MagnifierPhase2_KeyUpEventHandler(object sender, KeyEventArgs e)
        {
           // UserMagnifierOpeningOrClosing = false;
            var actions = ksViewForm.Actions;
            if (actions == null || actions.Any(action =>
                IsEnableMagnifierCommand((Command)action.Key) && action.Shortcut.LastOrDefault()?.Contains(e.KeyCode) == true))
            {
                MagnifierPhase2_KeyUpEventHandler(sender, e as EventArgs);
            }
        }

        private static bool IsEnableMagnifierCommand(Command command)
        {
            return command == Command.ToggleMagnifier || command == Command.ToggleMagnifierWithoutOperatingGuide || command == Command.ToggleMagnifierWithMinimalScreenEffects ||
                command == Command.EnableMagnifier || command == Command.EnableMagnifierWithoutOperatingGuide;
        }

        private void MagnifierPhase2_KeyUpEventHandler(object sender, EventArgs e)
        {
            UserMagnifierOpeningOrClosing = false;
            MagnifierPhase2();
            KeyUp -= MagnifierPhase2_KeyUpEventHandler;
            pbView.MouseUp -= MagnifierPhase2_KeyUpEventHandler;
            pbView.SizeChanged -= MagnifierPhase2_KeyUpEventHandler;
            foreach (Control control in Controls)
            {
                control.MouseUp -= MagnifierPhase2_KeyUpEventHandler;
            }
        }

        private void disableMagnifierForKeyboardWithOperatingGuide() => disableMagnifierForKeyboard(withOperatingGuide: true);
        private void disableMagnifierForKeyboard() => disableMagnifierForKeyboard(withOperatingGuide: false);
        private void disableMagnifierForKeyboard(bool withOperatingGuide)
        {
            var vm = ViewerMode;
            if (vm == ViewerModeEnum.Magnifier)
            {
                //if (ksViewForm.KeyboardShortcutExcutingWithKeyOrMouseDown()) UserMagnifierOpeningOrClosing = true;
                UserMagnifierOpeningOrClosing = true;
                if (withOperatingGuide) pbView_Paint_RectangleMessage = Message.MagnifierOperatingGuide.Replace("\\n", "\n");
                MagnifierPhase3(toPhase4: false);
                KeyUp += MagnifierPhase4_KeyUpEventHandler;
                MouseUp += MagnifierPhase4_KeyUpEventHandler;
                pbView.SizeChanged += MagnifierPhase4_KeyUpEventHandler;
                foreach (Control control in Controls)
                {
                    control.MouseUp += MagnifierPhase4_KeyUpEventHandler;
                }
            }
            else if(vm == ViewerModeEnum.Normal)
            {
                //if (ksViewForm.KeyboardShortcutExcutingWithKeyOrMouseDown()) UserMagnifierOpeningOrClosing = true;
                UserMagnifierOpeningOrClosing = true;
                MagnifierPhase1(null);
                MagnifierPhase2();
                if (withOperatingGuide) pbView_Paint_RectangleMessage = Message.MagnifierOperatingGuide.Replace("\\n", "\n");
                MagnifierPhase3(toPhase4: false);
                KeyUp += MagnifierPhase4_KeyUpEventHandler;
                MouseUp += MagnifierPhase4_KeyUpEventHandler;
                pbView.SizeChanged += MagnifierPhase4_KeyUpEventHandler;
                foreach (Control control in Controls)
                {
                    control.MouseUp += MagnifierPhase4_KeyUpEventHandler;
                }
            }
        }

        private void MagnifierPhase4_KeyUpEventHandler(object sender, MouseEventArgs e)
        {
            var key = ksViewForm.MouseButtonsToKeys(e.Button);
            if (key != Keys.None)
            {
                MagnifierPhase4_KeyUpEventHandler(sender, new KeyEventArgs(key));
            }
        }

        private void MagnifierPhase4_KeyUpEventHandler(object sender, KeyEventArgs e)
        {
            var actions = ksViewForm.Actions;
            if (actions == null || actions.Any(action => IsDisableMagnifierWithEffectCommand((Command)action.Key) && action.Shortcut.LastOrDefault()?.Contains(e.KeyCode) == true))
            {
                MagnifierPhase4_KeyUpEventHandler(sender, e as EventArgs);
            }
        }

        private static bool IsDisableMagnifierWithEffectCommand(Command command)
        {
            return command == Command.ToggleMagnifier || command == Command.DisableMagnifier || command == Command.DisableMagnifierWithoutOperatingGuide
                || command == Command.ToggleMagnifierWithoutOperatingGuide;
        }

        private void MagnifierPhase4_KeyUpEventHandler(object sender, EventArgs e)
        {
            UserMagnifierOpeningOrClosing = false;
            MagnifierPhase4(toPhase1: false);
            KeyUp -= MagnifierPhase4_KeyUpEventHandler;
            MouseUp -= MagnifierPhase4_KeyUpEventHandler;
            pbView.SizeChanged -= MagnifierPhase4_KeyUpEventHandler;
            foreach (Control control in Controls)
            {
                control.MouseUp -= MagnifierPhase4_KeyUpEventHandler;
            }
        }

        private void rightToLeftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ChangePageDirection(BindingMode.RightToLeft);
        }

        private void leftToRightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ChangePageDirection(BindingMode.LeftToRight);
        }

        private void singlePageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ChangePageDirection(BindingMode.SinglePage);
        }

        private void singlePageWithoutScalingUpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ChangePageDirection(BindingMode.SinglePageWithoutScalingUp);
        }

        private void forceTwoPageSpreadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleMinPageCountInWindow();
        }

        private void ToggleMinPageCountInWindow()
        {
            MinPageCountInWindow = 3 - MinPageCountInWindow;
            //if (MinPageCountInWindow > 1) MaxDivision = 1; // 強制見開きページの場合必要、見開き優先なら不要
            if (BindingMode == BindingMode.SinglePage || BindingMode == BindingMode.SinglePageWithoutScalingUp)
            {
                showCurrentPage();
            }
            else
            {
                Reload(force: true);
            }
        }

        private void allowPageDivisionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleMaxDivision();
        }

        private void ToggleMaxDivision()
        {
            MaxDivision = 3 - MaxDivision;
            //if (MaxDivision > 1) MinPageCountInWindow = 1; // 強制見開きページの場合必要、見開き優先なら不要
            if (BindingMode == BindingMode.SinglePage || BindingMode == BindingMode.SinglePageWithoutScalingUp)
            {
                showCurrentPage();
            }
            else
            {
                Reload(force: true);
            }
        }

        private void nextPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            moveToNextPage();
        }

        private void moveToNextPage()
        {
            if (ResizedImageArray == null) return;
            var count = ResizedImageArray.Length;
            if (count == 0) return;
            var nextPage = NextPage;
            if (nextPage < count && (nextPage > currentPage || currentPage == nextPage && CurrentDividedPosition >= 0 && MaxDivision > 1))
            {
                movePageNatural(1);
            }
            else if (nextPage == count && CurrentDividedPosition + 1 == getCurrentPageDivison())
            {
                ksViewForm?.InvokeCommand(ExtendedKeys.NextPageAtLastPage);
            }
        }

        private void previousPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MoveToPreviousPage();
        }

        private void MoveToPreviousPage()
        {
            if (ResizedImageArray == null) return;
            var count = ResizedImageArray.Length;
            if (count == 0) return;
            if (currentPage > 0 || currentPage == 0 && CurrentDividedPosition > 0 && MaxDivision > 1)
            {
                movePageNatural(-1);
            }
            else if (currentPage == 0 && CurrentDividedPosition == 0)
            {
                ksViewForm?.InvokeCommand(ExtendedKeys.PreviousPageAtFirstPage);
            }
        }

        private void rightPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            moveToRightPage();
        }

        private void moveToRightPage()
        {
            if (BindingMode != BindingMode.RightToLeft)
            {
                moveToNextPage();
            }
            else
            {
                MoveToPreviousPage();
            }
        }

        private void leftPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            moveToLeftPage();
        }

        private void moveToLeftPage()
        {
            if(BindingMode != BindingMode.RightToLeft)
            {
                MoveToPreviousPage();
            }
            else
            {
                moveToNextPage();
            }
            
        }

        private void moveForward1PageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            movePageToMinimulForward();
            //movePage(1);
        }

        private void moveBack1PageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            movePageToMinimulBack();
            //movePage(-1);
        }

        private void moveRight1PageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            movePageToMinimulRight();
        }

        private void movePageToMinimulRight()
        {
            if (BindingMode != BindingMode.RightToLeft) movePageToMinimulForward(); else movePageToMinimulBack();
        }

        private void moveLeft1PageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            movePageToMinimulLeft();
        }

        private void movePageToMinimulLeft()
        {
            if (BindingMode != BindingMode.RightToLeft) movePageToMinimulBack(); else movePageToMinimulForward();
        }

        private void percentage000YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.0); }
        private void percentage010YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.1); }
        private void percentage020YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.2); }
        private void percentage030YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.3); }
        private void percentage040YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.4); }
        private void percentage050YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.5); }
        private void percentage060YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.6); }
        private void percentage070YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.7); }
        private void percentage080YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.8); }
        private void percentage090YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(0.9); }
        private void percentage100YoolStripMenuItem_Click(object sender, EventArgs e) { movePageByRatio(1.0); }


        private double getRatioByPage(int p = -1)
        {
            if (p < 0) p = currentPage;
            if (ResizedImageArray == null) return 0;
            var pageCount = ResizedImageArray.Length;
            return Math.Max(0, Math.Min(1, (double)p / pageCount));
        }

        private int getPageByRatio(double r)
        {
            if (ResizedImageArray == null) return -1;
            var pageCount = ResizedImageArray.Length;
            return Math.Max(0, Math.Min(pageCount - 1, (int)(r * pageCount)));
        }

        private void movePageByRatio000() { movePageByRatio(0.0); }
        private void movePageByRatio010() { movePageByRatio(0.1); }
        private void movePageByRatio020() { movePageByRatio(0.2); }
        private void movePageByRatio030() { movePageByRatio(0.3); }
        private void movePageByRatio040() { movePageByRatio(0.4); }
        private void movePageByRatio050() { movePageByRatio(0.5); }
        private void movePageByRatio060() { movePageByRatio(0.6); }
        private void movePageByRatio070() { movePageByRatio(0.7); }
        private void movePageByRatio080() { movePageByRatio(0.8); }
        private void movePageByRatio090() { movePageByRatio(0.9); }
        private void movePageByRatio100() { movePageByRatio(1.0); }

        private void movePageByRatio(double r)
        {
            var nextPage = getPageByRatio(r);
            if (nextPage < 0) return;
            currentPage = nextPage;
            if (nextPage == 0)
            {
                // seemToBook は廃止
                onePageModeForNext = CoverBindingMode == CoverBindingMode.ForceSingle;
            }
            showCurrentPage();
            mtbPage.Value = currentPage;
        }

        private bool isMovablePageByRatio(double r)
        {
            var nextPage = getPageByRatio(r);
            return nextPage >= 0 && nextPage != currentPage;
        }


        private void ViewerForm_Activated(object sender, EventArgs e)
        {
            //tmMemoryReducer.Enabled = false;
            SetMemoryUBound();
            ReduceUsingMemory(usedMemoryUBound);
            bmwLoadEachPage.ThreadCount = LoadThreadCount;
        }
        

        private void mtbPage_MouseEnter(object sender, EventArgs e)
        {
            //mtbPage.Focus();
            focusIfRightness(mtbPage);
        }

        private void statusStrip_MouseEnter(object sender, EventArgs e)
        {
            focusIfRightness(mtbPage); // 自身ではなく mtbPage へフォーカス
        }

        private void focusIfRightness(Control control)
        {
            //if (!currentImageLongPathToolStripTextBox.Focused) control.Focus();
        }
        
        private void fixDropDownLocation(object toolStripMenuItem)
        {
            if (!FullScreen) return;

            var tsmi = toolStripMenuItem as ToolStripMenuItem;
            if (tsmi == null) return;

            var bounds = tsmi.Bounds;
            var point = tsmi.Owner.PointToScreen(new Point(bounds.Left, bounds.Bottom - 1)); // Windows10(2016/09/01) 画面の解像度 100%, 200% 共 -1 で良いと確認

            var dropDown = tsmi.DropDown;

            var screenSize = Screen.FromControl(this).Bounds.Size;

            if (point.X + dropDown.Width <= screenSize.Width && point.Y + dropDown.Height <= screenSize.Height)
            {
                NativeMethods.MoveWindow(dropDown.Handle, point.X, point.Y, dropDown.Width, dropDown.Height, true);
            }
        }
        
        private void mouseGestureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Program.OpenMouseGestureSetting(Icon, mgView, getMouseGestureSettingTemplate(CommandSender.MouseGesture), this, widthZoom: 1.4);
        }

        private void keyboardShortcutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(Program.OpenKeyboardShortcutSetting(Icon, ksViewForm, getMouseGestureSettingTemplate(CommandSender.KeyboardShortcut), ContinuousExecutionCommands.Select(enm => (int)enm).ToArray(),
                ViewerFormConfig.DefaultKeyboardShortcutCommands.Select(d => Tuple.Create(d.GetShortcut().Select(s => new HashSet<Keys>(s)).ToArray(), (int)d.Command)).ToArray(),
                useLButton: true, useMButton: true, useRButton: true, useX1Button: true, useX2Button:true,
                useLButtonWarningMessage: Message.WhenClickOnAndClickConflictTheFormerTakesPrecedence,
                useMButtonWarningMessage: null, useRButtonWarningMessage: null, owner: this))
            {
                setShortcutKeyDisplayStrings(setKeyNames: false);
            }
        }
        
    }


}
