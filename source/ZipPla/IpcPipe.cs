using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZipPla
{
    /// <summary>
    /// カタログとビューアのプロセス間通信が切れた（相手が終了した）ことを表す（P3。以前の RemotingException の代わり）
    /// </summary>
    public class IpcDisconnectedException : Exception
    {
        public IpcDisconnectedException() : base("The connection to the other ZipPla process was lost.") { }
        public IpcDisconnectedException(Exception inner) : base("The connection to the other ZipPla process was lost.", inner) { }
    }

    /// <summary>
    /// カタログとビューアの間の名前付きパイプのメッセージ（P3）。1 メッセージは「種類 1 バイト＋本体の長さ 4 バイト＋本体」
    /// </summary>
    internal static class IpcPipeProtocol
    {
        public const byte Hello = 1;     // ビューア → カタログ: 待機ビューアの guid を名乗る
        public const byte Request = 2;   // カタログ → ビューア: 依頼（IpcLookAheadInfo の全項目）
        public const byte Accepted = 3;  // ビューア → カタログ: 依頼を受け取った
        public const byte Reports = 4;   // ビューア → カタログ: 報告（OldAndNewString の配列）

        private const int MaxBody = 64 << 20;

        public static string PipeName(string currentIpcGuid) => "ZipPlaCE_" + IpcLookAheadInfo.ChannelName + "_" + currentIpcGuid;

        public static void Write(Stream stream, object writeLock, byte type, byte[] body)
        {
            var header = new byte[5];
            header[0] = type;
            var length = body?.Length ?? 0;
            header[1] = (byte)length; header[2] = (byte)(length >> 8); header[3] = (byte)(length >> 16); header[4] = (byte)(length >> 24);
            try
            {
                lock (writeLock)
                {
                    stream.Write(header, 0, header.Length);
                    if (length > 0) stream.Write(body, 0, length);
                    stream.Flush();
                }
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                throw new IpcDisconnectedException(e);
            }
        }

        /// <summary>1 つ読む。相手が閉じたら false</summary>
        public static bool TryRead(Stream stream, out byte type, out byte[] body)
        {
            type = 0;
            body = null;
            var header = new byte[5];
            if (!readFully(stream, header, 5)) return false;
            type = header[0];
            var length = header[1] | header[2] << 8 | header[3] << 16 | header[4] << 24;
            if (length < 0 || length > MaxBody) return false;
            body = new byte[length];
            return readFully(stream, body, length);
        }

        private static bool readFully(Stream stream, byte[] buffer, int count)
        {
            try
            {
                return StreamUtil.ReadExactly(stream, buffer, count);
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                return false;
            }
        }

        private static void writeString(BinaryWriter w, string s)
        {
            w.Write(s != null);
            if (s != null) w.Write(s);
        }

        private static string readString(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

        public static byte[] SerializeRequest(IpcLookAheadInfo info)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                writeString(w, info.Path);
                w.Write(info.Page);
                w.Write((int)info.ArchivesInArchive);
                w.Write((int)info.DefaultBinding);
                w.Write((int)info.CoverBinding);
                w.Write(info.BackColor.ToArgb());
                var sort = info.SortModeDetails;
                w.Write(sort != null);
                if (sort != null)
                {
                    w.Write((int)sort.SortMode);
                    w.Write((int)sort.PreSortMode);
                    writeString(w, sort.RandomSeed);
                }
                w.Write(info.IgnoreSavedFilerSetting);
                writeString(w, info.FilterString);
                writeString(w, info.FilterStringWithoutAlias);
                w.Write((int)info.Message);
                w.Write((int)info.InitialFullscreenMode);
                w.Write(info.AlwaysHideUI);
                w.Write((int)info.ReadOnMemoryMode);
                w.Write(info.Generation); // Y18
                w.Flush();
                return ms.ToArray();
            }
        }

        public static void DeserializeRequest(byte[] body, IpcLookAheadInfo info)
        {
            using (var r = new BinaryReader(new MemoryStream(body), Encoding.UTF8))
            {
                info.Path = readString(r);
                info.Page = r.ReadInt32();
                info.ArchivesInArchive = (ArchivesInArchiveMode)r.ReadInt32();
                info.DefaultBinding = (BindingMode)r.ReadInt32();
                info.CoverBinding = (CoverBindingMode)r.ReadInt32();
                info.BackColor = Color.FromArgb(r.ReadInt32());
                if (r.ReadBoolean())
                {
                    info.SortModeDetails = new SortModeDetails
                    {
                        SortMode = (SortMode)r.ReadInt32(),
                        PreSortMode = (SortMode)r.ReadInt32(),
                        RandomSeed = readString(r),
                    };
                }
                else
                {
                    info.SortModeDetails = null;
                }
                info.IgnoreSavedFilerSetting = r.ReadBoolean();
                info.FilterString = readString(r);
                info.FilterStringWithoutAlias = readString(r);
                info.Message = (IpcLookAheadInfo.MessageEnum)r.ReadInt32();
                info.InitialFullscreenMode = (InitialFullscreenMode)r.ReadInt32();
                info.AlwaysHideUI = r.ReadBoolean();
                info.ReadOnMemoryMode = (ReadOnMemoryMode)r.ReadInt32();
                info.Generation = r.BaseStream.Position + 4 <= r.BaseStream.Length ? r.ReadInt32() : 0; // Y18
            }
        }

        public static byte[] SerializeReports(IpcReportFromViewerToCatalog.OldAndNewString[] reports)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(reports.Length);
                foreach (var x in reports)
                {
                    w.Write(x != null);
                    if (x == null) continue;
                    writeString(w, x.Old);
                    writeString(w, x.New);
                    writeString(w, x.Sor);
                    w.Write(x.NewPage);
                    w.Write(x.RequestToUpdateLastAccessTime);
                    w.Write(x.RequestToChangeSelection);
                    w.Write((int)x.ViewerFormSortMode);
                    w.Write(x.RequestToReloadTags);
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        public static IpcReportFromViewerToCatalog.OldAndNewString[] DeserializeReports(byte[] body)
        {
            using (var r = new BinaryReader(new MemoryStream(body), Encoding.UTF8))
            {
                var count = r.ReadInt32();
                var result = new List<IpcReportFromViewerToCatalog.OldAndNewString>(count);
                for (var i = 0; i < count; i++)
                {
                    if (!r.ReadBoolean()) continue;
                    result.Add(new IpcReportFromViewerToCatalog.OldAndNewString
                    {
                        Old = readString(r),
                        New = readString(r),
                        Sor = readString(r),
                        NewPage = r.ReadInt32(),
                        RequestToUpdateLastAccessTime = r.ReadBoolean(),
                        RequestToChangeSelection = r.ReadBoolean(),
                        ViewerFormSortMode = (SortMode)r.ReadInt32(),
                        RequestToReloadTags = r.ReadBoolean(),
                    });
                }
                return result.ToArray();
            }
        }
    }

    /// <summary>
    /// カタログ側の名前付きパイプのサーバー（P3）。ビューアのプロセスごとに 1 本つながる。
    /// 待機ビューアは接続して guid を名乗り（Hello）、その guid の依頼（IpcLookAheadInfo）を受け取る。
    /// 報告（IpcReportFromViewerToCatalog）は、本を開いた後のビューアからも同じ接続で届く
    /// </summary>
    public sealed class CatalogIpcServer : IDisposable
    {
        private sealed class Connection
        {
            public NamedPipeServerStream Stream;
            public readonly object WriteLock = new object();
            public string Guid;
            public IpcLookAheadInfo LastSentInfo; // 最後に送った依頼と世代（Y18）。WriteLock で守る
            public int LastSentGeneration;
        }

        private readonly string pipeName;
        private readonly object locker = new object();
        private readonly List<Connection> connections = new List<Connection>();
        private readonly Dictionary<string, IpcLookAheadInfo> infos = new Dictionary<string, IpcLookAheadInfo>();
        private volatile bool disposed;

        public IpcReportFromViewerToCatalog Reports { get; set; }

        public CatalogIpcServer(string currentIpcGuid)
        {
            pipeName = IpcPipeProtocol.PipeName(currentIpcGuid);
            var thread = new Thread(acceptLoop) { IsBackground = true, Name = "ZipPla IPC server" };
            thread.Start();
        }

        private void acceptLoop()
        {
            while (!disposed)
            {
                NamedPipeServerStream stream;
                try
                {
                    // 同期のハンドルでは、読み込みを待っているスレッドがあると同じハンドルへの書き込みが止まるので、overlapped で開く
                    stream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                }
                catch
                {
                    if (disposed) return;
                    Thread.Sleep(100);
                    continue;
                }
                try
                {
                    stream.WaitForConnection();
                }
                catch
                {
                    stream.Dispose();
                    if (disposed) return;
                    continue;
                }
                if (disposed)
                {
                    stream.Dispose();
                    return;
                }
                var connection = new Connection { Stream = stream };
                lock (locker) connections.Add(connection);
                var reader = new Thread(() => readLoop(connection)) { IsBackground = true, Name = "ZipPla IPC connection" };
                reader.Start();
            }
        }

        private void readLoop(Connection connection)
        {
            try
            {
                while (!disposed && IpcPipeProtocol.TryRead(connection.Stream, out var type, out var body))
                {
                    switch (type)
                    {
                        case IpcPipeProtocol.Hello:
                            {
                                var guid = Encoding.UTF8.GetString(body);
                                IpcLookAheadInfo info;
                                lock (locker)
                                {
                                    connection.Guid = guid;
                                    infos.TryGetValue(guid, out info);
                                }
                                // 接続より前に依頼が書かれていれば、ここで送る
                                if (info != null && !info.AcceptRaw) trySendRequest(connection, info);
                                break;
                            }
                        case IpcPipeProtocol.Accepted:
                            {
                                IpcLookAheadInfo info;
                                lock (locker)
                                {
                                    info = connection.Guid == null ? null : (infos.TryGetValue(connection.Guid, out var i) ? i : null);
                                }
                                // 古い依頼の受け取りの知らせで、後から書いた新しい依頼を受け取り済みにしない（Y18）
                                if (info != null && (body == null || body.Length < 4 || BitConverter.ToInt32(body, 0) == Volatile.Read(ref info.Generation)))
                                {
                                    info.SetAcceptFromRemote(true);
                                }
                                break;
                            }
                        case IpcPipeProtocol.Reports:
                            Reports?.AddReports(IpcPipeProtocol.DeserializeReports(body));
                            break;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                lock (locker) connections.Remove(connection);
                try { connection.Stream.Dispose(); } catch { }
            }
        }

        private static void trySendRequest(Connection connection, IpcLookAheadInfo info)
        {
            try
            {
                lock (connection.WriteLock)
                {
                    // Hello を受け取ったときと Accept を false にしたときが重なっても、同じ依頼を二重に送らない（Y18）
                    var generation = Volatile.Read(ref info.Generation);
                    if (ReferenceEquals(connection.LastSentInfo, info) && connection.LastSentGeneration == generation) return;
                    IpcPipeProtocol.Write(connection.Stream, connection.WriteLock, IpcPipeProtocol.Request, IpcPipeProtocol.SerializeRequest(info));
                    connection.LastSentInfo = info;
                    connection.LastSentGeneration = generation;
                }
            }
            catch (IpcDisconnectedException)
            {
                // 待機ビューアが終了した。カタログ側はプロセスの終了で検出する
            }
        }

        /// <summary>待機ビューアへの依頼を公開する（以前の RemotingServices.Marshal）。Accept を false にすると、つながっていれば送る</summary>
        public void Register(IpcLookAheadInfo info, string guid)
        {
            lock (locker) infos[guid] = info;
            info.RequestSender = sender =>
            {
                Connection connection;
                lock (locker)
                {
                    connection = connections.Find(c => c.Guid == guid);
                }
                if (connection != null) trySendRequest(connection, sender);
                // まだつながっていなければ、Hello を受け取ったときに送る
            };
        }

        /// <summary>依頼の公開をやめる（以前の RemotingServices.Disconnect）。接続は報告のために残す</summary>
        public void Unregister(IpcLookAheadInfo info)
        {
            if (info == null) return;
            info.RequestSender = null;
            lock (locker)
            {
                string key = null;
                foreach (var pair in infos)
                {
                    if (ReferenceEquals(pair.Value, info)) { key = pair.Key; break; }
                }
                if (key != null) infos.Remove(key);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Connection[] all;
            lock (locker)
            {
                all = connections.ToArray();
                connections.Clear();
                infos.Clear();
            }
            foreach (var c in all)
            {
                try { c.Stream.Dispose(); } catch { }
            }
            // 待ち受け中の WaitForConnection を終わらせるため、自分でつないですぐ閉じる
            try
            {
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                {
                    client.Connect(200);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// ビューア側の名前付きパイプのクライアント（P3）。待機ビューアの依頼（IpcLookAheadInfo）と報告（IpcReportFromViewerToCatalog）の代理を作る
    /// </summary>
    public sealed class ViewerIpcClient
    {
        private readonly NamedPipeClientStream stream;
        private readonly object writeLock = new object();
        private volatile bool disconnected;

        public IpcLookAheadInfo LookAheadInfo { get; }
        public IpcReportFromViewerToCatalog Reports { get; }
        public bool Disconnected => disconnected;

        private ViewerIpcClient(NamedPipeClientStream stream, string guid)
        {
            this.stream = stream;
            LookAheadInfo = new IpcLookAheadInfo();
            LookAheadInfo.SetAcceptFromRemote(true); // 依頼が届くまでは「受け取り済み」
            LookAheadInfo.IsDisconnected = () => disconnected;
            LookAheadInfo.AcceptedSender = () => send(IpcPipeProtocol.Accepted, BitConverter.GetBytes(LookAheadInfo.Generation)); // 受け取った依頼の世代を返す（Y18）
            Reports = new IpcReportFromViewerToCatalog();
            Reports.RemoteSender = reports => send(IpcPipeProtocol.Reports, IpcPipeProtocol.SerializeReports(reports));
            send(IpcPipeProtocol.Hello, Encoding.UTF8.GetBytes(guid));
            var reader = new Thread(readLoop) { IsBackground = true, Name = "ZipPla IPC client" };
            reader.Start();
        }

        public static ViewerIpcClient Connect(string currentIpcGuid, string guid, int timeoutMilliseconds = 10000)
        {
            var stream = new NamedPipeClientStream(".", IpcPipeProtocol.PipeName(currentIpcGuid), PipeDirection.InOut, PipeOptions.Asynchronous); // 読み書きを別のスレッドで同時に行うので overlapped で開く
            try
            {
                stream.Connect(timeoutMilliseconds);
                return new ViewerIpcClient(stream, guid);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private void send(byte type, byte[] body)
        {
            if (disconnected) throw new IpcDisconnectedException();
            try
            {
                IpcPipeProtocol.Write(stream, writeLock, type, body);
            }
            catch (IpcDisconnectedException)
            {
                disconnected = true;
                throw;
            }
        }

        private void readLoop()
        {
            try
            {
                while (IpcPipeProtocol.TryRead(stream, out var type, out var body))
                {
                    if (type == IpcPipeProtocol.Request)
                    {
                        IpcPipeProtocol.DeserializeRequest(body, LookAheadInfo);
                        LookAheadInfo.SetAcceptFromRemote(false);
                        LookAheadInfo.RaiseRequestArrived();
                    }
                }
            }
            catch
            {
            }
            finally
            {
                disconnected = true;
                LookAheadInfo.RaiseRequestArrived(); // 待っている側に切断を気づかせる（Accept の getter が例外を出す）
                try { stream.Dispose(); } catch { }
            }
        }
    }
}

namespace ZipPla
{
    /// <summary>
    /// エクスプローラーなどから本を開くとき、起動しているカタログに渡すための受け口（S7）。
    /// 以前は、カタログが起動していても毎回フルのビューアのプロセスを新しく作っていた。
    /// パイプの名前は exe のパス・ユーザー・セッションごと。依頼は「パスの長さ 4 バイト＋UTF-8 のパス」、返事は 1 バイト（1 = 受け付けた）
    /// </summary>
    public static class OpenRequestPipe
    {
        public static string PipeName
        {
            get
            {
                unchecked
                {
                    var key = System.Windows.Forms.Application.ExecutablePath.ToUpperInvariant() + "|" + Environment.UserDomainName + "\\" + Environment.UserName + "|" +
                        System.Diagnostics.Process.GetCurrentProcess().SessionId;
                    var hash = 2166136261u; // FNV-1a
                    foreach (var c in key) hash = (hash ^ c) * 16777619u;
                    return "ZipPlaCE_Open_" + hash.ToString("x8");
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool WaitNamedPipe(string name, int timeout);

        /// <summary>
        /// 起動しているカタログに本を開かせる。受け付けられたら true（呼び出し側はそのまま終了する）。
        /// カタログがない・応答しない・断られたら false（呼び出し側は従来どおり自分で開く）
        /// </summary>

        public static bool TryForward(string path)
        {
            try
            {
                var name = PipeName;
                // パイプがなければ（カタログが起動していなければ）すぐ戻る。NamedPipeClientStream.Connect はパイプがないと時間切れまで待つので使わない
                if (!WaitNamedPipe(@"\\.\pipe\" + name, 200)) return false;
                using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    client.Connect(1000);
                    // 起動元（エクスプローラー）から受け継いだ前面に出る権利を、カタログが開くビューアに渡せるようにする
                    NativeMethods.AllowSetForegroundWindow(-1);
                    var body = Encoding.UTF8.GetBytes(path);
                    var header = BitConverter.GetBytes(body.Length);
                    client.Write(header, 0, 4);
                    client.Write(body, 0, body.Length);
                    client.Flush();
                    var reply = new byte[1];
                    var read = client.ReadAsync(reply, 0, 1);
                    if (!read.Wait(5000) || read.Result != 1) return false;
                    return reply[0] == 1;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>カタログ側の受け口（S7）。依頼は handler（UI スレッドで呼ばれ、受け付けたら true）に渡す</summary>
    public sealed class OpenRequestServer : IDisposable
    {
        private readonly string pipeName = OpenRequestPipe.PipeName;
        private readonly System.Windows.Forms.Control owner;
        private readonly Func<string, bool> handler;
        private volatile bool disposed;

        public OpenRequestServer(System.Windows.Forms.Control owner, Func<string, bool> handler)
        {
            this.owner = owner;
            this.handler = handler;
            new Thread(acceptLoop) { IsBackground = true, Name = "ZipPla open request" }.Start();
        }

        private void acceptLoop()
        {
            while (!disposed)
            {
                NamedPipeServerStream stream;
                try
                {
                    stream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                }
                catch
                {
                    if (disposed) return;
                    Thread.Sleep(500);
                    continue;
                }
                using (stream)
                {
                    try
                    {
                        stream.WaitForConnection();
                        if (disposed) return;
                        handle(stream);
                    }
                    catch
                    {
                        if (disposed) return;
                    }
                }
            }
        }

        private void handle(NamedPipeServerStream stream)
        {
            var header = new byte[4];
            if (!readFully(stream, header, 4)) return;
            var length = BitConverter.ToInt32(header, 0);
            if (length <= 0 || length > 64 * 1024) return;
            var body = new byte[length];
            if (!readFully(stream, body, length)) return;
            var path = Encoding.UTF8.GetString(body);

            // UI スレッドで開かせる。UI が 1 秒以内に応じなければ断る（呼び出し側が自分で開く。遅れて開かないように取り消しの印を付ける）。
            // 以前は 3 秒待ったので、カタログが忙しいと自分で開くより最大 3 秒遅れた（Y19。自分で開くのは 0.3 秒前後）
            var done = new ManualResetEventSlim(false);
            var accepted = false;
            var cancelled = false;
            var gate = new object();
            try
            {
                owner.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                {
                    lock (gate)
                    {
                        if (cancelled) return;
                        try { accepted = handler(path); } catch { accepted = false; }
                        done.Set();
                    }
                }));
            }
            catch
            {
                return;
            }
            if (!done.Wait(1000))
            {
                lock (gate)
                {
                    if (!done.IsSet) cancelled = true;
                }
            }
            var reply = new byte[] { (byte)(accepted && !cancelled ? 1 : 0) };
            try
            {
                stream.Write(reply, 0, 1);
                stream.Flush();
                stream.WaitForPipeDrain();
            }
            catch { }
        }

        private static bool readFully(Stream stream, byte[] buffer, int count)
        {
            var read = 0;
            while (read < count)
            {
                var task = stream.ReadAsync(buffer, read, count - read);
                if (!task.Wait(3000) || task.Result <= 0) return false;
                read += task.Result;
            }
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                {
                    client.Connect(200); // 待ち受けを終わらせる
                }
            }
            catch { }
        }
    }
}
