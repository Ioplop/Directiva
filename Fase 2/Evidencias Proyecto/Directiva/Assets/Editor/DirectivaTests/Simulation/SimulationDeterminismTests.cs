using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using DMath;
using DSimulation;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    internal static class SimulationDeterminismTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "Determinism", name, description, action);

            Add("SameSeedSameCollisions", "Two worlds with same seed and creation order have identical collision results", t =>
            {
                World a = MakeCoincidentWorld(100);
                World b = MakeCoincidentWorld(100);
                for (int i = 0; i < 5; i++)
                {
                    a.Update();
                    b.Update();
                    t.Equal(Snapshot(a), Snapshot(b), "matching world after tick " + (i + 1));
                }
                t.Attach("world_snapshot", Snapshot(a));
            });

            Add("DifferentSeedsPreserveNormalCorrection", "Changing the seed may change the slide side, but not the normal resolution", t =>
            {
                World a = SimulationTestHarness.NewWorld(seed: 1);
                World b = SimulationTestHarness.NewWorld(seed: 999);
                var a1 = SimulationTestHarness.Add(a, 5, 5, 1);
                var a2 = SimulationTestHarness.Add(a, 6, 5, 1);
                var b1 = SimulationTestHarness.Add(b, 5, 5, 1);
                var b2 = SimulationTestHarness.Add(b, 6, 5, 1);
                a.Update();
                b.Update();
                t.Equal(a1.Position.X, b1.Position.X, "first normal displacement");
                t.Equal(a2.Position.X, b2.Position.X, "second normal displacement");
                t.Check(FixedVector2.Distance(a1.Position, a2.Position) >= 2,
                    "first world has no residual overlap");
                t.Check(FixedVector2.Distance(b1.Position, b2.Position) >= 2,
                    "second world has no residual overlap");
            });

            Add("SlideDoesNotAdvanceScriptRandom", "Collision side choices do not consume the simulation's script RNG", t =>
            {
                World contact = SimulationTestHarness.NewWorld(seed: 765);
                World separated = SimulationTestHarness.NewWorld(seed: 765);
                SimulationTestHarness.Add(contact, 5, 5, 1);
                SimulationTestHarness.Add(contact, 6, 5, 1);
                SimulationTestHarness.Add(separated, 5, 5, 1);
                SimulationTestHarness.Add(separated, 9, 5, 1);
                contact.Update();
                separated.Update();
                var field = typeof(World).GetField("random",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                t.Check(field != null, "simulation's seeded RNG field exists");
                if (field == null) return;
                var a = (DeterministicRandom)field.GetValue(contact);
                var b = (DeterministicRandom)field.GetValue(separated);
                t.Equal(a.NextUInt(), b.NextUInt(), "script RNG sequence is unchanged");
            });

            Add("DeterministicCreateDieSequence", "Equal creation and death queues yield the same future state", t =>
            {
                World left = MakeCoincidentWorld(42);
                World right = MakeCoincidentWorld(42);
                left.Start();
                right.Start();
                SimulationTestHarness.Kill(SimulationTestHarness.Entities(left)[1]);
                SimulationTestHarness.Kill(SimulationTestHarness.Entities(right)[1]);
                SimulationTestHarness.Add(left, 8, 8, 1);
                SimulationTestHarness.Add(right, 8, 8, 1);
                for (int i = 0; i < 4; i++)
                {
                    left.Update();
                    right.Update();
                    t.Equal(Snapshot(left), Snapshot(right), "matching state after tick " + (i + 1));
                }
            });

            Add("RepeatedStressScenario", "Repeated crowded simulations produce exactly identical raw fixed-point state", t =>
            {
                World a = MakeCrowdedWorld(321);
                World b = MakeCrowdedWorld(321);
                for (int tick = 0; tick < 8; tick++)
                {
                    a.Update();
                    b.Update();
                    t.Equal(Snapshot(a), Snapshot(b), "state at tick " + tick);
                }
                t.Equal(20, SimulationTestHarness.Entities(a).Count, "entity count");
                foreach (var entity in SimulationTestHarness.Entities(a))
                    SimulationTestHarness.AssertInside(t, entity, 21, 21);
                t.Attach("stress_final_state", Snapshot(a));
            });

            Add("PreciseAccumulation", "Many fixed-length ticks accumulate exactly without float drift", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                for (int i = 0; i < 123; i++)
                    world.Update();
                t.Equal(123L, world.Tick, "tick count");
                t.Equal(SConstants.deltaTime * 123, world.Time, "exact accumulated fixed time");
            });
        }

        private static World MakeCoincidentWorld(ulong seed)
        {
            World world = SimulationTestHarness.NewWorld(seed);
            SimulationTestHarness.Add(world, 5, 5, 1);
            SimulationTestHarness.Add(world, 5, 5, 1);
            SimulationTestHarness.Add(world, 12, 12, 1);
            SimulationTestHarness.Add(world, 12, 12, 1);
            return world;
        }

        private static World MakeCrowdedWorld(ulong seed)
        {
            var source = new DeterministicRandom(881);
            World world = SimulationTestHarness.NewWorld(seed);
            for (int i = 0; i < 20; i++)
            {
                int x = source.NextInt(3, 18);
                int y = source.NextInt(3, 18);
                SimulationTestHarness.Add(world, x, y, SimulationTestHarness.Raw(6500));
            }
            return world;
        }

        private static string Snapshot(World world)
        {
            var builder = new StringBuilder();
            builder.Append("tick=").Append(world.Tick)
                .Append(";timeRaw=").Append(world.Time.RawValue)
                .Append(";count=").Append(SimulationTestHarness.Entities(world).Count);
            foreach (var entity in SimulationTestHarness.Entities(world))
            {
                builder.Append("\n").Append(entity.id).Append(':')
                    .Append(entity.alive ? 'A' : 'D').Append(':')
                    .Append(entity.Position.X.RawValue).Append(',')
                    .Append(entity.Position.Y.RawValue);
            }
            return builder.ToString();
        }
    }
}
