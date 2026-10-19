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
    /// Shared String representation of List, Tuple and Set, including mixed nesting,
    /// string quoting and cyclic-list placeholders; no separate repr() API.
    /// </summary>
    internal static class SequenceFormatter
    {
        private sealed class Frame
        {
            internal readonly uint Reference;
            internal readonly HeapObject Sequence;
            internal readonly DataTypeId Kind;
            internal readonly int Count;
            internal int Next;

            internal Frame(uint reference, HeapObject sequence, DataTypeId kind, int count)
            {
                Reference = reference;
                Sequence = sequence;
                Kind = kind;
                Count = count;
            }

            internal DataValue Element(int index) => Kind switch
            {
                DataTypeId.Tuple => ((TupleHeapObject)Sequence).GetElement(index),
                DataTypeId.List => ((ListHeapObject)Sequence).GetElement(index),
                DataTypeId.Set => ((SetHeapObject)Sequence).GetElement(index),
                _ => throw new InvalidOperationException("Unsupported sequence kind.")
            };
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
                if (next.dataType == DataTypeId.List || next.dataType == DataTypeId.Tuple ||
                    next.dataType == DataTypeId.Set)
                {
                    DataTypeId kind = next.dataType;
                    if (!next.TryGetHeapReference(out uint reference) ||
                        !memory.TryGetHeap(reference, out HeapObject sequence))
                    {
                        error = RuntimeError.InvalidMemoryAccess((uint)next.value);
                        text = null;
                        return false;
                    }
                    bool valid = kind switch
                    {
                        DataTypeId.Tuple => sequence is TupleHeapObject,
                        DataTypeId.List => sequence is ListHeapObject,
                        DataTypeId.Set => sequence is SetHeapObject,
                        _ => false
                    };
                    if (!valid)
                    {
                        error = RuntimeError.InternalVmError("Sequence reference has incorrect heap type.");
                        text = null;
                        return false;
                    }
                    if (!active.Add(reference))
                        builder.Append(kind == DataTypeId.Tuple ? "(...)" :
                            kind == DataTypeId.Set ? "{...}" : "[...]");
                    else
                    {
                        int count = kind switch
                        {
                            DataTypeId.Tuple => ((TupleHeapObject)sequence).Count,
                            DataTypeId.List => ((ListHeapObject)sequence).Count,
                            _ => ((SetHeapObject)sequence).Count
                        };
                        if (kind == DataTypeId.Set && count == 0)
                        {
                            builder.Append("set()");
                            active.Remove(reference);
                        }
                        else
                        {
                            builder.Append(kind == DataTypeId.Tuple ? '(' :
                                kind == DataTypeId.Set ? '{' : '[');
                            frames.Push(new Frame(reference, sequence, kind, count));
                        }
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
                        if (frame.Kind == DataTypeId.Tuple && frame.Count == 1)
                            builder.Append(',');
                        builder.Append(frame.Kind == DataTypeId.Tuple ? ')' :
                            frame.Kind == DataTypeId.Set ? '}' : ']');
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
