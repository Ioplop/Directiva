using System;
using System.Linq;
using DSCompilation.DScript.Modules;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Provides the editor's active buffer and .dscript.back working copies to the compiler.
    /// The disk provider first verifies exact casing and rejects symbolic links/junctions.
    /// Neither cached .dil nor obsolete hand-written .dil files are ever read as source.
    /// </summary>
    public sealed class WorkspaceDScriptSourceProvider : IDScriptSourceProvider
    {
        private readonly DirectivaCode codebase;
        private readonly ScriptWorkspace workspace;
        private readonly FileSystemDScriptSourceProvider disk;

        public WorkspaceDScriptSourceProvider(DirectivaCode codebase,
            ScriptWorkspace workspace, string scriptsRoot)
        {
            this.codebase = codebase ?? throw new ArgumentNullException(nameof(codebase));
            this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            disk = new FileSystemDScriptSourceProvider(scriptsRoot);
        }

        public bool TryReadModule(string moduleName, out string source)
        {
            // The filesystem provider validates every segment before we read a working copy.
            if (!disk.TryReadModule(moduleName, out _))
            {
                source = null;
                return false;
            }

            string path = moduleName.Replace('.', '/') + FileSystemScriptStorage.ScriptExtension;
            var script = codebase.Scripts.FirstOrDefault(candidate =>
                string.Equals(candidate.RelativePath, path, StringComparison.Ordinal));
            if (script == null)
            {
                source = null;
                return false;
            }

            source = workspace.GetWorkingContent(script.RelativePath);
            return true;
        }
    }
}
