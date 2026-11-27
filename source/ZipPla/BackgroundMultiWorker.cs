using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GenerarClasses
{
    /// <summary>
    /// マルチスレッドによる並列処理機能を提供します。
    /// 全てのメソッド呼び出し・プロパティ書き込みは同一のスレッドから行われなければなりません。
    /// </summary>
    class BackgroundMultiWorker : Component
    {
        public class EachDoWorkEventArgs
        {
            public object Argument { get; private set; }
            public object Result { get;  set; }
            public bool Cancel { get; set; }
            public int WorkNumber { get; private set; }
            public int CompletedWorksCount { get; private set; }
            public readonly Guid WorkSetGuid;
            public EachDoWorkEventArgs(DoWorkEventArgs e, int workNumber, int completedWorksCount, Guid workSetGuid)
            {
                Argument = e.Argument;
                Result = e.Result;
                Cancel = e.Cancel;
                WorkNumber = workNumber;
                CompletedWorksCount = completedWorksCount;
                WorkSetGuid = workSetGuid;
            }
            public EachDoWorkEventArgs(object argument, int workNumber, int completedWorksCount, Guid workSetGuid)
            {
                Argument = argument;
                WorkNumber = workNumber;
                CompletedWorksCount = completedWorksCount;
                WorkSetGuid = workSetGuid;
            }
        }
        public class AllRunWorkerCompletedEventArgs
        {
            public IReadOnlyList<object> Results { get; private set; }
            public Exception Error { get; private set; }
            public bool Cancelled { get; private set; }
            public AllRunWorkerCompletedEventArgs(IReadOnlyList<object> results, Exception error, bool cancelled)
            {
                Results = results;
                Error = error;
                Cancelled = cancelled;
            }
        }
        public class EachRunWorkerCompletedEventArgs
        {
            public object Result { get; private set; }
            public Exception Error { get; private set; }
            public bool Cancelled { get; private set; }
            public int WorkNumber { get; private set; }
            public int CompletedWorksCount { get; private set; }
            public readonly Guid WorkSetGuid;
            public EachRunWorkerCompletedEventArgs(object result, Exception error, bool cancelled, int workNumber, int completedWorksCount, Guid workSetGuid)
            {
                Result = result;
                Error = error;
                Cancelled = cancelled;
                WorkNumber = workNumber;
                CompletedWorksCount = completedWorksCount;
                WorkSetGuid = workSetGuid;
            }
            public EachRunWorkerCompletedEventArgs(RunWorkerCompletedEventArgs e, int workNumber, int completedWorksCount, Guid workSetGuid)
            {
                Result = e.Result;
                Error = e.Error;
                Cancelled = e.Cancelled;
                WorkNumber = workNumber;
                CompletedWorksCount = completedWorksCount;
                WorkSetGuid = workSetGuid;
            }
        }
        public class RunWorkerStartingEventArgs
        {
            public RunWorkerStartingEventArgs(IEnumerable arguments)
            {
                Arguments = arguments;
                // 以前は new Guid() で、これは常に Guid.Empty。作業の組ごとに違う値にしないと、受け取る側の世代の確認が働かない
                WorkSetGuid = Guid.NewGuid();
            }
            public readonly IEnumerable Arguments;
            public readonly Guid WorkSetGuid;
        }
        public delegate void RunWorkerStartingEventHandler(object sender, RunWorkerStartingEventArgs e);
        public delegate void EachDoWorkEventHandler(object sender, EachDoWorkEventArgs e);
        public delegate void AllRunWorkerCompletedEventHandler(object sender, AllRunWorkerCompletedEventArgs e);
        public delegate void EachRunWorkerCompletedEventHandler(object sender, EachRunWorkerCompletedEventArgs e);
        
        private DoWorkEventHandler GetDoWorkEventHandler(EachDoWorkEventHandler DoWork)
        {
            return ((sender, e) => {
                var tpl = (Tuple<object, int, int, Guid>)e.Argument;
                var e2 = new EachDoWorkEventArgs(tpl.Item1, tpl.Item2, tpl.Item3, tpl.Item4);
                try
                {
                    DoWork(sender, e2);
                }
                catch (ObjectDisposedException) { }
                e.Result = e2.Result;
                if(e2.Cancel)
                {
                    e.Cancel = true;
                }
            });
        }

        public int ThreadCount
        {
            get
            {
                return InternalThreadCount;
            }
            set
            {
                if (value >= 0)
                {
                    InternalThreadCount = value;
                    if (!CancellationPending && IsBusy && InternalThreadCount > CurrentThreadCount)
                    {
                        if (WorksOrderChanged) NextWork = 0;
                        WorksOrderChanged = false;
                        while (NextWork < WorksOrder.Length && IsStartedOrSkipped(WorksOrder[NextWork])) NextWork++;

                        for (var newThread = CurrentThreadCount; newThread < InternalThreadCount; newThread++)
                        {
                            while (NextWork < WorksOrder.Length && IsStartedOrSkipped(WorksOrder[NextWork])) NextWork++;
                            if (NextWork == WorksOrder.Length) break;

                            BackgroundWorker nextWorker;

                            var WorkNumber = WorksOrder[NextWork++];

                            nextWorker = new BackgroundWorker();
                            AllWorkerCount++;
                            nextWorker.WorkerSupportsCancellation = InternalWorkerSupportsCancellation;
                            nextWorker.DoWork += GetDoWorkEventHandlerDoWork;
                            nextWorker.RunWorkerCompleted += privateRunWorkerCompletedEventHandler;
                            CurrentThreadCount++;

                            Workers.Add(nextWorker);

                            WorkersWorks.Add(WorkNumber);
                            WorksStarted[WorkNumber] = true;

                            nextWorker.RunWorkerAsync(Tuple.Create(Works[WorkNumber], WorkNumber, CompletedWorksCount, WorkSetGuid));
                        }
                    }
                }
                else
                {
                    throw new ArgumentOutOfRangeException("ThreadCount", value, null);
                }
            }
        }
        private int InternalThreadCount = 1;

        public bool WorkerSupportsCancellation
        {
            get
            {
                return InternalWorkerSupportsCancellation;
            }
            set
            {
                InternalWorkerSupportsCancellation = value;
                if (Workers != null)
                {
                    foreach (var worker in Workers)
                    {
                        if (worker != null)
                        {
                            worker.WorkerSupportsCancellation = InternalWorkerSupportsCancellation;
                        }
                    }
                }
            }
        }
        private bool InternalWorkerSupportsCancellation = false;

        public enum WorkState { Waiting, InProgress, Finished, NotExists };
        public WorkState GetWorkState(int workNumber)
        {
            if(WorksStarted == null || workNumber < 0 || WorksStarted.Length <= workNumber)
            {
                return WorkState.NotExists;
            }
            else if(!WorksStarted[workNumber])
            {
                return WorkState.Waiting;
            }
            else if(ResultArgs[workNumber] == null)
            {
                return WorkState.InProgress;
            }
            else
            {
                return WorkState.Finished;
            }
        }

        public bool SelfClosing { get; set; } = true;

        /// <summary>
        /// true を返したジョブはスレッドを起動せず、結果 null で完了したものとして扱います（EachRunWorkerCompleted は発生しません）。
        /// RunWorkerAsync の開始時に評価されるため、実行中に変更した場合は次回の開始から反映されます。
        /// すべてのジョブが対象になる場合は、ワーカーが1つも起動せず完了しなくなるのを避けるためスキップしません。
        /// </summary>
        public Func<int, bool> WorkSkipper { get; set; }
        private Func<int, bool> activeWorkSkipper;

        private bool IsStartedOrSkipped(int workNumber)
        {
            if (WorksStarted[workNumber]) return true;
            if (activeWorkSkipper == null || !activeWorkSkipper(workNumber)) return false;
            WorksStarted[workNumber] = true;
            ResultArgs[workNumber] = new EachRunWorkerCompletedEventArgs(null, null, false, workNumber, ++CompletedWorksCount, WorkSetGuid);
            return true;
        }

        public event EachDoWorkEventHandler DoWork;
        private event DoWorkEventHandler GetDoWorkEventHandlerDoWork;

        public event RunWorkerStartingEventHandler RunWorkerStarting;

        /// <summary>
        /// 全ての処理が終了した後に一度だけ発生します。ただしキャンセルの完了を待たない割り込みを行った場合は発生しません。
        /// </summary>
        public event AllRunWorkerCompletedEventHandler AllRunWorkersCompleted;

        /// <summary>
        /// 各処理が完了後に RunWorkerAsync の引数の IEnumerable の順に発生します。
        /// </summary>
        public event EachRunWorkerCompletedEventHandler EachRunWorkerCompletedSequential;

        /// <summary>
        /// 各処理が終了する毎に、終了した順に発生します。開始された処理について一度ずつ発生します。
        /// </summary>
        public event EachRunWorkerCompletedEventHandler EachRunWorkerCompleted;

        /// <summary>
        /// クラスの管理から外れてから終了した Worker に対して発生します。WorkerNumber は -1 になります
        /// </summary>
        public event EachRunWorkerCompletedEventHandler InconclusiveWorkerDisposing;

        // 読み込みスレッド（最大 4 本）がループの中で読むので volatile にする（自動プロパティでは JIT がループの外に出しうる）
        private volatile bool cancellationPending;
        public bool CancellationPending { get => cancellationPending; private set => cancellationPending = value; }
        
        public BackgroundMultiWorker() { }
        
        /// <summary>
        /// 処理の順番を指定します。処理中でも変更できます。
        /// </summary>
        public void SetWorksOrder(int[] worksOrder)
        {
            WorksOrderChanged = true;
            WorksOrder = worksOrder;
#if DEBUG
            var check = new List<int>(worksOrder);
            check.Sort();
            if (check.Count > 0 && check[0] < 0 ) throw new Exception($"WorksOrder is not permutation. (0->{check[0]})");
            for (var i = 1; i < check.Count; i++)
            {
                if (check[i] == check[i - 1])
                {
                    throw new Exception($"WorksOrder is not permutation. ({i})");
                }
            }
#endif
        }

        private void privateRunWorkerCompletedEventHandler(object sender, RunWorkerCompletedEventArgs e)
        {
            var localWorker = (BackgroundWorker)sender;
            int WorkerNumber;
            if (Workers == null || (WorkerNumber = Workers.IndexOf(localWorker)) < 0)
            {
                if (InconclusiveWorkerDisposing != null)
                {
                    var ee2 = new EachRunWorkerCompletedEventArgs(e.Result, e.Error, false, -1, CompletedWorksCount, Guid.Empty);
                    InconclusiveWorkerDisposing(sender, ee2);
                }
                localWorker.Dispose();
                AllWorkerCount--;
                ExitQueAccepter();
                return;
            }
            var workSetGuid = WorkSetGuid;

            var WorkNumber = WorkersWorks[WorkerNumber];
            var e2 = new EachRunWorkerCompletedEventArgs(e.Result, e.Error, e.Cancelled || CancellationPending, WorkNumber, ++CompletedWorksCount, workSetGuid); // Cancelled は強制的に設定する
            var startedEarly = StartNextWorkBeforeCompletedEvent && TryStartNextWorkEarly(localWorker, WorkerNumber, workSetGuid, e.Error);
            if (EachRunWorkerCompleted != null) EachRunWorkerCompleted(sender, e2);
            if (startedEarly && ResultArgs == null) return; // 念のため: 完了イベントの中で作業セットが片付けられた場合（始めたジョブは InconclusiveWorkerDisposing で処理される）
            ResultArgs[WorkNumber] = e2;
            while (NextResult < ResultArgs.Length && ResultArgs[NextResult] != null)
            {
                if (EachRunWorkerCompletedSequential != null)
                {
                    EachRunWorkerCompletedSequential(sender, ResultArgs[NextResult]);
                }
                NextResult++;
            }
            if (startedEarly) return; // 次のジョブは同じワーカーで始めてある

            if (WorksOrderChanged) NextWork = 0;
            WorksOrderChanged = false;
            while (NextWork < WorksOrder.Length && IsStartedOrSkipped(WorksOrder[NextWork])) NextWork++;

            if (e.Error == null && !CancellationPending && NextWork < WorksOrder.Length)
            {
                if (InternalThreadCount >= CurrentThreadCount)
                {
                    var CurrentThreadCount0 = CurrentThreadCount;
                    for (var newThread = CurrentThreadCount0 - 1; newThread < InternalThreadCount; newThread++)
                    {
                        while (NextWork < WorksOrder.Length && IsStartedOrSkipped(WorksOrder[NextWork])) NextWork++;
                        if (NextWork == WorksOrder.Length) break;


                        BackgroundWorker nextWorker;
                        int nextWorkerNumber;
                        if (newThread < CurrentThreadCount0)
                        {
                            nextWorker = localWorker;
                            nextWorkerNumber = WorkerNumber;
                        }
                        else
                        {
                            nextWorker = new BackgroundWorker();
                            AllWorkerCount++;
                            nextWorker.WorkerSupportsCancellation = InternalWorkerSupportsCancellation;
                            nextWorker.RunWorkerCompleted += privateRunWorkerCompletedEventHandler;
                            nextWorker.DoWork += GetDoWorkEventHandlerDoWork;
                            nextWorkerNumber = newThread;
                            Workers.Add(null);
                            CurrentThreadCount++;
                            WorkersWorks.Add(0);
                        }


                        WorkNumber = WorksOrder[NextWork++];
                        Workers[nextWorkerNumber] = nextWorker;
                        WorkersWorks[nextWorkerNumber] = WorkNumber;
                        WorksStarted[WorkNumber] = true;
                        nextWorker.RunWorkerAsync(Tuple.Create(Works[WorkNumber], WorkNumber, CompletedWorksCount, workSetGuid));
                    }
                   
                }
                else
                {
                    localWorker.Dispose();
                    AllWorkerCount--;
                    ExitQueAccepter();
                    Workers.RemoveAt(WorkerNumber);
                    WorkersWorks.RemoveAt(WorkerNumber);
                    CurrentThreadCount--;
                }
            }
            else
            {
                localWorker.Dispose();
                AllWorkerCount--;
                ExitQueAccepter();
                Workers.RemoveAt(WorkerNumber);
                WorkersWorks.RemoveAt(WorkerNumber);
                CurrentThreadCount--;
                if ((SelfClosing || CancellationPending || e.Error != null) && Workers.Count <= 0)
                {
                    if (AllRunWorkersCompleted != null)
                    {
                        var Results = new object[ResultArgs.Length]; for (var i = 0; i < ResultArgs.Length; i++) Results[i] = ResultArgs[i] != null ? ResultArgs[i].Result : null;
                        var es = new AllRunWorkerCompletedEventArgs(Results, e.Error, CancellationPending);
                        AllRunWorkersCompleted(sender, es);
                    }
                    Workers = null;
                    WorkSetGuid = Guid.Empty;
                    WorkersWorks = null;
                    Works = null;
                    WorksStarted = null;
                    ResultArgs = null;
                    WorksOrder = null;
                    GetDoWorkEventHandlerDoWork = null;
                }
                
                if (WorksQue != null && !IsBusy)
                {
                    var wq = WorksQue;
                    WorksQue = null;
                    RunWorkerAsync(wq);
                }
            }
        }

        /// <summary>
        /// true のとき、SetWorksOrder に一部のジョブだけの順番を渡してよい（含まれないジョブは実行しない）。
        /// false（既定）のときは、開始時に長さが合わない順番を全ジョブの順番に置き換える（従来どおり）。
        /// </summary>
        public bool AllowPartialWorksOrder { get; set; }

        /// <summary>
        /// true のとき、ジョブが終わったら完了イベント（EachRunWorkerCompleted、UI スレッド）を呼ぶ前に次のジョブを始める（V1）。
        /// UI スレッドが完了イベントや描画で忙しくても読み込みが止まらない。
        /// 完了イベントの中で順番を変えた（SetWorksOrder・ReworkOrder）・スレッド数を変えた場合は、次のジョブが終わったときに反映される。
        /// 同じワーカーを使い回すだけで、スレッドを増やす処理は従来どおり完了イベントのあとに行う
        /// </summary>
        public bool StartNextWorkBeforeCompletedEvent { get; set; }

        private bool TryStartNextWorkEarly(BackgroundWorker localWorker, int workerNumber, Guid workSetGuid, Exception error)
        {
            if (error != null || CancellationPending || InternalThreadCount < CurrentThreadCount) return false;
            if (WorksOrderChanged) NextWork = 0;
            WorksOrderChanged = false;
            while (NextWork < WorksOrder.Length && IsStartedOrSkipped(WorksOrder[NextWork])) NextWork++;
            if (NextWork >= WorksOrder.Length) return false;
            var workNumber = WorksOrder[NextWork++];
            Workers[workerNumber] = localWorker;
            WorkersWorks[workerNumber] = workNumber;
            WorksStarted[workNumber] = true;
            localWorker.RunWorkerAsync(Tuple.Create(Works[workNumber], workNumber, CompletedWorksCount, workSetGuid));
            return true;
        }

        public void ReworkOrder(int workNumber)
        {
            if (SelfClosing)
            {
                throw new Exception("ReworkOrder cannot be used when SelfClosing = true.");
            }
            WorksStarted[workNumber] = false;
            WorksOrderChanged = true;
        }

        public int CurrentThreadCount { get; private set; }
        private List<BackgroundWorker> Workers = null;
        private Guid WorkSetGuid = Guid.Empty;
        private List<int> WorkersWorks = null;
        private object[] Works = null;
        private bool[] WorksStarted = null;
        public int[] WorksOrder { get; private set; } = null;
        private bool WorksOrderChanged = false;
        private EachRunWorkerCompletedEventArgs[] ResultArgs = null;
        public int AllWorkerCount { get; private set; } = 0;
        private int NextWork;
        private int NextResult;
        private int CompletedWorksCount;
        public void RunWorkerAsync(IEnumerable<object> arguments)
        {
            if (IsBusy)
            {
                throw new InvalidOperationException();
            }
            if (!SelfClosing && EachRunWorkerCompletedSequential != null)
            {
                throw new Exception("EachRunWorkerCompletedSequential cannot be used when SelfClosing = false.");
            }
            CancellationPending = false;
            if (InternalThreadCount < 1) InternalThreadCount = 1;
            CurrentThreadCount = InternalThreadCount;
            var e = new RunWorkerStartingEventArgs(arguments);
            if (RunWorkerStarting != null) RunWorkerStarting(this, e);
            Workers = new List<BackgroundWorker>();
            var workSetGuid = WorkSetGuid = e.WorkSetGuid;
            WorkersWorks = new List<int>();
            Works = arguments.ToArray();
            WorksStarted = new bool[Works.Length];
            ResultArgs = new EachRunWorkerCompletedEventArgs[Works.Length];
            if (WorksOrder == null || (AllowPartialWorksOrder ? WorksOrder.Any(w => w < 0 || w >= Works.Length) : WorksOrder.Length != Works.Length))
            {
                WorksOrder = new int[Works.Length];
                for (var i = 0; i < Works.Length; i++) WorksOrder[i] = i;
            }
            WorksOrderChanged = false;
            NextWork = 0;
            NextResult = 0;
            CompletedWorksCount = 0;
            GetDoWorkEventHandlerDoWork = GetDoWorkEventHandler(DoWork);
            var skipper = WorkSkipper;
            activeWorkSkipper = null;
            if (skipper != null)
            {
                for (var i = 0; i < Works.Length; i++)
                {
                    if (!skipper(i)) { activeWorkSkipper = skipper; break; }
                }
            }
            for (var initialWork = 0; initialWork < CurrentThreadCount; initialWork++)
            {
                while (NextWork < WorksOrder.Length && IsStartedOrSkipped(WorksOrder[NextWork])) NextWork++;
                if (NextWork == WorksOrder.Length) break;
                var wn = WorksOrder[NextWork++];

                var worker = new BackgroundWorker();
                AllWorkerCount++;
                worker.WorkerSupportsCancellation = InternalWorkerSupportsCancellation;
                worker.DoWork += GetDoWorkEventHandlerDoWork;
                
                RunWorkerCompletedEventHandler runWorkerCompeted = privateRunWorkerCompletedEventHandler;
                

                worker.RunWorkerCompleted += runWorkerCompeted;
                Workers.Add(worker);
                WorkersWorks.Add(wn);
                WorksStarted[wn] = true;
                worker.RunWorkerAsync(Tuple.Create(Works[wn], wn, 0, workSetGuid));
            }
        }

        /// <summary>
        /// ReserveExit で予約した終了で Environment.Exit を呼ぶ直前に起きる。Environment.Exit では FormClosed が呼ばれないので、
        /// 終了前に済ませておく処理（後回しにしたキャッシュの書き込みなど）をここで行う（X3）
        /// </summary>
        public event EventHandler ExitingByReservation;

        private void ExitQueAccepter()
        {
            if (Exit_Que && AllWorkerCount == 0)
            {
                try { ExitingByReservation?.Invoke(this, EventArgs.Empty); } catch { }
                Environment.Exit(Exit_Code);
            }
        }

        /// <summary>
        /// バックグラウンド処理のキャンセルをリクエストします。割り込み待ちもキャンセルされます。
        /// </summary>
        public void CancelAsync()
        {
            if(!InternalWorkerSupportsCancellation)
            {
                throw new InvalidOperationException();
            }
            WorksQue = null;
            CancellationPending = true;
            var WorkerExists = false;
            if (Workers != null)
            {
                foreach (var worker in Workers)
                {
                    if (worker != null)
                    {
                        WorkerExists = true;
                        worker.CancelAsync();
                    }
                }
            }
            if(!WorkerExists)
            {
                Workers = null;
                WorkSetGuid = Guid.Empty;
                WorkersWorks = null;
                Works = null;
                WorksStarted = null;
                ResultArgs = null;
                WorksOrder = null;
                GetDoWorkEventHandlerDoWork = null;
            }
        }

        /// <summary>
        /// バックグラウンド処理中かどうかを取得します。
        /// </summary>
        public bool IsBusy
        {
            get
            {
                return Workers != null;
            }
        }

        private IEnumerable<object> WorksQue = null;

        public void RunWorkerAsync(int workCount)
        {
            RunWorkerAsync(new object[workCount]);
        }

        public void RunWorkerAsyncWithInterrupt(int workCount, bool waitCancel)
        {
            RunWorkerAsyncWithInterrupt(new object[workCount], waitCancel);
        }
        /// <summary>
        /// 実行中の処理があればそれをキャンセルした上で新しい処理を開始します。
        /// </summary>
        public void RunWorkerAsyncWithInterrupt(IEnumerable<object> arguments, bool waitCancel)
        {
            if (IsBusy)
            {
                if (waitCancel)
                {
                    CancelAsync(); // ここで一度 Que が消される。またワーカーがゼロなら即時にキャンセル
                    if(IsBusy) // ワーカーがゼロでない場合
                    {
                        WorksQue = arguments;
                    }
                    else
                    {
                        RunWorkerAsync(arguments);
                    }
                }
                else
                {
                    var w = Workers;
                    Workers = null;
                    WorkSetGuid = Guid.Empty;
                    for (var i = 0; i < w.Count; i++)
                    {
                        w[i].CancelAsync();
                    }
                    WorksQue = null;
                    RunWorkerAsync(arguments);
                }
            }
            else
            {
                RunWorkerAsync(arguments);
            }
        }

        /// <summary>
        /// プログラムの終了を予約します。FormClosing イベントをハンドルし、そこでは終了をキャンセルして Visible = false とし、この関数を呼び出すとプログラムが安全に終了できます。
        /// </summary>
        private bool Exit_Que = false;
        private int Exit_Code = 0;
        public void ReserveExit(int exitCode, out bool realTime)
        {
            if (AllWorkerCount == 0)
            {
                realTime = true;
            }
            else
            {
                Exit_Que = true;
                Exit_Code = exitCode;
                CancelAsync();
                realTime = false;
            }
        }
    }
}
