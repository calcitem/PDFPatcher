﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using BrightIdeasSoftware;
using PDFPatcher.Common;
using PDFPatcher.Model;
using PDFPatcher.Processor;

namespace PDFPatcher.Functions;

public partial class BookmarkEditorView : TreeListView
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static List<BookmarkElement> _copiedBookmarks;

    private readonly Dictionary<BookmarkElement, Color> _markers = new();

    public BookmarkEditorView()
    {
        InitializeComponent();
        InitEditorBox();
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal UndoManager Undo { get; set; }

    public bool OperationAffectsDescendants { get; set; }
    public OLVColumn BookmarkOpenColumn { get; private set; }

    public OLVColumn BookmarkNameColumn { get; private set; }

    public OLVColumn BookmarkPageColumn { get; private set; }

    public bool HasMarker => _markers.Count > 0;
    public bool IsLabelEditing { get; private set; }

    private void InitEditorBox()
    {
        if (IsDesignMode)
        {
            return;
        }

        UseOverlays = false;

        #region Repair tree view does not choose the problem of nodes correctly

        SmallImageList = new ImageList();

        #endregion

        this.SetTreeViewLine();
        this.FixEditControlWidth();
        CanExpandGetter = x => x is BookmarkElement { HasSubBookmarks: true };
        ChildrenGetter = x => ((BookmarkElement)x).SubBookmarks;
        BookmarkNameColumn.AutoCompleteEditorMode = AutoCompleteMode.Suggest;
        //this.SelectedRowDecoration = new RowBorderDecoration ()
        //{
        //    FillBrush = new SolidBrush (Color.FromArgb (64, SystemColors.Highlight)),
        //    BoundsPadding = new Size (0, 0),
        //    CornerRounding = 2,
        //    BorderPen = new Pen (Color.FromArgb (216, SystemColors.Highlight))
        //};
        new TypedColumn<BookmarkElement>(BookmarkNameColumn)
        {
            AspectGetter = e => e.Title,
            AspectPutter = (e, newValue) =>
            {
                string s = newValue as string;
                if (e.Title == s)
                {
                    return;
                }

                ReplaceTitleTextProcessor p = new(s);
                Undo?.AddUndo("Editor's text", p.Process(e));
            }
        };
        new TypedColumn<BookmarkElement>(BookmarkOpenColumn)
        {
            AspectGetter = e => e == null ? false : (object)e.IsOpen,
            AspectPutter = (e, newValue) =>
            {
                if (e == null || e.HasSubBookmarks == false)
                {
                    return;
                }

                BookmarkOpenStatusProcessor p = new((bool)newValue);
                Undo.AddUndo(p.Name, p.Process(e));
            }
        };
        new TypedColumn<XmlElement>(BookmarkPageColumn)
        {
            AspectGetter = e =>
            {
                if (e == null)
                {
                    return 0;
                }

                int p = e.GetValue(Constants.DestinationAttributes.Page, 0);
                if (!e.HasAttribute(Constants.DestinationAttributes.FirstPageNumber))
                {
                    return p;
                }

                int o = e.GetValue(Constants.DestinationAttributes.FirstPageNumber, 0);
                if (o <= 0)
                {
                    return p;
                }

                p += o;
                e.RemoveAttribute(Constants.DestinationAttributes.FirstPageNumber);

                return p;
            },
            AspectPutter = (e, value) =>
            {
                if (e == null)
                {
                    return;
                }

                if (!value.ToString().TryParse(out int n))
                {
                    return;
                }

                ChangePageNumberProcessor p = new(n, true, false);
                Undo.AddUndo(p.Name, p.Process(e));
            }
        };
        _ActionColumn.AspectGetter = x =>
        {
            if (x is not XmlElement e)
            {
                return string.Empty;
            }

            string a = e.GetAttribute(Constants.DestinationAttributes.Action);
            if (string.IsNullOrEmpty(a))
            {
                return e.HasAttribute(Constants.DestinationAttributes.Page) ? Constants.ActionType.Goto : "None";
            }

            return a;
        };
    }

    protected override void OnBeforeSorting(BeforeSortingEventArgs e)
    {
        e.Canceled = true; // No sort
        base.OnBeforeSorting(e);
    }

    protected override void OnItemActivate(EventArgs e)
    {
        base.OnItemActivate(e);
        EditSubItem(SelectedItem, 0);
    }

    internal void LoadBookmarks(XmlNodeList bookmarks)
    {
        Roots = bookmarks.ToXmlNodeArray();
        foreach (BookmarkElement item in Roots)
        {
            if (item?.IsOpen == true)
            {
                Expand(item);
            }
        }

        _markers.Clear();
        Mark(bookmarks);
    }

    private void Mark(XmlNodeList bookmarks)
    {
        foreach (BookmarkElement item in bookmarks)
        {
            if (item == null || item.MarkerColor == 0)
            {
                continue;
            }

            _markers.Add(item, Color.FromArgb(item.MarkerColor));
            Mark(item.ChildNodes);
        }
    }

    /// <summary>
    ///     Copy or move the bookmark.
    /// </summary>
    /// <param name="Source">The source bookmark you need to copy or move.</param>
    /// <param name="target">Target bookmark.</param>
    /// <param name="child">is copied to the child node.</param>
    /// <param name="after">is copied to the back.</param>
    /// <param name="copy">Whether the bookmark is copied.</param>
    /// <param name="deepCopy">Whether the bookmark is depressed deeply.</param>
    /// okmark is depressed deeply.
    /// </param>
    internal void CopyOrMoveElement(List<BookmarkElement> source, XmlElement target, bool child, bool after, bool copy,
        bool deepCopy)
    {
        UndoActionGroup undo = new();
        bool spr = false; // source parent is root
        bool tpr = false; // target parent is root
        List<XmlNode> pn = new();
        if (copy)
        {
            List<BookmarkElement> clones = new(source.Count);
            XmlDocument td = target.OwnerDocument;
            foreach (BookmarkElement item in source)
            {
                if (item.OwnerDocument == td)
                {
                    clones.Add((BookmarkElement)item.CloneNode(deepCopy));
                }
                else
                {
                    clones.Add(td.ImportNode(item, deepCopy) as BookmarkElement);
                }
            }

            source = clones;
        }
        else
        {
            foreach (XmlElement e in source.Select(item => item.ParentNode as XmlElement))
            {
                if (e.Name == Constants.DocumentBookmark)
                {
                    spr = true;
                    pn = null;
                    break;
                }

                pn.Add(e);
            }
        }

        //else {
        //	foreach (var item in source) {
        //		this.Collapse (item);
        //	}
        //	this.RemoveObjects (source);
        //}
        if (child)
        {
            if (after)
            {
                tpr = target.Name == Constants.DocumentBookmark;
                foreach (BookmarkElement item in source)
                {
                    if (!copy)
                    {
                        undo.Add(new AddElementAction(item));
                    }

                    target.AppendChild(item);
                    undo.Add(new RemoveElementAction(item));
                }
            }
            else
            {
                source.Reverse();
                foreach (BookmarkElement item in source)
                {
                    if (!copy)
                    {
                        undo.Add(new AddElementAction(item));
                    }

                    target.PrependChild(item);
                    undo.Add(new RemoveElementAction(item));
                }
            }

            Expand(target);
        }
        else
        {
            XmlNode p = target.ParentNode;
            if (after)
            {
                tpr = p.Name == Constants.DocumentBookmark;
                source.Reverse();
                foreach (BookmarkElement item in source)
                {
                    if (!copy)
                    {
                        undo.Add(new AddElementAction(item));
                    }

                    p.InsertAfter(item, target);
                    undo.Add(new RemoveElementAction(item));
                }
            }
            else
            {
                foreach (BookmarkElement item in source)
                {
                    if (!copy)
                    {
                        undo.Add(new AddElementAction(item));
                    }

                    p.InsertBefore(item, target);
                    undo.Add(new RemoveElementAction(item));
                }
            }
        }

        Undo?.AddUndo(copy ? "Copy bookmark" : "Move bookmark", undo);
        if (copy == false && spr || tpr)
        {
            Roots = (target.OwnerDocument as PdfInfoXmlDocument).BookmarkRoot.SubBookmarks;
        }

        if (pn != null)
        {
            RefreshObjects(pn);
        }

        RefreshObject(target);
        SelectedObjects = source;
    }

    /// <summary>
    ///     Checks if <paramref name="source" /> is a predecessor element of <paramref name="target" /> .
    /// </summary>
    private static bool IsAncestorOrSelf(XmlElement source, XmlElement target)
    {
        do
        {
            if (source == target)
            {
                return true;
            }
        } while ((target = target.ParentNode as XmlElement) != null);

        return false;
    }

    internal void MarkItems(List<BookmarkElement> items, Color color)
    {
        foreach (BookmarkElement item in items)
        {
            _markers[item] = color;
            item.MarkerColor = color.ToArgb();
        }

        RefreshObjects(items);
    }

    internal List<BookmarkElement> SelectMarkedItems(Color color)
    {
        Freeze();
        List<BookmarkElement> items = new();
        int c = color.ToArgb();
        List<BookmarkElement> r = new();
        foreach (BookmarkElement k in from item in _markers where item.Value.ToArgb() == c select item.Key)
        {
            Debug.Assert((k.ParentNode == null || k.OwnerDocument == null) == false);
            if (k.ParentNode == null || k.OwnerDocument == null)
            {
                r.Add(k);
                continue;
            }

            items.Add(k);
            MakeItemVisible(k);
        }

        foreach (BookmarkElement item in r)
        {
            _markers.Remove(item);
        }

        SelectObjects(items);
        EnsureItemsVisible(items);
        Unfreeze();
        return items;
    }

    internal void UnmarkItems(List<BookmarkElement> items)
    {
        foreach (BookmarkElement item in items)
        {
            _markers.Remove(item);
            item.MarkerColor = 0;
        }

        RefreshObjects(items);
    }

    internal void ClearMarks(bool refresh)
    {
        if (refresh)
        {
            List<XmlElement> items = new(_markers.Count);
            foreach (KeyValuePair<BookmarkElement, Color> item in _markers)
            {
                items.Add(item.Key);
                item.Key.MarkerColor = 0;
            }

            _markers.Clear();
            RefreshObjects(items);
        }
        else
        {
            _markers.Clear();
        }
    }

    internal void MakeItemVisible(XmlElement item)
    {
        XmlNode p = item.ParentNode;
        Stack<XmlNode> a = new(); //ancestorsToExpand
        a.Push(null);
        a.Push(p);
        while (p.Name != Constants.DocumentBookmark)
        {
            p = p.ParentNode;
            a.Push(p);
        }

        while (a.Peek() != null)
        {
            Expand(a.Pop());
        }
    }

    internal void EnsureItemsVisible(ICollection<BookmarkElement> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        Rectangle cr = ClientRectangle;
        OLVListItem fi = null;
        foreach (BookmarkElement item in items)
        {
            OLVListItem i = ModelToItem(item);
            if (i == null)
            {
                continue;
            }

            Rectangle r = GetItemRect(i.Index);
            if (r.Top >= cr.Top && r.Bottom <= cr.Bottom)
            {
                return;
            }

            fi ??= i;
        }

        if (fi != null)
        {
            EnsureVisible(fi.Index);
        }
    }

    internal void CopySelectedBookmark()
    {
        _copiedBookmarks = GetSelectedElements(false);
        Clipboard.Clear();
    }

    internal void PasteBookmarks(XmlElement target, bool asChild)
    {
        try
        {
            string t = Clipboard.GetText();
            bool c = false;
            if (t.IsNullOrWhiteSpace() == false)
            {
                PdfInfoXmlDocument doc = new();
                using (StringReader s = new(t))
                {
                    OutlineManager.ImportSimpleBookmarks(s, doc);
                }

                _copiedBookmarks = doc.Bookmarks.ToNodeList<BookmarkElement>() as List<BookmarkElement>;
                c = true;
            }

            if (_copiedBookmarks == null || _copiedBookmarks.Count == 0)
            {
                return;
            }

            CopyOrMoveElement(_copiedBookmarks, target, asChild, true, true, c || OperationAffectsDescendants);
        }
        catch (Exception)
        {
            // ignore
        }
    }

    internal List<BookmarkElement> GetSelectedElements() => GetSelectedElements(this, true);

    internal List<BookmarkElement> GetSelectedElements(bool selectChildren) =>
        GetSelectedElements(this, selectChildren);

    private static List<BookmarkElement> GetSelectedElements(VirtualObjectListView treeList, bool selectChildren)
    {
        if (treeList == null)
        {
            return null;
        }

        SelectedIndexCollection si = treeList.SelectedIndices;
        int[] il = new int[si.Count];
        si.CopyTo(il, 0);
        Array.Sort(il);
        List<BookmarkElement> el = new();
        int l = -1;
        foreach (int item in il)
        {
            BookmarkElement e = treeList.GetModelObject(item) as BookmarkElement;
            if (selectChildren)
            {
                el.Add(e);
            }
            else if (item > l)
            {
                l = item + (treeList.VirtualListDataSource as Tree).GetVisibleDescendentCount(e);
                el.Add(e);
            }
        }

        return el;
    }

    private void BookmarkEditorView_BeforeLabelEdit(object sender, LabelEditEventArgs e) => IsLabelEditing = true;

    private void _BookmarkBox_AfterLabelEdit(object sender, LabelEditEventArgs e)
    {
        IsLabelEditing = false;
        if (GetModelObject(e.Item) is not XmlElement o || string.IsNullOrEmpty(e.Label))
        {
            e.CancelEdit = true;
            return;
        }

        ReplaceTitleTextProcessor p = new(e.Label);
        Undo?.AddUndo("Editor's text", p.Process(o));
        OLVListItem i = GetItem(e.Item);
        if (o.HasChildNodes && FormHelper.IsCtrlKeyDown == false)
        {
            Expand(o);
        }

        if (i.Index < Items.Count - 1)
        {
            GetItem(i.Index + 1).BeginEdit();
        }

        RefreshItem(i);
    }

    private void BookmarkEditor_CellClick(object sender, HyperlinkClickedEventArgs e)
    {
        if (e.Column != _ActionColumn)
        {
            return;
        }

        e.Handled = true;
        ShowBookmarkProperties(e.Model as BookmarkElement);
    }

    public void ShowBookmarkProperties(BookmarkElement bookmark)
    {
        if (bookmark == null)
        {
            return;
        }

        using ActionEditorForm form = new(bookmark);
        if (form.ShowDialog() != DialogResult.OK || form.UndoActions.Count <= 0)
        {
            return;
        }

        Undo?.AddUndo("Change bookmark action attribute", form.UndoActions);
        RefreshObject(bookmark);
    }

    private void BookmarkEditor_HotItemChanged(object sender, HotItemChangedEventArgs e)
    {
        if (e.HotColumnIndex == _ActionColumn.Index || e.OldHotColumnIndex == _ActionColumn.Index
           //&& (e.HotRowIndex != e.OldHotRowIndex || e.HotColumnIndex != e.OldHotColumnIndex)
           )
        {
            // e.handled = false;
            return;
        }

        e.Handled = true;
    }

    private void _BookmarkBox_FormatRow(object sender, FormatRowEventArgs e)
    {
        if (e.Model is not BookmarkElement b)
        {
            return;
        }

        e.Item.UseItemStyleForSubItems = false;
        e.UseCellFormatEvents = false;
        if (b.MarkerColor != 0)
        {
            e.Item.BackColor = Color.FromArgb(b.MarkerColor);
        }

        Color c = b.ForeColor;
        if (c != Color.Transparent)
        {
            e.Item.ForeColor = c;
        }

        FontStyle ts = b.TextStyle;
        if (ts != FontStyle.Regular)
        {
            e.Item.Font = new Font(e.Item.Font, ts);
        }

        if (_ActionColumn.Index != -1)
        {
            e.Item.SubItems[_ActionColumn.Index].ForeColor = Color.Blue;
        }
    }

    internal BookmarkElement SearchBookmark(BookmarkMatcher matcher)
    {
        BookmarkElement s = this.GetFirstSelectedModel<BookmarkElement>();
        if (s == null)
        {
            s = GetModelObject(0) as BookmarkElement;
            if (s == null)
            {
                return null;
            }
        }

        XPathNavigator n = s.CreateNavigator();
        while (n.MoveToFollowing(Constants.Bookmark, string.Empty))
        {
            if (n.UnderlyingObject is not BookmarkElement e || !matcher.Match(e))
            {
                continue;
            }

            MakeItemVisible(e);
            EnsureModelVisible(e);
            SelectedObject = e;
            return e;
        }

        return null;
    }

    internal List<BookmarkElement> SearchBookmarks(BookmarkMatcher matcher)
    {
        List<BookmarkElement> matches = new();
        Freeze();
        try
        {
            foreach (BookmarkElement item in Roots)
            {
                SearchBookmarks(matcher, matches, item);
            }
        }
        catch (Exception ex)
        {
            FormHelper.ErrorBox("An error occurred while matching text:" + ex.Message);
        }

        Unfreeze();
        if (matches.Count <= 0)
        {
            return matches;
        }

        EnsureItemsVisible(matches);
        SelectedObjects = matches;

        return matches;
    }

    private void SearchBookmarks(BookmarkMatcher matcher, ICollection<BookmarkElement> matches, BookmarkElement item)
    {
        if (item.HasChildNodes)
        {
            foreach (BookmarkElement c in item.SelectNodes(Constants.Bookmark))
            {
                SearchBookmarks(matcher, matches, c);
            }
        }

        if (!matcher.Match(item))
        {
            return;
        }

        matches.Add(item);
        MakeItemVisible(item);
    }

    #region Drag and drop operation and drop operation

    protected override void OnCanDrop(OlvDropEventArgs args)
    {
        if (args.DataObject is not DataObject o)
        {
            return;
        }

        StringCollection f = o.GetFileDropList();
        foreach (string item in f)
        {
            if (!FileHelper.HasExtension(item, Constants.FileExtensions.Xml) &&
                !FileHelper.HasExtension(item, Constants.FileExtensions.Pdf))
            {
                continue;
            }

            args.Handled = true;
            args.DropTargetLocation = DropTargetLocation.Background;
            args.Effect = DragDropEffects.Copy;
            args.InfoMessage = "open a file" + item;
            return;
        }

        base.OnCanDrop(args);
    }

    protected override void OnModelCanDrop(ModelDropEventArgs e)
    {
        IList si = e.SourceModels;
        XmlElement ti = e.TargetModel as XmlElement;
        if (si == null || si.Count == 0 || e.TargetModel == null)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        bool copy = (ModifierKeys & Keys.Control) != Keys.None ||
                    (e.SourceModels[0] as XmlElement).OwnerDocument != ti.OwnerDocument;
        if (copy == false)
        {
            if (e.DropTargetItem.Selected)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if (si.Cast<XmlElement>().Any(item => IsAncestorOrSelf(item, ti)))
            {
                e.Effect = DragDropEffects.None;
                e.InfoMessage = "Target bookmarks cannot be a sub-bookmark for source bookmarks.";
                return;
            }
        }

        OLVListItem d = e.DropTargetItem;
        Point ml = e.MouseLocation;
        bool child = ml.X > d.Position.X + d.GetBounds(ItemBoundsPortion.ItemOnly).Width / 2;
        bool append = ml.Y > d.Position.Y + d.Bounds.Height / 2;
        if (child == false && copy == false)
        {
            int xi = e.DropTargetIndex + (append ? 1 : -1);
            if (xi > -1 && xi < e.ListView.Items.Count
                        && e.ListView.Items[xi].Selected
                        && ti.ParentNode == (e.ListView.GetModelObject(xi) as XmlElement).ParentNode)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
        }

        e.Effect = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.InfoMessage = string.Concat(copy ? "copy" : "move", "to", child ? "all child bookmarks" : string.Empty,
            append ? "behind" : "before");
        base.OnModelCanDrop(e);
    }

    protected override void OnModelDropped(ModelDropEventArgs args)
    {
        base.OnModelDropped(args);
        List<BookmarkElement> se = GetSelectedElements(args.SourceListView as TreeListView, false);
        if (se == null)
        {
            return;
        }

        BookmarkElement ti = args.TargetModel as BookmarkElement;
        OLVListItem d = args.DropTargetItem;
        Point ml = args.MouseLocation;
        Freeze();
        bool child = ml.X > d.Position.X + d.GetBounds(ItemBoundsPortion.ItemOnly).Width / 2;
        bool append = ml.Y > d.Position.Y + d.Bounds.Height / 2;
        bool copy = (ModifierKeys & Keys.Control) != Keys.None ||
                    (args.SourceModels[0] as BookmarkElement).OwnerDocument != ti.OwnerDocument;
        bool deepCopy = copy && (OperationAffectsDescendants || (ModifierKeys & Keys.Shift) != Keys.None);
        int tii = TopItemIndex;
        CopyOrMoveElement(se, ti, child, append, copy, deepCopy);
        //e.RefreshObjects ();
        TopItemIndex = tii;
        Unfreeze();
        args.Handled = true;
    }

    #endregion
}
