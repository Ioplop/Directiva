using System;
using DMath;
using DSimulation;
using UnityEngine;

namespace Directiva.Levels
{
    /// <summary>Unity-serialized source of a private, deterministic per-match parameter snapshot.</summary>
    [Serializable]
    public sealed class LevelSimulationSettings
    {
        [Header("Tiempo simulado") ]
        public float deltaTime = 0.02f;
        [Header("Prioridad de colisiones") ]
        public int nexusPriority = 0;
        public int resourcePriority = 1;
        public int unitPriority = 2;
        [Header("Radio") ]
        public float nexusRadius = 2f;
        public float resourceRadius = 1f;
        public float unitRadius = 0.5f;
        [Header("Ataque - Nexo") ]
        public float nexusDamage = 5f;
        public float nexusAttackSpeed = 1f;
        public float nexusRange = 6f;
        [Header("Ataque - Unidad") ]
        public float unitDamage = 5f;
        public float unitAttackSpeed = 1f;
        public float unitRange = 4f;
        [Header("Vida") ]
        public float nexusHealth = 100f;
        public float unitHealth = 10f;
        public float resourceHealth = 100f;
        [Header("Escudos") ]
        public float nexusShield = 50f;
        public float unitShields = 10f;
        public float resourceShields = 0f;
        [Header("Regeneracion de escudos") ]
        public float nexusShieldRegen = 1.6666f;
        public float unitShieldRegen = 2.5f;
        public float resourceShieldRegen = 0f;
        [Header("Cooldown de escudos") ]
        public float shieldRegenCooldown = 4f;
        [Header("Recursos y produccion") ]
        public float resourceGatherSpeed = 1f;
        public int resourceGatherAmount = 1;
        public int resourceMaxAmount = 100;
        public int unitCost = 10;
        public float unitBuildTime = 10f;
        [Header("Movimiento y combate de unidades") ]
        public float followDistance = 0.1f;
        public float maxSpeed = 5f;
        public float acceleration = 5f;
        public float angularVelocity = 360f;
        public float rotationSoftness = 10f;
        public float gatherDistance = 0.2f;
        public float unloadDistance = 1f;
        public float cargoSlowdown = 0.9f;
        public int maxCargo = 15;
        public float followGain = 4f;
        public float aimAlignment = 0.99f;
        public float aimDistanceFactor = 1.5f;

        // Fixed is not Unity-serializable. Store Inspector numbers as float, then
        // round to the engine's four-decimal fixed representation exactly once.
        public static Fixed ToFixed(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            long raw = checked((long)Math.Round((double)value * Constants.FixedScale,
                MidpointRounding.AwayFromZero));
            return new Fixed(raw, raw: true);
        }

        public SimulationParameters ToParameters()
        {
            var p = new SimulationParameters
            {
                DeltaTime = ToFixed(deltaTime),
                NexusPriority = nexusPriority,
                ResourcePriority = resourcePriority,
                UnitPriority = unitPriority,
                NexusRadius = ToFixed(nexusRadius),
                ResourceRadius = ToFixed(resourceRadius),
                UnitRadius = ToFixed(unitRadius),
                NexusDamage = ToFixed(nexusDamage),
                NexusAttackSpeed = ToFixed(nexusAttackSpeed),
                NexusRange = ToFixed(nexusRange),
                UnitDamage = ToFixed(unitDamage),
                UnitAttackSpeed = ToFixed(unitAttackSpeed),
                UnitRange = ToFixed(unitRange),
                NexusHealth = ToFixed(nexusHealth),
                UnitHealth = ToFixed(unitHealth),
                ResourceHealth = ToFixed(resourceHealth),
                NexusShield = ToFixed(nexusShield),
                UnitShields = ToFixed(unitShields),
                ResourceShields = ToFixed(resourceShields),
                NexusShieldRegen = ToFixed(nexusShieldRegen),
                UnitShieldRegen = ToFixed(unitShieldRegen),
                ResourceShieldRegen = ToFixed(resourceShieldRegen),
                ShieldRegenCooldown = ToFixed(shieldRegenCooldown),
                ResourceGatherSpeed = ToFixed(resourceGatherSpeed),
                ResourceGatherAmount = resourceGatherAmount,
                ResourceMaxAmount = resourceMaxAmount,
                UnitCost = unitCost,
                UnitBuildTime = ToFixed(unitBuildTime),
                FollowDistance = ToFixed(followDistance),
                MaxSpeed = ToFixed(maxSpeed),
                Acceleration = ToFixed(acceleration),
                AngularVelocity = ToFixed(angularVelocity),
                RotationSoftness = ToFixed(rotationSoftness),
                GatherDistance = ToFixed(gatherDistance),
                UnloadDistance = ToFixed(unloadDistance),
                CargoSlowdown = ToFixed(cargoSlowdown),
                MaxCargo = maxCargo,
                FollowGain = ToFixed(followGain),
                AimAlignment = ToFixed(aimAlignment),
                AimDistanceFactor = ToFixed(aimDistanceFactor),
            };
            p.Validate();
            return p;
        }
    }
}
