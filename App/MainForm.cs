﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using iTextSharp.text.io;
using PDFPatcher.Common;
using PDFPatcher.Functions;
using PDFPatcher.Processor;

namespace PDFPatcher;

public partial class MainForm : Form
{
    private static readonly Dictionary<Function, FunctionControl> __FunctionControls = new();

    private ReportControl _LogControl;

    public MainForm() => InitializeComponent();

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PdfHelper.ToggleReaderDebugMode(true); // Open fault tolerance mode
        PdfHelper.ToggleUnethicalMode(true); // Open forced read encrypted document mode

        try
        {
            AppContext.Load(null);
        }
        catch (Exception)
        {
            // ignore loading exception
        }

        Text = Constants.AppName + " [" + Application.ProductVersion + "]";
        MinimumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += (_, args) => args.FeedbackDragFileOver(Constants.FileExtensions.Pdf);
        DragDrop += (_, args) => OpenFiles(args.DropFileOver(Constants.FileExtensions.Pdf));

        SetupCustomizeToolbar();
        _GeneralToolbar.Visible = AppContext.Toolbar.ShowGeneralToolbar;

        _OpenPdfDialog.DefaultExt = Constants.FileExtensions.Pdf;
        _OpenPdfDialog.Filter = Constants.FileExtensions.PdfFilter;

        _LogControl = new ReportControl
        {
            Location = _FunctionContainer.Location,
            Size = _FunctionContainer.Size,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right
        };
        Controls.Add(_LogControl);
        HideLogControl();
        _LogControl.VisibleChanged += (_, _) => _FunctionContainer.Visible = !_LogControl.Visible;
        _OpenConfigDialog.FileName =
            _SaveConfigDialog.FileName = Constants.AppName + "Profile" + Constants.FileExtensions.Json;
        _OpenConfigDialog.Filter = _SaveConfigDialog.Filter = Constants.FileExtensions.JsonFilter;
        _FunctionContainer.ImageList = new ImageList();
        _FunctionContainer.AllowDrop = true;
        _FunctionContainer.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Middle)
            {
                ClickCloseTab(args);
            }
        };
        _FunctionContainer.MouseDoubleClick += (_, args) => { ClickCloseTab(args); };
        _FunctionContainer.TabClosing += (_, args) =>
        {
            TabPage t = _FunctionContainer.SelectedTab;
            Tracker.DebugMessage(args.Action.ToString());
            if (t.Tag.CastOrDefault<Function>() == Function.FrontPage)
            {
                args.Cancel = true;
                return;
            }

            int i = args.TabPageIndex;
            int c = _FunctionContainer.TabCount;
            if (i == 0 && c > 1)
            {
                _FunctionContainer.SelectedIndex = 1;
            }
            else if (i < c)
            {
                _FunctionContainer.SelectedIndex = i - 1;
            }

            _FunctionContainer.TabPages.RemoveAt(i);
            t.Dispose();
            _MainStatusLabel.Text = string.Empty;
        };
        _FunctionContainer.Selected += SelectedFunctionChanged;
        _FunctionContainer.Deselected += FunctionDeselected;

        // Close the start screen window
        using (EventWaitHandle closeSplashEvent = new(false,
                   EventResetMode.ManualReset, "CloseSplashScreenEvent" + Constants.AppEngName))
        {
            closeSplashEvent.Set();
        }

        SelectFunctionList(Function.FrontPage);

        _GeneralToolbar.ItemClicked += MenuCommand;
        if (AppContext.CheckUpdateDate < DateTime.Today)
        {
            CheckUpdate();
            if (AppContext.CheckUpdateInterval != int.MaxValue)
            {
                AppContext.CheckUpdateDate = DateTime.Today + TimeSpan.FromDays(AppContext.CheckUpdateInterval);
            }
        }

        string[] ca = Environment.GetCommandLineArgs();
        if (ca.HasContent())
        {
            OpenFiles(ca);
        }
#if DEBUG
        StreamUtil.AddToResourceSearch("iTextAsian.dll");
#endif
    }

    private void OpenFiles(IEnumerable<string> files)
    {
        foreach (string item in files)
        {
            FilePath p = new(item);
            if (p.ExistsFile && p.HasExtension(Constants.FileExtensions.Pdf))
            {
                OpenFileWithEditor(p.ToFullPath());
            }
        }
    }

    private void CheckUpdate()
    {
        WebClient client = new();
        client.DownloadDataCompleted += (_, args) =>
        {
            if (args.Error != null)
            {
                goto Exit;
            }

            try
            {
                XmlDocument x = new();
                x.Load(new MemoryStream(args.Result));
                XmlElement r = x.DocumentElement;
                string u = r.GetAttribute("url");
                if (string.IsNullOrEmpty(u) == false
                    && new Version(ProductVersion) < new Version(r.GetAttribute("version"))
                    && this.ConfirmOKBox("Discover new versions, have you going to download?"))
                {
                    ShowDialogWindow(new UpdateForm());
                }
            }
            catch (Exception)
            {
                FormHelper.ErrorBox("Version information file format error, please try again later.");
            }

        Exit:
            client.Dispose();
            client = null;
        };
        client.DownloadDataAsync(new Uri(Constants.AppUpdateFile));
    }

    private void ClickCloseTab(MouseEventArgs args)
    {
        for (int i = _FunctionContainer.TabCount - 1; i >= 0; i--)
        {
            if (_FunctionContainer.GetTabRect(i).Contains(args.Location) == false)
            {
                continue;
            }

            if (_FunctionContainer.TabPages[i].Tag.CastOrDefault<Function>() != Function.FrontPage)
            {
                _FunctionContainer.TabPages[i].Dispose();
            }
        }
    }

    private void MenuCommand(object sender, ToolStripItemClickedEventArgs e)
    {
        ToolStripItem ci = e.ClickedItem;
        string t = ci.Tag as string;
        if (string.IsNullOrEmpty(t) == false)
        {
            Function func = (Function)Enum.Parse(typeof(Function), t);
            SelectFunctionList(func);
            return;
        }

        ci.HidePopupMenu();
        if (ci.OwnerItem == _RecentFiles)
        {
            FunctionControl f = GetActiveFunctionControl() as FunctionControl;
            f.RecentFileItemClicked?.Invoke(_MainMenu, e);
        }
        else
        {
            ExecuteCommand(ci.Name);
        }
    }

    internal void ExecuteCommand(string commandName)
    {
        switch (commandName)
        {
            case Commands.ResetOptions:
                {
                    if (GetActiveFunctionControl() is IResettableControl f
                        && FormHelper.YesNoBox("Do you return the current function to the default setting?") ==
                        DialogResult.Yes)
                    {
                        f.Reset();
                    }

                    break;
                }
            case Commands.RestoreOptions when _OpenConfigDialog.ShowDialog() == DialogResult.OK:
                {
                    if (AppContext.Load(_OpenConfigDialog.FileName) == false)
                    {
                        FormHelper.ErrorBox("Unable to load the specified profile.");
                        return;
                    }

                    foreach (FunctionControl item in __FunctionControls.Values)
                    {
                        (item as IResettableControl)?.Reload();
                    }

                    SetupCustomizeToolbar();
                    break;
                }
            case Commands.SaveOptions when _SaveConfigDialog.ShowDialog() == DialogResult.OK:
                AppContext.Save(_SaveConfigDialog.FileName, false);
                break;
            case Commands.LogWindow:
                ShowLogControl();
                break;
            case Commands.CreateShortcut:
                CommonCommands.CreateShortcut();
                break;
            case Commands.VisitHomePage:
                CommonCommands.VisitHomePage();
                break;
            case Commands.CheckUpdate:
                ShowDialogWindow(new UpdateForm());
                break;
            case Commands.Close when _FunctionContainer.SelectedTab.Tag.CastOrDefault<Function>() == Function.FrontPage:
                return;
            case Commands.Close:
                _FunctionContainer.SelectedTab.Dispose();
                break;
            case Commands.CustomizeToolbar or "_CustomizeToolbarCommand":
                ShowDialogWindow(new CustomizeToolbarForm());
                SetupCustomizeToolbar();
                break;
            case Commands.ShowGeneralToolbar:
                _FunctionContainer.SuspendLayout();
                _GeneralToolbar.Visible =
                    AppContext.Toolbar.ShowGeneralToolbar = !AppContext.Toolbar.ShowGeneralToolbar;
                _FunctionContainer.PerformLayout();
                break;
            case Commands.Exit:
                Close();
                break;
            default:
                {
                    if (GetActiveFunctionControl() is FunctionControl f)
                    {
                        if (commandName == Commands.Action && f.DefaultButton != null)
                        {
                            f.DefaultButton.PerformClick();
                        }
                        else
                        {
                            f.ExecuteCommand(commandName);
                        }
                    }

                    break;
                }
        }
    }

    private void SetupCustomizeToolbar()
    {
        AppContext.Toolbar.RemoveInvalidButtons();
        for (int i = _GeneralToolbar.Items.Count - 1; i > 0; i--)
        {
            _GeneralToolbar.Items[i].Dispose();
        }

        foreach (ToolbarOptions.ButtonOption item in AppContext.Toolbar.Buttons.Where(item => item.Visible))
        {
            _GeneralToolbar.Items.Add(item.CreateButton());
        }
    }

    private void ShowDialogWindow(Form window)
    {
        using Form f = window;
        f.StartPosition = FormStartPosition.CenterParent;
        f.ShowDialog(this);
    }

    private Control GetActiveFunctionControl()
    {
        TabPage t = _FunctionContainer.SelectedTab;
        if (t == null || t.HasChildren == false)
        {
            return null;
        }

        return t.Controls[0];
    }

    internal void OpenFileWithEditor(string path)
    {
        SelectFunctionList(Function.BookmarkEditor);
        EditorControl c = GetActiveFunctionControl() as EditorControl;
        if (string.IsNullOrEmpty(path))
        {
            c.ExecuteCommand(Commands.Open);
        }
        else
        {
            c.ExecuteCommand(Commands.OpenFile, path);
        }
    }

    internal void SelectFunctionList(Function func)
    {
        switch (func)
        {
            case Function.PatcherOptions:
                ShowDialogWindow(new PatcherOptionForm(false) { Options = AppContext.Patcher });
                break;
            case Function.MergerOptions:
                ShowDialogWindow(new MergerOptionForm());
                break;
            case Function.InfoFileOptions:
                ShowDialogWindow(new InfoFileOptionControl());
                break;
            case Function.EditorOptions:
                ShowDialogWindow(new PatcherOptionForm(true) { Options = AppContext.Editor });
                break;
            case Function.Options:
                ShowDialogWindow(new AppOptionForm());
                break;
            default:
                {
                    HideLogControl();
                    string p = (GetActiveFunctionControl() as IDocumentEditor)?.DocumentPath;
                    FunctionControl c = GetFunctionControl(func);
                    foreach (TabPage item in _FunctionContainer.TabPages)
                    {
                        if (item.Controls.Count <= 0 || item.Controls[0] != c)
                        {
                            continue;
                        }

                        _FunctionContainer.SelectedTab = item;
                        if (string.IsNullOrEmpty(p) == false)
                        {
                            c.ExecuteCommand(Commands.OpenFile, p);
                        }

                        return;
                    }

                    TabPage t = new(c.FunctionName) { Font = SystemFonts.SmallCaptionFont };
                    ImageList.ImageCollection im = _FunctionContainer.ImageList.Images;
                    for (int i = im.Count - 1; i >= 0; i--)
                    {
                        if (im[i] != c.IconImage)
                        {
                            continue;
                        }

                        t.ImageIndex = i;
                        break;
                    }

                    if (t.ImageIndex < 0)
                    {
                        im.Add(c.IconImage);
                        t.ImageIndex = im.Count - 1;
                    }

                    t.Tag = func;
                    _FunctionContainer.TabPages.Add(t);
                    c.Size = t.ClientSize;
                    c.Dock = DockStyle.Fill;
                    t.Controls.Add(c);
                    _FunctionContainer.SelectedTab = t;
                    AcceptButton = c.DefaultButton;

                    if (string.IsNullOrEmpty(p) == false)
                    {
                        c.ExecuteCommand(Commands.OpenFile, p);
                    }

                    //c.HideOnClose = true;
                    //c.Show (this._DockPanel);
                    break;
                }
        }
    }

    private void FunctionDeselected(object sender, TabControlEventArgs args)
    {
        if (GetActiveFunctionControl() is FunctionControl c)
        {
            c.OnDeselected();
        }
    }

    private void SelectedFunctionChanged(object sender, TabControlEventArgs args)
    {
        if (GetActiveFunctionControl() is not FunctionControl c)
        {
            return;
        }

        //foreach (ToolStripMenuItem item in _MainMenu.Items) {
        //	c.SetupMenu (item);
        //}
        c.OnSelected();
        _MainStatusLabel.Text = c is IDocumentEditor b ? b.DocumentPath : Messages.Welcome;
        AcceptButton = c.DefaultButton;
    }

    internal string ShowPdfFileDialog() =>
        _OpenPdfDialog.ShowDialog() == DialogResult.OK ? _OpenPdfDialog.FileName : null;

    private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
    {
        try
        {
            AppContext.Save(null, true);
        }
        catch (Exception)
        {
            // ignore error
        }
    }

    private void HideLogControl() => _LogControl.Hide();

    private void ShowLogControl() => _LogControl.Show();

    private void MenuOpening(object sender, EventArgs e)
    {
        if (GetActiveFunctionControl() is FunctionControl f)
        {
            f.SetupMenu(sender as ToolStripMenuItem);
        }
    }

    private void RecentFileMenuOpening(object sender, EventArgs e)
    {
        if (GetActiveFunctionControl() is FunctionControl { ListRecentFiles: { } } f)
        {
            f.ListRecentFiles(sender, e);
        }
    }

    #region Public function

    private BackgroundWorker _Worker;
    private readonly FormState _formState = new();
    private bool _FullScreen;

    /// <summary>Gets or specifies the value of the full screen display.</summary>
    public bool FullScreen
    {
        get => _FullScreen;
        set
        {
            if (value == _FullScreen)
            {
                return;
            }

            _FullScreen = value;
            if (value)
            {
                _MainMenu.Visible = _GeneralToolbar.Visible = false;
                _formState.Maximize(this);
            }
            else
            {
                _MainMenu.Visible = true;
                _GeneralToolbar.Visible = AppContext.Toolbar.ShowGeneralToolbar;
                _formState.Restore(this);
            }
        }
    }

    /// <summary>
    ///     Set the prompt information for the control.
    /// </summary>
    internal void SetTooltip(Control control, string text) => _ToolTip.SetToolTip(control, text);

    /// <summary>
    ///     Get or set the status bar text.
    /// </summary>
    internal string StatusText
    {
        get => _MainStatusLabel.Text;
        set => _MainStatusLabel.Text = value;
    }

    #region Worker

    /// <summary>Get or specify the background process.</summary>
    internal BackgroundWorker GetWorker()
    {
        if (_Worker != null)
        {
            return _Worker;
        }

        _Worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
        _Worker.DoWork += Worker_DoWork;
        _Worker.ProgressChanged += Worker_ProgressChanged;
        _Worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
        Tracker.SetWorker(_Worker);

        return _Worker;
    }

    internal bool IsWorkerBusy => _Worker?.IsBusy == true;

    private void Worker_DoWork(object sender, DoWorkEventArgs e)
    {
        _Worker.ReportProgress(0);
        FileHelper.ResetOverwriteMode();
        AppContext.Abort = false;
    }

    private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        _MainMenu.Enabled = _GeneralToolbar.Enabled = _FunctionContainer.Enabled = true;
        foreach (TabPage item in _FunctionContainer.TabPages)
        {
            item.Enabled = true;
        }

        if (e.Error == null || e.Cancelled == false)
        {
            _LogControl.Complete();
        }
    }

    public void ResetWorker()
    {
        if (_Worker == null)
        {
            return;
        }

        if (_Worker.IsBusy)
        {
            throw new InvalidOperationException("Worker is busy. Can't be reset.");
        }

        _Worker.Dispose();
        _Worker = null;
    }

    private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
    {
        string s = e.UserState as string;
        switch (s)
        {
            case "INC":
                _LogControl.IncrementProgress(e.ProgressPercentage);
                return;
            case "GOAL":
                _LogControl.SetGoal(e.ProgressPercentage);
                return;
            case "TINC":
                _LogControl.IncrementTotalProgress();
                break;
            case "TGOAL":
                _LogControl.SetTotalGoal(e.ProgressPercentage);
                break;
        }

        switch (e.ProgressPercentage)
        {
            case > 0:
                _LogControl.SetProgress(e.ProgressPercentage);
                break;
            case 0:
                {
                    _MainMenu.Enabled = _GeneralToolbar.Enabled = _FunctionContainer.Enabled = false;
                    foreach (TabPage item in _FunctionContainer.TabPages)
                    {
                        item.Enabled = false;
                    }

                    _LogControl.Reset();
                    ShowLogControl();
                    break;
                }
            default:
                {
                    if (s != null)
                    {
                        _LogControl.PrintMessage(s, (Tracker.Category)e.ProgressPercentage);
                    }

                    break;
                }
        }
    }

    #endregion

    internal FunctionControl GetFunctionControl(Function functionName)
    {
        if (__FunctionControls.TryGetValue(functionName, out FunctionControl f) && f.IsDisposed == false)
        {
            return f;
        }

        switch (functionName)
        {
            case Function.FrontPage:
                __FunctionControls[functionName] = new FrontPageControl();
                break;
            case Function.Patcher:
                __FunctionControls[functionName] = new PatcherControl();
                break;
            case Function.Merger:
                __FunctionControls[functionName] = new MergerControl();
                break;
            case Function.BookmarkGenerator:
                __FunctionControls[functionName] = new AutoBookmarkControl();
                break;
            case Function.InfoExchanger:
                __FunctionControls[functionName] = new InfoExchangerControl();
                break;
            case Function.ExtractPages:
                __FunctionControls[functionName] = new ExtractPageControl();
                break;
            case Function.ExtractImages:
                __FunctionControls[functionName] = new ExtractImageControl();
                break;
            case Function.BookmarkEditor:
                //__FunctionControls[functionName] = new BookmarkEditorControl ();
                //break;
                EditorControl b = new();
                b.DocumentChanged += OnDocumentChanged;
                return b;
            //case FormHelper.Functions.InfoFileOptions:
            //    __FunctionControls[functionName] = new InfoFileOptionControl ();
            //    break;
            case Function.Ocr:
                __FunctionControls[functionName] = new OcrControl();
                break;
            case Function.RenderPages:
                __FunctionControls[functionName] = new RenderImageControl();
                break;
            //case Form.Functions.ImportOptions:
            //    __FunctionControls[functionName] = new ImportOptionControl ();
            //    break;
            //case FormHelper.Functions.Options:
            //    __FunctionControls[functionName] = new AppOptionControl ();
            //    break;
            case Function.About:
                __FunctionControls[functionName] = new AboutControl();
                break;
            //case FormHelper.Functions.Log:
            //    __FunctionControls[functionName] = new ReportControl ();
            //    break;
            case Function.Inspector:
                //__FunctionControls[functionName] = new DocumentInspectorControl ();
                //break;
                DocumentInspectorControl d = new();
                d.DocumentChanged += OnDocumentChanged;
                return d;
            case Function.Rename:
                __FunctionControls[functionName] = new RenameControl();
                break;
            default:
                return null;
                //__FunctionControls[Form.Functions.Default] = new Label ();
                //functionName = Form.Functions.Default;
                //break;
        }

        return __FunctionControls[functionName];
    }

    private void OnDocumentChanged(object sender, DocumentChangedEventArgs args)
    {
        string p = args.Path;
        _MainStatusLabel.Text = p ?? string.Empty;
        if (!FileHelper.IsPathValid(p))
        {
            return;
        }

        p = Path.GetFileNameWithoutExtension(p);
        if (p.Length > 20)
        {
            p = p.Substring(0, 17) + "...";
        }

        if (sender is not Control f)
        {
            return;
        }

        f = f.Parent;
        if (f == null)
        {
            return;
        }

        f.Text = p;
    }

    #endregion
}
