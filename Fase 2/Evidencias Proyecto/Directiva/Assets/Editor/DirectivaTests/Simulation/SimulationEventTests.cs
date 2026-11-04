using System;
using System.Collections.Generic;
using DMath;
using DSEvents;
using DSimulation;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    internal static class SimulationEventTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "Events", name, description, action);

            Add("EmptyQueue", "TryConsumeEvent returns false when there are no events", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                t.Check(!world.TryConsumeEvent(out Event e), "empty queue should not yield an event");
                t.Check(e == null, "empty dequeue should output null");
            });

            Add("UnitCreatedOnRegistration", "A registered unit emits its complete initial visual state", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var unit = SimulationTestHarness.AddUnit(world, 3, 5, 6, 90);
                t.Equal(0, SimulationTestHarness.DrainEvents(world).Count,
                    "queueing an unregistered unit emits nothing");
                world.Start();

                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(1, events.Count, "one creation event");
                var created = Require<EventCreateUnit>(t, events, 0, "creation");
                t.Equal(unit.id, created.id, "unit ID");
                t.Equal(3, created.side, "side");
                t.Equal(unit.MaxHealth, created.maxHealth, "maximum health");
                t.Equal(unit.MaxShields, created.maxShields, "maximum shields");
                t.Equal(unit.Position, created.position, "initial position");
                t.Equal(unit.Rotation, created.rotation, "initial rotation");
            });

            Add("AdvancePerFixedTick", "Each update begins with the previous Tick and Time in EventAdvance", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                world.Update();
                world.Update();
                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(2, events.Count, "two advances and no other events");
                var first = Require<EventAdvance>(t, events, 0, "first advance");
                var second = Require<EventAdvance>(t, events, 1, "second advance");
                t.Equal(0L, first.tick, "initial tick");
                t.Equal((Fixed)0, first.time, "initial time");
                t.Equal(1L, second.tick, "next tick");
                t.Equal(SConstants.deltaTime, second.time, "next time");
                t.Equal(2L, world.Tick, "simulation tick afterward");
                t.Equal(SConstants.deltaTime * 2, world.Time, "simulation time afterward");
            });

            Add("CreationDoesNotRepeatTransform", "The initial unit transform is not resent without a change", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SimulationTestHarness.AddUnit(world, 2, 7, 8, 45);
                world.Start();
                t.Equal(1, SimulationTestHarness.DrainEvents(world).Count, "unit creation");
                world.Update();
                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(1, events.Count, "no redundant position or rotation");
                Require<EventAdvance>(t, events, 0, "only advance");
            });

            Add("BoundsProduceFinalPosition", "Clamping a moved entity is reported once with its final position", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, -5, -6, 1);
                world.Update();
                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(2, events.Count, "advance and final position");
                Require<EventAdvance>(t, events, 0, "advance");
                var moved = Require<EventPosition>(t, events, 1, "clamped position");
                t.Equal(entity.id, moved.id, "moved ID");
                t.Equal(SimulationTestHarness.P(1, 1), moved.position, "final clamped position");
            });

            Add("PositionAndRotationIndependent", "Each changed transform component emits once and only when changed", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 5, 5, 1);
                world.Start();
                SimulationTestHarness.Place(entity, 7, 5);
                entity.RotateTo(450);
                world.Update();

                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(3, events.Count, "advance, position, rotation");
                Require<EventAdvance>(t, events, 0, "advance");
                var position = Require<EventPosition>(t, events, 1, "position");
                var rotation = Require<EventRotation>(t, events, 2, "rotation");
                t.Equal(entity.id, position.id, "position owner");
                t.Equal(entity.id, rotation.id, "rotation owner");
                t.Equal(entity.Position, position.position, "final position");
                t.Equal((Fixed)90, rotation.rotation, "normalized rotation");

                world.Update();
                var unchanged = SimulationTestHarness.DrainEvents(world);
                t.Equal(1, unchanged.Count, "no repeated transform notifications");
                Require<EventAdvance>(t, unchanged, 0, "only next advance");
            });

            Add("CollisionEmitsOnePositionPerEntity", "Multiple collision corrections publish only one final position per entity", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, SimulationTestHarness.Raw(55000), 5, 1);
                var c = SimulationTestHarness.Add(world, 6, 5, 1);
                world.Update();

                var events = SimulationTestHarness.DrainEvents(world);
                var positions = new Dictionary<int, FixedVector2>();
                int advances = 0;
                foreach (Event e in events)
                {
                    if (e is EventAdvance)
                        advances++;
                    else if (e is EventPosition p)
                    {
                        t.Check(!positions.ContainsKey(p.id), "duplicate position for ID " + p.id);
                        positions.Add(p.id, p.position);
                    }
                    else
                        t.Check(false, "unexpected event " + e.GetType().Name);
                }
                t.Equal(1, advances, "one tick marker");
                t.Equal(3, positions.Count, "all three displaced entities reported");
                t.Equal(a.Position, positions[a.id], "first final position");
                t.Equal(b.Position, positions[b.id], "second final position");
                t.Equal(c.Position, positions[c.id], "third final position");
            });

            Add("DeathFlushesTransform", "Pending movement and rotation are emitted before death exactly once", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 5, 5, 1);
                world.Start();
                SimulationTestHarness.Place(entity, 8, 7);
                entity.RotateTo(-90);
                SimulationTestHarness.Kill(entity);
                SimulationTestHarness.Kill(entity);

                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(3, events.Count, "position, rotation, one death");
                var position = Require<EventPosition>(t, events, 0, "position before death");
                var rotation = Require<EventRotation>(t, events, 1, "rotation before death");
                var death = Require<EventDeath>(t, events, 2, "death last");
                t.Equal(SimulationTestHarness.P(8, 7), position.position, "last position");
                t.Equal((Fixed)270, rotation.rotation, "last normalized rotation");
                t.Equal(entity.id, death.id, "dead entity ID");

                world.Update();
                var after = SimulationTestHarness.DrainEvents(world);
                t.Equal(1, after.Count, "dead entity has no subsequent transform events");
                Require<EventAdvance>(t, after, 0, "next tick");
                t.Equal(0, SimulationTestHarness.Registry(world).Count, "removal applied");
            });

            Add("ShieldOnlyDamage", "Damage absorbed by shields does not emit health events", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.AddWithStats(world, 5, 5, 1, 10, 5, 0);
                world.Start();
                entity.ApplyDamage(2);
                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(1, events.Count, "shield update only");
                var shield = Require<EventShields>(t, events, 0, "shield change");
                t.Equal(entity.id, shield.id, "entity ID");
                t.Equal((Fixed)3, shield.shields, "remaining shields");
                t.Equal((Fixed)10, entity.Health, "health intact");
            });

            Add("DamageCrossesShieldBoundary", "Damage can emit shields then health in that order", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.AddWithStats(world, 5, 5, 1, 10, 3, 0);
                world.Start();
                entity.ApplyDamage(5);
                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(2, events.Count, "shield and health updates");
                var shields = Require<EventShields>(t, events, 0, "shields first");
                var health = Require<EventHealth>(t, events, 1, "health second");
                t.Equal((Fixed)0, shields.shields, "depleted shields");
                t.Equal((Fixed)8, health.health, "remaining health");
                t.Check(entity.alive, "survives damage");
            });

            Add("LethalDamageEventOrder", "Lethal damage publishes health before death and queues removal", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.AddWithStats(world, 5, 5, 1, 10, 3, 0);
                world.Start();
                entity.ApplyDamage(14);
                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(3, events.Count, "shields, health, death");
                Require<EventShields>(t, events, 0, "shields first");
                var health = Require<EventHealth>(t, events, 1, "health before death");
                var death = Require<EventDeath>(t, events, 2, "death last");
                t.Check(health.health <= 0, "lethal health");
                t.Equal(entity.id, death.id, "dead ID");
                t.Check(!entity.alive, "dead immediately");
                world.Update();
                t.Check(!SimulationTestHarness.Registry(world).ContainsKey(entity.id),
                    "removed at end of next update");
            });

            Add("NegativeDamageHealsHealth", "Negative damage heals health only and clamps it at maximum", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.AddWithStats(world, 5, 5, 1, 10, 3, 0);
                world.Start();
                entity.ApplyDamage(4);
                SimulationTestHarness.DrainEvents(world);
                t.Equal((Fixed)9, entity.Health, "health after damage");
                entity.ApplyDamage(-2);
                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(1, events.Count, "healing changes health only");
                var health = Require<EventHealth>(t, events, 0, "health healing");
                t.Equal((Fixed)10, health.health, "capped health");
                t.Equal((Fixed)0, entity.Shields, "shields not healed");
            });

            Add("ShieldsRegenerateAfterCooldown", "Fixed tick time governs shield regeneration and its event", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.AddWithStats(world, 5, 5, 1, 10, 10,
                    Fixed.Decimal(25, 1));
                world.Start();
                entity.ApplyDamage(1);
                SimulationTestHarness.DrainEvents(world);

                for (int i = 0; i < 500; i++)
                    world.Update();
                t.Equal((Fixed)10, world.Time, "500 fixed ticks equal ten seconds");
                t.Equal((Fixed)9, entity.Shields, "cooldown prevents early regeneration");
                var cooldownEvents = SimulationTestHarness.DrainEvents(world);
                t.Equal(500, cooldownEvents.Count, "advance markers only during cooldown");

                world.Update();
                t.Equal(Fixed.Decimal(905, 2), entity.Shields, "one tick of 2.5 shields/second");
                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(2, events.Count, "advance then regeneration");
                Require<EventAdvance>(t, events, 0, "advance");
                var shield = Require<EventShields>(t, events, 1, "regenerated shields");
                t.Equal(entity.Shields, shield.shields, "reported shield amount");
            });
        }

        private static T Require<T>(DirectivaTestContext t, List<Event> events,
            int index, string what) where T : Event
        {
            t.Check(index < events.Count, what + " missing");
            t.Check(events[index] is T, what + " should be " + typeof(T).Name);
            return (T)events[index];
        }
    }
}
