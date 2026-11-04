using DMath;

namespace DSEvents
{
    /// <summary>
    /// The simulation has advanced to the next physics step.
    /// </summary>
    public sealed class EventAdvance : Event
    {
        public readonly long tick;
        public readonly Fixed time;

        public EventAdvance(Fixed time, long tick)
        {
            this.tick = tick;
            this.time = time;
        }
    }
}