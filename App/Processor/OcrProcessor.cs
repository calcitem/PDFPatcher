﻿#if DEBUG
#define DEBUGOCR
#endif
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml;
using FreeImageAPI;
using iTextSharp.text.pdf;
using PDFPatcher.Common;
using PDFPatcher.Model;
using PDFPatcher.Processor.Imaging;
using Rectangle = iTextSharp.text.Rectangle;

namespace PDFPatcher.Processor;

internal sealed class OcrProcessor
{
    private const int OpenWorkload = 1;
    private const string __punctuations = @"·．“”，,\.－""～∼。:：\p{P}";

    private static readonly AutoBookmarkOptions __MergeOptions =
        new() { MergeAdjacentTitles = true, MergeDifferentSizeTitles = true };

    private static readonly Regex __ContentPunctuationExpression =
        new(@"[" + __punctuations + @"][" + __punctuations + @"0一\s]+[" + __punctuations + @"]\s*",
            RegexOptions.Compiled);

    private static readonly Regex __ContinuousWhiteSpaceExpression = new(@"[ 　]{3,}", RegexOptions.Compiled);

    private static readonly Regex __WhiteSpaceBetweenChineseCharacters =
        new(@"([\u4E00-\u9FFF\u3400-\u4DBF])[ 　]+(?=[\u4E00-\u9FFF\u3400-\u4DBF])", RegexOptions.Compiled);

    private readonly ModiOcr _Ocr;
    private readonly ImageExtractor _ocrImageExp;
    private readonly float _OcrQuantitativeFactor;
    private readonly OcrOptions _options;
    private readonly PdfReader _reader;
    private IResultWriter _resultWriter;

    public OcrProcessor(PdfReader reader, OcrOptions options) : this(options)
    {
        ImageExtracterOptions expOptions = new()
        {
            OutputPath = Path.GetTempPath(),
            FileMask = "\"ocr-" + DateTime.Now.ToString("yyMMddHHmmss") + "-\"0000",
            MergeImages = true,
            MergeJpgToPng = true,
            MinHeight = 100,
            MinWidth = 100
        };
        CleanUpTempFiles(expOptions.OutputPath);
        _reader = reader;
        _ocrImageExp = new ImageExtractor(expOptions) { PrintImageLocation = false };
    }

    private OcrProcessor(OcrOptions options)
    {
        _Ocr = new ModiOcr
        {
            LangID = options.OcrLangID,
            StretchPage = options.StretchPage,
            OrientPage = options.OrientPage,
            WritingDirection = options.WritingDirection
        };
        _OcrQuantitativeFactor = options.QuantitativeFactor;
        _options = options;
    }

    private static void CleanUpTempFiles(string folderPath)
    {
        string[] tf = Directory.GetFiles(folderPath, "ocr-*.tif");
        foreach (string file in tf)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception)
            {
            }
        }
    }

    internal int EstimateWorkload()
    {
        int n = _reader.NumberOfPages;
        int load = 0;
        load += OpenWorkload;
        int t = PageRangeCollection.Parse(_options.PageRanges, 1, n, true).TotalPages;
        load += t > 0 ? t : n;
        return load;
    }

    internal void SetWriter(XmlWriter writer) => _resultWriter = new XmlResultWriter(writer);

    internal void SetWriter(TextWriter writer) => _resultWriter = new TextResultWriter(writer);

    internal void PerformOcr()
    {
        Tracker.IncrementProgress(OpenWorkload);
        PageRangeCollection ranges = PageRangeCollection.Parse(_options.PageRanges, 1, _reader.NumberOfPages, true);
        __MergeOptions.DetectColumns = _options.DetectColumns;
        __MergeOptions.WritingDirection = _options.WritingDirection;
        TextLine.DefaultDirection = _options.WritingDirection;
        if (FileHelper.IsPathValid(_options.SaveOcredImagePath))
        {
            File.Delete(_options.SaveOcredImagePath);
        }

        List<int> el = new();
        foreach (PageRange r in ranges)
        {
            for (int i = r.StartValue; i <= r.EndValue; i++)
            {
                Tracker.TraceMessage("Recognizing page " + i + ".");
                IList<Result> or = OcrPage(i, el);
                if (or.Count > 0)
                {
                    _resultWriter?.BeginWritePage(i);

                    foreach (Result result in or)
                    {
                        _resultWriter?.BeginWriteImage(result.Image);
                        if (_options.OutputOriginalOcrResult)
                        {
                            if (_resultWriter != null)
                            {
                                foreach (TextLine item in result.Texts)
                                {
                                    _resultWriter.WriteText(item, null);
                                }
                            }
                        }
                        else
                        {
                            WriteOcrResult(i, result);
                        }

                        _resultWriter?.EndWriteImage();
                    }

                    _resultWriter?.EndWritePage();
                }

                Tracker.IncrementProgress(1);
            }
        }

        if (el.Count > 0)
        {
            Tracker.TraceMessage(Tracker.Category.Alert,
                string.Concat("Yes", el.Count,
                    "The page has an error in the identification process, the page number is: ",
                    string.Join(", ", el)));
        }
    }

    private void WriteOcrResult(int i, Result result)
    {
        SortRecognizedText(result.Texts, _options);
        Rectangle pr = _reader.GetPageNRelease(i).GetPageVisibleRectangle();
        List<TextLine> tl = _options.WritingDirection != WritingDirection.Unknown
            ? AutoBookmarkCreator.MergeTextInfoList(pr,
                result.Texts.ConvertAll(GetMergedTextInfo),
                __MergeOptions) // Reorganize the text according to the writing direction
            : result.Texts;
        foreach (TextLine item in tl)
        {
            string t = item.Text;
            Bound ir = item.Region;

            t = CleanUpText(t, _options);
            if (_options.PrintOcrResult)
            {
#if DEBUG
                Tracker.TraceMessage(string.Concat(item.Direction.ToString()[0], ir.Top.ToString(" 0000"), ',',
                    ir.Left.ToString("0000"), "(", ir.Width.ToString("0000"), ',', ir.Height.ToString("0000"), ")\t",
                    t));
#else
					Tracker.TraceMessage (t);
#endif
            }

            _resultWriter?.WriteText(item, t);
        }
    }

    /// <summary>
    ///     Optimize the output based on the identification option.
    /// </summary>
    /// <param name="text">text content.</param>
    /// <param name="options">Identification option.</param>
    /// <returns>Optimized text.</returns>
    internal static string CleanUpText(string text, OcrOptions options)
    {
        if (options.DetectContentPunctuations)
        {
            text = __ContentPunctuationExpression.Replace(text, " .... ");
        }

        if (options.CompressWhiteSpaces)
        {
            text = __ContinuousWhiteSpaceExpression.Replace(text, "  ");
        }

        if (options.RemoveWhiteSpacesBetweenChineseCharacters)
        {
            text = __WhiteSpaceBetweenChineseCharacters.Replace(text, "$1");
        }

        return text;
    }

    private IList<Result> OcrPage(int i, ICollection<int> errorList)
    {
#if DEBUGOCR
        Tracker.TraceMessage("Export the picture on page " + i + ".");
#endif
        _ocrImageExp.ExtractPageImages(_reader, i);
#if DEBUGOCR
        Tracker.TraceMessage("Finished exporting the picture on page " + i + ".");
#endif
        List<Result> or = new();
        try
        {
            foreach (Result r in _ocrImageExp.PosList.Select(item => new Result(item)))
            {
                OcrPage(r);
                or.Add(r);
            }
        }
        catch (COMException ex)
        {
            string err = null;
            switch (ex.ErrorCode)
            {
                case -959967087:
                    err = "The image on the page does not contain recognizable text.";
                    goto default;
                default:
                    Tracker.TraceMessage(Tracker.Category.Error, "Error performing OCR on page " + i + ": ");
                    if (err != null)
                    {
                        Tracker.TraceMessage(err);
                    }
                    else
                    {
                        Tracker.TraceMessage("Error number: " + ex.ErrorCode);
                        Tracker.TraceMessage(ex);
                        errorList.Add(i);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            Tracker.TraceMessage(Tracker.Category.Error, "Error performing OCR on page " + i + ": ");
            Tracker.TraceMessage(ex);
            errorList.Add(i);
        }
        finally
        {
            foreach (ImageInfo item in _ocrImageExp.InfoList.Where(item => !string.IsNullOrEmpty(item.FileName)))
            {
                try
                {
                    File.Delete(item.FileName);
                }
                catch (Exception ex)
                {
                    Tracker.TraceMessage(Tracker.Category.Error, ex.Message);
                    Tracker.TraceMessage(Tracker.Category.Error,
                        "Unable to delete the temporary file generated during the identification: " + item.FileName);
                }
            }
        }

        return or;
    }

    private void OcrPage(Result result)
    {
        ImageDisposition image = result.Image;
        string p = image.Image.FileName;
        if (string.IsNullOrEmpty(p))
        {
            return;
        }
#if DEBUGOCR
        Tracker.TraceMessage("Identify picture: " + p);
#endif
#if DEBUG
        Tracker.TraceMessage(p);
#endif
        result.Texts.Clear();
        OcrImageFile(result.Texts, p);

        #region Legacy code

        //            var ll = new List<TextLine> ();
        //            // Width width minimum
        //            var cw = image.Image.Width / 4;

        //            // Traverse the TextInfo that identifies the resulting, clustering it into a line using the minimum distance clustering method
        //            foreach (var item in or) {
        //                var ir = item.Region;
        //                DistanceInfo cd = null; // Distance from TextInfo to TextLine
        //                DistanceInfo md = new DistanceInfo (DistanceInfo.Placement.Unknown, float.MaxValue); // shortest distance
        //                TextLine ml = null; // Minimum distance TextLine

        //if (item.Text == "哉") {
        //    var lxx = 1;
        //}
        //                // Ask minimum distances of TextLine
        //                foreach (var li in ll) {
        //                    cd = li.GetDistance (ir);
        //                    if ((cd.Location == DistanceInfo.Placement.Overlapping // Current items overlap
        //                            && (md.Location != DistanceInfo.Placement.Overlapping // Minimum distance is not overlapping
        //                                || cd.Distance < md.Distance) // The overlap center of the current item and the text line is less than the minimum distance.
        //                            )
        //                        || ((md.Location == DistanceInfo.Placement.Unknown // Minimum distance without knowing
        //                            || (cd.Location != DistanceInfo.Placement.Overlapping
        //                                && md.Location != DistanceInfo.Placement.Overlapping
        //                                && cd.Distance < md.Distance) // The distance between the current item and the text line is less than the minimum distance.
        //                            )
        //                            && (((cd.Location == DistanceInfo.Placement.Left || cd.Location == DistanceInfo.Placement.Right) // The relative position is horizontal
        //                                    && li.Direction != TextLine.WritingDirection.Vertical // Text line direction is not a portrait
        //                                    )
        //                                || ((cd.Location == DistanceInfo.Placement.Up || cd.Location == DistanceInfo.Placement.Down) // The relative position is vertical
        //                                    && li.Direction != TextLine.WritingDirection.Horizontal // Text line is not horizontal
        //                                    )
        //                                )
        //                            && cd.Distance < cw
        //                        )
        //                        ) {
        //                        md = cd;
        //                        ml = li;
        //                    }
        //                }

        //                if (ml != null) {
        //                    // If there is a minimum distance of TextLine and can be merged, you will be classified into TextLine.
        //                    if (md.Location == DistanceInfo.Placement.Overlapping) {
        //                        // Check if there is an overlapping duplicate text
        //                        foreach (var t in ml.Texts) {
        //                            if (t.Region.IntersectWith (item.Region) // Item is overlapped in TextLine
        //                                && (t.Text.Contains (item.Text) || item.Text.Contains (t.Text) // Overlapping text and item text
        //                                )
        //                                ) {
        //                                goto Next; // Ignore this item
        //                            }
        //                        }
        //                    }
        //                    ml.AddText (item);
        //                }
        //                else {
        //                    // Otherwise, create new TextLine with item
        //                    ll.Add (new TextLine (item));
        //                }
        //            Next:
        //                continue;
        //            }

        //if (or.Count > 0) {
        //    float size = 0, size2 = 0, avgSize, maxSize = 0;
        //    float top = or[0].Region.Top, bottom = or[0].Region.Bottom, left = or[0].Region.Left, right = 0;
        //    var sb = new System.Text.StringBuilder ();
        //    int letterCount = 0;
        //    var rr = new List<TextInfo> ();
        //    Bound r;
        //    var end = or.Count - 1;
        //    for (var i = 0; i <= end; i++) {
        //        var item = or[i];
        //        r = item.Region;
        //        avgSize = letterCount > 0 ? size / letterCount : maxSize;
        //    AddLine:
        //        if (r.Top > bottom + 0.2f * (avgSize) || i > end) { // New line
        //            size = image.YScale * avgSize;
        //            if (_OcrQuatitiveFactor > 0) {
        //                var a = Math.IEEERemainder (size, _OcrQuatitiveFactor);
        //                var b = Math.IEEERemainder (size + _OcrQuatitiveFactor, _OcrQuatitiveFactor);
        //                if (a < b) {
        //                    size -= (float)a;
        //                }
        //                else {
        //                    size += _OcrQuatitiveFactor - (float)b;
        //                }
        //            }
        //            if (size >= _fontSizeThreshold) {
        //                var ni = new TextInfo ()
        //                {
        //                    Text = sb.ToString (),
        //                    Size = size,
        //                    Region = new Bound (
        //                        image.X + image.XScale * left,
        //                        image.Y + image.YScale * (image.Image.Height - bottom),
        //                        image.X + image.XScale * right,
        //                        image.Y + image.YScale * (image.Image.Height - top)),
        //                    Font = -1
        //                };
        //                rr.Add (ni);
        //            }
        //            maxSize = size = size2 = 0;
        //            left = r.Left;
        //            right = r.Right;
        //            top = r.Top;
        //            bottom = r.Bottom;
        //            sb.Length = 0;
        //            letterCount = 0;
        //            if (i > end) {
        //                break;
        //            }
        //        }
        //        if (Char.IsLetter (item.Text[0])) {
        //            size += item.Size;
        //            size2 += item.Size * item.Size;
        //            letterCount++;
        //        }
        //        if (item.Size > maxSize) {
        //            maxSize = item.Size;
        //        }
        //        if (r.Top < top) {
        //            top = r.Top;
        //        }
        //        if (r.Bottom > bottom) {
        //            bottom = r.Bottom;
        //        }
        //        if (r.Right > right) {
        //            right = r.Right;
        //        }
        //        sb.Append (item.Text);
        //        if (i == end) {
        //            i++;
        //            goto AddLine;
        //        }
        //    }
        //    this._TextList.AddRange (rr);
        //}

        #endregion
    }

    private void OcrImageFile(List<TextLine> result, string p)
    {
        string sp = _options.SaveOcredImagePath;
        if (FileHelper.HasExtension(p, Constants.FileExtensions.Tif) == false)
        {
            using FreeImageBitmap fi = new(p);
#if !DEBUG
					var t = Path.GetDirectoryName (p) + "\\ocr-" + new Random ().Next ().ToText () +".tif";
#else
            const string t = "m:\\ocr.tif";
#endif
            if (_options.PreserveColor)
            {
                fi.Save(t, FREE_IMAGE_FORMAT.FIF_TIFF);
            }
            else
            {
                using FreeImageBitmap ti =
                    fi.GetColorConvertedInstance(FREE_IMAGE_COLOR_DEPTH.FICD_01_BPP_THRESHOLD);
                ti.Save(t, FREE_IMAGE_FORMAT.FIF_TIFF);
            }

            _Ocr.Ocr(t, sp, result);
#if !DEBUG
					try {
						File.Delete (t);
					}
					catch (Exception) {
						Tracker.TraceMessage (Tracker.Category.Notice, "Unable to delete temporary files:" + t);
					}
#endif
        }
        else
        {
            _Ocr.Ocr(p, sp, result);
        }
#if DEBUGOCR
        Tracker.TraceMessage("Complete identification picture:" + p);
#endif
    }

    /// <summary>
    ///     Call the image processing engine identification bitmap. If the amount of text in the picture is too small, it will
    ///     not be recognized and an exception will be thrown.
    /// </summary>
    /// <param name="bmp">a picture that needs to be identified.</param>
    /// <param name="options">Identification option.</param>
    /// <exception cref="System.Runtime.InteropServices.COMException"> An error occurred during identification.</exception>
    /// <returns>Identified text.</returns>
    internal static List<TextLine> OcrBitmap(Bitmap bmp, OcrOptions options)
    {
        const int minSize = 500;
        OcrProcessor ocr = new(options);
        List<TextLine> r = new();
        string p;
        using (FreeImageBitmap fi = new(bmp))
        {
            if (fi.Width < minSize || fi.Height < minSize)
            {
                fi.EnlargeCanvas<RGBQUAD>(0, 0, fi.Width < minSize ? minSize : fi.Width,
                    fi.Height < minSize ? minSize : fi.Height, new RGBQUAD(fi.GetPixel(0, 0)));
            }

            p = FileHelper.CombinePath(Path.GetTempPath(),
                new Random().Next(int.MaxValue).ToText() + Constants.FileExtensions.Tif);
            fi.Save(p, FREE_IMAGE_FORMAT.FIF_TIFF);
        }

        ocr._Ocr.Ocr(p, null, r);
        File.Delete(p);
        return r;
    }

    private static TextInfo GetMergedTextInfo(TextLine item)
    {
        TextInfo ti = new()
        {
            Font = null,
            Region = item.Region,
            Text = item.Text,
            Size = (float)Math.Round(item.Direction == WritingDirection.Vertical
                ? item.Region.Width /* * image.XScale*/
                : item.Region.Height /* * image.YScale*/),
            LetterWidth = item.GetAverageCharSize()
        };
        //if (item.Texts.Count > 0) {
        //    float aw = 0;
        //    foreach (var t in item.Texts) {
        //        aw += t.LetterWidth;
        //    }
        //    aw /= item.Texts.Count;
        //    ti.LetterWidth = aw;
        //}
        return ti;
    }

    private static void SortRecognizedText(List<TextLine> list, OcrOptions ocrOptions)
    {
        switch (ocrOptions.WritingDirection)
        {
            case WritingDirection.Horizontal:
                list.Sort((a, b) =>
                {
                    Bound ra = a.Region;
                    Bound rb = b.Region;
                    if (ra.Bottom > rb.Top)
                    {
                        return 1;
                    }

                    if (ra.Top < rb.Bottom)
                    {
                        return -1;
                    }

                    if (ra.IsAlignedWith(rb, WritingDirection.Horizontal))
                    {
                        return ra.Center < rb.Center ? -1 : 1;
                    }

                    return ra.Middle < rb.Middle ? -1 : 1;
                });
                break;
            case WritingDirection.Vertical:
                list.Sort((a, b) =>
                {
                    Bound ra = a.Region;
                    Bound rb = b.Region;
                    if (ra.Left > rb.Right)
                    {
                        return -1;
                    }

                    if (ra.Right < rb.Left)
                    {
                        return 1;
                    }

                    if (ra.IsAlignedWith(rb, WritingDirection.Vertical))
                    {
                        return ra.Middle > rb.Middle ? -1 : 1;
                    }

                    return ra.Center > rb.Center ? -1 : 1;
                });
                break;
        }
    }

    internal sealed class Result
    {
        public Result(ImageDisposition image)
        {
            Image = image;
            Texts = new List<TextLine>();
        }

        public ImageDisposition Image { get; }
        public List<TextLine> Texts { get; }
    }

    private interface IResultWriter
    {
        void BeginWritePage(int i);
        void BeginWriteImage(ImageDisposition image);
        void WriteText(TextLine text, string optimizedText);
        void EndWriteImage();
        void EndWritePage();
    }

    private sealed class XmlResultWriter : IResultWriter
    {
        private readonly XmlWriter _writer;

        public XmlResultWriter(XmlWriter writer) => _writer = writer;

        #region IResultWriter member

        public void BeginWritePage(int i)
        {
            _writer.WriteStartElement(Constants.Ocr.Result);
            _writer.WriteAttributeString(Constants.Content.PageNumber, i.ToText());
        }

        public void WriteText(TextLine text, string optimizedText)
        {
            if (optimizedText != null)
            {
                WriteTextItem(optimizedText, text.Region, text.Direction);
                return;
            }

            _writer.WriteComment(text.Text);
            foreach (TextInfo item in text.Texts)
            {
                WriteTextItem(item.Text, item.Region, WritingDirection.Unknown);
            }
        }

        private void WriteTextItem(string text, Bound ir, WritingDirection direction)
        {
            _writer.WriteStartElement(Constants.Ocr.Content);
            _writer.WriteAttributeString(Constants.Ocr.Text, text);
            switch (direction)
            {
                case WritingDirection.Horizontal:
                    _writer.WriteAttributeString(Constants.Coordinates.Direction, Constants.Coordinates.Horizontal);
                    break;
                case WritingDirection.Vertical:
                    _writer.WriteAttributeString(Constants.Coordinates.Direction, Constants.Coordinates.Vertical);
                    break;
            }

            _writer.WriteAttributeString(Constants.Coordinates.Top, Math.Round(ir.Top).ToText());
            _writer.WriteAttributeString(Constants.Coordinates.Left, Math.Round(ir.Left).ToText());
            _writer.WriteAttributeString(Constants.Coordinates.Bottom, Math.Round(ir.Bottom).ToText());
            _writer.WriteAttributeString(Constants.Coordinates.Right, Math.Round(ir.Right).ToText());
            _writer.WriteEndElement();
        }

        public void EndWritePage() => _writer.WriteEndElement();

        public void BeginWriteImage(ImageDisposition image)
        {
            _writer.WriteStartElement(Constants.Ocr.Image);
            _writer.WriteAttributeString(Constants.Coordinates.Width, image.Image.Width.ToText());
            _writer.WriteAttributeString(Constants.Coordinates.Height, image.Image.Height.ToText());
            _writer.WriteAttributeString(Constants.Content.OperandNames.Matrix, PdfHelper.MatrixToString(image.Ctm));
        }

        public void EndWriteImage() => _writer.WriteEndElement();

        #endregion
    }

    private sealed class TextResultWriter : IResultWriter
    {
        private readonly TextWriter _writer;

        public TextResultWriter(TextWriter writer) => _writer = writer;

        #region IResultWriter member

        public void BeginWritePage(int i) => _writer.WriteLine("#identify page number=" + i);

        public void WriteText(TextLine text, string optimizedText) => _writer.WriteLine(optimizedText ?? text.Text);

        public void EndWritePage() => _writer.WriteLine();

        public void BeginWriteImage(ImageDisposition image) =>
            _writer.WriteLine("#Recognition image=" + PdfHelper.MatrixToString(image.Ctm));

        public void EndWriteImage()
        {
        }

        #endregion
    }
}
