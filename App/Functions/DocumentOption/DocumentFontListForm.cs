﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BrightIdeasSoftware;
using iTextSharp.text.pdf;
using PDFPatcher.Common;
using PDFPatcher.Model;
using PDFPatcher.Processor;
using PDFPatcher.Properties;

namespace PDFPatcher.Functions;

public partial class DocumentFontListForm : Form
{
    private Dictionary<int, string> _fontIdNames;
    private Dictionary<string, PageFont> _pageFonts;

    public DocumentFontListForm()
    {
        InitializeComponent();
        this.SetIcon(Resources.Fonts);
        Load += (_, _) =>
        {
            MinimumSize = Size;
            if (AppContext.Recent.SourcePdfFiles.HasContent())
            {
                _SourceFileBox.FileList.Text = AppContext.Recent.SourcePdfFiles[0];
            }
        };
        _Worker.ProgressChanged += (_, args) =>
        {
            switch (args.ProgressPercentage)
            {
                case < 0:
                    _ProgressBar.Maximum = -args.ProgressPercentage;
                    break;
                case > 0:
                    _ProgressBar.SetValue(args.ProgressPercentage);
                    break;
                default:
                    {
                        if (args.UserState is PageFont pf)
                        {
                            _FontListBox.AddObject(pf);
                        }

                        break;
                    }
            }
        };
        _Worker.RunWorkerCompleted += (_, _) =>
        {
            _ProgressBar.Value = _ProgressBar.Maximum;
            if (_pageFonts.HasContent())
            {
                _FontListBox.AddObjects(_pageFonts.Values);
            }

            _ListFontsButton.Enabled = true;
        };
        _Worker.DoWork += (_, _) =>
        {
            try
            {
                _fontIdNames = new Dictionary<int, string>();
                _pageFonts = new Dictionary<string, PageFont>();
                _FontListBox.ClearObjects();
                using PdfReader p = PdfHelper.OpenPdfFile(_SourceFileBox.FirstFile, false, false);
                PageRangeCollection r = PageRangeCollection.Parse(_PageRangeBox.Text, 1, p.NumberOfPages, true);
                int[] pp = new int[p.NumberOfPages + 1];
                _Worker.ReportProgress(-r.TotalPages);
                int i = 0;
                foreach (int page in r.SelectMany(range => range))
                {
                    if (_Worker.CancellationPending)
                    {
                        return;
                    }

                    _Worker.ReportProgress(++i);
                    if (pp[page] != 0)
                    {
                        continue;
                    }

                    pp[page] = 1;
                    GetPageFonts(p, page);
                }
            }
            catch (Exception ex)
            {
                FormHelper.ErrorBox(ex.Message);
            }
        };
        _FontListBox.PersistentCheckBoxes = true;
        new TypedColumn<PageFont>(_NameColumn) { AspectGetter = o => o.Name };
        new TypedColumn<PageFont>(_FirstPageColumn) { AspectGetter = o => o.FirstPage };
        new TypedColumn<PageFont>(_EmbeddedColumn) { AspectGetter = o => o.Embedded };
        new TypedColumn<PageFont>(_ReferenceColumn) { AspectGetter = o => o.Reference };
    }

    internal FontSubstitutionsEditor SubstitutionsEditor { get; set; }

    public IList<string> SelectedFonts => (from PageFont item in _FontListBox.CheckedObjects select item.Name).ToList();

    private void GetPageFonts(PdfReader pdf, int pageNumber)
    {
        PdfDictionary page = pdf.GetPageNRelease(pageNumber);
        PdfDictionary fl = page.Locate<PdfDictionary>(true, PdfName.RESOURCES, PdfName.FONT);
        if (fl == null)
        {
            return;
        }

        foreach (KeyValuePair<PdfName, PdfObject> item in fl)
        {
            if (item.Value is not PdfIndirectReference fr)
            {
                continue;
            }

            if (_fontIdNames.TryGetValue(fr.Number, out string fn))
            {
                _pageFonts[fn].IncrementReference();
                continue;
            }

            if (PdfReader.GetPdfObjectRelease(fr) is not PdfDictionary f)
            {
                continue;
            }

            PdfName bf = f.GetAsName(PdfName.BASEFONT);
            if (bf == null)
            {
                continue;
            }

            fn = PdfHelper.GetPdfNameString(bf, AppContext.Encodings.FontNameEncoding); // Font name
            fn = PdfDocumentFont.RemoveSubsetPrefix(fn);
            _fontIdNames.Add(fr.Number, fn);
            if (_pageFonts.TryGetValue(fn, out PageFont pf))
            {
                pf.IncrementReference();
                continue;
            }

            _pageFonts.Add(fn, new PageFont(fn, pageNumber, PdfDocumentFont.HasEmbeddedFont(f)));
        }
    }

    private void _ListFontsButton_Click(object sender, EventArgs e)
    {
        _ProgressBar.Value = 0;
        _ListFontsButton.Enabled = false;
        _Worker.RunWorkerAsync();
    }

    private void _SelectAllButton_Click(object sender, EventArgs e)
    {
        if (_FontListBox.GetItemCount() == 0)
        {
            return;
        }

        if (_FontListBox.GetItem(0).Checked == false)
        {
            _FontListBox.CheckObjects(_FontListBox.Objects);
        }
        else
        {
            _FontListBox.CheckedObjects = null;
        }

        _FontListBox.Focus();
    }

    private void _AddSelectedFontsButton_Click(object sender, EventArgs e)
    {
        if (SubstitutionsEditor == null)
        {
            return;
        }

        IList<string> sf = SelectedFonts;
        if (sf.Count == 0)
        {
            FormHelper.ErrorBox("Please select the font that needs to be added to the replacement list.");
            return;
        }

        SubstitutionsEditor.AddFonts(sf);
        Close();
    }

    private void _AppConfigButton_Click(object sender, EventArgs e) => this.ShowDialog<AppOptionForm>();

    private sealed class PageFont
    {
        public PageFont(string name, int firstPage, bool embedded)
        {
            Name = name;
            FirstPage = firstPage;
            Embedded = embedded;
            Reference = 1;
        }

        public string Name { get; }
        public int FirstPage { get; }
        public int Reference { get; private set; }
        public bool Embedded { get; }

        public void IncrementReference() => Reference++;
    }
}
