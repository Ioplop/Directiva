using System;

namespace DSExecution.DataTypes
{
    /// <summary>The three distinct forms of a DScript member invocation.</summary>
    public enum DScriptMemberKind { Method, Getter, Setter }

    /// <summary>
    /// Exposes a C# method to DScript. One count means exact arity; -1 means
    /// any non-negative arity. The two-count overload permits an inclusive range.
    /// The VM validates arity before calling the annotated handler.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class DScriptMethodAttribute : Attribute
    {
        public string Name { get; }
        // Compatibility: exact counts have their previous value; -1 denotes a
        // variadic or ranged method. Use min/max for validation.
        public int ArgumentCount { get; }
        public int MinimumArgumentCount { get; }
        public int MaximumArgumentCount { get; } // -1 means no maximum.

        public DScriptMethodAttribute(string name, int argumentCount = -1)
        {
            Name = name;
            ArgumentCount = argumentCount;
            MinimumArgumentCount = argumentCount == -1 ? 0 : argumentCount;
            MaximumArgumentCount = argumentCount;
        }

        public DScriptMethodAttribute(string name, int minimumArgumentCount, int maximumArgumentCount)
        {
            Name = name;
            ArgumentCount = -1;
            MinimumArgumentCount = minimumArgumentCount;
            MaximumArgumentCount = maximumArgumentCount;
        }
    }

    /// <summary>Exposes a C# method as a read accessor; the receiver is its only argument.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class DScriptGetterAttribute : Attribute
    {
        public string Name { get; }
        public DScriptGetterAttribute(string name) { Name = name; }
    }

    /// <summary>Exposes a C# method as a write accessor; receives receiver and new value.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class DScriptSetterAttribute : Attribute
    {
        public string Name { get; }
        public DScriptSetterAttribute(string name) { Name = name; }
    }
}
