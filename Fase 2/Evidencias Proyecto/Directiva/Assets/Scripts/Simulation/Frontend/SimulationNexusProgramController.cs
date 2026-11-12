using System;
using System.Collections.Generic;
using DSCompilation.DScript;
using Directiva.SimulationUI;
using UnityEngine;

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// Converts generic world clicks into a player-Nexus-only program picker.
    /// Script/function changes assign immediately; no apply or refresh buttons.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]
    public sealed class SimulationNexusProgramController : MonoBehaviour
    {
        private SimulationSceneSynchronizer synchronizer;
        private SimulationSceneView view;
        private SimulationUIController ui;
        private SimulationWorldPointerController pointer;
        private int selectedNexusId;
        private bool eventsAttached;

        public void Initialize(SimulationSceneSynchronizer host,
            SimulationSceneView sceneView, SimulationUIController uiController)
        {
            synchronizer = host;
            view = sceneView;
            ui = uiController;
            pointer = GetComponent<SimulationWorldPointerController>();
            if (pointer == null)
                pointer = gameObject.AddComponent<SimulationWorldPointerController>();
            pointer.Initialize(ui);
        }

        private void Start()
        {
            if (synchronizer == null || view == null || ui == null || ui.NexusPicker == null)
            {
                Debug.LogError("[Directiva] Nexus selection requires the scene synchronizer, " +
                    "simulation view and UI controller.", this);
                return;
            }
            pointer.EntityClicked += HandleClick;
            ui.NexusPicker.ScriptChanged += HandleScriptChanged;
            ui.NexusPicker.FunctionChanged += HandleFunctionChanged;
            ui.NexusPicker.RefreshRequested += RefreshScripts;
            eventsAttached = true;
        }

        private void OnDestroy()
        {
            if (!eventsAttached) return;
            if (pointer != null) pointer.EntityClicked -= HandleClick;
            if (ui != null && ui.NexusPicker != null)
            {
                ui.NexusPicker.ScriptChanged -= HandleScriptChanged;
                ui.NexusPicker.FunctionChanged -= HandleFunctionChanged;
                ui.NexusPicker.RefreshRequested -= RefreshScripts;
            }
        }

        private void HandleClick(SimulationEntityIdentity identity)
        {
            // The enemy program remains private to its independent level source.
            if (identity == null || identity.Kind != SimulationVisualKind.Nexus || identity.SideId != 1)
            {
                selectedNexusId = 0;
                ui.NexusPicker.Hide();
                return;
            }
            selectedNexusId = identity.EntityId;
            ui.NexusPicker.SetSelectionLocked(synchronizer.IsProgramSelectionLocked);
            RefreshScripts(preferAppliedSelection: true);
            ui.RefreshNexusPickerUnsavedState();
            ui.NexusPicker.Show();
        }

        // Used when either dropdown is about to open. It refreshes saved files
        // without interpreting a passive refresh as a new program assignment.
        private void RefreshScripts() => RefreshScripts(preferAppliedSelection: false);

        private void RefreshScripts(bool preferAppliedSelection)
        {
            if (ui == null || ui.NexusPicker == null || synchronizer == null) return;
            // A locked picker may display the current selection on open, but
            // must never rebuild its choices in response to disabled controls.
            if (synchronizer.IsProgramSelectionLocked && !preferAppliedSelection) return;
            var picker = ui.NexusPicker;
            try
            {
                string previousScript = picker.SelectedScript;
                string previousFunction = picker.SelectedFunction;
                List<string> scripts = PlayerNexusScriptCatalog.ListScripts(synchronizer.PlayerScriptsRoot);
                string preferred = preferAppliedSelection
                    ? synchronizer.SelectedPlayerScript : previousScript;
                picker.SetScripts(scripts, preferred);
                if (scripts.Count == 0)
                {
                    picker.SetFunctions(Array.Empty<string>(), null);
                    picker.SetLocalizedStatus("nexus_no_scripts", true);
                    return;
                }
                string selected = picker.SelectedScript;
                if (string.IsNullOrWhiteSpace(selected))
                {
                    // Available scripts do not imply an assigned program.
                    picker.SetFunctions(Array.Empty<string>(), null);
                    picker.SetStatus(string.Empty);
                    return;
                }
                string preferredFunction = !preferAppliedSelection &&
                    string.Equals(selected, previousScript, StringComparison.Ordinal)
                    ? previousFunction : null;
                PopulateFunctions(selected, preferredFunction, assign: false);
            }
            catch (Exception exception)
            {
                picker.SetStatus(exception.Message, true);
                ReportCompilationFailure(exception);
            }
        }

        private void HandleScriptChanged(string relativeScript)
        {
            if (synchronizer.IsProgramSelectionLocked) return;
            // Changing the script chooses its first zero-argument entry function
            // (or preserves the previously applied function for this script).
            PopulateFunctions(relativeScript, null, assign: true);
        }

        private void PopulateFunctions(string relativeScript, string preferredFunction, bool assign)
        {
            var picker = ui.NexusPicker;
            if (string.IsNullOrWhiteSpace(relativeScript))
            {
                picker.SetFunctions(Array.Empty<string>(), null);
                picker.SetStatus(string.Empty);
                return;
            }
            try
            {
                List<string> functions = PlayerNexusScriptCatalog.ListEntryFunctions(
                    synchronizer.PlayerScriptsRoot, relativeScript);
                if (preferredFunction == null)
                {
                    if (string.Equals(relativeScript, synchronizer.SelectedPlayerScript,
                        StringComparison.Ordinal))
                        preferredFunction = synchronizer.SelectedPlayerFunction;
                    else if (functions.Contains("nexus_main"))
                        preferredFunction = "nexus_main";
                    else if (functions.Contains("main"))
                        preferredFunction = "main";
                }
                picker.SetFunctions(functions, preferredFunction);
                if (functions.Count == 0)
                {
                    picker.SetLocalizedStatus("nexus_no_functions", true);
                    return;
                }
                picker.SetStatus(string.Empty);
                if (assign)
                    ApplySelection(relativeScript, picker.SelectedFunction);
            }
            catch (Exception exception)
            {
                picker.SetFunctions(Array.Empty<string>(), null);
                picker.SetStatus(exception.Message, true);
                ReportCompilationFailure(exception);
            }
        }

        private void HandleFunctionChanged(string relativeScript, string entryFunction)
        {
            if (synchronizer.IsProgramSelectionLocked) return;
            ApplySelection(relativeScript, entryFunction);
        }

        private void ApplySelection(string relativeScript, string entryFunction)
        {
            if (string.IsNullOrWhiteSpace(relativeScript) || string.IsNullOrWhiteSpace(entryFunction))
                return;
            if (synchronizer.TryAssignPlayerNexusProgram(relativeScript, entryFunction,
                out string error, out IReadOnlyList<DScriptDiagnostic> diagnostics))
                ui.NexusPicker.SetStatus(string.Empty);
            else
            {
                // This failure happens outside the embedded editor's own
                // Compile button. Publish it explicitly with source metadata.
                ui.CodeEditor?.PublishPlayerCompilationFailure(error, diagnostics);
                // A failed compilation must not leave a fictitious selection
                // displayed in the UI while a different program is assigned.
                RefreshScripts(preferAppliedSelection: true);
                ui.NexusPicker.SetStatus(error, true);
            }
        }

        private void ReportCompilationFailure(Exception exception)
        {
            if (exception is PlayerNexusCompilationException compilationError)
                ui?.CodeEditor?.PublishPlayerCompilationFailure(
                    compilationError.Message, compilationError.Diagnostics);
            else
                ui?.CodeEditor?.PublishPlayerCompilationFailure(exception.Message, null);
        }

        private void LateUpdate()
        {
            if (selectedNexusId == 0 || ui?.NexusPicker == null || !ui.NexusPicker.Visible)
                return;
            if (!view.TryGetVisual(selectedNexusId, out VisualEntity nexus))
            {
                selectedNexusId = 0;
                ui.NexusPicker.Hide();
                return;
            }
            ui.PositionNexusPicker(nexus.CenterPosition + Vector3.up * 0.5f, pointer.WorldCamera);
        }
    }
}
