#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using DSExecution.Errors;
using DSExecution.Operations;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// A DataType's annotated DScript members. Reflection runs once when that DataType is
    /// instantiated, never during script execution. Virtual/abstract C# overrides inherit
    /// their annotations and dispatch to the final derived implementation.
    /// </summary>
    public abstract partial class DataType
    {
        private sealed class MemberGroup
        {
            internal OperationHandler? Method;
            internal OperationHandler? Getter;
            internal OperationHandler? Setter;
            internal int MethodArity = -1;
        }

        private readonly Dictionary<string, MemberGroup> members =
            new Dictionary<string, MemberGroup>(StringComparer.Ordinal);

        private void PopulateMembers()
        {
            Type type = GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.IsSpecialName)
                    continue;

                var methodAttribute = method.GetCustomAttribute<DScriptMethodAttribute>(true);
                var getterAttribute = method.GetCustomAttribute<DScriptGetterAttribute>(true);
                var setterAttribute = method.GetCustomAttribute<DScriptSetterAttribute>(true);

                int annotationCount = (methodAttribute == null ? 0 : 1) +
                    (getterAttribute == null ? 0 : 1) + (setterAttribute == null ? 0 : 1);
                if (annotationCount == 0)
                    continue;
                if (annotationCount != 1)
                    throw new InvalidOperationException(type.Name + "." + method.Name +
                        " has more than one DScript member annotation.");

                if (method.ReturnType != typeof(OperationResult) ||
                    method.GetParameters().Length != 1 ||
                    method.GetParameters()[0].ParameterType != typeof(OperationCall) ||
                    method.IsStatic || method.ContainsGenericParameters)
                    throw new InvalidOperationException(type.Name + "." + method.Name +
                        " must be an instance OperationResult method with one OperationCall parameter.");

                var kind = methodAttribute != null ? DScriptMemberKind.Method :
                    getterAttribute != null ? DScriptMemberKind.Getter : DScriptMemberKind.Setter;
                string name = methodAttribute?.Name ?? getterAttribute?.Name ?? setterAttribute!.Name;
                if (!IsValidMemberName(name))
                    throw new InvalidOperationException(type.Name + "." + method.Name +
                        " has an invalid DScript member name '" + name + "'.");
                if (methodAttribute != null && methodAttribute.ArgumentCount < -1)
                    throw new InvalidOperationException(type.Name + "." + method.Name +
                        " has an invalid DScript method argument count.");

                if (!members.TryGetValue(name, out MemberGroup? group))
                {
                    group = new MemberGroup();
                    members.Add(name, group);
                }

                // The getter and setter of a property may share one name; a method may not.
                if (kind == DScriptMemberKind.Method &&
                    (group.Method != null || group.Getter != null || group.Setter != null) ||
                    kind != DScriptMemberKind.Method && group.Method != null ||
                    kind == DScriptMemberKind.Getter && group.Getter != null ||
                    kind == DScriptMemberKind.Setter && group.Setter != null)
                    throw new InvalidOperationException(type.Name + " registers DScript member '" +
                        name + "' more than once or mixes property and method.");

                var handler = (OperationHandler)method.CreateDelegate(typeof(OperationHandler), this);
                switch (kind)
                {
                    case DScriptMemberKind.Method:
                        group.Method = handler;
                        group.MethodArity = methodAttribute!.ArgumentCount;
                        break;
                    case DScriptMemberKind.Getter:
                        group.Getter = handler;
                        break;
                    case DScriptMemberKind.Setter:
                        group.Setter = handler;
                        break;
                }
            }
        }

        private static bool IsValidMemberName(string name)
        {
            if (string.IsNullOrEmpty(name) || !(name[0] == '_' || char.IsLetter(name[0])))
                return false;
            for (int i = 1; i < name.Length; i++)
                if (name[i] != '_' && !char.IsLetterOrDigit(name[i]))
                    return false;
            return true;
        }

        /// <summary>Dispatches an annotated member without runtime reflection.</summary>
        public OperationResult InvokeMember(DScriptMemberKind kind, string name, OperationCall call)
        {
            if (string.IsNullOrEmpty(name) || !members.TryGetValue(name, out MemberGroup? group))
                return OperationResult.OpError(RuntimeError.MemberNotFound(GetType().Name, name));

            OperationHandler? handler = kind switch
            {
                DScriptMemberKind.Method => group.Method,
                DScriptMemberKind.Getter => group.Getter,
                DScriptMemberKind.Setter => group.Setter,
                _ => null
            };
            if (handler == null)
            {
                if (kind == DScriptMemberKind.Setter && group.Getter != null)
                    return OperationResult.OpError(RuntimeError.ReadOnlyProperty(GetType().Name, name));
                if (kind == DScriptMemberKind.Getter && group.Setter != null)
                    return OperationResult.OpError(RuntimeError.WriteOnlyProperty(GetType().Name, name));
                return OperationResult.OpError(RuntimeError.MemberNotFound(GetType().Name, name));
            }
            int actualArguments = call.Arguments.Length - 1; // The receiver is not a method argument.
            int requiredArguments = kind switch
            {
                DScriptMemberKind.Getter => 0,
                DScriptMemberKind.Setter => 1,
                _ => group.MethodArity
            };
            if (actualArguments < 0 || requiredArguments >= 0 && requiredArguments != actualArguments)
                return OperationResult.OpError(RuntimeError.InvalidMemberArgumentCount(
                    GetType().Name, name, requiredArguments, actualArguments));

            OperationResult result = handler(call);
            return result.Status == OperationStatus.NotImplemented
                ? OperationResult.OpError(RuntimeError.InvalidOperation(
                    $"Member '{name}' is not supported by {GetType().Name} for these arguments."))
                : result;
        }
    }
}
