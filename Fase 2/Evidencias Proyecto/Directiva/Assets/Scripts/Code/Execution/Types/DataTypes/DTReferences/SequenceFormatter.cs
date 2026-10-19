using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// Shared String representation of List and Tuple, including mixed nesting,
    /// string quoting and cyclic-list placeholders; no separate repr() API.
    /// </summary>
    internal static class SequenceFormatter
    {
        private sealed class Frame
        {
            internal readonly uint Reference;
            internal readonly HeapObject Sequence;
            internal readonly bool IsTuple;
            internal readonly int Count;
            internal int Next;

            internal Frame(uint reference, HeapObject sequence, bool isTuple, int count)
            {
                Reference = reference;
                Sequence = sequence;
                IsTuple = isTuple;
                Count = count;
            }

            internal DataValue Element(int index) => IsTuple
                ? ((TupleHeapObject)Sequence).GetElement(index)
                : ((ListHeapObject)Sequence).GetElement(index);
        }

        internal static bool TryFormat(DataValue root, VMMemoryContext memory,
            out string text, out RuntimeError error)
        {
            var builder = new StringBuilder();
            var frames = new Stack<Frame>();
            var active = new HashSet<uint>();
            error = null;
            DataValue next = root;

            while (true)
            {
                if (next.dataType == DataTypeId.List || next.dataType == DataTypeId.Tuple)
                {
                    bool isTuple = next.dataType == DataTypeId.Tuple;
                    if (!next.TryGetHeapReference(out uint reference) ||
                        !memory.TryGetHeap(reference, out HeapObject sequence))
                    {
                        error = RuntimeError.InvalidMemoryAccess((uint)next.value);
                        text = null;
                        return false;
                    }
                    if (!(isTuple ? sequence is TupleHeapObject : sequence is ListHeapObject))
                    {
                        error = RuntimeError.InternalVmError("Sequence reference has incorrect heap type.");
                        text = null;
                        return false;
                    }
                    if (!active.Add(reference))
                        builder.Append(isTuple ? "(...)" : "[...]");
                    else
                    {
                        int count = isTuple ? ((TupleHeapObject)sequence).Count :
                            ((ListHeapObject)sequence).Count;
                        builder.Append(isTuple ? '(' : '[');
                        frames.Push(new Frame(reference, sequence, isTuple, count));
                    }
                }
                else if (next.dataType == DataTypeId.String)
                {
                    if (!TryReadString(next, memory, out string literal, out error))
                    {
                        text = null;
                        return false;
                    }
                    AppendQuoted(builder, literal);
                }
                else
                {
                    if (next.dataType == DataTypeId.Uninitialized)
                    {
                        error = RuntimeError.VariableUsedBeforeInitialization();
                        text = null;
                        return false;
                    }
                    DataType type = DataType.GetDataType(next.dataType);
                    if (type == null)
                    {
                        error = RuntimeError.InvalidCast(next.dataType, DataTypeId.String);
                        text = null;
                        return false;
                    }
                    var cast = type.CastTo(DataTypeId.String, next, memory);
                    if (!cast.IsSuccess)
                    {
                        error = cast.Error;
                        text = null;
                        return false;
                    }
                    if (!TryReadString(cast.Value, memory, out string leaf, out error))
                    {
                        text = null;
                        return false;
                    }
                    builder.Append(leaf);
                }

                // Unwind any completed containers and resume at the next element.
                while (frames.Count > 0)
                {
                    Frame frame = frames.Peek();
                    if (frame.Next == frame.Count)
                    {
                        if (frame.IsTuple && frame.Count == 1)
                            builder.Append(',');
                        builder.Append(frame.IsTuple ? ')' : ']');
                        frames.Pop();
                        active.Remove(frame.Reference);
                        continue;
                    }
                    if (frame.Next > 0)
                        builder.Append(", ");
                    next = frame.Element(frame.Next++);
                    break;
                }
                if (frames.Count == 0)
                    break;
            }
            text = builder.ToString();
            return true;
        }

        private static bool TryReadString(DataValue value, IMemContext memory,
            out string result, out RuntimeError error)
        {
            result = null;
            error = null;
            if (value.dataType != DataTypeId.String ||
                !value.TryGetHeapReference(out uint reference) ||
                !memory.TryGetHeap(reference, out HeapObject obj) ||
                !(obj is StringHeapObject stringObject))
            {
                error = RuntimeError.InvalidOperation("Expected a valid heap-backed String.");
                return false;
            }
            result = stringObject.Value;
            return true;
        }

        private static void AppendQuoted(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (char.IsControl(c))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
