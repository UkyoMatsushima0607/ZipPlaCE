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
        private enum CommandSender { MouseGesture = 1, KeyboardShortcut = 2 };

        public enum Command { NextPage, PreviousPage, MoveForwardOnePage, MoveBackOnePage, RightPage, LeftPage,
            MoveTo0Percent, MoveTo10Percent, MoveTo20Percent, MoveTo30Percent, MoveTo40Percent, MoveTo50Percent, MoveTo60Percent, MoveTo70Percent, MoveTo80Percent, MoveTo90Percent, MoveTo100Percent,
            MoveRightOnePage, MoveLeftOnePage, OpenNext, OpenPrevious, OpenRight, OpenLeft,
            EnableMagnifier, EnableMagnifierWithoutOperatingGuide, DisableMagnifier, DisableMagnifierWithoutOperatingGuide, DisableMagnifierWithoutScreenEffects, ToggleMagnifier, ToggleMagnifierWithoutOperatingGuide, ToggleMagnifierWithMinimalScreenEffects,
            MagnifierAutoScrollForward, MagnifierAutoScrollBack, MagnifierAutoScrollForwardWithoutOverwrap, MagnifierAutoScrollBackWithoutOverwrap,
            MoveForwardOrMagnifierAutoScroll, MoveBackOrMagnifierAutoScroll, MoveForwardOrMagnifierAutoScrollWithoutOverwrap, MoveBackOrMagnifierAutoScrollWithoutOverwrap,
            MagnifierAutoScrollRight, MagnifierAutoScrollLeft, MagnifierAutoScrollRightWithoutOverwrap, MagnifierAutoScrollLeftWithoutOverwrap,
            MoveRightOrMagnifierAutoScroll, MoveLeftOrMagnifierAutoScroll, MoveRightOrMagnifierAutoScrollWithoutOverwrap, MoveLeftOrMagnifierAutoScrollWithoutOverwrap,
            LeftToRight, RightToLeft, SinglePage, SinglePageWithoutScalingUp, TogglePageSequence,
            ToggleForceFirstPageToBeSingle, ToggleMinPageCountInWindow, ToggleMaxDivision,
            ToggleAlwaysHideUI, ToggleSeekBarMode, SelectBackgroundColor,
            ToggleRotateLeft, ToggleRotateRight, CancelRotationSetting, /*ToggleMoireRemover,*/ ToggleToneCurve,
            StartSlideshow, StopSlideshow, ToggleSlideshow,
            OpenFile, OpenThumbnailWindow, SwitchToThumbnailWindow, CloneWindow,
            ReadOnMemoryModeNone, ReadOnMemoryModeExceptLookAhead, ReadOnMemoryModeAlways,
            ArchivesInArchiveModeIgnore, ArchivesInArchiveModeUntilFound2Level, ArchivesInArchiveModeAlways1Level,
            FullScreen, Window, ToggleFullScreenMode, Exit,
            ScreenEffectAtRightSide, ScreenEffectAtLeftSide, ScreenEffectAtNextPageSide, ScreenEffectAtPreviousPageSide,
            StartMenu, MoveMenu, ViewMenu, SlideshowMenu, VirtualFolderMenu, EditMenu, OthersMenu,
            NOOP
        }

        private static readonly Command[] ContinuousExecutionCommands = new Command[] { Command.NextPage, Command.PreviousPage, Command.MoveForwardOnePage,
            Command.MoveBackOnePage, Command.RightPage,Command.LeftPage, Command.MoveRightOnePage,  Command.MoveLeftOnePage,
            Command.MagnifierAutoScrollForward, Command.MagnifierAutoScrollBack, Command.MagnifierAutoScrollForwardWithoutOverwrap, Command.MagnifierAutoScrollBackWithoutOverwrap,
            Command.MoveForwardOrMagnifierAutoScroll, Command.MoveBackOrMagnifierAutoScroll, Command.MoveForwardOrMagnifierAutoScrollWithoutOverwrap, Command.MoveBackOrMagnifierAutoScrollWithoutOverwrap,
            Command.MagnifierAutoScrollRight, Command.MagnifierAutoScrollLeft, Command.MagnifierAutoScrollRightWithoutOverwrap, Command.MagnifierAutoScrollLeftWithoutOverwrap,
            Command.MoveRightOrMagnifierAutoScroll, Command.MoveLeftOrMagnifierAutoScroll, Command.MoveRightOrMagnifierAutoScrollWithoutOverwrap, Command.MoveLeftOrMagnifierAutoScrollWithoutOverwrap
        };

        public class MouseGestureCommand { public MouseGestureDirection[] Gesture; public Command Command; }

        public class KeyboardShortcutCommand
        {
            public Keys[][] Shortcut;
            public Command Command;
            public KeyboardShortcutCommand() { } // シリアライザー用
            public KeyboardShortcutCommand(Command command, params System.Windows.Forms.Keys[] shortcut)
            {
                Shortcut = (from x in KeyboardShortcutAction.SimpleShortcutToFullShortcut(shortcut) select x.Select(k => (Keys)k).ToArray()).ToArray();
                Command = command;
            }
            public KeyboardShortcutCommand(KeyboardShortcutAction action)
            {
                Shortcut = (from x in action.Shortcut select x.Select(k => (Keys)k).ToArray()).ToArray();
                Command = (Command)action.Key;
            }
            public System.Windows.Forms.Keys[][] GetShortcut()
            {
                if (Shortcut == null) return null;
                return Shortcut.Select(ks => ks.Select(k => (System.Windows.Forms.Keys)k).ToArray()).ToArray();
            }
            
            // KeyboardShortcutSetting に似た役割の equalsCommands があるが、あちらは Hashset<Keys>[] を比較する
            private static bool ShortcutEquals(Keys[][] a, Keys[][] b)
            {
                if (a == null && b == null) return true;
                if (a == null || b == null) return false;
                var length = a.Length;
                if (length != b.Length) return false;
                for(var i = 0; i < length; i++)
                {
                    var ai = a[i];
                    var bi = b[i];
                    if (ai == null && bi == null) continue;
                    if (ai == null || bi == null) return false;
                    if (!((new HashSet<Keys>(ai)).SetEquals(bi))) return false;
                }
                return true;
            }
            public static bool ShortcutEquals(Keys[][] a, Keys b)
            {
                if (a == null || a.Length != 1) return false;
                var a0 = a[0];
                return a0 != null && a0.Length == 1 && a0[0] == b;
            }

            public static bool ShortcutCompetes(Keys[][] a, Keys[][] b)
            {
                if (ShortcutEquals(a, b)) return true;
                
                if (ShortcutEquals(a, Keys.LButton))
                {
                    a = b;
                }
                else if (!ShortcutEquals(b, Keys.LButton)) return false;
                if (a == null || a.Length != 1) return false;
                var a0 = a[0];
                return a0 != null && a0.Length == 1 && ExtendedKeys.IsLButton((System.Windows.Forms.Keys)a0[0]);
            }
            

            public bool ShortcutCompetes(KeyboardShortcutCommand other)
            {
                return ShortcutCompetes(Shortcut, other.Shortcut);
            }

            public bool ShortcutEquals(KeyboardShortcutCommand other)
            {
                return ShortcutEquals(Shortcut, other.Shortcut);
            }

            private static HashSet<System.Windows.Forms.Keys>[] Convert(Keys[][] x)
            {
                return x?.Select(xi => xi == null ? new HashSet<System.Windows.Forms.Keys>() : new HashSet<System.Windows.Forms.Keys>(xi.Select(xk => (System.Windows.Forms.Keys)xk))).ToArray();
            }

            public bool ShortcutWeaklyCompetes(KeyboardShortcutCommand other)
            {
                return KeyboardShortcutSettingForm.WeaklyCompetesShortcut(Convert(Shortcut), Convert(other.Shortcut));
            }


            public enum Keys
            {
                None = 0,
                LButton = 1,
                RButton = 2,
                Cancel = 3,
                MButton = 4,
                XButton1 = 5,
                XButton2 = 6,
                Back = 8,
                Tab = 9,
                LineFeed = 10,
                Clear = 12,
                Enter = 13,
                Return = 13,
                ShiftKey = 16,
                ControlKey = 17,
                Menu = 18,
                Pause = 19,
                CapsLock = 20,
                Capital = 20,
                HangulMode = 21,
                HanguelMode = 21,
                KanaMode = 21,
                JunjaMode = 23,
                FinalMode = 24,
                KanjiMode = 25,
                HanjaMode = 25,
                Escape = 27,
                IMEConvert = 28,
                IMENonconvert = 29,
                IMEAccept = 30,
                IMEAceept = 30,
                IMEModeChange = 31,
                Space = 32,
                Prior = 33,
                PageUp = 33,
                PageDown = 34,
                Next = 34,
                End = 35,
                Home = 36,
                Left = 37,
                Up = 38,
                Right = 39,
                Down = 40,
                Select = 41,
                Print = 42,
                Execute = 43,
                Snapshot = 44,
                PrintScreen = 44,
                Insert = 45,
                Delete = 46,
                Help = 47,
                D0 = 48,
                D1 = 49,
                D2 = 50,
                D3 = 51,
                D4 = 52,
                D5 = 53,
                D6 = 54,
                D7 = 55,
                D8 = 56,
                D9 = 57,
                A = 65,
                B = 66,
                C = 67,
                D = 68,
                E = 69,
                F = 70,
                G = 71,
                H = 72,
                I = 73,
                J = 74,
                K = 75,
                L = 76,
                M = 77,
                N = 78,
                O = 79,
                P = 80,
                Q = 81,
                R = 82,
                S = 83,
                T = 84,
                U = 85,
                V = 86,
                W = 87,
                X = 88,
                Y = 89,
                Z = 90,
                LWin = 91,
                RWin = 92,
                Apps = 93,
                Sleep = 95,
                NumPad0 = 96,
                NumPad1 = 97,
                NumPad2 = 98,
                NumPad3 = 99,
                NumPad4 = 100,
                NumPad5 = 101,
                NumPad6 = 102,
                NumPad7 = 103,
                NumPad8 = 104,
                NumPad9 = 105,
                Multiply = 106,
                Add = 107,
                Separator = 108,
                Subtract = 109,
                Decimal = 110,
                Divide = 111,
                F1 = 112,
                F2 = 113,
                F3 = 114,
                F4 = 115,
                F5 = 116,
                F6 = 117,
                F7 = 118,
                F8 = 119,
                F9 = 120,
                F10 = 121,
                F11 = 122,
                F12 = 123,
                F13 = 124,
                F14 = 125,
                F15 = 126,
                F16 = 127,
                F17 = 128,
                F18 = 129,
                F19 = 130,
                F20 = 131,
                F21 = 132,
                F22 = 133,
                F23 = 134,
                F24 = 135,
                NumLock = 144,
                Scroll = 145,
                LShiftKey = 160,
                RShiftKey = 161,
                LControlKey = 162,
                RControlKey = 163,
                LMenu = 164,
                RMenu = 165,
                BrowserBack = 166,
                BrowserForward = 167,
                BrowserRefresh = 168,
                BrowserStop = 169,
                BrowserSearch = 170,
                BrowserFavorites = 171,
                BrowserHome = 172,
                VolumeMute = 173,
                VolumeDown = 174,
                VolumeUp = 175,
                MediaNextTrack = 176,
                MediaPreviousTrack = 177,
                MediaStop = 178,
                MediaPlayPause = 179,
                LaunchMail = 180,
                SelectMedia = 181,
                LaunchApplication1 = 182,
                LaunchApplication2 = 183,
                OemSemicolon = 186,
                Oem1 = 186,
                Oemplus = 187,
                Oemcomma = 188,
                OemMinus = 189,
                OemPeriod = 190,
                Oem2 = 191,
                OemQuestion = 191,
                Oem3 = 192,
                Oemtilde = 192,
                Oem4 = 219,
                OemOpenBrackets = 219,
                OemPipe = 220,
                Oem5 = 220,
                OemCloseBrackets = 221,
                Oem6 = 221,
                OemQuotes = 222,
                Oem7 = 222,
                Oem8 = 223,
                Oem102 = 226,
                OemBackslash = 226,
                ProcessKey = 229,
                Packet = 231,
                Attn = 246,
                Crsel = 247,
                Exsel = 248,
                EraseEof = 249,
                Play = 250,
                Zoom = 251,
                NoName = 252,
                Pa1 = 253,
                OemClear = 254,
                KeyCode = 65535,
                Shift = 65536,
                Control = 131072,
                Alt = 262144,
                Modifiers = -65536,

                WheelUp = ExtendedKeys.WheelUp,
                WheelDown = ExtendedKeys.WheelDown,

                NextPageAtLastPage = ExtendedKeys.NextPageAtLastPage,
                PreviousPageAtFirstPage = ExtendedKeys.PreviousPageAtFirstPage,

                LButtonOnRight = ExtendedKeys.LButtonOnRight,
                LButtonOnTopRight = ExtendedKeys.LButtonOnTopRight,
                LButtonOnTop = ExtendedKeys.LButtonOnTop,
                LButtonOnTopLeft = ExtendedKeys.LButtonOnTopLeft,
                LButtonOnLeft = ExtendedKeys.LButtonOnLeft,
                LButtonOnBottomLeft = ExtendedKeys.LButtonOnBottomLeft,
                LButtonOnBottom = ExtendedKeys.LButtonOnBottom,
                LButtonOnBottomRight = ExtendedKeys.LButtonOnBottomRight,
                LButtonOnCenter = ExtendedKeys.LButtonOnCenter,

                LButtonDouble = ExtendedKeys.LButtonDouble,
            }
        }

        private MouseGestureSettingTemplate[] getMouseGestureSettingTemplate(CommandSender commandSender)
        {
            var result = new Tuple<MouseGestureSettingTemplate, CommandSender>[]
            {
                //Tuple.Create(new MouseGestureSettingTemplate( (int)Command.DefaultClickBehavior, Message.DefaultClickBehavior,
                //   withHideCursor(defaultClickBehavior, commandSender)), CommandSender.SingleLeftClick),
                
                Tuple.Create(new MouseGestureSettingTemplate( (int)Command.NextPage, Message.NextPage,
                   withHideCursor(moveToNextPage, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(new MouseGestureSettingTemplate( (int)Command.PreviousPage, Message.PreviousPage,
                   withHideCursor(MoveToPreviousPage, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(new MouseGestureSettingTemplate( (int)Command.MoveForwardOnePage, Message.MoveForwardOnePage,
                   withHideCursor(movePageToMinimalForward, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveBackOnePage, Message.MoveBackOnePage,
                   withHideCursor(movePageToMinimalBack, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.RightPage, Message.RightPage,
                   withHideCursor(moveToRightPage, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.LeftPage, Message.LeftPage,
                   withHideCursor(moveToLeftPage, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(   new MouseGestureSettingTemplate( (int)Command.MoveRightOnePage, Message.MoveRightOnePage,
                   withHideCursor(movePageToMinimalRight, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MoveLeftOnePage, Message.MoveLeftOnePage,
                   withHideCursor(movePageToMinimalLeft, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut), // コマンド実行時点での PageLeftToRight が必要なのでデリゲート自体を PageLeftToRight で選ぶのは誤り

                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo0Percent, Message.PositionRatio + " > 0 %",
                   withHideCursor(movePageByRatio000, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo10Percent, Message.PositionRatio + " > 10 %",
                   withHideCursor(movePageByRatio010, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo20Percent, Message.PositionRatio + " > 20 %",
                   withHideCursor(movePageByRatio020, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo30Percent, Message.PositionRatio + " > 30 %",
                   withHideCursor(movePageByRatio030, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo40Percent, Message.PositionRatio + " > 40 %",
                   withHideCursor(movePageByRatio040, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo50Percent, Message.PositionRatio + " > 50 %",
                   withHideCursor(movePageByRatio050, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo60Percent, Message.PositionRatio + " > 60 %",
                   withHideCursor(movePageByRatio060, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo70Percent, Message.PositionRatio + " > 70 %",
                   withHideCursor(movePageByRatio070, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo80Percent, Message.PositionRatio + " > 80 %",
                   withHideCursor(movePageByRatio080, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo90Percent, Message.PositionRatio + " > 90 %",
                   withHideCursor(movePageByRatio090, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.MoveTo100Percent, Message.PositionRatio + " > 100 %",
                   withHideCursor(movePageByRatio100, commandSender)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),


                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.OpenNext, Message.OpenNext,
                   toAction(OpenNext)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create( new MouseGestureSettingTemplate( (int)Command.OpenPrevious, Message.OpenPrevious,
                   toAction(OpenPrevious)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.OpenRight, Message.OpenRight,
                   toAction(OpenRight)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.OpenLeft, Message.OpenLeft,
                   toAction(OpenLeft)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.EnableMagnifier, Message.EnableMagnifier,
                   enableMagnifier), CommandSender.MouseGesture),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.DisableMagnifier, Message.DisableMagnifier,
                   disableMagnifier), CommandSender.MouseGesture),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleMagnifier, Message.ToggleMagnifier,
                   ToggleMagnifierWithoutScreenEffects), CommandSender.MouseGesture),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.EnableMagnifier, Message.EnableMagnifier,
                   enableMagnifierForKeyboardWithOperatingGuide), CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.EnableMagnifierWithoutOperatingGuide, Message.EnableMagnifierWithoutOperatingGuide,
                   enableMagnifierForKeyboard), CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.DisableMagnifier, Message.DisableMagnifier,
                   disableMagnifierForKeyboardWithOperatingGuide), CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.DisableMagnifierWithoutOperatingGuide, Message.DisableMagnifierWithoutOperatingGuide,
                   disableMagnifierForKeyboard), CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.DisableMagnifierWithoutScreenEffects, Message.DisableMagnifierWithoutScreenEffects,
                   disableMagnifier), CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleMagnifier, Message.ToggleMagnifier,
                   toggleMagnifierForKeyboardWithOperatingGuide), CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleMagnifierWithoutOperatingGuide, Message.ToggleMagnifierWithoutOperatingGuide,
                   toggleMagnifierForKeyboard), CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleMagnifierWithMinimalScreenEffects, Message.ToggleMagnifierWithMinimalScreenEffects,
                   toggleMagnifierForKeyboardWithMinimalScreenEffects), CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MagnifierAutoScrollForward, Message.AutoForwardScroll,
                   MagnifierAutoScrollForwardWith20PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MagnifierAutoScrollBack, Message.AutoBackScroll,
                   MagnifierAutoScrollBackWith20PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MagnifierAutoScrollForwardWithoutOverwrap, Message.AutoForwardScrollWithoutOverwrap,
                   MagnifierAutoScrollForwardWith0PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MagnifierAutoScrollBackWithoutOverwrap, Message.AutoBackScrollWithoutOverwrap,
                   MagnifierAutoScrollBackWith0PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MoveForwardOrMagnifierAutoScroll, Message.NextPageOrAutoForwardScroll,
                   MoveForwardOrMagnifierAutoScrollWith20PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MoveBackOrMagnifierAutoScroll, Message.PreviousPageOrAutoBackScroll,
                   MoveBackOrMagnifierAutoScrollWith20PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MoveForwardOrMagnifierAutoScrollWithoutOverwrap, Message.NextPageOrAutoForwardScrollWithoutOverwrap,
                   MoveForwardOrMagnifierAutoScrollWith0PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MoveBackOrMagnifierAutoScrollWithoutOverwrap, Message.PreviousPageOrAutoBackScrollWithoutOverwrap,
                   MoveBackOrMagnifierAutoScrollWith0PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MagnifierAutoScrollRight, Message.AutoRightScroll,
                   MagnifierAutoScrollRightWith20PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MagnifierAutoScrollLeft, Message.AutoLeftScroll,
                   MagnifierAutoScrollLeftWith20PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MagnifierAutoScrollRightWithoutOverwrap, Message.AutoRightScrollWithoutOverwrap,
                   MagnifierAutoScrollRightWith0PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MagnifierAutoScrollLeftWithoutOverwrap, Message.AutoLeftScrollWithoutOverwrap,
                   MagnifierAutoScrollLeftWith0PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MoveRightOrMagnifierAutoScroll, Message.RightPageOrAutoRightScroll,
                   MoveRightOrMagnifierAutoScrollWith20PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MoveLeftOrMagnifierAutoScroll, Message.LeftPageOrAutoLeftScroll,
                   MoveLeftOrMagnifierAutoScrollWith20PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MoveRightOrMagnifierAutoScrollWithoutOverwrap, Message.RightPageOrAutoRightScrollWithoutOverwrap,
                   MoveRightOrMagnifierAutoScrollWith0PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.MoveLeftOrMagnifierAutoScrollWithoutOverwrap, Message.LeftPageOrAutoLeftScrollWithoutOverwrap,
                   MoveLeftOrMagnifierAutoScrollWith0PercentOverwrap), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.LeftToRight, Message.LeftToRight,
                   () => ChangePageDirection(BindingMode.LeftToRight)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.RightToLeft, Message.RightToLeft,
                   () => ChangePageDirection(BindingMode.RightToLeft)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.SinglePage, Message.SinglePage,
                   () => ChangePageDirection(BindingMode.SinglePage)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.SinglePageWithoutScalingUp, Message.SinglePageWithoutScalingUp,
                   () => ChangePageDirection(BindingMode.SinglePageWithoutScalingUp)), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.TogglePageSequence, Message.TogglePageSequence,
                   togglePageSequence), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleForceFirstPageToBeSingle, Message.ForceFirstPageToBeSingle,
                   ToggleForceFirstPageToBeSingle), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleMinPageCountInWindow, Message.PrioritizePageSpread, //Message.ForcePageSpread,
                   ToggleMinPageCountInWindow), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleMaxDivision, Message.AllowPageDivision,
                   ToggleMaxDivision), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleAlwaysHideUI, Message.AlwaysAutomaticallyHideUI,
                   toggleAlwaysHideUI), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleSeekBarMode, Message.OrientSeekBarDirectionToPageSequence,
                   ToggleSeekBarMode), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.SelectBackgroundColor, Message.SelectBackgroundColor,
                   SelectBackgroundColor), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleRotateLeft, Message.RotateLeft,
                   toggleRotateLeft), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleRotateRight, Message.RotateRight,
                   toggleRotateRight), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.CancelRotationSetting, Message.CancelRotationSetting,
                   cancelRotationSetting), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                //Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleMoireRemover, Message.RemoveMoire,
                //   toggleMoireRemover), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleToneCurve, Message.ToneCurve,
                   togglePseudoColoring), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.StartSlideshow, Message.StartSlideshow,
                   slideshowStart), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.StopSlideshow, Message.StopSlideshow,
                   slideshowStop), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ToggleSlideshow, Message.ToggleSlideshow,
                   slideshowToggle), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.OpenFile, Message.OpenFile,
                   openFileWithDialog), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.OpenThumbnailWindow, Message.OpenThumbnailWindow,
                   openThumbnailWindow), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.SwitchToThumbnailWindow, Message.SwitchToThumbnailWindow,
                   switchToThumbnailWindow), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.CloneWindow, Message.CloneWindow,
                   cloneWindow), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ReadOnMemoryModeNone, Message.LoadWholeArchiveIntoMemory + " > " + Message.NoneRecommended,
                   SetReadOnMemoryModeNone), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ReadOnMemoryModeExceptLookAhead, Message.LoadWholeArchiveIntoMemory + " > " + Message.ExceptReadAheadProcess,
                   SetReadOnMemoryModeExceptLookAhead), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ReadOnMemoryModeAlways, Message.LoadWholeArchiveIntoMemory + " > " + Message.Always,
                   SetReadOnMemoryModeAlways), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ArchivesInArchiveModeIgnore, Message.LoadArchivesInArchive + " > " + Message.NoneRecommended,
                   SetArchivesInArchiveModeIgnore), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ArchivesInArchiveModeUntilFound2Level, Message.LoadArchivesInArchive + " > " + Message.TwoLevelUntilFound,
                   SetArchivesInArchiveModeUntilFound2Level), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.ArchivesInArchiveModeAlways1Level, Message.LoadArchivesInArchive + " > " + Message.OneLevelCompletelyNotRecommended,
                   SetArchivesInArchiveModeAlways1Level), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.FullScreen, Message.Fullscreen,
                   fullScreenModeChangeToFullScreen), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.Window, Message.Window,
                   fullScreenModeChangeToWindow), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(   new MouseGestureSettingTemplate( (int)Command.ToggleFullScreenMode, Message.ToggleFullscreenMode,
                   toggleFullScreenMode), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(   new MouseGestureSettingTemplate( (int)Command.Exit, Message.Exit,
                   Close), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(new MouseGestureSettingTemplate( (int)Command.ScreenEffectAtRightSide, Message.ScreenEffectAtRightSide,
                   drawRightSideLine), CommandSender.KeyboardShortcut),
                Tuple.Create(new MouseGestureSettingTemplate( (int)Command.ScreenEffectAtLeftSide, Message.ScreenEffectAtLeftSide,
                   drawLeftSideLine), CommandSender.KeyboardShortcut),
                Tuple.Create(new MouseGestureSettingTemplate( (int)Command.ScreenEffectAtNextPageSide, Message.ScreenEffectAtNextPageSide,
                   drawNextPageSideLine), CommandSender.KeyboardShortcut),
                Tuple.Create(new MouseGestureSettingTemplate( (int)Command.ScreenEffectAtPreviousPageSide, Message.ScreenEffectAtPreviousPageSide,
                   drawPrevPageSideLine), CommandSender.KeyboardShortcut),
                
                Tuple.Create(new MouseGestureSettingTemplate( (int)Command.StartMenu, Message.StartMenu,
                   openStartMenu), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(new MouseGestureSettingTemplate( (int)Command.MoveMenu, Message.MoveMenu,
                   openMoveMenu), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(new MouseGestureSettingTemplate( (int)Command.ViewMenu, Message.ViewMenu,
                   openViewMenu), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.SlideshowMenu, Message.SlideshowMenu,
                   openSlideshowMenu), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.VirtualFolderMenu, Message.VirtualFolderMenu,
                   openVirtualFolderMenu), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.EditMenu, Message.EditMenu,
                   openEditMenu), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),
                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.OthersMenu, Message.OthersMenu,
                   openOthersMenu), CommandSender.MouseGesture | CommandSender.KeyboardShortcut),

                Tuple.Create(  new MouseGestureSettingTemplate( (int)Command.NOOP, Message.NoOperation,
                   NOOP), CommandSender.KeyboardShortcut),

            };

            return (from t in result where (commandSender & t.Item2) != 0 select t.Item1).ToArray();
        }

        private void NOOP() { }

        private Action toAction(Func<OpenResult> func) { return () => func(); }

        private Action toAction(Func<Action, OpenResult> func) { return () => func(null); }

        private Action withHideCursor(Action baseAction, CommandSender sender)
        {
            if ((sender & CommandSender.KeyboardShortcut) != 0)
            {
                return () =>
                {
                    baseAction();
                    if (ksViewForm.PureKeyboardShortcutExcuting())
                    {
                        var p = Cursor.Position;
                        if (!pbView.ClientRectangle.Contains(pbView.PointToClient(p))) return;
                        if (FullScreen)
                        {
                            var b = menuStrip.RectangleToScreen(menuStrip.ClientRectangle);
                            if (p.Y < b.Bottom) return;
                            var t = pnlSeekbar.RectangleToScreen(pnlSeekbar.ClientRectangle);
                            if (p.Y >= t.Top) return;
                        }
                        if (MouseInPbView)
                        {
                            Program.HideCursor();
                        }
                    }
                };
            }
            else return baseAction;
        }

        void drawLeftSideLine() { drawSideLine(left: true); }

        void drawRightSideLine() { drawSideLine(left: false); }

        void drawNextPageSideLine() { drawSideLine(left: BindingMode == BindingMode.RightToLeft); }

        void drawPrevPageSideLine() { drawSideLine(left: BindingMode != BindingMode.RightToLeft); }

        void drawSideLine(bool left) { drawSideLine(left, Program.DpiScaling(5), Color.Red, TimeSpan.FromSeconds(0.5)); }

        DateTime drowSideLine_DrawStopTime = DateTime.MinValue;

        bool drowSideLine_DrawLeft;

        int drowSideLine_DrawWidth;

        Color drowSideLine_DrawColor;

        bool drowSideLine_Draw;

        async void drawSideLine(bool left, int width, Color color, TimeSpan time)
        {
            drowSideLine_DrawLeft = left;
            drowSideLine_DrawColor = color;
            drowSideLine_Draw = true;
            drowSideLine_DrawWidth = width;
            var now = DateTime.Now;
            var stopTime = now + time;
            drowSideLine_DrawStopTime = stopTime;
            pbView.Invalidate();
            await Task.Delay(time);
            if (drowSideLine_DrawStopTime <= stopTime)
            {
                drowSideLine_Draw = false;
                pbView.Invalidate();
            }
        }

        private void setShortcutKeyDisplayStrings(bool setKeyNames)
        {
            var kss = ksViewForm.Actions;
            if (kss == null) kss = new KeyboardShortcutAction[0];

            if (setKeyNames)
            {
                KeyboardShortcutAction.KeyNames = new Dictionary<Keys, string>
                {
                    { Keys.Escape, "Esc" },
                    { Keys.ControlKey, "Ctrl" },
                    { Keys.ShiftKey, "Shift" },
                    { Keys.Menu, "Alt" }, // Alt も別にある
                    { Keys.Delete, "Del" },
                    { Keys.Enter, "Enter" },// 別名の Return 回避
                    { Keys.Space, Message.SpaceKey },
                    { Keys.PageDown, "PageDown" }, // 別名の Next 回避
                    { Keys.Up, "↑" },
                    { Keys.Down, "↓" },
                    { Keys.Left, "←" },
                    { Keys.Right, "→" },
                    { Keys.OemMinus, "-" },
                    { Keys.Oem7, "^" },
                    { Keys.Oem5, @"\" },
                    { Keys.Oemtilde, "@" },
                    { Keys.OemOpenBrackets, "[" },
                    { Keys.Oemplus, ";" },
                    { Keys.Oem1, ":" },
                    { Keys.Oem6, "]" },
                    { Keys.Oemcomma, "," },
                    { Keys.OemPeriod, "." },
                    { Keys.OemQuestion, "/" },
                    { Keys.OemBackslash, @"\" },
                    { Keys.NumPad1, "N1" },
                    { Keys.NumPad2, "N2" },
                    { Keys.NumPad3, "N3" },
                    { Keys.NumPad4, "N4" },
                    { Keys.NumPad5, "N5" },
                    { Keys.NumPad6, "N6" },
                    { Keys.NumPad7, "N7" },
                    { Keys.NumPad8, "N8" },
                    { Keys.NumPad9, "N9" },
                    { Keys.NumPad0, "N0" },
                    { Keys.D1, "1" },
                    { Keys.D2, "2" },
                    { Keys.D3, "3" },
                    { Keys.D4, "4" },
                    { Keys.D5, "5" },
                    { Keys.D6, "6" },
                    { Keys.D7, "7" },
                    { Keys.D8, "8" },
                    { Keys.D9, "9" },
                    { Keys.D0, "0" },
                    { Keys.LButton, Message.LeftClick },
                    { Keys.MButton, Message.MiddleClick },
                    { Keys.RButton, Message.RightClick },
                    { Keys.XButton1, Message.X1Button },
                    { Keys.XButton2, Message.X2Button },
                    { ExtendedKeys.WheelUp, Message.WheelUp },
                    { ExtendedKeys.WheelDown, Message.WheelDown },

                    { ExtendedKeys.NextPageAtLastPage, Message.NextPageAtLastPage },
                    { ExtendedKeys.PreviousPageAtFirstPage, Message.PreviousPageAtFirstPage },

                    { ExtendedKeys.LButtonOnRight, Message.LeftClickOnRightSide },
                    { ExtendedKeys.LButtonOnTopRight, Message.LeftClickOnTopRight },
                    { ExtendedKeys.LButtonOnTop, Message.LeftClickOnTopSide },
                    { ExtendedKeys.LButtonOnTopLeft, Message.LeftClickOnTopLeft },
                    { ExtendedKeys.LButtonOnLeft, Message.LeftClickOnLeftSide },
                    { ExtendedKeys.LButtonOnBottomLeft, Message.LeftClickOnBottomLeft },
                    { ExtendedKeys.LButtonOnBottom, Message.LeftClickOnBottomSide },
                    { ExtendedKeys.LButtonOnBottomRight, Message.LeftClickOnBottomRight },
                    { ExtendedKeys.LButtonOnCenter, Message.LeftClickOnCenter },

                    { ExtendedKeys.LButtonDouble, Message.DoubleLeftClick },
                    /*
                    { Keys.Up, Message.CursorUp },
                    { Keys.Down, Message.CursorDown },
                    { Keys.Left, Message.CursorLeft },
                    { Keys.Right, Message.CursorRight },
                    */
                };

                foreach (var ks in kss) ks.SetNameFromShortcut();
            }

            openFileToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.OpenFile);
            openNewThumbnailWindowToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.OpenThumbnailWindow);
            switchToThumbnailWindowToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.SwitchToThumbnailWindow);
            cloneTheWindowToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.CloneWindow);
            cloneTheWindowToolStripMenuItem.Visible = !string.IsNullOrEmpty(cloneTheWindowToolStripMenuItem.ShortcutKeyDisplayString);

            readOnMemoryNoneToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ReadOnMemoryModeNone);
            readOnMemoryExceptLookAheadToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ReadOnMemoryModeExceptLookAhead);
            readOnMemoryAlwaysToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ReadOnMemoryModeAlways);

            nonerecommendedToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ArchivesInArchiveModeIgnore);
            untilFoundToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ArchivesInArchiveModeUntilFound2Level);
            alwaysLoadArchivesInArchiveToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ArchivesInArchiveModeAlways1Level);

            fullScreenInStartToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.FullScreen);
            windowToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.Window);
            toggleFullScreenModeToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleFullScreenMode);
            exitToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.Exit);

            nextPageToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(/*Message.WheelDown,*/ kss,
                Command.NextPage, Command.MoveForwardOrMagnifierAutoScroll, Command.MoveForwardOrMagnifierAutoScrollWithoutOverwrap);
            previousPageToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(/*Message.WheelUp,*/ kss,
                Command.PreviousPage, Command.MoveBackOrMagnifierAutoScroll, Command.MoveBackOrMagnifierAutoScrollWithoutOverwrap);
            rightPageToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss,
                Command.RightPage, Command.MoveRightOrMagnifierAutoScroll, Command.MoveRightOrMagnifierAutoScrollWithoutOverwrap);
            leftPageToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss,
                Command.LeftPage, Command.MoveLeftOrMagnifierAutoScroll, Command.MoveLeftOrMagnifierAutoScrollWithoutOverwrap);

            percentage000ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo0Percent);
            percentage010ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo10Percent);
            percentage020ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo20Percent);
            percentage030ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo30Percent);
            percentage040ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo40Percent);
            percentage050ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo50Percent);
            percentage060ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo60Percent);
            percentage070ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo70Percent);
            percentage080ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo80Percent);
            percentage090ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo90Percent);
            percentage100ToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveTo100Percent);

            moveForward1PageToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(Message.WheelDownOnBar, kss, Command.MoveForwardOnePage);
            //if (ksViewForm.ActionsContainsLButton)
            {
                moveBack1PageToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(Message.WheelUpOnBar, kss, Command.MoveBackOnePage);
            }
            moveRight1PageToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveRightOnePage);
            moveRight1PageToolStripMenuItem.Visible = !string.IsNullOrEmpty(moveRight1PageToolStripMenuItem.ShortcutKeyDisplayString);
            moveLeft1PageToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.MoveLeftOnePage);
            moveLeft1PageToolStripMenuItem.Visible = !string.IsNullOrEmpty(moveLeft1PageToolStripMenuItem.ShortcutKeyDisplayString);
            openNextToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(/*Message.MiddleClickOnPosteriorHalfOfBar, */kss, Command.OpenNext);
            openPreviousToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(/*Message.MiddleClickOnAnteriorHalfOfBar,*/ kss, Command.OpenPrevious);
            openRightToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.OpenRight);
            openRightToolStripMenuItem.Visible = !string.IsNullOrEmpty(openRightToolStripMenuItem.ShortcutKeyDisplayString);
            openLeftToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.OpenLeft);
            openLeftToolStripMenuItem.Visible = !string.IsNullOrEmpty(openLeftToolStripMenuItem.ShortcutKeyDisplayString);

            enableMagnifierToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.EnableMagnifier, Command.EnableMagnifierWithoutOperatingGuide);
            enableMagnifierToolStripMenuItem.Visible = !string.IsNullOrEmpty(enableMagnifierToolStripMenuItem.ShortcutKeyDisplayString);
            disableMagnifierToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.DisableMagnifier, Command.DisableMagnifierWithoutOperatingGuide, Command.DisableMagnifierWithoutScreenEffects);
            disableMagnifierToolStripMenuItem.Visible = !string.IsNullOrEmpty(disableMagnifierToolStripMenuItem.ShortcutKeyDisplayString);
            magnifierToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleMagnifier, Command.ToggleMagnifierWithoutOperatingGuide, Command.ToggleMagnifierWithMinimalScreenEffects);
            //magnifierToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(Message.MiddleClick, kss, Command.ToggleMagnifier);

            rightToLeftToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.RightToLeft);
            leftToRightToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.LeftToRight);
            singlePageToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.SinglePage);
            singlePageWithoutScalingUpToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.SinglePageWithoutScalingUp);
            togglePageSequenceToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.TogglePageSequence);
            togglePageSequenceToolStripMenuItem.Visible = !string.IsNullOrEmpty(togglePageSequenceToolStripMenuItem.ShortcutKeyDisplayString);

            forceFirstPageToBeSingleToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleForceFirstPageToBeSingle);

            forceTwoPageSpreadToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleMinPageCountInWindow);
            allowPageDivisionToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleMaxDivision);

            alwaysAutoHideUIToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleAlwaysHideUI);
            orientSeekBarDirectionToPageSequenceToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleSeekBarMode);
            selectBackgroundColorToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.SelectBackgroundColor);

            rotateLeftToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleRotateLeft);
            rotateRightToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleRotateRight);
            cancelRotationSettingToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.CancelRotationSetting);
            cancelRotationSettingToolStripMenuItem.Visible = !string.IsNullOrEmpty(cancelRotationSettingToolStripMenuItem.ShortcutKeyDisplayString);

            //removeMoireToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleMoireRemover);

            pseudoColoringToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleToneCurve);

            startSlideshowToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.StartSlideshow);
            stopSlideshowToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.StopSlideshow);
            toggleSlideshowToolStripMenuItem.ShortcutKeyDisplayString = getShortcutKeyDisplayStrings(kss, Command.ToggleSlideshow);
        }

        private static string getShortcutKeyDisplayStrings(KeyboardShortcutAction[] actions, Command command)
        {
            var intCommand = (int)command;
            return string.Join(", ", from ks in actions where ks.Key == intCommand select ks.Name);
        }

        private static string getShortcutKeyDisplayStrings(KeyboardShortcutAction[] actions, Command command1, Command command2)
        {
            var intCommand1 = (int)command1;
            var intCommand2 = (int)command2;
            return string.Join(", ", from ks in actions let key = ks.Key where key == intCommand1 || key == intCommand2 select ks.Name);
        }

        private static string getShortcutKeyDisplayStrings(KeyboardShortcutAction[] actions, Command command1, Command command2, Command command3)
        {
            var intCommand1 = (int)command1;
            var intCommand2 = (int)command2;
            var intCommand3 = (int)command3;
            return string.Join(", ", from ks in actions let key = ks.Key where key == intCommand1 || key == intCommand2 || key == intCommand3 select ks.Name);
        }

        private static string getShortcutKeyDisplayStrings(string last, KeyboardShortcutAction[] actions, Command command)
        {
            var intCommand = (int)command;
            //return stringJoin(", ", first, from ks in actions where ks.Key == intCommand select ks.Name);
            return stringJoin(", ", from ks in actions where ks.Key == intCommand select ks.Name, last);
        }

        private static string stringJoin(string separator, IEnumerable<string> others, params string[] lasts)
        {
            if (others == null || !others.Any()) return string.Join(separator, lasts);
            else return string.Join(separator, others) + separator + string.Join(separator, lasts);
        }

        private void openNextToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenNext();
        }

        private void openPreviousToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenPrevious();
        }

        private void openRightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenRight();
        }

        private void openLeftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenLeft();
        }

        enum OpenResult { Success, NoOperation, ShowWarning, CloseForm }

        private OpenResult OpenRight() { return OpenNextOrPrevious(next: BindingMode != BindingMode.RightToLeft, loop: false); }

        private OpenResult OpenLeft() { return OpenNextOrPrevious(next: BindingMode == BindingMode.RightToLeft, loop: false); }

        private OpenResult OpenNext(Action notFoundAction = null) { return OpenNextOrPrevious(next: true, loop: false, notFoundAction: notFoundAction); }

        private OpenResult OpenNextWithLoop(Action notFoundAction = null) { return OpenNextOrPrevious(next: true, loop: true, notFoundAction: notFoundAction); }

        private OpenResult OpenPrevious(Action notFoundAction = null) { return OpenNextOrPrevious(next: false, loop: false, notFoundAction: notFoundAction); }

        class OpenNextOrPrevious_Comparer : IComparer<FileSortInfo>
        {
            private static readonly LogicalStringComparer LogicalStringComparer = new LogicalStringComparer();
            private SortMode sortMode, preSortMode;
            private string randomSeed;
            public OpenNextOrPrevious_Comparer(SortModeDetails sortModeDetails) : base()
            {
                if (sortModeDetails == null)
                {
                    sortMode = SortMode.NameInAsc;
                }
                else
                {
                    sortMode = sortModeDetails.SortMode;
                    preSortMode = sortModeDetails.PreSortMode;
                    randomSeed = sortModeDetails.RandomSeed;
                }
            }
            public int Compare(FileSortInfo x, FileSortInfo y)
            {
                switch (sortMode)
                {
                    case SortMode.NameInAsc: return LogicalStringComparer.Compare(x.Name, y.Name);
                    case SortMode.NameInDesc: return -LogicalStringComparer.Compare(x.Name, y.Name);
                    case SortMode.Random: return x.GetRandomIndex(randomSeed).CompareTo(y.GetRandomIndex(randomSeed));
                    default:
                        //MessageBox.Show($"{x.Type}, {y.Type}");
                        var result = Compare(x, y, sortMode);
                        if (result != 0) return result;
                        result = Compare(x, y, preSortMode);
                        if (result != 0) return result;
                        //MessageBox.Show($"{sortMode} {preSortMode}");
                        //return result;
                        return LogicalStringComparer.Compare(x.Name, y.Name);
                }
               
            }
            private static int Compare(FileSortInfo x, FileSortInfo y, SortMode sortMode)
            {
                switch (sortMode)
                {
                    case SortMode.AccessedInAsc: return DateTime.Compare(x.LastAccessTime, y.LastAccessTime);
                    case SortMode.AccessedInDesc: return -DateTime.Compare(x.LastAccessTime, y.LastAccessTime);
                    case SortMode.CreatedInAsc: return DateTime.Compare(x.CreationTime, y.CreationTime);
                    case SortMode.CreatedInDesc: return -DateTime.Compare(x.CreationTime, y.CreationTime);
                    case SortMode.ModifiedInAsc: return DateTime.Compare(x.LastWriteTime, y.LastWriteTime);
                    case SortMode.ModifiedInDesc: return -DateTime.Compare(x.LastWriteTime, y.LastWriteTime);
                    case SortMode.RatingInAsc: return CompareRatingAsc(x.Rating, y.Rating);
                    case SortMode.RatingInDesc: return CompareRatingDesc(x.Rating, y.Rating);
                    case SortMode.SizeInAsc: return SizeCompareInAsc(x.Size, y.Size);
                    case SortMode.SizeInDesc: return SizeCompareInDesc(x.Size, y.Size);
                    case SortMode.TypeInAsc: return string.Compare(x.Type, y.Type);
                    case SortMode.TypeInDesc: return -string.Compare(x.Type, y.Type);

                    // preSort　用
                    case SortMode.NameInAsc: return LogicalStringComparer.Compare(x.Name, y.Name);
                    case SortMode.NameInDesc: return -LogicalStringComparer.Compare(x.Name, y.Name);

                }
                return 0;
                //throw new NotImplementedException();
            }
            private static int CompareRatingAsc(int ratingX, int ratingY)
            {
                if (ratingX <= 0) return ratingY <= 0 ? 0 : 1;
                else return ratingY <= 0 ? -1 : ratingX - ratingY;
            }
            private static int CompareRatingDesc(int ratingX, int ratingY)
            {
                if (ratingX <= 0) return ratingY <= 0 ? 0 : 1;
                else return ratingY <= 0 ? -1 : ratingY - ratingX;
            }
            private static int SizeCompareInAsc(long sizeX, long sizeY)
            {
                if (sizeX < 0) return sizeY < 0 ? 0 : 1;
                if (sizeY < 0) return -1;
                if (sizeX < sizeY) return -1;
                if (sizeX > sizeY) return +1;
                return 0;
            }
            private static int SizeCompareInDesc(long sizeX, long sizeY)
            {
                if (sizeX < 0) return sizeY < 0 ? 0 : 1;
                if (sizeY < 0) return -1;
                if (sizeX < sizeY) return +1;
                if (sizeX > sizeY) return -1;
                return 0;
            }
        }

        class FileSortInfo
        {
            private FileSystemInfo info;
            public FileSortInfo(FileSystemInfo info)
            {
                this.info = info;
            }
            public string FullName { get { return info.FullName; } }
            public string Name { get { return info.Name; } }
            public DateTime LastAccessTime { get { return info.LastAccessTime; } }
            public DateTime CreationTime { get { return info.CreationTime; } }
            public DateTime LastWriteTime { get { return info.LastWriteTime; } }

            public string Type;

            private long size = -2;
            public long Size
            {
                get
                {
                    if (size == -2)
                    {
                        var fileInfo = info as FileInfo;
                        if (fileInfo != null) size = fileInfo.Length;
                        else size = -1;
                    }
                    return size;
                }
            }

            private int rating = -1;
            public int Rating
            {
                get
                {
                    if (rating == -1)
                    {
                        rating = ZipPlaInfo.GetOnlyRating(info.Name);
                        if (rating <= 0) rating = 0;
                    }
                    return rating;
                    
                }
            }

            private bool randomIndexNotExists = true;
            private UBigInteger randomIndex;
            public UBigInteger GetRandomIndex(string seedForFirstCall)
            {
                if (randomIndexNotExists)
                {
                    randomIndex = CatalogForm.GetRandomIndex(info.FullName, seedForFirstCall);
                    randomIndexNotExists = false;
                }
                return randomIndex;
            }
        }

        /// <summary>
        /// candidates を comparer の順に、ascending なら小さいものから、そうでなければ大きいものから 1 つずつ返す（V27）。
        /// 以前は全体を並べ替えてから先頭から試していたが、ほとんどは最初の 1 つで開けるので、そのたびに残りから 1 回の走査で選ぶ。
        /// candidates は書き換える
        /// </summary>
        private static IEnumerable<FileSortInfo> EnumerateNearestFirst(List<FileSortInfo> candidates, IComparer<FileSortInfo> comparer, bool ascending)
        {
            while (candidates.Count > 0)
            {
                var best = 0;
                for (var i = 1; i < candidates.Count; i++)
                {
                    var c = comparer.Compare(candidates[i], candidates[best]);
                    if (ascending ? c < 0 : c > 0) best = i;
                }
                var result = candidates[best];
                candidates.RemoveAt(best);
                yield return result;
            }
        }

        // 次・前の本を探すときのフォルダの列挙結果（V27）。名前順のときだけ、フォルダの更新日時が変わるまで使い回す
        // （項目の追加・削除・名前変更ではフォルダの更新日時が変わる。日時やサイズの順はファイルの情報が変わるので毎回列挙する）
        private string siblingsCache_Parent;

        private DateTime siblingsCache_LastWriteTimeUtc;

        private int siblingsCache_Tick;

        private const int SiblingsCacheMilliseconds = 10000; // 更新日時が確実に変わらないファイルシステム（ネットワークなど）に備えて、短い間だけ

        private FileSortInfo[] siblingsCache_Files, siblingsCache_Folders;

        private void GetSiblingsForOpenNextOrPrevious(DirectoryInfo parent, SortModeDetails sort, out FileSortInfo[] files, out FileSortInfo[] folders)
        {
            var nameOnly = sort == null || sort.SortMode == SortMode.NameInAsc || sort.SortMode == SortMode.NameInDesc;
            var lastWrite = DateTime.MinValue;
            if (nameOnly)
            {
                lastWrite = parent.LastWriteTimeUtc;
                var age = unchecked(Environment.TickCount - siblingsCache_Tick);
                if (siblingsCache_Files != null && siblingsCache_Parent == parent.FullName && siblingsCache_LastWriteTimeUtc == lastWrite &&
                    0 <= age && age < SiblingsCacheMilliseconds)
                {
                    files = siblingsCache_Files;
                    folders = siblingsCache_Folders;
                    return;
                }
            }
            files = parent.GetFiles()
                .Where(info => PackedImageLoader.Supports(info.Name))
                .Select(info => new FileSortInfo(info)).ToArray();
            folders = parent.GetDirectories().Select(info => new FileSortInfo(info)).ToArray();
            if (nameOnly)
            {
                siblingsCache_Parent = parent.FullName;
                siblingsCache_LastWriteTimeUtc = lastWrite;
                siblingsCache_Tick = Environment.TickCount;
                siblingsCache_Files = files;
                siblingsCache_Folders = folders;
            }
            else
            {
                siblingsCache_Files = siblingsCache_Folders = null;
            }
        }

        private OpenResult OpenNextOrPrevious(bool next, bool loop, Action notFoundAction = null)
        {
            if (ExitAfterCoverSetting) return OpenResult.NoOperation;

            var currentParent = getCurrentParent();
            var current = currentArchiveFilePath;
            if (currentParent == null) return OpenResult.NoOperation;

            var sign = next ? 1 : -1;

            try
            {
                var isDir = Directory.Exists(current);
                var currentInfo = new FileSortInfo(isDir ? new DirectoryInfo(current) : new FileInfo(current) as FileSystemInfo);

                var parent = new DirectoryInfo(currentParent);
                
                var comparer = new OpenNextOrPrevious_Comparer(requestedSortModeDetails);
                
                FileSortInfo[] allFiles, allFolders;
                GetSiblingsForOpenNextOrPrevious(parent, requestedSortModeDetails, out allFiles, out allFolders);

                if (requestedSortModeDetails != null && (requestedSortModeDetails.SortMode == SortMode.TypeInAsc || requestedSortModeDetails.SortMode == SortMode.TypeInDesc ||
                    requestedSortModeDetails.PreSortMode == SortMode.TypeInAsc || requestedSortModeDetails.PreSortMode == SortMode.TypeInDesc))
                {
                    var nameArray = new string[1 + allFiles.Length + allFolders.Length];
                    nameArray[0] = isDir ? currentInfo.Name + Path.DirectorySeparatorChar : currentInfo.Name;
                    var i = 1;
                    foreach (var info in allFiles) nameArray[i++] = info.Name;
                    foreach (var info in allFolders) nameArray[i++] = info.Name + Path.DirectorySeparatorChar;
                    nameArray = CatalogForm.GetTypeNameArray(nameArray);
                    currentInfo.Type = nameArray[0];
                    //MessageBox.Show($"{currentInfo.Type} {currentInfo.Name}");
                    i = 1;
                    foreach (var info in allFiles) info.Type = nameArray[i++];
                    foreach (var info in allFolders) info.Type = nameArray[i++];
                }

                if (searchManagerFromCatalogForm != null) allFiles = allFiles.Where(info => searchManagerFromCatalogForm.Match(info.Name)).ToArray();
                var files = (from item in allFiles where sign * comparer.Compare(currentInfo, item) < 0 select item).ToArray();
                if (searchManagerFromCatalogForm != null) allFolders = allFolders.Where(info => searchManagerFromCatalogForm.Match(info.Name)).ToArray();
                var folders = (from item in allFolders where sign * comparer.Compare(currentInfo, item) < 0 select item).ToArray();
                var paths = new List<FileSortInfo>(files.Length + folders.Length);
                paths.AddRange(files);
                paths.AddRange(folders);
                // 全体を並べ替えず、今の本の次（前）にあたるものから 1 つずつ選ぶ（V27。ほとんどは最初の 1 つで開ける）

                while (true)
                {
                    foreach (var path in EnumerateNearestFirst(paths, comparer, ascending: next))
                    {
                        try
                        {
                            using (var loader = getPackedImageLoader(path.FullName, ViewerFormOnMemoryMode.ForCheckOnlyEntries))
                            {
                                //if(loader.GetPackedImageFullEntries().Count > 0) // FullEntries の方が処理が少なく、こちらが非ゼロなら Entries も非ゼロ
                                if (loader.HasAnyEntry()) // 根本的な改善
                                {
                                    return OpenFile(path.FullName);
                                }
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    if (loop)
                    {
                        files = (from item in allFiles where sign * comparer.Compare(currentInfo, item) > 0 select item).ToArray();
                        folders = (from item in allFolders where sign * comparer.Compare(currentInfo, item) > 0 select item).ToArray();
                        paths = new List<FileSortInfo>(files.Length + folders.Length);
                        paths.AddRange(files);
                        paths.AddRange(folders);
                        loop = false;
                        continue;
                    }

                    break;
                }

                notFoundAction?.Invoke();
                Program.ShowCursor();
                MessageBox.Show(this, Message.BookFolderIsNotFound, Message.Information, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return OpenResult.ShowWarning;
            }
            catch(Exception e)
            {
                notFoundAction?.Invoke();
                Program.ShowCursor();
#if DEBUG
                MessageBox.Show(this, e.ToString(), null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                MessageBox.Show(this, e.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
                return OpenResult.ShowWarning;
            }

        }

        private void ShowViewSettingHint()
        {
            if (ViewSettingHintMode == ViewSettingHintMode.ShowWhenChangingSettings)
            {
                var showThisHintAgain = true;
                MessageForm.Show(this, Message.BuiltInViewerSavingSettingsBehaviorDescription.Replace("\\n", "\n"), Message.Hint, Message.ShowThisHintAgainNextTime, ref showThisHintAgain, Message.OK, MessageBoxIcon.Information);
                if (!showThisHintAgain) ViewSettingHintMode = ViewSettingHintMode.NeverShow;
            }
        }

        private void viewToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            rotateLeftToolStripMenuItem.Checked = imageFilter.RotationAngle == 270;
            rotateRightToolStripMenuItem.Checked = imageFilter.RotationAngle == 90;

            //removeMoireToolStripMenuItem.Checked = imageFilter.MoireRemoverEnabled;
            pseudoColoringToolStripMenuItem.Checked = imageFilter.ToneCurveEnabled;
            if (ViewerFormImageFilter.TestGetToneCorveFile())
            {
                pseudoColoringToolStripMenuItem.Text = Message.ToneCurveUserDefined;
            }
            else
            {
                pseudoColoringToolStripMenuItem.Text = Message.ToneCurveSepiaTone;
            }

            if (ViewerMode == ViewerModeEnum.CoverSetting)
            {
                magnifierToolStripMenuItem.Enabled = false;
                enableMagnifierToolStripMenuItem.Enabled = false;
                disableMagnifierToolStripMenuItem.Enabled = false;

                rotateLeftToolStripMenuItem.Enabled = false;
                rotateRightToolStripMenuItem.Enabled = false;
                cancelRotationSettingToolStripMenuItem.Enabled = false;
                pseudoColoringToolStripMenuItem.Enabled = false;
            }
            else
            {
                var magEnabled = !string.IsNullOrEmpty(currentArchiveFilePath);
                magnifierToolStripMenuItem.Enabled = magEnabled;
                enableMagnifierToolStripMenuItem.Enabled = magEnabled && ViewerMode == ViewerModeEnum.Normal;
                disableMagnifierToolStripMenuItem.Enabled = magEnabled && ViewerMode == ViewerModeEnum.Magnifier;

                rotateLeftToolStripMenuItem.Enabled = true;
                rotateRightToolStripMenuItem.Enabled = true;
                cancelRotationSettingToolStripMenuItem.Enabled = imageFilter.RotationAngle != 0;
                pseudoColoringToolStripMenuItem.Enabled = true;
            }
            
            SetMessageForwarderStop(true);
        }

        private void enableMagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            enableMagnifier();
        }

        private void disableMagnifierToolStripMenuItem_Click(object sender, EventArgs e)
        {
            disableMagnifier();
        }

        private void togglePageSequenceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            togglePageSequence();
        }

        private void togglePageSequence()
        {
            // 2 つずつ独立に
            switch (BindingMode)
            {
                case BindingMode.LeftToRight: ChangePageDirection(BindingMode.RightToLeft); break;
                case BindingMode.RightToLeft: ChangePageDirection(BindingMode.LeftToRight); break;
                case BindingMode.SinglePage: ChangePageDirection(BindingMode.SinglePageWithoutScalingUp); break;
                default: ChangePageDirection(BindingMode.SinglePage); break;
            }

            // 4 つを順番に
        }

        private void pbView_MouseLeave(object sender, EventArgs e)
        {
            MouseInPbView = false;
            Program.ShowCursor();
            if (DrawMeasure) pbPaintDrawOnlyMeasure();
            pbView_MouseMove_prevContPos = null;
        }

        private void setSeekBarDirection()
        {
            switch(SeekBarMode)
            {
                case SeekBarMode.LeftToRight: mtbPage.InversedSlider = false; break;
                case SeekBarMode.RightToLeft: mtbPage.InversedSlider = true; break;
                case SeekBarMode.SameAsPage: mtbPage.InversedSlider = BindingMode == BindingMode.RightToLeft; break;
                case SeekBarMode.ReverseOfPage: mtbPage.InversedSlider = BindingMode != BindingMode.RightToLeft; break;
            }
        }

        private void orientSeekBarDirectionToPageSequenceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleSeekBarMode();
        }

        private void ToggleSeekBarMode()
        {
            SeekBarMode = SeekBarMode == SeekBarMode.LeftToRight ? SeekBarMode.SameAsPage : SeekBarMode.LeftToRight;
        }

        private void btnOpenLeft_Paint(object sender, PaintEventArgs e)
        {
            ControlPainter.FillLeftTriangle(sender as Control, e);
        }

        private void btnOpenRight_Paint(object sender, PaintEventArgs e)
        {
            ControlPainter.FillRightTriangle(sender as Control, e);
        }

        private void btnOpenLeft_Click(object sender, EventArgs e)
        {
            if (mtbPage.ValueCanChangedByMouseButton)
            {
                if (mtbPage.InversedSlider) OpenNext();
                else OpenPrevious();
            }
            else mtbPage.ValueCanChangedByMouseButton = true;
        }
    }
}
