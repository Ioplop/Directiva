using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Directiva.EditorTests
{
    /// <summary>
    /// Main-thread, one-case-per-editor-update executor. No Unity Test Framework dependency,
    /// Play Mode, scene mutation, or writes inside the repository.
    /// </summary>
    [InitializeOnLoad]
    internal static class DirectivaTestRunner
    {
        private static List<DirectivaTestCase> queue;
        private static readonly List<DirectivaTestOutcome> results = new List<DirectivaTestOutcome>();
        private static int position;
        private static bool cancel;
        private static string runFolder;
        private static string selection;
        private static DateTimeOffset started;
        internal static string LastFolder { get; private set; }
        internal static bool Running => queue != null;
        internal static int Position => position;
        internal static int Total => queue != null ? queue.Count : results.Count;
        internal static IReadOnlyList<DirectivaTestOutcome> Results => results;
        internal static string Current => queue != null && position < queue.Count ? queue[position].Id : "";
        internal static string Selection => selection ?? "";
        internal static string LastError { get; private set; }

        static DirectivaTestRunner()
        {
            // Domain reload naturally discards in-memory results, never leaves a half-written project file.
        }

        internal static bool Start(IEnumerable<DirectivaTestCase> cases, string title)
        {
            if (Running) return false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Directiva Tests",
                    "Exit Play Mode before running the isolated Editor test suite.", "OK");
                return false;
            }
            var selected = cases.ToList();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("Directiva Tests", "No tests selected.", "OK");
                return false;
            }
            if (!DirectivaTestReports.IsExternalDirectory(DirectivaTestReports.ReportFolder))
            {
                EditorUtility.DisplayDialog("Directiva Tests",
                    "Choose an external report folder first (outside the project).", "OK");
                DirectivaTestWindow.ShowWindow();
                return false;
            }
            try
            {
                runFolder = DirectivaTestReports.CreateRunFolder();
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                EditorUtility.DisplayDialog("Directiva Tests", "Cannot create report folder:\n" + ex.Message, "OK");
                return false;
            }
            queue = selected;
            results.Clear();
            position = 0;
            cancel = false;
            started = DateTimeOffset.Now;
            selection = title;
            LastFolder = runFolder;
            LastError = null;
            EditorApplication.update += Tick;
            DirectivaTestWindow.ShowWindow();
            UnityEngine.Debug.Log("[Directiva Tests] Running " + selected.Count + " tests: " + title +
                                  "\nExternal report folder: " + runFolder);
            return true;
        }

        internal static void Cancel() { cancel = true; }

        private static void Tick()
        {
            if (queue == null) return;
            if (cancel || position >= queue.Count)
            {
                Finish(cancel);
                return;
            }
            var test = queue[position];
            var outcome = new DirectivaTestOutcome { Test = test };
            var context = new DirectivaTestContext();
            var clock = Stopwatch.StartNew();
            try
            {
                test.Execute(context);
                outcome.Result = "PASS";
            }
            catch (DirectivaTestAssertionException ex)
            {
                outcome.Result = "FAIL";
                outcome.Failure = ex.ToString();
            }
            catch (Exception ex)
            {
                outcome.Result = "ERROR";
                outcome.Failure = ex.ToString();
            }
            finally
            {
                clock.Stop();
                outcome.ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds;
                outcome.Assertions = context.Assertions;
                outcome.Evidence.AddRange(context.Evidence);
                outcome.Attachments.AddRange(context.Attachments);
            }
            try
            {
                DirectivaTestReports.SaveCase(runFolder, outcome);
            }
            catch (Exception ex)
            {
                // A report failure is never presented as a successful test run.
                LastError = "Report write failed for " + test.Id + ": " + ex;
                outcome.Result = "ERROR";
                outcome.Failure = (outcome.Failure ?? "") + "\n" + LastError;
                cancel = true;
            }
            results.Add(outcome);
            position++;
            if (outcome.Result != "PASS")
                UnityEngine.Debug.LogError("[Directiva Tests] " + outcome.Result + " " + test.Id +
                                           "\n" + outcome.Failure + "\nReport: " + runFolder);
        }

        private static void Finish(bool wasCancelled)
        {
            EditorApplication.update -= Tick;
            int planned = queue != null ? queue.Count : results.Count;
            queue = null;
            try
            {
                DirectivaTestReports.SaveIndex(runFolder, selection, results, planned, wasCancelled, started);
            }
            catch (Exception ex)
            {
                LastError = "Failed to save report index: " + ex;
                UnityEngine.Debug.LogError("[Directiva Tests] " + LastError);
            }
            int failed = results.Count(r => r.Result != "PASS");
            string message = "[Directiva Tests] " + (wasCancelled ? "CANCELLED" : "COMPLETE") +
                             ": " + (results.Count - failed) + " passed, " + failed +
                             " failed/errors (" + results.Count + "/" + planned + ")." +
                             "\nReport: " + Path.Combine(runFolder, "SUMMARY.md");
            if (failed != 0 || wasCancelled || LastError != null)
                UnityEngine.Debug.LogError(message);
            else
                UnityEngine.Debug.Log(message);
        }

        internal static void StartCategory(string category, string group = null)
        {
            var tests = DirectivaTestCatalog.GetAll().Where(t => t.Category == category &&
                          (group == null || t.Group == group));
            Start(tests, category + (group != null ? "/" + group : "/TestEverything"));
        }

        internal static void StartAll()
        {
            Start(DirectivaTestCatalog.GetAll(), "Directiva/Tests/TestAll");
        }
    }
}
