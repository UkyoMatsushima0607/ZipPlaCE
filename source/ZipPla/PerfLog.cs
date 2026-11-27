using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace ZipPla
{
    /// <summary>
    /// 調査用のログ（PERFLOG 定数を付けたビルドだけ）。exe のフォルダの fileName に 1 行足す。
    /// PERFLOG がなければ呼び出しごと消える（引数も評価されない）。同じファイルへの書き込みはロックで直列にする
    /// （並列に書くと片方が失敗して行が抜ける。V23 の計測で判明）。書けなくても無視する
    /// </summary>
    internal static class PerfLog
    {
        private static readonly Dictionary<string, object> locks = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        [Conditional("PERFLOG")]
        public static void Append(string fileName, string line)
        {
            object fileLock;
            lock (locks)
            {
                if (!locks.TryGetValue(fileName, out fileLock)) locks[fileName] = fileLock = new object();
            }
            try
            {
                lock (fileLock) File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), fileName), line + Environment.NewLine);
            }
            catch { }
        }
    }
}
