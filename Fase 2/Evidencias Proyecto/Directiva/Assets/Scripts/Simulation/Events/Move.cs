using DMath;

namespace DSEvents
{
    /// <summary>
    /// An entity with given id moving to some position and rotation.
    /// </summary>
    public sealed class EventMove : Event
    {
        public readonly int id;
        public readonly FixedVector2 position;
        public readonly Fixed rotation;

        public EventMove(int id)
        {
            this.id = id;
        }
    }
}