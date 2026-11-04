namespace DSEvents
{
    /// <summary>
    /// An entity with given id dying.
    /// </summary>
    public sealed class EventDeath : Event
    {
        public readonly int id;

        public EventDeath(int id)
        {
            this.id = id;
        }
    }
}