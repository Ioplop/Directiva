namespace DSEvents
{
    /// <summary>
    /// An entity attacks another.
    /// </summary>
    public sealed class EventAttack : Event
    {
        public readonly int attackerId;
        public readonly int targetId;

        public EventAttack(int attackerId, int targetId)
        {
            this.attackerId = attackerId;
            this.targetId = targetId;
        }
    }
}