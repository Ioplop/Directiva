using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Directiva.EditorTests
{
    /// <summary>Always saves outside the Unity project. One Markdown file per test, plus indexes.</summary>
    internal static class DirectivaTestReports
    {
        private const string FolderPref = "Directiva.Tests.ExternalReportFolder.v1";
        internal static string ReportFolder
        {
            get
            {
                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "DirectivaTestReports");
                return EditorPrefs.GetString(FolderPref, fallback);
            }
        }

        private static bool IsUnderPath(string candidate, string parent, StringComparison comparison)
        {
            return string.Equals(candidate, parent, comparison) ||
                candidate.StartsWith(parent + Path.DirectorySeparatorChar, comparison) ||
                candidate.StartsWith(parent + Path.AltDirectorySeparatorChar, comparison);
        }

        internal static bool IsExternalDirectory(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return false;
            try
            {
                string path = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (IsUnderPath(path, root, comparison)) return false;

                // Reject folders anywhere inside an enclosing Git repository as well.
                DirectoryInfo directory = new DirectoryInfo(root);
                while (directory != null)
                {
                    string git = Path.Combine(directory.FullName, ".git");
                    if ((Directory.Exists(git) || File.Exists(git)) &&
                        IsUnderPath(path, directory.FullName, comparison))
                        return false;
                    directory = directory.Parent;
                }
                return true;
            }
            catch { return false; }
        }

        internal static bool SetDirectory(string folder)
        {
            if (!IsExternalDirectory(folder)) return false;
            EditorPrefs.SetString(FolderPref, Path.GetFullPath(folder));
            return true;
        }

        internal static string CreateRunFolder()
        {
            if (!IsExternalDirectory(ReportFolder))
                throw new InvalidOperationException("Reports folder must be outside the Unity project: " + ReportFolder);
            Directory.CreateDirectory(ReportFolder);
            string folder = Path.Combine(ReportFolder,
                "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) +
                "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, "tests"));
            return folder;
        }

        internal static void SaveCase(string runFolder, DirectivaTestOutcome outcome)
        {
            var b = new StringBuilder();
            b.AppendLine("# " + outcome.Test.Id);
            b.AppendLine();
            b.AppendLine("- **Result:** " + outcome.Result);
            b.AppendLine("- **Category/group:** " + outcome.Test.Category + " / " + outcome.Test.Group);
            b.AppendLine("- **Purpose:** " + outcome.Test.Description);
            b.AppendLine("- **Assertions:** " + outcome.Assertions);
            b.AppendLine("- **Duration:** " + outcome.ElapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms");
            b.AppendLine();
            if (!string.IsNullOrEmpty(outcome.Failure))
            {
                b.AppendLine("## Failure and exception stack");
                b.AppendLine();
                AppendCode(b, outcome.Failure);
            }
            b.AppendLine("## Evidence / observations");
            b.AppendLine();
            foreach (string line in outcome.Evidence)
                AppendCode(b, line);
            if (outcome.Evidence.Count == 0)
                b.AppendLine("No additional observations were captured.");
            foreach (var att in outcome.Attachments)
            {
                b.AppendLine();
                b.AppendLine("## " + att.Name.Replace("\r", " ").Replace("\n", " "));
                b.AppendLine();
                AppendCode(b, att.Text);
            }
            File.WriteAllText(Path.Combine(runFolder, "tests", outcome.Test.Id + ".md"),
                b.ToString(), new UTF8Encoding(false));
        }

        private static void AppendCode(StringBuilder b, string text)
        {
            // Choose a fence longer than any backtick sequence in source/diagnostics.
            int max = 2, count = 0;
            foreach (char c in text ?? "")
            {
                count = c == '`' ? count + 1 : 0;
                max = Math.Max(max, count);
            }
            string fence = new string('`', max + 1);
            b.AppendLine(fence + "text");
            b.AppendLine(text ?? "");
            b.AppendLine(fence);
            b.AppendLine();
        }

        [Serializable] private sealed class SummaryJson
        {
            public string generatedAt;
            public string unityVersion;
            public string runDirectory;
            public string selection;
            public bool cancelled;
            public int passed, failed, errors, total;
            public CaseJson[] cases;
        }
        [Serializable] private sealed class CaseJson
        {
            public string id, category, group, status, reportFile, failure;
            public float milliseconds;
            public int assertions;
        }

        internal static void SaveIndex(string folder, string selection, IList<DirectivaTestOutcome> results,
            int scheduledCount, bool cancelled, DateTimeOffset started)
        {
            if (string.IsNullOrEmpty(folder)) return;
            int pass = results.Count(o => o.Result == "PASS");
            int failed = results.Count(o => o.Result == "FAIL");
            int errors = results.Count(o => o.Result == "ERROR");
            var b = new StringBuilder();
            b.AppendLine("# Directiva Test Report");
            b.AppendLine();
            b.AppendLine("- **Started:** " + started.ToString("o", CultureInfo.InvariantCulture));
            b.AppendLine("- **Updated:** " + DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
            b.AppendLine("- **Unity:** " + Application.unityVersion);
            b.AppendLine("- **Selected:** " + selection);
            b.AppendLine("- **Planned / executed:** " + scheduledCount + " / " + results.Count);
            b.AppendLine("- **Passed / Failed / Errors:** " + pass + " / " + failed + " / " + errors);
            b.AppendLine("- **Cancelled:** " + cancelled);
            b.AppendLine();
            b.AppendLine("Each test has an individual report in `tests/`; **expected runtime errors count as PASS** when asserted.");
            b.AppendLine("No game scenes, player files or real project scripts were modified by these tests.");
            b.AppendLine();
            b.AppendLine("| Test | Result | Assertions | ms |");
            b.AppendLine("|---|---|---:|---:|");
            foreach (var r in results)
                b.AppendLine("| [" + r.Test.Id + "](tests/" + r.Test.Id + ".md) | " + r.Result +
                    " | " + r.Assertions + " | " + r.ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture) + " |");
            File.WriteAllText(Path.Combine(folder, "SUMMARY.md"), b.ToString(), new UTF8Encoding(false));
            var json = new SummaryJson
            {
                generatedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                runDirectory = folder,
                selection = selection,
                cancelled = cancelled,
                total = scheduledCount,
                passed = pass, failed = failed, errors = errors,
                cases = results.Select(r => new CaseJson
                {
                    id = r.Test.Id, category = r.Test.Category, group = r.Test.Group,
                    status = r.Result, milliseconds = (float)r.ElapsedMilliseconds,
                    assertions = r.Assertions,
                    reportFile = "tests/" + r.Test.Id + ".md",
                    failure = r.Failure ?? ""
                }).ToArray()
            };
            File.WriteAllText(Path.Combine(folder, "SUMMARY.json"),
                JsonUtility.ToJson(json, true), new UTF8Encoding(false));
        }
    }
}
