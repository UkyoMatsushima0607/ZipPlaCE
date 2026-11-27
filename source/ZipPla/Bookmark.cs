using Alteridem.WinTouch;
using Common;
using GenerarClasses;
using SharpCompress.Common;
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
    public class ColoredBookmarkConfig : Configuration
    {
        public SimpleBookmark[] Bookmarks;
        public SerializableRGB[] BookmarkColors;
        public CatalogProfile[] Profiles;
        public SerializableRGB[] ProfileColors;
    }

    public class ColoredBookmark
    {
        public ColoredBookmark(Color color, SimpleBookmark bookmark, CatalogProfile profile)
        {
            Color = color;
            SimpleBookmark = bookmark;
            Profile = profile;
        }

        public SimpleBookmark SimpleBookmark;
        public Color Color;
        public CatalogProfile Profile;

        public override string ToString()
        {
            return SimpleBookmark == null ? "" : SimpleBookmark.ToString();
        }
    }

    public enum SimpleBookmarkSpecialRole { None, Separator } // 数値付きでは XML に保存できない。 Separator がデフォルトにならないようこの順に。

    public class SimpleBookmark
    {
        public SimpleBookmark() { } // シリアライズ用

        public SimpleBookmark(string alias, string location)
        {
            Location = location;
            SetOrResetAlias(alias);
        }

        public SimpleBookmark(SimpleBookmarkSpecialRole specialRole)
        {
            SpecialRole = specialRole;
        }

        public const SimpleBookmarkSpecialRole Separator = SimpleBookmarkSpecialRole.Separator;
        public SimpleBookmarkSpecialRole SpecialRole = SimpleBookmarkSpecialRole.None;

        private bool locationChangedAfterGotDisplayName = false;
        private string location = null;
        public string Location
        {
            get { return location; }
            set
            {
                if (value != location)
                {
                    location = value;
                    locationChangedAfterGotDisplayName = true;
                }
            }
        }

        private string alias;
        public string Alias
        {
            get { return alias; }
            set { SetOrResetAlias(value); }
        }
        public void SetOrResetAlias(string alias)
        {
            if (Location != null && alias == GetDisplayName(Location).Replace("&", "&&"))
            {
                this.alias = null;
            }
            else
            {
                this.alias = alias;
            }
        }

        /// <summary>
        /// ToString() との違いは Location がない場合に末尾に * を付けること
        /// </summary>
        /// <returns></returns>
        public string GetDisplayString() => alias != null ?
                Location != null ? alias : (alias + "*") :
                privateGetDisplayName().Replace("&", "&&");
        
        public override string ToString() => alias ?? privateGetDisplayName().Replace("&", "&&");

        private string privateGetDisplayName_LastResult = "";
        private string privateGetDisplayName()
        {
            if (locationChangedAfterGotDisplayName)
            {
                locationChangedAfterGotDisplayName = false;
                return privateGetDisplayName_LastResult = GetDisplayName(location);
            }
            else
            {
                return privateGetDisplayName_LastResult;
            }
        }

        private static readonly string HistorySorPathInLowerCase = null;
        static SimpleBookmark()
        {
            try
            {
                HistorySorPathInLowerCase = Program.HistorySorPath.ToLower();
                var user = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                if (user.EndsWith(Path.DirectorySeparatorChar.ToString()) || user.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    user = user.Substring(0, user.Length - 1);
                }
                var SepBody = Regex.Escape(Path.DirectorySeparatorChar.ToString()) + Regex.Escape(Path.AltDirectorySeparatorChar.ToString());
                var Sep = "[" + SepBody + "]";
                var NotSep = "[^" + SepBody + "]";
                var Dir = "(?:" + Sep + NotSep + "+)";
            }
            catch { }
        }
        private static readonly Dictionary<string, string> GetDisplayNameDictionary = new Dictionary<string, string>();
        public static string GetDisplayName(string path)
        {
            if (path == null) path = "";
            var pathToLower = path.ToLower();
            if (pathToLower == HistorySorPathInLowerCase)
            {
                return $"<{Message.History}>";
            }
            else
            {
                // desktop.ini の存在をチェック
                string result;
                if (GetDisplayNameDictionary.TryGetValue(pathToLower, out result))
                {
                    return result;
                }
                else if (isRoot(path) || HasDesktopIni(path) == true)
                {
                    try
                    {
                        result = FileTypeManager.GetDisplayName(path);
                        GetDisplayNameDictionary[pathToLower] = result;
                        return result;
                    }
                    catch { }
                }


                // ルート及びユーザーフォルダ以下のフォルダのみ FileTypeManager を使う


                var sepPos = path.LastIndexOf(Path.DirectorySeparatorChar.ToString());
                if (sepPos >= 0 && sepPos < path.Length - 1) return path.Substring(sepPos + 1);
                else return path;
            }
        }

        private static bool isRoot(string path)
        {
            if (path == null) return false;
            var len = path.Length;
            return len == 2 && path[1] == ':' || len == 3 && path[1] == ':' && path[2] == Path.DirectorySeparatorChar;
        }

        public static bool? HasDesktopIni(string path)
        {
            try
            {
                return File.Exists(Path.Combine(path, "desktop.ini"));

            }
            catch
            {
#if !AUTOBUILD
                throw;
#else
                return null;
#endif
            }
        }

    }
}
