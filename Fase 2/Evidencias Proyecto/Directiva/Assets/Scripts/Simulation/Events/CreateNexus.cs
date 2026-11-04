using DMath;

namespace DSEvents
{
    /// <summary>
    /// A new nexus was created.
    /// </summary>
    public sealed class EventCreateNexus : Event
    {
        public readonly int id;
        public readonly int side;
        public readonly Fixed maxHealth;
        public readonly Fixed maxShields;
        public readonly FixedVector2 position;

        public EventCreateNexus(int id, int side, Fixed maxHealth, Fixed maxShields, FixedVector2 position)
        {
            this.id = id;
            this.side = side;
            this.maxHealth = maxHealth;
            this.maxShields = maxShields;
            this.position = position;
        }
    }
}