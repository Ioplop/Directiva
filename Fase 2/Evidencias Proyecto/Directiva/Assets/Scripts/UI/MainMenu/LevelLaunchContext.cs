namespace Directiva.MainMenu
{
    /// <summary>One-shot transfer of a level identifier across a scene load.</summary>
    public static class LevelLaunchContext
    {
        private static string pendingId;

        /// <summary>The current level name remains available after the one-shot launch ID is consumed.</summary>
        public static string CurrentLevelName { get; private set; }
        public static string CurrentLevelId { get; private set; }

        public static void SetPending(string id) => SetPending(id, null);

        public static void SetPending(string id, string displayName)
        {
            pendingId = id;
            CurrentLevelId = id;
            CurrentLevelName = displayName;
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
            CurrentLevelId = null;
            CurrentLevelName = null;
        }
    }
}
