﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using iTextSharp.text.pdf;
using PDFPatcher.Common;
using PDFPatcher.Model;

namespace PDFPatcher.Processor;

/// <summary>A class which manages outlines (bookmarks) of PDF documents.</summary>
internal static class OutlineManager
{
    // modifed: added split array for action parameters
    private static readonly char[] __fullWidthNums = "０１２３４５６７８９".ToCharArray();
    private static readonly char[] __halfWidthNums = "0123456789".ToCharArray();
    private static readonly char[] __cmdIdentifiers = { '=', '﹦', '＝', ':', '：' };
    private static readonly char[] __pageLabelSeparators = { ';', '；', ',', '，', ' ' };

    private static void BookmarkDepth(PdfActionExporter exporter, PdfDictionary outline,
        Dictionary<int, int> pageRefMap, XmlWriter target)
    {
        while (outline != null)
        {
            target.WriteStartElement(Constants.Bookmark);

            target.WriteAttributeString(Constants.BookmarkAttributes.Title,
                StringHelper.ReplaceControlAndBomCharacters(outline.GetAsString(PdfName.TITLE)
                    .Decode(AppContext.Encodings.BookmarkEncoding))
            );

            PdfArray color = outline.Locate<PdfArray>(PdfName.C);
            DocInfoExporter.ExportColor(color, target);

            PdfNumber style = outline.Locate<PdfNumber>(PdfName.F);
            if (style != null)
            {
                int f = style.IntValue & 0x03;
                if (f > 0)
                {
                    target.WriteAttributeString(Constants.BookmarkAttributes.Style,
                        Constants.BookmarkAttributes.StyleType.Names[f]);
                }
            }

            if (outline.Get(PdfName.COUNT) is PdfNumber count)
            {
                target.WriteAttributeString(Constants.BookmarkAttributes.Open,
                    count.IntValue < 0 ? Constants.Boolean.False : Constants.Boolean.True);
            }

            PdfObject dest = outline.Locate<PdfObject>(PdfName.DEST);
            if (dest != null)
            {
                exporter.ExportGotoAction(dest, target, pageRefMap);
            }
            else
            {
                exporter.ExportAction(outline.Locate<PdfDictionary>(PdfName.A), pageRefMap, target);
            }

            PdfDictionary first = outline.Locate<PdfDictionary>(PdfName.FIRST);
            if (first != null)
            {
                BookmarkDepth(exporter, first, pageRefMap, target);
            }

            outline = outline.Locate<PdfDictionary>(PdfName.NEXT);
            target.WriteEndElement();
        }
    }

    /// <summary>
    ///     Export bookmarks from PDFs as XML elements.
    /// </summary>
    public static XmlElement GetBookmark(PdfReader reader, UnitConverter unitConverter)
    {
        PdfDictionary catalog = reader.Catalog;
        PdfDictionary outlines = catalog.Locate<PdfDictionary>(PdfName.OUTLINES);
        if (outlines == null)
        {
            return null;
        }

        if (unitConverter == null)
        {
            throw new NullReferenceException("unitConverter");
        }

        Dictionary<int, int> pages = reader.GetPageRefMapper();
        XmlDocument doc = new();
        doc.AppendElement(Constants.DocumentBookmark);
        using (XmlWriter w = doc.DocumentElement.CreateNavigator().AppendChild())
        {
            PdfActionExporter a = new(unitConverter);
            BookmarkDepth(a,
                (PdfDictionary)PdfReader.GetPdfObjectRelease(outlines.Get(PdfName.FIRST)),
                pages,
                w);
        }

        return doc.DocumentElement;
    }

    private static object[] CreateOutlines(PdfWriter writer, PdfObject parent, XmlNode kids, int maxPageNumber,
        bool namedAsNames)
    {
        XmlNodeList bookmarks = kids.SelectNodes(Constants.Bookmark);
        PdfIndirectReference[] refs = new PdfIndirectReference[bookmarks.Count];
        for (int k = 0; k < refs.Length; ++k)
        {
            refs[k] = writer.PdfIndirectReference;
        }

        int ptr = 0;
        int count = 0;
        foreach (XmlElement child in bookmarks)
        {
            object[] lower = null;
            if (child.SelectSingleNode(Constants.Bookmark) != null)
            {
                lower = CreateOutlines(writer, refs[ptr], child, maxPageNumber, namedAsNames);
            }

            PdfDictionary outline = new();
            ++count;
            if (lower != null)
            {
                outline.Put(PdfName.FIRST, (PdfIndirectReference)lower[0]);
                outline.Put(PdfName.LAST, (PdfIndirectReference)lower[1]);
                int n = (int)lower[2];
                // Bookmark by default
                if (child.GetAttribute(Constants.BookmarkAttributes.Open) != Constants.Boolean.True)
                {
                    outline.Put(PdfName.COUNT, -n);
                }
                else
                {
                    outline.Put(PdfName.COUNT, n);
                    count += n;
                }
            }

            outline.Put(PdfName.PARENT, parent);
            if (ptr > 0)
            {
                outline.Put(PdfName.PREV, refs[ptr - 1]);
            }

            if (ptr < refs.Length - 1)
            {
                outline.Put(PdfName.NEXT, refs[ptr + 1]);
            }

            outline.Put(PdfName.TITLE, child.GetAttribute(Constants.BookmarkAttributes.Title));
            DocInfoImporter.ImportColor(child, outline);
            string style = child.GetAttribute(Constants.BookmarkAttributes.Style);
            if (string.IsNullOrEmpty(style) == false)
            {
                int bits = Array.IndexOf(Constants.BookmarkAttributes.StyleType.Names, style);
                if (bits == -1)
                {
                    bits = 0;
                }

                if (bits != 0)
                {
                    outline.Put(PdfName.F, bits);
                }
            }

            DocInfoImporter.ImportAction(writer, outline, child, maxPageNumber, namedAsNames);
            writer.AddToBody(outline, refs[ptr]);
            ++ptr;
        }

        return new object[] { refs[0], refs[refs.Length - 1], count };
    }

    internal static PdfIndirectReference WriteOutline(PdfWriter writer, XmlElement bookmarks, int maxPageNumber)
    {
        if (bookmarks?.SelectSingleNode(Constants.Bookmark) == null)
        {
            return null;
        }

        PdfDictionary top = new();
        PdfIndirectReference topRef = writer.PdfIndirectReference;
        object[] kids = CreateOutlines(writer, topRef, bookmarks, maxPageNumber, false);
        top.Put(PdfName.TYPE, PdfName.OUTLINES);
        top.Put(PdfName.FIRST, (PdfIndirectReference)kids[0]);
        top.Put(PdfName.LAST, (PdfIndirectReference)kids[1]);
        top.Put(PdfName.COUNT, (int)kids[2]);
        writer.AddToBody(top, topRef);
        writer.ExtraCatalog.Put(PdfName.OUTLINES, topRef);
        return topRef;
    }

    internal static void KillOutline(PdfReader source)
    {
        PdfDictionary catalog = source.Catalog;
        PdfObject o = catalog.Get(PdfName.OUTLINES);
        if (o == null)
        {
            return;
        }

        {
            PRIndirectReference outlines = o as PRIndirectReference;
            OutlineTravel(outlines);
            PdfReader.KillIndirect(outlines);
        }

        catalog.Remove(PdfName.OUTLINES);
        PdfReader.KillIndirect(catalog.Get(PdfName.OUTLINES));
        if (PdfName.USEOUTLINES.Equals(catalog.GetAsName(PdfName.PAGEMODE)))
        {
            catalog.Remove(PdfName.PAGEMODE);
        }
    }

    private static void OutlineTravel(PRIndirectReference outline)
    {
        while (outline != null)
        {
            PdfDictionary outlineR = (PdfDictionary)PdfReader.GetPdfObjectRelease(outline);
            PdfReader.KillIndirect(outline);
            if (outlineR != null)
            {
                PRIndirectReference first = (PRIndirectReference)outlineR.Get(PdfName.FIRST);
                if (first != null)
                {
                    OutlineTravel(first);
                }

                PdfReader.KillIndirect(outlineR.Get(PdfName.DEST));
                PdfReader.KillIndirect(outlineR.Get(PdfName.A));
                outline = (PRIndirectReference)outlineR.Get(PdfName.NEXT);
            }
            else
            {
                outline = null;
            }
        }
    }

    internal static void ImportSimpleBookmarks(TextReader source, PdfInfoXmlDocument target)
    {
        string indentString = "\t";
        bool isOpen = false; // Whether the bookmark is open by default
        int pageOffset = 0;
        int currentIndent = -1;
        int lineNum = 0;
        Regex pattern = new(@"(.+?)[\s\.…　\-_]*(-?[0-9０１２３４５６７８９]+)?\s*$", RegexOptions.Compiled);
        DocumentInfoElement docInfo = target.InfoNode;
        BookmarkRootElement root = target.BookmarkRoot;
        XmlElement pageLabels = target.PageLabelRoot;
        BookmarkContainer currentBookmark = root;
        while (source.Peek() != -1)
        {
            string s = source.ReadLine();
            lineNum++;
            if (s.Trim().Length == 0)
            {
                continue;
            }

            int p;
            if ((s[0] == '#' || s[0] == '＃') && (p = s.IndexOfAny(__cmdIdentifiers)) != -1)
            {
                string cmd = s.Substring(1, p - 1);
                string cmdData = s.Substring(p + 1);
                switch (cmd)
                {
                    case "Home Page Number":
                        if (cmdData.TryParse(out pageOffset))
                        {
                            Tracker.TraceMessage("Home page number changed to " + pageOffset);
                            pageOffset--;
                        }

                        break;
                    case "Indent Marker":
                        indentString = cmdData;
                        Tracker.TraceMessage(string.Concat("Indentation mark changed to \"", indentString, "\""));
                        break;
                    case "version":
                        if (lineNum == 1)
                        {
                            string v = cmdData.Trim();
                            target.DocumentElement.SetAttribute(Constants.Info.ProductVersion, v);
                            Tracker.TraceMessage("Import simple bookmark file, version: " + v);
                        }

                        break;
                    case "Open bookmark":
                        cmdData = cmdData.ToLowerInvariant();
                        isOpen = cmdData is "yes" or "true" or "y" or "yes" or "1";
                        break;
                    case Constants.Info.DocumentPath:
                        target.PdfDocumentPath = cmdData.Trim();
                        break;
                    case Constants.PageLabels:
                        string[] l = cmdData.Split(__pageLabelSeparators, 3);
                        if (l.Length < 1)
                        {
                            Tracker.TraceMessage(Constants.PageLabels +
                                                 "Incorrect format, at least start page number should be specified.");
                            continue;
                        }

                        if (l[0].TryParse(out int pn) == false || pn < 1)
                        {
                            Tracker.TraceMessage(Constants.PageLabels +
                                                 "Incorrect format: starting page number should be a positive integer.");
                            continue;
                        }

                        string style = l[1].Length > 0
                            ? ValueHelper.MapValue(l[1][0],
                                Constants.PageLabelStyles.SimpleInfoIdentifiers,
                                Constants.PageLabelStyles.Names,
                                Constants.PageLabelStyles.Names[1])
                            : Constants.PageLabelStyles.Names[1];
                        string prefix = l.Length > 2 ? l[2] : null;
                        XmlElement pl = target.CreateElement(Constants.PageLabelsAttributes.Style);
                        pl.SetAttribute(Constants.PageLabelsAttributes.PageNumber, pn.ToText());
                        pl.SetAttribute(Constants.PageLabelsAttributes.Style, style);
                        if (string.IsNullOrEmpty(prefix) == false)
                        {
                            pl.SetAttribute(Constants.PageLabelsAttributes.Prefix, prefix);
                        }

                        pageLabels.AppendChild(pl);
                        continue;
                    case Constants.Info.Title:
                    case Constants.Info.Subject:
                    case Constants.Info.Keywords:
                    case Constants.Info.Author:
                        docInfo.SetAttribute(cmd, cmdData);
                        break;
                }

                continue;
            }

            int indent = p = 0;
            while (s.IndexOf(indentString, p, StringComparison.Ordinal) == p)
            {
                p += indentString.Length;
                indent++;
            }

            Match m = pattern.Match(s, p);
            if (m.Success == false)
            {
                continue;
            }

            string title = m.Groups[1].Value;
            string pnText = m.Groups[2].Value;
            int pageNum;
            if (pnText.Length == 0)
            {
                pageNum = 0;
            }
            else
            {
                if (pnText.IndexOfAny(__fullWidthNums) != -1)
                {
                    char[] digits = Array.ConvertAll(m.Groups[2].Value.ToCharArray(),
                        d => ValueHelper.MapValue(d, __fullWidthNums, __halfWidthNums, d));
                    pnText = new string(digits, 0, digits.Length);
                }

                if (pnText.TryParse(out pageNum))
                {
                    pageNum += pageOffset;
                }
            }

            BookmarkElement bookmark = target.CreateBookmark();
            if (indent == currentIndent)
            {
                currentBookmark.ParentNode.AppendChild(bookmark);
            }
            else if (indent > currentIndent)
            {
                currentBookmark.AppendChild(bookmark);
                if (indent - currentIndent > 1)
                {
                    throw new FormatException(string.Concat("In Simple Bookmark", lineNum,
                        "The indentation format of the line is incorrect.\n\nNote: Subordinate bookmarks can only have at most one indentation mark more than the superior bookmark."));
                }

                currentIndent++;
            }
            else /* indent < currentIndent */
            {
                while (currentIndent > indent && currentBookmark.ParentNode != root)
                {
                    currentBookmark = currentBookmark.ParentNode as BookmarkContainer;
                    currentIndent--;
                }

                currentBookmark.ParentNode.AppendChild(bookmark);
            }

            bookmark.Title = title;
            if (isOpen == false)
            {
                bookmark.IsOpen = false;
            }

            if (pageNum > 0)
            {
                bookmark.Page = pageNum;
            }

            currentBookmark = bookmark;
        }
    }

    internal static void ImportSimpleBookmarks(string path, PdfInfoXmlDocument target)
    {
        using TextReader r = new StreamReader(path, DetectEncoding(path));
        ImportSimpleBookmarks(r, target);
    }

    public static void WriteSimpleBookmarkInstruction(TextWriter writer, string item, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        writer.Write("#");
        writer.Write(item);
        writer.Write("=");
        writer.WriteLine(value);
    }

    /// <summary>
    ///     The XML bookmark is output as a simple bookmark.
    /// </summary>
    /// <param name="writer">Output target.</param>
    /// <param name="container">Bookmark node.</param>
    /// <param name="indent">Regeneration.</param>
    /// <param name="indentChar">indent the string.</param>
    public static void WriteSimpleBookmark(TextWriter writer, BookmarkContainer container, int indent,
        string indentChar)
    {
        foreach (BookmarkElement item in container.SubBookmarks)
        {
            for (int i = 0; i < indent; i++)
            {
                writer.Write(indentChar);
            }

            writer.Write(item.Title);
            writer.Write("\t\t");
            writer.Write(item.Page.ToText());
            writer.WriteLine();
            WriteSimpleBookmark(writer, item, indent + 1, indentChar);
        }
    }

    private static Encoding DetectEncoding(string path)
    {
        const string VersionString = "#version";
        const string VersionString2 = "#Version";

        byte[] b = new byte[20];
        using (FileStream r = new(path, FileMode.Open))
        {
            if (r.Length < b.Length)
            {
                throw new FormatException("Insufficient content in the simple bookmark file.");
            }

            r.Read(b, 0, b.Length);
        }

        foreach (Encoding item in Constants.Encoding.Encodings)
        {
            if (item == null)
            {
                continue;
            }

            string s = item.GetString(b);
            if (s.StartsWith(VersionString, StringComparison.Ordinal) ||
                s.StartsWith(VersionString2, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return Encoding.Default;
    }
}
