using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZipPla
{
    public struct Fraction : IEquatable<Fraction>, IEquatable<int>, IComparable<Fraction>, IComparable<int>
    {
        private int numerator;
        public int Numerator { get { return numerator; } }
        private int denominator;
        public int Denominator { get { return denominator; } }

        public Fraction(int numerator, int denominator)
        {
            if (denominator == 0) throw new DivideByZeroException();
            this.numerator = numerator;
            this.denominator = denominator;
            positivate();
            reduction();
        }

        private void positivate()
        {
            if (denominator < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }
        }

        private void reduction()
        {
            var n = numerator > 0 ? numerator : -numerator;
            var d = denominator;
            while (n > 0)
            {
                var temp = n;
                n = d % n;
                d = temp;
            }
            numerator /= d;
            denominator /= d;
        }

        public static implicit operator Fraction(int i)
        {
            return new Fraction { numerator = i, denominator = 1 };
        }

        public static explicit operator Fraction(Size size)
        {
            return new Fraction(size.Width, size.Height);
        }

        public static explicit operator double(Fraction f)
        {
            return (double)f.numerator / f.denominator;
        }

        public static explicit operator int(Fraction f)
        {
            return f.numerator / f.denominator;
        }


        public static implicit operator Size(Fraction f)
        {
            return new Size(f.numerator, f.denominator);
        }

        public static bool operator ==(Fraction f, Fraction g)
        {
            return f.numerator == g.numerator && f.denominator == g.denominator;
        }

        public static bool operator !=(Fraction f, Fraction g)
        {
            return f.numerator != g.numerator || f.denominator != g.denominator;
        }

        public static bool operator ==(Fraction f, int i)
        {
            return f.denominator == 1 && f.numerator == i;
        }

        public static bool operator !=(Fraction f, int i)
        {
            return f.denominator != 1 || f.numerator != i;
        }
        
        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            if (obj is int && this == (int)obj) return true;
            if (obj is Fraction && this == (Fraction)obj) return true;
            return false;
        }

        public override int GetHashCode()
        {
            return numerator ^ (denominator - 1); // 逆数が一致するのを避け、なおかつ恒等写像で実装された int のハッシュと整合性をとる
        }

        public override string ToString()
        {
            return denominator == 1 ? $"{numerator}" : $"{numerator}/{denominator}";
        }

        public bool Equals(Fraction other)
        {
            return other != null && other.numerator == numerator && other.denominator == denominator;
        }

        public int FloorOfPlusHalf()
        {
            if (numerator > 0)
            {
                return (numerator + (denominator >> 1)) / denominator;
            }
            else
            {
                return (numerator - ((denominator-1) >> 1)) / denominator;
            }
        }


        public bool Equals(int other)
        {
            return denominator == 1 && numerator == other;
        }

        public static bool operator >(Fraction f, Fraction g)
        {
            return f.CompareTo(g) > 0;
        }

        public static bool operator <(Fraction f, Fraction g)
        {
            return f.CompareTo(g) < 0;
        }

        public int CompareTo(Fraction other)
        {
            if (other.numerator > 0)
            {
                return (this / other).CompareTo(1);
            }
            else
            {
                return -(this / other).CompareTo(1);
            }
        }

        public int CompareTo(int other)
        {
            return numerator.CompareTo(denominator * other);
        }

        public static Fraction operator /(Fraction f, int i)
        {
            if (i == 0) throw new DivideByZeroException();
            var temp = f.denominator;
            f.denominator = i;
            f.positivate();
            f.reduction();
            f.denominator *= temp;
            return f;
        }

        public static Fraction operator /(int i, Fraction f)
        {
            var temp = f.denominator;
            f.denominator = f.numerator;
            f.numerator = i;
            f.positivate();
            f.reduction();
            f.numerator *= temp;
            return f;
        }

        public static Fraction operator *(Fraction f, Fraction g)
        {
            var temp = f.denominator;
            f.denominator = g.denominator;
            g.denominator = temp;
            f.reduction();
            g.reduction();
            f.numerator *= g.numerator;
            f.denominator *= g.denominator;
            return f;
        }

        public static double operator /(double x, Fraction f)
        {
            return x * f.denominator / f.numerator;
        }
        

        public static Fraction operator /(Fraction f, Fraction g)
        {
            if (g.numerator == 0) throw new DivideByZeroException();
            var temp = f.denominator;
            var gNeg = g.numerator < 0;
            f.denominator = gNeg ? -g.numerator : g.numerator;
            g.numerator = gNeg ? -g.denominator : g.denominator;
            g.denominator = temp;
            f.reduction();
            g.reduction();
            f.numerator *= g.numerator;
            f.denominator *= g.denominator;
            return f;
        }
    }
}
