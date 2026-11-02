using System;
using System.Collections.Generic;
using System.Reflection;
using DMath;
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
