using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Directiva.Settings
{
    /// <summary>Desktop settings at Application.persistentDataPath/settings.json.</summary>
    public sealed class JsonFileSettingsStorage : SettingsStorage
    {
        public string FilePath { get; }

        public JsonFileSettingsStorage(string filePath = null)
        {
            FilePath = filePath ?? Path.Combine(Application.persistentDataPath, "settings.json");
        }

        protected override bool TryReadPrimary(out string json) => TryReadFile(FilePath, out json);
        protected override bool TryReadBackup(out string json) => TryReadFile(FilePath + ".bak", out json);

        private static bool TryReadFile(string path, out string json)
        {
            json = null;
            try
            {
                if (!File.Exists(path)) return false;
                json = File.ReadAllText(path, Encoding.UTF8);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Directiva Settings] Cannot read '{path}': {exception.Message}");
                return false;
            }
        }

        protected override bool TryWrite(string json)
        {
            string temporary = FilePath + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(temporary, json, new UTF8Encoding(false));

                if (!File.Exists(FilePath))
                {
                    File.Move(temporary, FilePath);
                    return true;
                }

                if (TryReadFile(FilePath, out string existing) && IsValidSavedJson(existing))
                {
                    // Preserve the previous valid version, not a broken file.
                    File.Copy(FilePath, FilePath + ".bak", true);
                }
                else
                {
                    // Never silently destroy a manually repairable corrupt configuration.
                    string preserved = FilePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
                    File.Copy(FilePath, preserved);
                }

                try
                {
                    // Atomic replacement on platforms that support it.
                    File.Replace(temporary, FilePath, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temporary, FilePath, true);
                }
                catch (IOException)
                {
                    // Some filesystems do not implement File.Replace.
                    File.Copy(temporary, FilePath, true);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogError($"[Directiva Settings] Cannot save settings: {exception.Message}");
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { /* Best effort cleanup only. */ }
                catch (UnauthorizedAccessException) { /* Best effort cleanup only. */ }
            }
        }
    }
}
