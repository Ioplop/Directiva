using System;
using UnityEngine;

namespace Directiva.Levels
{
    /// <summary>
    /// A level's menu metadata and the enemy program to load. World layout remains
    /// owned by the chosen scene for now; this asset never stores player code.
    /// </summary>
    [CreateAssetMenu(fileName = "NewLevel", menuName = "Directiva/Level")]
    public sealed class DirectivaLevel : ScriptableObject
    {
        [SerializeField] private string levelName = "Nuevo nivel";
        [SerializeField] private int levelNumber = 1;
        [SerializeField, Min(0)] private int requiredStars;

        // Kept as a path because UnityEditor.SceneAsset is unavailable in player builds.
        // The custom Inspector exposes this as a SceneAsset selector.
        [SerializeField] private string scenePath = "Assets/Scenes/BaseSimulation.unity";

        // The next three fields are selected by the custom Inspector. EntryModule is
        // the dot-qualified module path relative to Resources/Levels/<codeFolder>.
        [SerializeField] private string codeFolder;
        [SerializeField] private string entryModule;
        [SerializeField] private string entryFunction;

        public string LevelName => string.IsNullOrWhiteSpace(levelName) ? name : levelName;
        public int LevelNumber => levelNumber;
        public int RequiredStars => Mathf.Max(0, requiredStars);
        public string ScenePath => scenePath;
        // SceneManager.LoadScene expects the build path WITHOUT the .unity suffix.
        public string SceneLoadPath => scenePath != null &&
            scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
            ? scenePath.Substring(0, scenePath.Length - ".unity".Length)
            : scenePath;
        public string CodeFolder => codeFolder;
        public string EntryModule => entryModule;
        public string EntryFunction => entryFunction;
        public string QualifiedEnemyEntry => entryModule + "." + entryFunction;
    }
}
