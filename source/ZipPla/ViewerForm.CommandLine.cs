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
        private static bool TryParse(string value, bool ignoreCase, out CoverBindingMode? result)
        {
            foreach (CoverBindingMode r in Enum.GetValues(typeof(CoverBindingMode)))
            {
                if (string.Compare(ToString(r), value, ignoreCase) == 0)
                {
                    result = r;
                    return true;
                }
            }
            if (string.Compare(ToString(null as CoverBindingMode?), value, ignoreCase) == 0)
            {
                result = null;
                return true;
            }
            result = null;
            return false;
        }

        private static string ToString(CoverBindingMode? value)
        {
            switch (value)
            {
                case CoverBindingMode.Default: return "d";
                case CoverBindingMode.ForceSingle: return "f";
                case null: return "n";
                default: return "";
            }
        }

        private static bool TryParse(string value, bool ignoreCase, out InitialFullscreenMode result)
        {
            foreach (InitialFullscreenMode r in Enum.GetValues(typeof(InitialFullscreenMode)))
            {
                if (string.Compare(ToString(r), value, ignoreCase) == 0)
                {
                    result = r;
                    return true;
                }
            }
            result = default(InitialFullscreenMode);
            return false;
        }

        private static string ToString(InitialFullscreenMode value)
        {
            switch (value)
            {
                case InitialFullscreenMode.Default: return "d";
                case InitialFullscreenMode.ForceFullscreen: return "f";
                case InitialFullscreenMode.ForceWindow: return "w";
                default: return "";
            }
        }

        private static bool TryParse(string value, bool ignoreCase, out BindingMode? result)
        {
            foreach (BindingMode r in Enum.GetValues(typeof(BindingMode)))
            {
                if (string.Compare(ToString(r), value, ignoreCase) == 0)
                {
                    result = r;
                    return true;
                }
            }
            if (string.Compare(ToString(null as BindingMode?), value, ignoreCase) == 0)
            {
                result = null;
                return true;
            }
            result = null;
            return false;
        }

        private static string ToString(BindingMode? value)
        {
            switch (value)
            {
                case BindingMode.LeftToRight: return "l";
                case BindingMode.RightToLeft: return "r";
                case BindingMode.SinglePage: return "s";
                case BindingMode.SinglePageWithoutScalingUp: return "d";
                case null: return "n";
                default: return "";
            }
        }

        private static bool TryParse(string value, bool ignoreCase, out ArchivesInArchiveMode? result)
        {
            foreach (ArchivesInArchiveMode r in Enum.GetValues(typeof(ArchivesInArchiveMode)))
            {
                if (string.Compare(ToString(r), value, ignoreCase) == 0)
                {
                    result = r;
                    return true;
                }
            }
            if (string.Compare(ToString(null as ArchivesInArchiveMode?), value, ignoreCase) == 0)
            {
                result = null;
                return true;
            }
            result = null;
            return false;
        }

        private static string ToString(ArchivesInArchiveMode? value)
        {
            switch (value)
            {
                case ArchivesInArchiveMode.Always1Level: return "c1";
                case ArchivesInArchiveMode.Ignore: return "c0";
                case ArchivesInArchiveMode.UntilFound2Level: return "f2";
                case ArchivesInArchiveMode.IfNoOther1Level: return "l1";
                case null: return "n";
                default: return "";
            }
        }

        private static bool TryParse(string value, bool ignoreCase, out bool? result)
        {
            foreach (var r in new bool?[3] { null, false, true })
            {
                if (string.Compare(ToString(r), value, ignoreCase) == 0)
                {
                    result = r;
                    return true;
                }
            }
            result = null;
            return false;
        }

        private static string ToString(bool? value)
        {
            switch (value)
            {
                case true: return "t";
                case false: return "f";
                default: return "n";
            }
        }

        private static bool ColorTryParse(string value, out Color result)
        {
            if (!string.IsNullOrEmpty(value))
            {
                uint argb;
                bool success;
                switch (value.First())
                {
                    case '#': success = uint.TryParse(value.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out argb); break;
                    default: argb = 0; success = false;break;
                }
                if (success)
                {
                    result = Color.FromArgb((int)(argb | (uint)0xff000000));
                    return true;
                }
                else
                {
                    return Enum.TryParse(value, ignoreCase: true, result: out result);
                }
            }

            result = Color.Empty;
            return false;
        }

        private static string RGBToString(Color? color)
        {
            if (color == null) return "#000000";
            var c = (Color)color;
            return "#" + (c.R << 16 | c.G << 8 | c.B).ToString("x6"); // ToString は 0x を付けない
        }

        private static bool TryParse(string value, bool ignoreCase, out ReadOnMemoryMode? result)
        {
            foreach (ReadOnMemoryMode r in Enum.GetValues(typeof(ReadOnMemoryMode)))
            {
                if (string.Compare(ToString(r), value, ignoreCase) == 0)
                {
                    result = r;
                    return true;
                }
            }
            if (string.Compare(ToString(null as ReadOnMemoryMode?), value, ignoreCase) == 0)
            {
                result = null;
                return true;
            }
            result = null;
            return false;
        }

        private static string ToString(ReadOnMemoryMode? value)
        {
            switch (value)
            {
                case ReadOnMemoryMode.None: return "d";
                case ReadOnMemoryMode.ExceptLookAhead: return "e";
                case ReadOnMemoryMode.Always: return "a";
                case null: return "n";
                default: return "";
            }
        }

        private const string commandLineEqual = ":"; // パスとの衝突に注意

        /// <summary>
        /// 空でない場合先頭に空白を付加して返す。空なら null を返す
        /// </summary>
        /// 
        private static string GetCommandLineOption(CommandLineOptionInfo info, bool minimize)
        {
            var result = "-v";
            var def = minimize ? new CommandLineOptionInfo() : null;
            if (!minimize || info.CoverBindingMode != def.CoverBindingMode) addText(ref result, $"c{commandLineEqual}{ToString(info.CoverBindingMode)}");
            if (!minimize || info.OpenInPreviousImageFilterSetting != def.OpenInPreviousImageFilterSetting) addText(ref result, $"i{commandLineEqual}{ToString(info.OpenInPreviousImageFilterSetting)}");
            if (!minimize || info.InitialFullscreenMode != def.InitialFullscreenMode) addText(ref result, $"f{commandLineEqual}{ToString(info.InitialFullscreenMode)}");
            if (!minimize || info.BackColor != def.BackColor) addText(ref result, $"b{commandLineEqual}{RGBToString(info.BackColor)}");
            if (!minimize || info.ReadOnMemoryMode != def.ReadOnMemoryMode) addText(ref result, $"w{commandLineEqual}{ToString(info.ReadOnMemoryMode)}");
            if (!minimize || info.ArchivesInArchiveMode != def.ArchivesInArchiveMode) addText(ref result, $"a{commandLineEqual}{ToString(info.ArchivesInArchiveMode)}");
            if (!minimize || info.AlwaysHideUI != def.AlwaysHideUI) addText(ref result, $"h{commandLineEqual}{ToString(info.AlwaysHideUI)}");
            if (!minimize || info.DefaultBindingMode != def.DefaultBindingMode) addText(ref result, $"d{commandLineEqual}{ToString(info.DefaultBindingMode)}");

            return result;
        }

        // あまり長いコマンドラインはショートカットに登録できない

        private static void addText(ref string text, string text2)
        {
            text += " " + text2;
        }

        /// <summary>
        /// 例外を投げない
        /// </summary>
        /// <param name="path"></param>
        public static bool CreateShortcut(Form owner, CommandLineOptionInfo info, string defaultPath)
        {
            try
            {
                defaultPath = Program.ItsSelfOrExistingParentDirectory(defaultPath);
                using (var dialog = new SaveFileDialog())
                {
                    dialog.InitialDirectory = defaultPath;
                    dialog.FileName = $"ZipPla ({Message.BuiltInViewer}).lnk";
                    dialog.Filter = "*.lnk|*.lnk";
                    dialog.Title = Message.CreateShortcutFromCurrentSettings;
                    if (dialog.ShowDialog(owner) == DialogResult.OK)
                    {
                        CreateShortcut(dialog.FileName, info);
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(owner, e.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }

        /// <summary>
        /// 例外を投げる
        /// </summary>
        /// <param name="path"></param>
        public static void CreateShortcut(string path, CommandLineOptionInfo info)
        {
            var t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8"));
            dynamic shell = Activator.CreateInstance(t);
            try
            {
                //WshShortcutを作成
                var shortcut = shell.CreateShortcut(path);
                try
                {
                    shortcut.TargetPath = Application.ExecutablePath;
                    shortcut.IconLocation = Application.ExecutablePath + ",1"; // ビルド後にソース同梱の dark.ico を埋め込む
                    shortcut.Arguments = GetCommandLineOption(info, minimize: false);
                    shortcut.Save();

                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        public class CommandLineOptionInfo
        {
            public CoverBindingMode? CoverBindingMode = null;
            public bool OpenInPreviousImageFilterSetting = true;
            public InitialFullscreenMode InitialFullscreenMode = InitialFullscreenMode.Default;
            public Color? BackColor = null;
            public ArchivesInArchiveMode? ArchivesInArchiveMode = null;
            public bool? AlwaysHideUI = null;
            public BindingMode? DefaultBindingMode = null;
            public ReadOnMemoryMode? ReadOnMemoryMode = null;

            // これは Main に渡すためのもの。ViewerForm 内では解釈しない
            public string Path;
        }

        public ViewerForm(CommandLineOptionInfo info)
        {
            Constructor(info);

            Shown += (sender, e) =>
            {
                Program.StartCheckUpdateAndNgen(this, checkNgen);
                PackedImageLoader.CheckSevenZipExistence(this);
            };

            saveToHistory = true;

            OpenFile_UserOpen = true;
        }

        // 従来の直接起動コンストラクタ

        // 仮想的に先読みプロセスの動作をするコンストラクタ
        // フルスクリーン起動時のチラツキ軽減が目的

        public ViewerForm(string path, int page, CommandLineOptionInfo info)
        {
            this.ipcLookAheadInfo = new IpcLookAheadInfo(); // null でないことが重要

            if (info != null)
            {
                ipcLookAheadInfo.AlwaysHideUI = info.AlwaysHideUI ?? ipcLookAheadInfo.AlwaysHideUI;
                ipcLookAheadInfo.ArchivesInArchive = info.ArchivesInArchiveMode ?? ipcLookAheadInfo.ArchivesInArchive;
                ipcLookAheadInfo.BackColor = info.BackColor ?? ipcLookAheadInfo.BackColor;
                ipcLookAheadInfo.CoverBinding = info.CoverBindingMode ?? ipcLookAheadInfo.CoverBinding;
                ipcLookAheadInfo.DefaultBinding = info.DefaultBindingMode ?? ipcLookAheadInfo.DefaultBinding;
                ipcLookAheadInfo.InitialFullscreenMode = info.InitialFullscreenMode;
                ipcLookAheadInfo.ReadOnMemoryMode = info.ReadOnMemoryMode ?? ipcLookAheadInfo.ReadOnMemoryMode;
            }
            ipcLookAheadInfo.Path = path;
            ipcLookAheadInfo.Page = page;
            ipcLookAheadInfo.Message = IpcLookAheadInfo.MessageEnum.Show;
            ipcLookAheadInfo.Accept = false;
            

            Constructor(info);

            if (LoadSettingToShown_fullscreen || LoadSettingToShown_maximized)
            {
                ViewerForm_Shown_VisibleToFalse = true;
                WindowState = FormWindowState.Minimized;
            }


            Shown += (sender, e) =>
            {
                tmWatchIpc_Tick(sender, null); // Load で呼び出すと読み込みが早かった場合にエラー。しかも表示を２回行うので時間も無駄になる
                Program.StartCheckUpdateAndNgen(this, checkNgen);
                PackedImageLoader.CheckSevenZipExistence(this);
            };

            //tmMemoryReducer.Enabled = true; // Start() と同じ

            saveToHistory = true;
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
    }
}
