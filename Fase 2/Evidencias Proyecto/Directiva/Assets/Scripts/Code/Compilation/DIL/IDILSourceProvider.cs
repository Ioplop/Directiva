namespace DSCompilation.DIL
{
    /// <summary>
    /// Provides DIL source text by module name. Module names are rooted at the Directiva scripts
    /// directory and use dot-separated path segments, for example "utils.vectors".
    /// </summary>
    public interface IDILSourceProvider
    {
        bool TryReadModule(string moduleName, out string sourceText);
    }
}
