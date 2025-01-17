﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BrightIdeasSoftware;
using PDFPatcher.Common;
using PDFPatcher.Model;
using PDFPatcher.Processor;
using PDFPatcher.Properties;

namespace PDFPatcher.Functions;

[ToolboxItem(false)]
public partial class RenameControl : FunctionControl
{
    private static readonly string[] __EnabledCommands = { Commands.Copy, Commands.Delete };
    private FileListHelper _listHelper;

    public RenameControl() => InitializeComponent();

    public override string FunctionName => "Rename file";

    public override Bitmap IconImage => Resources.Rename;

    #region IDefaultButtonControl member

    public override Button DefaultButton => _RenameButton;

    #endregion

    private void PatcherControl_OnLoad(object sender, EventArgs e)
    {
        //Icon = FormHelper.ToIcon (Properties.Resources.CreateDocument);
        _ItemList.ListViewItemSorter = new ListViewItemComparer(0);

        AppContext.MainForm.SetTooltip(_ItemList, "Add the PDF file that needs to be renamed here");
        AppContext.MainForm.SetTooltip(_RenameButton,
            "Click this button to rename the PDF file according to the file properties and output file name");
        AppContext.MainForm.SetTooltip(_TargetPdfFile.FileList,
            "The generated target PDF file path (right-click the list to insert the file name substitute)");
        _ItemList.EmptyListMsg =
            "Please use the \"Add Files\" button to add PDF files to be processed, or drag and drop files from the Explorer to this list box";

        _TargetPdfFile.FileMacroMenu.LoadStandardInfoMacros();
        _TargetPdfFile.FileMacroMenu.LoadStandardSourceFileMacros();
        _TargetPdfFile.BrowseForFile += FileControl_BrowseForFile;
        _TargetPdfFile.TargetFileChangedByBrowseButton += (_, args) =>
        {
            int i;
            string f = _TargetPdfFile.FileDialog.FileName;
            if (_ItemList.Items.Count <= 1 || (i = f.LastIndexOf(Path.DirectorySeparatorChar)) == -1)
            {
                return;
            }

            _TargetPdfFile.Text = string.Concat(f.Substring(0, i), Path.DirectorySeparatorChar,
                Constants.FileNameMacros.FileName, Path.GetExtension(f));
            args.Cancel = true;
        };
        ImageList.ImageCollection fi = _FileTypeList.Images;
        fi.AddRange(new Image[] { Resources.OriginalPdfFile });
        _ItemList.FixEditControlWidth();
        _listHelper = new FileListHelper(_ItemList);
        _listHelper.SetupDragAndDrop(AddFiles);
        _listHelper.SetupHotkeys();
        FileListHelper.SetupCommonPdfColumns(_AuthorColumn, _KeywordsColumn, _SubjectColumn, _TitleColumn,
            _PageCountColumn, _NameColumn, _FolderColumn);
        _RefreshInfoButton.ButtonClick += (_, _) => _listHelper.RefreshInfo(AppContext.Encodings.DocInfoEncoding);
        _RefreshInfoButton.DropDown = _RefreshInfoMenu;
        foreach (string item in Constants.Encoding.EncodingNames)
        {
            _RefreshInfoMenu.Items.Add(item);
        }

        _RefreshInfoMenu.ItemClicked += (_, args) => _listHelper.RefreshInfo(ValueHelper.MapValue(args.ClickedItem.Text,
            Constants.Encoding.EncodingNames, Constants.Encoding.Encodings));
        _AddFilesButton.DropDownOpening += FileListHelper.OpenPdfButtonDropDownOpeningHandler;
        _AddFilesButton.DropDownItemClicked += (_, args) =>
        {
            args.ClickedItem.Owner.Hide();
            ExecuteCommand(Commands.OpenFile, args.ClickedItem.ToolTipText);
        };
        RecentFileItemClicked += (_, args) => ExecuteCommand(Commands.OpenFile, args.ClickedItem.ToolTipText);
    }

    public override void SetupCommand(ToolStripItem item)
    {
        if (__EnabledCommands.Contains(item.Name)
            || Commands.CommonSelectionCommands.Contains(item.Name))
        {
            EnableCommand(item, _ItemList.GetItemCount() > 0 && _ItemList.Focused, true);
        }

        base.SetupCommand(item);
    }

    public override void ExecuteCommand(string commandName, params string[] parameters)
    {
        if (_listHelper.ProcessCommonMenuCommand(commandName))
        {
            return;
        }

        switch (commandName)
        {
            case Commands.Open:
                OpenFileDialog b = _OpenPdfBox;
                _AddFilesButton.DropDown.Items.ClearDropDownItems();
                if (b.ShowDialog() == DialogResult.OK)
                {
                    AddFiles(b.FileNames, true);
                }

                break;
            case Commands.OpenFile:
                AddFiles(parameters, true);
                break;
        }

        base.ExecuteCommand(commandName, parameters);
    }

    private void FileControl_BrowseForFile(object sender, EventArgs e) => _listHelper.PrepareSourceFiles();

    private void _RenameButton_Click(object sender, EventArgs e)
    {
        string targetPdfFile = _TargetPdfFile.Text.Trim();
        if (string.IsNullOrEmpty(targetPdfFile) &&
            string.IsNullOrEmpty(targetPdfFile = _TargetPdfFile.BrowseTargetFile()))
        {
            FormHelper.ErrorBox(Messages.TargetFileNotSpecified);
            return;
        }

        int l = _ItemList.GetItemCount();
        if (l == 0)
        {
            FormHelper.InfoBox("Add PDF files that need to be renamed.");
            return;
        }

        GetSourceItemList();
        _TargetPdfFile.FileList.AddHistoryItem();

        AppContext.MainForm.ResetWorker();
        BackgroundWorker worker = AppContext.MainForm.GetWorker();
        worker.DoWork += (_, _) =>
        {
            List<SourceItem.Pdf> items = _listHelper.GetSourceItems<SourceItem.Pdf>(false);
            Worker.RenameFiles(items, targetPdfFile, _KeepSourceFileBox.Checked);
        };
        worker.RunWorkerAsync();
    }

    private void AddFiles(string[] files, bool alertInvalidFiles)
    {
        if (files == null || files.Length == 0)
        {
            return;
        }

        if ((ModifierKeys & Keys.Control) != Keys.None || _AutoClearListBox.Checked)
        {
            _ItemList.ClearObjects();
        }

        switch (files.Length)
        {
            case > 3:
                AppContext.MainForm.Enabled = false;
                break;
            case 0:
                return;
        }

        _AddDocumentWorker.RunWorkerAsync(files);
    }

    private void GetSourceItemList()
    {
        int l = _ItemList.GetItemCount();
        List<SourceItem> files = new(l);
        for (int i = 0; i < l; i++)
        {
            SourceItem item = _ItemList.GetModelObject(_ItemList.GetNthItemInDisplayOrder(i).Index) as SourceItem;
            if (item.Type == SourceItem.ItemType.Pdf
                && FileHelper.HasExtension(item.FilePath, Constants.FileExtensions.Pdf))
            {
                AppContext.RecentItems.AddHistoryItem(AppContext.Recent.SourcePdfFiles, item.FilePath.ToString());
            }

            files.Add(item);
        }
    }

    private void _SortMenu_ItemClicked(object sender, ToolStripItemClickedEventArgs e) =>
        _ItemList.ListViewItemSorter = e.ClickedItem.Name switch
        {
            "_SortByAlphaItem" => new ListViewItemComparer(0, false),
            "_SortByNaturalNumberItem" => new ListViewItemComparer(0, true),
            _ => _ItemList.ListViewItemSorter
        };

    private void _ImageList_ColumnClick(object sender, ColumnClickEventArgs e)
    {
        int c = e.Column;
        bool ss = c == 0 || c == _PageCountColumn.Index;
        SortOrder o = _ItemList.PrimarySortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
        _ItemList.ListViewItemSorter = new ListViewItemComparer(e.Column, ss, o);
    }

    private void _MainToolbar_ButtonClick(object sender, EventArgs e)
    {
        if (sender == _AddFilesButton)
        {
            ExecuteCommand(Commands.Open);
        }
    }

    private void _MainToolbar_ItemClicked(object sender, ToolStripItemClickedEventArgs e) =>
        ExecuteCommand(e.ClickedItem.Name);

    private void _TestRenameButton_Click(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_TargetPdfFile.Text))
        {
            FormHelper.ErrorBox(Messages.TargetFileNotSpecified);
            return;
        }

        List<SourceItem.Pdf> pdfs = _listHelper.GetSourceItems<SourceItem.Pdf>(false);
        if (pdfs.Count == 0)
        {
            FormHelper.InfoBox("Add PDF files that need to be renamed.");
            return;
        }

        PreviewRename(pdfs, _TargetPdfFile.Text);
    }

    private static void PreviewRename(List<SourceItem.Pdf> items, string template)
    {
        int i = 0;
        string[] result = new string[items.Count];
        string[] source = new string[items.Count];
        foreach (SourceItem.Pdf item in items)
        {
            try
            {
                FilePath s = item.FilePath;
                if (s.ExistsFile == false)
                {
                    string.Concat("(Can't find the PDF file: ", s, ")");
                    continue;
                }

                string t = Worker.RenameFile(template, item);
                if (t.Length == 0)
                {
                    t = "<Output file name is invalid>";
                }
                else if (Path.GetFileName(t).Length == 0)
                {
                    t = "<Output file name is empty>";
                }

                source[i] = s.ToString();
                result[i] = t;
                i++;
            }
            catch (Exception ex)
            {
                FormHelper.ErrorBox(ex.Message);
            }
        }

        using RenamePreviewForm f = new(source, result);
        f.ShowDialog();
    }

    #region AddDocumentWorker

    private void _AddDocumentWorker_DoWork(object sender, DoWorkEventArgs e)
    {
        string[] files = e.Argument as string[];
        Array.ForEach(files, f => ((BackgroundWorker)sender).ReportProgress(0, f));
    }

    private void _AddDocumentWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) =>
        AppContext.MainForm.Enabled = true;

    //_listHelper.ResizeItemListColumns ();
    private void _AddDocumentWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
    {
        string item = e.UserState as string;
        AddItem(SourceItem.Create(item));
    }

    private void AddItem(SourceItem item)
    {
        if (item == null)
        {
            return;
        }

        AddItems(new[] { item });
    }

    private void AddItems(ICollection items)
    {
        int i = _ItemList.GetLastSelectedIndex();
        _ItemList.InsertObjects(++i, items);
        _ItemList.SelectedIndex = --i + items.Count;
    }

    #endregion
}
