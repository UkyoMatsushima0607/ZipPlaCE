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
    static class ToolStripDropDownScroller
    {
        /// <summary>
        /// ToolStripDropDown をスクロール可能にします。末端以外に適用する場合は別途 MouseEnter 等の処理が必要です。
        /// </summary>
        /// <param name="tsdd">スクロール可能にする ToolStripDropDown</param>
        public static void Enscrollable(ToolStripDropDown tsdd, bool enterFocus = false)
        {
            Descrollable(tsdd);
            tsdd.MouseWheel += MouseWheelEventHandler;
            tsdd.KeyUp += KeyUpEventHandler;
            tsdd.Opened += OpenedEventHandler;
            if (enterFocus) tsdd.MouseEnter += FocusEventHandler;
        }

        public static void Enscrollable(ToolStripDropDown tsdd, Alteridem.WinTouch.GestureListener gestureListener, bool enterFocus = false)
        {
            Enscrollable(tsdd, enterFocus);
            if (gestureListener != null) gestureListener.Pan += GestureListener_Pan;
        }

        public static Alteridem.WinTouch.GestureListener GetGestureListener(Control control)
        {
            try
            {
                return new Alteridem.WinTouch.GestureListener(control, new Alteridem.WinTouch.GestureConfig[] {
                    //new GestureConfig(3, 1, 0), // ズーム
                    new Alteridem.WinTouch.GestureConfig(4, 2 | 4 , 8 | 16 ), // パン、向き拘束と慣性なし
                });
            }
            catch
            {
                return null;
            }
        }

        static bool GestureListener_Pan_Scrolled = false;
        static int GestureListener_Pan_StartingLocation;
        static int GestureListener_Pan_PrevTotalDelta;
        private static void GestureListener_Pan(object sender, Alteridem.WinTouch.PanEventArgs e)
        {
            var gestureListener = (Alteridem.WinTouch.GestureListener)sender;
            var tsdd = (ToolStripDropDown)gestureListener.Parent;
            var items = tsdd.Items;
            if (items.Count > 0)
            {
                var location = e.Location.Y;
                if (e.Begin)
                {
                    GestureListener_Pan_Scrolled = false;
                    e.Handled = true;
                    GestureListener_Pan_StartingLocation = location;
                    GestureListener_Pan_PrevTotalDelta = 0;
                }
                if (e.Handled) return;
                var height = items[0].Height;
                var totalDelta = (int)Math.Round((double)(location - GestureListener_Pan_StartingLocation) / height);
                var prevTotalDelta = GestureListener_Pan_PrevTotalDelta;
                GestureListener_Pan_PrevTotalDelta = totalDelta;
                if (totalDelta != prevTotalDelta)
                {
                    GestureListener_Pan_Scrolled = true;
                    var sub = totalDelta - prevTotalDelta;
                    var up = sub > 0;
                    PerformScroll(tsdd, up, up ? sub : -sub);
                }
                if (!e.End)
                {
                    e.Handled = true;
                }
                else if (GestureListener_Pan_Scrolled)
                {
                    GestureListener_Pan_Scrolled = false;
                    e.Handled = true;
                }
            }
        }

        private static void FocusEventHandler(object sender, EventArgs e)
        {
            var tsdd = (ToolStripDropDown)sender;
            tsdd.Focus();
        }

        public static void Descrollable(ToolStripDropDown tsdd)
        {
            tsdd.MouseWheel -= MouseWheelEventHandler;
            tsdd.KeyUp -= KeyUpEventHandler;
            tsdd.Opened -= OpenedEventHandler;
            tsdd.MouseEnter -= FocusEventHandler;
        }

        public static void EnscrollableOneTime(ToolStripDropDown tsdd, Alteridem.WinTouch.GestureListener gestureListener, bool enterFocus)
        {
            Enscrollable(tsdd, enterFocus);
            tsdd.Closed -= DescrollableAfterClose;
            tsdd.Closed += DescrollableAfterClose;
            if (gestureListener != null)
            {
                gestureListener.Pan -= GestureListener_Pan;
                gestureListener.Pan += GestureListener_Pan;
                ToolStripDropDownClosedEventHandler eh = null;
                eh = (sender, e) =>
                {
                    gestureListener.Pan -= GestureListener_Pan;
                    tsdd.Closed -= eh;
                };
                tsdd.Closed += eh;
            }
        }

        private static void DescrollableAfterClose(object sender, ToolStripDropDownClosedEventArgs e)
        {
            var tsdd = (ToolStripDropDown)sender;
            Descrollable(tsdd);
            tsdd.Closed -= DescrollableAfterClose;
        }

        private static void PerformScroll(ToolStripDropDown tsdd, bool up, int count)
        {
            Type t = typeof(ToolStripDropDownMenu);
            FieldInfo p = t.GetField((up ? "upScrollButton" : "downScrollButton"),
                BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance);
            if (p != null)
            {   //ボタンのオブジェクトを取得、スクロール処理のメソッドも確保
                ToolStripControlHost scrollButton = p.GetValue(tsdd) as ToolStripControlHost;
                MethodInfo m = t.GetMethod("ScrollInternal",
                    BindingFlags.NonPublic | BindingFlags.InvokeMethod | BindingFlags.Instance,
                    null, new Type[] { typeof(bool) }, null);
                for (int i = 0; i < count; i++)
                {
                    if (scrollButton != null && scrollButton.Visible && scrollButton.Enabled && m != null)
                    {   //該当ボタンが押せる状態であれば、スクロール用メソッドを呼び出す
                        m.Invoke(tsdd, new object[] { up });
                    }
                }
            }
        }

        private static void MouseWheelEventHandler(object sender, MouseEventArgs e)
        {
            //５件分スクロールさせ、処理済み状態にする
            PerformScroll(sender as ToolStripDropDown, e.Delta > 0, 5);
            ((HandledMouseEventArgs)e).Handled = true;
        }

        private static void KeyUpEventHandler(object sender, KeyEventArgs e)
        {
            var tsdd = sender as ToolStripDropDown;

            //まず、選択中のメニュー項目、およびコンテキストメニューの表示領域を把握する
            ToolStripItem item = null;
            foreach (ToolStripItem i in tsdd.Items)
            {
                if (i.Selected) { item = i; break; }
            }
            Rectangle displayRect = tsdd.RectangleToClient(tsdd.Bounds);
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {   //矢印キー関連は何もしない
            }
            else if (e.KeyCode == Keys.Apps)
            {   //アプリケーションキー関連も何もしない
            }
            else if (e.KeyCode == Keys.PageUp)
            {   //１０件分上スクロール
                PerformScroll(tsdd, true, 10);
                if (item != null && displayRect.Contains(item.Bounds) == false)
                {   //現在選択中のアイテムが表示領域外ならば、アイテム末尾から表示領域内のアイテムを探索
                    for (int i = tsdd.Items.Count - 1; i >= 0; i--)
                    {
                        item = tsdd.Items[i];
                        if (displayRect.Contains(item.Bounds) && item.CanSelect)
                        {   //表示領域内のアイテムを選択する
                            item.Select();
                            break;
                        }
                    }
                }
            }
            else if (e.KeyCode == Keys.PageDown)
            {   //１０件分下スクロール
                PerformScroll(tsdd, false, 10);
                if (item != null && displayRect.Contains(item.Bounds) == false)
                {   //現在選択中のアイテムが表示領域外ならば、アイテム先頭から表示領域内のアイテムを探索
                    for (int i = 0; i < tsdd.Items.Count; i++)
                    {
                        item = tsdd.Items[i];
                        if (displayRect.Contains(item.Bounds) && item.CanSelect)
                        {   //表示領域内のアイテムを選択する
                            item.Select();
                            break;
                        }
                    }
                }
            }
            else
            {   //アクセスキーによる操作とみなし、表示領域外の場合は選択中アイテムの位置へスクロール移動する
                if (item != null && displayRect.Contains(item.Bounds) == false)
                {
                    Type t = typeof(ToolStripDropDownMenu);
                    MethodInfo m = t.GetMethod("ChangeSelection",
                        BindingFlags.NonPublic | BindingFlags.InvokeMethod | BindingFlags.Instance,
                        null, new Type[] { typeof(ToolStripItem) }, null);
                    if (m != null)
                    {
                        m.Invoke(tsdd, new object[] { item });
                    }
                }
            }
        }
        
        private static void OpenedEventHandler(object sender, EventArgs e)
        {
            (sender as ToolStripDropDown).Focus();
        }
    }
}
