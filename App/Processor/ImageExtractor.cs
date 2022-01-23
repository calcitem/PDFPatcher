﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FreeImageAPI;
using iTextSharp.text.pdf;
using PDFPatcher.Common;
using PDFPatcher.Model;
using PDFPatcher.Processor.Imaging;

namespace PDFPatcher.Processor;

internal sealed class ImageExtractor
{
    private readonly string _fileMask;

    private readonly ImageExtracterOptions _options;
    private readonly PdfPageImageProcessor _parser;
    private readonly HashSet<PdfObject> _Refs = new();
    private int _activePage; // Use this property named file when exporting a file image
    private int _imageCount;
    private int _pageRotation;
    private int _totalImageCount;

    public ImageExtractor(ImageExtracterOptions options)
    {
        _fileMask = string.IsNullOrEmpty(options.FileMask) ? "0" : options.FileMask;
        _options = options;
        _parser = new PdfPageImageProcessor(PosList, InfoList);
    }

    internal List<ImageInfo> InfoList { get; } = new();

    internal List<ImageDisposition> PosList { get; } = new();

    internal bool PrintImageLocation { get; set; }

    internal void ExtractPageImages(PdfReader reader, int pageNum)
    {
        if (pageNum < 1 || pageNum > reader.NumberOfPages)
        {
            return;
        }

        _activePage = pageNum;
        _parser.Reset();
        _imageCount = 0;
        InfoList.Clear();
        PosList.Clear();
        PdfDictionary o = reader.GetPageNRelease(pageNum);
        if (o == null)
        {
            return;
        }

        // Collect the image on the page
        PdfDictionary pp = o.Locate<PdfDictionary>(PdfName.RESOURCES, PdfName.XOBJECT);
        if (pp != null)
        {
            ExtractImageInstances(pp, false);
        }

        // Collect pictures in annotations
        if (_options.ExtractAnnotationImages)
        {
            PdfArray an = o.Locate<PdfArray>(PdfName.ANNOTS);
            if (an != null)
            {
                foreach (PdfObject item in an.ArrayList)
                {
                    ExtractImageInstances(PdfReader.GetPdfObjectRelease(item) as PdfDictionary, true);
                }
            }
        }

        _pageRotation = PdfHelper.GetPageRotation(o);
        if (InfoList.Count == 0)
        {
            return;
        }

        _parser.ProcessContent(reader.GetPageContent(pageNum), o.Locate<PdfDictionary>(PdfName.RESOURCES));
        PosList.Sort();
        InfoList.Sort((x, y) =>
        {
            ImageDisposition xi = PosList.Find(info => info.Image == x);
            ImageDisposition yi = PosList.Find(info => info.Image == y);
            if (xi == null)
            {
                return yi == null ? 0 : -1;
            }

            if (yi == null)
            {
                return -1;
            }

            return xi.CompareTo(yi);
        });
        foreach (ImageInfo item in InfoList)
        {
            try
            {
                ExtractImage(item);
            }
            catch (FreeImageException ex)
            {
                if (item.ReferenceCount > 0)
                {
                    Tracker.TraceMessage(Tracker.Category.Error,
                        "An error was encountered while exporting the image on page " + pageNum + ": " + ex.Message);
                }
            }
        }

        if (_options.MergeImages && PosList.Count > 1)
        {
            // Missing the same width, the same type of picture
            MergeImages();
        }
    }

    private void ExtractImageInstances(PdfDictionary source, bool includeDescendants)
    {
        if (source == null)
        {
            return;
        }

        foreach (KeyValuePair<PdfName, PdfObject> item in source)
        {
            if (_options.SkipRedundantImages && _Refs.Add(item.Value) == false
                || PdfName.SMASK.Equals(item.Key)
                || PdfName.MASK.Equals(item.Key))
            {
                continue;
            }

            PdfObject obj = PdfReader.GetPdfObject(item.Value);
            PRStream stream = obj as PRStream;
            if (stream == null)
            {
                goto NEXT;
            }

            PdfName subType = stream.GetAsName(PdfName.SUBTYPE);
            if (PdfName.IMAGE.Equals(subType))
            {
                try
                {
                    InfoList.Add(new ImageInfo(item.Value as PRIndirectReference));
                }
                catch (NullReferenceException)
                {
                    Debug.WriteLine(item.Value);
                }
            }
            else if (PdfName.FORM.Equals(subType))
            {
                PdfDictionary fr = stream.Locate<PdfDictionary>(PdfName.RESOURCES, PdfName.XOBJECT);
                if (fr == null)
                {
                    continue;
                }

                foreach (KeyValuePair<PdfName, PdfObject> fri in fr)
                {
                    stream = PdfReader.GetPdfObject(fri.Value) as PRStream;
                    if (stream != null)
                    {
                        subType = stream.GetAsName(PdfName.SUBTYPE);
                        if (PdfName.IMAGE.Equals(subType))
                        {
                            InfoList.Add(new ImageInfo(fri.Value as PRIndirectReference));
                        }
                        else if (includeDescendants || PdfName.FORM.Equals(subType))
                        {
                            ExtractImageInstances(stream, true);
                        }
                    }
                    else if (includeDescendants)
                    {
                        ExtractImageInstances(null, true);
                    }
                }
            }

        NEXT:
            if (obj.Type is PdfObject.DICTIONARY or PdfObject.STREAM && includeDescendants)
            {
                ExtractImageInstances(obj as PdfDictionary, true);
            }
        }
    }

    internal void ExtractImage(ImageInfo info)
    {
        if (_totalImageCount == 0 && Directory.Exists(_options.OutputPath) == false)
        {
            Directory.CreateDirectory(_options.OutputPath);
        }

        byte[] bytes = info.DecodeImage(_options);
        if (bytes == null)
        {
            return;
        }

        if (info.LastDecodeError != null)
        {
            Tracker.TraceMessage(Tracker.Category.Error, info.LastDecodeError);
            return;
        }

        string fileName = GetNewImageFileName();
        if (info.ExtName is Constants.FileExtensions.Png or Constants.FileExtensions.Tif
           //|| info.MaskBytes != null
           )
        {
            SaveBitmap(info, bytes, fileName);
        }
        else
        {
            SaveImageBytes(info, bytes, fileName);
        }

        if (info.MaskBytes != null)
        {
            using FreeImageBitmap m = new(
                info.MaskSize.Width, info.MaskSize.Height,
                (info.MaskSize.Width + 7) / 8, PixelFormat.Format1bppIndexed,
                info.MaskBytes);
            //var r = bmp.Composite (false, null, m);
            m.Palette.CreateGrayscalePalette();
            m.Save(fileName + "[mask]" + Constants.FileExtensions.Tif);
        }

        _totalImageCount++;
    }

    private void SaveImageBytes(ImageInfo info, byte[] bytes, string fileName)
    {
        bool vFlip = _options.VerticalFlipImages ^ info.VerticalFlip;
        string n = fileName + info.ExtName;
        if (PrintImageLocation)
        {
            Tracker.TraceMessage(Tracker.Category.OutputFile, n);
            Tracker.TraceMessage("Export image:" + n);
        }

        if ((vFlip || _pageRotation != 0) && info.ExtName == Constants.FileExtensions.Jp2)
        {
            //var ctx = MuPdfSharp.ContextHandle.Create();
            //var pixmap = new MuPdfSharp.PixmapData(ctx, ctx.LoadJpeg2000(bytes));
            //var b = pixmap.ToBitmap(new MuPdfSharp.ImageRendererOptions());
            try
            {
                using MemoryStream ms = new(bytes);
                using FreeImageBitmap bmp = new(ms);
                RotateBitmap(bmp, _pageRotation, vFlip);
                info.CreatePaletteAndIccProfile(bmp);
                try
                {
                    bmp.Save(n);
                }
                catch (FreeImageException)
                {
                    File.Delete(n);
                    bmp.Save(new FilePath(n).ChangeExtension(Constants.FileExtensions.Png));
                }
            }
            catch (FreeImageException ex)
            {
                Tracker.TraceMessage(ex);
                bytes.DumpBytes(n);
            }
        }
        else if (PdfName.DEVICECMYK.Equals(info.ColorSpace))
        {
            using MemoryStream ms = new(bytes);
            using FreeImageBitmap bmp = new(ms,
                FREE_IMAGE_LOAD_FLAGS.JPEG_CMYK | FREE_IMAGE_LOAD_FLAGS.TIFF_CMYK);
            RotateBitmap(bmp, _pageRotation, vFlip);
            if (bmp.ConvertColorDepth(FREE_IMAGE_COLOR_DEPTH.FICD_24_BPP))
            {
                SwapRedBlue(bmp);
                n = fileName + Constants.FileExtensions.Png;
                bmp.Save(n, FREE_IMAGE_FORMAT.FIF_PNG);
            }
            else
            {
                n = fileName + Constants.FileExtensions.Tif;
                bmp.Save(n, FREE_IMAGE_FORMAT.FIF_TIFF,
                    FREE_IMAGE_SAVE_FLAGS.TIFF_CMYK | FREE_IMAGE_SAVE_FLAGS.TIFF_DEFLATE);
            }

            if (PrintImageLocation)
            {
                Tracker.TraceMessage("Export image:" + n);
            }
        }
        else
        {
            using (FileStream f = new(n, FileMode.Create))
            {
                f.Write(bytes, 0, bytes.Length);
            }

            if (info.ExtName == Constants.FileExtensions.Jpg)
            {
                if (vFlip)
                {
                    TransformJpeg(n, FREE_IMAGE_JPEG_OPERATION.FIJPEG_OP_FLIP_V);
                }

                if (_pageRotation != 0)
                {
                    TransformJpeg(n,
                        _pageRotation switch
                        {
                            90 => FREE_IMAGE_JPEG_OPERATION.FIJPEG_OP_ROTATE_90,
                            180 => FREE_IMAGE_JPEG_OPERATION.FIJPEG_OP_ROTATE_180,
                            270 => FREE_IMAGE_JPEG_OPERATION.FIJPEG_OP_ROTATE_270,
                            _ => FREE_IMAGE_JPEG_OPERATION.FIJPEG_OP_NONE
                        });
                }
            }
        }

        info.FileName = n;
    }

    private static void SwapRedBlue(FreeImageBitmap bmp)
    {
        FreeImageBitmap r = bmp.GetChannel(FREE_IMAGE_COLOR_CHANNEL.FICC_RED);
        FreeImageBitmap b = bmp.GetChannel(FREE_IMAGE_COLOR_CHANNEL.FICC_BLUE);
        bmp.SetChannel(b, FREE_IMAGE_COLOR_CHANNEL.FICC_RED);
        bmp.SetChannel(r, FREE_IMAGE_COLOR_CHANNEL.FICC_BLUE);
        //var stride = bmp.Stride;
        //var Scan0 = bmp.Scan0;

        //unsafe {
        //	var p = (byte*)(void*)Scan0;
        //	var nOffset = stride - bmp.Width * 3;
        //	byte temp;
        //	var h = bmp.Height;
        //	var w = bmp.Width;
        //	for (int y = 0; y < h; ++y) {
        //		for (int x = 0; x < w; ++x) {
        //			temp = p[0];
        //			p[0] = p[2];
        //			p[2] = temp;
        //			p += 3;
        //		}
        //		p += nOffset;
        //	}
        //}
    }

    private static void TransformJpeg(string fileName, FREE_IMAGE_JPEG_OPERATION operation)
    {
        string tmpName = fileName + Constants.FileExtensions.Tmp;
        if (FreeImageBitmap.JPEGTransform(fileName, tmpName, operation, true))
        {
            File.Delete(fileName);
            File.Move(tmpName, fileName);
            return;
        }

        File.Delete(tmpName);
        RotateFlipType type;
        switch (operation)
        {
            case FREE_IMAGE_JPEG_OPERATION.FIJPEG_OP_FLIP_H:
                type = RotateFlipType.RotateNoneFlipX;
                break;
            case FREE_IMAGE_JPEG_OPERATION.FIJPEG_OP_FLIP_V:
                type = RotateFlipType.RotateNoneFlipY;
                break;
            case FREE_IMAGE_JPEG_OPERATION.FIJPEG_OP_ROTATE_90:
                type = RotateFlipType.Rotate270FlipNone;
                break;
            case FREE_IMAGE_JPEG_OPERATION.FIJPEG_OP_ROTATE_180:
                type = RotateFlipType.Rotate180FlipNone;
                break;
            case FREE_IMAGE_JPEG_OPERATION.FIJPEG_OP_ROTATE_270:
                type = RotateFlipType.Rotate90FlipNone;
                break;
            default:
                Tracker.TraceMessage(Tracker.Category.Error, "Non-destructive flipping JPG picture failed:" + fileName);
                return;
        }

        using (FreeImageBitmap bmp = new(fileName))
        {
            bmp.RotateFlip(type);
            if (bmp.UniqueColors < 256)
            {
                bmp.ConvertColorDepth(FREE_IMAGE_COLOR_DEPTH.FICD_08_BPP);
            }

            bmp.Save(new FilePath(fileName).ChangeExtension(Constants.FileExtensions.Png));
        }

        File.Delete(fileName);
    }

    private void SaveBitmap(ImageInfo info, byte[] bytes, string fileName)
    {
        bool vFlip = _options.VerticalFlipImages ^ info.VerticalFlip;
        string ext = info.ExtName;
        if (info.PixelFormat == PixelFormat.Format1bppIndexed)
        {
            ext = _options.MonoPng == false ? Constants.FileExtensions.Tif : Constants.FileExtensions.Png;
        }

        string n = fileName + ext;
        if (PrintImageLocation)
        {
            Tracker.TraceMessage(Tracker.Category.OutputFile, n);
            Tracker.TraceMessage("Export image:" + n);
        }

        if (PdfName.DEVICECMYK.Equals(info.ColorSpace))
        {
            // TODO: CMYK in the converted byte array is loaded to FreeImageBitmap after RGB
            //info.PixelFormat = PixelFormat.Undefined;
            using FreeImageBitmap bmp = new(
                //info.Width,
                //info.Height,
                //GetStride (info, bytes, vFlip),
                //PixelFormat.Format32bppArgb, bytes
                new MemoryStream(bytes), FREE_IMAGE_LOAD_FLAGS.JPEG_CMYK
            );
            if (info.ICCProfile != null)
            {
                bmp.CreateICCProfile(info.ICCProfile);
            }

            RotateBitmap(bmp, _pageRotation, false);
            if (bmp.ConvertColorDepth(FREE_IMAGE_COLOR_DEPTH.FICD_24_BPP))
            {
                SwapRedBlue(bmp);
                n = fileName + Constants.FileExtensions.Png;
                bmp.Save(n, FREE_IMAGE_FORMAT.FIF_PNG);
            }
            else
            {
                bmp.Save(n,
                    FREE_IMAGE_FORMAT.FIF_TIFF,
                    FREE_IMAGE_SAVE_FLAGS.TIFF_CMYK | FREE_IMAGE_SAVE_FLAGS.TIFF_DEFLATE);
            }
        }
        else
        {
            using FreeImageBitmap bmp = CreateFreeImageBitmap(info, ref bytes, vFlip, true);
            if (ext == Constants.FileExtensions.Png
                && _options.InvertBlackAndWhiteImages
                && info.PixelFormat == PixelFormat.Format1bppIndexed
                && bmp.Palette.Length == 2)
            {
                bmp.SwapPaletteIndices(0, 1);
            }

            RotateBitmap(bmp, _pageRotation, false);
            if (ext == Constants.FileExtensions.Tif)
            {
                TiffHelper.Save(bmp, n);
            }
            //else if (info.MaskBytes != null) {
            //    bmp.Save (fileName + Constants.FileExtensions.Png);
            //}
            else
            {
                try
                {
                    bmp.Save(n, FREE_IMAGE_FORMAT.FIF_PNG);
                }
                catch (SEHException)
                {
                    Tracker.TraceMessage(Tracker.Category.Error,
                        "If you have an error while saving the picture, please contact developers:" + n);
                }
            }
        }

        info.FileName = n;
    }

    private static void RotateBitmap(FreeImageBitmap bitmap, int rotation, bool vflip)
    {
        if (rotation == 0 && vflip == false)
        {
            return;
        }

        RotateFlipType r;
        switch (rotation)
        {
            case 0:
                r = RotateFlipType.RotateNoneFlipY;
                break;
            case 90:
                r = vflip ? RotateFlipType.Rotate270FlipY : RotateFlipType.Rotate270FlipNone;
                break;
            case 180:
                r = vflip ? RotateFlipType.Rotate180FlipY : RotateFlipType.Rotate180FlipNone;
                break;
            case 270:
                r = vflip ? RotateFlipType.Rotate90FlipY : RotateFlipType.Rotate90FlipNone;
                break;
            default: return;
        }

        bitmap.RotateFlip(r);
    }

    internal static FreeImageBitmap CreateFreeImageBitmap(ImageInfo info, ref byte[] bytes, bool vFlip,
        bool loadPaletteAndIccp)
    {
        if (info.ExtName != Constants.FileExtensions.Jpg && info.ExtName != Constants.FileExtensions.Jp2)
        {
            info.ConvertDecodedBytes(ref bytes);
        }

        FreeImageBitmap bmp;
        if (PdfName.DEVICECMYK.Equals(info.ColorSpace))
        {
            bmp = new FreeImageBitmap(new MemoryStream(bytes), FREE_IMAGE_LOAD_FLAGS.TIFF_CMYK);
        }
        else if (info.ExtName is Constants.FileExtensions.Jp2 or Constants.FileExtensions.Jpg)
        {
            bmp = new FreeImageBitmap(new MemoryStream(bytes));
        }
        else
        {
            bmp = new FreeImageBitmap(info.Width, info.Height, GetStride(info, bytes, vFlip), info.PixelFormat,
                bytes);
        }

        if (loadPaletteAndIccp)
        {
            info.CreatePaletteAndIccProfile(bmp);
        }

        return bmp;
    }

    private static int GetStride(ImageInfo info, ICollection<byte> bytes, bool vFlip)
    {
        if (PdfName.COLORSPACE.Equals(info.ColorSpace))
        {
            return vFlip ? -(info.Width << 2) : info.Width << 2;
        }

        int components = bytes.Count / info.Width / info.Height;
        int stride = components > 0
            ? info.Width * components
            : (info.Width + 8 / info.BitsPerComponent - 1) / (8 / info.BitsPerComponent);
        return vFlip ? -stride : stride;
    }

    private string GetNewImageFileName()
    {
        _imageCount++;
        return string.Concat(
            FileHelper.CombinePath(_options.OutputPath, _activePage.ToString(_fileMask)),
            _imageCount > 1 ? "[" + _imageCount + "]" : null);
    }

    private void MergeImages()
    {
        int l = PosList.Count;
        for (int i = 0; i < l; i++)
        {
            ImageDisposition imageI = PosList[i];
            // Since only the PNG and TIF specify only the PixelFormat of the imageinfo, only these two types of files are handled during the merge process.
            if (imageI.Image.ReferenceCount < 1 // Image has been processed
                || imageI.Image.PixelFormat == PixelFormat.Undefined // Types that do not belong to merge
                || l - i < 2 // Is the last picture
               )
            {
                continue;
            }

            ImageInfo[] imageParts = new ImageInfo[l - i];
            int w = imageI.Image.Width;
            int h = 0;
            int i2 = 0;
            for (int j = i; j < l; j++)
            {
                ImageDisposition imageJ = PosList[j];
                if (imageJ.Image.ReferenceCount < 1 // Image processed
                    || imageJ.Image.Width != w // width does not match
                    || Math.Abs(Math.Round(imageJ.X - imageI.X)) > 1 // positions differ by more than 1 point
                                                                     //|| imageJ.Image.PixelFormat != imageI.Image.PixelFormat // format mismatch
                                                                     //|| imageJ.Image.ColorSpace == null
                                                                     //|| imageJ.Image.ColorSpace.Equals (imageI.Image.ColorSpace) == false // Colorspace does not match
                   )
                {
                    continue;
                }

                imageParts[i2] = imageJ.Image;
                h += imageJ.Image.Height;
                PosList[j].Image.ReferenceCount--; // avoid repeated processing
                i2++;
            }

            switch (i2)
            {
                case 0:
                    // Picture without consolidation conditions
                    continue;
                case 1:
                    PosList[i].Image.ReferenceCount++;
                    continue;
            }

            if (i2 < imageParts.Length)
            {
                Array.Resize(ref imageParts, i2);
            }

            if (PrintImageLocation)
            {
                Tracker.TraceMessage("Merge picture:" + string.Join("、",
                    Array.ConvertAll(imageParts, p => Path.GetFileName(p.FileName))));
            }

            string ext = Path.GetExtension(imageI.Image.FileName).ToLowerInvariant();
            if (imageI.Image.PixelFormat == PixelFormat.Format1bppIndexed)
            {
                ext = Constants.FileExtensions.Tif;
            }

            string f = GetNewImageFileName();
            using FreeImageBitmap bmp = new(w, h, imageI.Image.PixelFormat);
            h = 0;
            byte palEntryCount = 0;
            RGBQUAD[] bmpPal = bmp.HasPalette ? bmp.Palette.AsArray : null;
            foreach (ImageInfo part in imageParts)
            {
                using FreeImageBitmap bmp2 = FreeImageBitmap.FromFile(part.FileName);
                int pl = part.PaletteEntryCount;
                switch (pl)
                {
                    case > 0 when bmp.HasPalette && bmp2.HasPalette:
                        {
                            //var palMapSrc = new byte[pl];
                            //var palMapDest = new byte[pl];
                            //uint mi = 0;
                            for (int pi = 0; pi < pl; pi++)
                            {
                                int p = Array.IndexOf(bmpPal, part.PaletteArray[pi], 0, palEntryCount);
                                if (p != -1)
                                {
                                    continue;
                                }

                                if (palEntryCount == 255)
                                {
                                    if (bmpPal != null)
                                    {
                                        bmp.Palette.AsArray = bmpPal;
                                    }

                                    // The palette is not enough to store the merged picture color
                                    if (!bmp.ConvertColorDepth(FREE_IMAGE_COLOR_DEPTH.FICD_24_BPP) ||
                                        !bmp2.ConvertColorDepth(FREE_IMAGE_COLOR_DEPTH.FICD_24_BPP))
                                    {
                                        throw new OverflowException(
                                            "The palette overflows and cannot merge the picture.");
                                    }

                                    ext = Constants.FileExtensions.Png;
                                    bmpPal = null;
                                    goto Paste;
                                }

                                if (palEntryCount >= bmpPal.Length && palEntryCount < 129)
                                {
                                    bmp.ConvertColorDepth(FREE_IMAGE_COLOR_DEPTH.FICD_08_BPP);
                                    Array.Resize(ref bmpPal, 256);
                                }

                                bmpPal[palEntryCount] = part.PaletteArray[pi];
                                ++palEntryCount;
                                //if (p != pi) {
                                //	palMapSrc[mi] = (byte)pi;
                                //	palMapDest[mi] = (byte)(p);
                                //	mi++;
                                //}
                            }

                            //bmp2.ApplyPaletteIndexMapping (palMapSrc, palMapDest, mi, false);
                            break;
                        }
                    case > 0 when bmp2.HasPalette:
                        bmp2.ConvertColorDepth(FREE_IMAGE_COLOR_DEPTH.FICD_24_BPP);
                        break;
                }

            Paste:
                if (bmpPal != null)
                {
                    bmp.Palette.AsArray = bmpPal;
                }

                if (bmp.HasPalette && bmp2.HasPalette)
                {
                    RGBQUAD[] a1 = bmp.Palette.AsArray;
                    RGBQUAD[] a2 = bmp2.Palette.AsArray;
                    byte[] sp = new byte[palEntryCount];
                    byte[] dp = new byte[palEntryCount];
                    int di = 0;
                    for (int ai = 0; ai < a2.Length; ai++)
                    {
                        int p = Array.IndexOf(a1, a2[ai], 0, palEntryCount);
                        if (p == ai || p <= -1)
                        {
                            continue;
                        }

                        sp[di] = (byte)ai;
                        dp[di] = (byte)p;
                        ++di;
                    }
                    //todo: The two image palettes are inconsistent, they need to change color replication data.
                    //if (di > 0) {
                    //	bmp2.ApplyPaletteIndexMapping(sp, dp, (uint)di, true);
                    //}
                }

                if (bmp.Paste(bmp2, 0, h, int.MaxValue) == false)
                {
                    if (bmp.HasPalette && bmp2.HasPalette == false)
                    {
                        bmp.ConvertColorDepth(FREE_IMAGE_COLOR_DEPTH.FICD_24_BPP);
                        if (bmp.Paste(bmp2, 0, h, int.MaxValue) == false)
                        {
                            Tracker.TraceMessage("Merge picture failed");
                        }

                        bmpPal = null;
                    }
                }

                h += bmp2.Height;
            }

            if (bmpPal != null)
            {
                bmp.Palette.AsArray = bmpPal;
            }

            if (ext == Constants.FileExtensions.Jpg && _options.MergeJpgToPng)
            {
                ext = Constants.FileExtensions.Png;
            }
            else if (bmp.PixelFormat == PixelFormat.Format1bppIndexed)
            {
                ext = _options.MonoPng == false ? Constants.FileExtensions.Tif : Constants.FileExtensions.Png;
            }

            f += ext;
            if (PrintImageLocation)
            {
                Tracker.TraceMessage(Tracker.Category.OutputFile, f);
                Tracker.TraceMessage("Save the merged picture:" + f);
            }

            if (ext == Constants.FileExtensions.Tif)
            {
                TiffHelper.Save(bmp, f);
            }
            else
            {
                bmp.Save(f);
            }

            ImageInfo mii = new() { FileName = f, ReferenceCount = 1, Height = h, Width = w };
            InfoList.Add(mii);
            PosList.Add(new ImageDisposition(PosList[i].Ctm, mii));
        }

        foreach (ImageInfo item in InfoList.Where(item => item.ReferenceCount < 1))
        {
            File.Delete(item.FileName);
            item.FileName = null;
        }

        InfoList.Sort((x, y) =>
            string.Compare(x.FileName, y.FileName, StringComparison.OrdinalIgnoreCase));
        _totalImageCount -= _imageCount;
        _imageCount = 0;
        List<string> newFileNames = new();
        foreach (ImageInfo item in InfoList.Where(item => item.FileName != null && item.InlineImage == null))
        {
            string n;
            do
            {
                n = GetNewImageFileName() + Path.GetExtension(item.FileName);
            } while (PosList.Exists(i => i.Image.FileName == n) || newFileNames.Contains(n));

            if (PrintImageLocation)
            {
                Tracker.TraceMessage(string.Concat("Rename the merged file ", item.FileName, " to ", n));
                Tracker.TraceMessage(Tracker.Category.OutputFile, n);
            }

            newFileNames.Add(n);
            File.Delete(n);
            File.Move(item.FileName, n);
            item.FileName = n;
        }

        _totalImageCount += _imageCount;
    }

    private sealed class PdfPageImageProcessor : PdfContentStreamProcessor
    {
        private readonly List<ImageInfo> _infoList;
        private readonly List<ImageDisposition> _posList;

        public PdfPageImageProcessor(List<ImageDisposition> posList, List<ImageInfo> infoList)
        {
            PopulateOperators();
            _posList = posList;
            _infoList = infoList;
        }

        protected override void InvokeOperator(PdfLiteral oper, List<PdfObject> operands)
        {
            base.InvokeOperator(oper, operands);
            switch (oper.ToString())
            {
                case "Do":
                    PdfDictionary xobjects = Resource.GetAsDict(PdfName.XOBJECT);
                    PdfIndirectReference r = xobjects.GetAsIndirectObject(operands[0] as PdfName);
                    ImageInfo info = _infoList.Find(
                        i => i.InlineImage.PdfRef != null
                             && i.InlineImage.PdfRef.Number == r.Number
                             && i.InlineImage.PdfRef.Generation == r.Generation);
                    if (info != null)
                    {
                        info.ReferenceCount++;
                        _posList.Add(new ImageDisposition(CurrentGraphicState.TransMatrix, info));
                    }
                    else
                    {
                        Trace.WriteLine(string.Concat("Image ", r, " not found."));
                    }

                    break;
                case "BI":
                    info = new ImageInfo(new PdfImageData(operands[0] as PdfDictionary,
                        ((PdfImageData)operands[0]).RawBytes));
                    info.ReferenceCount++;
                    _infoList.Add(info);
                    _posList.Add(new ImageDisposition(CurrentGraphicState.TransMatrix, info));
                    break;
            }
        }
    }
}
