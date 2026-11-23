using System;
using System.Runtime.InteropServices;

namespace ZipPla
{
    /// <summary>
    /// ZipPla CE: アニメーションを表示している間だけ、タイマーの分解能を 1ms に上げる。
    /// 既定の分解能（約 15.6ms）では Task.Delay がその単位に切り上がり、20ms の GIF のコマが約 31ms ごとになっていた。
    /// Windows 10 2004 以降は timeBeginPeriod の効果は呼んだプロセスだけに及ぶ。Windows 11 では最小化・隠れたウィンドウのプロセスでは OS が効果を外す。
    /// UI スレッドからだけ呼ぶこと。
    /// </summary>
    public static class TimerResolution
    {
        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        private static bool raised;

        public static void SetHighResolution(bool high)
        {
            if (high == raised) return;
            try
            {
                if (high) { if (timeBeginPeriod(1) == 0) raised = true; }
                else { timeEndPeriod(1); raised = false; }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
