using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Directiva.CodeUI
{
    /// <summary>Opens the scripts directory with the user's configured desktop file manager.</summary>
    internal static class ScriptsFolderOpener
    {
        internal static ProcessStartInfo CreateStartInfo(string folderPath, RuntimePlatform platform)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("A scripts folder path is required.", nameof(folderPath));

            string absolutePath = Path.GetFullPath(folderPath);
            switch (platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                    // ShellExecute opens directories with Explorer, without starting a command shell.
                    return new ProcessStartInfo
                    {
                        FileName = absolutePath,
                        UseShellExecute = true
                    };

                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.LinuxPlayer:
                    // xdg-open dispatches directories to the user's default file manager
                    // (Nautilus, Nemo, Dolphin, etc.), not to a hard-coded distro app.
                    // A file URI encodes whitespace/quotes safely as ONE process argument.
                    string uri = new Uri(absolutePath).AbsoluteUri;
                    return new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = "\"" + uri + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                default:
                    throw new PlatformNotSupportedException(
                        "Opening the scripts folder is supported on Windows and Linux desktops.");
            }
        }

        internal static void Open(string folderPath, RuntimePlatform platform)
        {
            ProcessStartInfo startInfo = CreateStartInfo(folderPath, platform);
            if (!Directory.Exists(Path.GetFullPath(folderPath)))
                throw new DirectoryNotFoundException("Scripts folder not found: " + folderPath);

            // Dispose only our process handle; the file manager remains open.
            using (Process.Start(startInfo)) { }
        }
    }
}
