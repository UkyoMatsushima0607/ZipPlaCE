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
        
        
        private int mtbPage_MouseMove_LastValue = int.MinValue;
        private static readonly int Control_PreviewKeyDown_Speed = Program.DpiScaling(20);
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
        
    }


}
