using System;
using System.Collections.Generic;
using System.Linq;
using Directiva.CodeUI;
using DSCompilation.DScript;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Semantics;
using DSExecution.Errors;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.CodeUITests
{
    /// <summary>Console navigation regressions; no scene, UI panel or player files required.</summary>
    internal static class OutputNavigationTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "OutputNavigation", id, description, run));

            Add("MessageClickCopiesClipboard",
                "Console copy preserves message, warning and error text", t =>
            {
                string previous = UnityEngine.GUIUtility.systemCopyBuffer;
                try
                {
                    foreach (OutputSeverity severity in new[] {
                        OutputSeverity.Message, OutputSeverity.Warning, OutputSeverity.Error })
                    {
                        string text = "Code output " + severity + "\nLine 2";
                        var entry = new OutputEntry(severity, text);
                        OutputPanel.CopyMessageToClipboard(entry.Text);
                        t.Equal(text, UnityEngine.GUIUtility.systemCopyBuffer,
                            severity + " clipboard contains exact line breaks");
                    }
                }
                finally
                {
                    UnityEngine.GUIUtility.systemCopyBuffer = previous;
                }
            });

            Add("LocationSurvivesBuffer", "A diagnostic location travels with its buffered console message", t =>
            {
                var output = new OutputService(2);
                var origin = new OutputSourceLocation("utils\\ai.dscript", 5, 3, 19, 4);
                OutputEntry received = default;
                output.EntryAdded += entry => received = entry;
                output.WriteError("bad variable", origin);
                t.Equal(OutputSeverity.Error, received.Severity, "severity unchanged");
                t.Check(received.Source.HasValue, "location attached");
                t.Equal("utils/ai.dscript", received.Source.Value.FilePath, "normalized path");
                t.Equal(5, received.Source.Value.Line, "one-based line");
                t.Equal(3, received.Source.Value.Column, "one-based column");
                output.Write("regular print");
                output.WriteWarning("no source");
                var kept = output.Entries.ToArray();
                t.Equal(2, kept.Length, "buffer still bounded");
                t.Check(kept.All(e => !e.Source.HasValue), "no accidental locations");
                output.Clear();
                t.Equal(0, output.Entries.Count(), "clear still works");
            });

            Add("DefaultConsoleRetention", "Console keeps only the most recent 100 messages", t =>
            {
                var output = new OutputService();
                t.Equal(100, output.Capacity, "default output capacity reduced tenfold");
                int received = 0;
                output.EntryAdded += _ => received++;
                for (int i = 0; i < 250; i++)
                    output.Write("Print " + i);

                var retained = output.Entries.ToArray();
                t.Equal(250, received, "event subscribers receive every message");
                t.Equal(100, retained.Length, "bounded history retains 100 entries");
                t.Equal("Print 150", retained[0].Text, "oldest retained entry");
                t.Equal("Print 249", retained[99].Text, "newest retained entry");
            });

            Add("CompilerModuleAndSpan", "DScript diagnostics map a module, line, column and span to an editable file", t =>
            {
                var diagnostic = new DScriptDiagnostic("tools.helpers", "DS4016", "Missing", new SourceSpan(23, 7, 4, 6));
                var source = OutputSourceLocation.FromDiagnostic(diagnostic);
                t.Check(source.HasValue, "diagnostic should have a location");
                t.Equal("tools/helpers.dscript", source.Value.FilePath, "module relative path");
                t.Equal(4, source.Value.Line, "compiler line");
                t.Equal(6, source.Value.Column, "compiler column");
                t.Equal(-1, source.Value.StartOffset, "line and column take priority over raw offsets");
                t.Equal(7, source.Value.Length, "compiler span length");
                t.Check(!OutputSourceLocation.FromDiagnostic(null).HasValue, "null diagnostic");
            });

            Add("ActualCompilerError", "A frontend failure includes a navigable source location", t =>
            {
                var sources = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                {
                    ["main"] = "def main():\n    return undefined_name\n"
                });
                DScriptAnalysis analysis = DScriptFrontend.Analyze("main", sources);
                t.Check(!analysis.Success, "source must fail analysis");
                var diagnostic = analysis.Diagnostics.First(d => d.Module == "main");
                var source = OutputSourceLocation.FromDiagnostic(diagnostic);
                t.Check(source.HasValue, "frontend location");
                t.Equal("main.dscript", source.Value.FilePath, "filename");
                t.Equal(2, source.Value.Line, "failed line");
            });

            Add("CaretPositionAndFallback", "Navigation resolves lines/columns and ignores stale absolute offsets", t =>
            {
                const string text = "a\n  bogus\nx";
                t.Equal(4, CodeEditorView.ResolveSourceCaretIndex(text, 2, 3), "line 2 col 3");
                t.Equal(5, CodeEditorView.ResolveSourceCaretIndex(text, 2, 1, 5), "valid absolute offset");
                t.Equal(4, CodeEditorView.ResolveSourceCaretIndex(text, 2, 3, 0), "stale offset fallback");
                t.Equal(10, CodeEditorView.ResolveSourceCaretIndex(text, 99, 1), "line past EOF");
                t.Equal(0, CodeEditorView.ResolveSourceCaretIndex(string.Empty, 1, 1), "empty source");
                t.Equal(5, CodeEditorView.ResolveSourceCaretIndex("x\r\n  z", 2, 3), "Windows line endings");
            });

            Add("VMBatchFaultLocation", "Fast Tick retains VM DEBUG_* location before discarding the failed VM", t =>
            {
                var controller = new VMExecutionController();
                var code = DIL.DILTestHarness.Direct(t, 0,
                    Instruction.DebugFile("utils/ai.dscript"), Instruction.DebugLine(12),
                    Instruction.DebugSpan(70, 73), Instruction.Pop(), Instruction.Return());
                RuntimeError fault = null;
                controller.Faulted += error => fault = error;
                controller.Start(DIL.DILTestHarness.MakeVM(code));
                controller.Tick(30);
                t.Check(fault != null, "fault event should be raised");
                t.Equal(RuntimeErrorId.StackUnderflow, fault.Id, "fault type");
                t.Check(controller.CurrentVM == null, "faulted VM released");
                var source = OutputSourceLocation.FromDebug(controller.LastFaultDebug);
                t.Check(source.HasValue, "fault must preserve location");
                t.Equal("utils/ai.dscript", source.Value.FilePath, "file");
                t.Equal(13, source.Value.Line, "debug lines are zero-based");
                t.Equal(70, source.Value.StartOffset, "debug start");
                t.Equal(3, source.Value.Length, "debug span length");
                controller.Stop();
                t.Check(controller.LastFaultDebug == null, "stop clears old location");
            });

            Add("VMSteppedFaultLocation", "A paused Step fault also retains the source position", t =>
            {
                var controller = new VMExecutionController();
                var code = DIL.DILTestHarness.Direct(t, 0,
                    Instruction.DebugFile("entry.dscript"), Instruction.DebugLine(2),
                    Instruction.DebugSpan(11, 15), Instruction.Pop(), Instruction.Return());
                controller.Start(DIL.DILTestHarness.MakeVM(code), paused: true);
                for (int i = 0; i < 4; i++) controller.Step(false);
                t.Equal(ScriptExecutionState.Stopped, controller.State, "fault stopped stepping");
                t.Equal("entry.dscript", controller.LastFaultDebug.File, "location retained");
                var source = OutputSourceLocation.FromDebug(controller.LastFaultDebug);
                t.Equal(3, source.Value.Line, "line converted");
            });

            Add("NoDebugMeansNoLink", "VM and non-DScript debug locations must not create misleading links", t =>
            {
                t.Check(!OutputSourceLocation.FromDebug(null).HasValue, "absent debug state");
                var code = DIL.DILTestHarness.Direct(t, 0,
                    Instruction.DebugFile("generated.dil"), Instruction.DebugLine(0), Instruction.Pop());
                var controller = new VMExecutionController();
                controller.Start(DIL.DILTestHarness.MakeVM(code));
                controller.Tick(20);
                t.Check(!OutputSourceLocation.FromDebug(controller.LastFaultDebug).HasValue,
                    "generated DIL is not an editable DScript file");
            });

            Add("InitializerCalledFunctionFault", "Shared initializer link uses the failing called function, not the shared declaration", t =>
            {
                var sources = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                {
                    ["main"] = "def explode():\n    return 1 / 0\nshared bad = explode()\ndef main():\n    return 1\n"
                });
                int failures = 0;
                OutputSourceLocation? captured = null;
                var prepared = DScriptEditorRunPipeline.Prepare("main", "main", sources,
                    onInitializerErrorWithDebug: (step, error, debug) =>
                    {
                        failures++;
                        captured = OutputSourceLocation.FromDebug(debug) ??
                            OutputSourceLocation.FromModuleSpan(step.Module, step.Span);
                    });
                t.Check(prepared.Success, "script compiles despite runtime initializer failure");
                t.Equal(1, failures, "initializer fault callback");
                t.Check(captured.HasValue, "initializer location present");
                t.Equal("main.dscript", captured.Value.FilePath, "helper source file");
                t.Equal(2, captured.Value.Line, "failure occurs in helper on line 2, not shared line 3");
            });
        }
    }
}
