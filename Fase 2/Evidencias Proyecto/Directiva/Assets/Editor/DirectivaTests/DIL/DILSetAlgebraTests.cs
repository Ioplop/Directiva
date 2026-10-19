using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Set algebra and relations, including large continued operations.</summary>
    internal static class DILSetAlgebraTests
    {
        internal static string Integers(params long[] values)
        {
            var builder = new StringBuilder();
            foreach (long value in values)
                builder.Append("PUSH INT ").Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("SET ").Append(values.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return builder.ToString();
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string why, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "SetAlgebra", id, why, action);

            void ExpectMembers(DirectivaTestContext t, string program, params long[] expected)
            {
                var r = DILTestHarness.ExpectCompleted(t, program + "RETURN");
                t.Equal(DataTypeId.Set, r.Value.dataType, "set result");
                var actual = (SetHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(expected.Length, actual.Count, "unique result count");
                var seen = new HashSet<long>();
                for (int i = 0; i < actual.Count; i++)
                {
                    t.Equal(DataTypeId.Int, actual.GetElement(i).dataType, "element type");
                    seen.Add(unchecked((long)actual.GetElement(i).value));
                }
                foreach (long value in expected)
                    t.Check(seen.Contains(value), "missing member " + value);
            }

            Add("Union", "Union contains all distinct members", t =>
                ExpectMembers(t, Integers(1, 2) + Integers(2, 3) + "SET_UNION\n", 1, 2, 3));
            Add("Intersection", "Intersection returns common members", t =>
                ExpectMembers(t, Integers(1, 2, 4) + Integers(2, 3, 4) +
                    "SET_INTERSECTION\n", 2, 4));
            Add("Difference", "Difference is directional (A minus B)", t =>
                ExpectMembers(t, Integers(1, 2, 3) + Integers(2, 8) +
                    "SET_DIFFERENCE\n", 1, 3));
            Add("ReverseDifference", "B minus A is not A minus B", t =>
                ExpectMembers(t, Integers(2, 8) + Integers(1, 2, 3) +
                    "SET_DIFFERENCE\n", 8));
            Add("SymmetricDifference", "Symmetric difference excludes common members", t =>
                ExpectMembers(t, Integers(1, 2, 3) + Integers(2, 4) +
                    "SET_SYMMETRIC_DIFFERENCE\n", 1, 3, 4));
            Add("UnionEmpty", "Union with empty set retains all members", t =>
                ExpectMembers(t, Integers(1, 2) + "SET 0\nSET_UNION\n", 1, 2));
            Add("EmptyIntersection", "Intersection with empty set is empty", t =>
                ExpectMembers(t, Integers(1, 2) + "SET 0\nSET_INTERSECTION\n"));
            Add("EmptyDifference", "Difference against empty set is unchanged", t =>
                ExpectMembers(t, Integers(1, 2) + "SET 0\nSET_DIFFERENCE\n", 1, 2));
            Add("SymDiffSelf", "Symmetric difference of a set with itself is empty", t =>
                ExpectMembers(t, Integers(1, 2) + Integers(1, 2) +
                    "SET_SYMMETRIC_DIFFERENCE\n"));
            Add("UnionSelf", "Union of a set with itself is unchanged", t =>
                ExpectMembers(t, Integers(1, 2) + Integers(1, 2) + "SET_UNION\n", 1, 2));
            Add("IntersectionSelf", "Intersection of equal sets is unchanged", t =>
                ExpectMembers(t, Integers(1, 2) + Integers(2, 1) +
                    "SET_INTERSECTION\n", 1, 2));
            Add("DifferenceSelf", "Difference of same set is empty", t =>
                ExpectMembers(t, Integers(1, 2) + Integers(1, 2) + "SET_DIFFERENCE\n"));
            Add("DuplicateAcrossInputs", "Union never duplicates a member", t =>
                ExpectMembers(t, Integers(4, 4, 5) + Integers(5, 5, 4) +
                    "SET_UNION\n", 4, 5));
            Add("UnionNoOperandMutation", "Union returns new Set without mutating inputs", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    Integers(1) + "STORE_LOCAL 0\n" + Integers(2) + "STORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nSET_UNION\nPOP\n" +
                    "LOAD_LOCAL 0\nLENGTH\nPRINT\nLOAD_LOCAL 1\nLENGTH\nRETURN", locals: 2);
                t.Equal("1", r.Output[0], "left source intact");
                t.Equal(1L, unchecked((long)r.Value.value), "right source intact");
            });
            Add("UpdateContents", "SET_UPDATE publishes union on same heap identity", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    Integers(1, 2) + "STORE_LOCAL 0\n" + Integers(2, 3) + "STORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nSET_UPDATE\nPOP\n" +
                    "LOAD_LOCAL 0\nRETURN", locals: 2);
                t.Equal(DataTypeId.Set, r.Value.dataType, "updated result");
                var result = (SetHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(3, result.Count, "updated length");
                t.Equal(7UL, r.VM.Memory.Heap.AllocatedSize, "sum of final set charges");
            });
            Add("UpdateReturnsNone", "Set mutation operation returns None", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "SET 0\nSET 0\nSET_UPDATE\nRETURN");
                t.Equal(DataTypeId.None, r.Value.dataType, "return type");
            });
            Add("UpdateIdentity", "Aliases observe same updated Set", t =>
                DILTestHarness.ExpectBool(t,
                    Integers(1) + "STORE_LOCAL 0\nLOAD_LOCAL 0\nSTORE_LOCAL 1\n" +
                    Integers(2) + "STORE_LOCAL 2\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 2\nSET_UPDATE\nPOP\n" +
                    "LOAD_LOCAL 1\nPUSH INT 2\nCONTAINS\nRETURN", true, locals: 3));
            Add("UpdateSelfNoOp", "Self-update makes no changes", t =>
            {
                var result = DILTestHarness.ExpectCompleted(t,
                    Integers(1, 2) + "STORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 0\nSET_UPDATE\nPOP\n" +
                    "LOAD_LOCAL 0\nLENGTH\nRETURN", locals: 1);
                t.Equal(2L, unchecked((long)result.Value.value), "self update length");
            });
            Add("Subset", "A is a subset of B when all its members are in B", t =>
                DILTestHarness.ExpectBool(t,
                    Integers(1, 2) + Integers(1, 2, 3) + "SET_IS_SUBSET\nRETURN", true));
            Add("NotSubset", "Extra A member fails subset", t =>
                DILTestHarness.ExpectBool(t,
                    Integers(1, 4) + Integers(1, 2, 3) + "SET_IS_SUBSET\nRETURN", false));
            Add("EqualSubset", "Equal sets are subsets of each other", t =>
                DILTestHarness.ExpectBool(t,
                    Integers(1, 2) + Integers(2, 1) + "SET_IS_SUBSET\nRETURN", true));
            Add("Superset", "A is a superset when it contains all members of B", t =>
                DILTestHarness.ExpectBool(t,
                    Integers(1, 2, 3) + Integers(1, 3) + "SET_IS_SUPERSET\nRETURN", true));
            Add("NotSuperset", "Missing A member fails superset", t =>
                DILTestHarness.ExpectBool(t,
                    Integers(1, 3) + Integers(1, 2) + "SET_IS_SUPERSET\nRETURN", false));
            Add("Disjoint", "Disjoint sets contain no common members", t =>
                DILTestHarness.ExpectBool(t,
                    Integers(1, 2) + Integers(3, 4) + "SET_IS_DISJOINT\nRETURN", true));
            Add("NotDisjoint", "Disjoint returns false on intersection", t =>
                DILTestHarness.ExpectBool(t,
                    Integers(1, 2) + Integers(2, 4) + "SET_IS_DISJOINT\nRETURN", false));
            Add("EmptyDisjointSelf", "Empty set is disjoint from itself", t =>
                DILTestHarness.ExpectBool(t,
                    "SET 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nLOAD_LOCAL 0\n" +
                    "SET_IS_DISJOINT\nRETURN", true, locals: 1));
            Add("FullDisjointSelf", "Nonempty set is not disjoint from itself", t =>
                DILTestHarness.ExpectBool(t,
                    Integers(1) + "STORE_LOCAL 0\nLOAD_LOCAL 0\nLOAD_LOCAL 0\n" +
                    "SET_IS_DISJOINT\nRETURN", false, locals: 1));
            Add("EmptySubset", "Empty set is subset of any set", t =>
                DILTestHarness.ExpectBool(t,
                    "SET 0\n" + Integers(1) + "SET_IS_SUBSET\nRETURN", true));
            Add("EmptySuperset", "All sets are supersets of empty set", t =>
                DILTestHarness.ExpectBool(t,
                    Integers(1) + "SET 0\nSET_IS_SUPERSET\nRETURN", true));
            Add("UnionTypeMismatch", "Only sets are accepted for set algebra", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nLIST 0\nSET_UNION\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("ChainedAlgebra", "Set algebra results can feed new operations", t =>
                ExpectMembers(t,
                    Integers(1, 2) + Integers(2, 3) + "SET_UNION\n" +
                    Integers(3) + "SET_DIFFERENCE\n", 1, 2));
            Add("LargeUnionContinues", "Large Set union uses continuation budget", t =>
            {
                var a = new long[55];
                for (int i = 0; i < a.Length; i++) a[i] = i;
                var vm = DILTestHarness.MakeVM(t,
                    Integers(a) + Integers(50, 100) + "SET_UNION\nLENGTH\nRETURN");
                var result = DILTestHarness.Run(t, vm, budget: 1000);
                t.Equal(VMExecutionStatus.Completed, result.Status, "union completion");
                t.Equal(56L, unchecked((long)result.Value.value), "unique union size");
                t.Check(result.Steps >= 65, "more than one step per union after construction");
            });
            Add("LargeSubsetContinues", "Subset of long sets uses continuation steps", t =>
            {
                var a = new long[100];
                for (int i = 0; i < a.Length; i++) a[i] = i;
                var run = DILTestHarness.ExpectCompleted(t,
                    Integers(a) + Integers(a) + "SET_IS_SUBSET\nRETURN");
                t.Equal(1UL, run.Value.value, "large subset is true");
                t.Check(run.Steps >= 210, "long relation consumes multiple steps");
            });
            Add("LargeSetInNestedListEquality", "Set equality inside List is synchronous even when large", t =>
            {
                var values = new long[25];
                for (int i = 0; i < values.Length; i++) values[i] = i;
                DILTestHarness.ExpectBool(t,
                    Integers(values) + "LIST 1\n" + Integers(values) +
                    "LIST 1\nVALUE_EQUALS\nRETURN", true);
            });
        }
    }
}
