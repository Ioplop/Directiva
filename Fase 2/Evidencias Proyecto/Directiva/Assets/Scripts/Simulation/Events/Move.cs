using DMath;

namespace DSEvents
{
    /// <summary>
    /// An entity with given id is at some position and rotation.
    /// </summary>
    public sealed class EventPosition : Event
    {
        public readonly int id;
        public readonly FixedVector2 position;
        public readonly Fixed rotation;

        public EventPosition(int id, FixedVector2 position, Fixed rotation)
        {
            this.id = id;
            this.position = position;
            this.rotation = rotation;
        }
    }
}