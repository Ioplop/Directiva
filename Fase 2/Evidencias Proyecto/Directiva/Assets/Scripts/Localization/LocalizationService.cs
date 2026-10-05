using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Localización por componente.
    /// Busca Resources/Localization/CodeEditorUI/{component}/{locale}.txt
    /// </summary>
    public sealed class LocalizationService
    {
        private static readonly Regex LineRegex =
            new Regex(@"^\[(?<key>[^\]]+)\]\s*:\s*(?<value>.*)$", RegexOptions.Compiled);

        private readonly Dictionary<string, Dictionary<string, string>> _cache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public string CurrentLocale { get; private set; }
        public string DefaultLocale { get; private set; }

        public event Action<string> LocaleChanged;
        public event Action<string> LocalizationWarning;

        public LocalizationService(string defaultLocale = "es")
        {
            DefaultLocale = string.IsNullOrWhiteSpace(defaultLocale) ? "es" : defaultLocale.Trim();
            CurrentLocale = DefaultLocale;
        }

        public void SetDefaultLocale(string locale)
        {
            if (!string.IsNullOrWhiteSpace(locale))
                DefaultLocale = locale.Trim();
        }

        public void SetLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                return;

            locale = locale.Trim();
            if (string.Equals(CurrentLocale, locale, StringComparison.OrdinalIgnoreCase))
                return;

            CurrentLocale = locale;
            LocaleChanged?.Invoke(CurrentLocale);
        }

        public string Get(string component, string key, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(component) || string.IsNullOrWhiteSpace(key))
                return "[missing:invalid-key]";

            var value = TryGetRaw(component, CurrentLocale, key);

            if (value == null && !string.Equals(CurrentLocale, DefaultLocale, StringComparison.OrdinalIgnoreCase))
                value = TryGetRaw(component, DefaultLocale, key);

            if (value == null)
            {
                var missing = $"[missing:{component}.{key}]";
                LocalizationWarning?.Invoke(missing);
                return missing;
            }

            return ApplyArguments(component, key, value, args);
        }

        private string TryGetRaw(string component, string locale, string key)
        {
            var table = LoadTable(component, locale);
            return table.TryGetValue(key, out var value) ? value : null;
        }

        private Dictionary<string, string> LoadTable(string component, string locale)
        {
            var cacheKey = component + "::" + locale;
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var resourcePath = $"Localization/CodeEditorUI/{component}/{locale}";
            var asset = Resources.Load<TextAsset>(resourcePath);

            if (asset != null)
            {
                var lines = asset.text.Replace("\r\n", "\n").Split('\n');
                foreach (var rawLine in lines)
                {
                    var line = rawLine.TrimEnd();
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;

                    var match = LineRegex.Match(line);
                    if (!match.Success)
                        continue;

                    var key = match.Groups["key"].Value.Trim();
                    var value = match.Groups["value"].Value;
                    table[key] = value;
                }
            }

            _cache[cacheKey] = table;
            return table;
        }

        private string ApplyArguments(string component, string key, string template, object[] args)
        {
            args ??= Array.Empty<object>();
            var result = template;

            for (var i = 0; i < args.Length; i++)
            {
                var token = "{{" + (i + 1) + "}}";
                result = result.Replace(token, args[i]?.ToString() ?? string.Empty);
            }

            var unresolved = Regex.Match(result, @"\{\{\d+\}\}");
            if (unresolved.Success)
            {
                LocalizationWarning?.Invoke(
                    $"Missing locale argument in {component}.{key}: {unresolved.Value}");
            }

            return result;
        }
    }
}
