using DMath;

namespace DSEvents
{
    /// <summary>
    /// An entity with given id is at some position.
    /// </summary>
    public sealed class EventPosition : Event
    {
        public readonly int id;
        public readonly FixedVector2 position;

        public EventPosition(int id, FixedVector2 position)
        {
            this.id = id;
            this.position = position;
        }
    }
}