using DMath;

namespace DSEvents
{
    /// <summary>
    /// A new resource was created
    /// </summary>
    public sealed class CreateResource : Event
    {
        public readonly int id;
        public readonly Fixed maxHealth;
        public readonly FixedVector2 position;

        public CreateResource(int id, Fixed maxHealth, FixedVector2 position)
        {
            this.id = id;
            this.maxHealth = maxHealth;
            this.position = position;
        }
    }
}