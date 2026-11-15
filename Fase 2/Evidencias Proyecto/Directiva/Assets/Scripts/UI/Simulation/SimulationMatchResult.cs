using System;
using DMath;
using DOrchestration;
using Directiva.Progress;

namespace Directiva.SimulationUI
{
    /// <summary>Statistics attributable to one side of a finished match.</summary>
    public readonly struct SimulationSideMatchResult
    {
        public long ResourcesGathered { get; }
        public long Errors { get; }
        public int UnitsBuilt { get; }
        public int EntitiesDestroyed { get; }
        public long EffectiveDamageRaw { get; }
        public long AcceptedUnitOrders { get; }

        public SimulationSideMatchResult(long resourcesGathered, long errors,
            int unitsBuilt, int entitiesDestroyed, long effectiveDamageRaw,
            long acceptedUnitOrders)
        {
            ResourcesGathered = resourcesGathered;
            Errors = errors;
            UnitsBuilt = unitsBuilt;
            EntitiesDestroyed = entitiesDestroyed;
            EffectiveDamageRaw = effectiveDamageRaw;
            AcceptedUnitOrders = acceptedUnitOrders;
        }

        public double EffectiveDamage => (double)EffectiveDamageRaw / Constants.FixedScale;

        public double OrdersPerSecond(double elapsedSeconds) => elapsedSeconds > 0d
            ? AcceptedUnitOrders / elapsedSeconds : 0d;
    }

    /// <summary>
    /// Finished-match summary for both sides. Stars and outcome remain relative to
    /// the blue player (side 1); elapsed simulation time belongs to both sides.
    /// </summary>
    public readonly struct SimulationMatchResult
    {
        public MatchOutcome Outcome { get; }
        public LevelStarFlags EarnedStars { get; }
        public int FastVictorySeconds { get; }
        public SimulationSideMatchResult Blue { get; }
        public SimulationSideMatchResult Red { get; }
        public double ElapsedSeconds { get; }

        public SimulationMatchResult(MatchOutcome outcome, LevelStarFlags earnedStars,
            int fastVictorySeconds, SimulationSideMatchResult blue,
            SimulationSideMatchResult red, double elapsedSeconds)
        {
            Outcome = outcome;
            EarnedStars = earnedStars;
            FastVictorySeconds = fastVictorySeconds;
            Blue = blue;
            Red = red;
            ElapsedSeconds = Math.Max(0d, elapsedSeconds);
        }

        // Preserve existing callers that construct a side-1-only result directly.
        public SimulationMatchResult(MatchOutcome outcome, LevelStarFlags earnedStars,
            int fastVictorySeconds, long resourcesGathered, long errors,
            int unitsBuilt, int entitiesDestroyed, long effectiveDamageRaw,
            double elapsedSeconds, long acceptedUnitOrders)
            : this(outcome, earnedStars, fastVictorySeconds,
                new SimulationSideMatchResult(resourcesGathered, errors, unitsBuilt,
                    entitiesDestroyed, effectiveDamageRaw, acceptedUnitOrders),
                default, elapsedSeconds)
        {
        }

        // Legacy side-1 aliases; the results panel uses Blue and Red explicitly.
        public long ResourcesGathered => Blue.ResourcesGathered;
        public long Errors => Blue.Errors;
        public int UnitsBuilt => Blue.UnitsBuilt;
        public int EntitiesDestroyed => Blue.EntitiesDestroyed;
        public long EffectiveDamageRaw => Blue.EffectiveDamageRaw;
        public long AcceptedUnitOrders => Blue.AcceptedUnitOrders;
        public double EffectiveDamage => Blue.EffectiveDamage;
        public double UnitOrdersPerSecond => Blue.OrdersPerSecond(ElapsedSeconds);
    }
}
