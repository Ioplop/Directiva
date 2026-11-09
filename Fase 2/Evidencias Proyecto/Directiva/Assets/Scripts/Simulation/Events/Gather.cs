namespace DSEvents
{
    /// <summary>
    /// An entity with given id has started gathering from a resource with given id.
    /// </summary>
    public sealed class EventGather : Event
    {
        public readonly int gethererId;
        public readonly int resourceId;

        public EventGather(int gethererId, int resourceId)
        {
            this.gethererId = gethererId;
            this.resourceId = resourceId;
        }
    }
}