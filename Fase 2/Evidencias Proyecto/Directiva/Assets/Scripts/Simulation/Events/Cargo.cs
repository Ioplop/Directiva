namespace DSEvents
{
    /// <summary>
    /// An entity with given id has some amount of cargo.
    /// </summary>
    public sealed class EventCargo : Event
    {
        public readonly int entityId;
        public readonly int cargo;

        public EventCargo(int entityId, int cargo)
        {
            this.entityId = entityId;
            this.cargo = cargo;
        }
    }
}
