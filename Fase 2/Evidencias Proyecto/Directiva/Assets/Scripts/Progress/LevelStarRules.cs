using DMath;
using DOrchestration;

namespace Directiva.Progress
{
    /// <summary>Pure, deterministic evaluation of the three level objectives.</summary>
    public static class LevelStarRules
    {
        /// <param name="playerNexusLostHealth">Whether health (not just shields) was reduced at any point in this match.</param>
        /// <param name="elapsedSimulatedTime">Simulation time, not real-world time or playback speed.</param>
        public static LevelStarFlags Evaluate(MatchOutcome outcome, bool playerNexusLostHealth,
            Fixed elapsedSimulatedTime, int fastVictorySeconds)
        {
            if (outcome != MatchOutcome.Side1Won)
                return LevelStarFlags.None;

            LevelStarFlags result = LevelStarFlags.Victory;
            if (!playerNexusLostHealth)
                result |= LevelStarFlags.UntouchedNexus;
            // "Less than" is strict; a win exactly at the threshold is not fast.
            if (fastVictorySeconds > 0 && elapsedSimulatedTime < (Fixed)fastVictorySeconds)
                result |= LevelStarFlags.FastVictory;
            return result;
        }

        public static int Count(LevelStarFlags stars)
        {
            stars &= LevelStarFlags.All;
            int count = 0;
            if ((stars & LevelStarFlags.Victory) != 0) count++;
            if ((stars & LevelStarFlags.UntouchedNexus) != 0) count++;
            if ((stars & LevelStarFlags.FastVictory) != 0) count++;
            return count;
        }
    }
}
