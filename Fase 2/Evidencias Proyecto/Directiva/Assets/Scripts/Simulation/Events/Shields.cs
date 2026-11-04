using DMath;

namespace DSEvents
{
    /// <summary>
    /// An entity with given id has some amount of shields.
    /// </summary>
    public sealed class EventShields : Event
    {
        public readonly int id;
        public readonly Fixed shields;

        public EventShields(int id, Fixed shields)
        {
            this.id = id;
            this.shields = shields;
        }
    }
}