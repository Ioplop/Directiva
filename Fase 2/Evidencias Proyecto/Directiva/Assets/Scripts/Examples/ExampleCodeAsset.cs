using UnityEngine;

namespace Directiva.Examples
{
    /// <summary>
    /// Imported from a .dscript under Resources/ExampleCode/{locale}/.
    /// The relative path is retained so Resources.LoadAll can discover scripts in
    /// arbitrarily nested folders without a manually maintained manifest.
    /// </summary>
    public sealed class ExampleCodeAsset : ScriptableObject
    {
        [SerializeField] private string relativePath;
        [SerializeField, TextArea] private string source;

        public string RelativePath => relativePath;
        public string Source => source;

#if UNITY_EDITOR
        public void InitializeForImport(string path, string content)
        {
            relativePath = path;
            source = content;
        }
#endif
    }
}
