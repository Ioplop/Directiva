using System;
using UnityEngine;

namespace Directiva.Levels
{
    /// <summary>
    /// A level owns the initial layout, simulation tuning and enemy entry program.
    /// Player code is never stored in the level.
    /// </summary>
    [CreateAssetMenu(fileName = "NewLevel", menuName = "Directiva/Level")]
    public sealed class DirectivaLevel : ScriptableObject
    {
        [SerializeField] private string levelName = "Nuevo nivel";
        [SerializeField] private int levelNumber = 1;
        [SerializeField, Min(0)] private int requiredStars;

        [Header("Objetivos de la partida")]
        [SerializeField, Min(1), Tooltip("Duración máxima en segundos simulados (una partida que llega al límite es empate).")]
        private int maxMatchDurationSeconds = 900;
        [SerializeField, Min(1), Tooltip("Ganar en MENOS de estos segundos simulados concede la estrella de velocidad.")]
        private int fastVictorySeconds = 600;

        // Kept as a path because UnityEditor.SceneAsset is unavailable in player builds.
        // The custom Inspector exposes this as a SceneAsset selector.
        [SerializeField] private string scenePath = "Assets/Scenes/BaseSimulation.unity";

        // The next three fields are selected by the custom Inspector. EntryModule is
        // the dot-qualified module path relative to Resources/Levels/<codeFolder>.
        [SerializeField] private string codeFolder;
        [SerializeField] private string entryModule;
        [SerializeField] private string entryFunction;

        [Header("Simulation and map")]
        [SerializeField] private LevelWorldLayout worldLayout = new LevelWorldLayout();
        [SerializeField] private LevelSimulationSettings simulationSettings = new LevelSimulationSettings();

        // Existing assets predate these fields. Use exactly the original scene
        // defaults when Unity deserializes a legacy level without them.
        public LevelWorldLayout World => worldLayout ?? (worldLayout = new LevelWorldLayout());
        public LevelSimulationSettings Settings => simulationSettings ??
            (simulationSettings = new LevelSimulationSettings());

        public DSimulation.SimulationParameters CreateSimulationParameters() =>
            Settings.ToParameters();

        public string LevelName => string.IsNullOrWhiteSpace(levelName) ? name : levelName;
        public int LevelNumber => levelNumber;
        public int RequiredStars => Mathf.Max(0, requiredStars);
        public int MaxMatchDurationSeconds => Mathf.Max(1, maxMatchDurationSeconds);
        public int FastVictorySeconds => Mathf.Max(1, fastVictorySeconds);
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
