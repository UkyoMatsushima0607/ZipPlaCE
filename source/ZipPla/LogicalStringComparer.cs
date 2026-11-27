using Alteridem.WinTouch;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ZipPla
{
    // http://wiki.dobon.net/index.php?.NET%A5%D7%A5%ED%A5%B0%A5%E9%A5%DF%A5%F3%A5%B0%B8%A6%B5%E6%2F111
    public class LogicalStringComparer :
        System.Collections.IComparer,
        System.Collections.Generic.IComparer<string>
    {
        [System.Security.SuppressUnmanagedCodeSecurity] // 呼び出しごとのセキュリティのスタックウォークを省く（K5）
        [System.Runtime.InteropServices.DllImport("shlwapi.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode,
            ExactSpelling = true)]
        public static extern int StrCmpLogicalW(string x, string y);
        
        public int Compare(string x, string y)
        {
            return StrCmpLogicalW(x, y);
        }

        public int Compare(object x, object y)
        {
            return Compare(x.ToString(), y.ToString());
        }
    }
}
