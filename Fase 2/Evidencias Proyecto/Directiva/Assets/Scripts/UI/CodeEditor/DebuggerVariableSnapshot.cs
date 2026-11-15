using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DMath;
using DOrchestration;
using DSimulation;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Semantics;
using DSExecution.DataTypes;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.CodeUI
{
    /// <summary>One read-only value, formatted for display without invoking DScript.</summary>
    public readonly struct DebuggerVariableEntry
    {
        public string Name { get; }
        public string Value { get; }
        public DebuggerVariableEntry(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// Immutable debugger data captured at a VM suspension point. It never advances
    /// a VM, allocates VM heap objects, evaluates an expression or issues a world order.
    /// </summary>
    public sealed class DebuggerVariableSnapshot
    {
        public IReadOnlyList<DebuggerVariableEntry> Locals { get; }
        public IReadOnlyList<DebuggerVariableEntry> Globals { get; }
        public IReadOnlyList<DebuggerVariableEntry> Shared { get; }
        public IReadOnlyList<DebuggerVariableEntry> Self { get; }

        private DebuggerVariableSnapshot(List<DebuggerVariableEntry> locals,
            List<DebuggerVariableEntry> globals, List<DebuggerVariableEntry> shared,
            List<DebuggerVariableEntry> self)
        {
            Locals = locals.AsReadOnly();
            Globals = globals.AsReadOnly();
            Shared = shared.AsReadOnly();
            Self = self.AsReadOnly();
        }

        public static DebuggerVariableSnapshot Capture(DirectivaVM vm,
            DScriptCompiledProgram program, SimulationObserver observer = null,
            int entityId = 0)
        {
            var locals = new List<DebuggerVariableEntry>();
            var globals = new List<DebuggerVariableEntry>();
            var shared = new List<DebuggerVariableEntry>();
            var self = new List<DebuggerVariableEntry>();
            if (vm == null || program == null || !ReferenceEquals(vm.Code, program.Code))
                return new DebuggerVariableSnapshot(locals, globals, shared, self);

            // The current frame's slot layout comes from the exact compiled program,
            // not a re-analysis of an unsaved or subsequently edited source file.
            if (vm.CurrentFunction != null && vm.CurrentLocals != null &&
                program.FunctionLocalSlots.TryGetValue(vm.CurrentFunction.Name, out var slots))
            {
                foreach (DScriptSymbol symbol in slots)
                {
                    if (symbol.Slot >= 0 && symbol.Slot < vm.CurrentLocals.Count)
                    {
                        DataValue value = vm.CurrentLocals.Get(symbol.Slot);
                        if (value.dataType != DataTypeId.Uninitialized)
                            locals.Add(new DebuggerVariableEntry(symbol.Name,
                                DebugValueText.Format(value, vm.Memory, vm.Code)));
                    }
                }
            }

            AddSlots(program.VmGlobalSlots, vm.VmGlobalCount, vm.GetVmGlobal,
                vm, globals);
            AddSlots(program.SharedSlots, vm.Memory.GlobalCount, vm.Memory.GetGlobal,
                vm, shared);

            if (observer != null && entityId > 0)
                AddSelfProperties(observer, entityId, self);

            return new DebuggerVariableSnapshot(locals, globals, shared, self);
        }

        private static void AddSlots(IReadOnlyList<DScriptSymbol> symbols, int count,
            Func<int, DataValue> read, DirectivaVM vm, List<DebuggerVariableEntry> target)
        {
            // Different modules can declare same-named VM globals. Display the
            // module qualifier only when needed to disambiguate them.
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (DScriptSymbol symbol in symbols)
                if (!names.Add(symbol.Name)) duplicates.Add(symbol.Name);
            foreach (DScriptSymbol symbol in symbols.OrderBy(s => s.Slot))
            {
                if (symbol.Slot < 0 || symbol.Slot >= count) continue;
                DataValue value = read(symbol.Slot);
                string name = duplicates.Contains(symbol.Name)
                    ? symbol.QualifiedName : symbol.Name;
                target.Add(new DebuggerVariableEntry(name,
                    DebugValueText.Format(value, vm.Memory, vm.Code)));
            }
        }

        private static void AddSelfProperties(SimulationObserver observer, int entityId,
            List<DebuggerVariableEntry> target)
        {
            SimulationEntityType? entityType = observer.GetEntityType(entityId);
            SimulationEntityKinds kind = entityType switch
            {
                SimulationEntityType.Unit => SimulationEntityKinds.Unit,
                SimulationEntityType.Nexus => SimulationEntityKinds.Nexus,
                SimulationEntityType.Resource => SimulationEntityKinds.Resource,
                _ => SimulationEntityKinds.None
            };
            if (kind == SimulationEntityKinds.None) return;

            // Use the very same declared properties as DScript's self API.
            // Observation getters are read-only, unlike simulation orders.
            foreach (SimulationApiOperation operation in SimulationApiCatalog.Operations)
            {
                if (operation.Kind != SimulationApiKind.Observation ||
                    operation.MemberKind != SimulationObservationMemberKind.Property ||
                    operation.MaxArguments != 0 ||
                    (operation.AppliesTo & kind) == 0)
                    continue;
                try
                {
                    object value = operation.Method.Invoke(observer, new object[] { entityId });
                    target.Add(new DebuggerVariableEntry("self." + operation.Name,
                        DebugValueText.FormatObservation(value,
                            operation.ResultKind)));
                }
                catch (System.Reflection.TargetInvocationException)
                {
                    // A failed observation is not a VM error and cannot alter the match.
                    target.Add(new DebuggerVariableEntry("self." + operation.Name, "?"));
                }
            }
        }
    }

    /// <summary>Bounded, cycle-safe display formatting; no DScript conversion dispatch.</summary>
    internal static class DebugValueText
    {
        private const int MaxItems = 6;
        private const int MaxDepth = 2;
        private const int MaxString = 120;

        internal static string Format(DataValue value, VMMemoryContext memory, CodeContext code)
            => Format(value, memory, code, 0, new HashSet<uint>());

        private static string Format(DataValue value, VMMemoryContext memory,
            CodeContext code, int depth, HashSet<uint> visiting)
        {
            switch (value.dataType)
            {
                case DataTypeId.Uninitialized: return "—";
                case DataTypeId.None: return "None";
                case DataTypeId.Bool: return value.value == 0 ? "False" : "True";
                case DataTypeId.Int: return unchecked((long)value.value).ToString(CultureInfo.InvariantCulture);
                case DataTypeId.Decimal:
                    // DScript decimals use a different scale from simulation's Fixed.
                    return (unchecked((long)value.value) / (decimal)DTDecimal.Scale)
                        .ToString(CultureInfo.InvariantCulture);
                case DataTypeId.Function:
                    return value.TryGetFunctionId(out int functionId) &&
                        code.TryGetFunction(functionId, out FunctionContext function)
                        ? "<function " + function.Name + ">" : "<function>";
                case DataTypeId.SimulationObject:
                    return value.TryGetSimulationObjectId(out int id)
                        ? "<entity #" + id.ToString(CultureInfo.InvariantCulture) + ">" : "<entity>";
                case DataTypeId.SimulationEnum:
                    if (!value.TryGetSimulationEnum(out SimulationEnumKind kind, out int enumValue))
                        return "<enum>";
                    Type enumType = kind switch
                    {
                        SimulationEnumKind.Side => typeof(SimulationSideRelation),
                        SimulationEnumKind.Type => typeof(SimulationEntityType),
                        SimulationEnumKind.MoveMode => typeof(SimulationMoveMode),
                        SimulationEnumKind.ActionMode => typeof(SimulationActionMode),
                        _ => null
                    };
                    string enumName = enumType != null && Enum.IsDefined(enumType, enumValue)
                        ? Enum.GetName(enumType, enumValue)
                        : enumValue.ToString(CultureInfo.InvariantCulture);
                    return kind + "." + enumName;
            }
            if (memory == null || !value.TryGetHeapReference(out uint reference) ||
                !memory.TryGetHeap(reference, out HeapObject obj))
                return "<" + value.dataType + ">";
            if (obj is StringHeapObject str)
                return "\"" + Escape(str.Value) + "\"";
            if (depth >= MaxDepth || !visiting.Add(reference))
                return "<" + value.dataType + " ...>";
            try
            {
                if (obj is ListHeapObject list)
                    return Items("[", "]", list.Count, i =>
                        Format(list.GetElement(i), memory, code, depth + 1, visiting));
                if (obj is TupleHeapObject tuple)
                    return Items("(", ")", tuple.Count, i =>
                        Format(tuple.GetElement(i), memory, code, depth + 1, visiting),
                        tuple.Count == 1);
                if (obj is SetHeapObject set)
                    return Items("{", "}", set.Count, i =>
                        Format(set.GetElement(i), memory, code, depth + 1, visiting));
                if (obj is DictHeapObject dict)
                    return Items("{", "}", dict.Count, i =>
                        Format(dict.GetKey(i), memory, code, depth + 1, visiting) + ": " +
                        Format(dict.GetValue(i), memory, code, depth + 1, visiting));
                if (obj is RangeHeapObject range)
                    return "range(" + range.Start + ", " + range.Stop + ", " + range.Step + ")";
                return "<" + value.dataType + ">";
            }
            finally { visiting.Remove(reference); }
        }

        private static string Items(string start, string end, int count,
            Func<int, string> item, bool singleTuple = false)
        {
            var parts = new List<string>();
            for (int i = 0; i < Math.Min(count, MaxItems); i++) parts.Add(item(i));
            string content = string.Join(", ", parts);
            if (singleTuple) content += ",";
            if (count > MaxItems) content += ", ...";
            return start + content + end;
        }

        internal static string FormatObservation(object value, SimulationObservationResultKind kind)
        {
            if (value == null) return "None";
            if (kind == SimulationObservationResultKind.Entity && value is int id)
                return "<entity #" + id.ToString(CultureInfo.InvariantCulture) + ">";
            if (value is SimulationMoveTarget target)
                return target.Kind == SimulationMoveTargetKind.Entity
                    ? "<entity #" + target.EntityId.ToString(CultureInfo.InvariantCulture) + ">"
                    : target.Position.ToString();
            if (value is bool b) return b ? "True" : "False";
            if (value is Fixed fixedValue) return fixedValue.ToString();
            if (value is FixedVector2 vector) return vector.ToString();
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "None";
        }

        private static string Escape(string value)
        {
            string escaped = (value ?? string.Empty).Replace("\\", "\\\\")
                .Replace("\"", "\\\"").Replace("\r", "\\r")
                .Replace("\n", "\\n").Replace("\t", "\\t");
            return escaped.Length > MaxString
                ? escaped.Substring(0, MaxString) + "..." : escaped;
        }
    }
}
