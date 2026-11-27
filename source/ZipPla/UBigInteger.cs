using Alteridem.WinTouch;
using Common;
using GenerarClasses;
using SharpCompress.Common;
//using SharpCompress.Reader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Permissions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WilsonProgramming;
using WindowsFormUtilities;

namespace ZipPla
{
    public class UBigInteger : IComparable<UBigInteger>
    {
        ulong[] X;
        public UBigInteger(byte[] data)
        {
            var length = data == null ? 0 : (data.Length + 7) >> 3;
            X = new ulong[length];
            for (var i = 0; i < length; i++)
            {
                var i8 = i << 3;
                var stop = Math.Min(8, data.Length - i8);
                var x = (ulong)0;
                for (var j = 0; j < stop; j++)
                {
                    x |= (ulong)data[i8 + j] << (j << 3);
                }
                X[i] = x;
            }
        }

        public UBigInteger(ulong value)
        {
            X = new ulong[1] { value };
        }

        public static implicit operator UBigInteger(ulong value)
        {
            return new UBigInteger(value);
        }

        public static explicit operator ulong(UBigInteger value)
        {
            var X = value.X;
            return X.Length == 0 ? 0 : X[0];
        }

        public int CompareTo(UBigInteger other)
        {
            var Y = other.X;
            if (X.Length > Y.Length) return -other.CompareTo(this);

            for (var i = Y.Length - 1; i >= X.Length; i--)
            {
                if (Y[i] != 0) return -1;
            }

            for (var i = X.Length - 1; i >= 0; i--)
            {
                var result = X[i].CompareTo(Y[i]);
                if (result != 0) return result;
            }

            return 0;
        }
    }
}
