﻿using System;
using System.Collections.Generic;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.Win32;

namespace PDFPatcher.Common;

internal static class FontHelper
{
    public static string FontDirectory { get; } =
        Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.System) + "\\..\\fonts\\");

    /// <summary>
    ///     list the installed font and its path.
    /// </summary>
    /// <param name="includeFamilyName">Does it include the font group name </param>
    public static Dictionary<string, string> GetInstalledFonts(bool includeFamilyName)
    {
        Dictionary<string, string> d = new(50);
        using RegistryKey k =
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
        foreach (string name in k.GetValueNames())
        {
            string p = k.GetValue(name) as string;
            if (string.IsNullOrEmpty(p))
            {
                continue;
            }

            if (p.IndexOf('\\') == -1)
            {
                p = FontDirectory + p;
            }

            FilePath fp = new(p);
            try
            {
                if (fp.HasExtension(Constants.FileExtensions.Ttf)
                    || fp.HasExtension(Constants.FileExtensions.Otf))
                {
                    AddFontNames(d, p, includeFamilyName);
                }
                else if (fp.HasExtension(Constants.FileExtensions.Ttc))
                {
                    int nl = BaseFont.EnumerateTTCNames(p).Length;
                    //Tracker.DebugMessage (p);
                    for (int i = 0; i < nl; i++)
                    {
                        AddFontNames(d, p + "," + i.ToText(), includeFamilyName);
                    }
                }
            }
            catch (IOException)
            {
                // ignore
            }
            catch (NullReferenceException)
            {
            }
            catch (DocumentException)
            {
                // ignore
            }
        }

        return d;
    }

    private static void AddFontNames(IDictionary<string, string> fontNames, string fontPath, bool includeFamilyName)
    {
        object[] nl = BaseFont.GetAllFontNames(fontPath, "Cp936", null);
        //Tracker.DebugMessage (fontPath);
        if (includeFamilyName)
        {
            fontNames[nl[0] as string] = fontPath;
        }

        string[][] ffn = nl[2] as string[][];
        string n = null;
        string nn = null, cn = null;
        foreach (string[] fn in ffn)
        {
            string enc = fn[2];
            n = fn[3];
            if ("2052" == enc)
            {
                cn = n;
                break;
            }

            switch (enc)
            {
                case "1033":
                case "0" when nn == null:
                    nn = n;
                    break;
            }
        }

        if (n != null)
        {
            //Tracker.DebugMessage (cn ?? nn ?? n);
            fontNames[cn ?? nn ?? n] = fontPath;
        }
        //foreach (string[] item in nl[1] as string[][]) {
        //    fontNames[item] = fontPath;
        //    Tracker.DebugMessage (item);
        //}
    }
}
