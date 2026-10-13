using UnityEngine;

namespace Directiva.Settings
{
    /// <summary>JSON stored via PlayerPrefs (IndexedDB in WebGL); no filesystem assumptions.</summary>
    public sealed class PlayerPrefsSettingsStorage : SettingsStorage
    {
        private readonly string primaryKey;
        private readonly string backupKey;

        public PlayerPrefsSettingsStorage(string key = "Directiva.Settings")
        {
            primaryKey = key;
            backupKey = key + ".Backup";
        }

        protected override bool TryReadPrimary(out string json)
        {
            json = PlayerPrefs.GetString(primaryKey, null);
            return PlayerPrefs.HasKey(primaryKey);
        }

        protected override bool TryReadBackup(out string json)
        {
            json = PlayerPrefs.GetString(backupKey, null);
            return PlayerPrefs.HasKey(backupKey);
        }

        protected override bool TryWrite(string json)
        {
            try
            {
                if (PlayerPrefs.HasKey(primaryKey))
                {
                    string current = PlayerPrefs.GetString(primaryKey);
                    if (IsValidSavedJson(current)) PlayerPrefs.SetString(backupKey, current);
                }

                PlayerPrefs.SetString(primaryKey, json);
                PlayerPrefs.Save();
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[Directiva Settings] PlayerPrefs save failed: {exception.Message}");
                return false;
            }
        }
    }
}
