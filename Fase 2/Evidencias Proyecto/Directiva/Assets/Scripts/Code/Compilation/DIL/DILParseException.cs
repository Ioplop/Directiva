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

        public DILParseException(string moduleName, int lineNumber, string message)
            : base(FormatMessage(moduleName, lineNumber, message))
        {
            ModuleName = moduleName ?? string.Empty;
            LineNumber = lineNumber;
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
