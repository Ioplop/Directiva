using System;
using System.Collections.Generic;
using System.Reflection;
using DMath;
using DSEvents;
using DSimulation;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    internal static class SimulationControllerTests
    {
        private static void SetResources(SBSide side, int amount)
        {
            typeof(SBSide).GetProperty(nameof(SBSide.Resources)).SetValue(side, amount);
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "Orders", name, description, action);

            Add("MoveToPosition", "A movement order sets the destination without immediately moving the unit", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                world.Start();
                SimulationTestHarness.DrainEvents(world);
                SimulationController controller = world.Controller;

                t.Check(controller != null, "controller initialized");
                t.Equal((bool?)true, controller.TryMoveToPosition(unit.id, 10, 11), "move accepted");
                t.Equal((SimulationMoveMode?)SimulationMoveMode.Position,
                    world.Observer.GetMoveMode(unit.id), "position mode");
                SimulationMoveTarget? target = world.Observer.GetMoveTarget(unit.id);
                t.Check(target.HasValue && target.Value.Kind == SimulationMoveTargetKind.Position,
                    "position target kind");
                t.Equal(SimulationTestHarness.P(10, 11), target.Value.Position, "destination");
                t.Equal(SimulationTestHarness.P(5, 5), unit.Position, "no immediate displacement");
                t.Equal(0L, world.Tick, "no simulation tick advanced");
                t.Equal(0, SimulationTestHarness.DrainEvents(world).Count, "orders do not emit movement events");
            });

            Add("FollowAndStopMove", "Move(target) follows an entity; Hold brakes without cancelling actions", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                SBUnit other = SimulationTestHarness.AddUnit(world, 1, 12, 5, 0);
                world.Start();
                var controller = world.Controller;

                t.Equal((bool?)true, controller.TryAttack(unit.id, other.id), "attack ordered");
                t.Equal((bool?)true, controller.TryMoveToEntity(unit.id, other.id), "follow accepted");
                t.Equal((SimulationMoveMode?)SimulationMoveMode.Follow, world.Observer.GetMoveMode(unit.id), "follow mode");
                t.Equal(other.id, world.Observer.GetMoveTarget(unit.id).Value.EntityId, "follow target ID");
                t.Equal((bool?)false, controller.TryMoveToEntity(unit.id, unit.id), "cannot follow self");
                t.Equal(other.id, world.Observer.GetMoveTarget(unit.id).Value.EntityId, "rejection preserves target");
                t.Equal((bool?)true, controller.TryStopMove(unit.id), "hold accepted");
                t.Equal((SimulationMoveMode?)SimulationMoveMode.Hold, world.Observer.GetMoveMode(unit.id), "movement held");
                t.Check(!world.Observer.GetMoveTarget(unit.id).HasValue, "follow target cleared");
                t.Equal((SimulationActionMode?)SimulationActionMode.Attack, world.Observer.GetActionMode(unit.id),
                    "attack order retained");
                t.Equal((bool?)true, controller.TryStopMove(unit.id), "repeated hold accepted");
            });

            Add("AttackAndGatherTargets", "Attack allows friendly fire and neutral resources; gathering requires a deposit", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                SBUnit ally = SimulationTestHarness.AddUnit(world, 1, 10, 5, 0);
                var deposit = new SBResource(world.GetSide(0), SimulationTestHarness.P(Fixed.Decimal(66, 1), 5), 0, 10);
                world.EnqueueCreation(deposit);
                world.Start();
                SimulationTestHarness.DrainEvents(world);
                var controller = world.Controller;

                t.Equal((bool?)true, controller.TryAttack(unit.id, ally.id), "friendly fire order accepted");
                t.Equal((int?)ally.id, world.Observer.GetActionTarget(unit.id), "ally targeted");
                t.Equal((bool?)true, controller.TryAttack(unit.id, deposit.id), "neutral deposit attack accepted");
                t.Equal((int?)deposit.id, world.Observer.GetActionTarget(unit.id), "deposit targeted");
                t.Equal((bool?)false, controller.TryAttack(unit.id, 345678), "missing target rejected");
                t.Equal((int?)deposit.id, world.Observer.GetActionTarget(unit.id), "rejected attack leaves order intact");
                t.Equal((bool?)false, controller.TryGather(unit.id, ally.id), "non-resource gather rejected");
                t.Equal((bool?)true, controller.TryGather(unit.id, deposit.id), "resource gather accepted");
                t.Equal((SimulationActionMode?)SimulationActionMode.Gather, world.Observer.GetActionMode(unit.id),
                    "gather mode set");
                t.Equal((int?)deposit.id, world.Observer.GetActionTarget(unit.id), "resource target stored");
                t.Equal(SimulationTestHarness.P(5, 5), unit.Position, "gather order did not move unit");
                t.Equal(0, SimulationTestHarness.DrainEvents(world).Count, "no extraction before update");
            });

            Add("GatherTransitions", "Reordering gather preserves progress; changing actions releases the resource", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                SBUnit enemy = SimulationTestHarness.AddUnit(world, 2, 15, 15, 0);
                var first = new SBResource(world.GetSide(0), SimulationTestHarness.P(Fixed.Decimal(66, 1), 5), 0, 10);
                var second = new SBResource(world.GetSide(0), SimulationTestHarness.P(12, 5), 0, 10);
                world.EnqueueCreation(first);
                world.EnqueueCreation(second);
                world.Start();
                var c = world.Controller;

                t.Equal((bool?)true, c.TryGather(unit.id, first.id), "gather first");
                world.Update(); // Attach and begin gathering.
                world.Update(); // One tick of progress.
                t.Equal((int?)unit.id, world.Observer.GetGatherer(first.id), "first deposit reserved");
                Fixed? progress = world.Observer.GetGatherProgress(unit.id);
                t.Check(progress.HasValue && progress.Value > 0, "gather progress accumulated");
                t.Equal((bool?)true, c.TryGather(unit.id, first.id), "repeat gather accepted");
                t.Equal(progress, world.Observer.GetGatherProgress(unit.id), "progress retained");
                t.Equal((bool?)true, c.TryGather(unit.id, second.id), "switch deposits");
                t.Check(!world.Observer.GetGatherer(first.id).HasValue, "old deposit released immediately");
                t.Equal((Fixed?)0, world.Observer.GetGatherProgress(unit.id), "progress reset on switch");
                t.Equal((bool?)true, c.TryAttack(unit.id, enemy.id), "attack interrupts gather");
                t.Equal((SimulationActionMode?)SimulationActionMode.Attack, world.Observer.GetActionMode(unit.id),
                    "attack mode");
                t.Equal((bool?)true, c.TryStopAction(unit.id), "stop action accepted");
                t.Equal((SimulationActionMode?)SimulationActionMode.Hold, world.Observer.GetActionMode(unit.id),
                    "action held");
                t.Check(!world.Observer.GetActionTarget(unit.id).HasValue, "action target cleared");
                t.Equal((bool?)true, c.TryStopAction(unit.id), "repeated stop accepted");
            });

            Add("DropCargo", "Dropping cargo interrupts gathering but not movement, and emits events next tick", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(Fixed.Decimal(66, 1), 5), 0, 50);
                world.EnqueueCreation(resource);
                world.Start();
                var c = world.Controller;

                t.Equal((bool?)true, c.TryMoveToPosition(unit.id, 5, 5), "move order accepted");
                t.Equal((bool?)true, c.TryGather(unit.id, resource.id), "gather order accepted");
                for (int i = 0; i < 53; i++)
                    world.Update();
                t.Equal((int?)1, world.Observer.GetCargo(unit.id), "one unit of cargo collected");
                t.Equal((int?)unit.id, world.Observer.GetGatherer(resource.id), "resource reserved");
                SimulationTestHarness.DrainEvents(world);

                t.Equal((bool?)true, c.TryDropCargo(unit.id), "drop order accepted");
                t.Equal((bool?)true, world.Observer.IsDropPending(unit.id), "pending drop recorded");
                t.Equal((bool?)false, c.TryGather(unit.id, resource.id), "gather blocked while drop pending");
                t.Equal((SimulationActionMode?)SimulationActionMode.Hold, world.Observer.GetActionMode(unit.id),
                    "gather cancelled immediately");
                t.Equal((SimulationMoveMode?)SimulationMoveMode.Position, world.Observer.GetMoveMode(unit.id),
                    "movement unchanged");
                t.Check(!world.Observer.GetGatherer(resource.id).HasValue, "resource released immediately");
                t.Equal((int?)1, world.Observer.GetCargo(unit.id), "cargo remains until next update");
                world.Update();
                t.Equal((int?)0, world.Observer.GetCargo(unit.id), "cargo discarded on next tick");
                t.Equal((bool?)false, world.Observer.IsDropPending(unit.id), "pending flag cleared");
                var events = SimulationTestHarness.DrainEvents(world);
                t.Check(events.Exists(e => e is EventDropCargo dropped && dropped.unitId == unit.id),
                    "drop event emitted");
                t.Check(events.Exists(e => e is EventCargo cargo && cargo.entityId == unit.id && cargo.cargo == 0),
                    "cargo reset event emitted");
            });

            Add("DropDoesNotInterruptAttack", "Dropping while attacking preserves both action and move order", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                SBUnit enemy = SimulationTestHarness.AddUnit(world, 2, 12, 5, 0);
                world.Start();
                var c = world.Controller;

                t.Equal((bool?)true, c.TryAttack(unit.id, enemy.id), "attack accepted");
                t.Equal((bool?)true, c.TryMoveToEntity(unit.id, enemy.id), "follow accepted");
                t.Equal((bool?)true, c.TryDropCargo(unit.id), "drop accepted during attack");
                t.Equal((SimulationActionMode?)SimulationActionMode.Attack, world.Observer.GetActionMode(unit.id),
                    "attack preserved");
                t.Equal((SimulationMoveMode?)SimulationMoveMode.Follow, world.Observer.GetMoveMode(unit.id),
                    "follow preserved");
            });

            Add("InvalidAndDeadReceivers", "Wrong or dead entity receivers return null and rejected targets preserve orders", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                SBUnit target = SimulationTestHarness.AddUnit(world, 2, 10, 5, 0);
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(5, 12), 0, 10);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(12, 12));
                world.EnqueueCreation(resource);
                world.EnqueueCreation(nexus);
                world.Start();
                var c = world.Controller;

                t.Check(!c.TryMoveToPosition(nexus.id, 10, 10).HasValue, "nexus cannot move");
                t.Check(!c.TryAttack(resource.id, target.id).HasValue, "resource cannot attack by order");
                t.Check(!c.TryGather(nexus.id, resource.id).HasValue, "nexus cannot gather");
                t.Check(!c.TryDropCargo(nexus.id).HasValue, "nexus cannot discard cargo");
                t.Check(!c.TryBuildUnit(unit.id, out _).HasValue, "unit cannot construct");
                t.Check(!c.TryStopMove(999999).HasValue, "missing receiver cannot move");
                SimulationTestHarness.Kill(unit);
                t.Check(!c.TryAttack(unit.id, target.id).HasValue, "dead unit cannot attack");
                t.Check(!c.TryStopAction(unit.id).HasValue, "dead unit cannot stop action");
            });

            Add("BuildRequests", "The controller forwards accepted builds without knowing DScript functions", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 60, height: 40);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                world.EnqueueCreation(nexus);
                world.Start();
                var c = world.Controller;

                t.Equal((bool?)false, c.TryBuildUnit(nexus.id, out _), "no resources rejects build");
                SetResources(world.GetSide(1), 20);
                t.Equal((bool?)true, c.TryBuildUnit(nexus.id, out UnitBuildRequest request), "build accepted");
                t.Equal(nexus.id, request.nexusId, "request references the nexus");
                t.Equal(1, request.requestId, "first accepted request");
                t.Equal(10, world.GetSide(1).Resources, "cost paid once on acceptance");
                t.Equal((bool?)false, c.TryBuildUnit(nexus.id, out _), "busy nexus rejects build");
                t.Equal(10, world.GetSide(1).Resources, "rejection costs nothing");
                for (int i = 0; i < 500; i++)
                    world.Update();
                t.Check(world.TryConsumeCreatedUnit(out UnitCreationResult result), "completion produced");
                t.Equal(request.requestId, result.requestId, "completion correlates with request");
                t.Equal(1, world.GetSide(1).Units.Count, "unit created");
                t.Equal(world.GetSide(1).Units[0].id, result.unitId, "created unit ID");
            });

            Add("ReflectionMetadata", "Native order names, overloads and the manual build operation are discoverable", t =>
            {
                var signatures = new HashSet<string>();
                int automatic = 0;
                int manual = 0;
                foreach (MethodInfo method in typeof(SimulationController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    var annotation = method.GetCustomAttribute<SimulationOrderAttribute>();
                    t.Check(annotation != null, method.Name + " has order metadata");
                    ParameterInfo[] parameters = method.GetParameters();
                    t.Check(parameters.Length > 0 && parameters[0].ParameterType == typeof(int),
                        method.Name + " has an entity ID receiver");
                    t.Check(method.ReturnType == typeof(bool?), method.Name + " returns Bool or None");
                    string signature = annotation.Name + ":" + (parameters.Length - 1);
                    t.Check(signatures.Add(signature), "distinct name/arity signature: " + signature);
                    if (annotation.ManualRegistration)
                    {
                        manual++;
                        t.Equal("build", annotation.Name, "build is the only manual order");
                        t.Check(parameters.Length == 2 && parameters[1].IsOut,
                            "build provides the accepted request to the orchestrator");
                    }
                    else
                        automatic++;
                }
                t.Equal(7, automatic, "seven automatically registered order signatures");
                t.Equal(1, manual, "one special order requires orchestration");
            });
        }
    }
}
