using System;
using System.IO;
using Directiva.Examples;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Directiva.Editor.Levels
{
    /// <summary>
    /// Level programs remain TextAssets; example scripts carry their relative paths
    /// so Resources.LoadAll can reproduce a nested folder tree in player storage.
    /// </summary>
    [ScriptedImporter(2, "dscript")]
    public sealed class DScriptTextImporter : ScriptedImporter
    {
        private const string ExampleRoot = "Assets/Resources/ExampleCode/";

        public override void OnImportAsset(AssetImportContext context)
        {
            string path = context.assetPath.Replace('\\', '/');
            string contents = File.ReadAllText(context.assetPath);
            if (path.StartsWith(ExampleRoot, StringComparison.Ordinal))
            {
                string afterRoot = path.Substring(ExampleRoot.Length);
                int languageSeparator = afterRoot.IndexOf('/');
                if (languageSeparator > 0 && languageSeparator < afterRoot.Length - 1)
                {
                    string relative = afterRoot.Substring(languageSeparator + 1);
                    var example = ScriptableObject.CreateInstance<ExampleCodeAsset>();
                    example.name = Path.GetFileNameWithoutExtension(path);
                    example.InitializeForImport(relative, contents);
                    context.AddObjectToAsset("ExampleDScript", example);
                    context.SetMainObject(example);
                    return;
                }
            }

            // Preserve Resources/Levels scripts' historical TextAsset contract.
            var text = new TextAsset(contents) { name = Path.GetFileNameWithoutExtension(path) };
            context.AddObjectToAsset("DScript", text);
            context.SetMainObject(text);
        }
    }
}
