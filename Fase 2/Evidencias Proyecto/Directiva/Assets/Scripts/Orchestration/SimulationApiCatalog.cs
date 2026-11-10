using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using DMath;
using DSimulation;

namespace DOrchestration
{
    /// <summary>Identifies whether an exposed simulation operation reads state or issues an order.</summary>
    public enum SimulationApiKind { Observation, Order }

    /// <summary>
    /// Immutable description of one CLR-backed DScript operation. The first CLR parameter
    /// (receiverId) is implicit in DScript; Arguments contains only the remaining parameters.
    /// </summary>
    public sealed class SimulationApiOperation
    {
        public string Name { get; }
        public SimulationApiKind Kind { get; }
        public SimulationEntityKinds AppliesTo { get; }
        public SimulationObservationMemberKind MemberKind { get; }
        public SimulationObservationResultKind ResultKind { get; }
        public bool AllowsDeadReceiver { get; }
        public bool ManualRegistration { get; }
        public int MinArguments { get; }
        public int MaxArguments { get; }
        public IReadOnlyList<ParameterInfo> Arguments { get; }
        public MethodInfo Method { get; }

        /// <summary>
        /// Describes an observation using its annotated C# method. DScript callers
        /// provide only the arguments after the implicit entity receiver.
        /// </summary>
        internal SimulationApiOperation(MethodInfo method, SimulationObservationAttribute attribute)
        {
            Method = method;
            Name = attribute.Name;
            Kind = SimulationApiKind.Observation;
            AppliesTo = attribute.AppliesTo;
            MemberKind = attribute.MemberKind;
            ResultKind = attribute.ResultKind;
            AllowsDeadReceiver = attribute.AllowsDeadReceiver;
            Arguments = Array.AsReadOnly(method.GetParameters().Skip(1).ToArray());
            MinArguments = Arguments.Count(a => !a.IsOptional);
            MaxArguments = Arguments.Count;
        }

        /// <summary>
        /// Describes an order and the arguments a script must supply. Orders that
        /// need orchestration-specific values keep a manually defined signature.
        /// </summary>
        internal SimulationApiOperation(MethodInfo method, SimulationOrderAttribute attribute)
        {
            Method = method;
            Name = attribute.Name;
            Kind = SimulationApiKind.Order;
            AppliesTo = attribute.AppliesTo;
            ManualRegistration = attribute.ManualRegistration;
            // Building a unit needs a DScript entry function that the simulation must never see.
            // The orchestrator supplies that argument, so it cannot use the C# method's signature directly.
            Arguments = ManualRegistration
                ? Array.AsReadOnly(Array.Empty<ParameterInfo>())
                : Array.AsReadOnly(method.GetParameters().Skip(1).ToArray());
            MinArguments = ManualRegistration ? 1 : Arguments.Count(a => !a.IsOptional);
            MaxArguments = ManualRegistration ? 1 : Arguments.Count;
        }
    }

    /// <summary>
    /// Application-wide, immutable catalog of simulation observations and orders.
    /// Discovers annotated methods once through reflection; special operations such
    /// as build(entry_function) retain explicit manual bindings in SimulationBridge.
    /// No per-match state or VM state is stored here.
    /// </summary>
    public static class SimulationApiCatalog
    {
        private static readonly IReadOnlyList<SimulationApiOperation> operations;
        private static readonly Dictionary<string, IReadOnlyList<SimulationApiOperation>> observations;
        private static readonly Dictionary<string, IReadOnlyList<SimulationApiOperation>> orders;

        /// <summary>All discovered operations in a stable order, including manual entries.</summary>
        public static IReadOnlyList<SimulationApiOperation> Operations => operations;

        static SimulationApiCatalog()
        {
            // The public API is fixed for the entire application. Discover it once so
            // compilation and runtime dispatch agree on the same set of operations.
            var discovered = new List<SimulationApiOperation>();
            Discover<SimulationObserver, SimulationObservationAttribute>(discovered);
            Discover<SimulationController, SimulationOrderAttribute>(discovered);

            // Reflection does not promise a stable order. Sort the operations so
            // overload resolution and future native function IDs remain predictable.
            discovered.Sort((a, b) =>
            {
                int order = string.CompareOrdinal(a.Name, b.Name);
                if (order != 0) return order;
                order = a.Kind.CompareTo(b.Kind);
                if (order != 0) return order;
                return string.CompareOrdinal(Signature(a), Signature(b));
            });

            // Two methods cannot expose the same DScript name and argument signature:
            // callers would have no reliable way to choose between them.
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (SimulationApiOperation operation in discovered)
            {
                if (!unique.Add(operation.Kind + ":" + operation.Name + ":" + Signature(operation)))
                    throw new InvalidOperationException("Duplicate simulation API signature: " + operation.Name);
            }

            // Publish immutable views, keeping this catalog independent of any match.
            operations = discovered.AsReadOnly();
            observations = GroupByName(discovered, SimulationApiKind.Observation);
            orders = GroupByName(discovered, SimulationApiKind.Order);
        }

        /// <summary>Finds every overload of an observation, if its name is registered.</summary>
        public static bool TryGetObservation(string name, out IReadOnlyList<SimulationApiOperation> entries) =>
            observations.TryGetValue(name, out entries);

        /// <summary>Finds every overload of an order, including manually bound orders.</summary>
        public static bool TryGetOrder(string name, out IReadOnlyList<SimulationApiOperation> entries) =>
            orders.TryGetValue(name, out entries);

        /// <summary>
        /// Finds the operations that a simulation provider deliberately exposes to DScript.
        /// Ordinary C# methods without an annotation stay out of the public scripting API.
        /// </summary>
        private static void Discover<TProvider, TAttribute>(List<SimulationApiOperation> discovered)
            where TAttribute : Attribute
        {
            foreach (MethodInfo method in typeof(TProvider).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                TAttribute attribute = method.GetCustomAttribute<TAttribute>();
                if (attribute == null) continue;

                // Reject invalid declarations at startup, before a player script can invoke them.
                ValidateReceiver(method);

                if (attribute is SimulationObservationAttribute observation)
                {
                    var entry = new SimulationApiOperation(method, observation);
                    ValidateObservation(entry);
                    discovered.Add(entry);
                }
                else if (attribute is SimulationOrderAttribute order)
                {
                    var entry = new SimulationApiOperation(method, order);
                    ValidateOrder(entry);
                    discovered.Add(entry);
                }
            }
        }

        /// <summary>
        /// Ensures every exposed operation identifies the entity it acts on or observes.
        /// That entity ID is provided by the bridge, not as an argument in player code.
        /// </summary>
        private static void ValidateReceiver(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (method.IsStatic || parameters.Length == 0 || parameters[0].ParameterType != typeof(int) ||
                parameters[0].IsOut || parameters[0].IsOptional)
                throw new InvalidOperationException(method.Name + " must begin with an int receiverId.");
        }

        /// <summary>
        /// Checks that an observation's declared behavior matches its C# signature,
        /// so DScript can safely interpret its result without knowing simulation internals.
        /// </summary>
        private static void ValidateObservation(SimulationApiOperation entry)
        {
            ValidateName(entry);
            if (entry.AppliesTo == SimulationEntityKinds.None ||
                (entry.MemberKind == SimulationObservationMemberKind.Property && entry.Arguments.Count != 0))
                throw new InvalidOperationException("Invalid observation metadata: " + entry.Name);

            // An entity ID is different from an ordinary integer in DScript. Check the
            // declared result kind so the bridge can preserve that distinction.
            Type result = Nullable.GetUnderlyingType(entry.Method.ReturnType) ?? entry.Method.ReturnType;
            bool validResult = entry.ResultKind switch
            {
                SimulationObservationResultKind.Entity => entry.Method.ReturnType == typeof(int?),
                SimulationObservationResultKind.EntityList => entry.Method.ReturnType == typeof(List<int>),
                SimulationObservationResultKind.MoveTarget => entry.Method.ReturnType == typeof(SimulationMoveTarget?),
                _ => result == typeof(int) || result == typeof(long) || result == typeof(bool) ||
                     result == typeof(Fixed) || result == typeof(FixedVector2) || result.IsEnum
            };
            if (!validResult)
                throw new InvalidOperationException("Unsupported observation result: " + entry.Method.Name);
            ValidateArguments(entry);
        }

        /// <summary>
        /// Checks that an order reports whether it was accepted and that any special
        /// order has the expected shape before the orchestrator attempts to use it.
        /// </summary>
        private static void ValidateOrder(SimulationApiOperation entry)
        {
            ValidateName(entry);
            if (entry.AppliesTo == SimulationEntityKinds.None || entry.Method.ReturnType != typeof(bool?))
                throw new InvalidOperationException("Invalid order metadata: " + entry.Name);
            if (entry.ManualRegistration)
            {
                // build(entry_function) is the current exception: its DScript function
                // argument belongs to orchestration, not to the simulation's controller.
                ParameterInfo[] parameters = entry.Method.GetParameters();
                if (entry.Name != "build" || parameters.Length != 2 ||
                    !parameters[1].IsOut || parameters[1].ParameterType != typeof(UnitBuildRequest).MakeByRefType())
                    throw new InvalidOperationException("Unsupported manual simulation order: " + entry.Name);
                return;
            }
            ValidateArguments(entry);
        }

        /// <summary>
        /// Ensures an exposed name can be written naturally as a DScript identifier.
        /// Invalid names should fail when building the catalog, not in player programs.
        /// </summary>
        private static void ValidateName(SimulationApiOperation entry)
        {
            string name = entry.Name;
            if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_') ||
                name.Skip(1).Any(c => !char.IsLetterOrDigit(c) && c != '_'))
                throw new InvalidOperationException("Invalid simulation API name: " + name);
        }

        /// <summary>
        /// Restricts exposed parameters to values that can cross the scripting boundary
        /// unambiguously, including specially marked entity references.
        /// </summary>
        private static void ValidateArguments(SimulationApiOperation entry)
        {
            bool optionalSeen = false;
            foreach (ParameterInfo argument in entry.Arguments)
            {
                // An entity reference must be marked explicitly: a player passing 12
                // should not accidentally refer to entity #12 instead of the number 12.
                Type type = Nullable.GetUnderlyingType(argument.ParameterType) ?? argument.ParameterType;
                bool isEntity = argument.IsDefined(typeof(SimulationEntityArgumentAttribute), false);
                if (argument.IsOut || argument.ParameterType.IsByRef ||
                    (isEntity && argument.ParameterType != typeof(int)) ||
                    (!isEntity && type != typeof(Fixed) && type != typeof(int) &&
                     type != typeof(long) && type != typeof(bool) && type != typeof(FixedVector2) && !type.IsEnum))
                    throw new InvalidOperationException("Unsupported API parameter: " + entry.Method.Name + "." + argument.Name);
                // Optional parameters must come last so positional calls have one meaning.
                if (argument.IsOptional) optionalSeen = true;
                else if (optionalSeen)
                    throw new InvalidOperationException("Optional arguments must be trailing: " + entry.Method.Name);
            }
        }

        /// <summary>
        /// Describes the arguments visible to DScript, rather than the raw C# signature,
        /// so duplicate public overloads can be detected reliably.
        /// </summary>
        private static string Signature(SimulationApiOperation operation)
        {
            if (operation.ManualRegistration) return "Function";
            return string.Join(",", operation.Arguments.Select(p =>
                (p.IsDefined(typeof(SimulationEntityArgumentAttribute), false) ? "Entity" : p.ParameterType.FullName) +
                (p.IsOptional ? "?" : string.Empty)));
        }

        /// <summary>
        /// Groups operations by the names players call, keeping all overloads together
        /// for the bridge to choose from when a script makes a call.
        /// </summary>
        private static Dictionary<string, IReadOnlyList<SimulationApiOperation>> GroupByName(
            IEnumerable<SimulationApiOperation> entries, SimulationApiKind kind)
        {
            var groups = new Dictionary<string, IReadOnlyList<SimulationApiOperation>>(StringComparer.Ordinal);
            foreach (var group in entries.Where(e => e.Kind == kind).GroupBy(e => e.Name, StringComparer.Ordinal))
                groups.Add(group.Key, Array.AsReadOnly(group.ToArray()));
            return groups;
        }
    }
}
