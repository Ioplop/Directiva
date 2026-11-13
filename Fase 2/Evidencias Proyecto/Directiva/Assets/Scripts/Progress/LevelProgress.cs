using System.Collections.Generic;
using System.IO;
using Directiva.Levels;
using UnityEngine;

namespace Directiva.Progress
{
    /// <summary>Shared progress for the main menu and match scene (independent of settings.json).</summary>
    public static class LevelProgress
    {
        private static LevelProgressStore store;

        public static string SavePath => Path.Combine(Application.persistentDataPath, "Save.bin");
        public static LevelProgressStore Current => store ?? (store = new LevelProgressStore(SavePath));

        // Unity may retain static fields between Play sessions with Domain Reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => store = null;

        /// <summary>Only currently configured levels contribute to the menu's unlock currency.</summary>
        public static int CountStarsInLevels(IEnumerable<DirectivaLevel> levels,
            LevelProgressStore source = null)
        {
            int result = 0;
            var counted = new HashSet<int>();
            if (levels == null) return result;
            foreach (DirectivaLevel level in levels)
            {
                if (level != null && level.LevelNumber > 0 && counted.Add(level.LevelNumber))
                    result += LevelStarRules.Count((source ?? Current).GetStars(level.LevelNumber));
            }
            return result;
        }
    }
}
