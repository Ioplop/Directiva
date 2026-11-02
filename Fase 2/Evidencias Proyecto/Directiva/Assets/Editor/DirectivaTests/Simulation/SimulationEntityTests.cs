using System;
using System.Collections.Generic;
using DMath;
using DSimulation;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    internal static class SimulationEntityTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "Entities", name, description, action);

            Add("Defaults", "Entities start alive, unregistered and with default collision settings", t =>
            {
                var entity = new SimulationTestHarness.StubEntity(1);
                t.Equal(0, entity.id, "id before registration");
                t.Check(entity.simulation == null, "simulation should be null");
                t.Check(entity.alive, "entity should start alive");
                t.Equal((Fixed)1, entity.Radius, "radius");
                t.Equal((Fixed)0, entity.Rotation, "rotation");
                t.Equal(FixedVector2.Zero, entity.Position, "position");
                t.Check(entity.Collides, "collides by default");
                t.Equal(0, entity.CollisionPriority, "default collision priority");
                t.Contains("Unregistered", entity.ToString(), "unregistered ToString");
            });

            Add("CustomCollisionSettings", "Derived entities may configure collision properties", t =>
            {
                var entity = new SimulationTestHarness.StubEntity(2, false, 7);
                t.Equal((Fixed)2, entity.Radius, "radius");
                t.Check(!entity.Collides, "collision opt-out");
                t.Equal(7, entity.CollisionPriority, "collision priority");
            });

            Add("RegistrationOrder", "Start assigns monotonic IDs from one in creation order", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 3, 3, 1);
                var b = SimulationTestHarness.Add(world, 7, 7, 1);
                var c = SimulationTestHarness.Add(world, 11, 11, 1);
                t.Equal(0, a.id, "ID remains unset while queued");
                world.Start();
                t.Equal(1, a.id, "first ID");
                t.Equal(2, b.id, "second ID");
                t.Equal(3, c.id, "third ID");
                t.Check(ReferenceEquals(world, a.simulation), "entity owner");
                t.Check(ReferenceEquals(a, SimulationTestHarness.Registry(world)[1]), "registry ID 1");
                t.Check(ReferenceEquals(c, SimulationTestHarness.Entities(world)[2]), "list order");
                t.Contains("StubEntity 1", a.ToString(), "registered ToString");
                t.Equal(0L, world.Tick, "starting tick");
                t.Equal((Fixed)0, world.Time, "starting time");
            });

            Add("StartIdempotent", "Calling Start multiple times does not duplicate entities", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 5, 5, 1);
                world.Start();
                world.Start();
                t.Equal(1, entity.id, "ID after repeated Start");
                t.Equal(1, SimulationTestHarness.Entities(world).Count, "entity count");
                t.Equal(1, SimulationTestHarness.Registry(world).Count, "registry count");
                t.Equal(0L, world.Tick, "tick");
            });

            Add("UpdateAutoStarts", "First Update implicitly starts and registers the simulation", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 5, 5, 1);
                world.Update(1);
                t.Equal(1, entity.id, "registered on first update");
                t.Equal(1L, world.Tick, "tick");
                t.Equal((Fixed)1, world.Time, "time");
            });

            Add("WorldsHaveIndependentIds", "IDs are per simulation, not global", t =>
            {
                World left = SimulationTestHarness.NewWorld(1);
                World right = SimulationTestHarness.NewWorld(2);
                var a = SimulationTestHarness.Add(left, 5, 5, 1);
                var b = SimulationTestHarness.Add(right, 5, 5, 1);
                left.Start();
                right.Start();
                t.Equal(1, a.id, "first world ID");
                t.Equal(1, b.id, "second world ID");
                t.Check(!ReferenceEquals(a.simulation, b.simulation), "worlds remain distinct");
                t.Check(!ReferenceEquals(a, b), "entities remain distinct");
            });

            Add("RejectNullCreation", "Cannot queue a null entity", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                bool rejected = false;
                try { world.EnqueueCreation(null); }
                catch (ArgumentNullException) { rejected = true; }
                t.Check(rejected, "EnqueueCreation(null) must throw ArgumentNullException");
            });

            Add("RejectDuplicatePendingCreation", "Cannot queue the same entity twice", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 5, 5, 1);
                bool rejected = false;
                try { world.EnqueueCreation(entity); }
                catch (InvalidOperationException) { rejected = true; }
                t.Check(rejected, "duplicate pending entity must be rejected");
                world.Start();
                t.Equal(1, SimulationTestHarness.Registry(world).Count, "only one registration");
            });

            Add("RejectAlreadyRegisteredCreation", "Cannot re-enqueue a registered entity", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 5, 5, 1);
                world.Start();
                bool rejected = false;
                try { world.EnqueueCreation(entity); }
                catch (InvalidOperationException) { rejected = true; }
                t.Check(rejected, "registered entity should not be re-enqueued");
            });

            Add("RejectCrossWorldRegisteredCreation", "Registered entities cannot migrate to a second world", t =>
            {
                World a = SimulationTestHarness.NewWorld();
                World b = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(a, 5, 5, 1);
                a.Start();
                bool rejected = false;
                try { b.EnqueueCreation(entity); }
                catch (InvalidOperationException) { rejected = true; }
                t.Check(rejected, "cross-world registration must fail");
                t.Equal(0, SimulationTestHarness.Registry(b).Count, "second registry remains empty");
            });

            Add("DeathIsIdempotent", "Dying twice leaves entity dead and queues removal only once", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 5, 5, 1);
                world.Start();
                SimulationTestHarness.Kill(entity);
                SimulationTestHarness.Kill(entity);
                t.Check(!entity.alive, "entity must be dead");
                t.Check(SimulationTestHarness.Registry(world).ContainsKey(entity.id),
                    "removal must be deferred until Update");
                world.Update(1);
                t.Check(!SimulationTestHarness.Registry(world).ContainsKey(entity.id),
                    "registry should no longer contain dead entity");
                t.Equal(0, SimulationTestHarness.Entities(world).Count, "list after removal");
            });

            Add("DeathRemovalIsDeferred", "Die marks alive false immediately, removes on next tick", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 5, 5, 1);
                world.Start();
                SimulationTestHarness.Kill(entity);
                t.Check(!entity.alive, "alive flag must change immediately");
                t.Equal(1, SimulationTestHarness.Entities(world).Count, "still in list before tick");
                world.Update(1);
                t.Equal(0, SimulationTestHarness.Entities(world).Count, "removed after tick");
                world.Update(1);
                t.Equal(0, SimulationTestHarness.Entities(world).Count, "no repeated removal");
            });

            Add("IdsNeverReused", "New entities receive new IDs after older IDs are removed", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var first = SimulationTestHarness.Add(world, 3, 3, 1);
                var second = SimulationTestHarness.Add(world, 7, 7, 1);
                world.Start();
                SimulationTestHarness.Kill(first);
                world.Update(1);
                var third = SimulationTestHarness.Add(world, 11, 11, 1);
                world.Update(1);
                t.Equal(1, first.id, "first ID preserved on dead object");
                t.Equal(2, second.id, "second ID preserved");
                t.Equal(3, third.id, "new ID must be monotonic");
                t.Check(!SimulationTestHarness.Registry(world).ContainsKey(1), "old ID absent");
                t.Check(SimulationTestHarness.Registry(world).ContainsKey(3), "new ID present");
            });

            Add("CreationAfterStartIsDeferred", "Creation after Start registers at the end of Update", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                world.Start();
                var entity = SimulationTestHarness.Add(world, 5, 5, 1);
                t.Equal(0, entity.id, "pending entity has no ID");
                t.Equal(0, SimulationTestHarness.Registry(world).Count, "pending entity not registered");
                world.Update(1);
                t.Equal(1, entity.id, "registered during tick");
                t.Equal(1, SimulationTestHarness.Registry(world).Count, "registry updated");
            });

            Add("CreationAndRemovalSameTick", "Removal and creation queues are both consumed on Update", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var oldEntity = SimulationTestHarness.Add(world, 5, 5, 1);
                world.Start();
                SimulationTestHarness.Kill(oldEntity);
                var newEntity = SimulationTestHarness.Add(world, 10, 10, 1);
                world.Update(1);
                t.Equal(2, newEntity.id, "new ID");
                t.Equal(1, SimulationTestHarness.Registry(world).Count, "registry size");
                t.Check(!SimulationTestHarness.Registry(world).ContainsKey(1), "dead ID removed");
                t.Check(ReferenceEquals(newEntity, SimulationTestHarness.Entities(world)[0]),
                    "entity list now contains new entity");
            });

            Add("UpdateTimeAndTicks", "Time accumulates Fixed deltas while Tick increments by one", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                world.Update(1);
                world.Update(SimulationTestHarness.Raw(5000));
                world.Update(SimulationTestHarness.Raw(2500));
                t.Equal(3L, world.Tick, "three ticks");
                t.Equal(SimulationTestHarness.Raw(17500), world.Time, "1 + 0.5 + 0.25");
            });

            Add("IndependentClocks", "Simulations have independent Tick and Time counters", t =>
            {
                World left = SimulationTestHarness.NewWorld();
                World right = SimulationTestHarness.NewWorld();
                left.Update(1);
                left.Update(1);
                right.Update(SimulationTestHarness.Raw(5000));
                t.Equal(2L, left.Tick, "left tick");
                t.Equal(1L, right.Tick, "right tick");
                t.Equal((Fixed)2, left.Time, "left time");
                t.Equal(SimulationTestHarness.Raw(5000), right.Time, "right time");
            });
        }
    }
}
