using System;
using DMath;

namespace DSimulation
{
    /// <summary>
    /// Simulation-owned values. Each world takes a private copy at construction;
    /// changing a level asset cannot silently change a match already running.
    /// Defaults are the original SConstants/SBUnit values.
    /// </summary>
    public sealed class SimulationParameters
    {
        public Fixed DeltaTime { get; set; } = SConstants.deltaTime;
        public int NexusPriority { get; set; } = SConstants.NexusPriority;
        public int ResourcePriority { get; set; } = SConstants.ResourcePriority;
        public int UnitPriority { get; set; } = SConstants.UnitPriority;
        public Fixed NexusRadius { get; set; } = SConstants.NexusRadius;
        public Fixed ResourceRadius { get; set; } = SConstants.ResourceRadius;
        public Fixed UnitRadius { get; set; } = SConstants.UnitRadius;
        public Fixed UnitDamage { get; set; } = SConstants.UnitDamage;
        public Fixed UnitAttackSpeed { get; set; } = SConstants.UnitAttackSpeed;
        public Fixed UnitRange { get; set; } = SConstants.UnitRange;
        public Fixed NexusDamage { get; set; } = SConstants.NexusDamage;
        public Fixed NexusAttackSpeed { get; set; } = SConstants.NexusAttackSpeed;
        public Fixed NexusRange { get; set; } = SConstants.NexusRange;
        public Fixed NexusHealth { get; set; } = SConstants.NexusHealth;
        public Fixed UnitHealth { get; set; } = SConstants.UnitHealth;
        public Fixed ResourceHealth { get; set; } = SConstants.ResourceHealth;
        public Fixed NexusShield { get; set; } = SConstants.NexusShield;
        public Fixed UnitShields { get; set; } = SConstants.UnitShields;
        public Fixed ResourceShields { get; set; } = SConstants.ResourceShields;
        public Fixed UnitShieldRegen { get; set; } = SConstants.UnitShieldRegen;
        public Fixed NexusShieldRegen { get; set; } = SConstants.NexusShieldRegen;
        public Fixed ResourceShieldRegen { get; set; } = SConstants.ResourceShieldRegen;
        public Fixed ShieldRegenCooldown { get; set; } = SConstants.ShieldRegenCooldown;
        public Fixed ResourceGatherSpeed { get; set; } = SConstants.resourceGatherSpeed;
        public int ResourceGatherAmount { get; set; } = SConstants.resourceGatherAmount;
        public int ResourceMaxAmount { get; set; } = SConstants.resourceMaxAmount;
        public int UnitCost { get; set; } = SConstants.unitCost;
        public Fixed UnitBuildTime { get; set; } = SConstants.unitBuildTime;

        // Values previously fixed inside SBUnit itself.
        public Fixed FollowDistance { get; set; } = Fixed.Decimal(1, 1);
        public Fixed MaxSpeed { get; set; } = 5;
        public Fixed Acceleration { get; set; } = 5;
        public Fixed AngularVelocity { get; set; } = 360;
        public Fixed RotationSoftness { get; set; } = 10;
        public Fixed GatherDistance { get; set; } = Fixed.Decimal(2, 1);
        public Fixed UnloadDistance { get; set; } = 1;
        public Fixed CargoSlowdown { get; set; } = Fixed.Decimal(9, 1);
        public int MaxCargo { get; set; } = 15;
        public Fixed FollowGain { get; set; } = 4;
        public Fixed AimAlignment { get; set; } = Fixed.Decimal(99, 2);
        public Fixed AimDistanceFactor { get; set; } = Fixed.Decimal(15, 1);

        public SimulationParameters Clone() => (SimulationParameters)MemberwiseClone();

        public void Validate()
        {
            if (DeltaTime <= 0 || DeltaTime > 1 || NexusRadius <= 0 || ResourceRadius <= 0 ||
                UnitRadius <= 0 || NexusHealth <= 0 || UnitHealth <= 0 || ResourceHealth <= 0 ||
                NexusShield < 0 || UnitShields < 0 || ResourceShields < 0 ||
                NexusShieldRegen < 0 || UnitShieldRegen < 0 || ResourceShieldRegen < 0 ||
                ShieldRegenCooldown < 0 || NexusDamage < 0 || UnitDamage < 0 ||
                UnitRange < 0 || NexusRange < 0 || NexusAttackSpeed <= 0 ||
                UnitAttackSpeed <= 0 || ResourceGatherSpeed <= 0 ||
                ResourceGatherAmount <= 0 || ResourceMaxAmount <= 0 || UnitCost < 0 ||
                UnitBuildTime <= 0 || FollowDistance < 0 || MaxSpeed <= 0 ||
                Acceleration <= 0 || AngularVelocity <= 0 || RotationSoftness <= 0 ||
                GatherDistance < 0 || UnloadDistance < 0 || CargoSlowdown <= 0 ||
                CargoSlowdown > 1 || MaxCargo <= 0 || FollowGain <= 0 ||
                AimAlignment < -1 || AimAlignment > 1 || AimDistanceFactor <= 0 ||
                NexusPriority < -1 || ResourcePriority < -1 || UnitPriority < -1)
                throw new ArgumentOutOfRangeException(nameof(SimulationParameters),
                    "Invalid level simulation parameter (check radii, times, health, speed and cargo).");
        }
    }
}
