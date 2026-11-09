namespace DSEvents
{
    /// <summary>
    /// A unit with given id has dropped all of its cargo.
    /// </summary>
    public sealed class EventDropCargo : Event
    {
        public readonly int unitId;

        public EventDropCargo(int unitId)
        {
            this.unitId = unitId;
        }
    }
}
