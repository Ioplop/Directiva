using DMath;

namespace DSEvents
{
    /// <summary>
    /// An entity with given id has some rotation.
    /// </summary>
    public sealed class EventRotation : Event
    {
        public readonly int id;
        public readonly Fixed rotation;

        public EventRotation(int id, Fixed rotation)
        {
            this.id = id;
            this.rotation = rotation;
        }
    }
}