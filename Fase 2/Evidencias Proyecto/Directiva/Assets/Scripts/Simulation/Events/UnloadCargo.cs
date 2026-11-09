namespace DSEvents
{
    /// <summary>
    /// A unit with given id has unloaded all of its cargo into a nexus with given id.
    /// </summary>
    public sealed class EventUnloadCargo : Event
    {
        public readonly int unitId;
        public readonly int nexusId;

        public EventUnloadCargo(int unitId, int nexusId)
        {
            this.unitId = unitId;
            this.nexusId = nexusId;
        }
    }
}
