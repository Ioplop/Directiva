using System;

namespace DSCompilation.DIL
{
    /// <summary>
    /// Describes malformed or unresolved DIL discovered before a CodeContext is created.
    /// </summary>
    public sealed class DILParseException : Exception
    {
        public string ModuleName { get; }
        public int LineNumber { get; }
        public string LocalizationKey { get; }
        public object[] LocalizationArguments { get; }

        // Keep Message unchanged: parser tests and tooling rely on its English diagnostics.
        // Presentation code can format LocalizationKey instead, using the same location.
        public DILParseException(string moduleName, int lineNumber, string message,
            string localizationKey = null, params object[] localizationArguments)
            : base(FormatMessage(moduleName, lineNumber, message))
        {
            ModuleName = moduleName ?? string.Empty;
            LineNumber = lineNumber;
            LocalizationKey = localizationKey;
            LocalizationArguments = localizationArguments ?? Array.Empty<object>();
        }

        private static string FormatMessage(string moduleName, int lineNumber, string message)
        {
            string location = lineNumber > 0
                ? $"{moduleName}:{lineNumber}"
                : moduleName;

            return string.IsNullOrEmpty(location)
                ? message
                : $"{location}: {message}";
        }
    }
}
