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

public static class ExclusionControl
{
    public static void TryLock(object obj, Action body)
    {
        bool lockWasTaken = false;
        var temp = obj;
        try
        {
            Monitor.TryEnter(temp, ref lockWasTaken);
            body();
        }
        finally
        {
            if (lockWasTaken)
            {
                Monitor.Exit(temp);
            }
        }
    }


}
