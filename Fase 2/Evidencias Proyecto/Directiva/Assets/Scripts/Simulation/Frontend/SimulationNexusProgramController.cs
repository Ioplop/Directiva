using System;
using System.Collections.Generic;
using Directiva.SimulationUI;
using Directiva.Localization;
using UnityEngine;

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// Converts generic world clicks into a player-Nexus-only program picker.
    /// All unit/resource/enemy clicks remain observable through the pointer
    /// controller but have no game-specific action yet.
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

        private static string T(string key) => DirectivaLocalization.Get("Simulation", key);

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
            ui.NexusPicker.ApplyRequested += HandleApply;
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
                ui.NexusPicker.ApplyRequested -= HandleApply;
                ui.NexusPicker.RefreshRequested -= RefreshScripts;
            }
        }

        private void HandleClick(SimulationEntityIdentity identity)
        {
            // The enemy program remains private to its independent level source.
            // Generic clicks still work for other entity types and future menus.
            if (identity == null || identity.Kind != SimulationVisualKind.Nexus || identity.SideId != 1)
            {
                selectedNexusId = 0;
                ui.NexusPicker.Hide();
                return;
            }
            selectedNexusId = identity.EntityId;
            RefreshScripts();
            ui.NexusPicker.Show();
        }

        private void RefreshScripts()
        {
            if (ui == null || ui.NexusPicker == null || synchronizer == null) return;
            try
            {
                List<string> scripts = PlayerNexusScriptCatalog.ListScripts(synchronizer.PlayerScriptsRoot);
                string previous = ui.NexusPicker.Visible ? ui.NexusPicker.SelectedScript : null;
                string preferred = !string.IsNullOrEmpty(previous)
                    ? previous : synchronizer.SelectedPlayerScript;
                ui.NexusPicker.SetScripts(scripts, preferred);
                HandleScriptChanged(ui.NexusPicker.SelectedScript);
                if (scripts.Count == 0)
                    ui.NexusPicker.SetStatus(T("nexus_no_scripts"), true);
            }
            catch (Exception exception)
            {
                ui.NexusPicker.SetStatus(exception.Message, true);
            }
        }

        private void HandleScriptChanged(string relativeScript)
        {
            if (string.IsNullOrWhiteSpace(relativeScript))
            {
                ui.NexusPicker.SetFunctions(Array.Empty<string>(), null);
                return;
            }
            try
            {
                List<string> functions = PlayerNexusScriptCatalog.ListEntryFunctions(
                    synchronizer.PlayerScriptsRoot, relativeScript);
                string preferred = string.Equals(relativeScript, synchronizer.SelectedPlayerScript,
                    StringComparison.Ordinal) ? synchronizer.SelectedPlayerFunction : null;
                ui.NexusPicker.SetFunctions(functions, preferred);
                ui.NexusPicker.SetStatus(functions.Count > 0
                    ? string.Empty : T("nexus_no_functions"), functions.Count == 0);
            }
            catch (Exception exception)
            {
                ui.NexusPicker.SetFunctions(Array.Empty<string>(), null);
                ui.NexusPicker.SetStatus(exception.Message, true);
            }
        }

        private void HandleApply(string script, string function)
        {
            if (synchronizer.TryAssignPlayerNexusProgram(script, function, out string error))
                ui.NexusPicker.SetStatus(T("nexus_program_applied"));
            else
                ui.NexusPicker.SetStatus(error, true);
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
