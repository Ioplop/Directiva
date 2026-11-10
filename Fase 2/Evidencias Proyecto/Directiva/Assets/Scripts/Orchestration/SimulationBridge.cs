using System;
using System.Collections.Generic;
using System.Reflection;
using DMath;
using DSimulation;

namespace DOrchestration
{
    /// <summary>
    /// Identity-only reference to a simulation entity. Scripts can keep and pass these
    /// references without gaining direct access to the entity's mutable C# state.
    /// </summary>
    internal readonly struct SimulationEntityReference : IEquatable<SimulationEntityReference>
    {
        public int Id { get; }
        public SimulationEntityReference(int id) { Id = id; }
        public bool Equals(SimulationEntityReference other) => Id == other.Id;
        public override bool Equals(object obj) => obj is SimulationEntityReference other && Equals(other);
        public override int GetHashCode() => Id;
        public override string ToString() => "<entity #" + Id + ">";
    }

    internal enum SimulationCallStatus { Success, Warning, Error }

    /// <summary>
    /// Separates normal results, non-fatal misuse and forbidden operations. A warning
    /// can return None while allowing the script to continue; an error must stop that call.
    /// </summary>
    internal readonly struct SimulationCallResult
    {
        public SimulationCallStatus Status { get; }
        public object Value { get; }
        public string Message { get; }

        /// <summary>
        /// Keeps the result value and any diagnostic together so the DScript integration
        /// can report problems without mistaking them for normal return values.
        /// </summary>
        private SimulationCallResult(SimulationCallStatus status, object value, string message)
        {
            Status = status;
            Value = value;
            Message = message;
        }

        public static SimulationCallResult Success(object value) =>
            new SimulationCallResult(SimulationCallStatus.Success, value, null);
        public static SimulationCallResult Warning(string message) =>
            new SimulationCallResult(SimulationCallStatus.Warning, null, message);
        public static SimulationCallResult Error(string message) =>
            new SimulationCallResult(SimulationCallStatus.Error, null, message);
    }

    /// <summary>
    /// Connects scripting requests to one simulation while preserving who is allowed
    /// to control each entity. It selects the right operation, applies its arguments and
    /// turns entity IDs into safe references. VM scheduling and DataValue conversion
    /// remain outside this class.
    /// </summary>
    internal sealed class SimulationBridge
    {
        private readonly SimulationObserver observer;
        private readonly SimulationController controller;

        /// <summary>Creates a bridge bound to the APIs of one simulation.</summary>
        internal SimulationBridge(Simulation simulation)
        {
            observer = simulation.Observer;
            controller = simulation.Controller;
        }

        /// <summary>
        /// Invokes an observation from the specified entity's perspective. Observations
        /// are valid on any living entity, regardless of which VM requests them.
        /// </summary>
        internal SimulationCallResult Observe(int receiverId, string name, params object[] arguments)
        {
            // The catalog defines which observations exist; this bridge supplies the
            // particular entity whose point of view the script wants to inspect.
            if (!SimulationApiCatalog.TryGetObservation(name, out var operations))
                return SimulationCallResult.Error("Unknown observation: " + name);
            return Invoke(operations, observer, receiverId, arguments ?? Array.Empty<object>());
        }

        /// <summary>
        /// Issues an order only when the receiver is the entity controlled by the VM.
        /// Neither another allied entity nor an enemy can be ordered remotely.
        /// </summary>
        internal SimulationCallResult Order(int controlledEntityId, int receiverId, string name, params object[] arguments)
        {
            // Knowing about another unit (even an ally) never gives us control of it.
            if (receiverId != controlledEntityId)
                return SimulationCallResult.Error("A script cannot issue orders to an entity other than itself.");
            if (!SimulationApiCatalog.TryGetOrder(name, out var operations))
                return SimulationCallResult.Error("Unknown order: " + name);
            return Invoke(operations, controller, receiverId, arguments ?? Array.Empty<object>());
        }

        /// <summary>
        /// Handles the exceptional build(entry_function) order. The orchestrator supplies
        /// the function value separately and saves it using request.requestId on success.
        /// No DScript function reference is passed into DSimulation.
        /// </summary>
        internal SimulationCallResult RequestBuild(int controlledEntityId, int receiverId,
            out UnitBuildRequest request)
        {
            request = default;

            // build() follows the same ownership rule as every other order, even though
            // its DScript function argument needs special handling outside the simulation.
            if (controlledEntityId != receiverId)
                return SimulationCallResult.Error("A script cannot issue orders to an entity other than itself.");
            if (!observer.IsAlive(receiverId))
                return SimulationCallResult.Success(null);
            if (observer.GetEntityType(receiverId) != SimulationEntityType.Nexus)
                return SimulationCallResult.Warning("build() is not supported by this entity type.");

            // Only the construction request crosses into Simulation. The future
            // orchestrator retains the requested entry function using request.requestId.
            bool? accepted = controller.TryBuildUnit(receiverId, out request);
            return SimulationCallResult.Success(accepted);
        }

        /// <summary>
        /// Chooses the operation meant by a script call, checks whether it applies to
        /// the receiving entity and then performs the corresponding simulation API call.
        /// </summary>
        private SimulationCallResult Invoke(IReadOnlyList<SimulationApiOperation> overloads,
            object provider, int receiverId, object[] arguments)
        {
            SimulationApiOperation chosen = null;
            object[] parameters = null;
            int bestScore = int.MaxValue;
            bool ambiguous = false;
            bool manual = false;

            // Different forms of one DScript name can mean different operations
            // (for example move(position) versus move(entity)). Find the best fit
            // before executing anything, so trying overloads has no side effects.
            foreach (SimulationApiOperation operation in overloads)
            {
                if (operation.ManualRegistration)
                {
                    // Special operations such as build() need context only the
                    // orchestrator has; do not dispatch them as ordinary C# calls.
                    manual = true;
                    continue;
                }
                if (!TryConvertArguments(operation, receiverId, arguments, out object[] candidate, out int score))
                    continue;

                // Prefer the call that needs the fewest implicit conversions or
                // omitted optional arguments. Equal scores are genuinely ambiguous.
                if (score < bestScore)
                {
                    chosen = operation;
                    parameters = candidate;
                    bestScore = score;
                    ambiguous = false;
                }
                else if (score == bestScore)
                    ambiguous = true;
            }

            // Fail clearly if the arguments do not match any declared public form,
            // or if two forms are equally plausible.
            if (chosen == null)
                return SimulationCallResult.Error(manual
                    ? overloads[0].Name + "() requires manual orchestration."
                    : "No matching arguments for " + overloads[0].Name + "().");
            if (ambiguous)
                return SimulationCallResult.Error("Ambiguous overload of " + chosen.Name + "().");

            // Dead entities retain only the explicitly permitted observations, such
            // as id and alive. Other calls simply have no current value to return.
            if (!chosen.AllowsDeadReceiver && !observer.IsAlive(receiverId))
                return SimulationCallResult.Success(null);
            // Calling a unit-only operation on a living resource is a mistake, but
            // it should warn and return None instead of terminating the script.
            if (observer.IsAlive(receiverId) && !AcceptsReceiver(chosen.AppliesTo, receiverId))
                return SimulationCallResult.Warning(chosen.Name + "() is not supported by this entity type.");

            // Now that the call is known and authorized, execute the simulation's own
            // implementation. The catalog already validated which method is safe to call.
            object result;
            try
            {
                result = chosen.Method.Invoke(provider, parameters);
            }
            catch (TargetInvocationException error)
            {
                throw new InvalidOperationException("Simulation API implementation failed: " + chosen.Method.Name,
                    error.InnerException ?? error);
            }
            return SimulationCallResult.Success(ConvertResult(chosen, result));
        }

        /// <summary>
        /// Determines whether the receiving entity supports this operation. A shared
        /// DScript API is convenient, but units, nexuses and resources have different abilities.
        /// </summary>
        private bool AcceptsReceiver(SimulationEntityKinds kinds, int receiverId)
        {
            SimulationEntityKinds actual = observer.GetEntityType(receiverId) switch
            {
                SimulationEntityType.Unit => SimulationEntityKinds.Unit,
                SimulationEntityType.Nexus => SimulationEntityKinds.Nexus,
                SimulationEntityType.Resource => SimulationEntityKinds.Resource,
                _ => SimulationEntityKinds.None
            };
            return (kinds & actual) != 0;
        }

        /// <summary>
        /// Builds the arguments for a candidate operation, including the implicit
        /// receiver. Only compatible calls are considered, without changing the world.
        /// </summary>
        private static bool TryConvertArguments(SimulationApiOperation operation, int receiverId,
            object[] arguments, out object[] parameters, out int score)
        {
            parameters = null;
            score = 0;
            if (arguments.Length < operation.MinArguments || arguments.Length > operation.MaxArguments)
                return false;

            // The entity receiving this call is always the first C# argument,
            // even though the player does not have to write self explicitly.
            parameters = new object[operation.Arguments.Count + 1];
            parameters[0] = receiverId;
            for (int index = 0; index < operation.Arguments.Count; index++)
            {
                ParameterInfo parameter = operation.Arguments[index];
                if (index >= arguments.Length)
                {
                    parameters[index + 1] = parameter.DefaultValue;
                    score++; // Prefer the overload requiring less implicit work.
                    continue;
                }
                if (!TryConvertArgument(parameter, arguments[index], out object value, out int cost))
                    return false;
                parameters[index + 1] = value;
                score += cost;
            }
            return true;
        }

        /// <summary>
        /// Translates one player-supplied value into the form expected by the simulation.
        /// Entity references stay distinct from numeric IDs, and only safe numeric
        /// conversions are accepted when choosing an overload.
        /// </summary>
        private static bool TryConvertArgument(ParameterInfo parameter, object input,
            out object value, out int score)
        {
            value = null;
            score = 0;
            // A plain integer must never accidentally identify a real entity.
            // Only an actual SimulationObject reference may fill an entity argument.
            if (parameter.IsDefined(typeof(SimulationEntityArgumentAttribute), false))
            {
                if (input is not SimulationEntityReference entity) return false;
                value = entity.Id;
                return true;
            }

            // Prefer values that already match exactly. A small number of conversions
            // keep scripts ergonomic without making overloads unpredictable.
            Type declared = parameter.ParameterType;
            Type target = Nullable.GetUnderlyingType(declared) ?? declared;
            if (input == null)
                return !target.IsValueType || Nullable.GetUnderlyingType(declared) != null;
            if (target.IsInstanceOfType(input))
            {
                value = input;
                return true;
            }
            // DScript integers can represent positions and distances when they fit
            // the deterministic Fixed range.
            if (target == typeof(Fixed) && (input is int || input is long))
            {
                try
                {
                    value = new Fixed(input is int integer ? integer : (long)input);
                    score = 1;
                    return true;
                }
                catch (OverflowException)
                {
                    return false; // Do not silently change an unrepresentable number.
                }
            }
            // Widening and checked narrowing allow harmless integer differences
            // between the scripting and simulation APIs.
            if (target == typeof(long) && input is int number)
            {
                value = (long)number;
                score = 1;
                return true;
            }
            if (target == typeof(int) && input is long raw && raw >= int.MinValue && raw <= int.MaxValue)
            {
                value = (int)raw;
                score = 1;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gives scripts safe entity references instead of exposing simulation IDs as
        /// ordinary numbers. Values that do not represent entities remain unchanged.
        /// </summary>
        private static object ConvertResult(SimulationApiOperation operation, object result)
        {
            // Missing observations stay None, and orders already return their final
            // result (usually whether the requested order was accepted).
            if (result == null) return null;
            if (operation.Kind != SimulationApiKind.Observation) return result;

            switch (operation.ResultKind)
            {
                case SimulationObservationResultKind.Entity:
                    // A single observed entity becomes an identity-only reference.
                    return new SimulationEntityReference((int)result);

                case SimulationObservationResultKind.EntityList:
                    // Every item must be a usable entity reference. This is a new list,
                    // so changing it in DScript cannot change the simulation's collections.
                    var entities = new List<SimulationEntityReference>();
                    foreach (int id in (List<int>)result)
                        entities.Add(new SimulationEntityReference(id));
                    return entities;

                case SimulationObservationResultKind.MoveTarget:
                    // A movement destination can be another entity or a point in space.
                    // Preserve that distinction for scripts that inspect movement targets.
                    SimulationMoveTarget target = (SimulationMoveTarget)result;
                    return target.Kind == SimulationMoveTargetKind.Entity
                        ? (object)new SimulationEntityReference(target.EntityId)
                        : target.Position;

                default:
                    // Numbers, booleans and other ordinary observations need no wrapper.
                    return result;
            }
        }
    }
}
