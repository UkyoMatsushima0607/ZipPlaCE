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
    public class ViewControl : Control, ISupportInitialize
    {

        public ViewControl() : base()
        {
            // ダブルバッファリングによる描画の高速化
            
            // ZipPla CE: ダブルバッファは使わない。ViewerForm.pbView_Paint が、重ねて描くものがなければキャンバスをウィンドウに直接出し、
            // あれば BufferedGraphics に描いてから出す（ちらつかない）
            SetStyle(ControlStyles.DoubleBuffer, false);
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            
            SetStyle(ControlStyles.Opaque, true);

            RaiseSharedBufferLimit();
        }

        private static bool sharedBufferLimitRaised = false;

        /// <summary>
        /// WinForms のダブルバッファは共有のバッファを使い回すが、その上限（BufferedGraphicsManager.Current.MaximumBuffer）の既定は 225x96 しかない。
        /// これより大きいコントロールは描くたびに一時のバッファ（ウィンドウと同じ大きさの DIB）を作って捨てるので、
        /// ビューアではページ送り・拡大鏡の移動・アニメーションのコマごとに 1080p で約 8MB、4K で約 33MB の確保と解放をしていた。
        /// 上限をいちばん大きい画面の大きさまで上げ、共有のバッファを使い回させる（ビューアのプロセスだけ。バッファは描いた大きさの分だけ持ち続ける）
        /// </summary>
        private static void RaiseSharedBufferLimit()
        {
            if (sharedBufferLimitRaised) return;
            sharedBufferLimitRaised = true;
            try
            {
                var width = 0;
                var height = 0;
                foreach (var screen in Screen.AllScreens)
                {
                    width = Math.Max(width, screen.Bounds.Width);
                    height = Math.Max(height, screen.Bounds.Height);
                }
                var context = BufferedGraphicsManager.Current;
                var current = context.MaximumBuffer;
                if (width > 0 && height > 0 && (long)width * height > (long)current.Width * current.Height)
                {
                    context.MaximumBuffer = new Size(Math.Max(width, current.Width), Math.Max(height, current.Height));
                }
            }
            catch { }
        }


#region Paint 中にスレッドの譲渡が起こっても Paint が二重で動作することがないようにする実装
        bool invalidating = false;
        bool painting = false;
        bool reserveInvalidate = false;
        bool reserveInvalidateWithChildren = false;
        public new void Invalidate(bool invalidateChildren)
        {
            if (painting)
            {
                reserveInvalidate = true;
                reserveInvalidateWithChildren = reserveInvalidateWithChildren && invalidateChildren;
                return;
            }
            if (invalidating) return;

            invalidating = true;
            base.Invalidate(invalidateChildren);
        }
        public new void Invalidate()
        {
            Invalidate(false);
        }


        protected override void OnPaint(PaintEventArgs e)
        {
            // ここで引っかかった場合一瞬画面が暗くなるが
            // 画像を表示するだけならダブルバッファリングによりそのようなことはほとんど起こらない
            if (painting)
            {
                reserveInvalidate = true;
                return;
            }

            painting = true;

            // 描画中に例外が出ても painting を戻さないと、以後の描画がすべて止まる
            try
            {
                base.OnPaint(e);
            }
            finally
            {
                painting = false;
                invalidating = false;
            }

            if (reserveInvalidate)
            {
                reserveInvalidate = false;
                var invalidateChildren = reserveInvalidateWithChildren;
                reserveInvalidateWithChildren = false;
                Invalidate(invalidateChildren);
            }
        }
#endregion

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
           //base.OnPaintBackground(pevent);
        }


#region ISupportInitialize
        //private bool initializing = false;
        public void BeginInit()
        {
            //initializing = true;
        }

        public void EndInit()
        {
            //initializing = false;
        }
#endregion
    }
}
