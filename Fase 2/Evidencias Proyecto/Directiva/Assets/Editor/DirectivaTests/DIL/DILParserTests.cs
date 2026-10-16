using System;
using System.Collections.Generic;
using System.IO;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    internal static class DILParserTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string purpose, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Parser", id, purpose, action);

            Add("LiteralsAndComments", "PUSH INT/DECIMAL/BOOL/NONE/STRING; comments and PASS", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PASS // no-op\nPUSH STRING \"// not a comment\" // actual comment\nPRINT\n" +
                    "PUSH BOOL TRUE\nPRINT\nPUSH DECIMAL -2.125\nPRINT\nPUSH NONE\nPRINT\nPUSH INT -123\nRETURN");
                t.Equal(-123L, unchecked((long)r.Value.value), "INT literal");
                t.Equal("// not a comment", r.Output[0], "quoted comment syntax");
                t.Equal("True", r.Output[1], "Bool print");
                t.Equal("-2.125", r.Output[2], "Decimal print");
                t.Equal("None", r.Output[3], "None print");
            });
            Add("StringEscapeSequences", "Escaped quotation, backslash, \\n, \\t, \\u and null byte", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"quote: \\\" slash: \\\\ newline: \\n tab: \\t unicode: \\u0041 zero: \\0\"\nPRINT\nPUSH NONE\nRETURN");
                t.Equal("quote: \" slash: \\ newline: \n tab: \t unicode: A zero: \0",
                    r.Output[0], "escaped string");
            });
            Add("NumberConstants", "# name : literal and $name references", t =>
            {
                var source = "# big : 12345\n" + DILTestHarness.Function("PUSH INT $big\nRETURN");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = source });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "constants execution");
                t.Equal(12345L, unchecked((long)r.Value.value), "resolved constant");
            });
            Add("LabelsForwardBackward", "Forward and backward label resolution", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 0\nSTORE_LOCAL 0\nloop:\nLOAD_LOCAL 0\nPUSH INT 1\nADD\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 4\nLESS_THAN\nJUMP_IF_TRUE loop\nJUMP end\n" +
                    "PUSH INT 999\nend:\nLOAD_LOCAL 0\nRETURN", locals: 1);
                t.Equal(4L, unchecked((long)r.Value.value), "forward/backward labels");
            });
            Add("NumericJump", "Jump target represented as numeric instruction index", t =>
            {
                DILTestHarness.ExpectInt(t, "JUMP 2\nPUSH INT 99\nPUSH INT 17\nRETURN", 17);
            });
            Add("MultipleFunctions", "Namespaced CALL and explicit entry function id", t =>
            {
                var src = DILTestHarness.Function("PUSH INT 6\nCALL $test.double\nRETURN") +
                          DILTestHarness.Function("LOAD_LOCAL 0\nPUSH INT 2\nMULTIPLY\nRETURN", 1, 1, "double");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = src });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "function calls");
                t.Equal(12L, unchecked((long)r.Value.value), "call result");
                t.Check(code.Functions.Count == 2, "Expected two compiled functions");
            });
            Add("ImportsAndCall", "Multiple .dil modules, symbols, transitively imported functions", t =>
            {
                var sources = new Dictionary<string, string>
                {
                    ["test"] = "# IMPORT : utils.core\n" + DILTestHarness.Function("PUSH INT 7\nCALL $utils.core.triple\nRETURN"),
                    ["utils.core"] = DILTestHarness.Function("LOAD_LOCAL 0\nPUSH INT 3\nMULTIPLY\nRETURN", 1, 1, "triple")
                };
                var code = DILTestHarness.ParseModules(t, "test", sources);
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "import call status");
                t.Equal(21L, unchecked((long)r.Value.value), "import call result");
            });
            Add("CycleAndRepeatedImport", "Cyclic graph and duplicate IMPORT do not double-load modules", t =>
            {
                var sources = new Dictionary<string, string>
                {
                    ["test"] = "# IMPORT : other\n# IMPORT : other\n" + DILTestHarness.Function("PUSH INT 9\nRETURN"),
                    ["other"] = "# IMPORT : test\n" + DILTestHarness.Function("PUSH INT 1\nRETURN", functionName: "aux")
                };
                var code = DILTestHarness.ParseModules(t, "test", sources);
                t.Equal(2, code.Functions.Count, "unique functions in import cycle");
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(9L, unchecked((long)r.Value.value), "entry from root module");
            });
            Add("GlobalDeduplication", "Shared global symbols across imported modules", t =>
            {
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = "# IMPORT : other\n# GLOBAL : score\n" + DILTestHarness.Function("PUSH INT 10\nSTORE_GLOBAL $score\nLOAD_GLOBAL $score\nRETURN"),
                    ["other"] = "# GLOBAL : score\n" + DILTestHarness.Function("PUSH NONE\nRETURN", functionName: "aux")
                });
                t.Equal(1, code.GlobalCount, "duplicate global declaration should share slot");
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(10L, unchecked((long)r.Value.value), "global access");
            });
            Add("DebugMetadataOnOff", "Debug injection and jump target remapping", t =>
            {
                string source = "JUMP end\nPUSH INT 100\nend:\nPUSH INT 7\nRETURN";
                var a = DILTestHarness.Parse(t, source);
                var b = DILTestHarness.Parse(t, source, options: new DILParserOptions(generateDebugMetadata: true));
                t.Check(b.GetFunction(b.EntryFunctionId).InstructionCount == 4 * a.GetFunction(a.EntryFunctionId).InstructionCount,
                    "Each original instruction must become four VM instructions");
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(b));
                t.Equal(VMExecutionStatus.Completed, r.Status, "debug execution");
                t.Equal(7L, unchecked((long)r.Value.value), "debug jump remap");
                t.Equal("test", r.VM.Debug.File, "debug filename");
                t.Check(r.VM.Debug.Line >= 0 && r.VM.Debug.SpanStart >= 0, "missing debug line/span");
            });
            Add("QualifiedNameValidation", "Identifier/module-name validation API", t =>
            {
                foreach (var good in new[] { "abc", "foo.bar", "_x9", "x.Y_2" })
                    t.Check(DILParser.IsValidQualifiedName(good), "Rejected valid name: " + good);
                foreach (var bad in new[] { "", "a..b", ".root", "3abc", "a/b", "hello world" })
                    t.Check(!DILParser.IsValidQualifiedName(bad), "Accepted invalid name: " + bad);
            });
            var badCases = new[]
            {
                new[] { "UnknownInstruction", "IMPOSSIBLE\nPUSH NONE\nRETURN", "Unknown DIL" },
                new[] { "CaseSensitive", "push int 1\nRETURN", "Unknown DIL" },
                new[] { "NegativeTuple", "TUPLE -1\nRETURN", "non-negative" },
                new[] { "InvalidPushType", "PUSH LIST 3\nRETURN", "Unsupported PUSH type" },
                new[] { "InvalidCastType", "PUSH INT 3\nCAST LIST\nRETURN", "CAST target" },
                new[] { "BadBool", "PUSH BOOL yes\nRETURN", "BOOL literal" },
                new[] { "BadInt", "PUSH INT 999999999999999999999999999\nRETURN", "Int64" },
                new[] { "BadDecimal", "PUSH DECIMAL notdecimal\nRETURN", "decimal" },
                new[] { "BadStringEscape", "PUSH STRING \"bad\\q\"\nRETURN", "escape" },
                new[] { "MissingLabel", "JUMP nowhere\nPUSH NONE\nRETURN", "Unknown label" },
                new[] { "JumpPastEnd", "JUMP 999\nPUSH NONE\nRETURN", "outside" },
                new[] { "BadLocal", "LOAD_LOCAL 10\nRETURN", "Local slot" },
                new[] { "BadGlobal", "LOAD_GLOBAL 10\nRETURN", "Global slot" },
                new[] { "BadFunction", "CALL 999\nRETURN", "Function id" },
                new[] { "InvalidContextRange", "CONTEXT_START 0 4\nPUSH NONE\nRETURN", "CONTEXT_START range" },
                new[] { "ExtraOperand", "RETURN 1", "expects" },
                new[] { "UndefinedSymbol", "PUSH INT $missing\nRETURN", "Undefined parse symbol" }
            };
            foreach (var row in badCases)
            {
                string name = row[0], snippet = row[1], fragment = row[2];
                Add("Reject" + name, "Reject malformed DIL: " + name, t =>
                    DILTestHarness.ExpectParseFailure(t, DILTestHarness.Function(snippet), fragment));
            }
            Add("RejectNoFunction", "Root without FUNCTION is invalid", t =>
                DILTestHarness.ExpectParseFailure(t, "// nothing\n", "root DIL module"));
            Add("RejectMissingHeader", "A function must declare PARAMS and LOCALS", t =>
                DILTestHarness.ExpectParseFailure(t, "# FUNCTION : main\n# LOCALS : 0\nPUSH NONE\nRETURN\n# END_FUNCTION", "missing # PARAMS"));
            Add("RejectMissingEnd", "Missing # END_FUNCTION rejected", t =>
                DILTestHarness.ExpectParseFailure(t, "# FUNCTION : main\n# PARAMS : 0\n# LOCALS : 0\nPUSH NONE\nRETURN", "missing # END_FUNCTION"));
            Add("RejectDuplicateFunction", "Duplicate function symbols rejected", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("PUSH NONE\nRETURN") + DILTestHarness.Function("PUSH NONE\nRETURN"),
                    "more than once"));
            Add("RejectLateImport", "Imports must be at the start of the file", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("PUSH NONE\nRETURN") + "# IMPORT : mod\n", "beginning"));
            Add("RejectMissingImport", "Unresolvable module gets module-aware error", t =>
            {
                var sources = new Dictionary<string, string> {
                    ["test"] = "# IMPORT : notfound\n" + DILTestHarness.Function("PUSH NONE\nRETURN")
                };
                try { DILTestHarness.ParseModules(t, "test", sources); }
                catch (DILParseException ex)
                {
                    t.Equal("notfound", ex.ModuleName, "missing imported module");
                    t.Contains("could not be found", ex.Message, "diagnostic");
                    return;
                }
                throw new DirectivaTestAssertionException("Expected missing imported module error");
            });
            Add("GlobalSlotLimit", "Parser MaxGlobalSlots rejects oversized programs", t =>
                DILTestHarness.ExpectParseFailure(t,
                    "# GLOBAL : a\n# GLOBAL : b\n" + DILTestHarness.Function("PUSH NONE\nRETURN"),
                    "global slots", new DILParserOptions(maxGlobalSlots: 1)));
            Add("FileSystemModuleProvider", "Filesystem resolver loads nested .dil modules with exact ordinal casing", t =>
            {
                string root = Path.Combine(Path.GetTempPath(), "DirectivaTest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(root, "utils"));
                try
                {
                    string text = DILTestHarness.Function("PUSH INT 8\nRETURN");
                    File.WriteAllText(Path.Combine(root, "utils", "helpers.dil"), text);
                    var provider = new FileSystemDILSourceProvider(root);
                    t.Check(provider.TryReadModule("utils.helpers", out string found), "provider cannot read module");
                    t.Equal(text, found, "provider content");
                    t.Check(!provider.TryReadModule("Utils.helpers", out _), "module directories must be case-sensitive");
                    t.Check(!provider.TryReadModule("utils.Helpers", out _), "module files must be case-sensitive");
                    var code = new DILParser(provider).Parse("utils.helpers");
                    var result = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                    t.Equal(VMExecutionStatus.Completed, result.Status, "module disk parse status");
                    t.Equal(8L, unchecked((long)result.Value.value), "loaded module result");
                }
                finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
                t.Note("Temporary filesystem module created under OS temp directory, then deleted.");
            });
            Add("FileSystemRejectsTraversal", "Filesystem module resolver refuses traversal names before disk access", t =>
            {
                var provider = new FileSystemDILSourceProvider(Path.GetTempPath());
                bool rejected = false;
                try { provider.TryReadModule("..\\secret", out _); }
                catch (ArgumentException) { rejected = true; }
                t.Check(rejected, "Provider accepted traversal syntax");
            });
        }
    }
}
