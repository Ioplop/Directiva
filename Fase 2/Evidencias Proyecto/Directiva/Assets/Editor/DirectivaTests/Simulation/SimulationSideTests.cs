using System;
using System.Collections.Generic;
using DMath;
using DSEvents;
using DSimulation;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    internal static class SimulationSideTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "Sides", name, description, action);

            Add("ThreeSides", "Every simulation has neutral, player 1, and player 2 sides", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                for (int i = 0; i < 3; i++)
                {
                    SBSide side = world.GetSide(i);
                    t.Equal(i, side.id, "side ID");
                    t.Equal(0, side.Resources, "starting balance");
                    t.Check(side.Nexus == null, "no initial nexus");
                    t.Equal(0, side.Units.Count, "no initial units");
                    t.Equal(0, side.ResourceEntities.Count, "no initial deposits");
                }
            });

            Add("EntityMembership", "Units and nexuses belong to their player side; deposits are neutral", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit1 = SimulationTestHarness.AddUnit(world, 1, 4, 4, 0);
                SBUnit unit2 = SimulationTestHarness.AddUnit(world, 2, 10, 4, 0);
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(4, 12), 0, 20);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(12, 12));
                world.EnqueueCreation(resource);
                world.EnqueueCreation(nexus);
                world.Start();

                t.Equal(1, world.GetSide(1).Units.Count, "player 1 units");
                t.Check(world.GetSide(1).Units[0] == unit1, "player 1 unit identity");
                t.Check(ReferenceEquals(unit1.side, world.GetSide(1)), "unit side reference");
                t.Equal(1, world.GetSide(2).Units.Count, "player 2 units");
                t.Check(world.GetSide(2).Units[0] == unit2, "player 2 unit identity");
                t.Check(world.GetSide(1).Nexus == nexus, "player 1 nexus");
                t.Check(ReferenceEquals(nexus.side, world.GetSide(1)), "nexus side reference");
                t.Equal(1, world.GetSide(0).ResourceEntities.Count, "neutral resource count");
                t.Check(world.GetSide(0).ResourceEntities[0] == resource, "neutral resource identity");
                t.Check(ReferenceEquals(resource.side, world.GetSide(0)), "resource side reference");

                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(4, events.Count, "four creation events");
                t.Check(events[3] is EventCreateNexus created && created.id == nexus.id &&
                    created.side == 1, "nexus creation event");
            });

            Add("RemovalUpdatesSides", "Removed entities disappear from their corresponding side", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 4, 4, 0);
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(4, 12), 0, 20);
                var nexus = new SBNexus(world.GetSide(2), SimulationTestHarness.P(12, 12));
                world.EnqueueCreation(resource);
                world.EnqueueCreation(nexus);
                world.Start();

                SimulationTestHarness.Kill(unit);
                SimulationTestHarness.Kill(resource);
                SimulationTestHarness.Kill(nexus);
                world.Update();

                t.Equal(0, world.GetSide(1).Units.Count, "unit removed");
                t.Equal(0, world.GetSide(0).ResourceEntities.Count, "resource removed");
                t.Check(world.GetSide(2).Nexus == null, "nexus removed");
            });
        }
    }
}
