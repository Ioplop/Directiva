using System;

namespace DSimulation
{
    /// <summary>
    /// Marks a controller method as a DScript order for future static discovery.
    /// The first int parameter is the receiver's entity ID and is implicit in DScript.
    /// Different CLR signatures may share the same name to represent DScript overloads.
    /// The orchestrator checks that orders can only target the VM's own entity.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SimulationOrderAttribute : Attribute
    {
        public string Name { get; }
        public SimulationEntityKinds AppliesTo { get; set; } = SimulationEntityKinds.Player;

        /// <summary>
        /// Excludes operations requiring orchestrator-specific arguments or results
        /// from automatic registration, while keeping their public name discoverable.
        /// </summary>
        public bool ManualRegistration { get; set; }

        public SimulationOrderAttribute(string name)
        {
            Name = name;
        }
    }
}
