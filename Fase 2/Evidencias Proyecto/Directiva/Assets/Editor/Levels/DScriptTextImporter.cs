using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Directiva.Editor.Levels
{
    /// <summary>
    /// Preserves the .dscript extension while making its contents available as a
    /// TextAsset in a Unity build (including through Resources.Load<TextAsset>).
    /// </summary>
    [ScriptedImporter(1, "dscript")]
    public sealed class DScriptTextImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var text = new TextAsset(File.ReadAllText(context.assetPath))
            {
                name = Path.GetFileNameWithoutExtension(context.assetPath)
            };
            context.AddObjectToAsset("DScript", text);
            context.SetMainObject(text);
        }
    }
}
