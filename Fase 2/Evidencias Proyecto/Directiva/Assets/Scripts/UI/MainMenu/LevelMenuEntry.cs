using System;
using UnityEngine;
using UnityEngine.Events;

namespace Directiva.MainMenu
{
    /// <summary>Inspector-defined entry for the level selection screen.</summary>
    [Serializable]
    public sealed class LevelMenuEntry
    {
        public string name = "Nuevo nivel";
        [Range(0, 3)] public int stars;
        public bool locked;
        [Min(0)]
        [Tooltip("Total earned stars required to unlock this level. 0 = automatic suggested requirement based on its list position and Max Stars. Display-only for now; Locked still controls accessibility.")]
        public int requiredStars;
        [Tooltip("Scene name listed in Build Profiles. Several levels may share the same scene.")]
        public string sceneName = "BasicCoding";
        [Tooltip("Identifier delivered to the destination scene's LevelBootstrapper.")]
        public string launchId = "level_01";
        [Tooltip("Called immediately before SceneManager.LoadScene, while the menu is still loaded.")]
        public UnityEvent onBeforeLoad = new UnityEvent();
    }
}
