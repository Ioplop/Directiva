using UnityEngine;

namespace Directiva.MainMenu
{
    /// <summary>A single-use navigation hint across the results-to-main-menu scene load.</summary>
    public static class MainMenuNavigationRequest
    {
        private static bool openLevels;

        public static void RequestLevels() => openLevels = true;

        public static bool ConsumeLevels()
        {
            bool requested = openLevels;
            openLevels = false;
            return requested;
        }

        public static void Clear() => openLevels = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Clear();
    }
}
