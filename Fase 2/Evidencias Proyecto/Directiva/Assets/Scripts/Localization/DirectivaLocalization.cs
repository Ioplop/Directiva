using System;
using Directiva.CodeUI;
using Directiva.Settings;
using DSExecution.Errors;
using DSCompilation.DIL;
using DSCompilation.DScript;
using UnityEngine;

namespace Directiva.Localization
{
    /// <summary>
    /// One language for menus, editor and user-visible diagnostics. SettingsManager owns
    /// persistence; LocalizationService owns the text tables and fallback to Spanish.
    /// VM/compiler code keeps structured errors and does not depend on Unity UI.
    /// </summary>
    public static class DirectivaLocalization
    {
        private static LocalizationService service;
        private static bool initialized;

        public static event Action<string> LanguageChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (initialized) SettingsManager.SettingsChanged -= OnSettingsChanged;
            initialized = false;
            service = null;
            LanguageChanged = null;
        }

        private static void EnsureInitialized()
        {
            if (initialized) return;
            SettingsManager.Initialize();
            service = new LocalizationService("es");
            service.SetLocale(SettingsManager.Current.general.language);
            SettingsManager.SettingsChanged += OnSettingsChanged;
            initialized = true;
        }

        private static void OnSettingsChanged(GameSettings settings)
        {
            string previous = service.CurrentLocale;
            service.SetLocale(settings.general.language);
            if (!string.Equals(previous, service.CurrentLocale, StringComparison.OrdinalIgnoreCase))
                LanguageChanged?.Invoke(service.CurrentLocale);
        }

        public static string CurrentLocale
        {
            get { EnsureInitialized(); return service.CurrentLocale; }
        }

        /// <summary>Writes the preference through the same persistence mechanism as Options.</summary>
        public static void SetLocale(string locale) => SettingsManager.SetLanguage(locale);

        /// <summary>Returns a localized template; {{1}}, {{2}}... are substitution slots.</summary>
        public static string Get(string component, string key, params object[] arguments)
        {
            EnsureInitialized();
            return service.Get(component, key, arguments);
        }

        /// <summary>Formats DIL syntax errors without changing their original Message.</summary>
        public static string Format(DILParseException error)
        {
            if (error == null) return string.Empty;
            if (string.IsNullOrEmpty(error.LocalizationKey)) return error.Message;
            string details = Get("DILErrors", error.LocalizationKey, error.LocalizationArguments);
            string location = error.LineNumber > 0
                ? error.ModuleName + ":" + error.LineNumber
                : error.ModuleName;
            return string.IsNullOrEmpty(location) ? details : location + ": " + details;
        }

        /// <summary>Source positions and DSxxxx identifiers are never localized.</summary>
        public static string Format(DScriptDiagnostic diagnostic)
        {
            if (diagnostic == null) return string.Empty;
            if (string.IsNullOrEmpty(diagnostic.LocalizationKey)) return diagnostic.Message;
            if (diagnostic.LocalizationKey == "generated_dil_invalid" &&
                diagnostic.LocalizationArguments.Length > 0 &&
                diagnostic.LocalizationArguments[0] is DILParseException error)
                return Get("DScriptErrors", diagnostic.LocalizationKey, Format(error));
            return Get("DScriptErrors", diagnostic.LocalizationKey,
                diagnostic.LocalizationArguments);
        }

        /// <summary>Preserves original runtime diagnostics for tests, localizes only for display.</summary>
        public static string Format(RuntimeError error)
        {
            if (error == null) return string.Empty;
            if (string.IsNullOrEmpty(error.MessageKey)) return error.Message;
            object[] args = error.MessageArguments;
            if (error.Id == RuntimeErrorId.InvalidMemberArgumentCount && args.Length == 5)
            {
                int min = (int)args[2];
                int max = (int)args[3];
                string expected = max < 0
                    ? (min == 0 ? Get("RuntimeErrors", "expected_any")
                        : Get("RuntimeErrors", "expected_minimum", min))
                    : min == max ? Get("RuntimeErrors", "expected_exact", min)
                        : Get("RuntimeErrors", "expected_range", min, max);
                return Get("RuntimeErrors", error.MessageKey, args[0], args[1], expected, args[4]);
            }
            if (error.Id == RuntimeErrorId.UnhashableType && args.Length == 2)
            {
                string path = args[1] as string;
                return Get("RuntimeErrors", error.MessageKey, args[0],
                    string.IsNullOrEmpty(path) ? "" : Get("RuntimeErrors", "unhashable_path", path));
            }
            return Get("RuntimeErrors", error.MessageKey, args);
        }
    }
}
