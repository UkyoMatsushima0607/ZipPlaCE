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
        // tmWatchIpc_Tick が実行中か・実行中に呼ばれたか（UI スレッドだけで読み書きする）
        private bool tmWatchIpc_Tick_Running;
        private bool tmWatchIpc_Tick_Pending;
        private EventArgs tmWatchIpc_Tick_PendingArgs;

        private ViewerFormImageFilter initialViewerFormImageFilter = null;

        private bool saveToHistory = false;

        private SearchManager searchManagerFromCatalogForm = null;

        private InitialFullscreenMode? requestedInitialFullscreenMode = null;

        private bool? requestedAlwaysHideUI = null;

        private bool tmWatchIpc_Stopped = false;

        // 待機ビューアは、カタログが依頼を書いたときに立つ名前付きイベントで起きる（V6）。
        // 以前は 10ms ごとにプロセス間通信で依頼の有無を読みに行っていた。タイマーは、カタログの終了の検出（IpcDisconnectedException）と
        // 取りこぼしの保険として 500ms ごとに残す。イベントが使えないときは従来どおり 10ms ごと
        // 待機中（依頼を待っている）か。依頼はカタログから名前付きパイプで届き、RequestArrived で起こされる（P3。以前は名前付きイベント、V6）
        private bool tmWatchIpc_Waiting;

        // 待機中に依頼（先読みを含む）が届いたか（Y20）。届いたら起動直後のメモリの返却（ScheduleWaitingMemoryTrim）をしない
        private volatile bool waitingRequestArrived;

        private Action tmWatchIpc_RequestArrivedHandler;

        private void StartWatchIpcWakeEvent()
        {
            if (ipcLookAheadInfo == null) return;
            tmWatchIpc_RequestArrivedHandler = () =>
            {
                waitingRequestArrived = true; // Y20
                try
                {
                    if (tmWatchIpc_Stopped || IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)(() => tmWatchIpc_Tick(this, EventArgs.Empty))); // e が null でないこと（カタログからの依頼として扱う）
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            };
            ipcLookAheadInfo.RequestArrived += tmWatchIpc_RequestArrivedHandler;
            tmWatchIpc_Waiting = true;
            tmWatchIpc.Interval = 500;
            tmMemoryReducer.Interval = 500;
            // Shown より前に届いた依頼は tmWatchIpc_Tick が無視するので、Shown の直後に一度見る
            Shown += (sender, e) => tmWatchIpc_Tick(sender, EventArgs.Empty);
        }

        private void StopWatchIpcWakeEvent()
        {
            var info = ipcLookAheadInfo;
            if (info != null && tmWatchIpc_RequestArrivedHandler != null) info.RequestArrived -= tmWatchIpc_RequestArrivedHandler;
            tmWatchIpc_RequestArrivedHandler = null;
            tmWatchIpc_Waiting = false;
            tmMemoryReducer.Interval = 100; // 表示を始めたら通常の間隔に戻す
        }

        // 依頼の処理の途中で表示を待つ（await）ので、そのあいだに次の Tick が来ても重ねて実行しない。
        // 以前は Monitor.TryEnter で排他していたが、UI スレッドの Monitor は再入でき、await で抜けた時点でロックも外れていたので排他になっていなかった
        private async void tmWatchIpc_Tick(object sender, EventArgs e)
        {
            if (!ViewerForm_Shown_Called) return;

            if (tmWatchIpc_Stopped) return; // Timer.Stop() が完全にリアルタイムには起こらない可能性を考慮

            if (tmWatchIpc_Tick_Running)
            {
                // 前の呼び出しが await で待っている間に来た。いまは飛ばし、前の呼び出しが終わったらもう一度見る
                tmWatchIpc_Tick_Pending = true;
                tmWatchIpc_Tick_PendingArgs = e;
                return;
            }
            tmWatchIpc_Tick_Running = true;
            try
            {
                await tmWatchIpc_TickCore(e);
                while (tmWatchIpc_Tick_Pending && !tmWatchIpc_Stopped)
                {
                    tmWatchIpc_Tick_Pending = false;
                    await tmWatchIpc_TickCore(tmWatchIpc_Tick_PendingArgs);
                }
            }
            finally
            {
                tmWatchIpc_Tick_Running = false;
            }
        }

        // e が null のときは起動直後の呼び出し、null でなければタイマーかカタログからの依頼
        private async Task tmWatchIpc_TickCore(EventArgs e)
        {
            try
            {
                if (!ipcLookAheadInfo.Accept)
                {

                    var coverSetting = ipcLookAheadInfo.Message == IpcLookAheadInfo.MessageEnum.CoverSetting;
                    
                    var show = ipcLookAheadInfo.Message == IpcLookAheadInfo.MessageEnum.Show || coverSetting;
                    
                    var path = ipcLookAheadInfo.Path;
                    var page = ipcLookAheadInfo.Page;
                    var asia = ipcLookAheadInfo.ArchivesInArchive;
                    var rm = ipcLookAheadInfo.ReadOnMemoryMode;
                    var bind = ipcLookAheadInfo.DefaultBinding;
                    var coverBind = ipcLookAheadInfo.CoverBinding;
                    var ignFilter = ipcLookAheadInfo.IgnoreSavedFilerSetting;
                    var sort = ipcLookAheadInfo.SortModeDetails;
                    var fString = ipcLookAheadInfo.FilterString;
                    var fString2 = fString == null ? null : ipcLookAheadInfo.FilterStringWithoutAlias;
                    var color = ipcLookAheadInfo.BackColor;
                    var alwaysHideUI = ipcLookAheadInfo.AlwaysHideUI;
                    var iniFull = ipcLookAheadInfo.InitialFullscreenMode;

                    ipcLookAheadInfo.Accept = true; // これをすると通信が切られるので先にやってはいけない
                    if (show)
                    {
                        tmWatchIpc_Stopped = true;
                        tmWatchIpc.Stop();
                        StopWatchIpcWakeEvent();
                        //tmMemoryReducer.Enabled = false; // Stop() と同じ
                    }

                    var needToReload = false;
                    if (CommandLineOptionInfoForLookAhead != null)
                    {
                        var time = Configuration.GetLastWriteTime();
                        var reloadConfig = time != LoadSettings_LoadedFileTimeStampForLookAhead;
                        if (
                            requestedInitialFullscreenMode != iniFull && !(iniFull == InitialFullscreenMode.ForceFullscreen && FullScreen_pseudoFullScreen || iniFull == InitialFullscreenMode.ForceWindow && !FullScreen_pseudoFullScreen) ||
                            alwaysHideUI != requestedAlwaysHideUI || reloadConfig
                            )
                        {
                            LoadSettings_LoadedFileTimeStampForLookAhead_Setter = time;

                            {
                                var pSize = FullScreen_pseudoFullScreen ? Screen.FromControl(this).Bounds.Size :
                                    PseudoMaximized ? PseudoMaximizedSize : ViewerForm_SizeChanged_pbBoundsMemoryForMinimize.Size;

                                CommandLineOptionInfoForLookAhead.DefaultBindingMode = requestedBindingMode;
                                CommandLineOptionInfoForLookAhead.BackColor = pbView.BackColor;
                                CommandLineOptionInfoForLookAhead.ArchivesInArchiveMode = ArchivesInArchiveMode;
                                CommandLineOptionInfoForLookAhead.CoverBindingMode = CoverBindingMode;
                                CommandLineOptionInfoForLookAhead.InitialFullscreenMode = iniFull;
                                CommandLineOptionInfoForLookAhead.AlwaysHideUI = alwaysHideUI;
                                
                                LoadSettingsAndSetMessages(CommandLineOptionInfoForLookAhead, reloadConfig, reloadConfig);

                                var newSize = FullScreen_pseudoFullScreen ? Screen.FromControl(this).Bounds.Size :
                                    PseudoMaximized ? PseudoMaximizedSize : ViewerForm_SizeChanged_pbBoundsMemoryForMinimize.Size;

                                if (pSize != newSize)
                                {
                                    needToReload = true;
                                }
                            }
                        }
                    }

                    requestedAlwaysHideUI = alwaysHideUI;
                    requestedInitialFullscreenMode = iniFull;

                    if (show)
                    {
                        ipcLookAheadInfo = null;
                        LoadSettings_LastLoadedConfig = null;
                        LoadSettings_LastLoadedGeneralConfig = null;
                    }

                    bool secondTerm = false;

                    do
                    {
                        var newOpen = false;
                        try
                        {
                            Invoke((MethodInvoker)(() =>
                            {

                                var filterChanged = fString != requestedFilterString || fString2 != requestedFilterStringWithoutAlias;

                                // 送る側が sort の変化をチェックしないので受け側でもチェックしない
                                if (!secondTerm && (path != requestedImagePath || page != requestedImagePage || asia != ArchivesInArchiveMode ||/*requestedArchivesInArchiveMode*/
                                    rm != ReadOnMemoryMode || bind != requestedBindingMode ||
                                    ignFilter != requestedIgnoreSavedFilerSetting || filterChanged ||
                                    coverBind != CoverBindingMode /*requestedCoverBindingMode*/ || color != pbView.BackColor))
                                {

                                    var prevExists = !string.IsNullOrEmpty(currentArchiveFilePath); // こっちは shownImegesPath は不適切
                                    if (show && prevExists)
                                    {
                                        shownImegesPath = null;
                                    }

                                    if (ignFilter)
                                    {
                                        imageFilter.Set(null);
                                    }
                                    else if (requestedIgnoreSavedFilerSetting == true && initialViewerFormImageFilter != null)
                                    {
                                        imageFilter.Set(initialViewerFormImageFilter);
                                    }

                                    if (fString2 != requestedFilterStringWithoutAlias)
                                    {
                                        try
                                        {
                                            if (fString2 == null)
                                            {
                                                searchManagerFromCatalogForm = null;
                                            }
                                            else
                                            {
                                                searchManagerFromCatalogForm = new SearchManager(fString2);
                                            }
                                        }
                                        catch
                                        {
                                            searchManagerFromCatalogForm = null;
                                        }
                                    }

                                    var load = false;
                                    // ページ指定でページだけが変わってるパターン
                                    // 現状これはほぼ起こり得ないのでコメントアウト

                                    var sortChanged = filterChanged ? true : null as bool?;

                                    // パスが変わっているが親が変わっていないパターン
                                    if (/*!OpenFileFinishedButStartingIsNotCalled && */!load && page < 0 && page == requestedImagePage && bind == requestedBindingMode)
                                    {
                                        if (sortChanged == null) sortChanged = sort as object == null ? requestedSortModeDetails as object != null : !sort.QuickEquals(requestedSortModeDetails);
                                        if(sortChanged == false)
                                        {
                                            var a = nextData?.EntriesPaths ?? EntryLongPathArray;
                                            if (a != null)
                                            {
                                                var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
                                                // 画像ファイルのパターン
                                                if (altPos < 0)
                                                {
                                                    var p = path.LastIndexOf(Path.DirectorySeparatorChar);
                                                    if (p >= 0)
                                                    {
                                                        var pg = Array.IndexOf(a, path.Substring(0, p) + Path.AltDirectorySeparatorChar + path.Substring(p + 1));
                                                        if (pg >= 0 && pg <= mtbPage.Maximum)
                                                        {
                                                            mtbPage.Value = pg;
                                                            currentStartingFilePath = path;
                                                            load = true;
                                                        }
                                                    }
                                                }

                                                // アーカイブ内のパターン
                                                else
                                                {
                                                    var pg = Array.IndexOf(a, path);
                                                    if (pg >= 0 && pg <= mtbPage.Maximum)
                                                    {
                                                        mtbPage.Value = pg;
                                                        load = true;
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    var fromCatalogFormEvent = e != null;

                                    if (fromCatalogFormEvent && pbView.BackColor != color)
                                    {
                                        pbView.BackColor = color;
                                    }


                                    if (fromCatalogFormEvent)
                                    {
                                        requestedBindingMode = bind;
                                        ArchivesInArchiveMode = asia;
                                        ReadOnMemoryMode = rm;
                                        requestedFilterString = fString;
                                        requestedFilterStringWithoutAlias = fString2;
                                        requestedSortModeDetails = sort;
                                    }

                                    OpenResult openResult = default(OpenResult);
                                    if (!load)
                                    {
                                        openResult = OpenFile(path, page, sort, forLookAhead: true);//, bind);
                                        newOpen = true;
                                        needToReload = false;
                                    }

                                    requestedImagePath = path;
                                    requestedImagePage = page;

                                    if (fromCatalogFormEvent)
                                    {
                                        onePageModeForNext = currentPage == 0 && coverBind == CoverBindingMode.ForceSingle;

                                        //coverBind

                                        //requestedArchivesInArchiveMode = asia; // OpenFile の前に
                                        //requestedBindingMode = bind; // OpenFile の前に
                                        //requestedCoverBindingMode = coverBind;
                                        CoverBindingMode = coverBind;
                                        requestedIgnoreSavedFilerSetting = ignFilter;
                                    }

                                    // show で無くて OpenFile に失敗した場合次回も実行するように
                                    if (!show && !load && openResult == OpenResult.NoOperation)
                                    {
                                        requestedImagePath = null;
                                    }

                                    if (show && prevExists)
                                    {
                                        secondTerm = true;
                                        return;
                                    }
                                    else
                                    {
                                        secondTerm = false;
                                    }
                                }
                                else
                                {
                                    if (!secondTerm)
                                    {
                                        OpenFile_UserOpen = true;
                                    }
                                    else
                                    {
                                        secondTerm = false;
                                    }
                                }

                                if (needToReload)
                                {
                                    Reload();
                                }

                                if (show)
                                {
                                    if (coverSetting)
                                    {
                                        pbView_Paint_RectangleMessage = null;
                                        ViewerMode = ViewerModeEnum.CoverSetting;
                                        ExitAfterCoverSetting = true;
                                        openFileToolStripMenuItem.Enabled = false;
                                        openNewThumbnailWindowToolStripMenuItem.Enabled = false;
                                        switchToThumbnailWindowToolStripMenuItem.Enabled = false;
                                        cloneTheWindowToolStripMenuItem.Enabled = false;
                                        slideshowToolStripMenuItem.Enabled = false;
                                        btnOpenLeft.Enabled = false;
                                        btnOpenRight.Enabled = false;
                                        editToolStripMenuItem.Enabled = false;
                                    }
                                    
                                    lock (loaderLock) loader?.Release();
                                    nextData?.Loader.Release();

                                    var tempPrevSize = prevSize;
                                    BeginUpdate(pbView);
                                    if (FullScreen_pseudoFullScreen)
                                    {

                                        var temp = pbView_SizeChanged_SizeChanging;
                                        pbView_SizeChanged_SizeChanging = true;// WindowState = FormWindowState.Maximized;// MessageBox.Show("a");
                                        Visible = true; //  FormBorderStyle = FormBorderStyle.None; より前に実行しないとタスクバーにボタンが現れない
                                        FormBorderStyle = FormBorderStyle.None;
                                        pbView_SizeChanged_SizeChanging = temp;
                                        WindowState = FormWindowState.Maximized;


                                        // 最初にこうすることでと表示のズレを回避。なお、それでリロードがかからないのでサイズがかわっているわけではない。
                                        //pbView.Top = Top;
                                        pbView.Top = 0;
                                        pbView.Height = Height;
                                        //pbView.Left = Left; // シングルディスプレイでは 0 が出ていたので問題がないと勘違いしていたが明らかに誤り
                                        pbView.Left = 0;
                                        pbView.Width = Width;

                                        fullScreenToolStripMenuItem.Text = Message.Window;

                                        FullScreen_pseudoFullScreen = false;

                                        menuStrip_Bottom = menuStrip.Bounds.Bottom;
                                        mtbPageTop_As_Reference_pbViewBottom = pnlSeekbar.Bounds.Top - pbView.Bottom;

                                        MyActivate(finallyTopMost: true);


                                        prevSize = tempPrevSize;


                                        SetCurrentImageLongPathRequired = true;
                                    }
                                    else
                                    {

                                        if (PseudoMaximized)
                                        {
                                            PseudoMaximized = false;
                                            WindowState = FormWindowState.Maximized;
                                            prevSize = tempPrevSize;
                                        }

                                        MyActivate(finallyTopMost: false);
                                        

                                        SetCurrentImageLongPath();

                                    }

                                    // 表示直後からキーボードショートカットを作動させるため
                                    // Hidden -> Visible = true の場合にのみ必要
                                    Task focus = null;
                                    focus = new Task(() =>
                                    {
                                        try
                                        {
                                            Invoke(((MethodInvoker)(() =>
                                            {
                                                pbView.Focus();
                                            })));
                                        }
                                        catch (ObjectDisposedException) { }
                                        focus.Dispose();
                                    });
                                    focus.Start();
                                    
                                    EndUpdate(pbView);

                                    try
                                    {
                                        AddHistoryInBackground(path, Math.Max(0, page)); // V26
                                        SendSorMessageToCatalogForm(path, Program.HistorySorPath, Math.Max(0, page), requestToUpdateLastAccessTime: false, m: SelfLastAccessTimeUpdateMode.None);
                                    }
                                    catch { }
                                    saveToHistory = true;
                                }
                            }));
                        }
                        catch (ObjectDisposedException) { }
                        if (secondTerm && show && newOpen)
                        {
                            var leftTime = 10 * 1000;
                            do
                            {
                                await Task.Delay(10);
                                leftTime -= 10;
                                if (leftTime <= 0)
                                {
                                    break;
                                }
                            }
                            while (path != shownImegesPath || currentPage != shownImegesPage);
                        }
                    }
                    while (secondTerm);
                }
            }
#if DEBUG
            catch (IpcDisconnectedException ex)
            {
                MessageBox.Show(ex.ToString());
#else
            catch (IpcDisconnectedException)
            {
#endif
                tmWatchIpc_Stopped = true;
                tmWatchIpc.Stop();
                StopWatchIpcWakeEvent();
                try
                {
                    Invoke((MethodInvoker)(() =>
                    {
                        Application.Exit();
                    }));
                }
                catch (ObjectDisposedException) { }

            }
        }
    }
}
