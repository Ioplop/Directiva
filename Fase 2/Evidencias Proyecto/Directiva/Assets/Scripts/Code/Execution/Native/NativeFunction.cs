#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.Native
{
    /// <summary>Marks a static C# provider method as an exposed DScript function.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class DScriptNativeFunctionAttribute : Attribute
    {
        public string Module { get; }
        public string Name { get; }
        public int MinArguments { get; }
        public int MaxArguments { get; }

        public DScriptNativeFunctionAttribute(string module, string name, int minArguments, int maxArguments)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            if (minArguments < 0 || maxArguments < minArguments || maxArguments > 16)
                throw new ArgumentOutOfRangeException(nameof(maxArguments), "Native calls support 0 to 16 arguments.");
            MinArguments = minArguments;
            MaxArguments = maxArguments;
        }
    }

    /// <summary>One atomic native invocation's result, never a host exception.</summary>
    public readonly struct NativeResult
    {
        public DataValue Value { get; }
        public RuntimeError? Error { get; }
        public bool IsSuccess => Error == null;

        private NativeResult(DataValue value, RuntimeError? error)
        {
            Value = value;
            Error = error;
        }
        public static NativeResult Success(DataValue value) => new(value, null);
        public static NativeResult Failure(RuntimeError error) => new(DataValue.None(), error);
    }

    /// <summary>
    /// Restricted native bridge. Call arguments remain on the VM evaluation stack as GC roots
    /// until their result has been committed; native providers do not own VM frames or stacks.
    /// </summary>
    public sealed class NativeCallContext
    {
        private readonly Action<string> output;
        public VMMemoryContext Memory { get; }
        public IReadOnlyList<DataValue> Arguments { get; }
        public int Count => Arguments.Count;
        public DataValue this[int index] => Arguments[index];

        internal NativeCallContext(VMMemoryContext memory, DataValue[] arguments, Action<string> output)
        {
            Memory = memory;
            Arguments = arguments;
            this.output = output;
        }

        public NativeResult Cast(DataValue value, DataTypeId target)
        {
            if (value.dataType == DataTypeId.Uninitialized)
                return NativeResult.Failure(RuntimeError.VariableUsedBeforeInitialization());
            DataType? source = DataType.GetDataType(value.dataType);
            if (source == null)
                return NativeResult.Failure(RuntimeError.InvalidCast(value.dataType, target));
            CastResult result = source.CastTo(target, value, Memory);
            return result.IsSuccess ? NativeResult.Success(result.Value) :
                NativeResult.Failure(result.Error ?? RuntimeError.InvalidCast(value.dataType, target));
        }

        public NativeResult Operate(OperationId operation, params DataValue[] arguments)
        {
            OperationResult result = OperationDispatcher.Start(operation, arguments, Memory).Result;
            // A native call must never hide multiple VM steps. Only constant-time operations
            // are used here; continued operations cannot be called from this bridge.
            if (result.Status == OperationStatus.Success)
                return NativeResult.Success(result.Value);
            if (result.Status == OperationStatus.Error)
                return NativeResult.Failure(result.Error ?? RuntimeError.InternalVmError("Native operation failed without an error."));
            return NativeResult.Failure(RuntimeError.InvalidOperation(
                $"Native operation {operation} cannot be continued in a single step."));
        }

        /// <summary>Formats all native call arguments as a single space-separated console event.</summary>
        public NativeResult PrintArguments() => PrintValues(Arguments);

        // Preserve the existing single-value helper for other trusted native providers.
        public NativeResult Print(DataValue value) => PrintValues(new[] { value });

        private NativeResult PrintValues(IReadOnlyList<DataValue> values)
        {
            // Do not emit partial output when a later argument cannot be converted.
            // The VM keeps all original arguments on its evaluation stack as GC roots
            // for the duration of this native call. Converted strings are copied to
            // the host-side builder immediately and do not need to survive the next cast.
            var text = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                NativeResult converted = Cast(values[i], DataTypeId.String);
                if (!converted.IsSuccess) return converted;
                if (!converted.Value.TryGetHeapReference(out uint id) ||
                    !Memory.TryGetHeap(id, out HeapObject obj) || obj is not StringHeapObject str)
                    return NativeResult.Failure(RuntimeError.InvalidOperation(
                        "print() did not produce a valid String."));
                if (i > 0) text.Append(' ');
                text.Append(str.Value);
            }
            output(text.ToString());
            return NativeResult.Success(DataValue.None());
        }
    }

    /// <summary>Immutable native function bound to an ID local to one CodeContext.</summary>
    public sealed class NativeFunction
    {
        private readonly Func<NativeCallContext, NativeResult> implementation;
        public int Id { get; }
        public string QualifiedName { get; }
        public int MinArguments { get; }
        public int MaxArguments { get; }

        internal NativeFunction(int id, NativeFunctionDescription description)
        {
            Id = id;
            QualifiedName = description.QualifiedName;
            MinArguments = description.MinArguments;
            MaxArguments = description.MaxArguments;
            implementation = description.Implementation;
        }

        internal NativeResult Invoke(NativeCallContext context) => implementation(context);
        public bool Accepts(int count) => count >= MinArguments && count <= MaxArguments;
    }

    internal sealed class NativeFunctionDescription
    {
        public string Module { get; }
        public string QualifiedName { get; }
        public int MinArguments { get; }
        public int MaxArguments { get; }
        public Func<NativeCallContext, NativeResult> Implementation { get; }

        public NativeFunctionDescription(DScriptNativeFunctionAttribute attribute, Func<NativeCallContext, NativeResult> implementation)
        {
            Module = attribute.Module;
            QualifiedName = attribute.Module.Length == 0 ? attribute.Name : attribute.Module + "." + attribute.Name;
            MinArguments = attribute.MinArguments;
            MaxArguments = attribute.MaxArguments;
            Implementation = implementation;
        }
    }

    /// <summary>
    /// Frozen catalog shared by DIL and the future DScript frontend. Only native module names
    /// are reserved; ordinary modules continue to be loaded through IDILSourceProvider.
    /// New functions require only an attributed static provider method, not VM changes.
    /// </summary>
    public static class NativeLibrary
    {
        private static readonly string[] Modules = { "math", "random" };
        private static readonly NativeFunctionDescription[] Descriptions = BuildDescriptions();
        private static readonly Dictionary<string, DataValue> Constants = new(StringComparer.Ordinal)
        {
            ["math.pi"] = DataValue.FromDecimalRaw(31416),
            ["math.tau"] = DataValue.FromDecimalRaw(62832),
            ["math.e"] = DataValue.FromDecimalRaw(27183)
        };

        public static bool IsNativeModule(string module) => Array.IndexOf(Modules, module) >= 0;
        public static IReadOnlyCollection<string> ModuleNames => Modules;
        public static bool TryGetConstant(string qualifiedName, out DataValue value)
            => Constants.TryGetValue(qualifiedName, out value);

        /// <summary>Exposes metadata for DScript symbol resolution without VM internals.</summary>
        public static IReadOnlyList<string> GetFunctionNames(string module)
            => Descriptions.Where(d => d.Module == module).Select(d => d.QualifiedName).ToArray();

        public static IReadOnlyList<string> GetConstantNames(string module)
            => Constants.Keys.Where(k => k.StartsWith(module + ".", StringComparison.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal).ToArray();

        public static NativeFunction[] Bind(int firstId, ISet<string> importedModules)
        {
            if (firstId < 0) throw new ArgumentOutOfRangeException(nameof(firstId));
            var active = Descriptions.Where(d => d.Module.Length == 0 || importedModules.Contains(d.Module)).ToArray();
            if ((long)firstId + active.Length > int.MaxValue)
                throw new InvalidOperationException("Too many native functions in program.");
            var functions = new NativeFunction[active.Length];
            for (int i = 0; i < active.Length; i++) functions[i] = new NativeFunction(firstId + i, active[i]);
            return functions;
        }

        private static NativeFunctionDescription[] BuildDescriptions()
        {
            var functions = new Dictionary<string, NativeFunctionDescription>(StringComparer.Ordinal);
            // Only trusted, compiled-in providers are inspected: never reflect over user assemblies.
            foreach (Type type in new[] { typeof(NativeBuiltins), typeof(NativeMath), typeof(NativeRandom) })
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var attribute = method.GetCustomAttribute<DScriptNativeFunctionAttribute>();
                if (attribute == null) continue;
                if (!IsIdentifier(attribute.Name) ||
                    (attribute.Module.Length != 0 && !IsNativeModule(attribute.Module)))
                    throw new InvalidOperationException("Invalid native function name: " + attribute.Name);
                var handler = (Func<NativeCallContext, NativeResult>)method.CreateDelegate(
                    typeof(Func<NativeCallContext, NativeResult>));
                var description = new NativeFunctionDescription(attribute, handler);
                if (!functions.TryAdd(description.QualifiedName, description))
                    throw new InvalidOperationException("Duplicate native function: " + description.QualifiedName);
            }
            return functions.Values.OrderBy(d => d.QualifiedName, StringComparer.Ordinal).ToArray();
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || !(char.IsLetter(value[0]) || value[0] == '_')) return false;
            for (int i = 1; i < value.Length; i++)
                if (!(value[i] == '_' || char.IsLetterOrDigit(value[i]))) return false;
            return true;
        }
    }
}
