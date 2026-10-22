using System;
using System.Collections.Generic;

namespace DSCompilation.DScript.Modules
{
    /// <summary>Reads a DScript module by case-sensitive, dot-separated path; does not execute code.</summary>
    public interface IDScriptSourceProvider
    {
        bool TryReadModule(string moduleName, out string source);
    }

    /// <summary>Convenient in-memory provider for tests, tools and the future code editor.</summary>
    public sealed class InMemoryDScriptSourceProvider : IDScriptSourceProvider
    {
        private readonly IReadOnlyDictionary<string, string> sources;
        public InMemoryDScriptSourceProvider(IReadOnlyDictionary<string, string> sources)
        { this.sources = sources ?? throw new ArgumentNullException(nameof(sources)); }
        public bool TryReadModule(string name, out string source)
        { return sources.TryGetValue(name, out source); }
    }
}
