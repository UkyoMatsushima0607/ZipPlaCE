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
            //if (string.IsNullOrEmpty(text)) text = text2;
            //else
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
#if AUTOBUILD
                    shortcut.IconLocation = Application.ExecutablePath + ",1"; // ビルド後にソース同梱の dark.ico を埋め込む
#else
                    shortcut.IconLocation = Application.ExecutablePath + ",0";
#endif
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
            //Program.SetPriority(ProcessPriorityClass.BelowNormal);
            this.ipcLookAheadInfo = new IpcLookAheadInfo(); // null でないことが重要
                                                            //this.ipcReportFromViewerToCatalog = ipcReportFromViewerToCatalog;

            if (info != null)
            {
                ipcLookAheadInfo.AlwaysHideUI = info.AlwaysHideUI ?? ipcLookAheadInfo.AlwaysHideUI;
                ipcLookAheadInfo.ArchivesInArchive = info.ArchivesInArchiveMode ?? ipcLookAheadInfo.ArchivesInArchive;
                ipcLookAheadInfo.BackColor = info.BackColor ?? ipcLookAheadInfo.BackColor;
                ipcLookAheadInfo.CoverBinding = info.CoverBindingMode ?? ipcLookAheadInfo.CoverBinding;
                ipcLookAheadInfo.DefaultBinding = info.DefaultBindingMode ?? ipcLookAheadInfo.DefaultBinding;
                ipcLookAheadInfo.InitialFullscreenMode = info.InitialFullscreenMode;
                ipcLookAheadInfo.ReadOnMemoryMode = info.ReadOnMemoryMode ?? ipcLookAheadInfo.ReadOnMemoryMode;
                //CommandLineOptionInfoForLookAhead = info;
            }
            ipcLookAheadInfo.Path = path;
            ipcLookAheadInfo.Page = page;
            ipcLookAheadInfo.Message = IpcLookAheadInfo.MessageEnum.Show;
            ipcLookAheadInfo.Accept = false;
            

            Constructor(info);
            //Constructor(initialFullscreenMode, alwaysHideUI, backColor: Color.Black, aia: ArchivesInArchiveMode.Ignore);

            if (LoadSettingToShown_fullscreen || LoadSettingToShown_maximized)
            {
                //Visible = false;
                ViewerForm_Shown_VisibleToFalse = true;
                WindowState = FormWindowState.Minimized;
            }

            //tmWatchIpc_Tick(null, null);

            //Load += (sender, e) => tmWatchIpc_Tick(sender, null);
            //ViewerForm_Load_CallTick = true;

            //tmWatchIpc.Start();
            //var visibleTrue = !LoadSettingToShown_fullscreen;
            Shown += (sender, e) =>
            {
                //WindowState = FormWindowState.Minimized;
                //Visible = false;
                tmWatchIpc_Tick(sender, null); // Load で呼び出すと読み込みが早かった場合にエラー。しかも表示を２回行うので時間も無駄になる
                Program.StartCheckUpdateAndNgen(this, checkNgen);
                PackedImageLoader.CheckSevenZipExistence(this);
            };

            //tmMemoryReducer.Enabled = true; // Start() と同じ

            saveToHistory = true;
        }
    }
}
