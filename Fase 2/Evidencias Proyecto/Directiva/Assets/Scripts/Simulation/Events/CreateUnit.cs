using DMath;

namespace DSEvents
{
    /// <summary>
    /// A new unit was created.
    /// </summary>
    public sealed class EventCreateUnit : Event
    {
        public readonly int id;
        public readonly int side;
        public readonly Fixed maxHealth;
        public readonly Fixed maxShields;
        public readonly FixedVector2 position;
        public readonly Fixed rotation;

        public EventCreateUnit(int id, int side, Fixed maxHealth, Fixed maxShields, FixedVector2 position, Fixed rotation)
        {
            this.id = id;
            this.side = side;
            this.maxHealth = maxHealth;
            this.maxShields = maxShields;
            this.position = position;
            this.rotation = rotation;
        }
    }
}