using System;

namespace Directiva.Progress
{
    /// <summary>
    /// Each objective occupies one bit so a later win can fill a missing star
    /// without granting (or removing) stars belonging to other objectives.
    /// </summary>
    [Flags]
    public enum LevelStarFlags : byte
    {
        None = 0,
        Victory = 1 << 0,
        // Persisted bit 1: no lost Nexus health (damage absorbed by shields is allowed).
        UntouchedNexus = 1 << 1,
        FastVictory = 1 << 2,
        All = Victory | UntouchedNexus | FastVictory
    }
}
