﻿using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using PDFPatcher.Common;
using PDFPatcher.Functions;

namespace PDFPatcher;

public partial class TargetFileControl : UserControl
{
    public TargetFileControl()
    {
        InitializeComponent();

        _FileMacroMenu.ItemClicked += _FileMacroMenu.ProcessInsertMacroCommand;
    }

    /// <summary>Gets or specifies the value of the bookmark file path.</summary>
    internal HistoryComboBox FileList { get; private set; }

    internal FileDialog FileDialog => _SavePdfBox;
    internal MacroMenu FileMacroMenu => _FileMacroMenu;

    /// <summary>
    ///     Gets or sets the text of the file dropped box.
    /// </summary>
    public override string Text
    {
        get => FileList.Text;
        set => FileList.Text = value;
    }

    /// <summary>
    ///     Gets or sets the label text before the file drop-down box.
    /// </summary>
    [DefaultValue("Output PD&F file:")]
    public string Label
    {
        get => label1.Text;
        set => label1.Text = value;
    }

    internal event CancelEventHandler TargetFileChangedByBrowseButton;
    internal event EventHandler<EventArgs> BrowseForFile;

    public string BrowseTargetFile()
    {
        _BrowseTargetPdfButton_Click(_BrowseTargetPdfButton, null);
        return FileList.Text;
    }

    private void _BrowseTargetPdfButton_Click(object sender, EventArgs e)
    {
        BrowseForFile?.Invoke(sender, e);

        FilePath sourceFile = AppContext.SourceFiles is { Length: > 0 }
            ? AppContext.SourceFiles[0]
            : string.Empty;
        string t = FileList.Text;
        if (t.Length > 0 && FileHelper.IsPathValid(t) && Path.GetFileName(t).Length > 0)
        {
            _SavePdfBox.SetLocation(t);
        }
        else if (sourceFile.FileName.Length > 0)
        {
            t = FileHelper.GetNewFileNameFromSourceFile(sourceFile, Constants.FileExtensions.Pdf);
            _SavePdfBox.SetLocation(t);
        }

        if (_SavePdfBox.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        if (TargetFileChangedByBrowseButton != null)
        {
            CancelEventArgs a = new();
            TargetFileChangedByBrowseButton(this, a);
            if (a.Cancel)
            {
                return;
            }
        }

        Text = _SavePdfBox.FileName;
    }

    private void _TargetPdfBox_TextChanged(object sender, EventArgs e) => AppContext.TargetFile = FileList.Text;

    private void _TargetPdfBox_DragEnter(object sender, DragEventArgs e) =>
        e.FeedbackDragFileOver(Constants.FileExtensions.Pdf);

    private void _TargetPdfBox_DragDrop(object sender, DragEventArgs e) =>
        ((Control)sender).DropFileOver(e, Constants.FileExtensions.Pdf);

    private void TargetFileControl_Show(object sender, EventArgs e)
    {
        string t = Text;
        FileList.Contents = Visible switch
        {
            true when AppContext.MainForm != null => AppContext.Recent.TargetPdfFiles,
            false => null,
            _ => FileList.Contents
        };

        Text = t;
    }
}
