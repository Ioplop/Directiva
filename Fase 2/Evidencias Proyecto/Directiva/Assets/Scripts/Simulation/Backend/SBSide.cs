using System.Collections.Generic;

namespace DSimulation
{
    /// <summary>
    /// Holds the state and entities belonging to a side. Side 0 is neutral; 1 and 2 are players.
    /// </summary>
    public sealed class SBSide
    {
        public int id { get; }
        internal SimulationParameters Settings { get; }
        public SBNexus Nexus { get; internal set; }
        public int Resources { get; internal set; }

        internal readonly List<SBUnit> units = new();
        internal readonly List<SBResource> resourceEntities = new();

        public IReadOnlyList<SBUnit> Units => units;
        public IReadOnlyList<SBResource> ResourceEntities => resourceEntities;

        internal SBSide(int id, SimulationParameters settings)
        {
            this.id = id;
            Settings = settings;
        }
    }
}
