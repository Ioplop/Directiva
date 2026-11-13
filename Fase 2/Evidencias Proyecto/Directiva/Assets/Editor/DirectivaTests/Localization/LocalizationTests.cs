using System;
using System.Collections.Generic;
using System.Linq;
using Directiva.CodeUI;
using Directiva.Localization;
using DSCompilation.DIL;
using DSCompilation.DScript;
using DSExecution.DataTypes;
using DSExecution.Errors;
using UnityEngine;

namespace Directiva.EditorTests.LocalizationTests
{
    internal static class LocalizationTests
    {
        private static readonly string[] Components =
        {
            "MainMenu", "Simulation", "Console", "RuntimeErrors", "DILErrors", "DScriptErrors",
            "Common", "Dialogs", "Storage", "Output", "ScriptExplorer", "Tooltips"
        };

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("Localization", "Translations", name, description, run));

            Add("TablesExist", "Every text table is loaded in both languages", t =>
            {
                foreach (string name in Components)
                {
                    foreach (string locale in new[] { "es", "en" })
                    {
                        var asset = Resources.Load<TextAsset>("Localization/CodeEditorUI/" + name + "/" + locale);
                        t.Check(asset != null, "Missing locale file for " + name + "/" + locale);
                    }
                }
            });

            Add("MatchingKeys", "Spanish and English tables contain identical nonempty keys", t =>
            {
                foreach (string name in Components)
                {
                    var spanish = Table(name, "es");
                    var english = Table(name, "en");
                    t.Check(spanish.Count > 0, "Empty Spanish table: " + name);
                    t.Equal(string.Join(",", spanish.Keys.OrderBy(x => x, StringComparer.Ordinal)),
                        string.Join(",", english.Keys.OrderBy(x => x, StringComparer.Ordinal)),
                        "Keys in " + name);
                    foreach (string key in spanish.Keys)
                    {
                        t.Check(spanish[key].Length > 0, "Empty Spanish translation: " + name + "." + key);
                        t.Check(english[key].Length > 0, "Empty English translation: " + name + "." + key);
                    }
                }
            });

            Add("LocaleSwitchAndFallback", "Locale switches without changing settings, with fallback to Spanish", t =>
            {
                var locale = new LocalizationService("es");
                t.Equal("Jugar", locale.Get("MainMenu", "play"), "Spanish menu");
                locale.SetLocale("en");
                t.Equal("Play", locale.Get("MainMenu", "play"), "English menu");
                locale.SetLocale("zz");
                t.Equal("Jugar", locale.Get("MainMenu", "play"), "Missing language fallback");
            });

            Add("FormattedArguments", "Placeholder substitution respects localization", t =>
            {
                var locale = new LocalizationService("es");
                t.Contains("12", locale.Get("MainMenu", "time_remaining", 12), "Spanish countdown");
                locale.SetLocale("en");
                t.Equal("Time remaining: 5 s", locale.Get("MainMenu", "time_remaining", 5), "English countdown");
                t.Equal("Function 'foo' does not exist in module 'main'. The editor only runs functions from the selected file.",
                    locale.Get("Console", "script_function_not_found", "foo", "main"), "English composite message");
            });

            Add("PlayDisabledTooltipsLocalized", "Blocked Play has explanatory Spanish and English tooltips", t =>
            {
                var locale = new LocalizationService("es");
                t.Contains("errores", locale.Get("Simulation", "play_code_errors"),
                    "Spanish compilation-error explanation");
                t.Contains("partida", locale.Get("Simulation", "nexus_select_before_play"),
                    "Spanish missing-program explanation");
                t.Contains("preparación", locale.Get("Simulation", "play_setup_error"),
                    "Spanish setup-error explanation");
                locale.SetLocale("en");
                t.Contains("errors", locale.Get("Simulation", "play_code_errors"),
                    "English compilation-error explanation");
                t.Contains("Nexus", locale.Get("Simulation", "nexus_select_before_play"),
                    "English missing-program explanation");
                t.Contains("setup", locale.Get("Simulation", "play_setup_error"),
                    "English setup-error explanation");
            });

            Add("DiagnosticTemplatesInBothLanguages", "DIL, DScript and VM diagnostics translate in both languages", t =>
            {
                var locale = new LocalizationService("es");
                t.Equal("División por cero.", locale.Get("RuntimeErrors", "DivisionByZero"),
                    "Spanish runtime error");
                t.Contains("desconocida", locale.Get("DILErrors", "unknown_instruction", "BOGUS"),
                    "Spanish DIL error");
                t.Contains("no definido", locale.Get("DScriptErrors", "undefined_name", "missing"),
                    "Spanish DScript error");
                locale.SetLocale("en");
                t.Equal("Division by zero.", locale.Get("RuntimeErrors", "DivisionByZero"),
                    "English runtime error");
                t.Contains("Unknown DIL", locale.Get("DILErrors", "unknown_instruction", "BOGUS"),
                    "English DIL error");
                t.Contains("Undefined name", locale.Get("DScriptErrors", "undefined_name", "missing"),
                    "English DScript error");
            });

            Add("RuntimeErrorFormatting", "Runtime errors have stable IDs and localized presentation", t =>
            {
                RuntimeError err = RuntimeError.InvalidCastValue(DataTypeId.String, DataTypeId.Int, "bad text");
                t.Equal(RuntimeErrorId.InvalidCastValue, err.Id, "runtime ID");
                t.Equal("InvalidCastValue", err.MessageKey, "message key");
                t.Contains("bad text", err.Message, "original technical message");
                string displayed = DirectivaLocalization.Format(err);
                t.Contains("String", displayed, "source type remains visible");
                t.Contains("Int", displayed, "destination type remains visible");
                t.Contains("bad text", displayed, "value details remain visible");
                t.Check(!displayed.Contains("[missing:"), "runtime key missing");
            });

            Add("DILParserStructuredError", "DIL errors preserve location, IDs and source values", t =>
            {
                string source = "# FUNCTION : main\n# PARAMS : 0\n# LOCALS : 0\nBOGUS\nRETURN\n# END_FUNCTION";
                try
                {
                    new DILParser(new SingleModuleProvider(source)).Parse("test");
                    t.Check(false, "parser should reject BOGUS instruction");
                }
                catch (DILParseException ex)
                {
                    t.Equal("unknown_instruction", ex.LocalizationKey, "DIL error key");
                    t.Equal(4, ex.LineNumber, "source line");
                    t.Contains("Unknown DIL", ex.Message, "original diagnostic still available");
                    var visible = DirectivaLocalization.Format(ex);
                    t.Contains("BOGUS", visible, "instruction included in translation");
                    t.Contains("test:4", visible, "module and line preserved");
                    t.Check(!visible.Contains("[missing:"), "DIL translation missing");
                }
            });

            Add("DScriptDiagnostic", "DScript diagnostic code and span are kept while message is translated", t =>
            {
                var diagnostic = new DScriptDiagnostic("main", "DS4016", "Undefined name 'x'.",
                    new SourceSpan(0, 1, 4, 6), "undefined_name", "x");
                t.Equal("DS4016", diagnostic.Code, "diagnostic identifier");
                t.Equal(4, diagnostic.Span.Line, "source line");
                t.Contains("x", DirectivaLocalization.Format(diagnostic), "localized symbol");
                t.Check(!DirectivaLocalization.Format(diagnostic).Contains("[missing:"), "translation present");
            });
        }

        private static Dictionary<string, string> Table(string component, string locale)
        {
            var asset = Resources.Load<TextAsset>("Localization/CodeEditorUI/" + component + "/" + locale);
            if (asset == null) throw new Exception("Missing table " + component + "/" + locale);
            var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in asset.text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("[", StringComparison.Ordinal)) continue;
                int close = trimmed.IndexOf("]:", StringComparison.Ordinal);
                if (close < 0) continue;
                string key = trimmed.Substring(1, close - 1);
                if (items.ContainsKey(key)) throw new Exception("Duplicate locale key " + component + "." + key);
                items.Add(key, trimmed.Substring(close + 2).Trim());
            }
            return items;
        }

        private sealed class SingleModuleProvider : IDILSourceProvider
        {
            private readonly string text;
            internal SingleModuleProvider(string text) { this.text = text; }
            public bool TryReadModule(string name, out string source)
            {
                source = name == "test" ? text : null;
                return source != null;
            }
        }
    }
}
