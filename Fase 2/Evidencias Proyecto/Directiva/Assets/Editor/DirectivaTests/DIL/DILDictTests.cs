using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Constructor, key lookup, mutations, equality, identity and DIL syntax.</summary>
    internal static class DILDictTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Dictionaries", id, description, action);

            Add("Empty", "DICT 0 creates an empty dictionary", t =>
                DILTestHarness.ExpectInt(t, "DICT 0\nLENGTH\nRETURN", 0));
            Add("OnePair", "DICT 1 constructs one key/value pair", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 3\nPUSH INT 17\nDICT 1\nPUSH INT 3\nGET\nRETURN", 17));
            Add("TwoPairs", "Two separate keys are retrievable", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 2\nPUSH INT 20\nPUSH INT 3\nPUSH INT 30\nDICT 2\nPUSH INT 3\nGET\nRETURN", 30));
            Add("ZeroIsFalsy", "An empty map is falsy", t =>
                DILTestHarness.ExpectBool(t, "DICT 0\nIS_TRUTHY\nRETURN", false));
            Add("NonEmptyIsTruthy", "An entry whose value is False still makes the map truthy", t =>
                DILTestHarness.ExpectBool(t, "PUSH INT 1\nPUSH BOOL FALSE\nDICT 1\nIS_TRUTHY\nRETURN", true));
            Add("DuplicateKeyLastValue", "Duplicate keys retain the last value", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 10\nPUSH INT 1\nPUSH INT 90\nDICT 2\nPUSH INT 1\nGET\nRETURN", 90));
            Add("DuplicateKeyLength", "Equal keys count as one entry", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 10\nPUSH BOOL TRUE\nPUSH INT 90\nDICT 2\nLENGTH\nRETURN", 1));
            Add("CrossNumericKey", "Bool key is retrievable through equal Decimal key", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH BOOL TRUE\nPUSH INT 55\nDICT 1\nPUSH DECIMAL 1.0\nGET\nRETURN", 55));
            Add("CrossNumericOverwrite", "Int and Bool equal keys replace values", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH BOOL TRUE\nPUSH INT 9\nDICT 2\nPUSH INT 1\nGET\nRETURN", 9));
            Add("DuplicateString", "Distinct equal strings are the same key", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH STRING \"name\"\nPUSH INT 1\nPUSH STRING \"name\"\nPUSH INT 8\nDICT 2\nLENGTH\nRETURN", 1));
            Add("TupleKey", "Immutable tuples work as hashed keys", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 2\nTUPLE 2\nPUSH INT 88\nDICT 1\nPUSH INT 1\nPUSH INT 2\nTUPLE 2\nGET\nRETURN", 88));
            Add("NestedTupleKey", "Nested immutable tuple keys compare by content", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nTUPLE 1\nTUPLE 1\nPUSH INT 31\nDICT 1\nPUSH BOOL TRUE\nTUPLE 1\nTUPLE 1\nGET\nRETURN", 31));
            Add("NoneKey", "None is a valid key", t =>
                DILTestHarness.ExpectInt(t, "PUSH NONE\nPUSH INT 73\nDICT 1\nPUSH NONE\nGET\nRETURN", 73));
            Add("NegativeKey", "Negative Int key lookups work", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT -75\nPUSH INT 9\nDICT 1\nPUSH INT -75\nGET\nRETURN", 9));
            Add("DifferentKeyTypes", "Text 1 and numeric 1 are distinct", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 3\nPUSH STRING \"1\"\nPUSH INT 6\nDICT 2\nLENGTH\nRETURN", 2));
            Add("NullValue", "None is accepted as a value", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH NONE\nDICT 1\nPUSH INT 1\nCONTAINS\nRETURN", true));
            Add("MutableValue", "A mutable list can be a dictionary value", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 4\nPUSH INT 11\nLIST 1\nDICT 1\nPUSH INT 4\nGET\nLENGTH\nRETURN", 1));
            Add("ContainsFound", "CONTAINS checks keys, not values", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 2\nPUSH INT 20\nDICT 1\nPUSH INT 2\nCONTAINS\nRETURN", true));
            Add("ContainsMissing", "Missing key is not contained", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 2\nPUSH INT 20\nDICT 1\nPUSH INT 20\nCONTAINS\nRETURN", false));
            Add("ContainsCrossNumeric", "Hash equality applies to membership", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nPUSH BOOL TRUE\nCONTAINS\nRETURN", true));
            Add("GetMissing", "GET missing key faults rather than returning None", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nPUSH INT 1\nGET\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("GetDefaultMissing", "DICT_GET_DEFAULT returns supplied fallback", t =>
                DILTestHarness.ExpectInt(t,
                    "DICT 0\nPUSH INT 1\nPUSH INT 45\nDICT_GET_DEFAULT\nRETURN", 45));
            Add("GetDefaultFound", "DICT_GET_DEFAULT returns value when found", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 15\nDICT 1\nPUSH INT 1\nPUSH INT 45\nDICT_GET_DEFAULT\nRETURN", 15));
            Add("SetNew", "Generic SET adds a new dictionary entry", t =>
                ExpectIntWithLocals(t,
                    "DICT 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 2\nPUSH INT 15\nSET\nPOP\nLOAD_LOCAL 0\nPUSH INT 2\nGET\nRETURN", 15, 1));
            Add("SetExisting", "Generic SET overwrites existing key without duplication", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 3\nDICT 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 1\nPUSH INT 77\nSET\nPOP\nLOAD_LOCAL 0\nPUSH INT 1\nGET\nRETURN", 77, 1));
            Add("SetAlias", "Modification is visible through a different heap alias", t =>
                ExpectIntWithLocals(t,
                    "DICT 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nSTORE_LOCAL 1\nLOAD_LOCAL 0\nPUSH INT 7\nPUSH INT 55\nSET\nPOP\nLOAD_LOCAL 1\nPUSH INT 7\nGET\nRETURN", 55, 2));
            Add("SetReturnsNone", "SET returns None", t =>
                CheckNone(t, "DICT 0\nPUSH INT 5\nPUSH INT 12\nSET\nRETURN"));
            Add("SetSameKeyLength", "SET with equivalent Bool key does not grow the map", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH BOOL TRUE\nPUSH INT 20\nSET\nPOP\nLOAD_LOCAL 0\nLENGTH\nRETURN", 1, 1));
            Add("DeletePresent", "DELETE removes one dictionary key", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 10\nPUSH INT 2\nPUSH INT 20\nDICT 2\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 1\nDELETE\nPOP\nLOAD_LOCAL 0\nLENGTH\nRETURN", 1, 1));
            Add("DeleteMissing", "DELETE missing key returns script error", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nPUSH INT 3\nDELETE\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("DeleteReturnsNone", "DELETE returns None", t =>
                CheckNone(t, "PUSH INT 1\nPUSH INT 2\nDICT 1\nPUSH INT 1\nDELETE\nRETURN"));
            Add("PopReturnsValue", "POP_ITEM uses the key and returns its old value", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 22\nDICT 1\nPUSH INT 1\nPOP_ITEM\nRETURN", 22));
            Add("PopMissing", "POP_ITEM missing key returns script error", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nPUSH INT 9\nPOP_ITEM\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("PopRemoves", "POP_ITEM removes the key", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 22\nDICT 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 1\nPOP_ITEM\nPOP\nLOAD_LOCAL 0\nLENGTH\nRETURN", 0, 1));
            Add("Clear", "DICT_CLEAR removes every pair", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nDICT_CLEAR\nPOP\nLOAD_LOCAL 0\nLENGTH\nRETURN", 0, 1));
            Add("ClearEmpty", "DICT_CLEAR on empty map succeeds", t =>
                CheckNone(t, "DICT 0\nDICT_CLEAR\nRETURN"));
            Add("CopyContent", "COPY retains keys and values", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH INT 12\nDICT 1\nCOPY\nPUSH INT 1\nGET\nRETURN", 12));
            Add("CopyNewIdentity", "COPY returns a different object reference", t =>
                DILTestHarness.ExpectBool(t,
                    "DICT 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nCOPY\nLOAD_LOCAL 0\nREFERENCE_EQUALS\nRETURN", false, locals: 1));
            Add("IdentityCast", "CAST DICT is an identity cast", t =>
                DILTestHarness.ExpectBool(t,
                    "DICT 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nCAST DICT\nLOAD_LOCAL 0\nREFERENCE_EQUALS\nRETURN", true, locals: 1));
            Add("IdentitySame", "REFERENCE_EQUALS returns true for same dictionary", t =>
                DILTestHarness.ExpectBool(t,
                    "DICT 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nLOAD_LOCAL 0\nREFERENCE_EQUALS\nRETURN", true, locals: 1));
            Add("EqualityIgnoreOrder", "Dictionaries compare entries regardless of insertion order", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH INT 10\nPUSH INT 2\nPUSH INT 20\nDICT 2\n" +
                    "PUSH INT 2\nPUSH INT 20\nPUSH INT 1\nPUSH INT 10\nDICT 2\nVALUE_EQUALS\nRETURN", true));
            Add("EqualityNumericKeys", "Equal numeric keys and values compare equal", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH INT 7\nDICT 1\nPUSH BOOL TRUE\nPUSH INT 7\nDICT 1\nVALUE_EQUALS\nRETURN", true));
            Add("InequalityValue", "Same key with a different value is unequal", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH INT 7\nDICT 1\nPUSH INT 1\nPUSH INT 8\nDICT 1\nVALUE_EQUALS\nRETURN", false));
            Add("InequalityKey", "Different keys make dictionaries unequal", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH INT 7\nDICT 1\nPUSH INT 2\nPUSH INT 7\nDICT 1\nVALUE_EQUALS\nRETURN", false));
            Add("InequalityCount", "Different dictionary lengths compare unequal", t =>
                DILTestHarness.ExpectBool(t,
                    "DICT 0\nPUSH INT 1\nPUSH INT 2\nDICT 1\nVALUE_EQUALS\nRETURN", false));
            Add("DifferentTypeEquality", "List and dictionary are not value-equal", t =>
                DILTestHarness.ExpectBool(t, "DICT 0\nLIST 0\nVALUE_EQUALS\nRETURN", false));
            Add("DictNotHashable", "Mutable dictionaries cannot be used as hash keys", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nHASH\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("ListAsKeyError", "Mutable List key gives UnhashableType", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "LIST 0\nPUSH INT 1\nDICT 1\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("SetAsKeyError", "Mutable Set key gives UnhashableType", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nPUSH INT 1\nDICT 1\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("DictAsKeyError", "Mutable dictionary cannot itself be a key", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nPUSH INT 1\nDICT 1\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("TupleMutableKeyError", "Tuple with mutable child remains unhashable", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nTUPLE 1\nPUSH INT 1\nDICT 1\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("MissingKeyDefaultUnhashable", "Invalid keys do not silently use a fallback", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nLIST 0\nPUSH INT 1\nDICT_GET_DEFAULT\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("DictStackUnderflow", "Constructor requires two values per pair", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nDICT 1\nRETURN"), RuntimeErrorId.StackUnderflow));
            Add("InvalidNegativeCount", "Parser rejects negative DICT count", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("DICT -1\nRETURN"), "non-negative"));
            Add("InvalidOversizedCount", "Parser rejects pair count that would overflow stack slots", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("DICT 2147483647\nRETURN"), "non-negative"));
            Add("InvalidCast", "Unsupported Int to Dict conversion reports InvalidCast", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH INT 4\nCAST DICT\nRETURN"), RuntimeErrorId.InvalidCast));
            Add("UpdateAddsMissing", "DICT_UPDATE inserts missing source entries", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nSTORE_LOCAL 0\n" +
                    "PUSH INT 2\nPUSH INT 20\nDICT 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nDICT_UPDATE\nPOP\nLOAD_LOCAL 0\nLENGTH\nRETURN", 2, 2));
            Add("UpdateReplacesExisting", "DICT_UPDATE overwrites values of equal keys", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nSTORE_LOCAL 0\n" +
                    "PUSH BOOL TRUE\nPUSH INT 90\nDICT 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nDICT_UPDATE\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nGET\nRETURN", 90, 2));
            Add("UpdatePreservesOriginalKey", "Equivalent source key leaves destination key type unchanged", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nSTORE_LOCAL 0\n" +
                    "PUSH BOOL TRUE\nPUSH INT 90\nDICT 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nDICT_UPDATE\nPOP\nLOAD_LOCAL 0\nRETURN", locals: 2);
                var dict = (DictHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(DataTypeId.Int, dict.GetKey(0).dataType, "original key retained");
                t.Equal(90L, unchecked((long)dict.GetValue(0).value), "updated value");
            });
            Add("UpdateDoesNotMutateSource", "DICT_UPDATE copies from source without mutating it", t =>
                ExpectIntWithLocals(t,
                    "DICT 0\nSTORE_LOCAL 0\n" +
                    "PUSH INT 1\nPUSH INT 9\nDICT 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nDICT_UPDATE\nPOP\n" +
                    "LOAD_LOCAL 1\nLENGTH\nRETURN", 1, 2));
            Add("UpdateEmptySource", "DICT_UPDATE with empty source leaves target entries intact", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 6\nDICT 1\nSTORE_LOCAL 0\n" +
                    "DICT 0\nSTORE_LOCAL 1\nLOAD_LOCAL 0\nLOAD_LOCAL 1\nDICT_UPDATE\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nGET\nRETURN", 6, 2));
            Add("UpdateReturnsNone", "DICT_UPDATE returns None after success", t =>
                CheckNone(t, "DICT 0\nDICT 0\nDICT_UPDATE\nRETURN"));
            Add("UpdateKeepsAlias", "Buffered update keeps destination identity stable", t =>
                DILTestHarness.ExpectBool(t,
                    "DICT 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nSTORE_LOCAL 1\n" +
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nSTORE_LOCAL 2\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 2\nDICT_UPDATE\nPOP\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nREFERENCE_EQUALS\nRETURN", true, locals: 3));
            Add("CopyDoesNotAliasContainer", "Changes to copied Dictionary do not change original", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nCOPY\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 1\nPUSH INT 2\nPUSH INT 20\nSET\nPOP\n" +
                    "LOAD_LOCAL 0\nLENGTH\nRETURN", 1, 2));
            Add("CopyShallowValues", "Copied dictionary preserves identity of referenced values", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nLIST 0\nDICT 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nCOPY\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nGET\n" +
                    "LOAD_LOCAL 1\nPUSH INT 1\nGET\nREFERENCE_EQUALS\nRETURN", true, locals: 2));
            Add("DeleteReinsertOrder", "Delete then insert places key at end of the dictionary", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 10\nPUSH INT 2\nPUSH INT 20\nDICT 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nDELETE\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nPUSH INT 30\nSET\nPOP\nLOAD_LOCAL 0\nRETURN", locals: 1);
                var dict = (DictHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(2L, unchecked((long)dict.GetKey(0).value), "old second key first");
                t.Equal(1L, unchecked((long)dict.GetKey(1).value), "reinserted key last");
            });
            Add("DefaultMutableValue", "DICT_GET_DEFAULT can return a non-hashable default value", t =>
                DILTestHarness.ExpectInt(t,
                    "DICT 0\nPUSH INT 1\nPUSH INT 3\nLIST 1\nDICT_GET_DEFAULT\nLENGTH\nRETURN", 1));
            Add("UpdateDuplicateKeyLength", "Overlapping dictionaries count the union of their unique keys", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nSTORE_LOCAL 0\n" +
                    "PUSH BOOL TRUE\nPUSH INT 3\nPUSH INT 2\nPUSH INT 4\nDICT 2\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nDICT_UPDATE\nPOP\nLOAD_LOCAL 0\nLENGTH\nRETURN", 2, 2));
            Add("UnsupportedAppend", "Append is not a dictionary operation", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nPUSH INT 9\nAPPEND\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("UnsupportedInsert", "Index insertion is not a dictionary operation", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nPUSH INT 0\nPUSH INT 10\nINSERT\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("UnsupportedIndexOf", "Dictionary does not supply a numeric index", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nPUSH INT 1\nINDEX_OF\nRETURN"), RuntimeErrorId.InvalidOperation));
        }

        private static void ExpectIntWithLocals(DirectivaTestContext t, string body,
            long expected, int locals)
        {
            var r = DILTestHarness.ExpectCompleted(t, body, locals: locals);
            t.Equal(DataTypeId.Int, r.Value.dataType, "return type");
            t.Equal(expected, unchecked((long)r.Value.value), "return value");
        }

        private static void CheckNone(DirectivaTestContext t, string body)
        {
            var r = DILTestHarness.ExpectCompleted(t, body);
            t.Equal(DataTypeId.None, r.Value.dataType, "mutator return type");
        }
    }
}
