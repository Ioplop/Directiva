using System;
using System.Collections.Generic;
using DSCompilation.DScript;
using DSExecution.VirtualMachine;

namespace Directiva.CodeUI
{
    public enum OutputSeverity
    {
        Message = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>
    /// A position in an editable .dscript source file. Lines and columns are one-based;
    /// StartOffset is optional and represents the original compiler/VM character offset.
    /// </summary>
    public readonly struct OutputSourceLocation
    {
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }
        public int StartOffset { get; }
        public int Length { get; }

        public OutputSourceLocation(string filePath, int line, int column = 1,
            int startOffset = -1, int length = 0)
        {
            FilePath = (filePath ?? string.Empty).Replace('\\', '/');
            Line = line;
            Column = Math.Max(1, column);
            StartOffset = startOffset;
            Length = Math.Max(0, length);
        }

        public static OutputSourceLocation? FromDiagnostic(DScriptDiagnostic diagnostic)
        {
            if (diagnostic == null || string.IsNullOrWhiteSpace(diagnostic.Module))
                return null;

            return FromModuleSpan(diagnostic.Module, diagnostic.Span);
        }

        public static OutputSourceLocation? FromModuleSpan(string module, SourceSpan span)
        {
            if (string.IsNullOrWhiteSpace(module))
                return null;
            // SourceSpan.Line/Column stay accurate even if the editor normalizes CRLF to LF.
            return new OutputSourceLocation(module.Replace('.', '/') + FileSystemScriptStorage.ScriptExtension,
                span.Line, span.Column, -1, span.Length);
        }

        public static OutputSourceLocation? FromDebug(VMDebugState debug)
        {
            if (debug == null || debug.Line < 0 || string.IsNullOrWhiteSpace(debug.File) ||
                !debug.File.EndsWith(FileSystemScriptStorage.ScriptExtension,
                    StringComparison.OrdinalIgnoreCase))
                return null;

            int length = debug.SpanStart >= 0 && debug.SpanEnd >= debug.SpanStart
                ? debug.SpanEnd - debug.SpanStart : 0;
            return new OutputSourceLocation(debug.File, debug.Line + 1,
                startOffset: debug.SpanStart, length: length);
        }
    }

    public readonly struct OutputEntry
    {
        public readonly OutputSeverity Severity;
        public readonly string Text;
        public readonly DateTime Timestamp;
        public readonly OutputSourceLocation? Source;

        public OutputEntry(OutputSeverity severity, string text, OutputSourceLocation? source = null)
        {
            Severity = severity;
            Text = text ?? string.Empty;
            Source = source;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Buffer acotado de Output. Al superar el límite se descarta lo más antiguo.
    /// </summary>
    public sealed class OutputService
    {
        private readonly LinkedList<OutputEntry> _entries = new LinkedList<OutputEntry>();

        public int Capacity { get; }
        public IEnumerable<OutputEntry> Entries => _entries;

        public event Action<OutputEntry> EntryAdded;
        public event Action Cleared;

        public OutputService(int capacity = 1000)
        {
            Capacity = Math.Max(1, capacity);
        }

        public void Write(string text, OutputSourceLocation? source = null) =>
            Add(OutputSeverity.Message, text, source);
        public void WriteWarning(string text, OutputSourceLocation? source = null) =>
            Add(OutputSeverity.Warning, text, source);
        public void WriteError(string text, OutputSourceLocation? source = null) =>
            Add(OutputSeverity.Error, text, source);

        public void Add(OutputSeverity severity, string text, OutputSourceLocation? source = null)
        {
            var entry = new OutputEntry(severity, text, source);

            while (_entries.Count >= Capacity)
                _entries.RemoveFirst();

            _entries.AddLast(entry);
            EntryAdded?.Invoke(entry);
        }

        public void Clear()
        {
            _entries.Clear();
            Cleared?.Invoke();
        }
    }
}
