using System;
using System.Collections.Generic;
using DMath;
using DSimulation;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    internal static class SimulationUnitTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "Units", name, description, action);

            Add("ConstructorInitialState", "Unit construction takes side, position and rotation plus current simulation stats", t =>
            {
                var unit = new SBUnit(3, SimulationTestHarness.P(5, 6), 45);
                t.Equal(3, unit.side, "side");
                t.Equal(SimulationTestHarness.P(5, 6), unit.Position, "position");
                t.Equal((Fixed)45, unit.Rotation, "rotation");
                t.Equal(SConstants.UnitRadius, unit.Radius, "radius");
                t.Equal(SConstants.UnitPriority, unit.CollisionPriority, "priority");
                t.Equal(SConstants.UnitHealth, unit.Health, "starting health");
                t.Equal(SConstants.UnitShields, unit.Shields, "starting shields");
                t.Equal(SConstants.UnitShieldRegen, unit.ShieldRegen, "regeneration rate");
                t.Equal(FixedVector2.Zero, SimulationTestHarness.Velocity(unit), "initial velocity");
            });

            Add("MoveAcceleratesWithFixedTimestep", "Moving toward a position accelerates by 5 units/s^2 at 50 Hz", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                world.Start();
                SimulationTestHarness.DrainEvents(world);
                SimulationTestHarness.MoveTo(unit, 15, 5);

                world.Update();
                t.Equal(SimulationTestHarness.P(Fixed.Decimal(5002, 3), 5), unit.Position,
                    "first 0.002 units moved");
                t.Equal(SimulationTestHarness.P(Fixed.Decimal(1, 1), 0),
                    SimulationTestHarness.Velocity(unit), "first velocity 0.1");

                world.Update();
                t.Equal(SimulationTestHarness.P(Fixed.Decimal(5006, 3), 5), unit.Position,
                    "second movement totals 0.006 units");
                t.Equal(SimulationTestHarness.P(Fixed.Decimal(2, 1), 0),
                    SimulationTestHarness.Velocity(unit), "second velocity 0.2");
            });

            Add("HoldBrakesWithoutOvershoot", "Hold reduces velocity to zero without reversing the unit", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                world.Start();
                SimulationTestHarness.MoveTo(unit, 15, 5);
                world.Update();
                var movingPosition = unit.Position;
                SimulationTestHarness.Hold(unit);
                world.Update();
                t.Equal(FixedVector2.Zero, SimulationTestHarness.Velocity(unit), "braked velocity");
                t.Equal(movingPosition, unit.Position, "no reversal or overshoot");
            });

            Add("SpeedClamped", "ApproachVelocity respects the unit maximum speed", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                world.Start();
                SimulationTestHarness.MoveTo(unit, 19, 5);
                for (int i = 0; i < 80; i++)
                {
                    world.Update();
                    t.Check(SimulationTestHarness.Velocity(unit).Magnitude <= 5,
                        "speed limit at tick " + i);
                }
                t.Equal((Fixed)5, SimulationTestHarness.Velocity(unit).Magnitude,
                    "reaches maximum speed on a sufficiently long straight path");
            });

            Add("TurnsTowardTarget", "Position targeting turns and moves the unit on the same tick", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                world.Start();
                SimulationTestHarness.MoveTo(unit, 5, 15);
                world.Update();
                t.Equal(Fixed.Decimal(72, 1), unit.Rotation, "7.2-degree first turn");
                t.Equal(SimulationTestHarness.P(5, Fixed.Decimal(5002, 3)), unit.Position,
                    "motion toward positive Y");
            });

            Add("RotationWrapsByShortestArc", "Rotation from 359 degrees toward zero uses a short positive turn", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 359);
                world.Start();
                SimulationTestHarness.MoveTo(unit, 15, 5);
                world.Update();
                t.Equal(Fixed.Decimal(35972, 2), unit.Rotation,
                    "softened 0.72-degree turn instead of a long clockwise turn");
            });

            Add("RotationSoftensClockwise", "Rotation near the target slows correctly on the clockwise path", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 1);
                world.Start();
                SimulationTestHarness.MoveTo(unit, 15, 5);
                world.Update();
                t.Equal(Fixed.Decimal(28, 2), unit.Rotation,
                    "clockwise 0.72-degree correction leaves 0.28 degrees");
            });

            Add("FollowTargetEntity", "A unit can pursue another registered entity", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var target = SimulationTestHarness.Add(world, 15, 5, 1);
                var unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                world.Start();
                SimulationTestHarness.Follow(unit, target);
                world.Update();
                t.Check(unit.Position.X > 5, "follower closes distance");
                t.Equal((Fixed)5, unit.Position.Y, "follower stays on horizontal axis");
                t.Equal(SimulationTestHarness.P(Fixed.Decimal(1, 1), 0),
                    SimulationTestHarness.Velocity(unit), "follower starts accelerating");
            });

            Add("DeadFollowTargetRevertsToHold", "A dead followed entity causes the follower to brake", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var target = SimulationTestHarness.Add(world, 15, 5, 1);
                var unit = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                world.Start();
                SimulationTestHarness.Follow(unit, target);
                world.Update();
                var before = unit.Position;
                SimulationTestHarness.Kill(target);
                world.Update();
                t.Check(SimulationTestHarness.IsHolding(unit), "mode changes to Hold");
                t.Equal(FixedVector2.Zero, SimulationTestHarness.Velocity(unit), "brakes to rest");
                t.Equal(before, unit.Position, "does not move past the previous position");
            });
        }
    }
}
