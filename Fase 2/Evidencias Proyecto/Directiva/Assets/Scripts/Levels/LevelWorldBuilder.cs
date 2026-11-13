using System;
using DMath;
using DSimulation;
using UnityEngine;

namespace Directiva.Levels
{
    /// <summary>
    /// The level asset is the single authority for initial entities and simulation
    /// rules. UI and tests call this same method; nothing comes from scene defaults.
    /// </summary>
    public static class LevelWorldBuilder
    {
        public static Simulation Build(DirectivaLevel level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            LevelWorldLayout layout = level.World;
            SimulationParameters settings = level.CreateSimulationParameters();
            if (layout.Width < 16f || layout.Height < 16f ||
                float.IsNaN(layout.Width) || float.IsNaN(layout.Height) ||
                float.IsInfinity(layout.Width) || float.IsInfinity(layout.Height))
                throw new InvalidOperationException("Level world must have finite dimensions of at least 16 x 16.");
            // The collision grid has one List per ~2x2 world cell. Reject worlds
            // that could allocate millions of cells from an accidental Inspector edit.
            if ((double)layout.Width * layout.Height > 4000000d)
                throw new InvalidOperationException("Level world is too large for the spatial grid.");
            if (layout.InitialResourcesSide1 < 0 || layout.InitialResourcesSide2 < 0)
                throw new InvalidOperationException("Initial side resources must not be negative.");
            ValidatePosition(layout, layout.NexusSide1, settings.NexusRadius, "Side 1 Nexus");
            ValidatePosition(layout, layout.NexusSide2, settings.NexusRadius, "Side 2 Nexus");
            if (layout.Deposits == null)
                throw new InvalidOperationException("Level has no deposit list.");
            for (int i = 0; i < layout.Deposits.Count; i++)
            {
                LevelResourcePlacement deposit = layout.Deposits[i];
                if (deposit == null || deposit.Amount <= 0 || deposit.Richness <= 0)
                    throw new InvalidOperationException("Invalid resource deposit #" + (i + 1));
                ValidatePosition(layout, deposit.Position, settings.ResourceRadius,
                    "Resource #" + (i + 1));
            }

            var sim = new Simulation(unchecked((ulong)(uint)layout.Seed),
                new FixedVector2(LevelSimulationSettings.ToFixed(layout.Width),
                    LevelSimulationSettings.ToFixed(layout.Height)), settings);
            sim.GetSide(1).Resources = layout.InitialResourcesSide1;
            sim.GetSide(2).Resources = layout.InitialResourcesSide2;
            sim.EnqueueCreation(new SBNexus(sim.GetSide(1), Position(layout, layout.NexusSide1)));
            sim.EnqueueCreation(new SBNexus(sim.GetSide(2), Position(layout, layout.NexusSide2)));
            foreach (LevelResourcePlacement deposit in layout.Deposits)
                sim.EnqueueCreation(new SBResource(sim.GetSide(0),
                    Position(layout, deposit.Position), 0, deposit.Amount, deposit.Richness));
            return sim;
        }

        public static void ValidatePosition(LevelWorldLayout layout, Vector2 position,
            Fixed radius, string name)
        {
            if (float.IsNaN(position.x) || float.IsNaN(position.y) ||
                float.IsInfinity(position.x) || float.IsInfinity(position.y))
                throw new InvalidOperationException(name + " has invalid coordinates: " + position);
            // Compare quantized Fixed coordinates, exactly as the backend will.
            FixedVector2 local = Position(layout, position);
            Fixed width = LevelSimulationSettings.ToFixed(layout.Width);
            Fixed height = LevelSimulationSettings.ToFixed(layout.Height);
            if (local.X - radius < 0 || local.Y - radius < 0 ||
                local.X + radius > width || local.Y + radius > height)
                throw new InvalidOperationException(name + " is outside safe world bounds: " + position);
        }

        public static FixedVector2 Position(LevelWorldLayout layout, Vector2 absolute)
        {
            Vector2 local = absolute - layout.Origin;
            return new FixedVector2(LevelSimulationSettings.ToFixed(local.x),
                LevelSimulationSettings.ToFixed(local.y));
        }
    }
}
