using System;

namespace DSExecution.DataTypes
{
    /// <summary>The three distinct forms of a DScript member invocation.</summary>
    public enum DScriptMemberKind { Method, Getter, Setter }

    /// <summary>Exposes a C# method as a DScript method. -1 permits variable arity.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class DScriptMethodAttribute : Attribute
    {
        public string Name { get; }
        public int ArgumentCount { get; }
        public DScriptMethodAttribute(string name, int argumentCount = -1)
        {
            Name = name;
            ArgumentCount = argumentCount;
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
