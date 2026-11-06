namespace DSEvents
{
    /// <summary>
    /// An event indicating that resources are available at a specific location.
    /// </summary>
    public sealed class EventResources : Event
    {
        public readonly int id;
        public readonly int amount;

        public EventResources(int id, int amount)
        {
            this.id = id;
            this.amount = amount;
        }
    }
}