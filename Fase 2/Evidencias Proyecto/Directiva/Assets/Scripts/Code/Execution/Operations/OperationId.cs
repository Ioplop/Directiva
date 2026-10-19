namespace DSExecution.Operations

{

    /// <summary>

    /// Listado de posibles operaciones.

    /// </summary>

    public enum OperationId

    {

        // Ids explícitos para preservar estabilidad de código compilado si en el futuro se agregan o reordenan operaciones.

        // Arithmetic

        Add = 1,

        Subtract = 2,

        Multiply = 3,

        Divide = 4,

        FloorDivide = 5,

        Modulo = 6,

        Power = 7, // Not yet sure if I want this. Suffice to say, this is a continued operation rather than an instant one.



        // Comparators

        ValueEquals = 8,

        ReferenceEquals = 9,

        GreaterThan = 10,

        GreaterOrEqual = 11,

        LessThan = 12,

        LessOrEqual = 13,



        // Boolean

        Not = 14,



        // Container operations (Like lists, sets, dicts, strings, etc)

        Copy = 15,

        Append = 16,

        Insert = 17,

        Delete = 18,

        Pop = 19,

        // I am not entirely sure, but I think that there is only really one way to get and set in lists, strings, sets and dicts, thus there is no

        // need to implement get/set by index and by other type of value.

        Get = 20,

        Set = 21,



        // Identity and hashing

        Hash = 22,



        // Truthiness

        IsTruthy = 23,



        // Logical number of elements (or Unicode scalar values for strings).

        Length = 24,



        // Mutable sequences and linear search. Existing IDs never change.

        Extend = 25,

        Contains = 26,

        IndexOf = 27,



        // Hash-backed mutable sets. Existing serialized ids remain unchanged.

        SetAdd = 28,

        SetDiscard = 29,

        SetRemove = 30,

        SetPop = 31,

        SetUnion = 32,

        SetIntersection = 33,

        SetDifference = 34,

        SetSymmetricDifference = 35,

        SetUpdate = 36,

        SetIsSubset = 37,

        SetIsSuperset = 38,

        SetIsDisjoint = 39,

        // Hash-based key/value dictionaries. Older ids must remain stable.
        DictUpdate = 40,
        DictKeys = 41,
        DictValues = 42,
        DictItems = 43,
        DictClear = 44,
        DictGetDefault = 45,

    }

}