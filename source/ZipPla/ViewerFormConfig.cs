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
    public class ViewerFormConfig : Configuration
    {
        public bool FullScreen;
        public bool Maximized;
        public Point? Point;
        public Size Size;
        public int MaximizedSizeWidthMargin;
        public int MaximizedSizeHeightMargin;
        public int MaximizedSizeHeightMarginInHideMode;

        public ArchivesInArchiveMode ArchivesInArchiveMode = ArchivesInArchiveMode.Ignore;

        public CoverBindingMode CoverBindingMode = CoverBindingMode.Default;

        public bool AlwaysHideUI = false;
        public SerializableRGB BackColor = Color.Black;

        public ScalingAlgorithm NormalScalingAlgorithm = ScalingAlgorithm.Default;
        public double ResizeGammaNormal = 1.0;
        public bool UseAreaAverageWhenNormalUpsizing = false;
        public ScalingAlgorithm MagnifierScalingAlgorithm = ScalingAlgorithm.HighSpeed;
        public double ResizeGammaMagnifier = 1.0;
        public bool UseAreaAverageWhenMagnifierUpsizing = false;

        public int MinPageCountInWindow = 1;
        public int MaxDivision = 1;

        public SeekBarMode SeekBarMode = SeekBarMode.LeftToRight;

        public int SlideshowInterval = 5000;
        public bool SlideshowRepeat = true;
        public bool SlideshowGlobal = false;

        public bool MouseGestureEnabled = false;
        public double MouseGestureLineWidth = 5;
        public SerializableRGB MouseGestureLineColor = Color.Red;
        public ViewerForm.MouseGestureCommand[] MouseGestureCommands = null;

        public ReadOnMemoryMode ReadOnMemoryMode = ReadOnMemoryMode.None;

        public ViewerFormImageFilter ImageFilter = null;

        public ViewSettingHintMode ViewSettingHintMode = ViewSettingHintMode.ShowWhenChangingSettings;

        private ViewerForm.KeyboardShortcutCommand[] keyboardShortcutCommands;
        public ViewerForm.KeyboardShortcutCommand[] KeyboardShortcutCommands
        {
            get
            {
                return GetSupplementCommands(keyboardShortcutCommands);
            }
            set
            {
                keyboardShortcutCommands = value;
            }
        }

        public static readonly ViewerForm.KeyboardShortcutCommand[] DefaultKeyboardShortcutCommands = new ViewerForm.KeyboardShortcutCommand[]
        {
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.OpenFile, Keys.ControlKey, Keys.O),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.OpenThumbnailWindow, Keys.ControlKey, Keys.T),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.SwitchToThumbnailWindow, Keys.ControlKey, Keys.ShiftKey, Keys.T),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.FullScreen, Keys.ControlKey, Keys.ShiftKey, Keys.F),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.Window, Keys.Escape),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleFullScreenMode, Keys.F11),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.Exit, Keys.ControlKey, Keys.W),
            
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleMagnifier, Keys.MButton),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleMagnifier, Keys.Z),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleRotateLeft, Keys.ControlKey, Keys.L),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleRotateRight, Keys.ControlKey, Keys.R),
            //new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleToneCurve, Keys.M),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleToneCurve, Keys.C),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.LeftToRight, Keys.L),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.RightToLeft, Keys.R),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.SinglePage, Keys.S),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.SinglePageWithoutScalingUp, Keys.D),

            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleForceFirstPageToBeSingle, Keys.F),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleMinPageCountInWindow, Keys.W),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleMaxDivision, Keys.V),

            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleAlwaysHideUI, Keys.H),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleSeekBarMode, Keys.B),

            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.SelectBackgroundColor, Keys.ControlKey, Keys.ShiftKey, Keys.C),

            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.NextPage, ExtendedKeys.WheelDown),
            //new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.NextPage, Keys.Right),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.PreviousPage, ExtendedKeys.WheelUp),
            //new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.PreviousPage, Keys.Left),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.RightPage, ExtendedKeys.LButtonOnRight),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.LeftPage, ExtendedKeys.LButtonOnLeft),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveForwardOnePage, Keys.Down),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveBackOnePage, ExtendedKeys.LButtonOnCenter),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveBackOnePage, Keys.Up),

            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.OpenNext, Keys.XButton2),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.OpenNext, Keys.Menu, Keys.Right),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.OpenPrevious, Keys.XButton1),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.OpenPrevious, Keys.Menu, Keys.Left),

            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo0Percent, Keys.Home),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo10Percent, Keys.D1),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo20Percent, Keys.D2),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo30Percent, Keys.D3),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo40Percent, Keys.D4),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo50Percent, Keys.D5),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo60Percent, Keys.D6),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo70Percent, Keys.D7),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo80Percent, Keys.D8),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo90Percent, Keys.D9),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveTo100Percent, Keys.End),
            
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ToggleSlideshow, Keys.Space),

            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveForwardOrMagnifierAutoScroll, Keys.Right),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.MoveBackOrMagnifierAutoScroll, Keys.Left),


            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ScreenEffectAtNextPageSide, ExtendedKeys.NextPageAtLastPage),
            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.ScreenEffectAtPreviousPageSide, ExtendedKeys.PreviousPageAtFirstPage),

            new ViewerForm.KeyboardShortcutCommand(ViewerForm.Command.EditMenu, Keys.RButton),

        };

        public static ViewerForm.KeyboardShortcutCommand[] GetSupplementCommands(ViewerForm.KeyboardShortcutCommand[] baseSetting)
        {
            bool dummy;
            return GetSupplementCommands(baseSetting, out dummy);
        }
        public static ViewerForm.KeyboardShortcutCommand[] GetSupplementCommands(ViewerForm.KeyboardShortcutCommand[] baseSetting, out bool changed)
        {
            if (baseSetting == null) baseSetting = new ViewerForm.KeyboardShortcutCommand[0];
            var sup = DefaultKeyboardShortcutCommands.Where(d =>
            {
                // useMouse(d) が呼び出される回数の期待値が 1 を下回る場合 var useMouseD = useMouse(d) を計算しておく方法はよくない
                //return baseSetting.All(r => !(r.Command == d.Command && UseMouse(r) == UseMouse(d)) && !r.ShortcutCompetes(d)); // 位置指定同士は別ボタン扱いで補完される

                //return  baseSetting.All(r => !(r.Command == d.Command && UseMouse(r) == UseMouse(d)) && !r.ShortcutIntersect(d));

                return baseSetting.All(r => !(r.Command == d.Command && UseMouse(r) == UseMouse(d)) && !r.ShortcutWeaklyCompetes(d));
            });
            changed = sup.Any();
            if (changed)
            {
                return baseSetting.Concat(sup).ToArray();
            }
            else
            {
                return baseSetting;
            }
        }
        private static bool UseMouse(ViewerForm.KeyboardShortcutCommand command)
        {
            return UseMouse(command.GetShortcut());
        }
        public static bool UseMouse(IEnumerable<IEnumerable<Keys>> shortcut)
        {
            return shortcut.Any(keys => keys.Any(key => key == Keys.XButton1 || key == Keys.XButton2 || ExtendedKeys.IsLButton(key) ||
            key == Keys.RButton || key == Keys.MButton || key == ExtendedKeys.WheelUp || key == ExtendedKeys.WheelDown));
        }
    }
}
