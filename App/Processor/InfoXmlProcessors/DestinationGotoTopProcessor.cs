﻿using System.Xml;

namespace PDFPatcher.Processor;

internal sealed class DestinationGotoTopProcessor : IPdfInfoXmlProcessor
{
    #region IInfoDocProcessor member

    public string Name => "Set click target to page";

    public IUndoAction Process(XmlElement item)
    {
        if (!item.HasAttribute(Constants.DestinationAttributes.Page))
        {
            return null;
        }

        UndoActionGroup undo = new();
        undo.SetAttribute(item, Constants.DestinationAttributes.View, Constants.DestinationAttributes.ViewType.XYZ);
        undo.SetAttribute(item, Constants.Coordinates.Top, "10000");
        undo.RemoveAttribute(item, Constants.Coordinates.ScaleFactor);
        return undo;
    }

    #endregion
}
