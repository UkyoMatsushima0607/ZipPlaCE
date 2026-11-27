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
        private bool FullScreen_pseudoFullScreen = false;

        private bool FullScreen
        {
            get
            {
                return FormBorderStyle == FormBorderStyle.None && WindowState == FormWindowState.Maximized;
            }
            set
            {
                // 中途半端な状態の修復効果を得るため、フルスクリーン化は現在の状態にかからわず実行する
                if (value)
                {
                    var tempPrevSize = prevSize;

                    if (LoadSettings_FormWindowStateFromSetteingFile == null)
                    {
                        prevFormWindowState = WindowState;
                    }
                    else
                    {
                        prevFormWindowState = (FormWindowState)LoadSettings_FormWindowStateFromSetteingFile;
                        LoadSettings_FormWindowStateFromSetteingFile = null;
                    }
                    prevFormBorderStyle = FormBorderStyle;
                    var unvisibled = false;
                    var temp = pbView_SizeChanged_SizeChanging;
                    pbView_SizeChanged_SizeChanging = true;
                    var needToCareMoving = false;
                    if (WindowState == FormWindowState.Maximized && ipcLookAheadInfo == null)
                    {
                        if (Visible)
                        {
                            Visible = false;
                            unvisibled = true;
                        }
                        WindowState = FormWindowState.Normal; // タスクバーが残る問題の回避
                        needToCareMoving = true;
                    }

                    if (ipcLookAheadInfo != null)
                    {
                        pbView_SizeChanged_SizeChanging = temp;

                        FullScreen_pseudoFullScreen = true;
                    }
                    else
                    {
                        var currentDisplay = needToCareMoving ? Screen.FromControl(this).WorkingArea : Rectangle.Empty;
                        FormBorderStyle = FormBorderStyle.None;
                        if (needToCareMoving)
                        {
                            var memoriedDisplay = Screen.FromControl(this).WorkingArea;
                            if (currentDisplay != memoriedDisplay)
                            {
                                Bounds = BetterFormRestoreBounds.GetMovedWindowBounds(currentDisplay, Bounds, memoriedDisplay);
                            }
                        }
                        pbView_SizeChanged_SizeChanging = temp;
                        WindowState = FormWindowState.Maximized;

                        // ViewerForm_SizeChanged による書き換えを元に戻す
                        if (prevFormWindowState == FormWindowState.Maximized && prevSize.IsEmpty) prevSize = tempPrevSize;

                        if (unvisibled) Visible = true;
                        if (TopMostInFullscreen) TopMost = true;
                    }
                }

                // フルスクリーンでない場合、全画面かどうかが無意味に変更されないように条件をつける
                else if (FormBorderStyle == FormBorderStyle.None)
                {
                    if (prevFormWindowState == FormWindowState.Maximized && !prevSize.IsEmpty)
                    {
                        Size = prevSize;
                    }
                    if (prevFormWindowState == FormWindowState.Normal && ipcLookAheadInfo == null) prevSize = Size.Empty;

                    var temp = pbView_SizeChanged_SizeChanging;
                    pbView_SizeChanged_SizeChanging = true;

                    if (FormBorderStyle == FormBorderStyle.None)
                    {
                        FormBorderStyle = prevFormBorderStyle;
                    }

                    pbView_SizeChanged_SizeChanging = temp;
                    WindowState = prevFormWindowState;

                    TopMost = false;
                }
            }
        }

        private FormWindowState prevFormWindowState;

        private FormBorderStyle prevFormBorderStyle;

        private Size prevSize = Size.Empty;



        private Size ViewerForm_SizeChanged_CurrentSize = Size.Empty;

        private FormWindowState ViewerForm_SizeChanged_CurrentWindowState;

        private Rectangle ViewerForm_SizeChanged_pbBoundsMemoryForMinimize = Rectangle.Empty;

        private FormWindowState ViewerForm_SizeChanged_BeforeMinimizedState = FormWindowState.Normal;

        private void ViewerForm_SizeChanged(object sender, EventArgs e)
        {
            var ws = WindowState;
            var pre = ViewerForm_SizeChanged_CurrentWindowState;


            
            if (pre == FormWindowState.Maximized && ws == FormWindowState.Normal && normalStateLocation != null)
            {
                Location = (Point)normalStateLocation;
            }
            else if (ws == FormWindowState.Normal)
            {
                normalStateLocation = Location;
            }

            if (ws == FormWindowState.Minimized && pre != FormWindowState.Minimized)
            {
                ViewerForm_SizeChanged_BeforeMinimizedState = pre;
            }

            if (ws != ViewerForm_SizeChanged_CurrentWindowState)
            {
                ViewerForm_SizeChanged_CurrentWindowState = ws;
                if (ws != FormWindowState.Normal)
                {
                    if (prevSize.IsEmpty && !ViewerForm_SizeChanged_CurrentSize.IsEmpty)
                    {
                        prevSize = ViewerForm_SizeChanged_CurrentSize;
                    }
                    ViewerForm_SizeChanged_CurrentSize = Size.Empty;
                }
            }
            if (ws == FormWindowState.Normal && ipcLookAheadInfo == null)
            {
                if (!pbView_SizeChanged_SizeChanging)
                {
                    ViewerForm_SizeChanged_CurrentSize = Size;
                }
                prevSize = Size.Empty;

            }
        }

        void fullScreenModeChange(bool fullScreen)
        {
            // fullscreen = true の二重がけは想定されていない。fullscreen = false は pbView の位置調整のために多重に呼び出されることがある
            if (fullScreen == true && FullScreen == true) return;

            var temp = pbView_SizeChanged_SizeChanging;
            pbView_SizeChanged_SizeChanging = true;
            if (fullScreen)
            {
                FullScreen = true;

                HideUI = true;
                
                closeWindowtoolStripMenuItem.Visible = true;
                menuStrip.Location = new Point(0, -closeWindowtoolStripMenuItem.Bounds.Y);
                fullScreenToolStripMenuItem.Text = Message.Window;
                
                pbView_SizeChanged_SizeChanging = temp;
                pbView_SizeChanged(null, null);
            }
            else
            {
                HideUI = AlwaysHideUI;

                if (ipcLookAheadInfo == null) FullScreen = false; // FullScreen = false が非表示を解除してしまうので
                else FullScreen_pseudoFullScreen = false;
                
                //menuStrip.Location = new Point(0, 0); // TopMost == true ならこちらでもよい
                menuStrip.Bounds = new Rectangle(0, 0, ClientRectangle.Width, menuStrip.Bounds.Height);
                
                pbView.Left = 0;
                //pbView.Width = Width; // 起動直後は Width の値が正しくないのでこれは不適切

                // 実装変更により不要に
                //pbView.Width = ClientRectangle.Width;

                closeWindowtoolStripMenuItem.Visible = false;


                fullScreenToolStripMenuItem.Text = Message.Fullscreen;
                pbView_SizeChanged_SizeChanging = temp;
                pbView_SizeChanged(null, null);

                ViewerForm_SizeChanged_CurrentSize = Size;


                // if (ShowIconRequired)// ウインドウの状況によっては毎回必要なので条件はコメントアウト
                if (ipcLookAheadInfo == null) // 代わりの条件
                {
                    ShowIcon = false;
                    ShowIcon = true;
                }
            }
        }

        private bool pbView_SizeChanged_UserResizing = false;

        private bool pbView_SizeChanged_ReloadPending = false;

        private void ViewerForm_ResizeBegin_V4(object sender, EventArgs e)
        {
            pbView_SizeChanged_UserResizing = true;
        }

        private void ViewerForm_ResizeEnd_V4(object sender, EventArgs e)
        {
            pbView_SizeChanged_UserResizing = false;
            if (pbView_SizeChanged_ReloadPending)
            {
                pbView_SizeChanged_ReloadPending = false;
                pbView_SizeChanged(pbView, EventArgs.Empty);
            }
        }

        private void fullScreenInStartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            fullScreenModeChangeToFullScreen();
        }

        private void fullScreenModeChangeToFullScreen()
        {
            fullScreenModeChange(true);
        }

        private void windowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            fullScreenModeChangeToWindow();
        }

        private void fullScreenModeChangeToWindow()
        {
            fullScreenModeChange(false);
        }

        private void toggleFullScreenModeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            toggleFullScreenMode();
        }

        private void toggleFullScreenMode()
        {
            fullScreenModeChange(!FullScreen);
        }
    }
}
