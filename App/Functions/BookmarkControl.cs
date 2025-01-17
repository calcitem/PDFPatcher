﻿using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using PDFPatcher.Common;

namespace PDFPatcher;

public partial class BookmarkControl : UserControl
{
    public BookmarkControl() => InitializeComponent();

    //supportedBookmarkTypes = defaultBookmarkTypes;
    /// <summary>Get or specify the drop-down list box for the bookmark file path.</summary>
    internal HistoryComboBox FileList { get; private set; }

    internal FileDialog FileDialog => UseForBookmarkExport ? _SaveBookmarkBox : _OpenBookmarkBox;

    [Description("Text displayed on the label text")]
    public string LabelText
    {
        get => label1.Text;
        set => label1.Text = value;
    }

    [DefaultValue(null)]
    /// <summary>Gets or specifies the value of the bookmark file path.</summary>
    public override string Text
    {
        get => FileList.Text;
        set => FileList.Text = value;
    }

    /// <summary>Get or specify whether it is used to export bookmarks.</summary>
    [DefaultValue(false)]
    [Description("Whether to open the save dialog when you click the Browse button")]
    public bool UseForBookmarkExport { get; set; }

    //readonly string[] xmlBookmarkType = new string[] { ".xml" };
    //private string[] supportedBookmarkTypes;
    internal event EventHandler<EventArgs> BrowseForFile;

    private void _BrowseSourcePdfButton_Click(object sender, EventArgs e)
    {
        BrowseForFile?.Invoke(sender, e);
        string sourceFile = AppContext.SourceFiles is { Length: > 0 }
            ? AppContext.SourceFiles[0]
            : string.Empty;
        if (FileHelper.IsPathValid(FileList.Text) && Path.GetFileName(FileList.Text).Length > 0)
        {
            FilePath p = new(FileList.Text);
            _OpenBookmarkBox.SetLocation(p);
            _SaveBookmarkBox.SetLocation(p);
        }
        else if (sourceFile.Length > 0)
        {
            FilePath p = new FilePath(sourceFile).ChangeExtension("xml");
            _SaveBookmarkBox.SetLocation(p);
            _OpenBookmarkBox.SetLocation(p);
        }

        if (UseForBookmarkExport)
        {
            if (_SaveBookmarkBox.ShowDialog() == DialogResult.OK)
            {
                FileList.Text = _SaveBookmarkBox.FileName;
            }
        }
        else if (_OpenBookmarkBox.ShowDialog() == DialogResult.OK)
        {
            if (_OpenBookmarkBox.FileName == FileList.Text)
            {
                return;
            }

            FileList.Text = _OpenBookmarkBox.FileName;
        }
    }

    private void _BookmarkBox_DragEnter(object sender, DragEventArgs e) =>
        //Common.Form.FeedbackDragFileOver (e, supportedBookmarkTypes);
        e.FeedbackDragFileOver(Constants.FileExtensions.AllBookmarkExtension);

    private void _BookmarkBox_DragDrop(object sender, DragEventArgs e) =>
        //Common.Form.DropFileOver ((Control)sender, e, supportedBookmarkTypes);
        ((Control)sender).DropFileOver(e, Constants.FileExtensions.AllBookmarkExtension);

    private void _BookmarkBox_TextChanged(object sender, EventArgs e) => AppContext.BookmarkFile = FileList.Text;

    private void BookmarkControl_Show(object sender, EventArgs e)
    {
        string t = FileList.Text;
        FileList.Contents = Visible switch
        {
            true when AppContext.MainForm != null =>
                // _BookmarkBox.DataSource = new BindingList<string> (_UseForBookmarkExport ? ContextData.Recent.SavedInfoDocuments : ContextData.Recent.InfoDocuments);
                AppContext.Recent.InfoDocuments,
            false => null,
            _ => FileList.Contents
        };

        FileList.Text = t;
    }
}
