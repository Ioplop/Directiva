using System.Linq;
using DSCompilation.DScript;
using DSimulation;

namespace DOrchestration
{
    /// <summary>
    /// Compilation profile for scripts that control simulation entities. DScript remains
    /// simulation-agnostic; only the host supplies the authoritative callable names.
    /// Future catalog entries become available without changing the compiler.
    /// </summary>
    public static class SimulationDScriptProfile
    {
        public static DScriptFrontendOptions Options { get; } = new DScriptFrontendOptions(
            implicitSelfMethods: SimulationApiCatalog.Operations
                .Where(op => op.Kind == SimulationApiKind.Order ||
                    op.MemberKind == SimulationObservationMemberKind.Method)
                .Select(op => op.Name)
                .Distinct(System.StringComparer.Ordinal)
                .OrderBy(name => name, System.StringComparer.Ordinal));
    }
}
