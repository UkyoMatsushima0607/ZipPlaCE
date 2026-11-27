using Alteridem.WinTouch;
using Common;
using GenerarClasses;
using SharpCompress.Common;
//using SharpCompress.Reader;
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
    public partial class CatalogForm
    {
        //private DateTime CheckAndRecoverWaitingProcess_INIModified;
        bool CheckAndRecoverWaitingProcess(bool forceDisposing = false, bool forcePrepare = false)
        {
            // 待ちプロセスがユーザーなどに消される可能性を考慮
            var hasExited = waitingProcess == null || waitingProcess.HasExited;
            //var iniLastWriteTime = Configuration.GetLastWriteTime();

            if (
                (hasExited/* || iniLastWriteTime > CheckAndRecoverWaitingProcess_INIModified*/)
                || forceDisposing || (forcePrepare && lookAheadInfo == null)
                )
            {
                if (!hasExited)
                {
                    try
                    {
                        waitingProcess.Kill();
                    }
                    catch (InvalidOperationException) { }
                }

                PrepareLookAheadProcess(/*iniLastWriteTime,*/ forcePrepare);
                return true;
            }
            return false;
        }

        private SortModeDetails GetCurrentSortModeDetails(int page)
        {
            {
                return new SortModeDetails { SortMode = sortSyncMode == SortSyncMode.Directory ? sortMode : SortMode.NameInAsc,
                    PreSortMode = prevSortMode, RandomSeed = GetSortArray_RandomSeed };
            }
        }

        private string SetLookAheadArchive_PrevPath = null;

        private int SetLookAheadArchive_PrevPage = -1;

        private ArchivesInArchiveMode? SetLookAheadArchive_ArchivesInArchive = null;

        private ReadOnMemoryMode? SetLookAheadArchive_ReadOnMemory = null;

        private BindingMode? SetLookAheadArchive_PrevDefaultBinding = null;

        private SortModeDetails SetLookAheadArchive_PrevSortDetails = null;

        private IpcLookAheadInfo SetLookAheadArchive_PrevIpcLookAheadInfo = null;

        private Color? SetLookAheadArchive_PrevBackColor = null;

        private CoverBindingMode? SetLookAheadArchive_PrevCoverBindingMode = null;

        private bool? SetLookAheadArchive_PrevOpenInPrevImageFilter = null;

        private InitialFullscreenMode? SetLookAheadArchive_PrevOpenInPrevInitialFullscreenMode = null;

        private bool? SetLookAheadArchive_PrevOpenInPrevAlwaysHideUI = null;

        private bool? SetLookAheadArchive_SendFilterString = null;

        private string SetLookAheadArchive_BlackPrefix = null;

        void SetLookAheadArchive(string path, int page)
        {
            if (!AllowReadAheadProcess) return;
            if (path == null) return;
            if (!InDirectoryVirtualDirectoryOrSmartDirectory && !InArchive) return;
            if (IsOpendByExternalApplication(path)) return;
            //if (GPSizeThumbnail.TrySet_Creating.Contains(path)) return;
            CheckAndRecoverWaitingProcess();

            if (SetLookAheadArchive_BlackPrefix != null && path.StartsWith(SetLookAheadArchive_BlackPrefix)) return;

            if (lookAheadInfo?.Accept != true)
            {
                SetLookAheadArchive_PrevDataIndex = -1;
                return;
            }

            var currentSortModeDetails = null as SortModeDetails;

            if (SetLookAheadArchive_PrevPath != path || SetLookAheadArchive_PrevPage != page ||
                SetLookAheadArchive_ArchivesInArchive != ArchivesInArchiveMode ||
                SetLookAheadArchive_ReadOnMemory != ReadOnMemoryMode ||
                SetLookAheadArchive_PrevDefaultBinding != currentBindingMode ||
                SetLookAheadArchive_PrevBackColor != BackColorOfBuiltInViewer ||
                SetLookAheadArchive_PrevCoverBindingMode != CoverBindingMode ||
                SetLookAheadArchive_PrevOpenInPrevImageFilter != OpenInPreviousImageFilterSetting ||
                SetLookAheadArchive_SendFilterString != SendFilterStringToBuiltInViewer ||
                !(currentSortModeDetails = GetCurrentSortModeDetails(page)).ProbablyEquals(SetLookAheadArchive_PrevSortDetails) ||
                SetLookAheadArchive_PrevOpenInPrevInitialFullscreenMode != BuiltInViewerInitialFullscreenMode ||
                SetLookAheadArchive_PrevOpenInPrevAlwaysHideUI != AlwaysHideUI ||
                SetLookAheadArchive_PrevIpcLookAheadInfo != lookAheadInfo)
            {
                lookAheadInfo.Path = path;
                lookAheadInfo.Page = page;
                lookAheadInfo.ArchivesInArchive = ArchivesInArchiveMode;
                lookAheadInfo.ReadOnMemoryMode = ReadOnMemoryMode;
                lookAheadInfo.DefaultBinding = currentBindingMode;
                lookAheadInfo.CoverBinding = CoverBindingMode;
                lookAheadInfo.IgnoreSavedFilerSetting = !OpenInPreviousImageFilterSetting;
                lookAheadInfo.BackColor = BackColorOfBuiltInViewer;
                lookAheadInfo.SortModeDetails = currentSortModeDetails ?? (currentSortModeDetails = GetCurrentSortModeDetails(page));
                lookAheadInfo.InitialFullscreenMode = BuiltInViewerInitialFullscreenMode;
                lookAheadInfo.AlwaysHideUI = AlwaysHideUI;
                setSendFilterToLookAheadInfo();
                lookAheadInfo.Accept = false;

                SetLookAheadArchive_PrevPath = path;
                SetLookAheadArchive_PrevPage = page;
                SetLookAheadArchive_ArchivesInArchive = ArchivesInArchiveMode;
                SetLookAheadArchive_ReadOnMemory = ReadOnMemoryMode;
                SetLookAheadArchive_PrevDefaultBinding = currentBindingMode;
                SetLookAheadArchive_PrevCoverBindingMode = CoverBindingMode;
                SetLookAheadArchive_PrevOpenInPrevImageFilter = OpenInPreviousImageFilterSetting;
                SetLookAheadArchive_SendFilterString = SendFilterStringToBuiltInViewer;
                SetLookAheadArchive_PrevBackColor = BackColorOfBuiltInViewer;
                SetLookAheadArchive_PrevSortDetails = currentSortModeDetails;
                SetLookAheadArchive_PrevOpenInPrevInitialFullscreenMode = BuiltInViewerInitialFullscreenMode;
                SetLookAheadArchive_PrevIpcLookAheadInfo = lookAheadInfo;
                OpenWaitingProcess_AfterCalled = false;
            }
        }

        private void setSendFilterToLookAheadInfo()
        {
            if (SendFilterStringToBuiltInViewer)
            {
                var filterString = cbFilter.Text;
                if (string.IsNullOrEmpty(filterString))
                {
                    lookAheadInfo.FilterString = null;
                    lookAheadInfo.FilterStringWithoutAlias = null;
                }
                else
                {
                    lookAheadInfo.FilterString = filterString;
                    string filterStringWithoutAlias;
                    if (aliasesToFilteringStrings != null && aliasesToFilteringStrings.TryGetValue(filterString, out filterStringWithoutAlias))
                    {
                        lookAheadInfo.FilterStringWithoutAlias = filterStringWithoutAlias;
                    }
                    else
                    {
                        lookAheadInfo.FilterStringWithoutAlias = filterString;
                    }
                }
            }
            else
            {
                lookAheadInfo.FilterString = null;
                lookAheadInfo.FilterStringWithoutAlias = null;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        //private static readonly TimeSpan DoubleClickTimeSpan = TimeSpan.FromMilliseconds(SystemInformation.DoubleClickTime);
        int OpenWaitingProcess_LastOpenningTime;

        string OpenWaitingProcess_LastOpenningPath;

        bool OpenWaitingProcess_AfterCalled = false;

        bool OpenWaitingProcess_Opening = false;

        bool OpenWaitingProcess_Processing = false;

        bool OpenWaitingProcess_StopPrepareLookAheadProcess = false;

        async void OpenWaitingProcess(string path, int page, bool coverSettingMode, bool fromOtherProcess = false)
        {
            var now = Environment.TickCount;
            if (OpenWaitingProcess_LastOpenningPath == path && InDoubleClickTime(now, OpenWaitingProcess_LastOpenningTime)) return;
            OpenWaitingProcess_LastOpenningTime = now;
            OpenWaitingProcess_LastOpenningPath = path;
            if (OpenWaitingProcess_Processing) return;
            OpenWaitingProcess_Processing = true;
            try
            {
                var existingProcess = !CheckAndRecoverWaitingProcess(forcePrepare: true);
                Cursor.Current = Cursors.WaitCursor;
                if (existingProcess && !lookAheadInfo.Accept)
                {
                    // 応答がなければすぐに置き換える
                    // 起動直後の読み込み処理が無駄になる
                    //PrepareLookAheadProcess();
                    //existingProcess = false;


                    // プロセスが停止しない限り待つ
                    Cursor.Current = Cursors.WaitCursor;
                    while (true)
                    {
                        var wp = waitingProcess;
                        var exit = await Task.Run(() =>
                        {
                            try
                            {
                                return wp.WaitForExit(50);
                            }
                            catch
                            {
                                return true; // エラーは終了したとみなす
                            }
                        });
                        if (exit || lookAheadInfo == null) // 非同期メソッドなので途中で lookAheadInfo が書き換わる可能性がある
                        {
                            PrepareLookAheadProcess();
                            existingProcess = false;
                            break;
                        }
                        else if (lookAheadInfo.Accept)
                        {
                            break;
                        }
                    }
                }
                if (existingProcess) setWaitingProcessPriority(ProcessPriorityClass.Normal);
                lookAheadInfo.Path = path;
                lookAheadInfo.Page = page;
                lookAheadInfo.ArchivesInArchive = ArchivesInArchiveMode;
                lookAheadInfo.ReadOnMemoryMode = ReadOnMemoryMode;
                lookAheadInfo.DefaultBinding = currentBindingMode;
                lookAheadInfo.CoverBinding = CoverBindingMode;
                lookAheadInfo.IgnoreSavedFilerSetting = !OpenInPreviousImageFilterSetting;
                lookAheadInfo.BackColor = BackColorOfBuiltInViewer;
                lookAheadInfo.InitialFullscreenMode = BuiltInViewerInitialFullscreenMode;
                lookAheadInfo.AlwaysHideUI = AlwaysHideUI;
                var sortModeDetails = GetCurrentSortModeDetails(page);
                lookAheadInfo.SortModeDetails = sortModeDetails;
                setSendFilterToLookAheadInfo();
                if (fromOtherProcess)
                {
                    // エクスプローラーなどから開いた本は、カタログで開いているフォルダとは関係がないので、
                    // カタログの並べ替えと絞り込みを送らない（単独で起動したときと同じにする）（S7）
                    lookAheadInfo.SortModeDetails = null;
                    lookAheadInfo.FilterString = null;
                    lookAheadInfo.FilterStringWithoutAlias = null;
                }
                lookAheadInfo.Message = coverSettingMode ? IpcLookAheadInfo.MessageEnum.CoverSetting : IpcLookAheadInfo.MessageEnum.Show;
                if (waitingProcess != null) AllowSetForegroundWindow(waitingProcess.Id);
                lookAheadInfo.Accept = false;
                var existingProcessAndFirstRequest = existingProcess || SetLookAheadArchive_PrevPath != path;
                var waitTime = 0;
                do
                {
                    OpenWaitingProcess_Opening = true;
                    //await Task.Run(() => Thread.Sleep(10));
                    await Task.Delay(10);
                    waitTime += 10;

                    // 初の呼び出しでない場合、ViewerForm.OpenFile 中は応答できないが
                    // それをもってプロセスを停止するべきではない
                    // 初の呼び出しで応答がないということは別のファイルを開こうとしているので
                    // 直ちにプロセスを停止する
                    bool processExited;
                    try
                    {
                        processExited = waitingProcess == null || waitingProcess.HasExited;
                    }
                    catch
                    {
                        processExited = true;
                    }
                    if (processExited || waitTime > 10000)
                    {
                        //Program.AlertError(new Exception(Message.ViewerProcessStopsResponding)); // 原因が明らかなのでプログラムの再起動は選択肢に入れない。
                        if (!processExited || !StopLookAheadProcessByProgram)
                        {
                            MessageBox.Show(Message.BuiltInViewerProcessStopsResponding, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        StopLookAheadProcessByProgram = false;
                        break;
                    }
                    if (lookAheadInfo == null)// 非同期メソッドなので途中で lookAheadInfo が書き換わる可能性がある。ただし実際にはファイルの名前を変更しようとした場合などなので警告は出さない
                    {
                        break;
                    }
                }
                while (!lookAheadInfo.Accept);
                OpenWaitingProcess_Opening = false;

                Cursor.Current = Cursors.Default;

                if (!OpenWaitingProcess_StopPrepareLookAheadProcess && AllowReadAheadProcess)
                {
                    PrepareLookAheadProcess(/*Configuration.GetLastWriteTime()*/);
                }
                else
                {
                    if (lookAheadInfo != null)
                    {
                        currentIpcChanne?.Unregister(lookAheadInfo);
                        lookAheadInfo = null;
                    }
                    waitingProcess = null;
                }
                OpenWaitingProcess_StopPrepareLookAheadProcess = false;

                // 多くの場合に開いたファイル自体を再度先読みすることになりメリットがあまりない上、サムネイル用のリネームの妨げにもなる
                // こうすることで、一旦別のファイルの先読みをしなければ今開いたファイルの先読みは行われない
                // ただし単一のファイルの場合は永遠に先読みされなくなるので
                // After called の状態で Activate になれば元に戻す
                SetLookAheadArchive_PrevIpcLookAheadInfo = lookAheadInfo;
                SetLookAheadArchive_PrevPath = path;
                OpenWaitingProcess_AfterCalled = true;
                var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
                if (altPos >= 0)
                {
                    SetLookAheadArchive_BlackPrefix = path.Substring(0, altPos);
                }
            }
            finally
            {
                OpenWaitingProcess_Processing = false;
            }
        }

        private void CatalogForm_Activated(object sender, EventArgs e)
        {
            SetLookAheadArchive_BlackPrefix = null;

            // 単一の場合に先読みされなくなる問題の回避
            if (OpenWaitingProcess_AfterCalled)
            {
                OpenWaitingProcess_AfterCalled = false;
                SetLookAheadArchive_PrevIpcLookAheadInfo = null;
                SetLookAheadArchive_PrevPath = null;

                SetLookAheadArchive_PrevDataIndex = -1;
            }

            // ポインティングなしデバイスへの配慮としてアクティブ化直後はマスクしない
            unselectedItemMaskOff();


            if (BookmarkConfigModifiedDateTime > DateTime.MinValue)
            {
                try
                {
                    //if (File.Exists(Configuration.XmlPath))
                    if (Configuration.ConfigFileExists())
                    {
                        //if (BookmarkConfigModifiedDateTime < File.GetLastWriteTime(Configuration.XmlPath))
                        if (BookmarkConfigModifiedDateTime < Configuration.GetLastWriteTimeWithoutFileCheckWithException())
                        {
                            loadBookmarkFromConfig(reload: true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            AllowFullpower = true;
        }

        private void CatalogForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            CleanupOnClose();
        }

        private bool CleanupOnClose_Done = false;

        /// <summary>
        /// カタログを閉じるときの後始末。FormClosed と、読み込み中に閉じたときの予約した終了（Environment.Exit の直前、X3）の両方から呼ばれる
        /// </summary>
        private void CleanupOnClose()
        {
            if (CleanupOnClose_Done) return;
            CleanupOnClose_Done = true;
            GPSizeThumbnail.FlushPendingWrites(10000); // 後回しにしたサムネイルキャッシュの書き込みを済ませる（K7 (a)）
            VirtualFolder.WaitPendingReplacements(); // 背景の .sor の書き換えを済ませる（K23）
            openRequestServer?.Dispose(); // ほかのプロセスからの依頼を受け付けない（S7）
            openRequestServer = null;
            if (currentIpcChanne != null)
            {
                if (lookAheadInfo != null)
                {
                    currentIpcChanne?.Unregister(lookAheadInfo);
                    lookAheadInfo = null;
                }
                currentIpcChanne.Dispose();
                currentIpcChanne = null;
            }
            // 待ちプロセスは自分で消滅するためこちらから消す必要はない
        }

        private void tvCatalog_MouseMove(object sender, MouseEventArgs e)
        {
            var dataIndex = tvCatalog.PointToIndex(e.Location);
            SetLookAheadArchive(dataIndex);
            tvCatalog_MouseMove_RatingChangePart();// (dataIndex);
        }

        private void dgvFileList_MouseMove(object sender, MouseEventArgs e)
        {
            var showIndex = dgvFileList.HitTest(e.X, e.Y).RowIndex;
            if (showIndex < 0) return;
            var s2d = tvCatalog.ShowIndexToDataIndex;
            if (showIndex >= s2d.Length) return;
            var dataIndex = s2d[showIndex];
            SetLookAheadArchive(dataIndex);
        }

        private int SetLookAheadArchive_PrevDataIndex = -1;

        private void SetLookAheadArchive(int dataIndex)
        {
            if (!InDirectoryVirtualDirectoryOrSmartDirectory && !InArchive) return;
            if (OpenWaitingProcess_Opening) return;
            if (dataIndex < 0) return;
            if (dataIndex == SetLookAheadArchive_PrevDataIndex) return;
            if (CreatingZipPathArray) return;
            var aa = ApplicationArray;
            if (aa == null) return;
            var bp = aa.FirstOrDefault(a => a != null && a is BuiltInViewerProvider);
            if (bp == default(ApplicationProvider)) return;
            var count = tvCatalog.Count;

            var lr = LoadResultArray;
            if (lr == null || lr.Length != count) return;
            var lrd = lr[dataIndex];
            //if (lrd == LoadResult.LoadError || lrd == LoadResult.FileNotFound || thumbnailCache == GPSizeThumbnail.AlternateDataStream && lrd == LoadResult.NotYet) return;
            if (lrd == LoadResult.LoadError || lrd == LoadResult.FileNotFound) return;

            if (dataIndex >= count) return;
            var zpa = ZipPathArray; if (zpa == null || zpa.Length != count) return;
            var zna = ZipNameArray; if (zna == null || zna.Length != count) return;
            var path = zpa[dataIndex];
            var isDir = zna[dataIndex].EndsWith(Path.DirectorySeparatorChar.ToString());
            if (!bp.IsDefaultFor(path, isDir) && !bp.Supports(path, isDir)) return;

            // 代替データストリームの保存を妨げないように
            if (InDirectoryVirtualDirectoryOrSmartDirectory && lrd == LoadResult.NotYet && thumbnailCache == GPSizeThumbnail.AlternateDataStream && (
                isDir && SubfolderIsFullDisplayed() /*SubfolderMode == SubfolderMode.Display*/ || !isDir && ArchiveLoadLevel == ItemLoadLevel.Full
                )) return;

            SetLookAheadArchive_PrevDataIndex = dataIndex;

            var altSepPos = path.IndexOf(Path.AltDirectorySeparatorChar);
            if (altSepPos >= 0)
            {
                SetLookAheadArchive(path, -1);
                //SetLookAheadArchive(path.Substring(0, altSepPos), dataIndex);
            }
            else
            {
                var zga = ZipPageArray; if (zpa == null || zpa.Length != count) return;
                SetLookAheadArchive(path, zga[dataIndex]);
            }

        }

        private void tmWatchIpc_Tick(object sender, EventArgs e)
        {
            var startingGuid = loadingGuid;

            IpcReportFromViewerToCatalog.OldAndNewString[] tmWatchIpc_Tick_GotData = null;
            try
            {
                tmWatchIpc_Tick_GotData = reportFromViewerToCatalog.TakeReports();
            }
            catch { }

            if (tmWatchIpc_Tick_GotData != null)
            {
                // この関数はサブスレッドで実行されるため新たなスレッドの生成は不要
                string location = null;
                int[] d2s = null;
                var reloaded = false;

                for (var j = tmWatchIpc_Tick_GotData.Length - 1; j >= 0; j--)
                {
                    var newold = tmWatchIpc_Tick_GotData[j];
                    if (newold == null) continue;
                    if (newold.RequestToChangeSelection && (ItemSelectionFeedback == ItemSelectionFeedback.Always ||
                        ItemSelectionFeedback == ItemSelectionFeedback.IfSortMayBeSame && sortMode == newold.ViewerFormSortMode && IsNonvolatileSort()))
                    {
                        var path = newold.New;
                        var zpa = ZipPathArray;
                        var s2d = tvCatalog.ShowIndexToDataIndex;
                        if (path != null && zpa != null && s2d != null)
                        {
                            // できるだけ丁寧に無駄な走査をしないようチェック
                            if (InMovie) break;
                            var altPos = path.IndexOf(Path.AltDirectorySeparatorChar);
                            if (altPos >= 0)
                            {
                                if (InDirectoryVirtualDirectoryOrSmartDirectory) break;
                                var parent = path.Substring(0, altPos).ToLower();
                                var cd = currentLocation?.ToLower();
                                if (string.IsNullOrEmpty(cd)) break;
                                var last = cd.Last();
                                if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
                                {
                                    cd = cd.Substring(0, cd.Length - 1);
                                }
                                if (parent != cd) break;
                            }
                            else
                            {
                                if (InArchive) break;
                                if (!(InSmartDirectory || InVirtualDirectory))
                                {
                                    string parent;
                                    try
                                    {
                                        parent = Path.GetDirectoryName(path).ToLower();

                                        // ルートの場合に必要な処理
                                        if (parent[parent.Length - 1] == Path.DirectorySeparatorChar)
                                        {
                                            parent = parent.Substring(0, parent.Length - 1);
                                        }
                                    }
                                    catch
                                    {
                                        break;
                                    }
                                    var cd = currentLocation?.ToLower();
                                    if (string.IsNullOrEmpty(cd)) break;
                                    var last = cd.Last();
                                    if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
                                    {
                                        cd = cd.Substring(0, cd.Length - 1);
                                    }
                                    if (parent != cd) break;
                                }
                            }

                            var count = zpa.Length;
                            var index = -1;
                            var showIndex = -1;
                            foreach (var i in s2d)
                            {
                                showIndex++;
                                if (i < 0 || i >= count) break;
                                else if (path == zpa[i])
                                {
                                    index = i;
                                    break;
                                }
                            }
                            if (index >= 0)
                            {
                                tvCatalog.SelectedIndex = index;
                                tvCatalog.FocusedIndex = index;
                                tvCatalog.ScrollBarToIndexWithMinimalMove(showIndex);
                                // 詳細表示は自動的に連動する
                            }
                        }
                        break;
                    }
                }
                foreach (var newold in tmWatchIpc_Tick_GotData)
                {
                    if (newold == null) continue;
                    if (newold.RequestToReloadTags)
                    {
                        try
                        {
                            Invoke(new MethodInvoker(() =>
                            {
                                setTagFilteringMenuItems(needToRepairTagsFromBookmark: false, needToSaveCurrentTag: false);
                            }));
                        }
                        catch (ObjectDisposedException) { }

                        continue;
                    }
                    if (newold.RequestToChangeSelection)
                    {
                        // 先に実行済み
                        continue;
                    }
                    if (newold.RequestToUpdateLastAccessTime)
                    {
                        var path = newold.New;
                        Task.Run(async () =>
                        {
                            // 待つ間はプールのスレッドを持たない（X13）
                            for (var i = 0; i < 1000; i++)
                            {
                                await Task.Delay(100).ConfigureAwait(false);
                                //GPSizeThumbnail.AddBlackListWeight(path);
                                DateTime? now_;
                                GPSizeThumbnail.EnterEditing(path);
                                try
                                {
                                    now_ = Program.TryUpdateLastAccessTime(path);
                                }
                                finally
                                {
                                    GPSizeThumbnail.ExitedEditing(path);
                                }
                                if (now_ == null) GPSizeThumbnail.Editing(path);
                                else
                                {
                                    var now = (DateTime)now_;
                                    try
                                    {
                                        Invoke(new MethodInvoker(() =>
                                        {
                                            lock (ZipPathArrayLocker)
                                            {
                                                if (ZipPathArray != null)
                                                {
                                                    var index = Array.IndexOf(ZipPathArray, path);
                                                    if (index >= 0)
                                                    {
                                                        LastAccessTimeArray[index] = now;
                                                        var cul = Message.CurrentLanguage;
                                                        var ds = tvCatalog.DataIndexToShowIndex;
                                                        if (ds != null && ds.Length > index)
                                                        {
                                                            var showIndex = ds[index];
                                                            if (showIndex >= 0)
                                                            {
                                                                setFileListText(showIndex, tbcAccessTime, getShortTimeString(now, cul));

                                                                if (sortMode == SortMode.AccessedInAsc || sortMode == SortMode.AccessedInDesc)
                                                                {
                                                                    var s2d = tvCatalog.ShowIndexToDataIndex;
                                                                    var sign = sortMode == SortMode.AccessedInAsc ? 1 : -1;
                                                                    if (showIndex > 0 && showIndex - 1 < s2d.Length && sign * DateTime.Compare(LastAccessTimeArray[s2d[showIndex - 1]], now) > 0 ||
                                                                    showIndex + 1 < s2d.Length && sign * DateTime.Compare(now, LastAccessTimeArray[s2d[showIndex + 1]]) > 0)
                                                                    {
                                                                        tbcAccessTime.HeaderCell.SortGlyphDirection = SortOrder.None;
                                                                        cbSortBy.SelectedIndex = -1;
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }

                                        }));
                                    }
                                    catch (ObjectDisposedException) { }
                                    return;
                                }
                            }
                        });
                    }
                    if (reloaded) continue;
                    if (newold.Sor == null)
                    {
                        // 子の変更がかかった場合。実フォルダフォルダ監視に先を越されて意味がない
                        if (!CreatingZipPathArray && (InVirtualDirectory || InSmartDirectory))
                        {
                            //Thread.Sleep(5 * 1000);
                            ReloadOneThumbnailForSubThread(newold.Old, newold.New, startingGuid);
                            swapPathAndNameInSorBookmarkUndoBuff(newold.Old, newold.New, needToSaveBookmark: true);
                        }

                        // 親の変更がかかった場合
                        if (!CreatingZipPathArray && currentLocation?.ToLower() == newold.Old?.ToLower())
                        {
                            try
                            {
                                var newPath = newold.New;
                                Invoke(new MethodInvoker(() =>
                                {
                                    if (!CreatingZipPathArray && currentLocation?.ToLower() == newold.Old?.ToLower()) // 二重チェック
                                    {
                                        UpdatePreview(renamedLocation: newPath);
                                    }
                                }));
                            }
                            catch (ObjectDisposedException) { }
                        }

                    }
                    else
                    {
                        if (location == null)
                        {
                            location = currentLocation?.ToLower();
                        }
                        var currentSor = newold.Sor.ToLower() == location;
                        if (currentSor || (LoadLastViewedPageFromHistory && InDirectoryVirtualDirectoryOrSmartDirectory))
                        {
                            try
                            {
                                Invoke(new MethodInvoker(() =>
                                {
                                    if (IsDisplay(SubfolderMode) || ArchiveLoadLevel != ItemLoadLevel.Ignore)
                                    {
                                        lock (ZipPathArrayLocker)
                                        {
                                            if (ZipPathArray != null)
                                            {
                                                var index = Array.IndexOf(ZipPathArray, newold.New);
                                                if (index >= 0 && index < tvCatalog.Count)
                                                {
                                                    var oldPage = ZipPageArray[index];
                                                    ZipPageArray[index] = newold.NewPage;
                                                    var total = FileCountArray[index];
                                                    if (d2s == null) d2s = tvCatalog.DataIndexToShowIndex;
                                                    var showIndex = d2s == null || d2s.Length <= index ? -1 : d2s[index];
                                                    if (showIndex >= 0 && dgvFileList.Rows.Count > showIndex)
                                                    {
                                                        setFileListText(showIndex, tbcPage, getPageString(newold.NewPage, total));
                                                        if (LoadLastViewedPageFromHistory && oldPage != newold.NewPage)
                                                        {
                                                            tvCatalog[index]?.DrawItem(onlyFrame: false);
                                                        }

                                                    }
                                                }
                                                else
                                                {
                                                    // 見つからなければ再読込する実装
                                                    // 表示設定などで見つからないことは想定されて
                                                    // その場合再読込しても結果は変わらないので不要
                                                }
                                            }
                                        }
                                    }
                                }));
                            }
                            catch (ObjectDisposedException) { }
                        }
                    }
                }
            }

            var p = Cursor.Position;
            var currentMouseOnWindow = Bounds.Contains(p) && !CursorIsJustOnWindow(p);
            if (mouseOnWindow && !currentMouseOnWindow)
            {
                if (!VisibleCobtextMenuStripExists())
                {
                    mouseOnWindow = false;
                    globalMouseLeave(p);
                }
            }
            else if (!mouseOnWindow && currentMouseOnWindow)
            {
                mouseOnWindow = true;
                globalMouseEnter(p);
            }

        }
    }
}
