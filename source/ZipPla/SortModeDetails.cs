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
    [Serializable]
    public class SortModeDetails :IEquatable<SortModeDetails>
    {
        public SortMode SortMode, PreSortMode;
        public string RandomSeed;

        public bool ProbablyEquals(SortModeDetails other) => Equals(other);

        public SortModeDetails Clone()
        {
            return new SortModeDetails { SortMode = SortMode, PreSortMode = PreSortMode, RandomSeed = RandomSeed };
        }

        public bool Equals(SortModeDetails other)
        {
            return other as object != null && other.SortMode == SortMode && other.PreSortMode == PreSortMode && other.RandomSeed == RandomSeed;
        }

        public bool QuickEquals(SortModeDetails other)
        {
            return Equals(other);
        }

        public static bool operator ==(SortModeDetails a, SortModeDetails b)
        {
            if (a as object == null) return b as object == null;
            return a.Equals(b);
        }

        public static bool operator !=(SortModeDetails a, SortModeDetails b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SortModeDetails);
        }

        public override int GetHashCode()
        {
            if (RandomSeed == null)
            {
                return SortMode.GetHashCode() ^ PreSortMode.GetHashCode();
            }
            else
            {
                return SortMode.GetHashCode() ^ PreSortMode.GetHashCode() ^ RandomSeed.GetHashCode();
            }
        }
    }
}
