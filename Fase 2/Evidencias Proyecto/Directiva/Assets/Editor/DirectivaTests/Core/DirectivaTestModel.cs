using System;
using System.Collections.Generic;
using System.Globalization;

namespace Directiva.EditorTests
{
    /// <summary>A single independent regression test; no scene, Play Mode or disk fixture required.</summary>
    internal sealed class DirectivaTestCase
    {
        internal readonly string Id;
        internal readonly string Category;
        internal readonly string Group;
        internal readonly string Description;
        internal readonly Action<DirectivaTestContext> Execute;

        internal DirectivaTestCase(string category, string group, string name,
            string description, Action<DirectivaTestContext> execute)
        {
            Category = category;
            Group = group;
            Id = category + "." + group + "." + name;
            Description = description;
            Execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }
    }

    internal sealed class DirectivaTestAssertionException : Exception
    {
        internal DirectivaTestAssertionException(string message) : base(message) { }
    }

    internal sealed class DirectivaTestAttachment
    {
        internal readonly string Name;
        internal readonly string Text;
        internal DirectivaTestAttachment(string name, string text)
        {
            Name = name;
            Text = text ?? string.Empty;
        }
    }

    /// <summary>Evidence and expectations survive failures and go into per-test reports.</summary>
    internal sealed class DirectivaTestContext
    {
        internal readonly List<string> Evidence = new List<string>();
        internal readonly List<DirectivaTestAttachment> Attachments = new List<DirectivaTestAttachment>();
        internal int Assertions { get; private set; }

        internal void Note(string text) { Evidence.Add(text ?? string.Empty); }
        internal void Attach(string name, string text)
        {
            Attachments.Add(new DirectivaTestAttachment(name, text));
        }
        internal void Check(bool condition, string explanation)
        {
            Assertions++;
            if (!condition)
                throw new DirectivaTestAssertionException(explanation);
        }
        internal void Equal<T>(T expected, T actual, string what)
        {
            Assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new DirectivaTestAssertionException(
                    what + ": expected [" + Format(expected) + "] but observed [" + Format(actual) + "]");
        }
        internal void Contains(string fragment, string actual, string what)
        {
            Assertions++;
            if (actual == null || !actual.Contains(fragment))
                throw new DirectivaTestAssertionException(
                    what + ": expected substring [" + fragment + "] in [" + (actual ?? "<null>") + "]");
        }
        private static string Format<T>(T value)
        {
            if (ReferenceEquals(value, null)) return "<null>";
            if (value is IFormattable convertible)
                return convertible.ToString(null, CultureInfo.InvariantCulture);
            return value.ToString();
        }
    }

    internal sealed class DirectivaTestOutcome
    {
        internal DirectivaTestCase Test;
        internal string Result; // PASS, FAIL, ERROR
        internal double ElapsedMilliseconds;
        internal int Assertions;
        internal string Failure;
        internal readonly List<string> Evidence = new List<string>();
        internal readonly List<DirectivaTestAttachment> Attachments = new List<DirectivaTestAttachment>();
    }

    internal static class DirectivaTestCatalog
    {
        private static List<DirectivaTestCase> cache;

        internal static List<DirectivaTestCase> GetAll()
        {
            if (cache != null) return cache;
            var tests = new List<DirectivaTestCase>();
            DIL.DILParserTests.Register(tests);
            DIL.DILVMTests.Register(tests);
            DIL.DILControllerTests.Register(tests);
            DIL.DILNumericTests.Register(tests);
            DIL.DILReferenceTests.Register(tests);
            DIL.DILHashCacheTests.Register(tests);
            DIL.DILListTests.Register(tests);
            DIL.DILListMemoryTests.Register(tests);
            DIL.DILListConcurrencyTests.Register(tests);
            DIL.DILSetTests.Register(tests);
            DIL.DILSetAlgebraTests.Register(tests);
            DIL.DILSetMemoryTests.Register(tests);
            DIL.DILSetConcurrencyTests.Register(tests);
            DIL.DILSetRobustnessTests.Register(tests);
            DIL.DILDictTests.Register(tests);
            DIL.DILDictViewTests.Register(tests);
            DIL.DILDictMemoryTests.Register(tests);
            DIL.DILDictConcurrencyTests.Register(tests);
            DIL.DILDictRobustnessTests.Register(tests);
            DIL.DILMemberTests.Register(tests);
            DIL.DILFunctionTests.Register(tests);
            DIL.DILFunctionConcurrencyTests.Register(tests);
            DIL.DILIteratorTests.Register(tests);
            DIL.DILRangeTests.Register(tests);
            DIL.DILIteratorMemoryTests.Register(tests);
            DIL.DILMemoryTests.Register(tests);
            DIL.DILConcurrencyTests.Register(tests);
            DIL.DILErrorTests.Register(tests);
            DIL.DILRobustnessTests.Register(tests);
            // A future DScript/Simulation suite registers here with its own Category.
            DirectivaTupleSmokeTests.Register(tests);
            Directiva_TupleCompletionSmokeTests.Register(tests);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var test in tests)
                if (!unique.Add(test.Id))
                    throw new InvalidOperationException("Duplicate Directiva test id: " + test.Id);
            tests.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            cache = tests;
            return cache;
        }

        internal static void Add(List<DirectivaTestCase> tests, string group, string id,
            string description, Action<DirectivaTestContext> action)
        {
            tests.Add(new DirectivaTestCase("DIL", group, id, description, action));
        }
    }
}
