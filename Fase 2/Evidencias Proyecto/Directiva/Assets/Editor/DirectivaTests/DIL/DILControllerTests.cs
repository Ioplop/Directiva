using System;
using System.Collections.Generic;
using System.Linq;
using Directiva.CodeUI;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Tests the DIL execution adapter without constructing any Unity scene/UI.</summary>
    internal static class DILControllerTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string purpose, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Controller", id, purpose, action);

            Add("StartPauseContinueTick", "Lifecycle state, pause budget and completion event", t =>
            {
                var vm = DILTestHarness.MakeVM(t, "PUSH INT 5\nRETURN");
                var controller = new VMExecutionController();
                int completed = 0;
                DataValue result = default;
                controller.Completed += v => { completed++; result = v; };
                controller.Start(vm, paused: true);
                t.Equal(ScriptExecutionState.Paused, controller.State, "initial paused state");
                controller.Tick(50);
                t.Equal(0L, controller.TotalAdvances, "paused Tick must not execute");
                t.Equal(0L, controller.TotalLogicalSteps, "paused Tick consumes no budget");
                controller.Continue();
                controller.Tick(1);
                t.Equal(ScriptExecutionState.Running, controller.State, "budgeted running state");
                controller.Tick(4);
                t.Equal(ScriptExecutionState.Stopped, controller.State, "completed controller state");
                t.Equal(1, completed, "completed event count");
                t.Equal(5L, unchecked((long)result.value), "completed value");
                t.Equal(2L, controller.TotalAdvances, "exact instructions executed");
                t.Equal(2L, controller.TotalLogicalSteps, "two logical steps executed");
            });
            Add("StepByStep", "Paused Step executes precisely one VM instruction", t =>
            {
                var controller = new VMExecutionController();
                var vm = DILTestHarness.MakeVM(t, "PUSH INT 3\nPUSH INT 4\nADD\nRETURN");
                controller.Start(vm, paused: true);
                controller.Step(false);
                t.Equal(1, vm.InstructionPointer, "first IP");
                t.Equal(1L, controller.TotalAdvances, "single step advances");
                t.Equal(1L, controller.TotalLogicalSteps, "one logical step");
                t.Equal(ScriptExecutionState.Paused, controller.State, "remains paused");
                controller.Step(false);
                t.Equal(2, vm.InstructionPointer, "second IP");
                controller.Stop();
                t.Equal(ScriptExecutionState.Stopped, controller.State, "stopped after step");
            });
            Add("SkipDebugMetadata", "Step(skipDebugMetadata) executes a source instruction, not DEBUG_* steps", t =>
            {
                var code = DILTestHarness.Parse(t, "PUSH INT 12\nRETURN",
                    options: new DILParserOptions(generateDebugMetadata: true));
                var vm = DILTestHarness.MakeVM(code);
                var controller = new VMExecutionController();
                controller.Start(vm, paused: true);
                controller.Step(true);
                t.Equal(1, vm.EvaluationStackCount, "one source PUSH executed");
                t.Equal(ScriptExecutionState.Paused, controller.State, "debug step remains paused");
                t.Check(controller.TotalAdvances >= 4, "metadata steps should be transparently consumed");
                t.Equal(1L, controller.TotalLogicalSteps, "DEBUG metadata has zero cost");
                controller.Step(true);
                t.Equal(ScriptExecutionState.Stopped, controller.State, "source RETURN completes");
                t.Equal(2L, controller.TotalLogicalSteps, "PUSH and RETURN each cost one");
            });
            Add("TenStepLogicalBudget", "Tick(10) reports exactly ten VM logical steps even with DEBUG metadata", t =>
            {
                var body = string.Concat(System.Linq.Enumerable.Repeat("PASS\n", 12)) + "PUSH INT 9\nRETURN";
                var vm = DILTestHarness.MakeVM(t, body,
                    parserOptions: new DILParserOptions(generateDebugMetadata: true));
                var controller = new VMExecutionController();
                controller.Start(vm);
                controller.Tick(10);
                t.Equal(ScriptExecutionState.Running, controller.State, "budget leaves program running");
                t.Equal(10L, controller.TotalLogicalSteps, "ten steps must be exactly ten");
                t.Equal(40L, controller.TotalAdvances, "each instruction has three zero-cost debug entries");
                controller.Tick(10);
                t.Equal(ScriptExecutionState.Stopped, controller.State, "program completed");
                t.Equal(14L, controller.TotalLogicalSteps, "12 PASS + PUSH + RETURN");
                t.Equal(56L, controller.TotalAdvances, "all debug instructions still execute");
            });
            Add("ManualDebugSteps", "Step(false) counts DEBUG metadata as advances but not logical steps", t =>
            {
                var vm = DILTestHarness.MakeVM(t,
                    "DEBUG_FILE \"main.dscript\"\nDEBUG_LINE 2\nDEBUG_SPAN 0 5\nPUSH INT 7\nRETURN");
                var controller = new VMExecutionController();
                controller.Start(vm, paused: true);
                for (int i = 1; i <= 3; i++)
                {
                    controller.Step(false);
                    t.Equal((long)i, controller.TotalAdvances, "debug advance " + i);
                    t.Equal(0L, controller.TotalLogicalSteps, "debug cost is always zero");
                }
                controller.Step(false);
                t.Equal(1L, controller.TotalLogicalSteps, "PUSH costs one");
                controller.Step(false);
                t.Equal(2L, controller.TotalLogicalSteps, "RETURN costs one");
                t.Equal(ScriptExecutionState.Stopped, controller.State, "completed");
            });
            Add("BreakpointLogicalSteps", "Breakpoints report cost of executed instructions, not debug metadata", t =>
            {
                var vm = DILTestHarness.MakeVM(t,
                    "DEBUG_FILE \"main.dscript\"\nDEBUG_LINE 2\nDEBUG_SPAN 0 5\nPUSH INT 7\nRETURN");
                var controller = new VMExecutionController();
                long stepsAtPause = -1;
                controller.StateChanged += state =>
                {
                    if (state == ScriptExecutionState.Paused)
                        stepsAtPause = controller.TotalLogicalSteps;
                };
                controller.BreakpointPredicate = current => current.InstructionPointer == 4;
                controller.Start(vm);
                controller.Tick(10);
                t.Equal(ScriptExecutionState.Paused, controller.State, "paused before RETURN");
                t.Equal(1L, stepsAtPause, "only PUSH has consumed a logical step");
                t.Equal(4L, controller.TotalAdvances, "three DEBUG entries and PUSH executed");
                controller.Continue();
                controller.Tick(10);
                t.Equal(2L, controller.TotalLogicalSteps, "one more step after continue");
                t.Equal(ScriptExecutionState.Stopped, controller.State, "finished on return");
            });
            Add("LogicalCounterResets", "Starting a different VM resets both counters", t =>
            {
                var controller = new VMExecutionController();
                controller.Start(DILTestHarness.MakeVM(t, "PUSH INT 1\nRETURN"));
                controller.Tick(10);
                t.Equal(2L, controller.TotalLogicalSteps, "first run");
                controller.Start(DILTestHarness.MakeVM(t,
                    "DEBUG_FILE \"main.dscript\"\nPUSH NONE\nRETURN"), paused: true);
                t.Equal(0L, controller.TotalLogicalSteps, "new run resets logical counter");
                t.Equal(0L, controller.TotalAdvances, "new run resets advance counter");
                controller.Step(false);
                t.Equal(0L, controller.TotalLogicalSteps, "metadata stays free");
                controller.Stop();
                t.Equal(0L, controller.TotalLogicalSteps, "Stop preserves final count");
            });
            Add("ContinuationLogicalBudget", "Continued COPY consumes its real budget, excluding DEBUG metadata", t =>
            {
                string elements = string.Join("\n", System.Linq.Enumerable.Range(0, 24)
                    .Select(i => "PUSH INT " + i));
                var code = DILTestHarness.Parse(t,
                    elements + "\nLIST 24\nCOPY\nRETURN",
                    options: new DILParserOptions(generateDebugMetadata: true));
                var reference = DILTestHarness.MakeVM(code);
                var controller = new VMExecutionController();
                controller.Start(DILTestHarness.MakeVM(code));
                long referenceSteps = 0;
                long referenceAdvances = 0;
                for (int i = 0; i < 30 && reference.Status == VMExecutionStatus.Running; i++)
                {
                    var batch = reference.Run(7);
                    referenceSteps += batch.StepsExecuted;
                    referenceAdvances += batch.AdvancesExecuted;
                    controller.Tick(7);
                    t.Equal(referenceSteps, controller.TotalLogicalSteps,
                        "logical cost agrees with VM.Run after batch " + i);
                    t.Equal(referenceAdvances, controller.TotalAdvances,
                        "advances agree with VM.Run after batch " + i);
                }
                t.Equal(VMExecutionStatus.Completed, reference.Status, "reference VM completed");
                t.Equal(ScriptExecutionState.Stopped, controller.State, "controlled VM completed");
                t.Check(referenceSteps > 27, "COPY should require continuation steps");
                t.Check(referenceAdvances > referenceSteps, "DEBUG metadata should remain zero-cost");
            });
            Add("PrintedAndFaultedEvents", "Adapter forwards PRINT and runtime failure events", t =>
            {
                var controller = new VMExecutionController();
                string output = null;
                RuntimeError observed = null;
                controller.Printed += s => output = s;
                controller.Faulted += e => observed = e;
                controller.Start(DILTestHarness.MakeVM(t, "PUSH STRING \"hello\"\nPRINT\nPOP\nRETURN"));
                controller.Tick(100);
                t.Equal("hello", output, "forwarded output");
                t.Check(observed != null, "missing faulted event");
                t.Equal(RuntimeErrorId.StackUnderflow, observed.Id, "error forwarded");
                t.Equal(ScriptExecutionState.Stopped, controller.State, "controller stopped on fault");
            });
            Add("BreakpointAndSuppression", "Breakpoint fires before instruction; Continue skips same location once", t =>
            {
                var controller = new VMExecutionController();
                var vm = DILTestHarness.MakeVM(t, "PUSH INT 8\nRETURN");
                int hits = 0;
                controller.BreakpointPredicate = v => v.InstructionPointer == 0;
                controller.BreakpointHit += v => hits++;
                controller.Start(vm);
                controller.Tick(10);
                t.Equal(ScriptExecutionState.Paused, controller.State, "breakpoint paused");
                t.Equal(0, vm.InstructionPointer, "breakpoint before instruction");
                t.Equal(1, hits, "initial breakpoint hit");
                controller.Continue();
                controller.Tick(10);
                t.Equal(ScriptExecutionState.Stopped, controller.State, "resume completed");
                t.Equal(1, hits, "same breakpoint should not trigger again immediately");
            });
            Add("ResumeAfterStepRunsToNextBreakpoint", "Play after a manual step resumes until the next breakpoint, not step-by-step", t =>
            {
                var vm = DILTestHarness.MakeVM(t,
                    "PUSH INT 8\nPUSH INT 9\nADD\nRETURN");
                var controller = new VMExecutionController();
                int breakpointHits = 0;
                controller.BreakpointPredicate = current =>
                    current.InstructionPointer == 0 || current.InstructionPointer == 2;
                controller.BreakpointHit += _ => breakpointHits++;
                controller.Start(vm);
                controller.Tick(10);
                t.Equal(ScriptExecutionState.Paused, controller.State, "first breakpoint");
                t.Equal(0, vm.InstructionPointer, "first breakpoint before first instruction");
                controller.Step(false);
                t.Equal(1, vm.InstructionPointer, "one manual step");
                t.Equal(ScriptExecutionState.Paused, controller.State, "manual step keeps pause");

                controller.Continue();
                t.Equal(ScriptExecutionState.Running, controller.State, "Play resumes running");
                controller.Step(false);
                t.Equal(1, vm.InstructionPointer, "Step does nothing during Play");
                controller.Tick(10);
                t.Equal(ScriptExecutionState.Paused, controller.State, "next breakpoint pauses again");
                t.Equal(2, vm.InstructionPointer, "next breakpoint at ADD");
                t.Equal(2, breakpointHits, "only two breakpoint events");
                t.Equal(2L, controller.TotalLogicalSteps, "both PUSH instructions consumed");

                controller.Continue();
                controller.Tick(10);
                t.Equal(ScriptExecutionState.Stopped, controller.State, "Play completes after second breakpoint");
                t.Equal(4L, controller.TotalLogicalSteps, "full logical step cost");
                t.Equal(2, breakpointHits, "no repeat hit at resume location");
            });
            Add("ResumeAfterManualPauseRunsContinuously", "Manual pause and continue do not enter repeated single-step mode", t =>
            {
                var vm = DILTestHarness.MakeVM(t,
                    "PUSH INT 1\nPUSH INT 2\nADD\nRETURN");
                var controller = new VMExecutionController();
                var states = new List<ScriptExecutionState>();
                controller.StateChanged += states.Add;
                controller.Start(vm);
                controller.Tick(1);
                controller.Pause();
                t.Equal(ScriptExecutionState.Paused, controller.State, "manual pause");
                controller.Step(false);
                t.Equal(2, vm.InstructionPointer, "single instruction while paused");
                controller.Continue();
                t.Equal(ScriptExecutionState.Running, controller.State, "resumes from manual pause");
                controller.Tick(100);
                t.Equal(ScriptExecutionState.Stopped, controller.State, "runs to completion");
                t.Equal(1, states.FindAll(state => state == ScriptExecutionState.Paused).Count,
                    "only the explicit manual pause");
                t.Equal(4L, controller.TotalLogicalSteps, "full logical step count");
            });
            Add("StopDetaches", "Stop prevents further progress and detaches the printed event", t =>
            {
                var controller = new VMExecutionController();
                var vm = DILTestHarness.MakeVM(t, "PUSH STRING \"stop\"\nPRINT\nPUSH NONE\nRETURN");
                int printed = 0;
                controller.Printed += _ => printed++;
                controller.Start(vm, paused: true);
                controller.Stop();
                controller.Tick(100);
                t.Equal(0, printed, "stopped controller must not print");
                t.Equal(ScriptExecutionState.Stopped, controller.State, "stop state");
                var r = DILTestHarness.Run(t, vm);
                t.Equal(VMExecutionStatus.Completed, r.Status, "VM still independently usable");
                t.Equal(0, printed, "stopped controller still attached event handler");
            });
        }
    }
}
