using DMath;

namespace DSEvents
{
    /// <summary>
    /// An entity with given id has some amount of health and shields.
    /// </summary>
    public sealed class EventHealth : Event
    {
        public readonly int id;
        public readonly Fixed health;
        public readonly Fixed shields;

        public EventHealth(int id, Fixed health, Fixed shields)
        {
            this.id = id;
            this.health = health;
            this.shields = shields;
        }
    }
}