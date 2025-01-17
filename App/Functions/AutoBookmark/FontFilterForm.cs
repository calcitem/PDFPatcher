﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Xml;
using BrightIdeasSoftware;
using PDFPatcher.Common;
using PDFPatcher.Model;

namespace PDFPatcher.Functions;

public partial class FontFilterForm : Form
{
    private readonly XmlElement _fontInfo;

    public FontFilterForm(XmlNode fontInfo)
    {
        InitializeComponent();
        _fontInfo = fontInfo as XmlElement;

        TreeListView.TreeRenderer tcr = _FontInfoBox.TreeColumnRenderer;
        tcr.LinePen = new Pen(SystemColors.ControlDark) { DashCap = DashCap.Round, DashStyle = DashStyle.Dash };

        _FontInfoBox.CanExpandGetter = o => o is XmlElement { Name: Constants.Font.ThisName, HasChildNodes: true };
        _FontInfoBox.ChildrenGetter = o => o is not XmlElement f ? null : f.SelectNodes(Constants.Font.Size);
        _FontInfoBox.RowFormatter = o =>
        {
            if (_FontInfoBox.GetParent(o.RowObject) != null)
            {
                return;
            }

            o.SubItems[0].Font = new Font(o.SubItems[0].Font, FontStyle.Bold);
            o.SubItems[1].Text = string.Empty;
            o.BackColor = Color.LightBlue;
        };
        _FontNameSizeColumn.AspectGetter = o =>
        {
            if (o is not XmlElement f)
            {
                return null;
            }

            if (f.Name == Constants.Font.ThisName)
            {
                return f.GetAttribute(Constants.Font.Name);
            }

            if (f.ParentNode?.Name != Constants.Font.ThisName)
            {
                return null;
            }

            f.GetAttribute(Constants.Font.Size).TryParse(out float p);
            string t = f.GetAttribute(Constants.FontOccurance.FirstText);
            return string.Concat(p.ToText(), "(", t, ")");
        };
        _CountColumn.AspectGetter = o =>
        {
            if (o is not XmlElement f)
            {
                return null;
            }

            f.GetAttribute(Constants.FontOccurance.Count).TryParse(out int p);
            return p;
        };
        _FirstPageColumn.AspectGetter = o =>
        {
            if (o is not XmlElement f)
            {
                return null;
            }

            f.GetAttribute(Constants.FontOccurance.FirstPage).TryParse(out int p);
            return p;
        };
        _ConditionColumn.AspectGetter = o => o is AutoBookmarkCondition c ? c.Description : (object)null;
    }

    internal AutoBookmarkCondition[] FilterConditions { get; private set; }

    private void FontFilterForm_Load(object sender, EventArgs e)
    {
        if (_fontInfo == null)
        {
            FormHelper.ErrorBox("Missing font information.");
            _OkButton.Enabled = false;
            return;
        }

        XmlNodeList fonts = _fontInfo.SelectNodes(Constants.Font.ThisName + "[@" + Constants.Font.Name + " and " +
                                                  Constants.Font.Size + "]");
        XmlElement[] fi = new XmlElement[fonts.Count];
        int i = 0;
        foreach (XmlElement f in fonts)
        {
            fi[i++] = f;
        }

        _FontInfoBox.AddObjects(fi);
        foreach (XmlElement item in _FontInfoBox.Roots)
        {
            _FontInfoBox.Expand(item);
        }

        _FontInfoBox.EnsureVisible(0);
        _FontInfoBox.Sort(_CountColumn, SortOrder.Descending);
    }

    protected void _OkButton_Click(object source, EventArgs args)
    {
        DialogResult = DialogResult.OK;
        if (_FilterBox.Items.Count > 0)
        {
            FilterConditions = new AutoBookmarkCondition[_FilterBox.Items.Count];
            for (int i = 0; i < FilterConditions.Length; i++)
            {
                FilterConditions[i] = _FilterBox.GetModelObject(i) as AutoBookmarkCondition;
            }
        }

        Close();
    }

    protected void _CancelButton_Click(object source, EventArgs args)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void _AddFilterMenu_Opening(object sender, CancelEventArgs e)
    {
        if (_FontInfoBox.FocusedItem == null)
        {
            if (_FontInfoBox.SelectedItem != null)
            {
                _FontInfoBox.FocusedItem = _FontInfoBox.SelectedItem;
            }
            else
            {
                e.Cancel = true;
                return;
            }
        }

        if (_FontInfoBox.GetModelObject(_FontInfoBox.FocusedItem.Index) is not XmlElement f)
        {
            e.Cancel = true;
            return;
        }

        string n =
            (f.ParentNode.Name == Constants.Font.ThisName ? f.ParentNode as XmlElement : f).GetAttribute(Constants.Font
                .Name);
        if (string.IsNullOrEmpty(n))
        {
            e.Cancel = true;
            return;
        }

        f.GetAttribute(Constants.Font.Size).TryParse(out float s);

        _AddFilterMenu.Items.Clear();
        int p = n.IndexOf('+');
        int m = n.IndexOfAny(new[] { '-', ',' }, p != -1 ? p : 0);
        string fn;
        if (p != -1)
        {
            if (m > p + 1)
            {
                fn = n.Substring(p + 1, m - p - 1);
                if (s > 0)
                {
                    _AddFilterMenu.Items
                            .Add("Filter fonts whose name contains \"" + fn + "\" and whose size is " + s.ToText())
                            .Tag =
                        new FilterSetting(fn, false, s);
                }
                else
                {
                    _AddFilterMenu.Items.Add("Filter fonts whose name contains \"" + fn + "\"").Tag =
                        new FilterSetting(fn, false, 0);
                }
            }

            fn = n.Substring(p + 1);
            if (s > 0)
            {
                _AddFilterMenu.Items
                        .Add("Filter fonts whose name contains \"" + fn + "\" and whose size is " + s.ToText()).Tag =
                    new FilterSetting(fn, false, s);
            }
            else
            {
                _AddFilterMenu.Items.Add("Filter fonts whose name contains \"" + fn + "\"").Tag =
                    new FilterSetting(fn, false, 0);
            }
        }
        else if (m != -1)
        {
            fn = n.Substring(0, m);
            if (s > 0)
            {
                _AddFilterMenu.Items
                        .Add("Filter fonts whose name contains \"" + fn + "\" and whose size is " + s.ToText()).Tag =
                    new FilterSetting(fn, false, s);
            }
            else
            {
                _AddFilterMenu.Items.Add("Filter fonts whose name contains \"" + fn + "\"").Tag =
                    new FilterSetting(fn, false, 0);
            }
        }

        if (_AddFilterMenu.Items.Count > 0)
        {
            _AddFilterMenu.Items.Add(new ToolStripSeparator());
        }

        if (s > 0)
        {
            _AddFilterMenu.Items.Add("Filter font with name \"" + n + "\" and size " + s.ToText()).Tag =
                new FilterSetting(n, true, s);
        }
        else
        {
            _AddFilterMenu.Items.Add("Filter fonts whose name is \"" + n + "\"").Tag = new FilterSetting(n, true, 0);
        }

        e.Cancel = false;
    }

    private void _AddFilterMenu_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
    {
        if (e.ClickedItem.Tag is not FilterSetting f)
        {
            return;
        }

        AutoBookmarkCondition fc = new AutoBookmarkCondition.FontNameCondition(f.FontName, f.FullMatch);
        if (f.Size > 0)
        {
            AutoBookmarkCondition.MultiCondition m = new(fc);
            m.Conditions.Add(new AutoBookmarkCondition.TextSizeCondition(f.Size));
            fc = m;
        }

        _FilterBox.AddObject(fc);
    }

    private void ControlEvent(object sender, EventArgs e)
    {
        if (sender == _RemoveConditionButton)
        {
            _FilterBox.RemoveObjects(_FilterBox.SelectedObjects);
        }
        else if (sender == _AddConditionButton)
        {
            _AddFilterMenu.Show(_AddConditionButton, 0, _AddConditionButton.Height);
        }
    }

    private sealed class FilterSetting
    {
        public FilterSetting(string fontName, bool fullMatch, float size)
        {
            FontName = fontName;
            FullMatch = fullMatch;
            Size = size;
        }

        internal string FontName { get; }
        internal bool FullMatch { get; }
        internal float Size { get; }
    }
}
