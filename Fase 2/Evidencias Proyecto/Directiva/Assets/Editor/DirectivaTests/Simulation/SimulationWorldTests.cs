using System;
using System.Collections.Generic;
using DMath;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    internal static class SimulationWorldTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "World", name, description, action);

            Add("GridDimensionsExactMultiples", "20x20 units at scale 2 must allocate 10x10 cells", t =>
            {
                var world = SimulationTestHarness.NewWorld(width: 20, height: 20);
                var cells = SimulationTestHarness.Cells(world);
                t.Equal(10, cells.GetLength(0), "grid width");
                t.Equal(10, cells.GetLength(1), "grid height");
            });

            Add("GridDimensionsPartialCells", "Non-multiples of grid scale are rounded upward", t =>
            {
                var world = SimulationTestHarness.NewWorld(width: 11, height: 7);
                var cells = SimulationTestHarness.Cells(world);
                t.Equal(6, cells.GetLength(0), "ceil(11/2)");
                t.Equal(4, cells.GetLength(1), "ceil(7/2)");
            });

            Add("GridDimensionsSmallWorld", "A positive world smaller than a grid cell still has a cell", t =>
            {
                var world = SimulationTestHarness.NewWorld(width: 1, height: 1);
                var cells = SimulationTestHarness.Cells(world);
                t.Equal(1, cells.GetLength(0), "grid width");
                t.Equal(1, cells.GetLength(1), "grid height");
            });

            Add("GridCellsInitialized", "Every allocated grid cell has a distinct list", t =>
            {
                var cells = SimulationTestHarness.Cells(SimulationTestHarness.NewWorld(width: 5, height: 5));
                t.Equal(3, cells.GetLength(0), "X cells");
                t.Equal(3, cells.GetLength(1), "Y cells");
                for (int x = 0; x < 3; x++)
                    for (int y = 0; y < 3; y++)
                    {
                        t.Check(cells[x, y] != null, "cell must be initialized");
                        t.Equal(0, cells[x, y].Count, "initial occupancy");
                        if (x != 0 || y != 0)
                            t.Check(!ReferenceEquals(cells[0, 0], cells[x, y]),
                                "different coordinates must have independent lists");
                    }
            });

            Add("InitialGridOccupancy", "Entity overlapping four grid cells appears once in each", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 4, 4, SimulationTestHarness.Raw(5000));
                world.Start();
                var cells = SimulationTestHarness.Cells(world);
                t.Equal(4, SimulationTestHarness.CountCellReferences(world, entity), "occupied cells");
                t.Check(cells[1, 1].Contains(entity), "bottom left");
                t.Check(cells[1, 2].Contains(entity), "top left");
                t.Check(cells[2, 1].Contains(entity), "bottom right");
                t.Check(cells[2, 2].Contains(entity), "top right");
            });

            Add("CellRefreshWhenPositionChanges", "Worldspace indices follow entity movement across grid cells", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 3, 3, SimulationTestHarness.Raw(2500));
                world.Start();
                var cells = SimulationTestHarness.Cells(world);
                t.Check(cells[1, 1].Contains(entity), "initial cell");
                SimulationTestHarness.Place(entity, 9, 9);
                world.Update();
                t.Check(!cells[1, 1].Contains(entity), "old cell cleared");
                t.Check(cells[4, 4].Contains(entity), "new cell populated");
                t.Equal(1, SimulationTestHarness.CountCellReferences(world, entity), "single cell occupancy");
            });

            Add("ClampNegativeCoordinates", "Update clamps entities away from left and bottom walls", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, -5, -8, 1);
                world.Update();
                t.Equal(SimulationTestHarness.P(1, 1), entity.Position, "clamped position");
                SimulationTestHarness.AssertInside(t, entity, 21, 21);
            });

            Add("ClampUpperCoordinates", "Update clamps entities away from right and top walls", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 30, 40, 1);
                world.Update();
                t.Equal(SimulationTestHarness.P(20, 20), entity.Position, "clamped position");
                SimulationTestHarness.AssertInside(t, entity, 21, 21);
            });

            Add("ClampFractionalRadius", "Boundary clamping preserves fixed-point precision", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var quarter = SimulationTestHarness.Raw(2500);
                var entity = SimulationTestHarness.Add(world, 0, 0, quarter);
                world.Update();
                t.Equal(SimulationTestHarness.P(quarter, quarter), entity.Position, "0.25 radius clamp");
                SimulationTestHarness.AssertInside(t, entity, 21, 21);
            });

            Add("LeaveValidPositionUntouched", "In-bounds entities are not moved unless colliding", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 9, 13, 1);
                world.Update();
                t.Equal(SimulationTestHarness.P(9, 13), entity.Position, "no unwanted movement");
            });

            Add("ExactBoundaryIsValid", "Entities touching a boundary are not moved", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var a = SimulationTestHarness.Add(world, 1, 1, 1);
                var b = SimulationTestHarness.Add(world, 20, 20, 1);
                world.Update();
                t.Equal(SimulationTestHarness.P(1, 1), a.Position, "lower boundary");
                t.Equal(SimulationTestHarness.P(20, 20), b.Position, "upper boundary");
            });

            Add("LastCellContainsEdgeEntity", "Upper boundary entities remain indexed in the last grid cell", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.Add(world, 20, 20, 1);
                world.Start();
                var cells = SimulationTestHarness.Cells(world);
                t.Check(cells[10, 10].Contains(entity), "edge entity indexed");
                t.Check(SimulationTestHarness.CountCellReferences(world, entity) > 0,
                    "edge entity is represented in worldspace");
            });
        }
    }
}
