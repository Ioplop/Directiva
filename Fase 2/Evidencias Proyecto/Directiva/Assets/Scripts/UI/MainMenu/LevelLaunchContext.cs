using System;
using Directiva.Levels;

namespace Directiva.MainMenu
{
    /// <summary>
    /// Transfers the selected level across a scene load. ScriptableObjects stay
    /// alive as assets; the selected definition remains available for Stop/restart.
    /// Legacy launch IDs remain supported for older callers and LevelBootstrapper.
    /// </summary>
    public static class LevelLaunchContext
    {
        private static string pendingId;

        public static string CurrentLevelName { get; private set; }
        public static string CurrentLevelId { get; private set; }
        public static DirectivaLevel CurrentLevel { get; private set; }

        public static void SetPending(string id) => SetPending(id, null);

        public static void SetPending(string id, string displayName)
        {
            pendingId = id;
            CurrentLevel = null;
            CurrentLevelId = id;
            CurrentLevelName = displayName;
        }

        public static void SetPendingLevel(DirectivaLevel level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            // ScriptableObject levels are consumed directly by the scene synchronizer;
            // the legacy LevelBootstrapper has no ID/event to consume in this path.
            pendingId = null;
            CurrentLevel = level;
            CurrentLevelId = level.name;
            CurrentLevelName = level.LevelName;
        }

        public static bool TryConsume(out string id)
        {
            id = pendingId;
            pendingId = null;
            return !string.IsNullOrEmpty(id);
        }

        public static void Clear()
        {
            pendingId = null;
            CurrentLevel = null;
            CurrentLevelId = null;
            CurrentLevelName = null;
        }
    }
}
