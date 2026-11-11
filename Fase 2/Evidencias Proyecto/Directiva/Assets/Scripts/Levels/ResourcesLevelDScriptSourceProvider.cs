using System;
using DSCompilation.DScript.Modules;
using UnityEngine;

namespace Directiva.Levels
{
    /// <summary>
    /// Resolves DScript imports ONLY inside one enemy-level folder. Another level's
    /// modules, and the player's workspace, are never visible to this provider.
    /// .dscript files are imported as TextAssets by DScriptTextImporter (Editor).
    /// </summary>
    public sealed class ResourcesLevelDScriptSourceProvider : IDScriptSourceProvider
    {
        private readonly string resourcesRoot;

        public ResourcesLevelDScriptSourceProvider(string levelFolder)
        {
            if (string.IsNullOrWhiteSpace(levelFolder) ||
                levelFolder.StartsWith("/", StringComparison.Ordinal) ||
                levelFolder.Contains("\\") || levelFolder.Contains(":") ||
                levelFolder.Contains(".."))
                throw new ArgumentException("Invalid Resources/Levels folder: " + levelFolder,
                    nameof(levelFolder));

            string[] parts = levelFolder.Split('/');
            foreach (string part in parts)
                if (string.IsNullOrWhiteSpace(part) || part == ".")
                    throw new ArgumentException("Invalid level folder segment: " + levelFolder,
                        nameof(levelFolder));

            resourcesRoot = "Levels/" + levelFolder.Trim('/');
        }

        public bool TryReadModule(string moduleName, out string source)
        {
            source = null;
            if (!DScriptModuleLoader.IsValidName(moduleName)) return false;

            string path = resourcesRoot + "/" + moduleName.Replace('.', '/');
            TextAsset text = Resources.Load<TextAsset>(path);
            if (text == null) return false;
            source = text.text;
            return true;
        }
    }
}
