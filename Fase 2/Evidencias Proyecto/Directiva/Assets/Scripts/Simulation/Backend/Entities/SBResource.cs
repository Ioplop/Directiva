using DMath;

namespace DSimulation
{
    /// <summary>
    /// Representa un recurso en la simulacion, que puede ser recolectado por unidades.
    /// Los recursos pueden ser destruidos (idealmente para negar su recoleccion al oponente) o pueden ser recolectados hasta que se agoten.
    /// </summary>
    public class SBResource : SBEntity
    {
        public int Amount { get; protected set; }
        public int InitialAmount { get; }
        public int GatherAmount { get; protected set; }
        public SBUnit GatheringUnit { get; internal set; }

        public SBResource(SBSide side, FixedVector2 position, Fixed rotation, int resources, int richness = 1) : base(
            side, position, rotation,
            SConstants.ResourceRadius, SConstants.ResourcePriority,
            SConstants.ResourceHealth, SConstants.ResourceShields, SConstants.ResourceShieldRegen)
        { 
            Amount = resources;
            InitialAmount = resources;
            GatherAmount = richness;
        }

        /// <summary>
        /// Creates an event that informs the current entity's resources.
        /// </summary>
        internal void NotifyResources()
        {
            simulation.NotifyEvent(new DSEvents.EventResources(id, Amount));
        }

        /// <summary>
        /// Creates an event that informs the current entity's creation as a resource.
        /// </summary>
        internal void NotifyCreateResource()
        {
            simulation.NotifyEvent(new DSEvents.EventCreateResource(id, MaxHealth, Position, Amount, GatherAmount));
        }

        /// <summary>
        /// Called by a unit when it wishes to gather resources from this resource entity.
        /// </summary>
        /// <param name="unit">Unit that wants to gather resources</param>
        /// <returns>true if the unit was able to attach to the resource, false otherwise</returns>
        internal bool TryAttachGatherer(SBUnit unit)
        {
            if (!alive)
                return false;
            if (GatheringUnit == unit)
                return true;
            if (GatheringUnit != null && GatheringUnit.alive)
                return false;
            GatheringUnit = unit;
            return true;
        }

        /// <summary>
        /// Called by the unit when it no longer is gathering from this resource.
        /// </summary>
        internal void DetachGatherer(SBUnit gatherUnit)
        {
            if (GatheringUnit == gatherUnit)
                GatheringUnit = null;
        }

        /// <summary>
        /// Returns the amount of resources gathered, which is the minimum between the resource's gather amount, the remaining amount, and the maximum amount requested.
        /// Does not check if the unit is attached to the resource, as this is handled by the unit.
        /// </summary>
        /// <param name="maxAmount">The maximum amount of resources a unit can take.</param>
        /// <returns>The number of resources gathered.</returns>
        internal int TryGather(int maxAmount)
        {
            if (maxAmount == 0) return 0;
            int gathered = System.Math.Min(GatherAmount, Amount);
            gathered = System.Math.Min(gathered, maxAmount);
            Amount -= gathered;
            NotifyResources();
            if (Amount <= 0)
                Die();
            return gathered;
        }

        /// <summary>
        /// Ensures that when a resource dies, it detaches any unit that was gathering from it.
        /// </summary>
        internal override void Die()
        {
            base.Die();
            DetachGatherer(GatheringUnit);
        }
    }
}