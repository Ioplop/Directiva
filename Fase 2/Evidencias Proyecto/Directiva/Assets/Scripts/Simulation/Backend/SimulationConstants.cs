using DMath;

namespace DSimulation
{
    public static class SConstants
    {
        // Precision de la simulacion
        public static readonly Fixed deltaTime = Fixed.Decimal(2, 2); // 50 Hz

        // Prioridad de colisiones para cada tipo de entidad
        public const int NexusPriority = 0;
        public const int ResourcePriority = 1;
        public const int UnitPriority = 2;

        // Radio de cada tipo de entidad
        public static readonly Fixed NexusRadius = 2;
        public static readonly Fixed ResourceRadius = 1;
        public static readonly Fixed UnitRadius = Fixed.Decimal(5, 1); // 0.5

        // Vida
        public static readonly Fixed NexusHealth = 100;
        public static readonly Fixed UnitHealth = 10;
        public static readonly Fixed ResourceHealth = 100;

        // Escudos
        public static readonly Fixed NexusShield = 50;
        public static readonly Fixed UnitShields = 10;
        public static readonly Fixed ResourceShields = 0;

        // Regeneracion escudos
        public static readonly Fixed UnitShieldRegen = new Fixed(50) / 20; // Recharge the entire shield (50 points) in 20 seconds.
        public static readonly Fixed NexusShieldRegen = new Fixed(10) / 6;// Recharge the entire shield (10 points) in 6 seconds.
        public static readonly Fixed ResourceShieldRegen = 0;

        /// <summary> Time in seconds after taking damage before beginning shield regeneration. </summary>
        public static readonly Fixed ShieldRegenCooldown = 10;
    }
}