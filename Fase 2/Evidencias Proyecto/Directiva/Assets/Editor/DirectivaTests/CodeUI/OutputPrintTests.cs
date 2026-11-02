using System;
using System.Collections.Generic;
using System.Linq;
using Directiva.CodeUI;
using DSCompilation.DScript.Modules;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.CodeUITests
{
    /// <summary>PRINT navigation and pause metrics; no scene, disk files or MonoBehaviour required.</summary>
    internal static class OutputPrintTests
    {
        private static DScriptEditorPreparedRun Prepare(DirectivaTestContext t, string source)
        {
            t.Attach("main.dscript", source);
            var prepared = DScriptEditorRunPipeline.Prepare("main", "main",
                new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                {
                    ["main"] = source
                }));
            t.Check(prepared.Compilation.Success, "DScript must compile: " +
                string.Join(" | ", prepared.Compilation.Diagnostics));
            t.Check(prepared.Success, "Editor run must be prepared: " + prepared.Error);
            return prepared;
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "OutputPrint", id, description, run));

            Add("NativePrintCallSite", "A DScript print navigates to its own call, not a nested argument's return", t =>
            {
                const string source = "def calculate():\n    return 42\ndef main():\n    print(\"answer:\", calculate())\n";
                var prepared = Prepare(t, source);
                var controller = new VMExecutionController();
                string output = null;
                OutputSourceLocation? sourceLocation = null;
                controller.PrintedWithDebug += (text, debug) =>
                {
                    output = text;
                    sourceLocation = OutputSourceLocation.FromDebug(debug);
                };
                controller.Start(prepared.VM);
                controller.Tick(10000);
                t.Equal(ScriptExecutionState.Stopped, controller.State, "VM completed");
                t.Equal("answer: 42", output, "native print output");
                t.Check(sourceLocation.HasValue, "PRINT has source metadata");
                t.Equal("main.dscript", sourceLocation.Value.FilePath, "filename");
                t.Equal(4, sourceLocation.Value.Line, "print line, not return line");
                t.Equal(source.IndexOf("print(", StringComparison.Ordinal),
                    sourceLocation.Value.StartOffset, "print call span start");
                t.Check(sourceLocation.Value.Length > 0, "print span length");
            });

            Add("PrintInCalledFunction", "A print inside a helper points to that helper, not the caller", t =>
            {
                var prepared = Prepare(t,
                    "def helper():\n    print(\"helper\")\ndef main():\n    helper()\n");
                var controller = new VMExecutionController();
                OutputSourceLocation? source = null;
                controller.PrintedWithDebug += (text, debug) =>
                {
                    t.Equal("helper", text, "helper print");
                    source = OutputSourceLocation.FromDebug(debug);
                };
                controller.Start(prepared.VM);
                controller.Tick(10000);
                t.Check(source.HasValue, "source present");
                t.Equal(2, source.Value.Line, "helper print line");
            });

            Add("ImportedModulePrint", "A print from an imported DScript file links to that file", t =>
            {
                var sources = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                {
                    ["main"] = "from utils.helper import ping\ndef main():\n    ping()\n",
                    ["utils.helper"] = "def ping():\n    print(\"imported\")\n"
                });
                var prepared = DScriptEditorRunPipeline.Prepare("main", "main", sources);
                t.Check(prepared.Success, "imported module compiled");
                var controller = new VMExecutionController();
                OutputSourceLocation? source = null;
                controller.PrintedWithDebug += (_, debug) => source = OutputSourceLocation.FromDebug(debug);
                controller.Start(prepared.VM);
                controller.Tick(10000);
                t.Check(source.HasValue, "print source available");
                t.Equal("utils/helper.dscript", source.Value.FilePath, "imported source file");
                t.Equal(2, source.Value.Line, "imported print line");
            });

            Add("DistinctPrintedLines", "Each printed entry keeps its own immutable source snapshot", t =>
            {
                var prepared = Prepare(t,
                    "def main():\n    print(\"one\")\n    print(\"two\")\n");
                var controller = new VMExecutionController();
                var output = new OutputService();
                controller.PrintedWithDebug += (text, debug) =>
                    output.Write(text, OutputSourceLocation.FromDebug(debug));
                controller.Start(prepared.VM);
                controller.Tick(10000);
                var entries = output.Entries.ToArray();
                t.Equal(2, entries.Length, "two messages");
                t.Equal("one", entries[0].Text, "first message");
                t.Equal("two", entries[1].Text, "second message");
                t.Equal(2, entries[0].Source.Value.Line, "first line");
                t.Equal(3, entries[1].Source.Value.Line, "second line");
            });

            Add("SharedInitializerPrint", "A print from a shared initializer's helper links to the actual print", t =>
            {
                const string source = "def announce():\n    print(\"initializing\")\n    return 7\nshared value = announce()\ndef main():\n    return value\n";
                var oldOutput = new List<string>();
                OutputSourceLocation? location = null;
                string localizedOutput = null;
                var prepared = DScriptEditorRunPipeline.Prepare("main", "main",
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string> { ["main"] = source }),
                    onInitializerOutput: oldOutput.Add,
                    onInitializerOutputWithDebug: (text, debug) =>
                    {
                        localizedOutput = text;
                        location = OutputSourceLocation.FromDebug(debug);
                    });
                t.Check(prepared.Success, "initializer compilation");
                t.Check(!prepared.Initialization.HasErrors, "initializer completed");
                t.Equal(1, oldOutput.Count, "old callback preserved");
                t.Equal("initializing", oldOutput[0], "old message text");
                t.Equal("initializing", localizedOutput, "new message text");
                t.Check(location.HasValue, "initializer print is navigable");
                t.Equal("main.dscript", location.Value.FilePath, "source file");
                t.Equal(2, location.Value.Line, "helper print, not shared declaration");
            });

            Add("PrintWithoutDebugIsPlainText", "A raw DIL PRINT remains visible but not navigable without .dscript DEBUG_*", t =>
            {
                var controller = new VMExecutionController();
                string oldText = null;
                string newText = null;
                OutputSourceLocation? location = null;
                controller.Printed += text => oldText = text;
                controller.PrintedWithDebug += (text, debug) =>
                {
                    newText = text;
                    location = OutputSourceLocation.FromDebug(debug);
                };
                controller.Start(DIL.DILTestHarness.MakeVM(t,
                    "PUSH STRING \"plain\"\nPRINT\nPUSH NONE\nRETURN"));
                controller.Tick(100);
                t.Equal("plain", oldText, "original event still works");
                t.Equal(oldText, newText, "new event forwards same text");
                t.Check(!location.HasValue, "no misleading link");
            });

            Add("PauseReportsCurrentMetrics", "Manual Pause raises an observable state after executed steps", t =>
            {
                var controller = new VMExecutionController();
                int observedPauses = 0;
                long advancesAtPause = -1;
                controller.StateChanged += state =>
                {
                    if (state != ScriptExecutionState.Paused) return;
                    observedPauses++;
                    advancesAtPause = controller.TotalAdvances;
                    t.Check(controller.ElapsedMilliseconds >= 0, "elapsed time readable at pause");
                };
                controller.Start(DIL.DILTestHarness.MakeVM(t,
                    "PUSH INT 3\nPUSH INT 4\nADD\nRETURN"));
                controller.Tick(1);
                controller.Pause();
                t.Equal(1, observedPauses, "pause state event");
                t.Equal(1L, advancesAtPause, "counter captured on pause");
                controller.Step(false);
                t.Equal(2L, controller.TotalAdvances, "stepping updates count while paused");
                controller.Stop();
            });

            Add("BreakpointReportsCurrentMetrics", "Breakpoint exposes its exact step count before pausing", t =>
            {
                var controller = new VMExecutionController();
                long advancesAtPause = -1;
                controller.StateChanged += state =>
                {
                    if (state == ScriptExecutionState.Paused)
                        advancesAtPause = controller.TotalAdvances;
                };
                controller.BreakpointPredicate = vm => vm.InstructionPointer == 1;
                controller.Start(DIL.DILTestHarness.MakeVM(t,
                    "PUSH INT 3\nPUSH INT 4\nADD\nRETURN"));
                controller.Tick(100);
                t.Equal(ScriptExecutionState.Paused, controller.State, "paused by breakpoint");
                t.Equal(1L, advancesAtPause, "metrics correspond to breakpoint instruction");
                t.Equal(1L, controller.TotalAdvances, "no next instruction executed");
                controller.Stop();
            });
        }
    }
}
