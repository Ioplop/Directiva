namespace DSExecution.DataTypes
{
    public enum DataTypeId
    {
        // Fixed numbers are used to prevent things breaking if new types are added in the future.

        // Empty types
        Uninitialized = 0,
        None = 1,

        // Value types

        Bool = 2,
        Int = 3,
        Decimal = 4, // Technically not float if we intend to make this deterministic through machines with different CPUs.

        // Heap reference types

        String = 5,
        List = 6,
        Set = 7,
        Dict = 8,

        // Simulation types
        Unit = 9,
        Base = 10,
        Resource = 11,
    }
}
