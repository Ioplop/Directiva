using DMath;

namespace DSEvents
{
    /// <summary>
    /// A new resource was created
    /// </summary>
    public sealed class EventCreateResource : Event
    {
        public readonly int id;
        public readonly Fixed maxHealth;
        public readonly FixedVector2 position;
        public readonly int amount;
        public readonly int richness;

        public EventCreateResource(int id, Fixed maxHealth, FixedVector2 position, int amount, int richness)
        {
            this.id = id;
            this.maxHealth = maxHealth;
            this.position = position;
            this.amount = amount;
            this.richness = richness;
        }
    }
}