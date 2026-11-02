using DMath;

namespace DSimulation
{
    public static class SConstants
    {
        // Prioridad de colisiones para cada tipo de entidad
        public const int NexusPriority = 0;
        public const int ResourcePriority = 1;
        public const int UnitPriority = 2;

        // Radio de cada tipo de entidad
        public static readonly Fixed NexusRadius = 2;
        public static readonly Fixed ResourceRadius = 1;
        public static readonly Fixed UnitRadius = new Fixed(Constants.FixedScale / 2, raw: true); // 0.5

        // Vida
        public static readonly Fixed NexusHealth = 100;
        public static readonly Fixed UnitHealth = 10;
        public static readonly Fixed ResourceHealth = 100;

        // Escudos
        public static readonly Fixed NexusShield = 50;
        public static readonly Fixed UnitShields = 10;
        public static readonly Fixed ResourceShields = 0;

        // Regeneracion escudos
        public static readonly Fixed UnitShieldRegen = new Fixed(50 * Constants.FixedScale / 20); // ~50 escudos en 20 segundos.
        public static readonly Fixed NexusShieldRegen = new Fixed(10 * Constants.FixedScale / 6); // ~10 escudos en 6 segundos.
        public static readonly Fixed ResourceShieldRegen = 0;

        /// <summary> Time in seconds after taking damage before beginning shield regeneration. </summary>
        public static readonly Fixed ShieldRegenCooldown = 10;
    }
}