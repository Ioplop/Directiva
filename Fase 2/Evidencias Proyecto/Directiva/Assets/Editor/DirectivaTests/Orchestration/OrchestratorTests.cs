using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DMath;
using DOrchestration;
using DSimulation;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;
using DSExecution.VirtualMachine;
using Directiva.EditorTests.SimulationTests;
using World = DSimulation.Simulation;

namespace Directiva.EditorTests.OrchestrationTests
{
    /// <summary>End-to-end scheduling, lifecycle, construction, logging and match tests.</summary>
    internal static class OrchestratorTests
    {
        private sealed class Fixture
        {
            internal World World;
            internal SBNexus FirstNexus;
            internal SBNexus SecondNexus;
            internal SBUnit FirstUnit;
            internal SBUnit SecondUnit;
            internal SBUnit Ally;
        }

        private static Fixture NewFixture(ulong seed = 123UL, bool withAlly = false)
        {
            World world = SimulationTestHarness.NewWorld(seed, 50, 50);
            var first = new SBNexus(world.GetSide(1), SimulationTestHarness.P(6, 25));
            var second = new SBNexus(world.GetSide(2), SimulationTestHarness.P(44, 25));
            SBUnit unit1 = SimulationTestHarness.AddUnit(world, 1, 6, 6, 0);
            SBUnit unit2 = SimulationTestHarness.AddUnit(world, 2, 44, 6, 0);
            SBUnit ally = withAlly ? SimulationTestHarness.AddUnit(world, 1, 10, 6, 0) : null;
            world.EnqueueCreation(first);
            world.EnqueueCreation(second);
            return new Fixture { World = world, FirstNexus = first,
                SecondNexus = second, FirstUnit = unit1, SecondUnit = unit2, Ally = ally };
        }

        private static DScriptCompiledProgram Compile(DirectivaTestContext t, string script)
        {
            var provider = new InMemoryDScriptSourceProvider(
                new Dictionary<string, string> { ["test"] = script });
            DScriptCompilationResult result = DScriptCompiler.Compile("test", provider,
                SimulationDScriptProfile.Options);
            t.Attach("test.dscript", script);
            t.Check(result.Success, "DScript compilation: " + string.Join("; ", result.Diagnostics));
            return result.Program;
        }

        private static Orchestrator Create(DirectivaTestContext t, Fixture f, string firstScript,
            string secondScript = null, int steps = 10, int logCapacity = 4096)
        {
            DScriptCompiledProgram first = Compile(t, firstScript);
            DScriptCompiledProgram second = secondScript == null ? first : Compile(t, secondScript);
            return new Orchestrator(f.World, first, second, steps, logCapacity);
        }

        private static List<VMOutputMessage> Messages(Orchestrator orchestrator, int side,
            VMOutputKind kind) => orchestrator.GetOutput(side, kind: kind).ToList();

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                tests.Add(new DirectivaTestCase("Simulation", "Orchestrator", name, description, action));

            Add("OneWorldTick", "One orchestrator update advances exactly one simulation tick at 50 Hz", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main():\n    pass\n");
                t.Equal(0L, f.World.Tick, "Start must not advance time");
                o.Update();
                t.Equal(1L, f.World.Tick, "one tick");
                t.Equal(SConstants.deltaTime, f.World.Time, "fixed delta time");
                o.Update();
                t.Equal(2L, f.World.Tick, "two ticks");
                t.Equal(SConstants.deltaTime * 2, f.World.Time, "exact fixed time");
            });

            Add("BudgetPerEntity", "Exactly one logical step per entity in each configured round", t =>
            {
                Fixture f = NewFixture(withAlly: true);
                Orchestrator o = Create(t, f, "def main():\n    pass\n", steps: 2);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                o.BindInitialEntity(f.SecondUnit.id, "test.main");
                o.BindInitialEntity(f.Ally.id, "test.main");
                o.Update();
                t.Equal(6L, o.TotalVmSteps, "3 VMs x 2 effective steps");
                o.Update();
                t.Equal(12L, o.TotalVmSteps, "budget replenished next world tick");
                o.StepsPerTick = 3;
                o.Update();
                t.Equal(21L, o.TotalVmSteps, "new configurable budget");
            });

            Add("InvalidBudget", "The scheduler rejects zero and negative step budgets", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main():\n    pass\n");
                bool rejected = false;
                try { o.StepsPerTick = 0; }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                t.Check(rejected, "zero budget forbidden");
                t.Equal(10, o.StepsPerTick, "invalid assignment has no effects");
            });

            Add("GlobalRoundRobinOrder", "Logs reveal stable interleaving of two same-side entity VMs", t =>
            {
                Fixture f = NewFixture(withAlly: true);
                Orchestrator o = Create(t, f, "def main():\n    print(self.id)\n", steps: 30);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                o.BindInitialEntity(f.Ally.id, "test.main");
                o.Update();
                List<VMOutputMessage> lines = Messages(o, 1, VMOutputKind.Print);
                t.Check(lines.Count >= 4, "both VMs print multiple times");
                for (int i = 0; i < lines.Count; i++)
                    t.Equal(i % 2 == 0 ? f.FirstUnit.id : f.Ally.id,
                        lines[i].EntityId.GetValueOrDefault(), "entity order at print #" + i);
                t.Equal(1L, f.World.Tick, "one world update after 30 scheduling rounds");
            });

            Add("NaturalRestart", "A completed entry restarts while preserving shared globals", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f,
                    "shared counter = 0\ndef main():\n    counter += 1\n    print(counter)\n",
                    steps: 70);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                o.Update();
                List<string> texts = Messages(o, 1, VMOutputKind.Print).Select(m => m.Text).ToList();
                t.Check(texts.Count >= 2, "VM must complete and restart within a single world tick");
                t.Equal("1", texts[0], "first invocation");
                t.Equal("2", texts[1], "second invocation reuses shared counter");
                t.Check(!o.GetOutput(1).Any(m => m.Kind == VMOutputKind.Error), "no runtime faults");
            });

            Add("RuntimeErrorOnce", "Faulted VMs stop and report one attributed error", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main():\n    return 1 / 0\n", steps: 30);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                o.Update();
                t.Check(o.TryGetVMStatus(f.FirstUnit.id, out VMExecutionStatus status), "binding is visible");
                t.Equal(VMExecutionStatus.Faulted, status, "division by zero faults VM");
                List<VMOutputMessage> errors = Messages(o, 1, VMOutputKind.Error);
                t.Equal(1, errors.Count, "one error reported");
                t.Equal(1L, o.GetErrorCount(1), "blue side accumulates one error");
                t.Equal(0L, o.GetErrorCount(2), "red side error count remains zero");
                t.Equal(f.FirstUnit.id, errors[0].EntityId.GetValueOrDefault(), "origin entity");
                t.Check(!string.IsNullOrEmpty(errors[0].Text), "error text is preserved");
                o.Update();
                t.Equal(1, Messages(o, 1, VMOutputKind.Error).Count, "not repeated on later ticks");
                t.Equal(1L, o.GetErrorCount(1), "fault counted once across later ticks");
            });

            Add("BreakpointPausesBeforeWorldAndResumesSameTick",
                "Any friendly VM breakpoint freezes physics; Step advances only that VM", t =>
            {
                Fixture f = NewFixture(withAlly: true);
                Orchestrator o = Create(t, f,
                    "def main():\n    print(self.id)\n    pass\n", steps: 3);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                o.BindInitialEntity(f.Ally.id, "test.main");
                int hits = 0;
                o.BreakpointPredicate = (side, id, file, line) =>
                    side == 1 && id == f.FirstUnit.id && file == "test.dscript" && line == 2;
                o.BreakpointHit += (id, debug) => hits++;
                o.Update();
                t.Equal(1, hits, "one breakpoint at the requested source line");
                t.Check(o.IsPausedAtBreakpoint, "match suspended by source breakpoint");
                t.Check(o.HasPendingTick, "partially scheduled tick retained");
                t.Equal(0L, f.World.Tick, "simulation physics has not advanced");
                long beforeStep = o.TotalVmSteps;
                t.Check(o.StepEntity(f.FirstUnit.id), "manually advance the paused VM");
                t.Equal(beforeStep + 1, o.TotalVmSteps, "exactly one charged step");
                t.Equal(0L, f.World.Tick, "manual step does not move the world");
                t.Check(o.TryGetDebugState(f.FirstUnit.id, out var debug),
                    "selected entity exposes current source position");
                t.Check(debug != null, "debug snapshot available");
                o.BreakpointPredicate = null;
                o.ResumeFromBreakpoint();
                o.Update();
                t.Equal(1L, f.World.Tick, "interrupted tick finishes exactly once");
                t.Check(!o.HasPendingTick, "no stale tick cursor after resume");
                t.Equal(6L, o.TotalVmSteps,
                    "2 VMs x 3 scheduled turns, including the single manual step");
            });

            Add("FaultRecoveryPreservesSharedMemory",
                "Faulted entity VM restarts after simulated delay without resetting side globals", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f,
                    "shared n = 0\ndef main():\n    n += 1\n    print(n)\n    return 1 / 0\n",
                    steps: 25);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                int faults = 0;
                int recovered = 0;
                o.EntityFaulted += id => { if (id == f.FirstUnit.id) faults++; };
                o.EntityRecovered += id => { if (id == f.FirstUnit.id) recovered++; };
                o.Update();
                t.Equal(1, faults, "initial script failure reported exactly once");
                t.Equal(0, recovered, "recovery is not immediate");
                long due = o.FaultRestartDelayTicks;
                for (long i = 0; i <= due + 2; i++) o.Update();
                t.Check(recovered >= 1, "VM was restarted after the delay");
                var printed = Messages(o, 1, VMOutputKind.Print).Select(m => m.Text).ToArray();
                t.Check(printed.Contains("1") && printed.Contains("2"),
                    "shared counter was not reinitialized during recovery");
                t.Check(o.GetErrorCount(1) >= 2, "later faults count separately");
            });

            Add("LiveMatchStats", "Memory usage is side-specific and errors survive bounded output eviction", t =>
            {
                Fixture memoryFixture = NewFixture();
                Orchestrator memory = Create(t, memoryFixture,
                    "shared items = [1, 2, 3]\ndef main():\n    pass\n",
                    "def main():\n    pass\n");
                t.Check(memory.GetHeapUsage(1) > 0UL,
                    "side one has a heap allocation from its shared list");
                t.Equal(0UL, memory.GetHeapUsage(2),
                    "side two has no heap allocation from its no-op program");

                Fixture errorsFixture = NewFixture(withAlly: true);
                Orchestrator match = Create(t, errorsFixture,
                    "def boom():\n    return 1 / 0\n" +
                    "def chatter():\n    print('still running')\n",
                    steps: 80, logCapacity: 1);
                match.BindInitialEntity(errorsFixture.FirstUnit.id, "test.boom");
                match.BindInitialEntity(errorsFixture.Ally.id, "test.chatter");
                match.Update();
                t.Equal(1L, match.GetErrorCount(1),
                    "one failing VM is counted regardless of other logs");
                t.Equal(0, Messages(match, 1, VMOutputKind.Error).Count,
                    "bounded buffer has already evicted the error message");
                t.Equal(0L, match.GetErrorCount(2), "no failures on the enemy side");
            });

            Add("ImplicitOrderBeforePhysics", "Script orders execute before this tick's physical movement", t =>
            {
                Fixture f = NewFixture();
                var original = f.FirstUnit.Position;
                Orchestrator o = Create(t, f, "def main():\n    move(20, 20)\n", steps: 35);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                o.Update();
                t.Check(f.FirstUnit.Position != original, "unit moves during the same Simulation.Update");
                t.Equal(1L, f.World.Tick, "exactly one physical tick");
            });

            Add("DeadEntitySkipped", "Killed entities never run and bindings are removed", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main():\n    print('alive')\n", steps: 35);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                SimulationTestHarness.Kill(f.FirstUnit);
                o.Update();
                t.Equal(0, Messages(o, 1, VMOutputKind.Print).Count, "dead unit never prints");
                t.Equal(0, o.BindingCount(1), "dead unit detached after physics");
                t.Check(!o.TryGetVMStatus(f.FirstUnit.id, out _), "VM removed");
            });

            Add("DiedAfterPreviousTick", "Bindings retain logs when an entity dies between ticks", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main():\n    print('before')\n", steps: 35);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                o.Update();
                t.Check(Messages(o, 1, VMOutputKind.Print).Count > 0, "unit emitted logs");
                SimulationTestHarness.Kill(f.FirstUnit);
                o.Update();
                t.Equal(0, o.BindingCount(1), "VM unbound");
                t.Check(o.GetOutput(1, f.FirstUnit.id).Count > 0,
                    "historical messages survive unbinding");
            });

            Add("WinnerWhenNexusDies", "Eliminating the opposing nexus ends the match", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main():\n    print('go')\n");
                int ended = 0;
                o.MatchEnded += _ => ended++;
                SimulationTestHarness.Kill(f.SecondNexus);
                o.Update();
                t.Equal(MatchOutcome.Side1Won, o.Outcome, "side 1 wins");
                t.Equal(0L, f.World.Tick, "external death stops scheduling before physics");
                t.Equal(0L, o.FinishedAtTick.GetValueOrDefault(), "finish tick");
                o.Update();
                t.Equal(0L, f.World.Tick, "frozen world remains frozen");
                t.Equal(1, ended, "exactly one end event");
            });

            Add("DrawOnSimultaneousNexusDeaths", "Two nexuses dead at the same boundary produce a draw", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main():\n    pass\n");
                SimulationTestHarness.Kill(f.FirstNexus);
                SimulationTestHarness.Kill(f.SecondNexus);
                o.Update();
                t.Equal(MatchOutcome.Draw, o.Outcome, "draw");
                t.Check(o.IsFinished, "match is finished");
                t.Equal(0L, f.World.Tick, "no additional world update");
            });

            Add("VictoryAfterSimulationUpdate", "Nexus death during the world tick is evaluated after attacks", t =>
            {
                World world = SimulationTestHarness.NewWorld(71UL, 50, 50);
                var first = new SBNexus(world.GetSide(1), SimulationTestHarness.P(20, 25));
                var second = new SBNexus(world.GetSide(2), SimulationTestHarness.P(25, 25));
                world.EnqueueCreation(first);
                world.EnqueueCreation(second);
                var program = Compile(t, "def main():\n    pass\n");
                var o = new Orchestrator(world, program, program);
                MethodInfo damage = typeof(SBEntity).GetMethod("Damage",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                t.Check(damage != null, "test can set up almost-dead nexus");
                damage.Invoke(second, new object[] { (Fixed)149 });
                t.Check(second.alive, "second nexus survives setup with 1 health");
                o.Update();
                t.Equal(1L, world.Tick, "world advanced once");
                t.Equal(MatchOutcome.Side1Won, o.Outcome, "first nexus killed second in world update");
                o.Update();
                t.Equal(1L, world.Tick, "no further update after victory");
            });

            Add("RequiresBothNexuses", "A match requires both nexuses before orchestration starts", t =>
            {
                World world = SimulationTestHarness.NewWorld();
                var program = Compile(t, "def main():\n    pass\n");
                bool failed = false;
                try { _ = new Orchestrator(world, program, program); }
                catch (InvalidOperationException) { failed = true; }
                t.Check(failed, "missing nexuses rejected");
            });

            Add("InitialBindingsBeforeStartOnly", "Entry functions must be assigned before the first tick", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main():\n    pass\n");
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                o.Update();
                bool failed = false;
                try { o.BindInitialEntity(f.SecondUnit.id, "test.main"); }
                catch (InvalidOperationException) { failed = true; }
                t.Check(failed, "late manual initial binding rejected");
            });

            Add("InvalidEntryArity", "An initial entity entry must be a compiled zero-argument function", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main(arg):\n    pass\n");
                bool failed = false;
                try { o.BindInitialEntity(f.FirstUnit.id, "test.main"); }
                catch (ArgumentException) { failed = true; }
                t.Check(failed, "parameterized entry rejected");
            });

            Add("SideLogsIsolatedAndOrdered", "Every print has a side, entity and global sequence", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main():\n    print(self.id)\n", steps: 25);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                o.BindInitialEntity(f.SecondUnit.id, "test.main");
                o.Update();
                var first = Messages(o, 1, VMOutputKind.Print);
                var second = Messages(o, 2, VMOutputKind.Print);
                t.Check(first.Count >= 1 && second.Count >= 1, "both teams printed");
                t.Check(first.All(m => m.SideId == 1 && m.EntityId == f.FirstUnit.id), "first-side attribution");
                t.Check(second.All(m => m.SideId == 2 && m.EntityId == f.SecondUnit.id), "second-side attribution");
                var sequences = first.Concat(second).Select(m => m.Sequence).OrderBy(x => x).ToArray();
                t.Equal(sequences.Length, sequences.Distinct().Count(), "global sequence unique");
                t.Check(sequences.Length == 1 || sequences[0] < sequences[1], "global ordering");
            });

            Add("WarningsAttributed", "Simulation warnings retain originating VM and source", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f,
                    "def child():\n    pass\ndef main():\n    build(child)\n", steps: 20);
                o.BindInitialEntity(f.FirstUnit.id, "test.main"); // build is nexus-only
                o.Update();
                var warning = Messages(o, 1, VMOutputKind.Warning);
                t.Check(warning.Count > 0, "unsupported order warns");
                t.Equal(f.FirstUnit.id, warning[0].EntityId.GetValueOrDefault(), "warning entity");
                t.Check(!string.IsNullOrEmpty(warning[0].Text), "warning message");
            });

            Add("BootstrapLogsWithoutEntity", "Each side's initialization is logged without an entity ID", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f,
                    "shared greeting = print('boot')\ndef main():\n    pass\n");
                var first = Messages(o, 1, VMOutputKind.Print);
                var second = Messages(o, 2, VMOutputKind.Print);
                t.Equal(1, first.Count, "side1 initializer prints once");
                t.Equal(1, second.Count, "side2 initializer prints once");
                t.Equal("boot", first[0].Text, "initialization text");
                t.Check(!first[0].EntityId.HasValue && !second[0].EntityId.HasValue,
                    "bootstrap prints do not invent a VM entity");
                t.Equal(0, o.GetOutput(1, f.FirstUnit.id).Count, "entity filter excludes bootstrap");
            });

            Add("BoundedLogs", "Deterministic log capacity keeps the newest messages by side", t =>
            {
                Fixture f = NewFixture();
                Orchestrator o = Create(t, f, "def main():\n    print('a')\n", steps: 80,
                    logCapacity: 3);
                o.BindInitialEntity(f.FirstUnit.id, "test.main");
                o.Update();
                var output = Messages(o, 1, VMOutputKind.Print);
                t.Equal(3, output.Count, "side buffer bounded");
                t.Check(output[0].Sequence < output[1].Sequence &&
                    output[1].Sequence < output[2].Sequence, "newest messages preserved in order");
            });

            Add("SidesKeepIndependentShared", "The same compiled program has separate global state per side", t =>
            {
                Fixture f = NewFixture();
                var program = Compile(t,
                    "shared value = 0\ndef write():\n    value = 7\n" +
                    "def read():\n    print(value)\n");
                var o = new Orchestrator(f.World, program, program, stepsPerTick: 60);
                o.BindInitialEntity(f.FirstUnit.id, "test.write");
                o.BindInitialEntity(f.SecondUnit.id, "test.read");
                o.Update();
                var output = Messages(o, 2, VMOutputKind.Print);
                t.Check(output.Count >= 1, "second side reads globals");
                t.Check(output.All(m => m.Text == "0"), "enemy cannot observe our shared changes");
            });

            Add("BuildBindsChildNextTick", "Finished build request creates an entity VM with its chosen entry", t =>
            {
                Fixture f = NewFixture();
                // NewFixture enqueues initial entities. They are not registered until
                // the Orchestrator constructor calls Simulation.Start().
                t.Equal(0, f.World.GetSide(1).Units.Count,
                    "fixture's initial unit has not yet been registered");
                PropertyInfo resources = typeof(SBSide).GetProperty(nameof(SBSide.Resources));
                resources.SetValue(f.World.GetSide(1), 40);
                Orchestrator o = Create(t, f,
                    "def child():\n    print('born')\ndef main():\n    build(child)\n",
                    secondScript: "def main():\n    pass\n", steps: 35);
                int originalUnitCount = f.World.GetSide(1).Units.Count;
                t.Equal(1, originalUnitCount,
                    "orchestrator startup registers exactly one friendly unit");
                o.BindInitialEntity(f.FirstNexus.id, "test.main");
                o.Update(); // Accept the request and start its construction.
                FieldInfo request = typeof(SBNexus).GetField("buildingRequestId",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo progress = typeof(SBNexus).GetField("buildTime",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                t.Check(request != null && progress != null, "build state fields present");
                t.Check(request.GetValue(f.FirstNexus) != null, "a build was requested");
                progress.SetValue(f.FirstNexus, SConstants.unitBuildTime - SConstants.deltaTime);
                o.Update(); // Completes construction at the end of this update.
                t.Equal(originalUnitCount + 1, f.World.GetSide(1).Units.Count,
                    "exactly one new unit registered");
                t.Check(f.World.GetSide(1).Units.Contains(f.FirstUnit),
                    "the fixture's original unit is still present");
                SBUnit child = f.World.GetSide(1).Units.Single(unit => unit.id != f.FirstUnit.id);
                t.Equal(2, o.BindingCount(1), "nexus and child have VMs");
                t.Check(o.TryGetVMStatus(child.id, out _), "new child received a VM");
                t.Check(!o.TryGetVMStatus(f.FirstUnit.id, out _),
                    "unbound fixture unit must not receive the new VM");
                t.Equal(0, o.GetOutput(1, child.id).Count,
                    "child has not executed in its creation tick");
                o.Update(); // The child participates in the new snapshot.
                var childPrints = o.GetOutput(1, child.id, VMOutputKind.Print);
                t.Check(childPrints.Any(m => m.Text == "born"),
                    "child runs the function selected by build(entry_function)");
            });

            Add("RepeatableDeterminism", "Matching worlds and programs generate identical outputs and positions", t =>
            {
                var a = NewFixture(17UL);
                var b = NewFixture(17UL);
                const string script = "def main():\n    move(22, 20)\n    print(self.id)\n";
                var program = Compile(t, script);
                var left = new Orchestrator(a.World, program, program, stepsPerTick: 15);
                var right = new Orchestrator(b.World, program, program, stepsPerTick: 15);
                left.BindInitialEntity(a.FirstUnit.id, "test.main");
                left.BindInitialEntity(a.SecondUnit.id, "test.main");
                right.BindInitialEntity(b.FirstUnit.id, "test.main");
                right.BindInitialEntity(b.SecondUnit.id, "test.main");
                for (int tick = 0; tick < 12; tick++)
                {
                    left.Update();
                    right.Update();
                    t.Equal(a.FirstUnit.Position, b.FirstUnit.Position, "side1 position at tick " + tick);
                    t.Equal(a.SecondUnit.Position, b.SecondUnit.Position, "side2 position at tick " + tick);
                }
                foreach (int side in new[] { 1, 2 })
                {
                    var first = left.GetOutput(side).Select(m =>
                        m.Sequence + ":" + m.Tick + ":" + m.EntityId + ":" + m.Kind + ":" + m.Text);
                    var second = right.GetOutput(side).Select(m =>
                        m.Sequence + ":" + m.Tick + ":" + m.EntityId + ":" + m.Kind + ":" + m.Text);
                    t.Equal(string.Join("|", first), string.Join("|", second),
                        "log replay for side " + side);
                }
            });
        }
    }
}
