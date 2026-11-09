namespace DSimulation
{
    /// <summary>
    /// Reports that a build request produced a registered unit.
    /// The future orchestrator consumes these results to assign unit VMs.
    /// </summary>
    public readonly struct UnitCreationResult
    {
        public readonly int requestId;
        public readonly int unitId;

        public UnitCreationResult(int requestId, int unitId)
        {
            this.requestId = requestId;
            this.unitId = unitId;
        }
    }
}
