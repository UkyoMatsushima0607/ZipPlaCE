using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZipPla
{
    public static class LockManager
    {
#if DEBUG
        public static void Test()
        {
            var obj = new object();

            lock(obj)
            {
                lock(obj)
                {
                }
            }
            System.Windows.Forms.MessageBox.Show("test1 ok");

            var token1 = false;
            var token2 = false;
            Monitor.Enter(obj, ref token1);
            Monitor.Enter(obj, ref token2);
            if (token2) Monitor.Exit(obj);
            if (token1) Monitor.Exit(obj);

            System.Windows.Forms.MessageBox.Show($"test2 ok token1 = {token1}, token2 = {token2}");

            lock (obj)
            {
                token2 = false;
                Monitor.Enter(obj, ref token2);
                if (token2) Monitor.Exit(obj);
            }
            System.Windows.Forms.MessageBox.Show($"test3 ok  token2 = {token2}");

            token1 = false;
            Monitor.Enter(obj, ref token1);
            lock(obj)
            {

            }
            if (token1) Monitor.Exit(obj);
            System.Windows.Forms.MessageBox.Show($"test4 ok  token1 = {token1}");

        }
#endif

        /// <summary>
        /// list[index] の項目のロックを取って body を呼ぶ（項目が null ならロックなしで null を渡す）。
        /// 配列のロックは項目の参照を取る間だけ持ち、項目のロックは配列のロックを外してから取る。取った後に
        /// 配列の項目が差し替わっていたらやり直す（V30。以前は配列のロックを持ったまま項目のロックを待ち、
        /// UI が縮小中のページを破棄しようとして待つ間、ほかの全ページの読み込みが止まっていた）
        /// </summary>
        public static void ForIReadOnlyList<T>(IReadOnlyList<T> list, int index, Action<T> body) where T : class
        {
            while (true)
            {
                T item;
                lock (list)
                {
                    item = list[index];
                }
                if (item == null)
                {
                    body(null);
                    return;
                }
                var itemLockWasTaken = false;
                try
                {
                    Monitor.Enter(item, ref itemLockWasTaken);
                    // 待っている間に差し替えられた（破棄されて null になった）なら、ロックを外して取り直す
                    if (!ReferenceEquals(list[index], item)) continue;
                    body(item);
                    return;
                }
                finally
                {
                    if (itemLockWasTaken) Monitor.Exit(item);
                }
            }
        }
    }
}
