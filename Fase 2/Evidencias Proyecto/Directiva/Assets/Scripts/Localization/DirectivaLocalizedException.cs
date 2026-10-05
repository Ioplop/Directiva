using System;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Exception that carries a localization key instead of a final user-facing string.
    /// The UI localizes it only when it is displayed.
    /// </summary>
    public sealed class DirectivaLocalizedException : Exception
    {
        public string Component { get; }
        public string Key { get; }
        public object[] Arguments { get; }

        public DirectivaLocalizedException(
            string component,
            string key,
            params object[] arguments)
            : base($"{component}.{key}")
        {
            Component = string.IsNullOrWhiteSpace(component) ? "Common" : component;
            Key = key ?? string.Empty;
            Arguments = arguments ?? Array.Empty<object>();
        }
    }
}
