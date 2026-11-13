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
    /// <summary>Script-facing signature; unknown parameter types are never fabricated.</summary>
    public sealed class DScriptCallSignatureResult
    {
        public string Name { get; }
        public IReadOnlyList<string> Parameters { get; }
        public string ReturnType { get; }
        public int ActiveParameter { get; }
        /// <summary>Real DScript enum constants accepted by the active argument, if known.</summary>
        public string ActiveArgumentChoices { get; }
        public DScriptCallSignatureResult(string name, IReadOnlyList<string> parameters,
            string returnType, int activeParameter, string activeArgumentChoices = null)
        {
            Name = name;
            Parameters = parameters;
            ReturnType = returnType;
            ActiveParameter = activeParameter;
            ActiveArgumentChoices = activeArgumentChoices ?? string.Empty;
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
                // An edit in an unrelated statement must not display an old signature.
                if (DScriptEditorSyntaxContext.IsAbandoned(source, open, caret)) continue;
                // Analyze a syntactically complete prefix first. At this point the
                // original buffer may contain `move(` without its closing parenthesis,
                // which prevents the recovering parser from retaining its function AST.
                string prefix = source.Substring(0, open.Offset);
                DScriptCompletionResult candidates = DScriptCompletionEngine.Complete(
                    module, prefix, prefix.Length, provider);
                var item = candidates.Items.FirstOrDefault(x => x.Name == name && x.IsCallable);
                if (item == null)
                {
                    // The complete buffer may contain declarations below the caret.
                    candidates = DScriptCompletionEngine.Complete(module, source, open.Offset, provider);
                    item = candidates.Items.FirstOrDefault(x => x.Name == name && x.IsCallable);
                }
                if (item == null) continue;
                int active = open.Commas;
                var parameters = ParametersFromDetail(item.Detail);
                string activeArgumentChoices = string.Empty;
                if (item.Kind == "method")
                {
                    // The simulation catalog contains real, annotated CLR types and
                    // overloads. Prefer a signature that can accept the current arg.
                    var operations = SimulationApiCatalog.Operations.Where(o => o.Name == name &&
                        (o.Kind == SimulationApiKind.Order ||
                         o.MemberKind == SimulationObservationMemberKind.Method))
                        .OrderByDescending(o => o.MaxArguments).ToArray();
                    // When a call has overloads, an explicitly written DScript enum
                    // tells us which overload the player is using. For example,
                    // entities(Type.Unit) must show Type, not the Side overload.
                    string firstEnum = FirstArgumentEnum(source, open.Offset, caret);
                    var operation = operations.FirstOrDefault(o =>
                        active < o.MaxArguments && o.Arguments.Count > 0 &&
                        DScriptSimulationEnumDisplay.ModuleName(Nullable.GetUnderlyingType(
                            o.Arguments[0].ParameterType) ?? o.Arguments[0].ParameterType) == firstEnum &&
                        firstEnum != null);
                    operation ??= operations.FirstOrDefault(o => active < o.MaxArguments) ??
                        operations.FirstOrDefault();
                    if (operation != null)
                    {
                        parameters = operation.ManualRegistration
                            ? new[] { "entry_function" }
                            : operation.Arguments.Select(DescribeArgument).ToArray();
                        if (!operation.ManualRegistration && active < operation.Arguments.Count)
                        {
                            Type clrType = operation.Arguments[active].ParameterType;
                            activeArgumentChoices = DScriptSimulationEnumDisplay.Choices(
                                Nullable.GetUnderlyingType(clrType) ?? clrType);
                        }
                    }
                }
                string returnType = item.Type;
                if (string.IsNullOrEmpty(returnType))
                {
                    // User functions expose return annotations in their signature
                    // detail even when the completion symbol has no inferred type.
                    int arrow = item.Detail.IndexOf(" -> ", StringComparison.Ordinal);
                    if (arrow >= 0) returnType = item.Detail.Substring(arrow + 4).Trim();
                }
                return new DScriptCallSignatureResult(item.Name, parameters, returnType,
                    active, activeArgumentChoices);
            }
            return null;
        }

        // Only use an unambiguous, explicitly named enum namespace. We never
        // guess the overload from an untyped variable or arbitrary expression.
        private static string FirstArgumentEnum(string source, int opening, int caret)
        {
            if (opening + 1 >= caret) return null;
            string first = source.Substring(opening + 1, caret - opening - 1).TrimStart();
            foreach (string name in DSExecution.Native.NativeLibrary.SimulationEnumModules)
                if (first.StartsWith(name + ".", StringComparison.Ordinal))
                    return name;
            return null;
        }

        private static string DescribeArgument(ParameterInfo parameter)
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
