namespace Directiva.CodeUI
{
    public readonly struct CodeAnalysisRequest
    {
        public readonly DirectivaCode Codebase;
        public readonly string CurrentScriptPath;
        public readonly string WorkingCode;

        public CodeAnalysisRequest(DirectivaCode codebase, string currentScriptPath, string workingCode)
        {
            Codebase = codebase;
            CurrentScriptPath = currentScriptPath ?? string.Empty;
            WorkingCode = workingCode ?? string.Empty;
        }
    }

    public interface ICodeAnalyzer
    {
        CodeAnalysisResult Analyze(CodeAnalysisRequest request);
    }
}
