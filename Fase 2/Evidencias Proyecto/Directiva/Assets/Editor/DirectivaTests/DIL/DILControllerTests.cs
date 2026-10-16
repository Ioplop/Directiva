using System;
using System.Collections.Generic;
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
                controller.Continue();
                controller.Tick(1);
                t.Equal(ScriptExecutionState.Running, controller.State, "budgeted running state");
                controller.Tick(4);
                t.Equal(ScriptExecutionState.Stopped, controller.State, "completed controller state");
                t.Equal(1, completed, "completed event count");
                t.Equal(5L, unchecked((long)result.value), "completed value");
                t.Equal(2L, controller.TotalAdvances, "exact instructions executed");
            });
            Add("StepByStep", "Paused Step executes precisely one VM instruction", t =>
            {
                var controller = new VMExecutionController();
                var vm = DILTestHarness.MakeVM(t, "PUSH INT 3\nPUSH INT 4\nADD\nRETURN");
                controller.Start(vm, paused: true);
                controller.Step(false);
                t.Equal(1, vm.InstructionPointer, "first IP");
                t.Equal(1L, controller.TotalAdvances, "single step advances");
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
                controller.Step(true);
                t.Equal(ScriptExecutionState.Stopped, controller.State, "source RETURN completes");
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
