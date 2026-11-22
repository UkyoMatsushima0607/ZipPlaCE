using Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZipPla
{
    public static class UpdateCheck
    {
        /// <summary>
        /// ZipPla CE の更新を GitHub の Releases で確認する（X11。以前は元祖 ZipPla の vector.co.jp のページを http で見て CE の版と比べていた）。
        /// 設定の読み込みと通信は背景で行い、UI スレッドを止めない（S8）
        /// </summary>
        public static async Task FromGitHub(Control owner, string repository, Action<bool> postAction = null, bool byUser = false)
        {
            var messageShown = false;
            var releasesUri = $"https://github.com/{repository}/releases";
            try
            {
                var config = await Task.Run(() => new UpdateCheckConfig()); // XmlSerializer の準備を UI スレッドでしない（S8）
                if (needToCheck(owner, config, force: byUser, dialog: out var dialog))
                {
                    if (dialog) byUser = true;
                    var latest = await GetLatestReleaseTagFromGitHub(repository);
                    var developmentVersion = false;
                    if (latest != null && isNewerThanThis(latest, out developmentVersion))
                    {
                        messageShown = true;
                        MethodInvoker action = () =>
                        MessageForm.Show(owner, "ZipPla CE " + latest + "\r\n\r\n" + Message.NewVersionIsAvailable, Message.Information,
                            Message.DownloadPage, (sender, e) =>
                            {
                                try
                                {
                                    System.Diagnostics.Process.Start(releasesUri + "/latest");
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            },
                            Message.ZipPlaFolder, (sender, e) =>
                            {
                                try
                                {
                                    System.Diagnostics.Process.Start(Application.StartupPath); // 既に開いていればそれを表示
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            },
                            Message.Close, (sender, e) =>
                            {
                                try
                                {
                                    sender.Close();
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            },
                            MessageBoxIcon.Information);
                        if (owner == null || !owner.InvokeRequired) action(); else owner.Invoke(action);
                    }
                    else if (byUser)
                    {
                        MessageForm.Show(owner, developmentVersion ? Message.YouAreUsingUnreleasedVersion : Message.NewVersionIsNotAvailable, Message.Information, Message.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                if (byUser) MessageBox.Show(ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            postAction?.Invoke(messageShown);
        }

        private static readonly Regex regexTagName = new Regex("\"tag_name\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);

        /// <summary>最新のリリースのタグ名。リリースがまだなければ null</summary>
        private static async Task<string> GetLatestReleaseTagFromGitHub(string repository)
        {
            var uri = $"https://api.github.com/repos/{repository}/releases/latest";
            using (var wc = new System.Net.WebClient())
            {
                wc.Headers[System.Net.HttpRequestHeader.UserAgent] = "ZipPlaCE-UpdateCheck"; // GitHub の API は User-Agent が必須
                wc.Headers[System.Net.HttpRequestHeader.Accept] = "application/vnd.github+json";
                string json;
                try
                {
                    json = await wc.DownloadStringTaskAsync(uri);
                }
                catch (System.Net.WebException ex) when ((ex.Response as System.Net.HttpWebResponse)?.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null; // リリースがない
                }
                var m = regexTagName.Match(json);
                if (!m.Success) throw new FormatException("GitHub release");
                return m.Groups[1].Value;
            }
        }

        private static readonly Regex regexVersionParts = new Regex(@"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?", RegexOptions.Compiled);

        // タグ（v1.8.0 や 1.7.2.10 など。足りない部分は 0）がこの版より新しいか
        private static bool isNewerThanThis(string tag, out bool developmentVersion)
        {
            developmentVersion = false;
            var m = regexVersionParts.Match(tag);
            if (!m.Success) throw new FormatException("Tag: " + tag);
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var mine = new[] { version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision) };
            for (var i = 0; i < 4; i++)
            {
                var g = m.Groups[i + 1];
                var theirs = g.Success ? int.Parse(g.Value) : 0;
                if (mine[i] < theirs) return true;
                if (mine[i] > theirs) { developmentVersion = true; return false; }
            }
            return false;
        }

        private static readonly Regex regexGetVersion = new Regex(@"(\d+)\.(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);
        public static int VersionStringComp(string a, string b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            Match aM, bM = null;
            if((aM = regexGetVersion.Match(a)).Success && (bM = regexGetVersion.Match(b)).Success)
            {
                for(var i = 1; i <= 4; i++)
                {
                    var ad = int.Parse(aM.Groups[i].Value);
                    var bd = int.Parse(bM.Groups[i].Value);
                    if (ad > bd) return +1;
                    if (ad < bd) return -1;
                }
                return 0;
            }
            throw new ArgumentException(null, bM == null ? "a" : "b");
        }

        private static bool needToCheck(Control owner, bool force, out bool dialog)
        {
            return needToCheck(owner, new UpdateCheckConfig(), force, out dialog);
        }

        private static bool needToCheck(Control owner, UpdateCheckConfig config, bool force, out bool dialog)
        {

            var lastCheckTime = config.LastCheckTime;
            var first = lastCheckTime == DateTime.MinValue;

            if (config.Mode == null) config.Mode = first ? UpdateCheckMode.Default : UpdateCheckMode.Silent;

            if (force)
            {
                config.LastCheckTime = DateTime.Now;
                config.Save();
                dialog = false;
                return true;
            }

            if (config.Mode == UpdateCheckMode.Never)
            {
                dialog = false;
                return false;
            }

            var now = DateTime.Now;

            if (first)
            {
                config.LastCheckTime = now;
                config.Save();
                dialog = false;
                return false;
            }

            if (lastCheckTime > now) lastCheckTime = DateTime.MinValue;
            //lastCheckTime = DateTime.MinValue;

            var lastUpdateTime = now.Date;
            while (true)
            {
                switch (lastUpdateTime.DayOfWeek)
                {
                    case DayOfWeek.Tuesday:
                    case DayOfWeek.Thursday:
                    case DayOfWeek.Saturday:
                        break;
                    default:
                        lastUpdateTime -= TimeSpan.FromDays(1);
                        continue;
                }
                break;
            }
            if (lastUpdateTime > lastCheckTime)
            {
                bool returnTrue;
                if (config.Mode == UpdateCheckMode.Silent)
                {
                    dialog = false;
                    returnTrue = true;
                }
                else
                {
                    dialog = true;
                    var checkBoxChecked = config.Mode == UpdateCheckMode.WithDialog;
                    returnTrue = MessageForm.Show(owner, Message.ZipPlaWillCheckForUpdate, Message.Information, Message.ShowThisDialogAgainNextTime, ref checkBoxChecked, Message.OK, Message.Cancel, MessageBoxIcon.Information) == 0;
                    if (returnTrue)
                    {
                        config.Mode = checkBoxChecked ? UpdateCheckMode.WithDialog : UpdateCheckMode.Silent;
                    }
                }
                if (returnTrue)
                {
                    config.LastCheckTime = now;
                    config.Save();
                    return true;
                }
            }
            dialog = false;
            return false;

        }
        
        /*
        private static async Task<string> GetFileNameFromVectorByBrowser(string vectorUri)
        {
            var document = await GetDocument(vectorUri);
            var table = document.GetElementById("download_list").Parent.GetElementsByTagName("table")[0];
            foreach(HtmlElement tr in table.GetElementsByTagName("tr"))
            {
                var th = tr.GetElementsByTagName("th");
                if (th.Count != 1 || th[0].InnerText != "ファイル：") continue;
                var td = tr.GetElementsByTagName("td");
                if (td.Count != 1) break;
                var tdText = td[0].InnerText;
                var slashPos = tdText.IndexOf(" / ");
                if (slashPos < 0) break;
                return tdText.Substring(0, slashPos);
            }
            throw new FormatException("Html");
        }

        [DllImport("urlmon.dll")]
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Error)]
        internal static extern int CoInternetSetFeatureEnabled(uint FeatureEntry, [MarshalAs(UnmanagedType.U4)] uint dwFlags, bool fEnable);
        private static async Task<HtmlDocument> GetDocument(string uri)
        {
            using (var webBrowser = new WebBrowser())
            {
                webBrowser.ScriptErrorsSuppressed = true;
                webBrowser.Navigated += (sender, e) =>
                {
                    const uint FEATURE_DISABLE_NAVIGATION_SOUNDS = 21;
                    const uint SET_FEATURE_ON_PROCESS = 0x00000002;
                    CoInternetSetFeatureEnabled(FEATURE_DISABLE_NAVIGATION_SOUNDS, SET_FEATURE_ON_PROCESS, true);
                };
                var loading = true;
                {
                    webBrowser.DocumentCompleted += (sender, e) => loading = false;
                    webBrowser.Url = new Uri(uri);
                    await Task.Run(() => { while (loading) { Thread.Sleep(50); } });
                }
                return webBrowser.Document;
            }
        }
        */
    }

    public enum UpdateCheckMode { Default, Never, WithDialog, Silent }

    public class UpdateCheckConfig : Configuration
    {
        public UpdateCheckMode? Mode = null;
        public DateTime LastCheckTime = DateTime.MinValue;
    }
}
