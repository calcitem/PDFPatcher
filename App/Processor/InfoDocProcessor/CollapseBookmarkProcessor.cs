﻿using System.Xml;
using PDFPatcher.Model;

namespace PDFPatcher.Processor;

internal sealed class CollapseBookmarkProcessor : IInfoDocProcessor
{
    public BookmarkStatus BookmarkStatus { get; set; }

    #region IBookmarkProcessor member

    public bool Process(XmlElement bookmark)
    {
        switch (BookmarkStatus)
        {
            case BookmarkStatus.AsIs:
                return false;
            case BookmarkStatus.CollapseAll:
                bookmark.SetAttribute(Constants.BookmarkAttributes.Open, Constants.Boolean.False);
                return true;
            case BookmarkStatus.ExpandAll:
                bookmark.SetAttribute(Constants.BookmarkAttributes.Open, Constants.Boolean.True);
                return true;
            case BookmarkStatus.ExpandTop:
                XmlNode p = bookmark.ParentNode;
                bookmark.SetAttribute(Constants.BookmarkAttributes.Open,
                    p is { Name: Constants.DocumentBookmark } ? Constants.Boolean.True : Constants.Boolean.False);

                return true;
            default:
                return false;
        }
    }

    #endregion
}
