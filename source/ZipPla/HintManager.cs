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
    public static class HintManager
    {
        private const string HintVersion = "1.4.3.0"; // 表示内容の主旨自体が変わらないかぎり増やさない
        private static Regex ShowDialog_ReplacerRegex;
        public static void ShowDialog(Form owner, Action preAction)
        {
            try
            {
                // ヒントを表示しない条件
                if (MovieThumbnailLoader.ffmpegExists())
                {
                    return;
                }

                var config = new GeneralConfig();
                var vComp = UpdateCheck.VersionStringComp(HintVersion, config.ReadHintVersion);
                if (config.ShowHintOnlyOnce && vComp <= 0) return;
                preAction?.Invoke();

                bool hiddenExtension;
                try
                {
                    var zippla = Application.ExecutablePath;
                    hiddenExtension = FileTypeManager.GetDisplayName(zippla).ToLower() ==
                        System.IO.Path.GetFileNameWithoutExtension(zippla).ToLower();
                }
                catch
                {
                    hiddenExtension = false;
                }

                string message;
                if (hiddenExtension)
                {
                    message = Message.RecommendFfmpeg + "\n\n\n" + Message.RecommendFfmpegRemarkForHiddenExtension;
                }
                else
                {
                    message = Message.RecommendFfmpeg;
                }
                // 閉鎖した配布元（Zeranoe、2020 年終了）。AutoBuild は既存の言語ファイルを作り直さないので、古いファイルの URL もここで置き換え、
                // Zeranoe 向けの「$1-bit / Static 版を推奨」の行は除く
                const string closedFfmpegSite = "https://ffmpeg.zeranoe.com/builds/";
                var staleFfmpegMessage = message.Contains(closedFfmpegSite);
                if (staleFfmpegMessage) message = message.Replace(closedFfmpegSite, "https://ffmpeg.org/download.html");
                var a = message.Split(new string[] { @"\n" }, StringSplitOptions.None);
                if (staleFfmpegMessage) a = a.Where(line => !line.Contains("$1")).ToArray();
                var str = "";
                if (ShowDialog_ReplacerRegex == null) ShowDialog_ReplacerRegex = new Regex(@"\$1|\{[^|]+\|[^|]+\}");
                var bitsString = Environment.Is64BitOperatingSystem ? "64" : "32"; // DLL 等の都合で 32bit で動作させても ffmpeg まで合わせる必要はないので Is64Bit"OperatingSystem"
                for (var i = 0; i < a.Length; i++)
                {
                    var ai = a[i];
                    if (ShowDialog_ReplacerRegex.IsMatch(ai))
                    {
                        var b = bitsString;
                        if (!string.IsNullOrEmpty(b))
                        {
                            str += SelectorReplace(ai.Replace("$1", b), b == "32" ? 0 : 1, 2);
                        }
                    }
                    else
                    {
                        str += ai;
                    }
                    if (i < a.Length - 1) str += "\n";
                }

                var checkBoxChecked0 = !config.ShowHintOnlyOnce;
                var checkBoxChecked = checkBoxChecked0;
                var res = MessageForm.Show(owner, str, Message.Information, Message.CheckExistenceOfFfmpegAtStartup, ref checkBoxChecked, Message.OK, MessageBoxIcon.Information);
                if (res == 0 && checkBoxChecked != checkBoxChecked0 || vComp > 0) // 表示したバージョンの更新は常に行う。さもなければバツで閉じた後で共通設定の内容を無視したような挙動になる。
                {
                    Configuration.ClosePrepareMode();
                    config = new GeneralConfig(); // ポップアップ表示中に設定が書き換えられた場合のため
                    var changed = false;
                    if (UpdateCheck.VersionStringComp(HintVersion, config.ReadHintVersion) > 0)
                    {
                        config.ReadHintVersion = HintVersion;
                        changed = true;
                    }
                    if (res == 0 && checkBoxChecked != !config.ShowHintOnlyOnce)
                    {
                        config.ShowHintOnlyOnce = !checkBoxChecked;
                        changed = true;
                    }
                    if (changed)
                    {
                        var e = config.Save();
                        if (e != null) throw e;
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(owner, e.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private static string SelectorReplace(string str, int zeroOriginIndex, int totalCount)
        {
            var ptn = @"\{([^|]*)";
            for (var i = 1; i < totalCount; i++) ptn += @"\|([^|]*)";
            ptn += @"\}";
            var m = Regex.Match(str, ptn);
            if (m.Success)
            {
                str = str.Replace(str.Substring(m.Index, m.Length), m.Groups[zeroOriginIndex + 1].Value);
            }
            return str;
        }
    }
}
