using System;
using System.Collections.Generic;
using DMath;
using DSimulation;
using World = DSimulation.Simulation;

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
                world.Update();
                t.Equal(SimulationTestHarness.P(5, 5), a.Position, "first position");
                t.Equal(SimulationTestHarness.P(9, 5), b.Position, "second position");
            });

            Add("TangentCirclesDoNotMove", "Two circles touching at exactly their radii do not overlap", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, 7, 5, 1);
                world.Update();
                t.Equal(SimulationTestHarness.P(5, 5), a.Position, "first tangent");
                t.Equal(SimulationTestHarness.P(7, 5), b.Position, "second tangent");
            });

            Add("EqualPrioritySeparatesSymmetrically", "Equal-priority circles split both normal and tangential corrections", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, 6, 5, 1);
                world.Update();
                t.Equal(SimulationTestHarness.Raw(45000), a.Position.X, "first normal correction");
                t.Equal(SimulationTestHarness.Raw(65000), b.Position.X, "second normal correction");
                t.Check(a.Position.Y != 5 && b.Position.Y != 5, "contact induces a small lateral slip");
                t.Equal((Fixed)10, a.Position.Y + b.Position.Y, "equal priority splits lateral slip");
                t.Check(FixedVector2.Distance(a.Position, b.Position) >= 2,
                    "the correction does not reduce normal separation");
            });

            Add("DifferentRadiiSeparate", "Collision uses sum of radii, not a fixed unit distance", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, 7, 5, 2);
                world.Update();
                t.Equal(SimulationTestHarness.Raw(45000), a.Position.X, "first normal position");
                t.Equal(SimulationTestHarness.Raw(75000), b.Position.X, "second normal position");
                t.Check(FixedVector2.Distance(a.Position, b.Position) >= 3,
                    "sum of radii remains the minimum separation");
            });

            Add("DisabledCollisionSkipsBothDirections", "A noncolliding entity is ignored in either scan direction", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1, collides: false);
                var b = SimulationTestHarness.Add(world, 6, 5, 1);
                world.Update();
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
                world.Update();
                t.Equal(SimulationTestHarness.P(6, 5), live.Position, "survivor stays put");
                t.Equal(1, SimulationTestHarness.Registry(world).Count, "only survivor remains");
            });

            Add("HigherPriorityMovesFirst", "Current priority convention: larger value yields position", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1, priority: 3);
                var b = SimulationTestHarness.Add(world, 6, 5, 1, priority: 1);
                world.Update();
                t.Equal((Fixed)4, a.Position.X, "larger priority takes the normal correction");
                t.Check(a.Position.Y != 5, "larger priority also slides tangentially");
                t.Equal(SimulationTestHarness.P(6, 5), b.Position, "smaller priority stays");
            });

            Add("HigherPrioritySecondEntityMoves", "Priority branch behaves symmetrically when B has larger value", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1, priority: 1);
                var b = SimulationTestHarness.Add(world, 6, 5, 1, priority: 3);
                world.Update();
                t.Equal(SimulationTestHarness.P(5, 5), a.Position, "smaller priority stays");
                t.Equal((Fixed)7, b.Position.X, "larger priority takes the normal correction");
                t.Check(b.Position.Y != 5, "larger priority also slides tangentially");
            });

            Add("CoincidentEntitiesSeparate", "Identical centers are separated by deterministic cardinal direction", t =>
            {
                World world = SimulationTestHarness.NewWorld(seed: 7);
                var a = SimulationTestHarness.Add(world, 10, 10, 1);
                var b = SimulationTestHarness.Add(world, 10, 10, 1);
                world.Update();
                t.Check(FixedVector2.SqrDistance(a.Position, b.Position) >= 4,
                    "coincident radius-1 circles separate despite the additional tangent");
                SimulationTestHarness.AssertInside(t, a, 21, 21);
                SimulationTestHarness.AssertInside(t, b, 21, 21);
            });

            Add("ZeroRadiusDoesNotDisplace", "Two zero-radius points have no collision overlap", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 0);
                var b = SimulationTestHarness.Add(world, 5, 5, 0);
                world.Update();
                t.Equal(SimulationTestHarness.P(5, 5), a.Position, "first zero-radius entity");
                t.Equal(SimulationTestHarness.P(5, 5), b.Position, "second zero-radius entity");
            });

            Add("CollisionAcrossGridBoundary", "Overlaps spanning neighboring spatial cells are detected", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var radius = SimulationTestHarness.Raw(6000);
                var a = SimulationTestHarness.Add(world, SimulationTestHarness.Raw(17000), 5, radius);
                var b = SimulationTestHarness.Add(world, SimulationTestHarness.Raw(23000), 5, radius);
                world.Update();
                t.Equal(SimulationTestHarness.Raw(14000), a.Position.X,
                    "first entity normal correction across grid boundary");
                t.Equal(SimulationTestHarness.Raw(26000), b.Position.X,
                    "second entity normal correction across grid boundary");
                t.Check(FixedVector2.Distance(a.Position, b.Position) >= SimulationTestHarness.Raw(12000),
                    "the pair no longer overlaps across the cell boundary");
            });

            Add("NearBoundaryStaysInside", "Collision correction cannot push entities past world limits", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 1, 1, 1);
                var b = SimulationTestHarness.Add(world, SimulationTestHarness.Raw(15000), 1, 1);
                world.Update();
                SimulationTestHarness.AssertInside(t, a, 21, 21);
                SimulationTestHarness.AssertInside(t, b, 21, 21);
                t.Check(b.Position.X > SimulationTestHarness.Raw(15000),
                    "second entity should move away from wall-bound neighbor");
            });

            Add("SlideDirectionStableAcrossTicks", "The same pair keeps its passing side between contacts", t =>
            {
                World world = SimulationTestHarness.NewWorld(seed: 456);
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, 6, 5, 1);
                world.Update();
                Fixed firstSlip = b.Position.Y - 5;
                t.Check(firstSlip != 0, "first contact has a tangential direction");

                // Recreate the same head-on contact without recreating the IDs.
                // A reroll each tick would sometimes cancel this displacement.
                SimulationTestHarness.Place(a, 5, 5);
                SimulationTestHarness.Place(b, 6, 5);
                world.Update();
                t.Equal(firstSlip, b.Position.Y - 5, "passing side is persistent");
            });

            Add("AlignedUnitsSpreadSideways", "Perfectly aligned overlapping units no longer remain collinear", t =>
            {
                World world = SimulationTestHarness.NewWorld(seed: 11);
                var a = SimulationTestHarness.Add(world, 5, 10, 1);
                var b = SimulationTestHarness.Add(world, 6, 10, 1);
                var c = SimulationTestHarness.Add(world, 7, 10, 1);
                world.Update();
                t.Check(a.Position.Y != 10 || b.Position.Y != 10 || c.Position.Y != 10,
                    "tangential separation breaks the straight line");
                SimulationTestHarness.AssertInside(t, a, 21, 21);
                SimulationTestHarness.AssertInside(t, b, 21, 21);
                SimulationTestHarness.AssertInside(t, c, 21, 21);
            });

            Add("UnitPassesCenteredResource", "A commanded unit can get past a centered stationary obstacle", t =>
            {
                World world = SimulationTestHarness.NewWorld(seed: 19);
                var obstacle = SimulationTestHarness.Add(world, 10, 10, 1, priority: 1);
                var unit = SimulationTestHarness.AddUnit(world, 1, 4, 10, 0);
                world.Start();
                SimulationTestHarness.MoveTo(unit, 19, 10);

                // 400 deterministic 50 Hz ticks give the unit time to slide
                // around the obstacle instead of pushing forever into its center.
                for (int tick = 0; tick < 400; tick++)
                {
                    world.Update();
                    SimulationTestHarness.AssertInside(t, unit, 21, 21);
                    t.Equal(SimulationTestHarness.P(10, 10), obstacle.Position,
                        "stationary obstacle position at tick " + tick);
                }
                t.Check(unit.Position.X > 12, "unit has cleared the centered obstacle");
            });

            Add("NearWallObstacleChoosesOpenSide", "Collision sliding selects the usable passage beside a world boundary", t =>
            {
                // This seed initially favors the lower side. There is no room
                // to pass below the obstacle, so the solver must prefer above.
                World world = SimulationTestHarness.NewWorld(seed: 2);
                var obstacle = SimulationTestHarness.Add(world, 10, 1, 1, priority: 1);
                var unit = SimulationTestHarness.AddUnit(world, 1, 4, 1, 0);
                world.Start();
                SimulationTestHarness.MoveTo(unit, 19, 1);
                for (int tick = 0; tick < 400; tick++)
                    world.Update();
                t.Check(unit.Position.X > 12, "unit passes on the open side of the obstacle");
                t.Check(unit.Position.Y >= unit.Radius, "unit remains inside lower boundary");
                t.Equal(SimulationTestHarness.P(10, 1), obstacle.Position,
                    "obstacle does not move");
            });

            Add("ChainCollisionRunsMultiplePasses", "A three-entity chain propagates correction beyond first pair", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 5, 5, 1);
                var b = SimulationTestHarness.Add(world, SimulationTestHarness.Raw(55000), 5, 1);
                var c = SimulationTestHarness.Add(world, 6, 5, 1);
                world.Update();
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
