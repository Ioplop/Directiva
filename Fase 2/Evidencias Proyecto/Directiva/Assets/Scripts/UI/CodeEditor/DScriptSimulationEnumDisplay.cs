using System;
using System.Linq;
using DSimulation;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Maps CLR enums in the simulation bridge to their actual DScript namespaces.
    /// An enum is not a script Int: its constants are Type.Unit, Side.Enemy, etc.
    /// Unknown CLR enums have no script-facing name and must not be guessed.
    /// </summary>
    internal static class DScriptSimulationEnumDisplay
    {
        internal static string ModuleName(Type clrType)
        {
            if (clrType == typeof(SimulationEntityType)) return "Type";
            if (clrType == typeof(SimulationSideRelation)) return "Side";
            if (clrType == typeof(SimulationMoveMode)) return "MoveMode";
            if (clrType == typeof(SimulationActionMode)) return "ActionMode";
            return null;
        }

        internal static string Choices(Type clrType)
        {
            string module = ModuleName(clrType);
            return module == null ? string.Empty :
                string.Join("  ·  ", Enum.GetNames(clrType).Select(name => module + "." + name));
        }
    }
}
