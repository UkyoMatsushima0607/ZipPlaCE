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
        private void ShwoCoverRectangle()
        {
            pbView_Paint_Rectangle = GetCoverRectangleForDisplay();
            //pbView.Invalidate(false); // 描画は Paint イベントハンドラで行われる
            pbPaintInvalidate();
        }

        private static bool isDirSafety(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private void SetCoverForThumbnail(bool delete = false, Point? clientPoint = null)
        {

            Program.ShowCursor();

            var prevArchiveFilePath = getCurrentRenameTarget();
            if (string.IsNullOrEmpty(prevArchiveFilePath))
            {
                Program.ShowCursor();
                MessageBox.Show(this, (new FileNotFoundException(null, $"{currentArchiveFilePath}")).Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitCoverSetting();
                //OpenFile(newArchiveFilePath); // こちらは不適切
                //OpenFile(prevArchiveFilePath, currentPage); // loader をまだ null にしていないので再読み込みは不要
                return;
            }
            var zipPlaInfo = new ZipPlaInfo(prevArchiveFilePath);
            zipPlaInfo.ThumbnailInfo = delete ? null : GetThumbnailInfoAsTuple(clientPoint);
            var newArchiveFilePath = zipPlaInfo.GetPathOfCurrentInfo(isDirSafety(prevArchiveFilePath));

            Program.ShowCursor();

            if (MessageForm.Show(this, (delete ? Message.WillClearCoverSetting : Message.RenameForCover) + "\r\n\r\n" +
                Message.BeforeRename + ":\r\n  " + Path.GetFileName(prevArchiveFilePath) + "\r\n" +
                Message.AfterRename + ":\r\n  " + Path.GetFileName(newArchiveFilePath) + "\r\n", Message.Question, Message.OK, Message.Cancel, MessageBoxIcon.Question)
                != 0)
            {
                ExitCoverSetting();
                return;
            }

            RenameCurrentArchiveAndReopen(prevArchiveFilePath, newArchiveFilePath, beforeReopen: ExitCoverSetting);
        }

        /// <summary>
        /// 今の書庫の名前を変えて開き直す（表紙・綴じ方向・評価・タグの設定をファイル名に書くとき）。
        /// 読み込み中の書庫を閉じてから移動し、失敗したら元の名前で開き直す（失敗はよくあるので知らせない）。
        /// 成功したらカタログに報告する。beforeReopen は開き直す直前に呼ぶ（成功・失敗とも）
        /// </summary>
        private void RenameCurrentArchiveAndReopen(string prevArchiveFilePath, string newArchiveFilePath, Action beforeReopen = null)
        {
            DisposeLoader(); // 読み込みジョブが取り出し中でないときに（V1）
            try
            {
                Program.FileOrDirectoryMove(prevArchiveFilePath, newArchiveFilePath, retryOwner: this);
            }
            catch
            {
                beforeReopen?.Invoke();
                OpenFile(prevArchiveFilePath, currentPage, currentSortModeDetails); // loader を閉じたので開き直す
                return;
            }

            SendRenameMessageToCatalogForm(prevArchiveFilePath, newArchiveFilePath);
            beforeReopen?.Invoke();
            OpenFile(newArchiveFilePath, currentPage, currentSortModeDetails);
        }

        private void ExitCoverSetting()
        {
            if (ExitAfterCoverSetting)
            {
                Application.Exit();
            }

            ViewerMode = ViewerModeEnum.Normal;
            pbView_Paint_Rectangle = Rectangle.Empty;
            //pbView.Invalidate(false); // 描画は Paint イベントハンドラで行われる
            if (imageFilter.PreFilterExists() ||
                 (BindingMode != BindingMode.SinglePage && BindingMode != BindingMode.SinglePageWithoutScalingUp && MaxDivision > 1))
            {
                reloadForChangeFilter(forOnlyPostFilter: false);
            }
            else if (imageFilter.PostFilterExists())
            {
                reloadForChangeFilter(forOnlyPostFilter: true);
            }
            else
            {
                pbPaintInvalidate();
            }
        }

        private void SendRenameMessageToCatalogForm(string prevArchiveFilePath, string newArchiveFilePath)
        {
            Program.ShowCursor();

            if (ipcReportFromViewerToCatalog != null)
            {
                try
                {
                    var rep = new IpcReportFromViewerToCatalog.OldAndNewString();
                    rep.Old = prevArchiveFilePath;
                    rep.New = newArchiveFilePath;

                    SendReportsToCatalog(rep);
                }
                catch (IpcDisconnectedException)
                {
                    ipcReportFromViewerToCatalog = null;
                }
                catch (Exception error)
                {
                    Program.AlertError(error);
                }

            }
        }

        private void RequestToUpdateTags()
        {
            if (ipcReportFromViewerToCatalog != null)
            {
                try
                {
                    var rep = new IpcReportFromViewerToCatalog.OldAndNewString();
                    rep.RequestToReloadTags = true;
                    SendReportsToCatalog(rep);
                }
                catch (IpcDisconnectedException)
                {
                    ipcReportFromViewerToCatalog = null;
                }
                catch (Exception error)
                {
                    Program.AlertError(error);
                }
            }
        }

        enum SelfLastAccessTimeUpdateMode { None, Reserve, Force }

        string TryUpdateLastAccessTimeAfterLoaderDisposed = null;

        private void SendSorMessageToCatalogForm(string archiveFilePath, string sorPath, int page, bool requestToUpdateLastAccessTime, SelfLastAccessTimeUpdateMode m)
        {
            SendSorMessageToCatalogForm(new string[] { archiveFilePath }, new string[] { sorPath }, new int[] { page }, new bool[] { requestToUpdateLastAccessTime }, m);
        }

        private void SendSorMessageToCatalogForm(IReadOnlyList<string> archiveFilePath, IReadOnlyList<string> sorPath, IReadOnlyList<int> page, IReadOnlyList<bool> requestToUpdateLastAccessTime, SelfLastAccessTimeUpdateMode m)
        {
            Program.ShowCursor();

            if (ipcReportFromViewerToCatalog != null)
            {
                try
                {
                    var count = archiveFilePath.Count;
                    var reps = new IpcReportFromViewerToCatalog.OldAndNewString[count];
                    for (var i = 0; i < count; i++)
                    {
                        var rep = new IpcReportFromViewerToCatalog.OldAndNewString();
                        rep.New = rep.Old = archiveFilePath[i];
                        rep.Sor = sorPath[i];
                        rep.NewPage = page[i];
                        rep.RequestToUpdateLastAccessTime = requestToUpdateLastAccessTime[i];
                        reps[i] = rep;
                    }

                    SendReportsToCatalog(reps);
                }
                catch (IpcDisconnectedException)
                {
                    ipcReportFromViewerToCatalog = null;
                }
                catch (Exception error)
                {
                    Program.AlertError(error);
                }
            }
            else if (m != SelfLastAccessTimeUpdateMode.None)
            {
                try
                {
                    var count = archiveFilePath.Count;
                    for (var i = 0; i < count; i++)
                    {
                        if (requestToUpdateLastAccessTime[i])
                        {
                            if (m == SelfLastAccessTimeUpdateMode.Reserve)
                            {
                                TryUpdateLastAccessTimeAfterLoaderDisposed = archiveFilePath[i];
                                return;
                            }
                            else
                            {
                                DisposeLoader(); // 読み込みジョブが取り出し中でないときに（V1）
                                Program.TryUpdateLastAccessTime(archiveFilePath[i]);
                            }
                        }
                    }
                }
                catch (Exception error)
                {
                    Program.AlertError(error);
                }
            }
        }

        // カタログへの報告（V7）。
        // 選択変更の依頼（ページ送りのたび）は UI スレッドで同期のプロセス間通信をしないように、最新の 1 件だけを背景で送る。
        // それ以外の報告（リネーム・履歴・タグ）は従来どおり同期で送るが、先にまだ送っていない選択変更の依頼を送って順番を保つ
        private readonly object reportToCatalog_SendLocker = new object(); // プロセス間通信の呼び出しを直列にする

        private readonly object reportToCatalog_PendingLocker = new object(); // 下の 2 つを守る（短時間だけ取る）

        private IpcReportFromViewerToCatalog.OldAndNewString reportToCatalog_PendingSelection = null;

        private bool reportToCatalog_SenderRunning = false;

        /// <summary>報告を同期で送る。IpcDisconnectedException などは呼び出し側で処理する</summary>
        private void SendReportsToCatalog(params IpcReportFromViewerToCatalog.OldAndNewString[] reports)
        {
            var ipc = ipcReportFromViewerToCatalog;
            if (ipc == null) return;
            lock (reportToCatalog_SendLocker)
            {
                IpcReportFromViewerToCatalog.OldAndNewString pending;
                lock (reportToCatalog_PendingLocker)
                {
                    pending = reportToCatalog_PendingSelection;
                    reportToCatalog_PendingSelection = null;
                }
                if (pending != null) reports = new[] { pending }.Concat(reports).ToArray();
                ipc.AddReports(reports);
            }
        }

        /// <summary>まだ送っていない選択変更の依頼を同期で送る（閉じるとき）</summary>
        private void FlushReportsToCatalog()
        {
            try
            {
                SendReportsToCatalog();
            }
            catch (IpcDisconnectedException)
            {
                ipcReportFromViewerToCatalog = null;
            }
            catch { }
        }

        private void SendRequestToChangeSelectionAsync(IpcReportFromViewerToCatalog.OldAndNewString rep)
        {
            var ipc = ipcReportFromViewerToCatalog;
            lock (reportToCatalog_PendingLocker)
            {
                reportToCatalog_PendingSelection = rep; // 古い依頼は送らずに捨てる（最新のページだけでよい）
                if (reportToCatalog_SenderRunning) return;
                reportToCatalog_SenderRunning = true;
            }
            Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        lock (reportToCatalog_SendLocker)
                        {
                            IpcReportFromViewerToCatalog.OldAndNewString r;
                            lock (reportToCatalog_PendingLocker)
                            {
                                r = reportToCatalog_PendingSelection;
                                reportToCatalog_PendingSelection = null;
                                if (r == null)
                                {
                                    reportToCatalog_SenderRunning = false;
                                    return;
                                }
                            }
                            ipc.AddReports(new[] { r });
                        }
                    }
                }
                catch (Exception error)
                {
                    lock (reportToCatalog_PendingLocker)
                    {
                        reportToCatalog_PendingSelection = null;
                        reportToCatalog_SenderRunning = false;
                    }
                    if (error is IpcDisconnectedException)
                    {
                        if (ipcReportFromViewerToCatalog == ipc) ipcReportFromViewerToCatalog = null;
                    }
                    else
                    {
                        try { BeginInvoke((MethodInvoker)(() => Program.AlertError(error))); } catch { }
                    }
                }
            });
        }

        private void SendRequestToChangeSelection(string path, SortMode? viewerFormSortMode = null)
        {
            if (path != null && ipcReportFromViewerToCatalog != null && ipcLookAheadInfo == null && Visible)
            {
                try
                {
                    var rep = new IpcReportFromViewerToCatalog.OldAndNewString();
                    rep.New = path;
                    rep.RequestToChangeSelection = true;
                    rep.ViewerFormSortMode = viewerFormSortMode ?? (currentSortModeDetails == null ? SortMode.NameInAsc : currentSortModeDetails.SortMode);
                    SendRequestToChangeSelectionAsync(rep);
                }
                catch (IpcDisconnectedException)
                {
                    ipcReportFromViewerToCatalog = null;
                }
                catch (Exception error)
                {
                    Program.AlertError(error);
                }

            }
        }

        private void SetPageDirectionToFileName(bool delete = false)
        {

            var prevArchiveFilePath = getCurrentRenameTarget();
            if (string.IsNullOrEmpty(prevArchiveFilePath)) return;
            var zipPlaInfo = new ZipPlaInfo(prevArchiveFilePath);
            var binding = BindingMode;
            zipPlaInfo.BindingModeForSet = delete ? null : (BindingMode?)binding;
            var newArchiveFilePath = zipPlaInfo.GetPathOfCurrentInfo(isDirSafety(prevArchiveFilePath));

            Program.ShowCursor();
            if (MessageForm.Show(this,
                (delete ? Message.WillClearPageSequenceSetting : binding == BindingMode.LeftToRight ? Message.RenameForPageSequenceLeftToRight :
                binding == BindingMode.RightToLeft ? Message.RenameForPageSequenceRightToLeft : Message.RenameForPageSequenceSinglePage) + "\r\n\r\n" +
                Message.BeforeRename + ":\r\n  " + Path.GetFileName(prevArchiveFilePath) + "\r\n" +
                Message.AfterRename + ":\r\n  " + Path.GetFileName(newArchiveFilePath) + "\r\n", Message.Question, Message.OK, Message.Cancel, MessageBoxIcon.Question)
                != 0)
            {
                return;
            }

            RenameCurrentArchiveAndReopen(prevArchiveFilePath, newArchiveFilePath);
        }

        private Rectangle GetCoverRectangleForDisplay(Point? clientPoint = null)
        {
            GetCoverInfo_ClientPoint = clientPoint ?? pbView.PointToClient(Cursor.Position);
            var result = GetCoverInfo(1);
            return result != null ? (Rectangle)result : Rectangle.Empty;
        }

        private Tuple<int, double> GetThumbnailInfoAsTuple(Point? clientPoint = null)
        {
            GetCoverInfo_ClientPoint = clientPoint ?? pbView.PointToClient(Cursor.Position);
            return (Tuple<int, double>)GetCoverInfo(2);
        }

        Point GetCoverInfo_ClientPoint;

        private object GetCoverInfo(int getMode)
        {
            var totalWidth = 0;
            var nextPage = NextPage;
            if (currentPage < 0 || nextPage <= currentPage) return null;
            var pageCount = nextPage - currentPage;
            var Boundaries = new int[pageCount + 1];
            for (var i = nextPage - 1; i >= currentPage; i--)
            {
                var img = ResizedImageArray[i];
                if (img == null)
                {
                    return null;
                }
                Boundaries[pageCount - 1 - (i - currentPage)] = totalWidth;
                totalWidth += img.Width;
            }
            Boundaries[pageCount] = totalWidth;

            var pageLeftToRight = BindingMode == BindingMode.LeftToRight;

            if (pageLeftToRight)
            {
                var Boundaries2 = new int[pageCount + 1];
                for (var i = 0; i <= pageCount; i++)
                {
                    Boundaries2[i] = totalWidth - Boundaries[pageCount - i];
                }
                Boundaries = Boundaries2;
            }

            var pbViewSize = pbView.Size;

            int offsetX;//, offsetY;
            if (pageCount > 1 || !(currentPage == 0 || nextPage == ResizedImageArray.Length) ||
               !(BindingMode == BindingMode.LeftToRight || BindingMode == BindingMode.RightToLeft) || totalWidth > pbViewSize.Width / 2)
            {
                if (pageCount == 2 && ResizedImageArray[currentPage].Width <= pbViewSize.Width / 2 && ResizedImageArray[currentPage + 1].Width <= pbViewSize.Width / 2)
                {
                    offsetX = pbViewSize.Width / 2 - ResizedImageArray[BindingMode == BindingMode.LeftToRight ? currentPage : (currentPage + 1)].Width;
                }
                else
                {
                    offsetX = (pbViewSize.Width - totalWidth) / 2;
                }
            }
            else
            {
                if (BindingMode == BindingMode.LeftToRight ^ currentPage == 0)
                {
                    offsetX = pbViewSize.Width / 2 - totalWidth;
                }
                else
                {
                    offsetX = pbViewSize.Width / 2;
                }
            }


            for (var i = 0; i <= pageCount; i++)
            {
                Boundaries[i] += offsetX;
            }
            
            var clientPoint = GetCoverInfo_ClientPoint;

            var selectedPage = currentPage + pageCount - 1;
            for (var i = 1; i < pageCount; i++)
            {
                if (clientPoint.X < Boundaries[i]) break;
                selectedPage = currentPage + pageCount - i - 1;
            }

            var relativePhysicalIndex = pageCount - 1 - (selectedPage - currentPage);

            if (pageLeftToRight)
            {
                selectedPage = currentPage + pageCount - 1 - (selectedPage - currentPage);
            }

            int offsetX2 = Boundaries[relativePhysicalIndex];
            var totalHeight = ResizedImageArray[selectedPage].Height;
            int offsetY2 = (pbViewSize.Height - totalHeight) / 2;

            const double widthPerHeight = 0.7071067811865475244;

            // 原点：画像の左上、スケール：画面表示
            int selectedImageWidth = Boundaries[relativePhysicalIndex + 1] - Boundaries[relativePhysicalIndex];
            double onImageX, onImageY, onImageWidth, onImageHeight;
            if (selectedImageWidth >= totalHeight * widthPerHeight)
            {
                onImageWidth = totalHeight * widthPerHeight;
                onImageHeight = totalHeight;
                onImageX = Math.Max(0, Math.Min(selectedImageWidth - onImageWidth, clientPoint.X - offsetX2 - onImageWidth / 2.0));
                onImageY = 0;
            }
            else
            {
                onImageWidth = selectedImageWidth;
                onImageHeight = selectedImageWidth / widthPerHeight;
                onImageX = 0;
                onImageY = Math.Max(0, Math.Min(totalHeight - onImageHeight, clientPoint.Y - offsetY2 - onImageHeight / 2.0));
            }


            switch (getMode)
            {
                case 1: // 表示上の四角形
                    return new Rectangle((int)(offsetX2 + onImageX + 0.5), (int)(offsetY2 + onImageY + 0.5), (int)(onImageWidth + 0.5), (int)(onImageHeight + 0.5));

                case 2: // 標準的な左上基準の割合文字列
                    var value = Math.Min(0.999, onImageWidth < selectedImageWidth ?
                        (onImageWidth < selectedImageWidth ? onImageX / (selectedImageWidth - onImageWidth) : 0) :
                        (onImageHeight < totalHeight ? onImageY / (totalHeight - onImageHeight) : 0)); // ToString は四捨五入（もしくは銀行丸め）を行う
                    return Tuple.Create(selectedPage, value);
            }

            return null;
        }
    }
}
