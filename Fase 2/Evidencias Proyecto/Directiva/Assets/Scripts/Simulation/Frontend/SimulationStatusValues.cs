using System;
using System.Globalization;
using DMath;

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// Visual-only snapshot of an entity's health and shields. It contains no
    /// simulation references and is updated only from snapshot/event data.
    /// </summary>
    public readonly struct SimulationStatusValues
    {
        public Fixed Health { get; }
        public Fixed MaxHealth { get; }
        public Fixed Shields { get; }
        public Fixed MaxShields { get; }

        public SimulationStatusValues(Fixed health, Fixed maxHealth, Fixed shields, Fixed maxShields)
        {
            Health = health;
            MaxHealth = maxHealth;
            Shields = shields;
            MaxShields = maxShields;
        }

        // Both bars are shown if either value is below maximum. A zero-capacity
        // shield is already full (0/0) and does not trigger visibility on its own.
        public bool ShouldDisplay => Health < MaxHealth || Shields < MaxShields;

        public float HealthFraction => Fraction(Health, MaxHealth);
        public float ShieldFraction => Fraction(Shields, MaxShields);

        public string HealthLabel => Format(Health) + " / " + Format(MaxHealth);
        public string ShieldLabel => Format(Shields) + " / " + Format(MaxShields);

        public SimulationStatusValues WithHealth(Fixed health) =>
            new SimulationStatusValues(health, MaxHealth, Shields, MaxShields);

        public SimulationStatusValues WithShields(Fixed shields) =>
            new SimulationStatusValues(Health, MaxHealth, shields, MaxShields);

        private static float Fraction(Fixed current, Fixed maximum)
        {
            if (maximum.RawValue <= 0)
                return 0f;
            double ratio = (double)current.RawValue / maximum.RawValue;
            return (float)Math.Max(0.0, Math.Min(1.0, ratio));
        }

        private static string Format(Fixed value)
        {
            if (value.RawValue % Constants.FixedScale == 0)
                return (value.RawValue / Constants.FixedScale).ToString(CultureInfo.InvariantCulture);
            return ((double)value.RawValue / Constants.FixedScale)
                .ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
