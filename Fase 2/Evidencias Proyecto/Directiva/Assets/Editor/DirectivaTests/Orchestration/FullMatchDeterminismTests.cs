using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DMath;
using DOrchestration;
using DSEvents;
using DSimulation;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;
using Directiva.EditorTests.SimulationTests;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.OrchestrationTests
{
    /// <summary>
    /// Full deterministic match: real DScript, two independent side VMs, resource
    /// gathering, constructions, shared coordination, combat, and match termination.
    /// No scene, Unity objects, forced building progress, or artificial nexus deaths.
    /// </summary>
    internal static class FullMatchDeterminismTests
    {
        // Five sequential constructions take at least 50 simulated seconds; allow
        // time for the resource economy, full-capacity deliveries and combat.
        // Production matches still default to 300 s.
        private const int RegressionDurationSeconds = 150;
        private const int RegressionStepsPerTick = 10;
        private const ulong MatchSeed = 20261110UL;

        // Keep these strings byte-identical to the downloadable .dscript examples.
        // Building the fifth unit requires a resource delivery on each side.
        private const string Side1Script = "shared built = 0\n" +
            "shared assault = False\n" +
            "\n" +
            "def nexus_main():\n" +
            "    if built < 5 and self.can_build:\n" +
            "        if build(worker):\n" +
            "            built += 1\n" +
            "    if built >= 5 and len(units()) >= 5 and self.resources >= 5 and not assault:\n" +
            "        assault = True\n" +
            "        print(\"Bando 1: ataque\")\n" +
            "\n" +
            "def worker():\n" +
            "    if assault:\n" +
            "        target = enemy_nexus()\n" +
            "        if target != None:\n" +
            "            move(target)\n" +
            "            attack(target)\n" +
            "    elif self.cargo >= 5:\n" +
            "        stop_action()\n" +
            "        move(self.nexus)\n" +
            "    else:\n" +
            "        deposit = closest_resource()\n" +
            "        if deposit != None:\n" +
            "            move(deposit)\n" +
            "            gather(deposit)\n";
        private const string Side2Script = "shared built = 0\n" +
            "shared charge = False\n" +
            "\n" +
            "def nexus_main():\n" +
            "    if built < 5:\n" +
            "        if self.can_build:\n" +
            "            if build(worker):\n" +
            "                built += 1\n" +
            "    if built >= 5 and len(self.units()) >= 5 and self.resources >= 5 and not charge:\n" +
            "        charge = True\n" +
            "        print(\"Bando 2: ataque\")\n" +
            "\n" +
            "def worker():\n" +
            "    if charge:\n" +
            "        base = enemy_nexus()\n" +
            "        if base != None:\n" +
            "            attack(base)\n" +
            "            move(base)\n" +
            "    elif self.is_cargo_full:\n" +
            "        stop_action()\n" +
            "        move(self.nexus)\n" +
            "    else:\n" +
            "        vein = closest_resource()\n" +
            "        if vein != None:\n" +
            "            gather(vein)\n" +
            "            move(vein)\n";

        private sealed class MatchResult
        {
            internal string Hash;
            internal MatchOutcome Outcome;
            internal MatchEndReason EndReason;
            internal long Ticks;
            internal long VmSteps;
            internal int CreatedUnits;
            internal int Side1Units;
            internal int Side2Units;
            internal int Side1UnitsAtAssault = -1;
            internal int Side2UnitsAtAssault = -1;
            internal int Side1FiveCargoUnloads;
            internal int Side2FullCargoUnloads;
            internal int Side1Unloads;
            internal int Side2Unloads;
            internal readonly Dictionary<int, int> UnitSideById = new Dictionary<int, int>();
            // Keep the most recent nonzero cargo event: EventCargo(0) is emitted
            // immediately before EventUnloadCargo, which has no quantity field.
            internal readonly Dictionary<int, int> PendingCargoByUnit = new Dictionary<int, int>();
            internal int GatherStarts;
            internal int ResourceChanges;
            internal int Unloads;
            internal int Attacks;
            internal int UnitAttacks;
            internal readonly HashSet<int> BuiltUnitIds = new HashSet<int>();
            internal int Errors;
            internal int Side1Assaults;
            internal int Side2Assaults;
            internal int MaxSide1Bindings;
            internal int MaxSide2Bindings;

            internal string Summary =>
                "SHA256=" + Hash + "\n" +
                "outcome=" + Outcome + "; reason=" + EndReason + "; ticks=" + Ticks +
                "; vmSteps=" + VmSteps + "\n" +
                "built=" + CreatedUnits + " (side1/side2=" + Side1Units + "/" + Side2Units + ")" +
                "; gather=" + GatherStarts +
                "; resourceUpdates=" + ResourceChanges + "; unload=" + Unloads +
                "; attacks=" + Attacks + "; unitAttacks=" + UnitAttacks + "; errors=" + Errors + "\n" +
                "assaults(side1/side2)=" + Side1Assaults + "/" + Side2Assaults +
                "; builtAtAssault(side1/side2)=" + Side1UnitsAtAssault + "/" + Side2UnitsAtAssault + "\n" +
                "unloads(side1/side2)=" + Side1Unloads + "/" + Side2Unloads +
                "; fiveCargo(side1)=" + Side1FiveCargoUnloads +
                "; fullCargo(side2)=" + Side2FullCargoUnloads + "\n" +
                "peakBindings(side1/side2)=" + MaxSide1Bindings + "/" + MaxSide2Bindings;
        }

        /// <summary>
        /// A versioned, byte-exact SHA-256 transcript. Include the state of *every*
        /// tick (not just the final winner), every simulation event, and every VM
        /// output, with sorted IDs and event field names. Raw Fixed numbers avoid
        /// any dependence on ToString/culture or process-specific object hashes.
        /// </summary>
        private sealed class MatchRecorder : IDisposable
        {
            private readonly SHA256 sha = SHA256.Create();
            private readonly CryptoStream stream;
            private readonly BinaryWriter writer;
            private bool completed;

            internal MatchRecorder(string side1, string side2, ulong seed, int steps, int seconds)
            {
                stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
                writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                writer.Write("Directiva.FullMatch.Transcript.v1");
                writer.Write(seed);
                writer.Write(steps);
                writer.Write(seconds);
                writer.Write(side1);
                writer.Write(side2);
            }

            internal void RecordOutput(VMOutputMessage message)
            {
                writer.Write((byte)1);
                writer.Write(message.Sequence);
                writer.Write(message.Tick);
                writer.Write(message.SideId);
                writer.Write(message.EntityId.HasValue);
                if (message.EntityId.HasValue) writer.Write(message.EntityId.Value);
                writer.Write((int)message.Kind);
                writer.Write(message.Text);
                writer.Write(message.SourceFile ?? "");
                writer.Write(message.SourceLine);
                writer.Write(message.SpanStart);
                writer.Write(message.SpanEnd);
            }

            internal void RecordWorld(Orchestrator orchestrator, World world)
            {
                writer.Write((byte)2);
                writer.Write(world.Tick);
                writer.Write(world.Time.RawValue);
                writer.Write(orchestrator.TotalVmSteps);
                writer.Write((int)orchestrator.Outcome);
                writer.Write((int)orchestrator.EndReason);
                for (int side = 1; side <= 2; side++)
                {
                    writer.Write(world.GetSide(side).Resources);
                    writer.Write(orchestrator.BindingCount(side));
                    writer.Write(world.GetSide(side).Nexus?.id ?? 0);
                }

                SBEntity[] entities = SimulationTestHarness.Entities(world)
                    .OrderBy(e => e.id).ToArray();
                writer.Write(entities.Length);
                foreach (SBEntity entity in entities)
                {
                    writer.Write(entity.id);
                    writer.Write(entity.side.id);
                    writer.Write(entity is SBNexus ? (byte)1 :
                        entity is SBUnit ? (byte)2 : entity is SBResource ? (byte)3 : (byte)0);
                    writer.Write(entity.alive);
                    writer.Write(entity.Position.X.RawValue);
                    writer.Write(entity.Position.Y.RawValue);
                    writer.Write(entity.Rotation.RawValue);
                    writer.Write(entity.Health.RawValue);
                    writer.Write(entity.Shields.RawValue);
                    writer.Write(entity.LastDamage.RawValue);
                    if (entity is SBUnit)
                    {
                        writer.Write(world.Observer.GetCargo(entity.id) ?? -1);
                        FixedVector2 velocity = world.Observer.GetVelocity(entity.id) ?? FixedVector2.Zero;
                        writer.Write(velocity.X.RawValue);
                        writer.Write(velocity.Y.RawValue);
                        writer.Write((int)world.Observer.GetMoveMode(entity.id).GetValueOrDefault());
                        writer.Write((int)world.Observer.GetActionMode(entity.id).GetValueOrDefault());
                        writer.Write(world.Observer.GetActionTarget(entity.id) ?? 0);
                    }
                    else if (entity is SBResource resource)
                    {
                        writer.Write(resource.Amount);
                        writer.Write(resource.GatherAmount);
                        writer.Write(resource.GatheringUnit?.id ?? 0);
                    }
                    else if (entity is SBNexus)
                    {
                        writer.Write(world.Observer.GetCurrentBuildRequestId(entity.id) ?? 0);
                        writer.Write((world.Observer.GetBuildProgress(entity.id) ?? (Fixed)0).RawValue);
                    }
                    if (orchestrator.TryGetVMStatus(entity.id, out var status))
                    {
                        writer.Write(true);
                        writer.Write((int)status);
                    }
                    else writer.Write(false);
                }
            }

            internal void RecordEvent(Event e)
            {
                writer.Write((byte)3);
                Type type = e.GetType();
                writer.Write(type.FullName ?? type.Name);
                // Reflection is confined to the Editor test; sorted fields keep the
                // transcript stable regardless of reflection enumeration order.
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
                writer.Write(fields.Length);
                foreach (FieldInfo field in fields)
                {
                    writer.Write(field.Name);
                    WriteValue(field.GetValue(e));
                }
            }

            private void WriteValue(object value)
            {
                if (value == null) { writer.Write((byte)0); return; }
                if (value is int integer) { writer.Write((byte)1); writer.Write(integer); return; }
                if (value is long longValue) { writer.Write((byte)2); writer.Write(longValue); return; }
                if (value is Fixed fixedValue)
                { writer.Write((byte)3); writer.Write(fixedValue.RawValue); return; }
                if (value is FixedVector2 point)
                {
                    writer.Write((byte)4);
                    writer.Write(point.X.RawValue);
                    writer.Write(point.Y.RawValue);
                    return;
                }
                if (value is bool boolean) { writer.Write((byte)5); writer.Write(boolean); return; }
                if (value is string text) { writer.Write((byte)6); writer.Write(text); return; }
                if (value.GetType().IsEnum)
                {
                    writer.Write((byte)7);
                    writer.Write(value.GetType().FullName ?? "");
                    writer.Write(Convert.ToInt64(value));
                    return;
                }
                throw new InvalidOperationException(
                    "A simulation event has an unhandled deterministic payload: " + value.GetType());
            }

            internal string Complete()
            {
                if (completed) throw new InvalidOperationException("Match hash already finalized.");
                completed = true;
                writer.Flush();
                stream.FlushFinalBlock();
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }

            public void Dispose()
            {
                writer.Dispose();
                stream.Dispose();
                sha.Dispose();
            }
        }

        private static DScriptCompiledProgram Compile(DirectivaTestContext t, string module, string script)
        {
            var source = new InMemoryDScriptSourceProvider(
                new Dictionary<string, string> { [module] = script });
            DScriptCompilationResult result = DScriptCompiler.Compile(
                module, source, SimulationDScriptProfile.Options);
            t.Attach(module + ".dscript", script);
            t.Check(result.Success, module + " compilation: " +
                string.Join("; ", result.Diagnostics));
            return result.Program;
        }

        private static void GiveResources(World world, int sideId, int amount)
        {
            PropertyInfo property = typeof(SBSide).GetProperty(nameof(SBSide.Resources));
            property.SetValue(world.GetSide(sideId), amount);
        }

        private static MatchResult Play(DScriptCompiledProgram firstProgram,
            DScriptCompiledProgram secondProgram, int maxSeconds)
        {
            var world = SimulationTestHarness.NewWorld(MatchSeed, 50, 50);
            var firstNexus = new SBNexus(world.GetSide(1), SimulationTestHarness.P(6, 25));
            var secondNexus = new SBNexus(world.GetSide(2), SimulationTestHarness.P(44, 25));
            world.EnqueueCreation(firstNexus);
            world.EnqueueCreation(secondNexus);
            // Four units are prepaid; the fifth must be financed by gathering.
            // Side 1 unloads in batches of five, side 2 only at full capacity.
            GiveResources(world, 1, 4 * SConstants.unitCost);
            GiveResources(world, 2, 4 * SConstants.unitCost);
            // Deposits are offset from the nexus-to-nexus combat corridor so
            // collision against an ore deposit cannot block the attack route.
            world.EnqueueCreation(new SBResource(world.GetSide(0),
                SimulationTestHarness.P(9, 20), 0, 100));
            world.EnqueueCreation(new SBResource(world.GetSide(0),
                SimulationTestHarness.P(41, 30), 0, 100));

            var result = new MatchResult();
            using (var recorder = new MatchRecorder(Side1Script, Side2Script,
                MatchSeed, RegressionStepsPerTick, maxSeconds))
            {
                var game = new Orchestrator(world, firstProgram, secondProgram,
                    stepsPerTick: RegressionStepsPerTick, maxDurationSeconds: maxSeconds);
                // Side shared initialization ran in the constructor, before
                // event subscription. Include its attributed output as well.
                foreach (VMOutputMessage bootstrap in game.GetOutput(1)
                    .Concat(game.GetOutput(2)).OrderBy(message => message.Sequence))
                {
                    recorder.RecordOutput(bootstrap);
                    if (bootstrap.Kind == VMOutputKind.Error) result.Errors++;
                }
                game.OutputEmitted += message =>
                {
                    recorder.RecordOutput(message);
                    if (message.Kind == VMOutputKind.Error) result.Errors++;
                    if (message.Kind == VMOutputKind.Print && message.Text == "Bando 1: ataque" && message.SideId == 1)
                    {
                        if (result.Side1Assaults == 0) result.Side1UnitsAtAssault = result.Side1Units;
                        result.Side1Assaults++;
                    }
                    if (message.Kind == VMOutputKind.Print && message.Text == "Bando 2: ataque" && message.SideId == 2)
                    {
                        if (result.Side2Assaults == 0) result.Side2UnitsAtAssault = result.Side2Units;
                        result.Side2Assaults++;
                    }
                };
                game.BindInitialEntity(firstNexus.id, "red.nexus_main");
                game.BindInitialEntity(secondNexus.id, "blue.nexus_main");

                // Include initial creations and state as well as every subsequent tick.
                recorder.RecordWorld(game, world);
                ConsumeEvents(world, recorder, result);
                while (!game.IsFinished && world.Tick < game.MaxSimulationTicks)
                {
                    game.Update();
                    result.MaxSide1Bindings = Math.Max(result.MaxSide1Bindings, game.BindingCount(1));
                    result.MaxSide2Bindings = Math.Max(result.MaxSide2Bindings, game.BindingCount(2));
                    recorder.RecordWorld(game, world);
                    ConsumeEvents(world, recorder, result);
                }
                result.Ticks = world.Tick;
                result.VmSteps = game.TotalVmSteps;
                result.Outcome = game.Outcome;
                result.EndReason = game.EndReason;
                result.Hash = recorder.Complete();
            }
            return result;
        }

        private static void ConsumeEvents(World world, MatchRecorder recorder, MatchResult result)
        {
            while (world.TryConsumeEvent(out Event e))
            {
                recorder.RecordEvent(e);
                if (e is EventCreateUnit built)
                {
                    result.CreatedUnits++;
                    result.BuiltUnitIds.Add(built.id);
                    result.UnitSideById.Add(built.id, built.side);
                    if (built.side == 1) result.Side1Units++;
                    else if (built.side == 2) result.Side2Units++;
                }
                else if (e is EventCargo cargo)
                {
                    if (cargo.cargo > 0)
                        result.PendingCargoByUnit[cargo.entityId] = cargo.cargo;
                }
                else if (e is EventGather) result.GatherStarts++;
                else if (e is EventResources) result.ResourceChanges++;
                else if (e is EventUnloadCargo unload)
                {
                    result.Unloads++;
                    if (result.UnitSideById.TryGetValue(unload.unitId, out int side))
                    {
                        if (side == 1) result.Side1Unloads++;
                        else if (side == 2) result.Side2Unloads++;
                        if (result.PendingCargoByUnit.TryGetValue(unload.unitId, out int quantity))
                        {
                            if (side == 1 && quantity == 5) result.Side1FiveCargoUnloads++;
                            if (side == 2 && world.Observer.GetCargoCapacity(unload.unitId) == quantity)
                                result.Side2FullCargoUnloads++;
                        }
                    }
                    result.PendingCargoByUnit.Remove(unload.unitId);
                }
                else if (e is EventAttack attack)
                {
                    result.Attacks++;
                    if (result.BuiltUnitIds.Contains(attack.attackerId))
                        result.UnitAttacks++;
                }
            }
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                tests.Add(new DirectivaTestCase("Simulation", "FullMatch", name, description, action));

            Add("TwoDScriptSidesSameSeedSameSHA256",
                "Two independent full matches, including economy and combat, have identical per-tick SHA-256 transcripts", t =>
            {
                t.Check(Side1Script != Side2Script, "distinct faction programs");
                DScriptCompiledProgram red = Compile(t, "red", Side1Script);
                DScriptCompiledProgram blue = Compile(t, "blue", Side2Script);
                MatchResult one = Play(red, blue, RegressionDurationSeconds);
                t.Attach("match_run_1.txt", one.Summary);
                t.Note("Match 1 SHA-256: " + one.Hash);
                // A full, freshly constructed world and scheduler. No state is
                // shared with the first run; only the immutable compiled code.
                MatchResult two = Play(red, blue, RegressionDurationSeconds);
                t.Attach("match_run_2.txt", two.Summary);
                t.Note("Match 2 SHA-256: " + two.Hash);

                t.Check(one.Outcome != MatchOutcome.InProgress, "match must terminate");
                t.Check(one.Ticks <= RegressionDurationSeconds * 50L, "maximum duration honored");
                t.Check(one.VmSteps > 0, "real VM work occurred");
                t.Equal(5, one.Side1Units, "side 1 built all five units");
                t.Equal(5, one.Side2Units, "side 2 built all five units");
                t.Equal(10, one.CreatedUnits, "ten real construction events");
                t.Check(one.GatherStarts > 0, "workers began gathering");
                t.Check(one.ResourceChanges > 0, "resource extraction occurred");
                t.Check(one.Side1FiveCargoUnloads > 0,
                    "side 1 delivered a batch of exactly five resources");
                t.Check(one.Side2FullCargoUnloads > 0,
                    "side 2 delivered a full cargo hold");
                t.Check(one.Side1Unloads > 0 && one.Side2Unloads > 0,
                    "both sides delivered gathered resources");
                t.Equal(1, one.Side1Assaults, "side 1 launched exactly once");
                t.Equal(1, one.Side2Assaults, "side 2 launched exactly once");
                t.Check(one.Side1UnitsAtAssault >= 5 && one.Side2UnitsAtAssault >= 5,
                    "both assaults start only after five units are actually built");
                t.Check(one.Attacks > 0, "combat actually occurred");
                t.Check(one.UnitAttacks > 0, "a script-controlled worker actually attacked");
                t.Equal(0, one.Errors, "no unexpected script runtime errors");
                t.Check(one.MaxSide1Bindings >= 6 && one.MaxSide2Bindings >= 6,
                    "each nexus and its five constructed units had VMs");
                t.Equal(one.Hash, two.Hash, "two complete match SHA-256 transcripts");
                t.Equal(one.Outcome, two.Outcome, "winner/draw must be identical");
                t.Equal(one.EndReason, two.EndReason, "termination reason must be identical");
                t.Equal(one.Ticks, two.Ticks, "termination tick must be identical");
                t.Equal(one.CreatedUnits, two.CreatedUnits, "build events identical");
                t.Equal(one.Side1FiveCargoUnloads, two.Side1FiveCargoUnloads,
                    "side 1 five-resource deliveries identical");
                t.Equal(one.Side2FullCargoUnloads, two.Side2FullCargoUnloads,
                    "side 2 full-load deliveries identical");
                t.Equal(one.Unloads, two.Unloads, "unload events identical");
                t.Equal(one.Attacks, two.Attacks, "attack events identical");
                t.Equal(one.UnitAttacks, two.UnitAttacks, "worker attacks identical");
            });

            Add("TimeoutProducesDrawAndStopsAtExactTick",
                "The default five-minute simulated duration is configurable; timeout is a draw and halts updates", t =>
            {
                var world = SimulationTestHarness.NewWorld(77UL, 50, 50);
                world.EnqueueCreation(new SBNexus(world.GetSide(1), SimulationTestHarness.P(5, 25)));
                world.EnqueueCreation(new SBNexus(world.GetSide(2), SimulationTestHarness.P(45, 25)));
                DScriptCompiledProgram program = Compile(t, "test", "def main():\n    pass\n");
                var game = new Orchestrator(world, program, program, maxDurationSeconds: 1);
                t.Equal(1, game.MaxDurationSeconds, "configurable seconds");
                t.Equal(50L, game.MaxSimulationTicks, "50 Hz one-second match cap");
                int endEvents = 0;
                game.MatchEnded += _ => endEvents++;
                for (int i = 0; i < 49; i++)
                {
                    game.Update();
                    t.Equal(MatchOutcome.InProgress, game.Outcome, "not timed out before tick 50");
                }
                game.Update();
                t.Equal(MatchOutcome.Draw, game.Outcome, "time limit is a draw");
                t.Equal(MatchEndReason.TimeLimit, game.EndReason, "timeout reason");
                t.Equal(50L, world.Tick, "stops at exactly 50 ticks");
                t.Equal((Fixed)1, world.Time, "one second of simulated time");
                t.Equal(50L, game.FinishedAtTick.GetValueOrDefault(), "finish tick");
                t.Equal(1, endEvents, "one completion event");
                game.Update();
                t.Equal(50L, world.Tick, "frozen world after timeout");
                t.Equal(1, endEvents, "completion event does not repeat");
            });

            Add("NexusVictoryBeatsTimeoutOnTheSameTick",
                "Destroying a nexus during the last allowed physics update is a win, not a timeout draw", t =>
            {
                var world = SimulationTestHarness.NewWorld(19UL, 50, 50);
                var first = new SBNexus(world.GetSide(1), SimulationTestHarness.P(20, 25));
                var second = new SBNexus(world.GetSide(2), SimulationTestHarness.P(25, 25));
                world.EnqueueCreation(first);
                world.EnqueueCreation(second);
                DScriptCompiledProgram program = Compile(t, "test", "def main():\n    pass\n");
                var game = new Orchestrator(world, program, program, maxDurationSeconds: 1);
                for (int i = 0; i < 49; i++) game.Update();
                t.Equal(MatchOutcome.InProgress, game.Outcome, "both nexuses survived 49 ticks");

                // Nexus attacks have a one-second cooldown. At time=0.98 the
                // normal next attack is still on cooldown; prepare a single
                // lethal attack so it occurs during the *50th* physics tick.
                MethodInfo damage = typeof(SBEntity).GetMethod("Damage",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo lastAttack = typeof(SBVMEntity).GetField("lastAttack",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                t.Check(damage != null && lastAttack != null, "test-only combat fixture fields");
                // After the first tick, second nexus lost 5 shield points, so
                // 144 additional damage leaves one point of health.
                damage.Invoke(second, new object[] { (Fixed)144 });
                t.Check(second.alive, "target survives with one health");
                lastAttack.SetValue(first, (Fixed)(-1));
                game.Update();
                t.Equal(50L, world.Tick, "death occurred exactly on deadline");
                t.Equal(MatchOutcome.Side1Won, game.Outcome, "nexus death decides match");
                t.Equal(MatchEndReason.NexusDestroyed, game.EndReason,
                    "victory takes precedence over the time-limit draw");
            });

            Add("DefaultDurationAndInvalidValues",
                "Matches default to 300 simulated seconds and reject nonpositive duration settings", t =>
            {
                var world = SimulationTestHarness.NewWorld(10UL, 50, 50);
                world.EnqueueCreation(new SBNexus(world.GetSide(1), SimulationTestHarness.P(5, 25)));
                world.EnqueueCreation(new SBNexus(world.GetSide(2), SimulationTestHarness.P(45, 25)));
                DScriptCompiledProgram program = Compile(t, "test", "def main():\n    pass\n");
                bool rejected = false;
                try { _ = new Orchestrator(world, program, program, maxDurationSeconds: 0); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                t.Check(rejected, "zero-duration matches are rejected");
                var game = new Orchestrator(world, program, program);
                t.Equal(300, game.MaxDurationSeconds, "default is five minutes");
                t.Equal(15000L, game.MaxSimulationTicks, "default duration is 15000 ticks");
                t.Equal(MatchEndReason.None, game.EndReason, "unfinished match has no end reason");
            });
        }
    }
}
