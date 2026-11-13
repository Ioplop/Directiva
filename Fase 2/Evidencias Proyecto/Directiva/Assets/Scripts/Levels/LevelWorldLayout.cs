using System;
using System.Collections.Generic;
using UnityEngine;

namespace Directiva.Levels
{
    public enum LevelMirrorMode { Off, Axis, Point }

    [Serializable]
    public sealed class LevelResourcePlacement
    {
        public Vector2 Position;
        [Min(1)] public int Amount = 100;
        [Min(1)] public int Richness = 1;
        // Pair identity is editor metadata only, never passed to the simulation.
        public int MirrorPairId;

        public LevelResourcePlacement() { }
        public LevelResourcePlacement(Vector2 position, int amount, int richness, int pair = 0)
        {
            Position = position;
            Amount = amount;
            Richness = richness;
            MirrorPairId = pair;
        }
    }

    /// <summary>
    /// Absolute editor coordinates for a level. The simulation always runs in
    /// its own (0,0) -> (Width,Height) coordinate system: subtract Origin on load.
    /// This allows all four editor borders to move independently.
    /// </summary>
    [Serializable]
    public sealed class LevelWorldLayout
    {
        public Vector2 Origin = Vector2.zero;
        [Min(16f)] public float Width = 50f;
        [Min(16f)] public float Height = 50f;
        public int Seed = 20261111;
        [Min(0)] public int InitialResourcesSide1 = 40;
        [Min(0)] public int InitialResourcesSide2 = 40;
        public Vector2 NexusSide1 = new Vector2(6f, 25f);
        public Vector2 NexusSide2 = new Vector2(44f, 25f);
        public List<LevelResourcePlacement> Deposits = CreateDefaultDeposits();

        // Editor-only state, stored with the asset for convenient reopening.
        public LevelMirrorMode MirrorMode = LevelMirrorMode.Off;
        public Vector2 MirrorCenter = new Vector2(25f, 25f);
        public float MirrorAxisDegrees = 90f;

        private static List<LevelResourcePlacement> CreateDefaultDeposits()
        {
            var deposits = new List<LevelResourcePlacement>();
            Vector2[] left = { new Vector2(9, 20), new Vector2(14, 34), new Vector2(19, 16) };
            foreach (Vector2 point in left)
            {
                deposits.Add(new LevelResourcePlacement(point, 100, 1));
                deposits.Add(new LevelResourcePlacement(new Vector2(50 - point.x, 50 - point.y), 100, 1));
            }
            return deposits;
        }

        public bool Contains(Vector2 p, float radius = 0f) =>
            p.x - radius >= Origin.x && p.y - radius >= Origin.y &&
            p.x + radius <= Origin.x + Width && p.y + radius <= Origin.y + Height;
    }

    /// <summary>Editor reflection geometry; no persistent state and no simulated randomness.</summary>
    public static class LevelMirrorMath
    {
        public static Vector2 Reflect(Vector2 position, LevelMirrorMode mode,
            Vector2 center, float axisDegrees)
        {
            Vector2 offset = position - center;
            if (mode == LevelMirrorMode.Point) return center - offset;
            if (mode != LevelMirrorMode.Axis) return position;
            float radians = axisDegrees * Mathf.Deg2Rad;
            Vector2 axis = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            return center + 2f * Vector2.Dot(offset, axis) * axis - offset;
        }
    }
}
