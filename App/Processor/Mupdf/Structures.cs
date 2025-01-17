﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using PDFPatcher.Common;

namespace MuPdfSharp;

public interface IMuBoundedElement
{
    Rectangle BBox { get; }
}

[DebuggerDisplay(
    "(Abort: {_Abort}, Progress: {_Progress}/{_ProgressMax}, Errors: {ErrorCount}, Incomplete: {_Incomplete})")]
public struct MuCookie
{
    private int _Abort;
    private readonly int _Progress;
    private readonly int _ProgressMax;

    /*
            readonly int _IncompleteOk;
    */
    private readonly int _Incomplete;

    public bool IsCancellationPending => _Abort != 0;
    public int ErrorCount { get; }

    public void CancelAsync() => _Abort = 1;
}

/// <summary>
///     MuPDF engine work mode.
/// </summary>
[Flags]
public enum DeviceHints
{
    None = 0,
    IgnoreShade = 2,
    DontInterperateImages = 4,
    NoCache = 16
}

/// <summary>
///     Rendering the color space of the page.
/// </summary>
public enum ColorSpace
{
    Rgb,
    Bgr,
    Cmyk,
    Gray
}

/// <summary>
///     Save the file format of the rendered page.
/// </summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Tiff
}

[DebuggerDisplay("From: {FromPageNumber}, Format: {Format (1)}")]
public readonly struct PageLabel : IComparable<PageLabel>
{
    public readonly string Prefix;
    public readonly PageLabelStyle NumericStyle;
    public readonly int StartAt;
    public readonly int FromPageNumber;
    public static PageLabel Empty = new(-1, -1, null, PageLabelStyle.Default);
    public bool IsEmpty => FromPageNumber < 0;

    public PageLabel(int pageNumber, int startAt, string prefix, PageLabelStyle numericStyle)
    {
        FromPageNumber = pageNumber;
        StartAt = startAt;
        Prefix = prefix;
        NumericStyle = numericStyle;
    }

    int IComparable<PageLabel>.CompareTo(PageLabel other) => FromPageNumber.CompareTo(other.FromPageNumber);

    public string Format(int pageNumber)
    {
        int n = pageNumber - FromPageNumber + (StartAt < 1 ? 0 : StartAt - 1);
        switch (NumericStyle)
        {
            case PageLabelStyle.Default:
            case PageLabelStyle.Digit:
                return string.Concat(Prefix, n.ToText());
            case PageLabelStyle.UpperRoman:
                return string.Concat(Prefix, n.ToRoman());
            case PageLabelStyle.LowerRoman:
                return string.Concat(Prefix, n.ToRoman()).ToLowerInvariant();
            case PageLabelStyle.UpperAlphabetic:
                return string.Concat(Prefix, n.ToAlphabet(true));
            case PageLabelStyle.LowerAlphabetic:
                return string.Concat(Prefix, n.ToAlphabet(false));
            default:
                goto case PageLabelStyle.Digit;
        }
    }
}

public enum PageLabelStyle : byte
{
    Default = 0,
    Digit = (byte)'d',
    UpperRoman = (byte)'R',
    LowerRoman = (byte)'r',
    UpperAlphabetic = (byte)'A',
    LowerAlphabetic = (byte)'a'
}

/// <summary>
///     Representation point.
/// </summary>
[DebuggerDisplay("({X},{Y})")]
public readonly struct Point : IEquatable<Point>
{
    public readonly float X, Y;

    public override string ToString() => string.Concat("(", X, ",", Y, ")");

    public Point(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    ///     Convert PDF page coordinate points to rendering page coordinate points.
    /// </summary>
    /// <param name="pageVisualBound">The page view area.</param>
    /// <returns>Convert to the point of the page coordinate.</returns>
    public Point ToPageCoordinate(Rectangle pageVisualBound) => new(X - pageVisualBound.Left,
        pageVisualBound.Height - (Y - pageVisualBound.Top));

    public static explicit operator System.Drawing.Point(Point point) =>
        new(point.X.ToInt32(), point.Y.ToInt32());

    public static implicit operator PointF(Point point) => new(point.X, point.Y);

    public static implicit operator Point(System.Drawing.Point point) => new(point.X, point.Y);

    public static implicit operator Point(PointF point) => new(point.X, point.Y);

    public override bool Equals(object obj) => obj is Point && this == (Point)obj;

    public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode();

    public bool Equals(Point other) => this == other;

    public static bool operator ==(Point left, Point right) => left.X == right.X && left.Y == right.Y;

    public static bool operator !=(Point left, Point right) => !(left == right);
}

/// <summary>
///     Indicates the border (the rectangle of the coordinate value is an integer).
///     In MuPDF, the <see cref="Bottom" /> value of the BBox should be greater than the <see cref="Top" /> value.
/// </summary>
[DebuggerDisplay("({Left},{Top})-({Right},{Bottom})")]
public readonly struct BBox : IEquatable<BBox>
{
    public readonly int Left, Top, Right, Bottom;

    public BBox(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public Size Size => new(Width, Height);
    public bool IsInfinite => Left > Right || Top > Bottom;
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public bool Contains(Point point) => Right >= point.X && Left <= point.X && Top <= point.Y && Bottom >= point.Y;

    public static implicit operator System.Drawing.Rectangle(BBox rect) =>
        new(
            rect.Left,
            rect.Top < rect.Bottom ? rect.Top : rect.Bottom,
            rect.Width,
            rect.Height);

    public override bool Equals(object obj) => obj is BBox && this == (BBox)obj;

    public bool Equals(BBox other) =>
        Left == other.Left &&
        Top == other.Top &&
        Right == other.Right &&
        Bottom == other.Bottom;

    public override int GetHashCode()
    {
        int hashCode = -1819631549;
        hashCode = hashCode * -1521134295 + Left;
        hashCode = hashCode * -1521134295 + Top;
        hashCode = hashCode * -1521134295 + Right;
        hashCode = hashCode * -1521134295 + Bottom;
        return hashCode;
    }

    public static bool operator ==(BBox left, BBox right) => left.Equals(right);

    public static bool operator !=(BBox left, BBox right) => !(left == right);
}

/// <summary>
///     Indicates the rectangle using the floating point number as the coordinate.
/// </summary>
[DebuggerDisplay("({Left},{Top})-({Right},{Bottom})")]
public readonly struct Rectangle : IEquatable<Rectangle>
{
    public readonly float Left, Top, Right, Bottom;
    public static readonly Rectangle Infinite = new(1, 1, -1, -1);
    public static readonly Rectangle Empty = new(0, 0, 0, 0);
    public static readonly Rectangle Unit = new(0, 0, 1, 1);

    public Rectangle(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    private static int SafeInt(double f) => f > int.MaxValue ? int.MaxValue : f < int.MinValue ? int.MinValue : (int)f;

    public SizeF Size => new(Width, Height);
    public bool IsEmpty => Left == Right || Top == Bottom;
    public bool IsInfinite => Left > Right || Top > Bottom;
    public float Width => Right - Left;
    public float Height => Bottom - Top;

    public BBox Round => new(
        SafeInt(Math.Floor(Left + 0.001)),
        SafeInt(Math.Floor(Top + 0.001)),
        SafeInt(Math.Ceiling(Right - 0.001)),
        SafeInt(Math.Ceiling(Bottom - 0.001))
    );

    public override string ToString() => string.Concat("(", Left, ",", Top, ")-(", Right, ",", Bottom, ")");

    /// <summary>
    ///     Returns whether there is another point in the current rectangular area.
    /// </summary>
    /// <param name="point">Another point.</param>
    /// <returns>Returns true when the content is included.</returns>
    public bool Contains(Point point) => Right >= point.X && Left <= point.X && Top <= point.Y && Bottom >= point.Y;

    public bool Contains(float pointX, float pointY) =>
        Right >= pointX && Left <= pointX && Top <= pointY && Bottom >= pointY;

    /// <summary>Returns whether there is an intersection with another rectangular area.</summary>
    /// <param name="other">Another rectangular area.</param>
    /// <returns>Returns True when a rectangular area is included.</returns>
    public bool Contains(Rectangle other)
    {
        if (IsEmpty || other.IsInfinite)
        {
            return false;
        }

        if (IsInfinite || other.IsEmpty)
        {
            return true;
        }

        return Contains(other.Left, other.Top) && Contains(other.Right, other.Bottom);
    }

    /// <summary>Returns the intersection of the current rectangular area and another rectangular area. </summary>
    /// <param name="other">Another rectangular area. </param>
    /// <returns>Returns the intersection of two rectangular regions. </returns>
    public Rectangle Intersect(Rectangle other)
    {
        if (IsEmpty || other.IsEmpty)
        {
            return Empty;
        }

        if (other.IsInfinite)
        {
            return this;
        }

        if (IsInfinite)
        {
            return other;
        }

        float x0 = Left < other.Left ? other.Left : Left;
        float y0 = Top < other.Top ? other.Top : Top;
        float x1 = Right > other.Right ? other.Right : Right;
        float y1 = Bottom > other.Bottom ? other.Bottom : Bottom;
        if (x1 < x0 || y1 < y0)
        {
            return Empty;
        }

        return new Rectangle(x0, y0, x1, y1);
    }

    /// <summary>Returns a new rectangular area containing the two rectangular areas. </summary>
    /// <param name="other">Another rectangular area. </param>
    /// <returns>A new rectangular area containing the two rectangular areas. </returns>
    public Rectangle Union(Rectangle other)
    {
        if (IsEmpty || other.IsInfinite)
        {
            return other;
        }

        if (other.IsEmpty || IsInfinite)
        {
            return this;
        }

        return new Rectangle(
            Left > other.Left ? other.Left : Left,
            Top > other.Top ? other.Top : Top,
            Right < other.Right ? other.Right : Right,
            Bottom < other.Bottom ? other.Bottom : Bottom
        );
    }

    internal static Rectangle FromArray(MuPdfObject array)
    {
        MuPdfArray r = array.AsArray();
        float a = r[0].FloatValue;
        float b = r[1].FloatValue;
        float c = r[2].FloatValue;
        float d = r[3].FloatValue;
        return new Rectangle(Math.Min(a, c), Math.Min(b, d), Math.Max(a, c), Math.Max(b, d));
    }

    public static implicit operator RectangleF(Rectangle rect) =>
        new(
            rect.Left,
            rect.Top < rect.Bottom ? rect.Top : rect.Bottom,
            rect.Width,
            rect.Height);

    public static implicit operator System.Drawing.Rectangle(Rectangle rect) =>
        new(
            rect.Left.ToInt32(),
            (rect.Top < rect.Bottom ? rect.Top : rect.Bottom).ToInt32(),
            rect.Width.ToInt32(),
            rect.Height.ToInt32());

    public static Rectangle operator &(Rectangle r1, Rectangle r2) => r1.Intersect(r2);

    public static Rectangle operator |(Rectangle r1, Rectangle r2) => r1.Union(r2);

    /// <summary>Returns the proportion of two rectangular overlap regions and two rectangular tolerance.</summary>
    public static float operator /(Rectangle r1, Rectangle r2)
    {
        Rectangle i = r1.Intersect(r2);
        if (i.IsEmpty)
        {
            return 0f;
        }

        Rectangle u = r1.Union(r1);
        return i.Height * i.Width / (u.Height * u.Width);
    }

    internal Rectangle ToPageCoordinate(Rectangle pageVisualBound) =>
        new(
            Left - pageVisualBound.Left,
            pageVisualBound.Height - (Top - pageVisualBound.Top),
            Right - pageVisualBound.Left,
            pageVisualBound.Height - (Bottom - pageVisualBound.Top)
        );

    public override bool Equals(object obj) => obj is Rectangle && Equals((Rectangle)obj);

    public override int GetHashCode() =>
        Left.GetHashCode()
        ^ ((Right.GetHashCode() << 13) | (Right.GetHashCode() >> 19))
        ^ ((Top.GetHashCode() << 26) | (Top.GetHashCode() >> 6))
        ^ ((Bottom.GetHashCode() << 7) | (Bottom.GetHashCode() >> 25));

    public bool Equals(Rectangle other) =>
        Left == other.Left && Right == other.Right && Top == other.Top && Bottom == other.Bottom;

    public static bool operator ==(Rectangle left, Rectangle right) => left.Equals(right);

    public static bool operator !=(Rectangle left, Rectangle right) => !(left == right);
}

/// <summary>
///     Represents a rectangle of four coordinates.
/// </summary>
public readonly struct Quad : IEquatable<Quad>
{
    public readonly Point UpperLeft, UpperRight, LowerLeft, LowerRight;

    public Quad(Point upperLeft, Point upperRight, Point lowerLeft, Point lowerRight)
    {
        UpperLeft = upperLeft;
        UpperRight = upperRight;
        LowerLeft = lowerLeft;
        LowerRight = lowerRight;
    }

    public Quad Union(Quad other)
    {
        float x1 = Math.Min(Math.Min(UpperLeft.X, other.UpperLeft.X), Math.Min(LowerLeft.X, other.LowerLeft.X));
        float x2 = Math.Max(Math.Max(UpperLeft.X, other.UpperLeft.X), Math.Max(LowerLeft.X, other.LowerLeft.X));
        float y1 = Math.Min(Math.Min(UpperLeft.Y, other.UpperLeft.Y), Math.Min(LowerLeft.Y, other.LowerLeft.Y));
        float y2 = Math.Max(Math.Max(UpperLeft.Y, other.UpperLeft.Y), Math.Max(LowerLeft.Y, other.LowerLeft.Y));
        return new Quad(new Point(x1, y1), new Point(x2, y2), new Point(x1, y2), new Point(x2, y2));
    }

    public Rectangle ToRectangle()
    {
        float x1 = Math.Min(Math.Min(UpperLeft.X, UpperRight.X), Math.Min(LowerLeft.X, LowerRight.X));
        float x2 = Math.Max(Math.Max(UpperLeft.X, UpperRight.X), Math.Max(LowerLeft.X, LowerRight.X));
        float y1 = Math.Min(Math.Min(UpperLeft.Y, UpperRight.Y), Math.Min(LowerLeft.Y, LowerRight.Y));
        float y2 = Math.Max(Math.Max(UpperLeft.Y, UpperRight.Y), Math.Max(LowerLeft.Y, LowerRight.Y));
        return new Rectangle(x1, y1, x2, y2);
    }

    public override bool Equals(object obj) => obj is Quad && Equals((Quad)obj);

    public bool Equals(Quad other) =>
        UpperLeft.Equals(other.UpperLeft) &&
        UpperRight.Equals(other.UpperRight) &&
        LowerLeft.Equals(other.LowerLeft) &&
        LowerRight.Equals(other.LowerRight);

    public override int GetHashCode()
    {
        int hashCode = -1690381272;
        hashCode = hashCode * -1521134295 + EqualityComparer<Point>.Default.GetHashCode(UpperLeft);
        hashCode = hashCode * -1521134295 + EqualityComparer<Point>.Default.GetHashCode(UpperRight);
        hashCode = hashCode * -1521134295 + EqualityComparer<Point>.Default.GetHashCode(LowerLeft);
        hashCode = hashCode * -1521134295 + EqualityComparer<Point>.Default.GetHashCode(LowerRight);
        return hashCode;
    }

    public static bool operator ==(Quad quad1, Quad quad2) => quad1.Equals(quad2);

    public static bool operator !=(Quad quad1, Quad quad2) => !(quad1 == quad2);
}

/// <summary>
///     Indicates the transposition matrix.
/// </summary>
[DebuggerDisplay("({A},{B},{C},{D},{E},{F})")]
public readonly struct Matrix : IEquatable<Matrix>
{
    public readonly float A, B, C, D, E, F;

    private static float Min4(float a, float b, float c, float d) => Math.Min(Math.Min(a, b), Math.Min(c, d));

    private static float Max4(float a, float b, float c, float d) => Math.Max(Math.Max(a, b), Math.Max(c, d));

    /// <summary>
    ///     Unit matrix.
    /// </summary>
    public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

    /// <summary>
    ///     Vertical flip matrix.
    /// </summary>
    public static readonly Matrix VerticalFlip = new(1, 0, 0, -1, 0, 0);

    /// <summary>
    ///     Horizontal flip matrix.
    /// </summary>
    public static readonly Matrix HorizontalFlip = new(-1, 0, 0, 1, 0, 0);

    public Matrix(float a, float b, float c, float d, float e, float f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }

    /// <summary>
    ///     Multiply two matrices.
    /// </summary>
    /// <param name="one">The matrix to be multiplied. </param>
    /// <param name="two">Multiplier matrix. </param>
    /// <returns>The new matrix after multiplication. </returns>
    public static Matrix Concat(Matrix one, Matrix two) =>
        new(
            one.A * two.A + one.B * two.C,
            one.A * two.B + one.B * two.D,
            one.C * two.A + one.D * two.C,
            one.C * two.B + one.D * two.D,
            one.E * two.A + one.F * two.C + two.E,
            one.E * two.B + one.F * two.D + two.F);

    public static Matrix Scale(float x, float y) => new(x, 0, 0, y, 0, 0);

    public Matrix ScaleTo(float x, float y) => Concat(this, Scale(x, y));

    public static Matrix Shear(float h, float v) => new(1, v, h, 1, 0, 0);

    public static Matrix Rotate(float theta)
    {
        float s;
        float c;

        while (theta < 0)
        {
            theta += 360;
        }

        while (theta >= 360)
        {
            theta -= 360;
        }

        if (Math.Abs(0 - theta) < float.Epsilon)
        {
            s = 0;
            c = 1;
        }
        else if (Math.Abs(90.0f - theta) < float.Epsilon)
        {
            s = 1;
            c = 0;
        }
        else if (Math.Abs(180.0f - theta) < float.Epsilon)
        {
            s = 0;
            c = -1;
        }
        else if (Math.Abs(270.0f - theta) < float.Epsilon)
        {
            s = -1;
            c = 0;
        }
        else
        {
            s = (float)Math.Sin(theta * Math.PI / 180f);
            c = (float)Math.Cos(theta * Math.PI / 180f);
        }

        return new Matrix(c, s, -s, c, 0, 0);
    }

    public Matrix RotateTo(float theta) => Concat(this, Rotate(theta));

    public static Matrix Translate(float tx, float ty) => new(1, 0, 0, 1, tx, ty);

    public Point Transform(float x, float y) => new(x * A + y * C + E, x * B + y * D + F);

    public Rectangle Transform(Rectangle rect)
    {
        if (rect.IsInfinite)
        {
            return rect;
        }

        Point s = Transform(rect.Left, rect.Top);
        Point t = Transform(rect.Left, rect.Bottom);
        Point u = Transform(rect.Right, rect.Bottom);
        Point v = Transform(rect.Right, rect.Top);
        return new Rectangle(Min4(s.X, t.X, u.X, v.X),
            Min4(s.Y, t.Y, u.Y, v.Y),
            Max4(s.X, t.X, u.X, v.X),
            Max4(s.Y, t.Y, u.Y, v.Y)
        );
    }

    public override bool Equals(object obj) => obj is Matrix && Equals((Matrix)obj);

    public bool Equals(Matrix other) =>
        A == other.A && B == other.B && C == other.C && D == other.D && E == other.E && F == other.F;

    public override int GetHashCode()
    {
        int hashCode = 165473199;
        hashCode = hashCode * -1521134295 + A.GetHashCode();
        hashCode = hashCode * -1521134295 + B.GetHashCode();
        hashCode = hashCode * -1521134295 + C.GetHashCode();
        hashCode = hashCode * -1521134295 + D.GetHashCode();
        hashCode = hashCode * -1521134295 + E.GetHashCode();
        hashCode = hashCode * -1521134295 + F.GetHashCode();
        return hashCode;
    }

    public static bool operator ==(Matrix matrix1, Matrix matrix2) => matrix1.Equals(matrix2);

    public static bool operator !=(Matrix matrix1, Matrix matrix2) => !(matrix1 == matrix2);
}
