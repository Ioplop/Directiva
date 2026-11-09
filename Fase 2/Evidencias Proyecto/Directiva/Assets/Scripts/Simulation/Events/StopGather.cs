namespace DSEvents
{
    /// <summary>
    /// An entity with given id has stopped gathering.
    /// </summary>
    public sealed class EventStopGather : Event
    {
        public readonly int gethererId;

        public EventStopGather(int gethererId)
        {
            this.gethererId = gethererId;
        }
    }
}