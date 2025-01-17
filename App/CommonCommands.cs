﻿using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using PDFPatcher.Common;

namespace PDFPatcher;

internal static class CommonCommands
{
    internal static void CreateShortcut()
    {
        string p = Path.GetDirectoryName(Application.ExecutablePath);
        ShortcutFile s =
            new(FileHelper.CombinePath(p, Constants.AppEngName + ".exe"))
            {
                WorkingDirectory = p,
                Description = Constants.AppName
            };
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        s.Save(FileHelper.CombinePath(desktopPath, Constants.AppName + ".lnk"));

        FormHelper.InfoBox("A shortcut for " + Constants.AppName + " has been created on the desktop.");
    }

    internal static void VisitHomePage() => Process.Start(Constants.AppHomePage);
}
