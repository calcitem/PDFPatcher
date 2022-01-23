﻿using System;
using System.Globalization;
using System.Xml.Serialization;

namespace PDFPatcher.Common;

public class UnitConverter
{
    private const string Null = "null";
    internal const string ToStringFormat = "0.###";

    private int _Precision;
    private float _PreservedValue;

    private string _Unit;

    public UnitConverter()
    {
        Unit = Constants.Units.CM;
        Precision = 3;
    }

    /// <summary>
    ///     Get the unit conversion factor.
    /// </summary>
    [XmlIgnore]
    public float UnitFactor { get; private set; }

    /// <summary>Gets or specifies the value of the conversion accuracy.</summary>
    [XmlIgnore]
    public int Precision
    {
        get => _Precision;
        set
        {
            if (value is < 0 or > 6)
            {
                throw new ArgumentException("转换精度不能小于 0 或大于 6。");
            }

            _Precision = value;
            _PreservedValue = (float)Math.Pow(0.1, _Precision);
        }
    }

    /// <summary>Get or specify the unit of conversion.</summary>
    [XmlAttribute("单位")]
    public string Unit
    {
        get => _Unit;
        set
        {
            float f = ValueHelper.MapValue(value, Constants.Units.Names, Constants.Units.Factors, 0);
            if (f == 0)
            {
                throw new ArgumentException("尺寸单位无效。");
            }

            UnitFactor = f;
            _Unit = value;
        }
    }

    internal float FromPoint(float point) =>
        point < _PreservedValue && point >= 0 // preserve small fragment
            ? point
            : (float)Math.Round(point / UnitFactor, _Precision);

    internal float ToPoint(float value) =>
        value < _PreservedValue && value >= 0 ||
        value >= 10000 // preserve small fragment or extra large values
            ? value
            : (float)Math.Round(value * UnitFactor, _Precision);

    internal static string FromPoint(string point, float unitFactor)
    {
        if (string.IsNullOrEmpty(point) || point == Null)
        {
            return Null;
        }

        if (unitFactor == 1)
        {
            return point;
        }

        if (point.TryParse(out float v))
        {
            return v < 0.01 && v >= 0 // preserve small fragment
                ? point
                : (v / unitFactor).ToString(ToStringFormat, NumberFormatInfo.InvariantInfo);
        }

        return point;
    }

    internal static string FromPoint(float point, float unitFactor) =>
        point < 0.01 && point >= 0 || unitFactor == 1 // preserve small fragment
            ? point.ToString(NumberFormatInfo.InvariantInfo)
            : (point / unitFactor).ToString(ToStringFormat, NumberFormatInfo.InvariantInfo);

    internal static string ToPoint(string value, float unitFactor)
    {
        if (string.IsNullOrEmpty(value) || value == Null)
        {
            return Null;
        }

        if (value.TryParse(out float v))
        {
            return v < 0.01 && v >= 0 // preserve small fragment
                ? value
                : (v * unitFactor).ToString(ToStringFormat, NumberFormatInfo.InvariantInfo);
        }

        return value;
    }

    internal static float ToPoint(float value, float unitFactor) =>
        value < 0.01 && value >= 0 || value >= 10000 // preserve small fragment or extra large values
            ? value
            : value * unitFactor;
}
