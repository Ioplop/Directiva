using System;
using System.Collections.Generic;
using DMath;
using SSimulation;
using World = SSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    internal static class SimulationCollisionTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "Collisions", name, description, action);

            Add("DistantEntitiesDoNotMove", "Nonoverlapping circles keep their positions", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, 9, 5, 1);
                world.Update(1);
                t.Equal(SimulationTestHarness.P(5, 5), a.Position, "first position");
                t.Equal(SimulationTestHarness.P(9, 5), b.Position, "second position");
            });

            Add("TangentCirclesDoNotMove", "Two circles touching at exactly their radii do not overlap", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, 7, 5, 1);
                world.Update(1);
                t.Equal(SimulationTestHarness.P(5, 5), a.Position, "first tangent");
                t.Equal(SimulationTestHarness.P(7, 5), b.Position, "second tangent");
            });

            Add("EqualPrioritySeparatesSymmetrically", "Equal-priority circles split the overlap in half", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, 6, 5, 1);
                world.Update(1);
                t.Equal(SimulationTestHarness.P(SimulationTestHarness.Raw(45000), 5),
                    a.Position, "first corrected position");
                t.Equal(SimulationTestHarness.P(SimulationTestHarness.Raw(65000), 5),
                    b.Position, "second corrected position");
                t.Equal((Fixed)2, FixedVector2.Distance(a.Position, b.Position),
                    "final separation");
            });

            Add("DifferentRadiiSeparate", "Collision uses sum of radii, not a fixed unit distance", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, 7, 5, 2);
                world.Update(1);
                t.Equal(SimulationTestHarness.P(SimulationTestHarness.Raw(45000), 5),
                    a.Position, "first position");
                t.Equal(SimulationTestHarness.P(SimulationTestHarness.Raw(75000), 5),
                    b.Position, "second position");
                t.Equal((Fixed)3, FixedVector2.Distance(a.Position, b.Position),
                    "sum of radii");
            });

            Add("DisabledCollisionSkipsBothDirections", "A noncolliding entity is ignored in either scan direction", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1, collides: false);
                var b = SimulationTestHarness.Add(world, 6, 5, 1);
                world.Update(1);
                t.Equal(SimulationTestHarness.P(5, 5), a.Position, "noncolliding entity");
                t.Equal(SimulationTestHarness.P(6, 5), b.Position, "other entity");
            });

            Add("DeadEntityNoLongerCollides", "Dead entities never displace surviving entities", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var dead = SimulationTestHarness.Add(world, 5, 5, 1);
                var live = SimulationTestHarness.Add(world, 6, 5, 1);
                world.Start();
                SimulationTestHarness.Kill(dead);
                world.Update(1);
                t.Equal(SimulationTestHarness.P(6, 5), live.Position, "survivor stays put");
                t.Equal(1, SimulationTestHarness.Registry(world).Count, "only survivor remains");
            });

            Add("HigherPriorityMovesFirst", "Current priority convention: larger value yields position", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1, priority: 3);
                var b = SimulationTestHarness.Add(world, 6, 5, 1, priority: 1);
                world.Update(1);
                t.Equal(SimulationTestHarness.P(4, 5), a.Position, "larger priority moves fully");
                t.Equal(SimulationTestHarness.P(6, 5), b.Position, "smaller priority stays");
            });

            Add("HigherPrioritySecondEntityMoves", "Priority branch behaves symmetrically when B has larger value", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1, priority: 1);
                var b = SimulationTestHarness.Add(world, 6, 5, 1, priority: 3);
                world.Update(1);
                t.Equal(SimulationTestHarness.P(5, 5), a.Position, "smaller priority stays");
                t.Equal(SimulationTestHarness.P(7, 5), b.Position, "larger priority moves fully");
            });

            Add("CoincidentEntitiesSeparate", "Identical centers are separated by deterministic cardinal direction", t =>
            {
                World world = SimulationTestHarness.NewWorld(seed: 7);
                var a = SimulationTestHarness.Add(world, 10, 10, 1);
                var b = SimulationTestHarness.Add(world, 10, 10, 1);
                world.Update(1);
                t.Equal((Fixed)4, FixedVector2.SqrDistance(a.Position, b.Position),
                    "squared distance after separating two coincident radius-1 circles");
                SimulationTestHarness.AssertInside(t, a, 21, 21);
                SimulationTestHarness.AssertInside(t, b, 21, 21);
            });

            Add("ZeroRadiusDoesNotDisplace", "Two zero-radius points have no collision overlap", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 0);
                var b = SimulationTestHarness.Add(world, 5, 5, 0);
                world.Update(1);
                t.Equal(SimulationTestHarness.P(5, 5), a.Position, "first zero-radius entity");
                t.Equal(SimulationTestHarness.P(5, 5), b.Position, "second zero-radius entity");
            });

            Add("CollisionAcrossGridBoundary", "Overlaps spanning neighboring spatial cells are detected", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var radius = SimulationTestHarness.Raw(6000);
                var a = SimulationTestHarness.Add(world, SimulationTestHarness.Raw(17000), 5, radius);
                var b = SimulationTestHarness.Add(world, SimulationTestHarness.Raw(23000), 5, radius);
                world.Update(1);
                t.Equal(SimulationTestHarness.P(SimulationTestHarness.Raw(14000), 5),
                    a.Position, "first entity across border");
                t.Equal(SimulationTestHarness.P(SimulationTestHarness.Raw(26000), 5),
                    b.Position, "second entity across border");
            });

            Add("NearBoundaryStaysInside", "Collision correction cannot push entities past world limits", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 1, 1, 1);
                var b = SimulationTestHarness.Add(world, SimulationTestHarness.Raw(15000), 1, 1);
                world.Update(1);
                SimulationTestHarness.AssertInside(t, a, 21, 21);
                SimulationTestHarness.AssertInside(t, b, 21, 21);
                t.Check(b.Position.X > SimulationTestHarness.Raw(15000),
                    "second entity should move away from wall-bound neighbor");
            });

            Add("ChainCollisionRunsMultiplePasses", "A three-entity chain propagates correction beyond first pair", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, SimulationTestHarness.Raw(55000), 5, 1);
                var c = SimulationTestHarness.Add(world, 6, 5, 1);
                world.Update(1);
                t.Check(a.Position.X < 5, "first entity should move left");
                t.Check(b.Position.X != SimulationTestHarness.Raw(55000), "middle should move");
                t.Check(c.Position.X != 6, "last entity should move in subsequent pass");
                SimulationTestHarness.AssertInside(t, a, 21, 21);
                SimulationTestHarness.AssertInside(t, b, 21, 21);
                SimulationTestHarness.AssertInside(t, c, 21, 21);
            });
        }
    }
}
