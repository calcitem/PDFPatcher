﻿using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace PDFPatcher;

public class ToolbarOptions
{
    public ToolbarOptions() => ShowGeneralToolbar = true;

    [XmlAttribute("Show Main Toolbar")]
    [DefaultValue(true)]
    public bool ShowGeneralToolbar { get; set; }

    [XmlElement("Button")] public List<ButtonOption> Buttons { get; } = new();

    public void Reset()
    {
        Buttons.Clear();
        foreach (Toolkit item in Toolkit.Toolkits)
        {
            Buttons.Add(new ButtonOption(item.Identifier, item.Name, item.ShowText, item.DefaultVisible));
        }
    }

    internal void RemoveInvalidButtons()
    {
        if (Buttons.Count == 0)
        {
            Reset();
            return;
        }

        for (int i = Buttons.Count - 1; i >= 0; i--)
        {
            if (Buttons[i].GetToolkit() == null)
            {
                Buttons.RemoveAt(i);
            }
        }
    }

    internal void AddMissedButtons()
    {
        foreach (Toolkit item in Toolkit.Toolkits)
        {
            foreach (ButtonOption unused in Buttons.Where(b => b.ID == item.Identifier))
            {
                goto Next;
            }

            Buttons.Add(new ButtonOption(item.Identifier, item.Name, item.ShowText, false));
        Next:;
        }
    }

    public class ButtonOption
    {
        public ButtonOption()
        {
        }

        public ButtonOption(string id, string name, bool showText, bool visible)
        {
            ID = id;
            DisplayName = name;
            ShowText = showText;
            Visible = visible;
        }

        [XmlAttribute("ID")] public string ID { get; set; }

        [XmlAttribute("Button Name")] public string DisplayName { get; set; }

        [XmlAttribute("Show button text")] public bool ShowText { get; set; }

        [XmlAttribute("Show button")] public bool Visible { get; set; }

        internal Toolkit GetToolkit() => Toolkit.Get(ID);

        internal ToolStripButton CreateButton()
        {
            ToolStripButton b = GetToolkit().CreateButton();
            b.Text = DisplayName;
            b.DisplayStyle = ShowText ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image;
            return b;
        }
    }
}
