using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DSCompilation.DScript.Modules;
using DOrchestration;
using DMath;
using DSimulation;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Editor-only expected type for an unfinished function argument. Unknown or
    /// disagreeing overloads return null; no guesses are made at runtime.
    /// </summary>
    internal static class DScriptExpectedArgument
    {
        internal static string Get(string module, string source, int caret, IDScriptSourceProvider provider)
        {
            if (string.IsNullOrEmpty(source)) return null;
            var openings = DScriptEditorSyntaxContext.OpeningsAt(source, caret, out bool literal);
            if (literal) return null;
            for (int i = openings.Count - 1; i >= 0; --i)
            {
                var open = openings[i];
                if (open.Kind != '(' ||
                    !DScriptEditorSyntaxContext.TryCallName(source, open.Offset, out string callName)) continue;
                int position = open.Commas;
                var possible = new List<string>();
                foreach (var operation in SimulationApiCatalog.Operations.Where(o => o.Name == callName &&
                    (o.Kind == SimulationApiKind.Order ||
                     o.MemberKind == SimulationObservationMemberKind.Method)))
                {
                    if (position >= operation.MaxArguments) continue;
                    if (operation.ManualRegistration)
                        possible.Add("Function");
                    else if (position < operation.Arguments.Count)
                        possible.Add(ParameterType(operation.Arguments[position]));
                }
                if (possible.Count == 0)
                {
                    // Forward-declared user functions are found even when an
                    // unfinished previous call breaks parser recovery.
                    string name = callName;
                    int dot = open.Offset - callName.Length - 1;
                    // Qualified imports can be added later. Never pretend an
                    // unresolved receiver belongs to the current module.
                    if (dot >= 0 && source[dot] == '.') return null;
                    foreach (var function in DScriptCompletionEngine.FunctionHeaders(source))
                        if (function.Name == name && position < function.ParameterTypes.Count)
                            possible.Add(function.ParameterTypes[position]);
                }
                if (possible.Count == 0) return null;
                // null represents an unannotated argument, not a mismatch.
                if (possible.Any(x => x == null)) return null;
                string first = possible[0];
                return possible.All(x => x == first) ? first : null;
            }
            // Outside a call, annotated assignments and annotated function
            // returns can also request a function *reference*.
            int lineStart = source.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
            string before = source.Substring(lineStart, caret - lineStart);
            // Completion asks for the expected type at ReplaceStart (the first
            // character of the identifier), not at the end of the typed prefix.
            // Accept an *empty* right-hand-side prefix as well as a partial name.
            // Without this, `shared cb: Function = uni` looks untyped at `uni`'s
            // start, so the editor incorrectly inserts `unit()` instead of `unit`.
            Match assignment = Regex.Match(before,
                @"^[ \t]*(?:shared[ \t]+)?[A-Za-z_][A-Za-z0-9_]*[ \t]*:[ \t]*(?<type>[A-Za-z_][A-Za-z0-9_]*)[ \t]*=[ \t]*(?:[A-Za-z_][A-Za-z0-9_]*)?$");
            if (assignment.Success)
                return DScriptCompletionEngine.CanonicalType(assignment.Groups["type"].Value);
            // Likewise, a `return uni` completion asks at the start of `uni`.
            // An unannotated return remains unknown, preserving dynamic behavior.
            if (Regex.IsMatch(before, @"^[ \t]+return[ \t]+(?:[A-Za-z_][A-Za-z0-9_]*)?$"))
            {
                var current = DScriptCompletionEngine.FunctionHeaders(source)
                    .Where(f => f.Start < lineStart).OrderByDescending(f => f.Start).FirstOrDefault();
                return current.ReturnType;
            }
            return null;
        }

        internal static string ParameterType(ParameterInfo formal)
        {
            if (formal.IsDefined(typeof(SimulationEntityArgumentAttribute), false))
                return "SimulationObject";
            Type type = Nullable.GetUnderlyingType(formal.ParameterType) ?? formal.ParameterType;
            string enumeration = DScriptSimulationEnumDisplay.ModuleName(type);
            if (enumeration != null) return enumeration;
            if (type == typeof(bool)) return "Bool";
            if (type == typeof(int) || type == typeof(long)) return "Int";
            if (type == typeof(Fixed)) return "Decimal";
            if (type == typeof(FixedVector2)) return "Tuple";
            if (typeof(Delegate).IsAssignableFrom(type)) return "Function";
            return null;
        }
    }
}
