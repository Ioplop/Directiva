using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Directiva.EditorTests
{
    /// <summary>Choose output directory and individual tests without adding reports to Assets.</summary>
    internal sealed class DirectivaTestWindow : EditorWindow
    {
        private readonly HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
        private string search = "";
        private Vector2 scroll;
        private bool initialized;
        private bool showResults = true;

        internal static void ShowWindow()
        {
            var window = GetWindow<DirectivaTestWindow>("Directiva Tests");
            window.minSize = new Vector2(580, 460);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }
        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }
        private void OnEditorUpdate()
        {
            if (DirectivaTestRunner.Running) Repaint();
        }

        private void OnGUI()
        {
            var all = DirectivaTestCatalog.GetAll();
            if (!initialized)
            {
                foreach (var t in all) selected.Add(t.Id);
                initialized = true;
            }
            EditorGUILayout.LabelField("Directiva — Automated Tests", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Editor-only, no Play Mode required. All reports are written outside the Unity project. " +
                "Tests run isolated in memory; each produces its own report.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Reports folder", GUILayout.Width(100));
            EditorGUILayout.SelectableLabel(DirectivaTestReports.ReportFolder, EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("Browse...", GUILayout.Width(88)))
            {
                string chosen = EditorUtility.OpenFolderPanel("Choose external Directiva test reports folder",
                    DirectivaTestReports.ReportFolder, "");
                if (!string.IsNullOrEmpty(chosen) && !DirectivaTestReports.SetDirectory(chosen))
                    EditorUtility.DisplayDialog("Invalid directory",
                        "Select a folder outside this Unity project/repository.", "OK");
            }
            EditorGUILayout.EndHorizontal();
            if (!DirectivaTestReports.IsExternalDirectory(DirectivaTestReports.ReportFolder))
                EditorGUILayout.HelpBox("Select a writable folder outside this project.", MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !DirectivaTestRunner.Running;
            if (GUILayout.Button("TestAll (registered)", GUILayout.Height(27)))
                DirectivaTestRunner.StartAll();
            if (GUILayout.Button("DIL / TestEverything", GUILayout.Height(27)))
                DirectivaTestRunner.StartCategory("DIL");
            if (GUILayout.Button("Run selected (" + selected.Count + ")", GUILayout.Height(27)))
                DirectivaTestRunner.Start(all.Where(t => selected.Contains(t.Id)), "Selected tests");
            GUI.enabled = DirectivaTestRunner.Running;
            if (GUILayout.Button("Cancel", GUILayout.Width(70), GUILayout.Height(27)))
                DirectivaTestRunner.Cancel();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (DirectivaTestRunner.Running)
            {
                EditorGUILayout.LabelField(DirectivaTestRunner.Position + " / " + DirectivaTestRunner.Total +
                    " — " + DirectivaTestRunner.Current);
                var rect = GUILayoutUtility.GetRect(12, 18);
                EditorGUI.ProgressBar(rect,
                    DirectivaTestRunner.Total > 0 ? (float)DirectivaTestRunner.Position / DirectivaTestRunner.Total : 0,
                    "Executing...");
            }
            if (!string.IsNullOrEmpty(DirectivaTestRunner.LastError))
                EditorGUILayout.HelpBox(DirectivaTestRunner.LastError, MessageType.Error);
            if (!string.IsNullOrEmpty(DirectivaTestRunner.LastFolder))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Open latest report folder", GUILayout.Width(175)))
                    EditorUtility.RevealInFinder(DirectivaTestRunner.LastFolder);
                EditorGUILayout.SelectableLabel(DirectivaTestRunner.LastFolder, GUILayout.Height(18));
                EditorGUILayout.EndHorizontal();
            }
            showResults = EditorGUILayout.Foldout(showResults,
                "Results (" + DirectivaTestRunner.Results.Count + ")", true);
            if (showResults)
            {
                foreach (var result in DirectivaTestRunner.Results.Where(r => r.Result != "PASS").Take(6))
                    EditorGUILayout.HelpBox(result.Result + " " + result.Test.Id + "\n" + result.Failure,
                        MessageType.Error);
                if (DirectivaTestRunner.Results.Count > 0)
                {
                    int passed = DirectivaTestRunner.Results.Count(r => r.Result == "PASS");
                    EditorGUILayout.LabelField(passed + " PASS, " +
                        (DirectivaTestRunner.Results.Count - passed) + " FAIL/ERROR");
                }
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            search = EditorGUILayout.TextField("Filter", search);
            if (GUILayout.Button("All", GUILayout.Width(48)))
                foreach (var t in all) selected.Add(t.Id);
            if (GUILayout.Button("None", GUILayout.Width(48))) selected.Clear();
            EditorGUILayout.EndHorizontal();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            string currentGroup = null;
            foreach (var test in all)
            {
                if (!string.IsNullOrEmpty(search) &&
                    test.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    test.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string group = test.Category + " / " + test.Group;
                if (group != currentGroup)
                {
                    currentGroup = group;
                    EditorGUILayout.Space(7);
                    EditorGUILayout.LabelField(group, EditorStyles.boldLabel);
                }
                bool enabled = selected.Contains(test.Id);
                bool next = EditorGUILayout.ToggleLeft(test.Id + " — " + test.Description, enabled);
                if (next) selected.Add(test.Id);
                else selected.Remove(test.Id);
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
