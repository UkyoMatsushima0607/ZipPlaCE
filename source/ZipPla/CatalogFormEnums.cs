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
    public enum ClipMode { Letterbox, PanAndScan, PlaClip }

    public enum SortMode
    {
        TypeInAsc, TypeInDesc, NameInAsc, NameInDesc, RatingInAsc, RatingInDesc, CreatedInAsc, CreatedInDesc, AccessedInAsc, AccessedInDesc, ModifiedInAsc, ModifiedInDesc, SizeInAsc, SizeInDesc, Random,
        VPageInAsc, VPageInDesc, VResolutionInAsc, VResolutionInDesc, VRatioInAsc, VRatioInDesc, VWidthInAsc, VWidthInDesc, VHeightInAsc, VHeightInDesc, VLengthInAsc, VLengthInDesc, VFPSInAsc, VFPSInDesc// 揮発性（volatility）ソートは Random のあとに書く
    }

    public enum SubfolderMode { Display, DisplayHeader, Ignore, Search, DisplayAndSearch, DisplayHeaderAndSearch }

    public enum BindingMode { SinglePage, SinglePageWithoutScalingUp, LeftToRight, RightToLeft }

    public enum InitialFullscreenMode { ForceFullscreen, ForceWindow, Default }

    public enum ArchivesInArchiveMode { Ignore, IfNoOther1Level, UntilFound2Level, Always1Level }

    public enum CoverBindingMode { Default, ForceSingle }

    public enum SelectionHighlightMode { None, WhenMouseLeaves }

    public enum DoubleClickMode { SelectAndClick, IgnoreMouseMove, Standard }

    public enum FilterRemovingMode { Never, WhenMovingLocation }

    public enum NumberOfLinesOfName { Full, Automatic1, Automatic2, One, Two, Three }

    public enum FolderSortMode { None, Ahead, Behind, FitToDirectionOfSort }

    public enum CatalogFormContextMenu { ZipPla, Tag, Explorer }

    public enum ItemSelectionFeedback { None, IfSortMayBeSame, Always }

    public enum SortSyncMode { None, Directory }

    public enum ItemLoadLevel { Ignore = 0, /*Minimul = 1,*/ Header = 1, Full = 3 }
}
