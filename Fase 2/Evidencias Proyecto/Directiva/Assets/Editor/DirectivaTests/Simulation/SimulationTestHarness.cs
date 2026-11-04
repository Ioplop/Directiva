using System;
using System.Collections.Generic;
using System.Reflection;
using DMath;
using DSEvents;
using DSimulation;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    /// <summary>
    /// Isolated fixtures for the current simulation foundation.
    /// Reflection is limited to the test harness: the Editor assembly cannot invoke
    /// internal SBEntity.Die or set SBEntity.Position, nor inspect the private registry.
    /// No runtime / production APIs have been changed to enable these tests.
    /// </summary>
    internal static class SimulationTestHarness
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        internal sealed class StubEntity : SBEntity
        {
            internal StubEntity(Fixed radius, bool collides = true, int priority = 0)
                : base()
            {
                Radius = radius;
                Collides = collides;
                CollisionPriority = priority;
            }

            internal StubEntity(FixedVector2 position, Fixed radius,
                Fixed maxHealth, Fixed maxShields, Fixed shieldRegen)
                : base(position, 0, radius, 0, maxHealth, maxShields, shieldRegen)
            {
            }

            internal void RotateTo(Fixed angle) => Rotation = angle;
            internal void ApplyDamage(Fixed amount) => Damage(amount);
        }

        private static readonly MethodInfo SetPositionMethod =
            typeof(SBEntity).GetProperty(nameof(SBEntity.Position))?.GetSetMethod(true);
        private static readonly MethodInfo DieMethod =
            typeof(SBEntity).GetMethod("Die", PrivateInstance);
        private static readonly FieldInfo EntityListField =
            typeof(World).GetField("entityList", PrivateInstance);
        private static readonly FieldInfo RegistryField =
            typeof(World).GetField("entityRegistry", PrivateInstance);
        private static readonly FieldInfo CellsField =
            typeof(World).GetField("worldSpace", PrivateInstance);
        private static readonly FieldInfo UnitModeField =
            typeof(SBUnit).GetField("moveMode", PrivateInstance);
        private static readonly FieldInfo UnitPositionTargetField =
            typeof(SBUnit).GetField("positionTarget", PrivateInstance);
        private static readonly FieldInfo UnitEntityTargetField =
            typeof(SBUnit).GetField("entityTarget", PrivateInstance);
        private static readonly FieldInfo UnitVelocityField =
            typeof(SBUnit).GetField("linearVelocity", PrivateInstance);

        internal static Fixed Raw(long value) => new Fixed(value, raw: true);
        internal static FixedVector2 P(Fixed x, Fixed y) => new FixedVector2(x, y);

        internal static World NewWorld(ulong seed = 123UL, long width = 21, long height = 21)
        {
            return new World(seed, new FixedVector2(width, height));
        }

        internal static StubEntity Add(World world, Fixed x, Fixed y, Fixed radius,
            bool collides = true, int priority = 0)
        {
            var entity = new StubEntity(radius, collides, priority);
            Place(entity, x, y);
            world.EnqueueCreation(entity);
            return entity;
        }

        internal static StubEntity AddWithStats(World world, Fixed x, Fixed y, Fixed radius,
            Fixed maxHealth, Fixed maxShields, Fixed shieldRegen)
        {
            var entity = new StubEntity(P(x, y), radius, maxHealth, maxShields, shieldRegen);
            world.EnqueueCreation(entity);
            return entity;
        }

        internal static SBUnit AddUnit(World world, int side, Fixed x, Fixed y, Fixed rotation)
        {
            var unit = new SBUnit(side, P(x, y), rotation);
            world.EnqueueCreation(unit);
            return unit;
        }

        private static void SetUnitMode(SBUnit unit, string mode)
        {
            if (UnitModeField == null)
                throw new InvalidOperationException("SBUnit.moveMode field not found.");
            UnitModeField.SetValue(unit, Enum.Parse(UnitModeField.FieldType, mode));
        }

        internal static void MoveTo(SBUnit unit, Fixed x, Fixed y)
        {
            if (UnitPositionTargetField == null)
                throw new InvalidOperationException("SBUnit.positionTarget field not found.");
            UnitPositionTargetField.SetValue(unit, P(x, y));
            SetUnitMode(unit, "Position");
        }

        internal static void Follow(SBUnit unit, SBEntity target)
        {
            if (UnitEntityTargetField == null)
                throw new InvalidOperationException("SBUnit.entityTarget field not found.");
            UnitEntityTargetField.SetValue(unit, target);
            SetUnitMode(unit, "Follow");
        }

        internal static void Hold(SBUnit unit) => SetUnitMode(unit, "Hold");

        internal static bool IsHolding(SBUnit unit)
        {
            if (UnitModeField == null)
                throw new InvalidOperationException("SBUnit.moveMode field not found.");
            return UnitModeField.GetValue(unit).ToString() == "Hold";
        }

        internal static FixedVector2 Velocity(SBUnit unit)
        {
            if (UnitVelocityField == null)
                throw new InvalidOperationException("SBUnit.linearVelocity field not found.");
            return (FixedVector2)UnitVelocityField.GetValue(unit);
        }

        internal static List<Event> DrainEvents(World world)
        {
            var consumed = new List<Event>();
            while (world.TryConsumeEvent(out Event e))
                consumed.Add(e);
            return consumed;
        }

        internal static void Place(SBEntity entity, Fixed x, Fixed y)
        {
            if (SetPositionMethod == null)
                throw new InvalidOperationException("SBEntity.Position internal setter not found.");
            SetPositionMethod.Invoke(entity, new object[] { P(x, y) });
        }

        internal static void Kill(SBEntity entity)
        {
            if (DieMethod == null)
                throw new InvalidOperationException("SBEntity.Die internal method not found.");
            try
            {
                DieMethod.Invoke(entity, null);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        internal static List<SBEntity> Entities(World world)
        {
            if (EntityListField == null)
                throw new InvalidOperationException("Simulation.entityList field not found.");
            return (List<SBEntity>)EntityListField.GetValue(world);
        }

        internal static Dictionary<int, SBEntity> Registry(World world)
        {
            if (RegistryField == null)
                throw new InvalidOperationException("Simulation.entityRegistry field not found.");
            return (Dictionary<int, SBEntity>)RegistryField.GetValue(world);
        }

        internal static List<SBEntity>[,] Cells(World world)
        {
            if (CellsField == null)
                throw new InvalidOperationException("Simulation.worldSpace field not found.");
            return (List<SBEntity>[,])CellsField.GetValue(world);
        }

        internal static int CountCellReferences(World world, SBEntity entity)
        {
            int count = 0;
            foreach (var cell in Cells(world))
                if (cell.Contains(entity))
                    count++;
            return count;
        }

        internal static void AssertInside(DirectivaTestContext t, SBEntity entity,
            Fixed width, Fixed height)
        {
            t.Check(entity.Position.X - entity.Radius >= 0,
                entity + " extends beyond the left boundary");
            t.Check(entity.Position.Y - entity.Radius >= 0,
                entity + " extends beyond the bottom boundary");
            t.Check(entity.Position.X + entity.Radius <= width,
                entity + " extends beyond the right boundary");
            t.Check(entity.Position.Y + entity.Radius <= height,
                entity + " extends beyond the top boundary");
        }

        internal static void AddTest(List<DirectivaTestCase> tests, string group, string name,
            string description, Action<DirectivaTestContext> execute)
        {
            tests.Add(new DirectivaTestCase("Simulation", group, name, description, execute));
        }
    }
}
