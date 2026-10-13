namespace Directiva.MainMenu
{
    /// <summary>One-shot transfer of a level identifier across a scene load.</summary>
    public static class LevelLaunchContext
    {
        private static string pendingId;

        public static void SetPending(string id) { pendingId = id; }

        public static bool TryConsume(out string id)
        {
            id = pendingId;
            pendingId = null;
            return !string.IsNullOrEmpty(id);
        }

        public static void Clear() { pendingId = null; }
    }
}
