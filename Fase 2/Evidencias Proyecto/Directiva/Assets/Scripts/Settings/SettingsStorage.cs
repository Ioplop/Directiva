using System;
using UnityEngine;

namespace Directiva.Settings
{
    public enum SettingsLoadResult
    {
        Loaded,
        Missing,
        RecoveredFromBackup,
        InvalidData,
        NewerVersion
    }

    /// <summary>
    /// Platform-independent JSON serializer and storage contract. Override only the three
    /// raw JSON operations to implement a file, browser, cloud or testing backend.
    /// </summary>
    public abstract class SettingsStorage
    {
        protected abstract bool TryReadPrimary(out string json);
        protected abstract bool TryReadBackup(out string json);
        protected abstract bool TryWrite(string json);

        public SettingsLoadResult Load(GameSettings defaults, out GameSettings settings)
        {
            settings = defaults.Copy();
            bool found = TryReadPrimary(out string primary);
            if (found)
            {
                var result = Decode(primary, defaults, out GameSettings parsed);
                if (result == DecodeResult.NewerVersion) return SettingsLoadResult.NewerVersion;
                if (result == DecodeResult.Valid)
                {
                    settings = parsed;
                    return SettingsLoadResult.Loaded;
                }
                Debug.LogWarning("[Directiva Settings] Invalid primary settings; trying backup.");
            }

            bool hasBackup = TryReadBackup(out string backup);
            if (hasBackup)
            {
                var result = Decode(backup, defaults, out GameSettings parsed);
                if (result == DecodeResult.NewerVersion) return SettingsLoadResult.NewerVersion;
                if (result == DecodeResult.Valid)
                {
                    settings = parsed;
                    return SettingsLoadResult.RecoveredFromBackup;
                }
                Debug.LogWarning("[Directiva Settings] Invalid settings backup.");
            }

            return found || hasBackup ? SettingsLoadResult.InvalidData : SettingsLoadResult.Missing;
        }

        public bool Save(GameSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return TryWrite(JsonUtility.ToJson(settings, true));
        }

        /// <summary>Used by storage backends to avoid replacing a good backup with corrupt data.</summary>
        protected static bool IsValidSavedJson(string json)
        {
            return Decode(json, new GameSettings(), out _) == DecodeResult.Valid;
        }

        private enum DecodeResult { Valid, Invalid, NewerVersion }

        private static DecodeResult Decode(string json, GameSettings defaults, out GameSettings settings)
        {
            settings = null;
            if (string.IsNullOrWhiteSpace(json)) return DecodeResult.Invalid;
            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
                !trimmed.EndsWith("}", StringComparison.Ordinal)) return DecodeResult.Invalid;

            try
            {
                // FromJsonOverwrite preserves default values when fields are missing.
                var candidate = defaults.Copy();
                JsonUtility.FromJsonOverwrite(json, candidate);
                if (candidate.version > GameSettings.CurrentVersion)
                    return DecodeResult.NewerVersion;
                if (candidate.version < 0) return DecodeResult.Invalid;
                candidate.version = GameSettings.CurrentVersion;
                settings = candidate;
                return DecodeResult.Valid;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return DecodeResult.Invalid;
            }
        }
    }
}
