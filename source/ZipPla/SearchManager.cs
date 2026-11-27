#define USEFUZZYCOMPARE

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZipPla
{
    public class SearchManager
    {
        private readonly Regex[] regexArray;
        private readonly string[] simpleMatchArray;
        private readonly int[] ratingStarsArray;
        private enum boolOperator { And, Or, Not, OrNot }
        private readonly boolOperator[] boolOperatorArray;
        private enum compareOperator
        { RatingEqual, RatingLessThanOrEqual, RatingLessThan, RatingGreaterThan, RatingGreaterThanOrEqual }
        private readonly compareOperator[] compareOperatorArray;

        // Compiled は開放されないので static readonly 以外で使うのは適切ではない
        // https://msdn.microsoft.com/ja-jp/library/8zbs0h2f(v=vs.110).aspx

        public static Regex GetRegex(string text)
        {
            return new Regex(IsRegex.Replace(text, "$1"), (IsIgnoreCaseRegex.IsMatch(text) ?
                    RegexOptions.IgnoreCase : RegexOptions.None));
        }
        
        /// <summary>作ったときの絞り込みの文字列（K29）</summary>
        public string Text { get; }

        /// <summary>
        /// newText で一致する名前が、必ず oldText でも一致するか（K29）。両方とも演算子・評価・ワイルドカード・正規表現のない
        /// AND の単純な語だけで、oldText の各語が newText のどれかの語に含まれるときだけ true（判定できなければ false）。
        /// 濁点などの結合文字が続く場合は、あいまい比較で前の文字と一体になりうるので false にする
        /// </summary>
        public static bool IsNarrowing(string oldText, string newText)
        {
            if (oldText == null || newText == null) return false;
            if (IsRegex.IsMatch(oldText) || IsRegex.IsMatch(newText)) return false;
            string[] oldTerms, newTerms;
            try
            {
                oldTerms = Split(oldText);
                newTerms = Split(newText);
            }
            catch
            {
                return false;
            }
            if (!allSimpleTerms(oldTerms) || !allSimpleTerms(newTerms)) return false;
            foreach (var o in oldTerms)
            {
                var found = false;
                foreach (var n in newTerms)
                {
                    var idx = n.IndexOf(o, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;
                    var after = idx + o.Length;
                    if (after < n.Length && isCombining(n[after])) continue;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }

        private static bool allSimpleTerms(string[] terms)
        {
            if (terms.Length == 0) return false;
            foreach (var t in terms)
            {
                if (string.IsNullOrEmpty(t)) return false;
                var c = t[0];
                if (c == '+' || c == '-' || c == '"') return false;
                if (t.IndexOf('*') >= 0 || t.IndexOf('?') >= 0 || t.IndexOf('"') >= 0) return false;
                if (IsRatingFilter.IsMatch(t)) return false;
                if (isCombining(c)) return false;
            }
            return true;
        }

        private static bool isCombining(char c)
        {
            if (c == '\uFF9E' || c == '\uFF9F' || c == '\u3099' || c == '\u309A' || c == '\u309B' || c == '\u309C') return true;
            var cat = char.GetUnicodeCategory(c);
            return cat == System.Globalization.UnicodeCategory.NonSpacingMark || cat == System.Globalization.UnicodeCategory.SpacingCombiningMark ||
                cat == System.Globalization.UnicodeCategory.EnclosingMark;
        }

        public SearchManager(string text)
        {
            Text = text;
            if (IsRegex.IsMatch(text))
            {
                regexArray = new Regex[1] { GetRegex(text) };
                simpleMatchArray = new string[1];
                boolOperatorArray = new boolOperator[1] { boolOperator.And };
            }
            else
            {
                var array = Split(text);
                var length = array.Length;
                regexArray = new Regex[length];
                simpleMatchArray = new string[length];
                ratingStarsArray = new int[length];
                boolOperatorArray = new boolOperator[length];
                compareOperatorArray = new compareOperator[length];
                for (var i = 0; i < length; i++)
                {
                    var str = array[i];
                    var strLen = str.Length;
                    if (strLen > 2 && str.StartsWith("+-"))
                    {
                        str = str.Substring(2);
                        boolOperatorArray[i] = boolOperator.OrNot;
                    }
                    else if (strLen > 1 && str.StartsWith("+"))
                    {
                        str = str.Substring(1);
                        boolOperatorArray[i] = boolOperator.Or;
                    }
                    else if (strLen > 1 && str.StartsWith("-"))
                    {
                        str = str.Substring(1);
                        boolOperatorArray[i] = boolOperator.Not;
                    }
                    else
                    {
                        boolOperatorArray[i] = boolOperator.And;
                    }

                    var ratingMatch = IsRatingFilter.Match(str);
                    if (ratingMatch.Success)
                    {
                        ratingStarsArray[i] = int.Parse(ratingMatch.Groups[2].Value);
                        switch (ratingMatch.Groups[1].Value)
                        {
                            case "=":
                                compareOperatorArray[i] = compareOperator.RatingEqual;
                                break;
                            case "<":
                                compareOperatorArray[i] = compareOperator.RatingLessThan;
                                break;
                            case "<=":
                                compareOperatorArray[i] = compareOperator.RatingLessThanOrEqual;
                                break;
                            case ">":
                                compareOperatorArray[i] = compareOperator.RatingGreaterThan;
                                break;
                            case ">=":
                                compareOperatorArray[i] = compareOperator.RatingGreaterThanOrEqual;
                                break;
                        }
                    }
                    else
                    {
                        string wildcard = str.Replace("\"", "");
                        if (includesInvalidPathChars(wildcard)) throw new FormatException();
                        wildcard = wildcardTrim(wildcard);
                        if (wildcard.Contains("*") || wildcard.Contains("?"))
                        {
                            regexArray[i] = wildcardToRegex(wildcard);
                        }
                        else
                        {
                            simpleMatchArray[i] = wildcard.ToLower();
                        }
                    }
                }
            }
        }

        private static readonly Regex IsRatingFilter = new Regex(@"^r(=|<|>|<=|>=)([0-9]+)$", RegexOptions.Compiled);
        public static readonly Regex IsRatingFilterWithOrWithoutOperator = new Regex(@"^((?:\+|\+-|-)?)r(=|<|>|<=|>=)([0-9]+)$", RegexOptions.Compiled);

        public static string TrimAndPutQuotationIfNeeded(string text, IEnumerable<string> stopwords)
        {
            text = text.Trim();
            if (
                text.Contains(' ') ||
                text.StartsWith("+") ||
                text.StartsWith("-") ||
                IsRatingFilter.IsMatch(text) ||
                stopwords != null && stopwords.Contains(text)
                )
            {
                return $"\"{text}\"";
            }
            else
            {
                return text;
            }
        }

        public bool Match(string name)
        {
            if (name == null) name = "";
            var count = regexArray.Length;
            var result = true;
            for (var i = 0; i < count; i++)
            {
                var op = boolOperatorArray[i];
                switch (op)
                {
                    case boolOperator.And:
                    case boolOperator.Not:
                        if (!result) continue;
                        break;
                    case boolOperator.Or:
                    case boolOperator.OrNot:
                        if (result) continue;
                        break;
                }

                bool r;
                var simple = simpleMatchArray[i];
                // IndexOf(*, StringComparision.Ordinal) は Contains と同じ
                // 今は IgnoreCase が必要なので OrdinalIgnoreCase を使う
                // Ordinal から始まるものが最もパフォーマンスが高く間違いが起こりにくい
                // http://dobon.net/vb/dotnet/string/stringindexof.html
                if (simple != null) r = name.IndexOf(simple, StringComparison.OrdinalIgnoreCase) >= 0;
                else
                {
                    {
                        var regex = regexArray[i];
                        if (regex != null) r = regex.IsMatch(name);
                        else
                        {
                            var rating = ZipPlaInfo.GetOnlyRating(name);
                            if (rating < 0) r = false;
                            else
                            {
                                r = true;
                                switch (compareOperatorArray[i])
                                {
                                    case compareOperator.RatingEqual:
                                        r = rating == ratingStarsArray[i];
                                        break;
                                    case compareOperator.RatingLessThan:
                                        r = rating < ratingStarsArray[i];
                                        break;
                                    case compareOperator.RatingLessThanOrEqual:
                                        r = rating <= ratingStarsArray[i];
                                        break;
                                    case compareOperator.RatingGreaterThan:
                                        r = rating > ratingStarsArray[i];
                                        break;
                                    case compareOperator.RatingGreaterThanOrEqual:
                                        r = rating >= ratingStarsArray[i];
                                        break;
                                }
                            }
                        }
                    }
                }

                switch (op)
                {
                    case boolOperator.And:
                        result = result && r;
                        break;
                    case boolOperator.Or:
                        result = result || r;
                        break;
                    case boolOperator.Not:
                        result = result && !r;
                        break;
                    case boolOperator.OrNot:
                        result = result || !r;
                        break;
                }
            }
            return result;
        }

        public static readonly Regex IsRegex = new Regex(@"^\/(.+)\/[imgy]*$", RegexOptions.Compiled);
        private static readonly Regex IsIgnoreCaseRegex = new Regex(@"i[imgy]*$", RegexOptions.Compiled);


        public static string[] Split(string str)
        {
            var strArray = str.ToCharArray();
            var length = strArray.Length;
            var outOfDoubleQuotation = true;
            for (var i = 0; i < length; i++)
            {
                var c = strArray[i];
                if (c == '"')
                {
                    outOfDoubleQuotation = !outOfDoubleQuotation;
                }
                else if (outOfDoubleQuotation && c == ' ')
                {
                    strArray[i] = '\t';
                }
            }
            if (!outOfDoubleQuotation) throw new FormatException();
            return (new string(strArray)).Split(new char[1] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static string[] Split(string str, out string[] fullTrimmed)
        {
            var simpleSplit = Split(str);
            fullTrimmed = new string[simpleSplit.Length];
            for (var i = 0; i < simpleSplit.Length; i++)
            {
                fullTrimmed[i] =TrimOperatorAndDoubleQuotations(simpleSplit[i]);
            }
            return simpleSplit;
        }
        
        public static string TrimOperatorAndDoubleQuotations(string str)
        {
            return TrimDoubleQuotations(TrimOperator(str));
        }

        public static string TrimOperator(string str)
        {
            if (str == null) return null;
            var length = str.Length;
            if (length < 2) return str;
            var c = str[0];
            if (c == '+')
            {
                if (str[1] == '-')
                {
                    if (length >= 3)
                    {
                        return str.Substring(2);
                    }
                    else
                    {
                        return str;
                    }
                }
                else
                {
                    return str.Substring(1);
                }
            }
            else if (c == '-')
            {
                return str.Substring(1);
            }
            else
            {
                return str;
            }
        }

        public static string TrimDoubleQuotations(string str)
        {
            if (str != null && str.Length >= 3 && str.First() == '"' && str.Last() == '"')
            {
                return str.Substring(1, str.Length - 2);
            }
            else
            {
                return str;
            }
        }

        private static readonly char[] invalidPathChars = Path.GetInvalidPathChars();
        private static bool includesInvalidPathChars(string str)
        {
            return str.IndexOfAny(invalidPathChars) >= 0;
        }

        private static string wildcardTrim(string wildcard)
        {
            var questions = "";
            var stop = wildcard.Length - 1;
            for (var i = 0; true; i++)
            {
                var c = wildcard[i];
                if (c == '?')
                {
                    questions += "?";
                }
                else if (c != '*')
                {
                    wildcard = questions + wildcard.Substring(i);
                    break;
                }
                if (i == stop)
                {
                    wildcard = questions;
                    break;
                }
            }
            questions = "";
            for (var i = wildcard.Length - 1; true; i--)
            {
                var c = wildcard[i];
                if (c == '?')
                {
                    questions += "?";
                }
                else if (c != '*')
                {
                    wildcard = wildcard.Substring(0, i + 1) + questions;
                    break;
                }
                if (i == 0)
                {
                    wildcard = questions;
                    break;
                }
            }
            return wildcard;
        }

        private static readonly Regex eachCharRegex = new Regex(".", RegexOptions.Compiled);
        private static Regex wildcardToRegex(string wildcard)
        {
            // 動的に作られる正規表現は Compiled とするのは不適切
            // また高負荷時にはパフォーマンスの逆転もあるので Compiled のメリットはほとんどない
            return new Regex(eachCharRegex.Replace(wildcard, wildcardToRegex_charConverter), RegexOptions.IgnoreCase); // | RegexOptions.Compiled);
        }

        private static string wildcardToRegex_charConverter(Match m)
        {
            var c = m.Value;
            switch(c)
            {
                case "?":return ".";
                case "*": return ".*";
                default: return Regex.Escape(c);
            }
        }
    }
}
