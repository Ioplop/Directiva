using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DMath;
using DSimulation;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    internal static class SimulationObserverTests
    {
        private static void SetResources(SBSide side, int amount)
        {
            typeof(SBSide).GetProperty(nameof(SBSide.Resources)).SetValue(side, amount);
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "Observations", name, description, action);

            Add("IdentityAndDeadEntities", "Only id and alive can be observed after death", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                world.Start();
                SimulationObserver observer = world.Observer;

                t.Equal(unit.id, observer.GetId(unit.id), "living ID");
                t.Check(observer.IsAlive(unit.id), "living entity");
                t.Equal((SimulationEntityType?)SimulationEntityType.Unit, observer.GetEntityType(unit.id), "type");
                t.Equal((Fixed?)SConstants.UnitHealth, observer.GetHealth(unit.id), "health");

                SimulationTestHarness.Kill(unit);
                world.Update(); // Remove the entity from the registry.

                t.Equal(unit.id, observer.GetId(unit.id), "removed entity keeps its identifier");
                t.Check(!observer.IsAlive(unit.id), "removed entity is dead");
                t.Check(!observer.GetEntityType(unit.id).HasValue, "no type retained");
                t.Check(!observer.GetHealth(unit.id).HasValue, "no health retained");
                t.Check(!observer.GetPosition(unit.id).HasValue, "no position retained");
                t.Check(!observer.GetResources(unit.id).HasValue, "no side resources retained");
                t.Check(observer.GetEnemies(unit.id) == null, "no entity queries after death");
                t.Check(!observer.GetTime(unit.id).HasValue, "no global query after death");
                t.Check(!observer.GetTick(unit.id).HasValue, "no world tick after death");
                t.Check(!observer.GetUnitCost(unit.id).HasValue, "no rules after death");
                t.Check(!observer.IsAlive(987654), "unknown entity is not alive");
            });

            Add("NeutralRelationships", "Neutrality always wins; allies and enemies are relative", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var ally = SimulationTestHarness.AddUnit(world, 1, 3, 3, 0);
                var fellow = SimulationTestHarness.AddUnit(world, 1, 8, 3, 0);
                var enemy = SimulationTestHarness.AddUnit(world, 2, 14, 3, 0);
                var deposit = new SBResource(world.GetSide(0), SimulationTestHarness.P(3, 12), 0, 10);
                var otherDeposit = new SBResource(world.GetSide(0), SimulationTestHarness.P(8, 12), 0, 10);
                world.EnqueueCreation(deposit);
                world.EnqueueCreation(otherDeposit);
                world.Start();
                var o = world.Observer;

                t.Equal((SimulationSideRelation?)SimulationSideRelation.Ally, o.GetRelation(ally.id, fellow.id), "same team");
                t.Equal((SimulationSideRelation?)SimulationSideRelation.Enemy, o.GetRelation(ally.id, enemy.id), "opponent");
                t.Equal((SimulationSideRelation?)SimulationSideRelation.Enemy, o.GetRelation(enemy.id, ally.id), "reverse perspective");
                t.Equal((SimulationSideRelation?)SimulationSideRelation.Neutral, o.GetRelation(ally.id, deposit.id), "neutral target");
                t.Equal((SimulationSideRelation?)SimulationSideRelation.Neutral, o.GetRelation(deposit.id, enemy.id), "neutral observer");
                t.Equal((SimulationSideRelation?)SimulationSideRelation.Neutral, o.GetRelation(deposit.id, otherDeposit.id), "two neutrals");
                t.Equal((bool?)true, o.IsAlly(ally.id, fellow.id), "ally helper");
                t.Equal((bool?)true, o.IsEnemy(ally.id, enemy.id), "enemy helper");
                t.Equal((bool?)true, o.IsNeutral(deposit.id, ally.id), "neutral helper");
                t.Check(o.GetEnemies(deposit.id) == null,
                    "neutral resources cannot use side-relative enemies()");
                // Geometric lookups without a side filter now accept neutral receivers.
                // The closest-resource observation excludes the caller itself.
                t.Equal((int?)otherDeposit.id, o.GetClosestResource(deposit.id),
                    "neutral resource can find its closest other resource");
                t.Equal((int?)otherDeposit.id,
                    o.GetClosestEntityByType(deposit.id, SimulationEntityType.Resource),
                    "generic type-only lookup works from a neutral resource");
                t.Equal(2, o.GetResourceNodes(deposit.id).Count,
                    "resource nodes are a world observation");
            });

            Add("ListsAreSnapshots", "Lists have stable registration order and never expose simulation collections", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 50, height: 50);
                var self = SimulationTestHarness.AddUnit(world, 1, 3, 3, 0);
                var firstEnemy = SimulationTestHarness.AddUnit(world, 2, 20, 3, 0);
                var friend = SimulationTestHarness.AddUnit(world, 1, 9, 3, 0);
                var secondEnemy = SimulationTestHarness.AddUnit(world, 2, 24, 3, 0);
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(20, 20), 0, 30);
                var enemyNexus = new SBNexus(world.GetSide(2), SimulationTestHarness.P(40, 40));
                world.EnqueueCreation(resource);
                world.EnqueueCreation(enemyNexus);
                world.Start();
                var o = world.Observer;

                List<int> enemies = o.GetEnemies(self.id);
                t.Equal(3, enemies.Count, "two enemy units and one nexus");
                t.Equal(firstEnemy.id, enemies[0], "first enemy in registration order");
                t.Equal(secondEnemy.id, enemies[1], "second enemy in registration order");
                t.Equal(enemyNexus.id, enemies[2], "enemy nexus in registration order");
                t.Equal((int?)enemyNexus.id, o.GetNexus(firstEnemy.id), "enemy perspective sees enemy nexus");
                enemies.Clear();
                t.Equal(3, o.GetEnemies(self.id).Count, "a modified snapshot does not affect the world");
                t.Equal(1, o.GetAllies(self.id).Count, "self excluded from allies");
                t.Equal(friend.id, o.GetAllies(self.id)[0], "ally identity");
                t.Equal(2, o.GetUnits(self.id).Count, "self included in units");
                t.Equal(self.id, o.GetUnits(self.id)[0], "self comes first");
                t.Equal(resource.id, o.GetResourceNodes(self.id)[0], "resource list");
                t.Equal(2, o.GetEnemies(firstEnemy.id).Count, "enemy perspective includes our units");
                t.Equal((int?)firstEnemy.id, o.GetClosestEnemy(self.id), "nearest enemy");
                t.Equal(1, o.GetEnemies(self.id, SimulationEntityType.Nexus).Count, "type filter");
                t.Equal(2, o.GetEntities(self.id, SimulationSideRelation.Enemy, SimulationEntityType.Unit).Count, "generic relation and type");
                t.Equal(1, o.GetEntitiesInRange(self.id, 18, SimulationSideRelation.Enemy).Count, "range filter");
                t.Equal((int?)enemyNexus.id, o.GetEnemyNexus(self.id), "enemy nexus lookup");
                t.Equal((int?)3, o.CountEnemies(self.id), "enemy count");
                t.Equal((int?)1, o.CountAllies(self.id), "ally count");
            });

            Add("ClosestTieAndResource", "Nearest results break ties using entity registration order", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var self = SimulationTestHarness.AddUnit(world, 1, 10, 10, 0);
                var a = SimulationTestHarness.AddUnit(world, 2, 15, 10, 0);
                SimulationTestHarness.AddUnit(world, 2, 10, 15, 0);
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(10, 12), 0, 10);
                world.EnqueueCreation(resource);
                world.Start();

                t.Equal((int?)a.id, world.Observer.GetClosestEnemy(self.id), "stable enemy tie");
                t.Equal((int?)resource.id, world.Observer.GetClosestResource(self.id), "closest resource");
            });

            Add("FlexibleEntityFilters", "Side and type filters are independently optional, including from neutral resources", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 80, height: 80);
                var self = SimulationTestHarness.AddUnit(world, 1, 10, 10, 0);
                var ally = SimulationTestHarness.AddUnit(world, 1, 12, 10, 0);
                var enemy = SimulationTestHarness.AddUnit(world, 2, 16, 10, 0);
                var friendlyNexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(6, 20));
                var enemyNexus = new SBNexus(world.GetSide(2), SimulationTestHarness.P(20, 20));
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(24, 20), 0, 10);
                world.EnqueueCreation(friendlyNexus);
                world.EnqueueCreation(enemyNexus);
                world.EnqueueCreation(resource);
                world.Start();
                SimulationObserver o = world.Observer;

                // No filter, type-only, side-only and combined filters.
                t.Equal((int?)ally.id, o.GetClosestEntityByType(self.id), "nearest any type");
                t.Equal((int?)friendlyNexus.id,
                    o.GetClosestEntityByType(self.id, SimulationEntityType.Nexus), "nearest nexus");
                t.Equal((int?)enemy.id,
                    o.GetClosestEntity(self.id, SimulationSideRelation.Enemy), "nearest enemy");
                t.Equal((int?)enemyNexus.id,
                    o.GetClosestEntity(self.id, SimulationSideRelation.Enemy, SimulationEntityType.Nexus),
                    "nearest enemy nexus");

                // A neutral resource can compare distances without a side affiliation.
                t.Equal((int?)enemyNexus.id,
                    o.GetClosestEntityByType(resource.id, SimulationEntityType.Nexus),
                    "closest nexus from a neutral resource");
                t.Equal((int?)enemyNexus.id, o.GetClosestEntityByType(resource.id),
                    "closest entity from a neutral resource");
                t.Check(!o.GetClosestEntity(resource.id, SimulationSideRelation.Enemy).HasValue,
                    "neutral resources have no relative enemy side");

                t.Equal(6, o.GetEntitiesByType(self.id).Count, "no filter includes every living entity");
                t.Equal(2, o.GetEntitiesByType(self.id, SimulationEntityType.Nexus).Count,
                    "type-only list across both sides");
                t.Equal(2, o.GetEntities(self.id, SimulationSideRelation.Enemy).Count,
                    "side-only list");
                t.Equal(1, o.GetEntities(self.id, SimulationSideRelation.Enemy, SimulationEntityType.Unit).Count,
                    "combined list filters");
                t.Equal(2, o.GetEntitiesByType(resource.id, SimulationEntityType.Nexus).Count,
                    "type-only list from resource");

                t.Equal(2, o.GetEntitiesInRangeByType(self.id, 5).Count,
                    "range without filters includes receiver and nearby ally");
                t.Equal(0, o.GetEntitiesInRangeByType(self.id, 5, SimulationEntityType.Nexus).Count,
                    "type-only range excludes distant nexuses");
                t.Equal(1, o.GetEntitiesInRange(self.id, 12, SimulationSideRelation.Enemy,
                    SimulationEntityType.Unit).Count, "combined range filters");
                t.Equal(1, o.GetEntitiesInRangeByType(resource.id, 5,
                    SimulationEntityType.Nexus).Count, "nexus in range of neutral resource");
                t.Equal(2, o.GetEntitiesInRangeByType(resource.id, 5).Count,
                    "unfiltered neutral range includes resource itself");
                t.Equal(0, o.GetEntitiesInRangeByType(resource.id, -1).Count,
                    "negative radius yields empty results");
                t.Equal((int?)null, o.GetClosestEntityByType(-9999),
                    "unknown receiver cannot search");
                t.Check(o.GetEntitiesByType(-9999) == null,
                    "unknown receiver has no list observations");
                t.Equal(0L, world.Tick, "queries do not advance the simulation");
            });

            Add("GeometryAndReach", "Geometry uses fixed-point distances and separate attack/gather ranges", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var unit = SimulationTestHarness.AddUnit(world, 1, 10, 10, 0);
                var enemy = SimulationTestHarness.AddUnit(world, 2, 13, 14, 0);
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(12, 10), 0, 10);
                world.EnqueueCreation(resource);
                world.Start();
                var o = world.Observer;

                t.Equal((Fixed?)25, o.GetDistanceSquaredTo(unit.id, enemy.id), "Pythagorean squared distance");
                t.Equal((Fixed?)5, o.GetDistanceTo(unit.id, enemy.id), "Pythagorean distance");
                t.Equal((Fixed?)4, o.GetSurfaceDistanceTo(unit.id, enemy.id), "surface separation subtracts radii");
                t.Equal((bool?)true, o.IsWithinDistance(unit.id, enemy.id, 5), "inclusive range");
                t.Equal((bool?)false, o.IsWithinDistance(unit.id, enemy.id, 4), "out of range");
                t.Equal((bool?)false, o.IsInAttackRange(unit.id, enemy.id), "attack range uses centers");
                t.Equal((bool?)false, o.IsInGatherRange(unit.id, resource.id), "gather margin is shorter");
                SimulationTestHarness.Place(resource, 11, 10);
                t.Equal((bool?)true, o.IsInGatherRange(unit.id, resource.id), "gathering in reach");
                t.Check(!o.IsInGatherRange(unit.id, enemy.id).HasValue, "gather requires resource target");
                t.Equal((FixedVector2?)FixedVector2.Right, o.GetForward(unit.id), "forward from rotation");
            });

            Add("NexusCombatState", "Observer exposes the nexus lock and attack cooldown without changing combat", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var enemy = SimulationTestHarness.AddUnit(world, 2, 15, 10, 0);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                world.EnqueueCreation(nexus);
                world.Start();
                var o = world.Observer;

                t.Equal((Fixed?)SConstants.NexusRange, o.GetAttackRange(nexus.id), "attack range");
                t.Equal((Fixed?)SConstants.NexusDamage, o.GetAttackDamage(nexus.id), "attack damage");
                t.Equal((Fixed?)SConstants.NexusAttackSpeed, o.GetAttackCooldown(nexus.id), "attack interval");
                t.Equal((bool?)true, o.IsAttackReady(nexus.id), "ready immediately after creation");
                t.Check(!o.GetAttackTarget(nexus.id).HasValue, "no initial target lock");
                world.Update();
                t.Equal((int?)enemy.id, o.GetAttackTarget(nexus.id), "locked enemy");
                t.Equal((bool?)false, o.IsAttackReady(nexus.id), "shot started cooldown");
                t.Equal((Fixed?)(SConstants.NexusAttackSpeed - SConstants.deltaTime),
                    o.GetAttackCooldownRemaining(nexus.id), "cooldown uses current simulation time");
            });

            Add("UnitStateAndUnsupportedTypes", "Unit-only queries return null for incompatible entities", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(20, 20));
                world.EnqueueCreation(nexus);
                world.Start();
                var o = world.Observer;

                t.Equal((SimulationMoveMode?)SimulationMoveMode.Hold, o.GetMoveMode(unit.id), "initial movement mode");
                t.Check(!o.GetMoveTarget(unit.id).HasValue, "hold has no movement target");
                SimulationTestHarness.MoveTo(unit, 8, 7);
                var position = o.GetMoveTarget(unit.id);
                t.Equal((SimulationMoveTargetKind?)SimulationMoveTargetKind.Position, position?.Kind, "position target kind");
                t.Equal((FixedVector2?)SimulationTestHarness.P(8, 7), position?.Position, "ordered position");
                SimulationTestHarness.Follow(unit, nexus);
                var target = o.GetMoveTarget(unit.id);
                t.Equal((SimulationMoveTargetKind?)SimulationMoveTargetKind.Entity, target?.Kind, "follow target kind");
                t.Equal((int?)nexus.id, target?.EntityId, "follow target ID");
                t.Equal((bool?)true, o.HasMoveOrder(unit.id), "movement order exists");
                SimulationTestHarness.Hold(unit);
                t.Equal((bool?)false, o.HasMoveOrder(unit.id), "hold cancels movement order");
                t.Equal((SimulationActionMode?)SimulationActionMode.Hold, o.GetActionMode(unit.id), "action mode starts at hold");
                t.Equal((bool?)false, o.IsGathering(unit.id), "unit is not gathering");
                t.Equal((int?)0, o.GetCargo(unit.id), "empty cargo");
                t.Equal((int?)15, o.GetCargoCapacity(unit.id), "cargo capacity");
                t.Equal((Fixed?)5, o.GetMaxSpeed(unit.id), "base movement speed");
                t.Check(!o.GetMoveMode(nexus.id).HasValue, "nexus cannot report unit move mode");
                t.Check(!o.GetCargo(nexus.id).HasValue, "nexus cannot report cargo");
            });

            Add("ResourceDataAndReservations", "Resource amounts and gatherer reservations can be observed", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var unit = SimulationTestHarness.AddUnit(world, 1, 4, 4, 0);
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(6, 6), 0, 17, 3);
                world.EnqueueCreation(resource);
                world.Start();
                var o = world.Observer;

                t.Equal((int?)17, o.GetAmount(resource.id), "remaining resources");
                t.Equal((int?)3, o.GetRichness(resource.id), "gather per extraction");
                t.Check(!o.GetGatherer(resource.id).HasValue, "initial gatherer");
                t.Equal((bool?)true, o.IsAvailableToGather(resource.id), "initially available");
                typeof(SBResource).GetProperty(nameof(SBResource.GatheringUnit)).SetValue(resource, unit);
                t.Equal((int?)unit.id, o.GetGatherer(resource.id), "reserved by unit");
                t.Equal((bool?)false, o.IsAvailableToGather(resource.id), "reservation blocks gathering");
                t.Check(!o.GetAmount(unit.id).HasValue, "unit is not a resource deposit");
            });

            Add("BuildAndEconomy", "Nexus build state and team resources reflect accepted requests", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 60, height: 40);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                world.EnqueueCreation(nexus);
                world.Start();
                SetResources(world.GetSide(1), 25);
                var o = world.Observer;

                t.Equal((int?)25, o.GetResources(nexus.id), "side balance");
                t.Equal((bool?)true, o.CanBuild(nexus.id), "available construction");
                t.Equal((bool?)false, o.IsBuilding(nexus.id), "idle at start");
                t.Check(!o.GetBuildProgress(nexus.id).HasValue, "no active build progress");
                t.Equal((int?)SConstants.unitCost, o.GetUnitCost(nexus.id), "unit cost");
                t.Equal((Fixed?)SConstants.unitBuildTime, o.GetUnitBuildTime(nexus.id), "unit production time");
                t.Check(world.TryRequestUnitBuild(nexus.id, out UnitBuildRequest request), "accepted build");
                t.Equal((int?)15, o.GetResources(nexus.id), "resource cost deducted");
                t.Equal((bool?)true, o.IsBuilding(nexus.id), "nexus building");
                t.Equal((bool?)false, o.CanBuild(nexus.id), "busy nexus cannot build");
                t.Equal((int?)request.requestId, o.GetCurrentBuildRequestId(nexus.id), "request ID");
                t.Equal((Fixed?)0, o.GetBuildProgress(nexus.id), "initial progress");
                t.Equal((Fixed?)10, o.GetBuildTimeRemaining(nexus.id), "initial remaining time");
                world.Update();
                t.Equal((Fixed?)SConstants.deltaTime, o.GetBuildProgress(nexus.id), "progress after one tick");
                t.Equal((Fixed?)(SConstants.unitBuildTime - SConstants.deltaTime),
                    o.GetBuildTimeRemaining(nexus.id), "remaining time after one tick");
            });

            Add("WorldRulesAndReadOnly", "World rules are observed without changing time, balances or event queues", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 20);
                var unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                world.Start();
                SimulationTestHarness.DrainEvents(world);
                var o = world.Observer;

                t.Equal((Fixed?)0, o.GetTime(unit.id), "initial time");
                t.Equal((long?)0, o.GetTick(unit.id), "initial tick");
                t.Equal((Fixed?)SConstants.deltaTime, o.GetDeltaTime(unit.id), "fixed delta");
                t.Equal((FixedVector2?)SimulationTestHarness.P(40, 20), o.GetWorldSize(unit.id), "world dimensions");
                t.Equal((FixedVector2?)SimulationTestHarness.P(20, 10), o.GetWorldCenter(unit.id), "world center");
                t.Equal((bool?)true, o.IsInsideWorld(unit.id, 0, 20), "boundary inclusive");
                t.Equal((bool?)false, o.IsInsideWorld(unit.id, -1, 10), "outside world");
                o.GetResourceNodes(unit.id);
                o.GetEnemies(unit.id);
                o.GetHealth(unit.id);
                o.GetCargo(unit.id);
                t.Equal(0, SimulationTestHarness.DrainEvents(world).Count, "observations emit no events");
                t.Equal(0L, world.Tick, "observations do not advance time");
                t.Equal((Fixed)0, world.Time, "observations do not advance simulation");
            });

            Add("DiscoverableAnnotations", "Each observation has a unique public signature and typed receiver", t =>
            {
                var signatures = new HashSet<string>();
                int found = 0;
                foreach (MethodInfo method in typeof(SimulationObserver).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    var annotation = method.GetCustomAttribute<SimulationObservationAttribute>();
                    t.Check(annotation != null, method.Name + " has observation metadata");
                    ParameterInfo[] parameters = method.GetParameters();
                    string signature = annotation.Name + "(" + string.Join(",",
                        parameters.Skip(1).Select(a => a.ParameterType.FullName +
                            (a.IsOptional ? "?" : string.Empty))) + ")";
                    t.Check(signatures.Add(signature), "unique public DScript signature: " + signature);
                    t.Check(parameters.Length > 0 && parameters[0].ParameterType == typeof(int),
                        method.Name + " has an entity ID receiver");
                    if (annotation.AllowsDeadReceiver)
                        t.Check(annotation.Name == "id" || annotation.Name == "alive", "dead receiver whitelist");
                    found++;
                }
                t.Check(found >= 70, "complete observation catalog: " + found);
                t.Check(typeof(SimulationObserver).GetMethod(nameof(SimulationObserver.GetEnemies)) != null,
                    "enemies entry present");
            });
        }
    }
}
