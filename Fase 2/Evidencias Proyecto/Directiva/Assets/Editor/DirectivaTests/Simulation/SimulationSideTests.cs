using System;
using System.Collections.Generic;
using DMath;
using DSEvents;
using DSimulation;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.SimulationTests
{
    internal static class SimulationSideTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                SimulationTestHarness.AddTest(tests, "Sides", name, description, action);

            Add("ThreeSides", "Every simulation has neutral, player 1, and player 2 sides", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                for (int i = 0; i < 3; i++)
                {
                    SBSide side = world.GetSide(i);
                    t.Equal(i, side.id, "side ID");
                    t.Equal(0, side.Resources, "starting balance");
                    t.Check(side.Nexus == null, "no initial nexus");
                    t.Equal(0, side.Units.Count, "no initial units");
                    t.Equal(0, side.ResourceEntities.Count, "no initial deposits");
                }
            });

            Add("EntityMembership", "Units and nexuses belong to their player side; deposits are neutral", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit1 = SimulationTestHarness.AddUnit(world, 1, 4, 4, 0);
                SBUnit unit2 = SimulationTestHarness.AddUnit(world, 2, 10, 4, 0);
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(4, 12), 0, 20);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(12, 12));
                world.EnqueueCreation(resource);
                world.EnqueueCreation(nexus);
                world.Start();

                t.Equal(1, world.GetSide(1).Units.Count, "player 1 units");
                t.Check(world.GetSide(1).Units[0] == unit1, "player 1 unit identity");
                t.Check(ReferenceEquals(unit1.side, world.GetSide(1)), "unit side reference");
                t.Equal(1, world.GetSide(2).Units.Count, "player 2 units");
                t.Check(world.GetSide(2).Units[0] == unit2, "player 2 unit identity");
                t.Check(world.GetSide(1).Nexus == nexus, "player 1 nexus");
                t.Check(ReferenceEquals(nexus.side, world.GetSide(1)), "nexus side reference");
                t.Equal(1, world.GetSide(0).ResourceEntities.Count, "neutral resource count");
                t.Check(world.GetSide(0).ResourceEntities[0] == resource, "neutral resource identity");
                t.Check(ReferenceEquals(resource.side, world.GetSide(0)), "resource side reference");

                var events = SimulationTestHarness.DrainEvents(world);
                t.Equal(4, events.Count, "four creation events");
                t.Check(events[3] is EventCreateNexus created && created.id == nexus.id &&
                    created.side == 1, "nexus creation event");
            });

            Add("NexusTargetsOnlyEnemies", "The nexus attacks non-neutral enemies without requiring rotation", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var neutral = SimulationTestHarness.Add(world, 10, 5, 1);
                var ally = SimulationTestHarness.AddUnit(world, 1, 10, 15, 0);
                var enemy = SimulationTestHarness.AddUnit(world, 2, 15, 10, 180);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                world.EnqueueCreation(nexus);
                world.Start();
                SimulationTestHarness.DrainEvents(world);

                world.Update();
                var events = SimulationTestHarness.DrainEvents(world);
                var attacks = events.FindAll(e => e is EventAttack);

                t.Equal(1, attacks.Count, "one attack per tick");
                t.Check(attacks[0] is EventAttack attack && attack.attackerId == nexus.id &&
                    attack.targetId == enemy.id, "nexus attacks the enemy unit");
                t.Equal(SConstants.UnitShields - SConstants.NexusDamage, enemy.Shields, "enemy shields damaged");
                t.Equal(SConstants.UnitShields, ally.Shields, "ally shields untouched");
                t.Equal((Fixed)1, neutral.Health, "neutral entity untouched");
                t.Equal((Fixed)0, nexus.Rotation, "no rotation needed to fire");
            });

            Add("NexusRangeAndCooldown", "The nexus respects its range and cannot attack again before its cooldown", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var enemy = SimulationTestHarness.AddUnit(world, 2, 17, 10, 0);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                world.EnqueueCreation(nexus);
                world.Start();
                SimulationTestHarness.DrainEvents(world);

                world.Update();
                t.Equal(0, SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack).Count,
                    "cannot attack beyond range");

                SimulationTestHarness.Place(enemy, 15, 10);
                world.Update();
                t.Equal(1, SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack).Count,
                    "attacks immediately once in range");
                t.Equal(SConstants.UnitShields - SConstants.NexusDamage, enemy.Shields,
                    "first hit damage");

                for (int i = 0; i < 49; i++)
                    world.Update();
                t.Equal(0, SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack).Count,
                    "no attacks during the cooldown");

                world.Update();
                t.Equal(1, SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack).Count,
                    "attacks when cooldown finishes");
                t.Equal(SConstants.UnitShields - 2 * SConstants.NexusDamage, enemy.Shields,
                    "second hit damage");
            });

            Add("NexusTargetsNearestEnemy", "The nexus locks onto the closest enemy rather than the first registered", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var fartherEnemy = SimulationTestHarness.AddUnit(world, 2, 15, 10, 0);
                var closerEnemy = SimulationTestHarness.AddUnit(world, 2, 13, 10, 0);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                world.EnqueueCreation(nexus);
                world.Start();
                SimulationTestHarness.DrainEvents(world);

                world.Update();
                var attacks = SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack);
                t.Equal(1, attacks.Count, "one attack");
                t.Check(attacks[0] is EventAttack attack && attack.attackerId == nexus.id &&
                    attack.targetId == closerEnemy.id, "nearest enemy chosen even when registered later");
                t.Equal(SConstants.UnitShields, fartherEnemy.Shields, "farther enemy untouched");
            });

            Add("NexusKeepsTargetDuringCooldown", "A closer enemy does not cause the nexus to change its locked target", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var firstEnemy = SimulationTestHarness.AddUnit(world, 2, 15, 10, 0);
                var newCloserEnemy = SimulationTestHarness.AddUnit(world, 2, 18, 10, 0);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                world.EnqueueCreation(nexus);
                world.Start();
                SimulationTestHarness.DrainEvents(world);

                world.Update();
                var firstAttacks = SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack);
                t.Equal(1, firstAttacks.Count, "initial attack");
                t.Check(firstAttacks[0] is EventAttack first && first.targetId == firstEnemy.id,
                    "initial lock");

                SimulationTestHarness.Place(newCloserEnemy, 13, 10);
                for (int i = 0; i < 49; i++)
                    world.Update();
                t.Equal(0, SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack).Count,
                    "cooldown prevents early attacks");

                world.Update();
                var attacks = SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack);
                t.Equal(1, attacks.Count, "one attack after cooldown");
                t.Check(attacks[0] is EventAttack attack && attack.targetId == firstEnemy.id,
                    "original target retained despite closer enemy");
            });

            Add("NexusReacquiresOutOfRangeTarget", "Leaving range causes the nexus to select another enemy", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var firstEnemy = SimulationTestHarness.AddUnit(world, 2, 13, 10, 0);
                var replacement = SimulationTestHarness.AddUnit(world, 2, 15, 10, 0);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                world.EnqueueCreation(nexus);
                world.Start();
                SimulationTestHarness.DrainEvents(world);

                world.Update();
                var firstAttacks = SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack);
                t.Check(firstAttacks.Count == 1 && firstAttacks[0] is EventAttack first &&
                    first.targetId == firstEnemy.id, "initial target acquired");

                SimulationTestHarness.Place(firstEnemy, 18, 10);
                for (int i = 0; i < 50; i++)
                    world.Update();
                var attacks = SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack);
                t.Equal(1, attacks.Count, "one attack after cooldown");
                t.Check(attacks[0] is EventAttack attack && attack.targetId == replacement.id,
                    "new in-range target acquired");
            });

            Add("NexusReacquiresDeadTarget", "The nexus acquires another enemy after its locked target dies", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var firstEnemy = SimulationTestHarness.AddUnit(world, 2, 13, 10, 0);
                var replacement = SimulationTestHarness.AddUnit(world, 2, 15, 10, 0);
                var nexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                world.EnqueueCreation(nexus);
                world.Start();
                SimulationTestHarness.DrainEvents(world);

                world.Update();
                var firstAttacks = SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack);
                t.Check(firstAttacks.Count == 1 && firstAttacks[0] is EventAttack first &&
                    first.targetId == firstEnemy.id, "initial target acquired");

                SimulationTestHarness.Kill(firstEnemy);
                for (int i = 0; i < 50; i++)
                    world.Update();
                var attacks = SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack);
                t.Equal(1, attacks.Count, "one attack after cooldown");
                t.Check(attacks[0] is EventAttack attack && attack.targetId == replacement.id,
                    "new living target acquired");
            });

            Add("NexusCanAttackEnemyNexus", "Nexuses can attack one another when in range", t =>
            {
                World world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var nexus1 = new SBNexus(world.GetSide(1), SimulationTestHarness.P(10, 10));
                var nexus2 = new SBNexus(world.GetSide(2), SimulationTestHarness.P(15, 10));
                world.EnqueueCreation(nexus1);
                world.EnqueueCreation(nexus2);
                world.Start();
                SimulationTestHarness.DrainEvents(world);

                world.Update();
                var attacks = SimulationTestHarness.DrainEvents(world).FindAll(e => e is EventAttack);
                t.Equal(2, attacks.Count, "both nexuses attack");
                t.Check(attacks[0] is EventAttack first && first.attackerId == nexus1.id &&
                    first.targetId == nexus2.id, "first nexus targets second");
                t.Check(attacks[1] is EventAttack second && second.attackerId == nexus2.id &&
                    second.targetId == nexus1.id, "second nexus targets first");
                t.Equal(SConstants.NexusShield - SConstants.NexusDamage, nexus1.Shields,
                    "first nexus shields damaged");
                t.Equal(SConstants.NexusShield - SConstants.NexusDamage, nexus2.Shields,
                    "second nexus shields damaged");
            });

            Add("RemovalUpdatesSides", "Removed entities disappear from their corresponding side", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                SBUnit unit = SimulationTestHarness.AddUnit(world, 1, 4, 4, 0);
                var resource = new SBResource(world.GetSide(0), SimulationTestHarness.P(4, 12), 0, 20);
                var nexus = new SBNexus(world.GetSide(2), SimulationTestHarness.P(12, 12));
                world.EnqueueCreation(resource);
                world.EnqueueCreation(nexus);
                world.Start();

                SimulationTestHarness.Kill(unit);
                SimulationTestHarness.Kill(resource);
                SimulationTestHarness.Kill(nexus);
                world.Update();

                t.Equal(0, world.GetSide(1).Units.Count, "unit removed");
                t.Equal(0, world.GetSide(0).ResourceEntities.Count, "resource removed");
                t.Check(world.GetSide(2).Nexus == null, "nexus removed");
            });
        }
    }
}
