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
    public class PrefixEscapedToolStripMenuItem : ToolStripMenuItem
    {
        private string plainText;
        public string PlainText
        {
            get
            {
                return plainText;
            }
        }

        private CheckState initialCheckState;

        public PrefixEscapedToolStripMenuItem(string text, CheckState initialCheckState) : base(text?.Replace("&", "&&"))
        {
            plainText = text;
            CheckState = initialCheckState;
            this.initialCheckState = initialCheckState;
        }

        public void ToggleCheck()
        {
            switch (CheckState)
            {
                case CheckState.Unchecked:
                    CheckState = CheckState.Checked;
                    break;
                case CheckState.Indeterminate:
                    CheckState = CheckState.Unchecked;
                    break;
                default:
                    CheckState = initialCheckState == CheckState.Indeterminate ? CheckState.Indeterminate : CheckState.Unchecked;
                    break;
            }
        }
    }
}
