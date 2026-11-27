using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Common;
using System.Reflection;
using System.Diagnostics;

namespace ZipPla
{
    /// <summary>
    /// カタログから待機ビューアへの依頼。名前付きパイプで送る（P3。以前は .NET Remoting の MarshalByRefObject で、
    /// 待機ビューアが項目を 1 つずつプロセス間で読みに来ていた）。
    /// カタログ側では、項目を書いてから Accept を false にすると、つながっている待機ビューアに全項目を送る。
    /// 待機ビューア側では、依頼が届くと項目が更新されて Accept が false になり、RequestArrived が起きる。Accept を true にすると受け取りを知らせる
    /// </summary>
    public class IpcLookAheadInfo
    {
        public string Path;
        public int Page;
        public ArchivesInArchiveMode ArchivesInArchive;
        public BindingMode DefaultBinding;
        public CoverBindingMode CoverBinding;
        public Color BackColor;
        public SortModeDetails SortModeDetails;
        public bool IgnoreSavedFilerSetting;
        public string FilterString;
        public string FilterStringWithoutAlias;
        public enum MessageEnum { Wait, Show, CoverSetting }
        public MessageEnum Message = MessageEnum.Wait;
        public InitialFullscreenMode InitialFullscreenMode;
        public bool AlwaysHideUI;
        public ReadOnMemoryMode ReadOnMemoryMode;

        private volatile bool accept = true;
        public bool Accept
        {
            get
            {
                if (IsDisconnected?.Invoke() == true) throw new IpcDisconnectedException(); // カタログが終了した（待機ビューア側）
                return accept;
            }
            set
            {
                if (!value && RequestSender != null) System.Threading.Interlocked.Increment(ref Generation); // カタログ側: 新しい依頼（Y18）
                accept = value;
                if (!value) RequestSender?.Invoke(this);  // カタログ側: 待機ビューアへ全項目を送る
                else AcceptedSender?.Invoke();            // 待機ビューア側: 受け取りを知らせる
            }
        }

        // 依頼の世代（Y18）。カタログ側で Accept を false にするたびに増え、依頼と一緒に送られ、受け取りの知らせで送り返される。
        // 同じ依頼を二重に送らない・古い依頼の受け取りの知らせで新しい依頼を受け取り済みにしないために使う
        internal int Generation;

        // 通信の側から書く（送り返さない）
        internal void SetAcceptFromRemote(bool value) => accept = value;
        internal bool AcceptRaw => accept;

        internal Action<IpcLookAheadInfo> RequestSender; // カタログ側（CatalogIpcServer.Register）
        internal Action AcceptedSender;                   // 待機ビューア側（ViewerIpcClient）
        internal Func<bool> IsDisconnected;               // 待機ビューア側

        /// <summary>待機ビューア側: 依頼が届いた、または通信が切れた（別のスレッドから呼ばれる）</summary>
        public event Action RequestArrived;
        internal void RaiseRequestArrived()
        {
            try { RequestArrived?.Invoke(); } catch { }
        }

        public static string ChannelName = "ipcZipPla";
    }

    /// <summary>
    /// ビューアからカタログへの報告。名前付きパイプで送る（P3）。ビューア側では AddReports がカタログへ送り、
    /// カタログ側では届いた報告を積んで TakeReports で取り出す
    /// </summary>
    public class IpcReportFromViewerToCatalog
    {
        [Serializable]
        public class OldAndNewString
        {
            public string Old, New, Sor = null;
            public int NewPage;
            public bool RequestToUpdateLastAccessTime;
            public bool RequestToChangeSelection;
            public SortMode ViewerFormSortMode;
            public bool RequestToReloadTags;
        }
        public List<OldAndNewString> PathRequiredReload = new List<OldAndNewString>();

        // ビューアからの追加とカタログでの取り出しを、カタログのプロセス内で排他して行う（V7）。
        // 以前はビューアがリスト全体を読んで書き戻していた（プロセス間の呼び出し 2 回、取り出しと重なると報告が消えうる）
        private readonly object locker = new object();
        internal Action<OldAndNewString[]> RemoteSender; // ビューア側（ViewerIpcClient）
        public void AddReports(OldAndNewString[] reports)
        {
            if (reports == null || reports.Length == 0) return;
            var sender = RemoteSender;
            if (sender != null)
            {
                sender(reports); // 切れていれば IpcDisconnectedException
                return;
            }
            lock (locker)
            {
                var list = PathRequiredReload.Where(x => x != null).ToList();
                list.AddRange(reports);
                PathRequiredReload = list;
            }
        }
        public OldAndNewString[] TakeReports()
        {
            lock (locker)
            {
                if (PathRequiredReload.Count == 0) return null;
                var result = PathRequiredReload.ToArray();
                PathRequiredReload = new List<OldAndNewString>();
                return result;
            }
        }

    }
}
