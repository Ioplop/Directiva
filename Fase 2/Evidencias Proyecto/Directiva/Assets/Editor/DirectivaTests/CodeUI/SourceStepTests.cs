using System;
using System.Collections.Generic;
using Directiva.CodeUI;
using DSCompilation.DScript.Modules;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.CodeUITests
{
    /// <summary>Actual DScript VM calls, not just a mocked source position.</summary>
    internal static class SourceStepTests
    {
        private const string Calls =
            "def helper():\n" +
            "    print('inner')\n" +
            "    return 9\n" +
            "def main():\n" +
            "    value = helper()\n" +
            "    print('outer')\n";

        private static VMExecutionController Start(DirectivaTestContext t, string script)
        {
            var run = DScriptEditorRunPipeline.Prepare("main", "main",
                new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                {
                    ["main"] = script
                }));
            t.Check(run.Success, "DScript compiles for stepping: " + run.Error);
            var controller = new VMExecutionController();
            controller.Start(run.VM, paused: true);
            return controller;
        }

        private static void UntilPaused(DirectivaTestContext t, VMExecutionController controller,
            int maxFrames = 400)
        {
            for (int i = 0; i < maxFrames && controller.State == ScriptExecutionState.Running; i++)
                controller.Tick(3); // Small budget exercises incremental execution.
            t.Equal(ScriptExecutionState.Paused, controller.State,
                "source step must terminate at a source boundary");
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "SourceStep", name, description, run));

            Add("OverSkipsCalledFunction", "Step Over skips the helper's source lines", t =>
            {
                var c = Start(t, Calls);
                var prints = new List<string>();
                c.Printed += prints.Add;
                c.StepSource(VMSourceStepMode.Over);
                UntilPaused(t, c);
                t.Equal(5, c.CurrentVM.Debug.Line, "stop at the next outer line (zero based)");
                t.Equal(1, c.CurrentVM.CallDepth, "back in main");
                t.Equal(1, prints.Count, "helper completed once");
                t.Equal("inner", prints[0], "only the callee has printed");
            });

            Add("IntoEntersCalledFunction", "Step Into pauses at helper's first line", t =>
            {
                var c = Start(t, Calls);
                c.StepSource(VMSourceStepMode.Into);
                UntilPaused(t, c);
                t.Equal(2, c.CurrentVM.CallDepth, "callee active");
                t.Equal(1, c.CurrentVM.Debug.Line, "first executable helper line");
            });

            Add("OutRestoresCallerLocation", "Step Out restores the caller even on its call line", t =>
            {
                var c = Start(t, Calls);
                c.StepSource(VMSourceStepMode.Into);
                UntilPaused(t, c);
                c.StepSource(VMSourceStepMode.Out);
                UntilPaused(t, c);
                t.Equal(1, c.CurrentVM.CallDepth, "callee returned");
                t.Equal(4, c.CurrentVM.Debug.Line,
                    "source metadata restored to the caller's original call expression");
            });

            Add("ImportedFunctionSwitchesFiles", "Stepping across imports uses each module's source file", t =>
            {
                var run = DScriptEditorRunPipeline.Prepare("main", "main",
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    {
                        ["main"] = "from utils import helper\ndef main():\n    helper()\n    pass\n",
                        ["utils"] = "def helper():\n    pass\n    return 1\n"
                    }));
                t.Check(run.Success, "imported function compiles: " + run.Error);
                var c = new VMExecutionController();
                c.Start(run.VM, paused: true);
                c.StepSource(VMSourceStepMode.Into);
                UntilPaused(t, c);
                t.Equal("utils.dscript", c.CurrentVM.Debug.File, "navigated into imported module");
                t.Equal(1, c.CurrentVM.Debug.Line, "first executable line of imported helper");
                c.StepSource(VMSourceStepMode.Out);
                UntilPaused(t, c);
                t.Equal("main.dscript", c.CurrentVM.Debug.File, "returned to importing module");
                t.Equal(2, c.CurrentVM.Debug.Line, "returned to call expression in main");
            });

            Add("RootStepOutCompletes", "Step Out from the entry function ends on VM completion", t =>
            {
                var c = Start(t, Calls);
                c.StepSource(VMSourceStepMode.Out);
                for (int i = 0; i < 400 && c.State == ScriptExecutionState.Running; i++)
                    c.Tick(2);
                t.Equal(ScriptExecutionState.Stopped, c.State, "root returned");
            });

            Add("InfiniteCalleeCanBeCancelled", "Long source steps remain responsive to Pause", t =>
            {
                const string infinite =
                    "def spin():\n    while True:\n        pass\n" +
                    "def main():\n    spin()\n";
                var c = Start(t, infinite);
                c.StepSource(VMSourceStepMode.Into);
                UntilPaused(t, c);
                c.StepSource(VMSourceStepMode.Out);
                for (int i = 0; i < 8; i++) c.Tick(3);
                t.Equal(ScriptExecutionState.Running, c.State,
                    "no artificial step limit on an unreturned function");
                c.Pause();
                t.Equal(ScriptExecutionState.Paused, c.State,
                    "manual Pause interrupts a long source step");
            });

            Add("ProcessorStepStillOneInstruction", "Separate processor step preserves old behavior", t =>
            {
                var c = Start(t, Calls);
                c.Step(skipDebugMetadata: true);
                t.Equal(1L, c.TotalLogicalSteps, "exactly one charged instruction");
                t.Equal(ScriptExecutionState.Paused, c.State, "does not auto-resume");
            });

            Add("SourceStepBreakpointsStillWork", "A breakpoint reached during a source step wins", t =>
            {
                var c = Start(t, Calls);
                int hits = 0;
                c.BreakpointPredicate = vm => vm.Debug.Line == 1 &&
                    vm.CurrentFunction != null && vm.CurrentFunction.Id != vm.EntryFunctionId;
                c.BreakpointHit += _ => hits++;
                c.StepSource(VMSourceStepMode.Over);
                UntilPaused(t, c);
                t.Equal(1, hits, "explicit breakpoint inside the helper is honored");
                t.Equal(1, c.CurrentVM.Debug.Line, "stopped in helper at breakpoint");
            });
        }
    }
}
