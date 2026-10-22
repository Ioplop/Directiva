# Directiva — DScript backend (phase 2)

This folder contains the actual DScript -> DIL backend. The editor now uses it, but it does **not** make the
player's editor execute DScript yet; the editor host integration is separate.

## Pipeline

```csharp
// The provider supplies case-sensitive absolute module names rooted at DirectivaScripts.
DScriptCompilationResult result = DScriptCompiler.Compile("main", sourceProvider);
if (!result.Success)
{
    // Display result.Diagnostics; result.Program is null.
    return;
}
DScriptCompiledProgram program = result.Program;
// program.DILModules maps "main" -> generated main.dil text,
// "utils.vectors" -> utils/vectors.dil, plus program.BootstrapModule.
// Compilation is complete before any DIL text is exposed for optional persistence.

// New memory for each independent team/match. Same CodeContext for all VMs.
var teamMemory = new VMMemoryContext(program.Code.GlobalCount);
DScriptInitializationReport bootstrap = DScriptBootstrapRunner.Initialize(
    program, teamMemory, logicalBudgetPerInitializer: 100000,
    onError: (step, error) => LogInitializerError(step.Module, step.Span, step.Variable, error),
    onOutput: text => LogToConsole(text));

// Runtime failures of individual initializers are recorded, but do not cancel
// later initializers. The host can still create normal unit VMs afterward.
if (program.TryGetFunctionId("main.update", out int updateId))
{
    var unitVM = new DirectivaVM(program.Code, teamMemory, null, updateId);
    // Add unitVM to the deterministic scheduler here.
}
```

The snippet uses host-defined `LogInitializerError` / `LogToConsole`; those are
not part of the compiler API. There is deliberately no automatic scene, UI, or
unit creation in the backend.

## Rules and invariants

- DScript name resolution comes exclusively from `DScriptAnalysis` side tables.
  Generated DIL text is not interpreted as a new DScript source.
- Every user module generates a separate `.dil` text in `program.DILModules`.
  Imports are static; `# GLOBAL` declarations exist only in the generated
  bootstrap module and are in the exact `GlobalSlots` order.
- Calls to runtime methods are always `CALL_METHOD name n`. Normal function
  references and native builtins use `PUSH_FUNCTION` / `PUSH_NATIVE` and
  `CALL_INDIRECT n`. There are no bound-method values.
- `and`/`or` short-circuit and yield an operand, not necessarily `Bool`.
  `if` and `while` convert their condition with `IS_TRUTHY`.
- A `for` keeps the iterator cursor on the VM stack; `continue` retains it,
  `break` pops it, natural exhaustion is handled by `ITER_NEXT`.
- Every instruction generated from DScript has explicit `DEBUG_FILE`,
  `DEBUG_LINE` and `DEBUG_SPAN` immediately before it. These point into the
  original `.dscript` and consume zero *logical* VM budget (but not zero CPU).
  DIL files written by hand can still request automatic debug injection.
- Bootstrap initializers are separate functions with a separate VM call stack,
  one budget per initializer, and one shared heap/slot storage; failures and
  timeouts are reported independently, with no transactional rollback.
- A memory context can bootstrap only once and must use the same instance of
  `CodeContext` for every VM. Separate teams need separate `VMMemoryContext`s.
- `DScriptCompiler` and `DScriptBootstrapRunner` do not write files. A host may
  persist `.dil` sources only after `CompilationResult.Success`, but must avoid
  executing stale `.dil` files or showing them as user source.

## Unity tests

`Directiva > Tests > DScript > Backend > TestEverything` covers real execution,
not just string comparisons. `Directiva > Tests > TestAll` covers regressions.

The execution limits here are safety defaults, not final simulation game-balancing
settings. On any failure, export the existing Markdown test report bundle.

## Editor integration (October 22, 2026)

`DirectivaCodeUIController` now runs `DScriptEditorRunPipeline.Prepare` with
`WorkspaceDScriptSourceProvider`. The provider reads the active editor buffer and
`.dscript.back` of imported modules and validates paths with the disk source
provider. Compilation calls the in-memory DIL parser, never any on-disk `.dil`.
The selected zero-argument function (`fn:` defaults to `main`) runs after a new
`VMMemoryContext` has been initialized using `DScriptBootstrapRunner`. Shared
initializer runtime failures are displayed but do not prevent starting the VM.

`GeneratedDILCache` optionally writes sibling `.dil` files **after** a fully
successful compilation. It skips manual/unmarked `.dil` files, writes only
generated files bearing `DIRECTIVA_GENERATED_DIL_V1`, and never persists the
internal bootstrap. The editor tree only contains `.dscript`; old `.dil` files
are preserved and deliberately not migrated to DScript.

Source debugger paths use relative filenames (`utils/foo.dscript`); the VM
remains at instruction granularity for Step, and Step In / Step Out are
reserved for future source-level debugging.

Run `Directiva > Tests > DScript > EditorIntegration > TestEverything` after
updating the editor, then `Directiva > Tests > TestAll`.
