using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DMath;
using DOrchestration;
using DSimulation;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;
using DSExecution.DataTypes;
using DSExecution.Values;
using DSExecution.VirtualMachine;
using Directiva.EditorTests.SimulationTests;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.OrchestrationTests
{
    /// <summary>
    /// Full source-to-VM-to-simulation regression tests. Each catalog entry receives an
    /// independent automated case, in addition to isolation/security/value tests.
    /// </summary>
    internal static class SimulationLinkTests
    {
        private sealed class Fixture
        {
            internal World World;
            internal SBUnit Unit;
            internal SBUnit Ally;
            internal SBUnit Enemy;
            internal SBNexus Nexus;
            internal SBResource Resource;
        }

        private static Fixture NewFixture()
        {
            World world = SimulationTestHarness.NewWorld(width: 50, height: 50);
            SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 6, 6, 0);
            SBUnit ally = SimulationTestHarness.AddUnit(world, 1, 10, 6, 0);
            SBUnit enemy = SimulationTestHarness.AddUnit(world, 2, 14, 6, 0);
            var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(6, 25));
            var enemyNexus = new SBNexus(world.GetSide(2), SimulationTestHarness.P(42, 25));
            var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(13, 13), 0, 20);
            world.EnqueueCreation(nexus);
            world.EnqueueCreation(enemyNexus);
            world.EnqueueCreation(resource);
            world.Start();
            return new Fixture { World = world, Unit = unit, Ally = ally, Enemy = enemy,
                Nexus = nexus, Resource = resource };
        }

        private static void SetResources(SBSide side, int amount) =>
            typeof(SBSide).GetProperty(nameof(SBSide.Resources)).SetValue(side, amount);

        private static DScriptCompiledProgram Compile(DirectivaTestContext t, string source)
        {
            var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                { ["test"] = source });
            DScriptCompilationResult result = DScriptCompiler.Compile("test", provider,
                SimulationDScriptProfile.Options);
            t.Attach("test.dscript", source);
            t.Attach("diagnostics", string.Join("\n", result.Diagnostics));
            t.Check(result.Success, "Compilation failed: " + string.Join("; ", result.Diagnostics));
            return result.Program;
        }

        private static DirectivaVM Run(DirectivaTestContext t, SideRuntime side,
            int entityId, string function = "test.main")
        {
            t.Check(side.Program.TryGetFunctionId(function, out int entry), "Function " + function);
            DirectivaVM vm = side.BindEntity(entityId, entry).VM;
            VMRunResult result = vm.Run(50000);
            t.Note("VM " + entityId + ": " + result.Status + ", steps=" + result.StepsExecuted +
                ", result=" + vm.ReturnValue.dataType + ", error=" + vm.Error?.Message);
            t.Equal(VMExecutionStatus.Completed, vm.Status, "VM completion: " + vm.Error?.Message);
            return vm;
        }

        private static bool ResourceOnly(SimulationApiOperation op) =>
            (op.AppliesTo & SimulationEntityKinds.Player) == 0 &&
            (op.AppliesTo & SimulationEntityKinds.Resource) != 0;

        private static bool NexusOnly(SimulationApiOperation op) =>
            (op.AppliesTo & SimulationEntityKinds.Unit) == 0 &&
            (op.AppliesTo & SimulationEntityKinds.Nexus) != 0;

        private static string DScriptArgument(ParameterInfo argument)
        {
            var entity = argument.GetCustomAttribute<SimulationEntityArgumentAttribute>();
            if (entity != null)
                return (entity.AppliesTo & SimulationEntityKinds.Resource) != 0 &&
                    (entity.AppliesTo & SimulationEntityKinds.Player) == 0
                    ? "self.closest_resource()" : "self";
            Type type = Nullable.GetUnderlyingType(argument.ParameterType) ?? argument.ParameterType;
            if (type == typeof(Fixed)) return "3";
            if (type == typeof(SimulationSideRelation)) return "Side.Enemy";
            if (type == typeof(SimulationEntityType)) return "Type.Unit";
            if (type == typeof(int) || type == typeof(long)) return "3";
            throw new InvalidOperationException("Unhandled DScript argument type: " + type.Name);
        }

        private static object HostArgument(ParameterInfo parameter, Fixture f)
        {
            var entity = parameter.GetCustomAttribute<SimulationEntityArgumentAttribute>();
            if (entity != null)
                return new SimulationEntityReference((entity.AppliesTo & SimulationEntityKinds.Player) == 0
                    ? f.Resource.id : f.Enemy.id);
            Type type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            if (type == typeof(Fixed)) return (Fixed)3;
            if (type == typeof(SimulationSideRelation)) return SimulationSideRelation.Enemy;
            if (type == typeof(SimulationEntityType)) return SimulationEntityType.Unit;
            if (type == typeof(int) || type == typeof(long)) return 3;
            throw new InvalidOperationException("Unhandled host argument: " + type.Name);
        }

        private static void CheckObservation(DirectivaTestContext t, SimulationApiOperation operation)
        {
            Fixture f = NewFixture();
            bool resourceReceiver = ResourceOnly(operation);
            bool nexusReceiver = NexusOnly(operation);
            int observedId = resourceReceiver ? f.Resource.id : nexusReceiver ? f.Nexus.id : f.Unit.id;
            int boundId = nexusReceiver ? f.Nexus.id : f.Unit.id;
            string receiver = resourceReceiver ? "self.closest_resource()" : "self";
            string[] arguments = operation.Arguments.Where(a => !a.IsOptional)
                .Select(DScriptArgument).ToArray();
            string access = operation.MemberKind == SimulationObservationMemberKind.Property
                ? receiver + "." + operation.Name
                : receiver + "." + operation.Name + "(" + string.Join(", ", arguments) + ")";
            var program = Compile(t, "def main():\n    return " + access + "\n");
            var side = new SideRuntime(f.World, 1, program, 9UL);
            var vm = Run(t, side, boundId);

            var bridge = new SimulationBridge(f.World);
            var hostArguments = operation.Arguments.Where(a => !a.IsOptional)
                .Select(a => HostArgument(a, f)).ToArray();
            SimulationCallResult observed = bridge.Observe(observedId, operation.Name, hostArguments);
            t.Equal(SimulationCallStatus.Success, observed.Status,
                "Bridge observation " + operation.Name + ": " + observed.Message);
            if (observed.Value == null)
                t.Equal(DataTypeId.None, vm.ReturnValue.dataType, "null -> None");
            else
                t.Check(vm.ReturnValue.dataType != DataTypeId.None &&
                    vm.ReturnValue.dataType != DataTypeId.Uninitialized,
                    "Observation " + operation.Name + " must be converted to a DScript value.");
        }

        private static void CheckOrder(DirectivaTestContext t, SimulationApiOperation operation)
        {
            Fixture f = NewFixture();
            string expression = operation.Method.Name switch
            {
                "TryMoveToPosition" => "self.move(10, 11)",
                "TryMoveToEntity" => "self.move(self.closest_enemy())",
                "TryStopMove" => "self.stop_move()",
                "TryAttack" => "self.attack(self.closest_enemy())",
                "TryGather" => "self.gather(self.closest_resource())",
                "TryStopAction" => "self.stop_action()",
                "TryDropCargo" => "self.drop_cargo()",
                "TryBuildUnit" => "self.build(next)",
                _ => throw new InvalidOperationException("No test for order " + operation.Method.Name)
            };
            bool nexus = operation.AppliesTo == SimulationEntityKinds.Nexus;
            if (nexus) SetResources(f.World.GetSide(1), 40);
            var program = Compile(t, "def next():\n    pass\ndef main():\n    return " + expression + "\n");
            var side = new SideRuntime(f.World, 1, program, 9UL);
            var vm = Run(t, side, nexus ? f.Nexus.id : f.Unit.id);
            t.Equal(DataTypeId.Bool, vm.ReturnValue.dataType, "order must return Bool");
            t.Equal(1UL, vm.ReturnValue.value, "order must be accepted");
            if (operation.Method.Name == "TryBuildUnit")
            {
                t.Equal(1, side.PendingBuildCount, "build function retained by request ID");
                t.Check(side.TryConsumeBuild(1, out int functionId), "request can be consumed");
                t.Check(program.TryGetFunctionId("test.next", out int requestedId), "entry exists");
                t.Equal(requestedId, functionId, "correct entry function bound to request");
                t.Check(!side.TryConsumeBuild(1, out _), "build request cannot be consumed twice");
            }
        }

        private static void CheckImplicitMethod(DirectivaTestContext t, SimulationApiOperation operation)
        {
            Fixture f = NewFixture();
            bool nexus = operation.AppliesTo == SimulationEntityKinds.Nexus || NexusOnly(operation);
            if (operation.Kind == SimulationApiKind.Order)
            {
                if (nexus) SetResources(f.World.GetSide(1), 40);
                // Match the tested explicit order signature, but omit only the receiver.
                string expression = operation.Method.Name switch
                {
                    "TryMoveToPosition" => "move(10, 11)",
                    "TryMoveToEntity" => "move(self.closest_enemy())",
                    "TryStopMove" => "stop_move()",
                    "TryAttack" => "attack(self.closest_enemy())",
                    "TryGather" => "gather(self.closest_resource())",
                    "TryStopAction" => "stop_action()",
                    "TryDropCargo" => "drop_cargo()",
                    "TryBuildUnit" => "build(next)",
                    _ => throw new InvalidOperationException("No implicit order test for " + operation.Method.Name)
                };
                var program = Compile(t, "def next():\n    pass\ndef main():\n    return " + expression + "\n");
                t.Contains("CALL_METHOD " + operation.Name + " ", program.DILModules["test"],
                    "bare order must emit method dispatch");
                var vm = Run(t, new SideRuntime(f.World, 1, program, 3UL),
                    nexus ? f.Nexus.id : f.Unit.id);
                t.Equal(DataTypeId.Bool, vm.ReturnValue.dataType, "implicit order returns Bool");
                t.Equal(1UL, vm.ReturnValue.value, "implicit order accepted");
            }
            else
            {
                string[] arguments = operation.Arguments.Where(a => !a.IsOptional)
                    .Select(DScriptArgument).ToArray();
                string expression = operation.Name + "(" + string.Join(", ", arguments) + ")";
                var program = Compile(t, "def main():\n    return " + expression + "\n");
                string dil = program.DILModules["test"];
                t.Contains("PUSH_SELF", dil, "implicit observation loads self");
                t.Contains("CALL_METHOD " + operation.Name + " " + arguments.Length,
                    dil, "implicit observation uses CALL_METHOD");
                // Resource-only methods cannot be invoked on a resource as self: resources
                // are not script-capable. The host correctly returns a nonfatal warning.
                var side = new SideRuntime(f.World, 1, program, 3UL);
                var vm = Run(t, side, nexus ? f.Nexus.id : f.Unit.id);
                t.Check(vm.ReturnValue.dataType != DataTypeId.Uninitialized,
                    "implicit observation returns a defined value (possibly None)");
            }
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "OrchestrationLink", name, description, action);

            // The catalog is authoritative: when the API grows, tests grow with it.
            // Each observation and order overload gets its own named regression test.
            // Every callable member also gets a regression test without an explicit self.
            foreach (SimulationApiOperation entry in SimulationApiCatalog.Operations)
            {
                SimulationApiOperation operation = entry;
                // Names are unique per public overload, not per DScript method name.
                // Keep older test IDs unchanged for the (much more common) single form.
                string name = operation.Kind == SimulationApiKind.Observation
                    ? "Observe_" + operation.Name +
                        (SimulationApiCatalog.TryGetObservation(operation.Name, out var variants) &&
                        variants.Count > 1 ? "_" + operation.Method.Name : string.Empty)
                    : "Order_" + operation.Method.Name;
                if (operation.Kind == SimulationApiKind.Observation)
                    Add(name, "DScript/VM observes " + operation.Name,
                        t => CheckObservation(t, operation));
                else
                    Add(name, "DScript/VM issues " + operation.Name + " via " + operation.Method.Name,
                        t => CheckOrder(t, operation));

                if (operation.Kind == SimulationApiKind.Order ||
                    operation.MemberKind == SimulationObservationMemberKind.Method)
                {
                    string implicitName = "Implicit_" + operation.Kind + "_" +
                        operation.Method.Name;
                    Add(implicitName, "Bare " + operation.Name + "() dispatches through self",
                        t => CheckImplicitMethod(t, operation));
                }
            }

            Add("FilterOverloadsFromDScript", "DScript can search from a neutral resource by type with optional side filtering", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t,
                    "def main():\n" +
                    "    r = closest_resource()\n" +
                    "    nearby = r.closest_entity(Type.Nexus)\n" +
                    "    no_filter = closest_entity()\n" +
                    "    enemy_base = closest_entity(Side.Enemy, Type.Nexus)\n" +
                    "    all_nexuses = r.entities(Type.Nexus)\n" +
                    "    anything = entities()\n" +
                    "    enemy_units = entities(Side.Enemy, Type.Unit)\n" +
                    "    in_range = r.entities_in_range(20, Type.Nexus)\n" +
                    "    enemy_in_range = entities_in_range(20, Side.Enemy, Type.Unit)\n" +
                    "    a = nearby == self.nexus and no_filter != None\n" +
                    "    b = enemy_base == enemy_nexus() and all_nexuses.length == 2\n" +
                    "    c = anything.length == 6 and enemy_units.length == 1\n" +
                    "    d = in_range.length == 1 and enemy_in_range.length == 1\n" +
                    "    return a and b and c and d\n");
                var vm = Run(t, new SideRuntime(f.World, 1, program, 5UL), f.Unit.id);
                t.Equal(DataTypeId.Bool, vm.ReturnValue.dataType, "filter expressions return Bool");
                t.Equal(1UL, vm.ReturnValue.value, "type filters from neutral objects work end-to-end");
            });

            Add("ShadowedByFunction", "A user def move() takes precedence over the implicit method", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t,
                    "def move():\n    return self.move(10, 20)\n" +
                    "def main():\n    return move()\n");
                string dil = program.DILModules["test"];
                t.Contains("PUSH_FUNCTION test.move", dil, "bare move resolves to user function");
                t.Contains("CALL_METHOD move 2", dil, "explicit self.move still resolves to order");
                t.Equal(1UL, Run(t, new SideRuntime(f.World, 1, program, 8UL), f.Unit.id).ReturnValue.value,
                    "user function calls simulation move successfully");
            });

            Add("ShadowedByLocal", "A local function value hides a simulation API method", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t,
                    "def helper():\n    return 27\n" +
                    "def main():\n    move = helper\n    return move()\n");
                t.Check(!program.DILModules["test"].Contains("CALL_METHOD move 0"),
                    "local calls must remain indirect");
                t.Equal(27L, unchecked((long)Run(t, new SideRuntime(f.World, 1, program, 8UL),
                    f.Unit.id).ReturnValue.value), "local wins");
            });

            Add("ShadowedByShared", "A shared name hides the implicit simulation method", t =>
            {
                var program = Compile(t,
                    "shared move = None\ndef main():\n    return move()\n");
                t.Check(!program.DILModules["test"].Contains("CALL_METHOD move 0"),
                    "shared names must not fall back to self");
                t.Contains("LOAD_GLOBAL", program.DILModules["test"],
                    "shared binding is loaded normally");
            });

            Add("ShadowedByImport", "An imported function hides the implicit simulation method", t =>
            {
                Fixture f = NewFixture();
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                {
                    ["test"] = "from util import move\ndef main():\n    return move()\n",
                    ["util"] = "def move():\n    return 33\n"
                });
                var result = DScriptCompiler.Compile("test", provider, SimulationDScriptProfile.Options);
                t.Check(result.Success, "imported move compiles: " + string.Join("; ", result.Diagnostics));
                t.Equal(33L, unchecked((long)Run(t, new SideRuntime(f.World, 1, result.Program, 8UL),
                    f.Unit.id).ReturnValue.value), "imported function wins");
            });

            Add("ImplicitMoveBounds", "Bare move() retains the bridge's world-bounds protection", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t,
                    "def main():\n    return move(9223372036854775807, 2)\n");
                var side = new SideRuntime(f.World, 1, program, 8UL);
                t.Check(program.TryGetFunctionId("test.main", out int entry), "main exists");
                var vm = side.BindEntity(f.Unit.id, entry).VM;
                vm.Run(10000);
                t.Equal(VMExecutionStatus.Faulted, vm.Status, "unsafe position must fault VM");
                t.Contains("world bounds", vm.Error.Message, "same bounds error as self.move");
                t.Equal(SimulationMoveMode.Hold, f.World.Observer.GetMoveMode(f.Unit.id).Value,
                    "rejected implicit move has no effects");
            });

            Add("ShadowArityStillChecked", "An explicit user function never falls back on wrong arity", t =>
            {
                var result = DScriptCompiler.Compile("test", new InMemoryDScriptSourceProvider(
                    new Dictionary<string, string> { ["test"] =
                        "def move():\n    pass\ndef main():\n    return move(10, 20)\n" }),
                    SimulationDScriptProfile.Options);
                t.Check(!result.Success && result.Diagnostics.Any(d => d.Code == "DS4014"),
                    "user function retains compile-time arity checking");
            });

            Add("ContextlessImplicitCall", "Standalone VMs compile implicit self but cannot execute it", t =>
            {
                var program = Compile(t, "def main():\n    return move(10, 20)\n");
                t.Check(program.TryGetFunctionId("test.main", out int entry), "entry found");
                var vm = new DirectivaVM(program.Code,
                    new VMMemoryContext(program.Code.GlobalCount), null, entry);
                vm.Run(10000);
                t.Equal(VMExecutionStatus.Faulted, vm.Status, "self requires a bound entity");
                t.Contains("entity-bound VM", vm.Error.Message, "clear standalone diagnostic");
            });

            Add("NativePrecedence", "A real builtin wins over any proposed implicit name", t =>
            {
                var options = new DSCompilation.DScript.DScriptFrontendOptions(
                    implicitSelfMethods: new[] { "print", "move" });
                var source = "def main():\n    print('hello')\n    move(2, 3)\n";
                var analysis = DSCompilation.DScript.DScriptFrontend.Analyze("test",
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                        { ["test"] = source }), options);
                t.Check(analysis.Success, "source binds under overlapping host names");
                var function = analysis.FunctionSymbols.Keys.Single();
                var printCall = ((DSCompilation.DScript.Ast.ExpressionStatement)function.Body[0]).Value
                    as DSCompilation.DScript.Ast.CallExpression;
                var moveCall = ((DSCompilation.DScript.Ast.ExpressionStatement)function.Body[1]).Value
                    as DSCompilation.DScript.Ast.CallExpression;
                t.Equal(DSCompilation.DScript.Semantics.SymbolKind.NativeFunction,
                    analysis.ExpressionSymbols[printCall.Callee].Kind, "builtin wins");
                t.Equal(DSCompilation.DScript.Semantics.SymbolKind.ImplicitSelfMethod,
                    analysis.ExpressionSymbols[moveCall.Callee].Kind, "unbound move uses self");
            });

            Add("UndefinedStillUndefined", "Only known callable members are eligible for implicit self", t =>
            {
                foreach (string expression in new[] { "unknown_operation()", "health()", "move" })
                {
                    var result = DScriptCompiler.Compile("test", new InMemoryDScriptSourceProvider(
                        new Dictionary<string, string> { ["test"] = "def main():\n    return " + expression + "\n" }),
                        SimulationDScriptProfile.Options);
                    t.Check(!result.Success && result.Diagnostics.Any(d => d.Code == "DS4016"),
                        expression + " must remain an undefined name");
                }
            });

            Add("WorldBounds", "Move destinations are rejected before Fixed math or side effects", t =>
            {
                Fixture f = NewFixture();
                var bridge = new SimulationBridge(f.World);
                foreach (object[] coords in new[]
                {
                    new object[] { -1, 4 }, new object[] { 4, -1 },
                    new object[] { 51, 4 }, new object[] { 4, 51 },
                    new object[] { long.MaxValue, 3 }
                })
                {
                    SimulationCallResult outcome = bridge.Order(f.Unit.id, f.Unit.id, "move", coords);
                    t.Equal(SimulationCallStatus.Error, outcome.Status, "outside world rejected");
                    t.Contains("world bounds", outcome.Message, "clear movement bounds error");
                    t.Equal(SimulationMoveMode.Hold, f.World.Observer.GetMoveMode(f.Unit.id).Value,
                        "failed move has no side effect");
                }
                SimulationCallResult edge = bridge.Order(f.Unit.id, f.Unit.id, "move", 50, 50);
                t.Equal(SimulationCallStatus.Success, edge.Status, "boundary is inclusive");
                t.Equal(true, (bool)edge.Value, "boundary accepted");
            });

            Add("VMWorldBounds", "Out-of-world coordinates fault the VM with a readable message", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t, "def main():\n    return self.move(9223372036854775807, 2)\n");
                var side = new SideRuntime(f.World, 1, program, 4UL);
                t.Check(program.TryGetFunctionId("test.main", out int id), "entry exists");
                var vm = side.BindEntity(f.Unit.id, id).VM;
                vm.Run(10000);
                t.Equal(VMExecutionStatus.Faulted, vm.Status, "must fault cleanly");
                t.Contains("world bounds", vm.Error.Message, "specific bounds diagnostic");
                t.Equal(SimulationMoveMode.Hold, f.World.Observer.GetMoveMode(f.Unit.id).Value,
                    "failed VM order cannot change simulation");
            });

            Add("HugeRange", "Unreasonably large squared-distance filters are rejected safely", t =>
            {
                Fixture f = NewFixture();
                var bridge = new SimulationBridge(f.World);
                SimulationCallResult outcome = bridge.Observe(f.Unit.id, "within_distance",
                    new SimulationEntityReference(f.Enemy.id), new Fixed(long.MaxValue, raw: true));
                t.Equal(SimulationCallStatus.Error, outcome.Status, "huge range rejected");
                t.Contains("distance", outcome.Message, "clear distance diagnostic");
            });

            Add("EntityValuesAndCollections", "Entity identity survives shared, lists, sets and dict keys", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t,
                    "shared remembered = None\ndef main():\n" +
                    "    enemy = self.closest_enemy()\n" +
                    "    remembered = enemy\n" +
                    "    values = [enemy, enemy]\n" +
                    "    keys = {enemy, enemy}\n" +
                    "    mapping = {enemy: 7}\n" +
                    "    return values[1] == remembered and keys.length == 1 and mapping[enemy] == 7\n");
                var side = new SideRuntime(f.World, 1, program, 1UL);
                var vm = Run(t, side, f.Unit.id);
                t.Equal(DataTypeId.Bool, vm.ReturnValue.dataType, "result is Bool");
                t.Equal(1UL, vm.ReturnValue.value, "identity/hash/container semantics");
                t.Equal(DataTypeId.SimulationObject, side.Memory.GetGlobal(0).dataType,
                    "shared stores an identity value");
                t.Equal(f.Enemy.id, (int)side.Memory.GetGlobal(0).value, "shared entity ID");
            });

            Add("EnumsAreTyped", "Side, Type and movement enums keep type identity", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t,
                    "def main():\n    return self.type == Type.Unit and " +
                    "self.relation(self) == Side.Ally and self.move_mode == MoveMode.Hold\n");
                var vm = Run(t, new SideRuntime(f.World, 1, program, 2UL), f.Unit.id);
                t.Equal(DataTypeId.Bool, vm.ReturnValue.dataType, "Bool result");
                t.Equal(1UL, vm.ReturnValue.value, "enum values preserve kind and value");
                t.Check(DataValue.FromSimulationEnum(SimulationEnumKind.Side, 1).value !=
                    DataValue.FromSimulationEnum(SimulationEnumKind.Type, 1).value,
                    "different enum families cannot collide");
            });

            Add("SharedIsolation", "A side shares its globals; an opposing side has a separate heap and globals", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t, "shared counter = 1\ndef main():\n    counter += 1\n    return counter\n");
                var firstSide = new SideRuntime(f.World, 1, program, 4UL);
                var secondSide = new SideRuntime(f.World, 2, program, 5UL);
                t.Check(!ReferenceEquals(firstSide.Memory, secondSide.Memory), "distinct memory contexts");
                t.Equal(2L, unchecked((long)Run(t, firstSide, f.Unit.id).ReturnValue.value),
                    "first friendly VM incremented");
                t.Equal(3L, unchecked((long)Run(t, firstSide, f.Ally.id).ReturnValue.value),
                    "second friendly VM sees previous increment");
                t.Equal(2L, unchecked((long)Run(t, secondSide, f.Enemy.id).ReturnValue.value),
                    "enemy VM starts from independent initialized value");
                t.Equal(3L, unchecked((long)firstSide.Memory.GetGlobal(0).value), "side one global");
                t.Equal(2L, unchecked((long)secondSide.Memory.GetGlobal(0).value), "side two global");
            });

            Add("Authorization", "Entity-bound VM cannot issue orders through a foreign handle", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t, "def main():\n    other = self.closest_enemy()\n    return other.attack(self)\n");
                var side = new SideRuntime(f.World, 1, program, 2UL);
                t.Check(program.TryGetFunctionId("test.main", out int entry), "entry found");
                var vm = side.BindEntity(f.Unit.id, entry).VM;
                vm.Run(10000);
                t.Equal(VMExecutionStatus.Faulted, vm.Status, "foreign order faults the VM");
                t.Contains("other than itself", vm.Error.Message, "authorization diagnostic");
            });

            Add("WarningsAreNonFatal", "Inapplicable observations return None and report a warning", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t, "def main():\n    return self.closest_resource().enemies()\n");
                var side = new SideRuntime(f.World, 1, program, 2UL);
                var warnings = new List<string>();
                side.Warning += (id, message) => warnings.Add(message);
                var vm = Run(t, side, f.Unit.id);
                t.Equal(DataTypeId.None, vm.ReturnValue.dataType, "unsupported query returns None");
                t.Equal(1, warnings.Count, "one warning logged");
                t.Contains("not supported", warnings[0], "warning reason");
            });

            Add("MoveTupleBounds", "Tuple destinations also obey movement bounds", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t,
                    "def valid():\n    return self.move((10, 12))\n" +
                    "def invalid():\n    return self.move((9999, 2))\n" +
                    "def overflow():\n    return self.move((9223372036854775807, 2))\n");
                var side = new SideRuntime(f.World, 1, program, 7UL);
                t.Equal(1UL, Run(t, side, f.Unit.id, "test.valid").ReturnValue.value,
                    "in-bounds tuple accepted");
                t.Check(program.TryGetFunctionId("test.invalid", out int invalidEntry), "entry exists");
                var invalid = side.BindEntity(f.Ally.id, invalidEntry).VM;
                invalid.Run(10000);
                t.Equal(VMExecutionStatus.Faulted, invalid.Status, "out-of-bounds tuple must fault");
                t.Contains("world bounds", invalid.Error.Message, "bounds diagnostic");
                t.Check(program.TryGetFunctionId("test.overflow", out int hugeEntry), "entry exists");
                var third = SimulationTestHarness.AddUnit(f.World, 1, 7, 10, 0);
                // Apply queued creation before binding a brand-new entity.
                f.World.Update();
                var overflow = side.BindEntity(third.id, hugeEntry).VM;
                overflow.Run(10000);
                t.Equal(VMExecutionStatus.Faulted, overflow.Status, "overflow tuple must fault");
                t.Contains("world bounds", overflow.Error.Message, "safe numeric bound diagnostic");
            });

            Add("SelfPerVM", "Each VM gets its own self while sharing side memory", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t, "def main():\n    return self.id\n");
                var side = new SideRuntime(f.World, 1, program, 1UL);
                t.Equal((ulong)f.Unit.id, Run(t, side, f.Unit.id).ReturnValue.value,
                    "first unit self ID");
                t.Equal((ulong)f.Ally.id, Run(t, side, f.Ally.id).ReturnValue.value,
                    "second unit self ID");
            });

            Add("DeadReferences", "Entity references retain final stats after death and registry removal", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t,
                    "shared saved = None\n" +
                    "def capture():\n    saved = self.closest_enemy()\n    return saved\n" +
                    "def check():\n    return saved.id > 0 and not saved.alive and saved.health == 0 and saved.shields == 0 and saved.type == Type.Unit and saved.position != None\n");
                var side = new SideRuntime(f.World, 1, program, 1UL);
                t.Equal(DataTypeId.SimulationObject,
                    Run(t, side, f.Unit.id, "test.capture").ReturnValue.dataType,
                    "captured identity");
                SimulationTestHarness.Kill(f.Enemy);
                f.World.Update();
                t.Equal(1UL, Run(t, side, f.Ally.id, "test.check").ReturnValue.value,
                    "dead entity identity and read-only observations");
            });

            Add("AlliedAuthorization", "Foreign allied entities cannot receive VM orders", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t,
                    "def main():\n    other = self.closest_ally()\n    return other.stop_move()\n");
                var side = new SideRuntime(f.World, 1, program, 1UL);
                t.Check(program.TryGetFunctionId("test.main", out int entry), "entry found");
                var vm = side.BindEntity(f.Unit.id, entry).VM;
                vm.Run(10000);
                t.Equal(VMExecutionStatus.Faulted, vm.Status, "allied order must fault");
                t.Contains("other than itself", vm.Error.Message, "authorization diagnostic");
            });

            Add("TypedEnumInputs", "Integer arguments cannot impersonate enum constants", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t, "def main():\n    return self.entities(1)\n");
                var side = new SideRuntime(f.World, 1, program, 1UL);
                t.Check(program.TryGetFunctionId("test.main", out int entry), "entry found");
                var vm = side.BindEntity(f.Unit.id, entry).VM;
                vm.Run(10000);
                t.Equal(VMExecutionStatus.Faulted, vm.Status, "integer is not a Side enum");
                t.Contains("No matching arguments", vm.Error.Message, "typed parameter diagnostic");
            });

            Add("ReadOnlySelf", "self is not writable or a permitted parameter", t =>
            {
                var invalid = new[]
                {
                    "def main():\n    self = 1\n",
                    "def main(self):\n    return 1\n",
                    "shared self = 2\ndef main():\n    pass\n",
                    "def self():\n    pass\n",
                    "def main():\n    for self in [1, 2]:\n        pass\n"
                };
                foreach (string source in invalid)
                {
                    var result = DScriptCompiler.Compile("test", new InMemoryDScriptSourceProvider(
                        new Dictionary<string, string> { ["test"] = source }));
                    t.Check(!result.Success, "reserved self must fail to compile");
                    t.Check(result.Diagnostics.Any(d => d.Code == "DS4031"), "self diagnostic");
                }
            });

            Add("ReadOnlyProperties", "Simulation properties cannot be assigned by player scripts", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t, "def main():\n    self.health = 500\n");
                var side = new SideRuntime(f.World, 1, program, 1UL);
                t.Check(program.TryGetFunctionId("test.main", out int entry), "entry found");
                var vm = side.BindEntity(f.Unit.id, entry).VM;
                vm.Run(10000);
                t.Equal(VMExecutionStatus.Faulted, vm.Status, "setter must fault");
                t.Contains("read-only", vm.Error.Message, "read-only property diagnostic");
            });
        }
    }
}
