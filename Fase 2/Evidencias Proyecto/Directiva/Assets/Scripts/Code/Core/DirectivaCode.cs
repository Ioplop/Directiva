using System;
using System.Collections.Generic;
using System.Linq;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Índice liviano del codebase guardado.
    /// V1 conoce scripts y rutas. En el futuro aquí vivirán imports,
    /// símbolos, grafos de dependencia y referencias semánticas.
    /// </summary>
    public sealed class DirectivaCode
    {
        public sealed class ScriptReference
        {
            public string RelativePath { get; internal set; }
            public string Name { get; internal set; }

            internal ScriptReference(string relativePath)
            {
                RelativePath = relativePath;
                Name = System.IO.Path.GetFileNameWithoutExtension(relativePath);
            }
        }

        private readonly IScriptStorage _storage;
        private readonly Dictionary<string, ScriptReference> _scripts =
            new Dictionary<string, ScriptReference>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<ScriptReference> Scripts => _scripts.Values;

        public DirectivaCode(IScriptStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Refresh();
        }

        public void Refresh()
        {
            _scripts.Clear();
            ScanFolder(string.Empty);
        }

        public bool TryGet(string relativePath, out ScriptReference script) =>
            _scripts.TryGetValue(Normalize(relativePath), out script);

        private void ScanFolder(string folder)
        {
            foreach (var script in _storage.EnumerateScripts(folder))
                _scripts[Normalize(script)] = new ScriptReference(Normalize(script));

            foreach (var child in _storage.EnumerateDirectories(folder))
                ScanFolder(child);
        }

        private static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/');
    }
}
