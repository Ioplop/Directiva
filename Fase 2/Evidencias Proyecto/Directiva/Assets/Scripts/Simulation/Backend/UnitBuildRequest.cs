namespace DSimulation
{
    /// <summary>
    /// Identifies an accepted request to construct a unit at a nexus.
    /// The request ID is independent of entity IDs.
    /// </summary>
    public readonly struct UnitBuildRequest
    {
        public readonly int requestId;
        public readonly int nexusId;

        public UnitBuildRequest(int requestId, int nexusId)
        {
            this.requestId = requestId;
            this.nexusId = nexusId;
        }
    }
}
