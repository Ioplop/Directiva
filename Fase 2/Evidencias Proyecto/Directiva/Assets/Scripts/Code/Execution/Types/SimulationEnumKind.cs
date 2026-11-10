namespace DSExecution.DataTypes
{
    /// <summary>
    /// Stable DScript enum families. No dependency on DSimulation CLR enum types;
    /// orchestration translates these values at the boundary.
    /// </summary>
    public enum SimulationEnumKind
    {
        None = 0,
        Side = 1,
        Type = 2,
        MoveMode = 3,
        ActionMode = 4
    }
}
