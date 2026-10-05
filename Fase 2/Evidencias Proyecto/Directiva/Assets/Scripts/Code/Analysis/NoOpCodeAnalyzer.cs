namespace Directiva.CodeUI
{
    /// <summary>
    /// Analyzer inicial. Siempre considera válido el código.
    /// </summary>
    public sealed class NoOpCodeAnalyzer : ICodeAnalyzer
    {
        public CodeAnalysisResult Analyze(CodeAnalysisRequest request) => CodeAnalysisResult.Empty;
    }
}
