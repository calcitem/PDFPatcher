﻿using System;
using System.Xml;

namespace PDFPatcher.Processor;

internal sealed class SetTextStyleProcessor : IPdfInfoXmlProcessor
{
	public enum Style
	{
		SetBold = 0, SetItalic = 1, RemoveBold = 10, RemoveItalic = 11
	}

	private readonly Style _style;

	public SetTextStyleProcessor(XmlElement element, Style style) {
		string s = element.GetAttribute(Constants.BookmarkAttributes.Style);
		switch (style) {
			case Style.SetBold when s != Constants.BookmarkAttributes.StyleType.Bold &&
									s != Constants.BookmarkAttributes.StyleType.BoldItalic:
				_style = Style.SetBold;
				break;
			case Style.SetBold:
				_style = Style.RemoveBold;
				break;
			case Style.SetItalic when s != Constants.BookmarkAttributes.StyleType.Italic &&
									  s != Constants.BookmarkAttributes.StyleType.BoldItalic:
				_style = Style.SetItalic;
				break;
			case Style.SetItalic:
				_style = Style.RemoveItalic;
				break;
		}
	}

	#region IInfoDocProcessor 成员

	public string Name =>
		_style switch {
			Style.SetBold => "设置书签文本为粗体",
			Style.SetItalic => "设置书签文本为斜体",
			Style.RemoveBold => "清除书签文本粗体样式",
			Style.RemoveItalic => "清除书签文本斜体样式",
			_ => ""
		};

	public IUndoAction Process(XmlElement item) {
		string value = item.GetAttribute(Constants.BookmarkAttributes.Style);
		int style = 0;
		switch (value) {
			case Constants.BookmarkAttributes.StyleType.Bold:
				style = 1;
				break;
			case Constants.BookmarkAttributes.StyleType.Italic:
				style = 2;
				break;
			case Constants.BookmarkAttributes.StyleType.BoldItalic:
				style = 3;
				break;
		}

		switch (_style) {
			case Style.SetBold:
				if ((style & 0x01) > 0) {
					return null;
				}

				style |= 0x01;
				break;
			case Style.SetItalic:
				if ((style & 0x02) > 0) {
					return null;
				}

				style |= 0x02;
				break;
			case Style.RemoveBold:
				if ((style & 0x01) == 0) {
					return null;
				}

				style ^= 0x01;
				break;
			case Style.RemoveItalic:
				if ((style & 0x02) == 0) {
					return null;
				}

				style ^= 0x02;
				break;
			default: throw new ArgumentOutOfRangeException("Style");
		}

		value = style switch {
			1 => Constants.BookmarkAttributes.StyleType.Bold,
			2 => Constants.BookmarkAttributes.StyleType.Italic,
			3 => Constants.BookmarkAttributes.StyleType.BoldItalic,
			_ => null
		};
		return UndoAttributeAction.GetUndoAction(item, Constants.BookmarkAttributes.Style, value);
	}

	#endregion
}