﻿using System;
using System.Linq;
using System.Text;
using iTextSharp.text.pdf;

namespace PDFPatcher.Model;

internal sealed class FontInfo : CMapAwareDocumentFont
{
    public const int DefaultDefaultWidth = 1000;

    private static readonly Encoding __GbkEncoding = System.Text.Encoding.GetEncoding("gbk");

    //private readonly static Encoding __Big5Encoding = System.Text.Encoding.GetEncoding ("big5");
    private static readonly PdfName[] __GbkEncodingNames =
    {
        new("GBK-EUC-H"), new("GBK-EUC-V"), new("GB-EUC-H"), new("GB-EUC-V")
    };

    private static readonly string[] __gbkFontNames =
    {
        "Song style", "Hei Style", "Kaiti _GB2312", "Imitation Song style", "Imitation Song style _GB2312",
        "Li Shu", "Young Yuan"
    };

    private static readonly PdfName[] __IdentityEncodingNames = { new("Identity-H"), new("Identity-V") };

    //private readonly static string[] __big5FontNames = new string[] { "MINGLIU" };

    private readonly PdfDictionary _Font;

    private CjkFontType _CjkFontType = CjkFontType.Unknown;

    private PdfDictionary _FontDescriptor;
    private string _FontName;

    public FontInfo(PdfDictionary font, int refNumber)
        : base(font)
    {
        _Font = font;
        FontID = refNumber;
        //this.DefaultWidth = _Font.Locate<PdfArray> (PdfName.DESCENDANTFONTS).Locate<PdfDictionary> (0).Locate<PdfDictionary> (PdfName.FONTDESCRIPTOR).TryGetInt32 (PdfName.W, 1000);
    }

    public FontInfo(PRIndirectReference refFont) : base(refFont)
    {
        _Font = (PdfDictionary)PdfReader.GetPdfObjectRelease(refFont);
        FontID = refFont.Number;
        //this.DefaultWidth = _Font.Locate<PdfArray> (PdfName.DESCENDANTFONTS).Locate<PdfDictionary> (0).Locate<PdfDictionary> (PdfName.FONTDESCRIPTOR).TryGetInt32 (PdfName.W, 1000);
    }

    internal PdfDictionary FontDescriptor
    {
        get
        {
            if (_FontDescriptor != null)
            {
                return _FontDescriptor;
            }

            _FontDescriptor = _Font.Locate<PdfArray>(PdfName.DESCENDANTFONTS).Locate<PdfDictionary>(0)
                .Locate<PdfDictionary>(PdfName.FONTDESCRIPTOR) ?? new PdfDictionary();

            return _FontDescriptor;
        }
    }

    internal string FontName
    {
        get
        {
            if (_FontName != null)
            {
                return _FontName;
            }

            PdfName f = FontDescriptor.GetAsName(PdfName.FONTNAME);
            if (f != null)
            {
                _FontName = PdfName.DecodeName(f.ToString());
            }
            else
            {
                string fn = PostscriptFontName;
                int i = fn.LastIndexOf(',');
                if (i != -1)
                {
                    fn = fn.Substring(0, i);
                }

                _FontName = fn;
            }

            // Delete the name of the subset
            _FontName = PdfDocumentFont.RemoveSubsetPrefix(_FontName);

            return _FontName;
        }
    }

    internal CjkFontType CjkType
    {
        get
        {
            if (_CjkFontType == CjkFontType.Unknown)
            {
                InitCjkFontType();
            }

            return _CjkFontType;
        }
    }

    internal int FontID { get; } = -1;

    private void InitCjkFontType()
    {
        if (_Font.Contains(PdfName.TOUNICODE))
        {
            _CjkFontType = CjkFontType.None;
            return;
        }

        PdfName encoding = _Font.GetAsName(PdfName.ENCODING);
        string fn = FontName.ToUpperInvariant();
        bool c = __gbkFontNames.Contains(fn) || __GbkEncodingNames.Contains(encoding);
        //&& PdfName.WIN_ANSI_ENCODING.Equals (this.fontDict.GetAsName (PdfName.ENCODING));
        _CjkFontType = c ? CjkFontType.Chinese : CjkFontType.None;
        if (_CjkFontType != CjkFontType.None)
        {
            return;
        }

        c = __IdentityEncodingNames.Contains(encoding);
        if (c)
        {
            _CjkFontType = CjkFontType.Unicode;
        }
        //c = Common.Range.InCollection (fn, __big5FontNames);
        //_CjkFontType = c ? CjkFontType.Big5Chinese : CjkFontType.None;
    }

    internal string DecodeTextBytes(byte[] bytes)
    {
        if (AppContext.Encodings.TextEncoding != null)
        {
            return AppContext.Encodings.TextEncoding.GetString(bytes);
        }

        return CjkType == CjkFontType.Chinese ? __GbkEncoding.GetString(bytes) : Decode(bytes, 0, bytes.Length);

        //else if (CjkType == CjkFontType.Big5Chinese) {
        //	return __Big5Encoding.GetString (bytes);
        //}
    }

    internal string DecodeText(PdfString text) => DecodeTextBytes(text.GetBytes());

    [Flags]
    internal enum CjkFontType
    {
        Unknown,
        CJK = 0x01,
        Chinese = 0x02 + CJK,
        Korean = 0x08 + CJK,
        Unicode = 0x4000,
        None = 0x8000
    }
}
