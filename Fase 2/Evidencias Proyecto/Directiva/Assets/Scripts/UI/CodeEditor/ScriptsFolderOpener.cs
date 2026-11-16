using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Directiva.CodeUI
{
    internal enum ScriptsFolderOpenMode
    {
        WindowsShell,
        LinuxDefaultFileManager
    }

    /// <summary>A validated folder path and its platform-specific opening method.</summary>
    internal readonly struct ScriptsFolderOpenRequest
    {
        internal string FolderPath { get; }
        internal string FileUri { get; }
        internal ScriptsFolderOpenMode Mode { get; }

        internal ScriptsFolderOpenRequest(string folderPath, string fileUri,
            ScriptsFolderOpenMode mode)
        {
            FolderPath = folderPath;
            FileUri = fileUri;
            Mode = mode;
        }
    }

    /// <summary>
    /// Opens the player's scripts in their preferred desktop file manager.
    /// Process.Start is deliberately avoided: it is not reliably supported by IL2CPP.
    /// </summary>
    internal static class ScriptsFolderOpener
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        // ShellExecuteW honors Windows' registered folder handler and works under IL2CPP.
        [DllImport("shell32.dll", EntryPoint = "ShellExecuteW", CharSet = CharSet.Unicode)]
        private static extern IntPtr ShellExecuteFolder(IntPtr window, string operation,
            string file, IntPtr parameters, IntPtr directory, int showCommand);
#endif

#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        // GIO resolves the default directory handler (Nautilus, Nemo, Dolphin, etc.).
        // The last two arguments may be null; no unmanaged error object is allocated.
        [DllImport("libgio-2.0.so.0", EntryPoint = "g_app_info_launch_default_for_uri",
            CallingConvention = CallingConvention.Cdecl)]
        private static extern int LaunchDefaultFileUri(IntPtr uri, IntPtr context,
            IntPtr error);
#endif

        // Separate this pure part from the native call, so all platforms can test it.
        internal static ScriptsFolderOpenRequest CreateOpenRequest(string folderPath,
            RuntimePlatform platform)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("A scripts folder path is required.", nameof(folderPath));

            ScriptsFolderOpenMode mode;
            switch (platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                    mode = ScriptsFolderOpenMode.WindowsShell;
                    break;
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.LinuxPlayer:
                    mode = ScriptsFolderOpenMode.LinuxDefaultFileManager;
                    break;
                default:
                    throw new PlatformNotSupportedException(
                        "Opening the scripts folder is supported on Windows and Linux desktops.");
            }

            string fullPath = Path.GetFullPath(folderPath);
            return new ScriptsFolderOpenRequest(fullPath,
                new Uri(fullPath).AbsoluteUri, mode);
        }

        internal static void Open(string folderPath, RuntimePlatform platform)
        {
            ScriptsFolderOpenRequest request = CreateOpenRequest(folderPath, platform);
            if (!Directory.Exists(request.FolderPath))
                throw new DirectoryNotFoundException("Scripts folder not found: " + request.FolderPath);

            switch (request.Mode)
            {
                case ScriptsFolderOpenMode.WindowsShell:
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                    // ShellExecute results <= 32 represent errors, not OS error numbers.
                    long result = ShellExecuteFolder(IntPtr.Zero, "open", request.FolderPath,
                        IntPtr.Zero, IntPtr.Zero, 1).ToInt64();
                    if (result <= 32)
                        throw new IOException("Could not open the scripts folder (Windows shell code " +
                            result + ").");
                    return;
#else
                    break;
#endif
                case ScriptsFolderOpenMode.LinuxDefaultFileManager:
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
                    // Marshal the URI as UTF-8 explicitly, including accented folder names.
                    byte[] bytes = Encoding.UTF8.GetBytes(request.FileUri + "\0");
                    IntPtr uri = Marshal.AllocHGlobal(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, uri, bytes.Length);
                        if (LaunchDefaultFileUri(uri, IntPtr.Zero, IntPtr.Zero) == 0)
                            throw new IOException("Could not open the scripts folder using the " +
                                "default Linux file manager.");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(uri);
                    }
                    return;
#else
                    break;
#endif
            }

            throw new PlatformNotSupportedException(
                "The requested file manager is unavailable on this operating system.");
        }
    }
}
