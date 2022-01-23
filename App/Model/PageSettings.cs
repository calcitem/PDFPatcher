﻿using System.ComponentModel;
using System.Diagnostics;
using System.Xml;
using System.Xml.Serialization;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PDFPatcher.Common;

namespace PDFPatcher.Model;

[XmlRoot(Constants.Content.Page)]
public class PageSettings
{
    /// <summary>Gets or specifies the value of the page range.</summary>
    [XmlAttribute(Constants.PageRange)]
    public string PageRange { get; set; }

    /// <summary>Get or specify the value of the page filtering.</summary>
    [XmlAttribute(Constants.PageFilterTypes.ThisName)]
    public string Filter { get; set; }

    /// <summary>Gets or specifies the value of the page size.</summary>
    [XmlAttribute(Constants.Content.PageSettings.MediaBox)]
    public string PageSize { get; set; }

    /// <summary>Gets or specifies the value of the crop frame.</summary>
    [XmlAttribute(Constants.Content.PageSettings.CropBox)]
    public string CropBox { get; set; }

    /// <summary>Gets or specifies the value of the trim frame.</summary>
    [XmlAttribute(Constants.Content.PageSettings.TrimBox)]
    public string TrimBox { get; set; }

    /// <summary>Gets or specifies the value of the art box.</summary>
    [XmlAttribute(Constants.Content.PageSettings.ArtBox)]
    public string ArtBox { get; set; }

    /// <summary>Gets or specifies the value of the bleid box.</summary>
    [XmlAttribute(Constants.Content.PageSettings.BleedBox)]
    public string BleedBox { get; set; }

    /// <summary>Gets or specifies the value of the angle of rotation.</summary>
    [XmlAttribute(Constants.Content.PageSettings.Rotation)]
    [DefaultValue(0)]
    public int Rotation { get; set; }

    internal static PageSettings FromReader(PdfReader reader, int pageIndex, UnitConverter converter)
    {
        PageSettings s = new();
        Rectangle b = reader.GetPageSize(pageIndex);
        s.PageSize = ConvertPageSize(b, converter);
        b = reader.GetCropBox(pageIndex);
        s.CropBox = b != null ? ConvertPageSize(b, converter) : null;
        b = reader.GetBoxSize(pageIndex, "trim");
        s.TrimBox = b != null ? ConvertPageSize(b, converter) : null;
        b = reader.GetBoxSize(pageIndex, "art");
        s.ArtBox = b != null ? ConvertPageSize(b, converter) : null;
        b = reader.GetBoxSize(pageIndex, "bleed");
        s.BleedBox = b != null ? ConvertPageSize(b, converter) : null;
        s.Rotation = reader.GetPageRotation(pageIndex);
        return s;
    }

    private static string ConvertPageSize(Rectangle b, UnitConverter converter)
    {
        string[] p = new string[4];
        p[0] = converter.FromPoint(b.Left).ToText("0.###");
        p[1] = converter.FromPoint(b.Bottom).ToText("0.###");
        p[2] = converter.FromPoint(b.Right).ToText("0.###");
        p[3] = converter.FromPoint(b.Top).ToText("0.###");
        return string.Join(" ", p);
    }

    internal static bool HavingSameDimension(PageSettings s1, PageSettings s2)
    {
        if (s1 == null && s2 == null)
        {
            return true;
        }

        if (s1 == null || s2 == null)
        {
            return false;
        }

        return s1.Rotation == s2.Rotation && s1.PageSize == s2.PageSize && s1.CropBox == s2.CropBox &&
               s1.TrimBox == s2.TrimBox && s1.BleedBox == s2.BleedBox && s1.ArtBox == s2.ArtBox;
    }

    internal void WriteXml(XmlWriter writer)
    {
        if (string.IsNullOrEmpty(PageRange))
        {
            Debug.WriteLine("Empty page range.");
            return;
        }

        writer.WriteAttributeString(Constants.PageRange, PageRange);
        writer.WriteAttributeString(Constants.Content.PageSettings.MediaBox, PageSize);
        if (CropBox != null)
        {
            writer.WriteAttributeString(Constants.Content.PageSettings.CropBox, CropBox);
        }

        if (TrimBox != null)
        {
            writer.WriteAttributeString(Constants.Content.PageSettings.TrimBox, TrimBox);
        }

        if (ArtBox != null)
        {
            writer.WriteAttributeString(Constants.Content.PageSettings.ArtBox, ArtBox);
        }

        if (BleedBox != null)
        {
            writer.WriteAttributeString(Constants.Content.PageSettings.BleedBox, BleedBox);
        }

        if (Rotation != 0)
        {
            writer.WriteAttributeString(Constants.Content.PageSettings.Rotation, Rotation.ToText());
        }
    }
}
