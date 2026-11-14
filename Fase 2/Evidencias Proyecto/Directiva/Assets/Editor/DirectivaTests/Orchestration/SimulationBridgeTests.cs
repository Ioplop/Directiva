using System;
using System.Collections.Generic;
using System.Linq;
using DMath;
using DOrchestration;
using DSimulation;
using Directiva.EditorTests.SimulationTests;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.OrchestrationTests
{
    /// <summary>Checks the reflection catalog and the CLR-to-simulation invocation boundary.</summary>
    internal static class SimulationBridgeTests
    {
        private static void SetResources(SBSide side, int amount) =>
            typeof(SBSide).GetProperty(nameof(SBSide.Resources)).SetValue(side, amount);

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "OrchestrationBridge", name, description, action);

            Add("Catalog", "All observations, overloaded orders and the manual build are registered", t =>
            {
                t.Equal(88, SimulationApiCatalog.Operations.Count(o => o.Kind == SimulationApiKind.Observation),
                    "observation count");
                t.Equal(8, SimulationApiCatalog.Operations.Count(o => o.Kind == SimulationApiKind.Order),
                    "order overload count");
                t.Check(SimulationApiCatalog.TryGetOrder("move", out var moves) && moves.Count == 2,
                    "move has two overloads");
                t.Check(SimulationApiCatalog.TryGetOrder("build", out var builds) &&
                    builds.Count == 1 && builds[0].ManualRegistration && builds[0].MinArguments == 1,
                    "build requires manual function binding");
                t.Check(SimulationApiCatalog.TryGetObservation("alive", out var alive) &&
                    alive[0].AllowsDeadReceiver && alive[0].MemberKind == SimulationObservationMemberKind.Property,
                    "alive is an identity property");
                foreach (string name in new[] { "closest_entity", "entities", "entities_in_range" })
                {
                    t.Check(SimulationApiCatalog.TryGetObservation(name, out var filters) && filters.Count == 2,
                        name + " supports both side-relative and type-only overloads");
                    t.Check(filters.All(entry => entry.MemberKind == SimulationObservationMemberKind.Method),
                        name + " remains callable as a method");
                }

            });

            Add("PerspectivesAndSnapshots", "Queries on an enemy use its perspective; lists are independent", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit self = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                SBUnit enemy = SimulationTestHarness.AddUnit(world, 2, 11, 5, 0);
                world.Start();
                var bridge = new SimulationBridge(world);

                SimulationCallResult closest = bridge.Observe(self.id, "closest_enemy");
                t.Equal(SimulationCallStatus.Success, closest.Status, "query succeeded");
                t.Equal(enemy.id, ((SimulationEntityReference)closest.Value).Id, "nearest enemy handle");
                var fromEnemy = (List<SimulationEntityReference>)bridge.Observe(enemy.id, "enemies").Value;
                t.Equal(1, fromEnemy.Count, "enemy has one enemy");
                t.Equal(self.id, fromEnemy[0].Id, "query uses receiver perspective");
                fromEnemy.Clear();
                var fresh = (List<SimulationEntityReference>)bridge.Observe(enemy.id, "enemies").Value;
                t.Equal(1, fresh.Count, "mutating a returned list does not mutate simulation");
                t.Equal(0L, world.Tick, "observation did not advance the world");
            });

            Add("FlexibleFilterDispatch", "Overloads select Side and Type independently and support neutral resource origins", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 80, height: 80);
                var unit = SimulationTestHarness.AddUnit(world, 1, 10, 10, 0);
                var ally = SimulationTestHarness.AddUnit(world, 1, 12, 10, 0);
                var enemy = SimulationTestHarness.AddUnit(world, 2, 16, 10, 0);
                var friendlyNexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(6, 20));
                var enemyNexus = new SBNexus(world.GetSide(2), SimulationTestHarness.P(20, 20));
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(24, 20), 0, 10);
                world.EnqueueCreation(friendlyNexus);
                world.EnqueueCreation(enemyNexus);
                world.EnqueueCreation(resource);
                world.Start();
                var bridge = new SimulationBridge(world);

                SimulationCallResult nearest = bridge.Observe(resource.id, "closest_entity", SimulationEntityType.Nexus);
                t.Equal(SimulationCallStatus.Success, nearest.Status, "type-only query from resource");
                t.Equal(enemyNexus.id, ((SimulationEntityReference)nearest.Value).Id,
                    "resource finds its nearest nexus irrespective of side");
                t.Equal(ally.id, ((SimulationEntityReference)
                    bridge.Observe(unit.id, "closest_entity").Value).Id, "no-filter query");
                t.Equal(enemy.id, ((SimulationEntityReference)
                    bridge.Observe(unit.id, "closest_entity", SimulationSideRelation.Enemy).Value).Id,
                    "side-only query");
                t.Equal(enemyNexus.id, ((SimulationEntityReference)
                    bridge.Observe(unit.id, "closest_entity", SimulationSideRelation.Enemy,
                        SimulationEntityType.Nexus).Value).Id, "side and type query");

                t.Equal(6, ((List<SimulationEntityReference>)bridge.Observe(resource.id, "entities").Value).Count,
                    "unfiltered list from neutral resource");
                t.Equal(2, ((List<SimulationEntityReference>)
                    bridge.Observe(resource.id, "entities", SimulationEntityType.Nexus).Value).Count,
                    "type-only list from neutral resource");
                t.Equal(2, ((List<SimulationEntityReference>)
                    bridge.Observe(unit.id, "entities", SimulationSideRelation.Enemy).Value).Count,
                    "side-only list");
                t.Equal(1, ((List<SimulationEntityReference>)
                    bridge.Observe(unit.id, "entities", SimulationSideRelation.Enemy,
                        SimulationEntityType.Unit).Value).Count, "combined list");

                t.Equal(2, ((List<SimulationEntityReference>)
                    bridge.Observe(resource.id, "entities_in_range", 5).Value).Count,
                    "range without filters");
                t.Equal(1, ((List<SimulationEntityReference>)
                    bridge.Observe(resource.id, "entities_in_range", 5,
                        SimulationEntityType.Nexus).Value).Count, "type-only range");
                t.Equal(1, ((List<SimulationEntityReference>)
                    bridge.Observe(unit.id, "entities_in_range", 12,
                        SimulationSideRelation.Enemy, SimulationEntityType.Unit).Value).Count,
                    "combined range filters");
                t.Equal(SimulationCallStatus.Warning,
                    bridge.Observe(resource.id, "closest_entity", SimulationSideRelation.Enemy).Status,
                    "side-relative query is not meaningful for a neutral receiver");
                t.Equal(SimulationCallStatus.Error,
                    bridge.Observe(unit.id, "closest_entity", 1).Status,
                    "numeric values cannot impersonate enum filters");
                t.Equal(SimulationCallStatus.Error,
                    bridge.Observe(unit.id, "entities_in_range", 1000000000).Status,
                    "new range overload retains distance bounds protection");
                t.Equal(0L, world.Tick, "read-only queries do not advance the world");
            });

            Add("DeadAndIncompatible", "Archived states remain inspectable; unsupported receivers still warn", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                SBResource resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(10, 10), 0, 10);
                world.EnqueueCreation(resource);
                world.Start();
                var bridge = new SimulationBridge(world);

                SimulationCallResult mismatch = bridge.Observe(resource.id, "enemies");
                t.Equal(SimulationCallStatus.Warning, mismatch.Status, "incompatible warning");
                t.Check(mismatch.Value == null, "incompatible result is None");
                SimulationTestHarness.Kill(unit);
                t.Equal(unit.id, (int)bridge.Observe(unit.id, "id").Value, "dead ID is retained");
                t.Equal(false, (bool)bridge.Observe(unit.id, "alive").Value, "dead alive is false");
                world.Update(); // Archive the dead unit after removing it from the active registry.
                SimulationCallResult health = bridge.Observe(unit.id, "health");
                t.Equal(SimulationCallStatus.Success, health.Status, "dead property does not warn");
                t.Equal((Fixed)0, (Fixed)health.Value, "dead health is zero");
                t.Equal(SimulationEntityType.Unit, (SimulationEntityType)bridge.Observe(unit.id, "type").Value,
                    "dead entity type remains available");
                t.Equal(SimulationMoveMode.Hold, (SimulationMoveMode)bridge.Observe(unit.id, "move_mode").Value,
                    "dead unit movement is Hold");
                t.Equal(SimulationCallStatus.Success,
                    bridge.Observe(unit.id, "distance_to", new SimulationEntityReference(resource.id)).Status,
                    "distance from archived entity remains valid");
                t.Check(bridge.Observe(unit.id, "enemies").Value == null, "dead receiver cannot discover enemies");
                t.Check(bridge.Order(unit.id, unit.id, "move", 10, 10).Value == null,
                    "dead unit cannot receive orders");
            });

            Add("OrderOverloads", "Position and entity movement use the same public DScript name", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                SBUnit target = SimulationTestHarness.AddUnit(world, 2, 12, 5, 0);
                world.Start();
                var bridge = new SimulationBridge(world);

                t.Equal(true, (bool)bridge.Order(unit.id, unit.id, "move", 9, 7).Value,
                    "position overload accepted");
                t.Equal(SimulationMoveMode.Position, world.Observer.GetMoveMode(unit.id).Value,
                    "position mode");
                t.Equal(true, (bool)bridge.Order(unit.id, unit.id, "move",
                    new SimulationEntityReference(target.id)).Value, "entity overload accepted");
                t.Equal(SimulationMoveMode.Follow, world.Observer.GetMoveMode(unit.id).Value,
                    "follow mode");
                SimulationCallResult invalid = bridge.Order(unit.id, unit.id, "move", target.id);
                t.Equal(SimulationCallStatus.Error, invalid.Status,
                    "ordinary integers cannot masquerade as entity handles");
            });

            Add("Authorization", "No script can order another unit, including an ally", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                SBUnit ally = SimulationTestHarness.AddUnit(world, 1, 10, 5, 0);
                world.Start();
                var bridge = new SimulationBridge(world);
                SimulationCallResult denied = bridge.Order(unit.id, ally.id, "move", 15, 15);
                t.Equal(SimulationCallStatus.Error, denied.Status, "remote order is an error");
                t.Equal(SimulationMoveMode.Hold, world.Observer.GetMoveMode(ally.id).Value,
                    "rejected order did not change target");
                t.Equal(SimulationCallStatus.Warning,
                    bridge.RequestBuild(unit.id, unit.id, out _).Status,
                    "unit cannot issue a nexus build order");
            });

            Add("ManualBuild", "Build is manually dispatched and returns a request for VM binding", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 60, height: 40);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                world.EnqueueCreation(nexus);
                world.Start();
                SetResources(world.GetSide(1), 20);
                var bridge = new SimulationBridge(world);

                t.Equal(SimulationCallStatus.Error,
                    bridge.Order(nexus.id, nexus.id, "build", 123).Status,
                    "automatic path rejects manual build");
                t.Equal(SimulationCallStatus.Success,
                    bridge.RequestBuild(nexus.id, nexus.id, out UnitBuildRequest request).Status,
                    "manual request succeeds");
                t.Equal(1, request.requestId, "accepted request ID");
                t.Equal(10, world.GetSide(1).Resources, "resources deducted on acceptance");
                SimulationCallResult busy = bridge.RequestBuild(nexus.id, nexus.id, out _);
                t.Equal(false, (bool)busy.Value, "busy nexus rejects without error");
                t.Equal(10, world.GetSide(1).Resources, "rejection did not deduct resources");
            });

            Add("ArgumentConversion", "Entity handles and relative enums are strongly distinguished", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                SBUnit enemy = SimulationTestHarness.AddUnit(world, 2, 10, 5, 0);
                world.Start();
                var bridge = new SimulationBridge(world);

                t.Equal(SimulationCallStatus.Error,
                    bridge.Observe(unit.id, "distance_to", enemy.id).Status,
                    "integer cannot replace entity reference");
                t.Equal(SimulationCallStatus.Success,
                    bridge.Observe(unit.id, "distance_to", new SimulationEntityReference(enemy.id)).Status,
                    "typed entity reference accepted");
                t.Equal(SimulationCallStatus.Error,
                    bridge.Observe(unit.id, "entities", 2).Status,
                    "raw numeric side is not a relative-side enum");
                var observed = bridge.Observe(unit.id, "entities", SimulationSideRelation.Enemy);
                t.Equal(1, ((List<SimulationEntityReference>)observed.Value).Count,
                    "relative side enum accepted");
            });
        }
    }
}
