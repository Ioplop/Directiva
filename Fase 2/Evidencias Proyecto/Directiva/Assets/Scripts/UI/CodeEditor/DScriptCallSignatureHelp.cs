using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DSCompilation.DScript.Modules;
using DOrchestration;
using DMath;
using DSimulation;

namespace Directiva.CodeUI
{
    /// <summary>One documented calling form; alternatives are never discarded.</summary>
    public sealed class DScriptCallSignatureOption
    {
        public IReadOnlyList<string> Parameters { get; }
        public string ReturnType { get; }
        public string ActiveArgumentChoices { get; }

        public DScriptCallSignatureOption(IReadOnlyList<string> parameters, string returnType,
            string activeArgumentChoices = null)
        {
            Parameters = parameters ?? Array.Empty<string>();
            ReturnType = returnType ?? string.Empty;
            ActiveArgumentChoices = activeArgumentChoices ?? string.Empty;
        }
    }

    /// <summary>
    /// Parameter help for an open call. The first overload is the best match for
    /// the arguments already typed; other valid declarations remain navigable.
    /// </summary>
    public sealed class DScriptCallSignatureResult
    {
        public string Name { get; }
        public int OpeningOffset { get; }
        public IReadOnlyList<DScriptCallSignatureOption> Overloads { get; }
        public int ActiveParameter { get; }
        public int PreferredOverloadIndex => 0;
        // Compatibility with the single-signature API used by existing tests.
        public IReadOnlyList<string> Parameters => Overloads[PreferredOverloadIndex].Parameters;
        public string ReturnType => Overloads[PreferredOverloadIndex].ReturnType;
        public string ActiveArgumentChoices => Overloads[PreferredOverloadIndex].ActiveArgumentChoices;

        public DScriptCallSignatureResult(string name, int openingOffset,
            IReadOnlyList<DScriptCallSignatureOption> overloads, int activeParameter)
        {
            Name = name;
            OpeningOffset = openingOffset;
            Overloads = overloads ?? Array.Empty<DScriptCallSignatureOption>();
            ActiveParameter = activeParameter;
        }
    }

    /// <summary>
    /// IDE-only parameter assistance. Reuses completion's scope/type resolution
    /// rather than presenting a signature just because a matching global name exists.
    /// </summary>
    public static class DScriptCallSignatureHelp
    {
        public static DScriptCallSignatureResult Get(string module, string source, int caret,
            IDScriptSourceProvider provider)
        {
            if (string.IsNullOrEmpty(module) || source == null || provider == null) return null;
            var stack = DScriptEditorSyntaxContext.OpeningsAt(source, caret, out bool insideLiteral);
            if (insideLiteral) return null;
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                var open = stack[i];
                if (open.Kind != '(' || !DScriptEditorSyntaxContext.TryCallName(
                    source, open.Offset, out string name)) continue;
                if (DScriptEditorSyntaxContext.IsAbandoned(source, open, caret)) continue;

                // The incomplete call itself may not survive parser recovery. Analyze
                // the complete prefix before the opening, then try the full document
                // for declarations written below the caret.
                string prefix = source.Substring(0, open.Offset);
                DScriptCompletionResult candidates = DScriptCompletionEngine.Complete(
                    module, prefix, prefix.Length, provider);
                var item = candidates.Items.FirstOrDefault(x => x.Name == name && x.IsCallable);
                if (item == null)
                {
                    candidates = DScriptCompletionEngine.Complete(module, source, open.Offset, provider);
                    item = candidates.Items.FirstOrDefault(x => x.Name == name && x.IsCallable);
                }
                if (item == null) continue;

                int active = open.Commas;
                string returnType = item.Type;
                if (string.IsNullOrEmpty(returnType))
                {
                    int arrow = item.Detail.IndexOf(" -> ", StringComparison.Ordinal);
                    if (arrow >= 0) returnType = item.Detail.Substring(arrow + 4).Trim();
                }

                IReadOnlyList<DScriptCallSignatureOption> options = Array.Empty<DScriptCallSignatureOption>();
                if (item.Kind == "method")
                {
                    var operations = SimulationApiCatalog.Operations.Where(o => o.Name == name &&
                        (o.Kind == SimulationApiKind.Order ||
                         o.MemberKind == SimulationObservationMemberKind.Method)).ToArray();
                    if (operations.Length > 0)
                    {
                        // A comma inside a nested call or a string is not an argument
                        // separator. The lexer here only splits at top-level commas.
                        var arguments = ArgumentTexts(source, open.Offset, caret);
                        var knownTypes = DScriptCompletionEngine.InferCallArgumentTypes(module,
                            source, open.Offset, arguments, provider);
                        options = operations
                            .Select(o => new
                            {
                                Operation = o,
                                Score = Rank(o, name, active, arguments, knownTypes),
                                Label = string.Join(",", o.Arguments.Select(a => a.ParameterType.FullName))
                            })
                            .OrderByDescending(x => x.Score)
                            .ThenBy(x => x.Label, StringComparer.Ordinal)
                            .Select(x =>
                            {
                                var operation = x.Operation;
                                var parameters = operation.ManualRegistration
                                    ? new[] { "entry_function" }
                                    : operation.Arguments.Select(p => DescribeArgument(p, name)).ToArray();
                                string choices = string.Empty;
                                if (!operation.ManualRegistration && active < operation.Arguments.Count)
                                {
                                    Type clrType = operation.Arguments[active].ParameterType;
                                    choices = DScriptSimulationEnumDisplay.Choices(
                                        Nullable.GetUnderlyingType(clrType) ?? clrType);
                                }
                                return new DScriptCallSignatureOption(parameters, returnType, choices);
                            }).ToArray();
                    }
                }
                if (options.Count == 0)
                    options = new[] { new DScriptCallSignatureOption(
                        ParametersFromDetail(item.Detail), returnType) };
                return new DScriptCallSignatureResult(item.Name, open.Offset, options, active);
            }
            return null;
        }

        // Favors a compatible overload without hiding alternatives. An unknown type
        // contributes no score, so dynamic DScript values never rule out a candidate.
        // With no argument typed, move(entity) is preferable to move(x, y).
        private static int Rank(SimulationApiOperation operation, string name, int active,
            IReadOnlyList<string> arguments, IReadOnlyList<string> knownTypes)
        {
            int score = 0;
            if (name == "move" && operation.Arguments.Count == 1 &&
                operation.Arguments[0].IsDefined(typeof(SimulationEntityArgumentAttribute), false))
                score += 10;
            if (active >= operation.MaxArguments) score -= 90;
            else score += 6;
            // Other operations retain the existing longer-signature preference
            // unless an already-written argument identifies a better overload.
            if (name != "move") score += operation.MaxArguments;
            for (int i = 0; i < arguments.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(arguments[i])) continue;
                if (i >= operation.MaxArguments) { score -= 60; continue; }
                string known = knownTypes[i];
                if (string.IsNullOrEmpty(known)) continue;
                if (operation.ManualRegistration) continue;
                var formal = operation.Arguments[i];
                if (MatchesType(known, formal)) score += 35;
                else score -= 100;
            }
            return score;
        }

        private static bool MatchesType(string actual, ParameterInfo formal)
        {
            if (formal.IsDefined(typeof(SimulationEntityArgumentAttribute), false))
                return actual == "SimulationObject";
            Type type = Nullable.GetUnderlyingType(formal.ParameterType) ?? formal.ParameterType;
            string enumName = DScriptSimulationEnumDisplay.ModuleName(type);
            if (enumName != null) return actual == enumName;
            if (type == typeof(Fixed)) return actual == "Int" || actual == "Decimal";
            if (type == typeof(FixedVector2)) return actual == "Tuple";
            if (type == typeof(bool)) return actual == "Bool";
            if (type == typeof(int) || type == typeof(long)) return actual == "Int";
            return true; // Unknown CLR type: do not claim a mismatch.
        }

        private static IReadOnlyList<string> ArgumentTexts(string source, int openOffset, int caret)
        {
            var segments = new List<string>();
            int start = openOffset + 1;
            int depth = 0;
            char quote = '\0';
            bool escaped = false, comment = false;
            for (int i = start; i < caret; i++)
            {
                char c = source[i];
                if (c == '\n') { comment = false; quote = '\0'; escaped = false; continue; }
                if (comment) continue;
                if (quote != '\0')
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '#') { comment = true; continue; }
                if (c == '\'' || c == '"') { quote = c; continue; }
                if (c == '(' || c == '[' || c == '{') { depth++; continue; }
                if (c == ')' || c == ']' || c == '}') { depth = Math.Max(0, depth - 1); continue; }
                if (c == ',' && depth == 0)
                {
                    segments.Add(source.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            segments.Add(source.Substring(start, caret - start).Trim());
            return segments;
        }

        /// <summary>Wrap-around navigation shared by popup controls and regression tests.</summary>
        public static int CycleOverload(int index, int count, int direction)
        {
            if (count <= 0) return 0;
            return ((index + (direction < 0 ? count - 1 : 1)) % count + count) % count;
        }

        private static string DescribeArgument(ParameterInfo parameter, string operationName)
        {
            string type;
            if (parameter.IsDefined(typeof(SimulationEntityArgumentAttribute), false))
                type = "SimulationObject";
            else
            {
                Type t = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                // Reflection supplies CLR types, but the help must describe the
                // script-facing value. In particular Type.Unit is NOT a DScript Int.
                type = DScriptSimulationEnumDisplay.ModuleName(t);
                if (type == null)
                {
                    if (t == typeof(Fixed)) type = "Decimal";
                    else if (t == typeof(FixedVector2)) type = "Tuple";
                    else if (t == typeof(bool)) type = "Bool";
                    else if (t == typeof(int) || t == typeof(long)) type = "Int";
                }
            }
            string label = parameter.Name ?? "argument";
            // CLR IDs are not script integers. Use the player's terminology for
            // move(entity), which accepts a SimulationObject reference.
            if (operationName == "move" &&
                parameter.IsDefined(typeof(SimulationEntityArgumentAttribute), false))
                label = "entity";
            return label + (type == null ? "" : ": " + type) + (parameter.IsOptional ? "?" : "");
        }

        private static IReadOnlyList<string> ParametersFromDetail(string detail)
        {
            if (string.IsNullOrEmpty(detail) || detail[0] != '(') return Array.Empty<string>();
            int close = detail.IndexOf(')');
            if (close < 0) return Array.Empty<string>();
            string content = detail.Substring(1, close - 1).Trim();
            if (content.Length == 0 || content == "0 arguments")
                return Array.Empty<string>();
            return content.Split(',').Select(s => s.Trim()).ToArray();
        }
    }
}
