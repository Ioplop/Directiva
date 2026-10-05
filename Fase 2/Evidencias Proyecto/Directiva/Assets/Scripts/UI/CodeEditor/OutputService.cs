using System;
using System.Collections.Generic;

namespace Directiva.CodeUI
{
    public enum OutputSeverity
    {
        Message = 0,
        Warning = 1,
        Error = 2
    }

    public readonly struct OutputEntry
    {
        public readonly OutputSeverity Severity;
        public readonly string Text;
        public readonly DateTime Timestamp;

        public OutputEntry(OutputSeverity severity, string text)
        {
            Severity = severity;
            Text = text ?? string.Empty;
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

        public void Write(string text) => Add(OutputSeverity.Message, text);
        public void WriteWarning(string text) => Add(OutputSeverity.Warning, text);
        public void WriteError(string text) => Add(OutputSeverity.Error, text);

        public void Add(OutputSeverity severity, string text)
        {
            var entry = new OutputEntry(severity, text);

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
