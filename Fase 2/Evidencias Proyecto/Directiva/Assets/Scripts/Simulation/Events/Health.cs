using DMath;

namespace DSEvents
{
    /// <summary>
    /// An entity with given id has some amount of health.
    /// </summary>
    public sealed class EventHealth : Event
    {
        public readonly int id;
        public readonly Fixed health;

        public EventHealth(int id, Fixed health)
        {
            this.id = id;
            this.health = health;
        }
    }
}