using System.Collections.Generic;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// Iterative structural comparison shared by Tuple, List and Dict. Prevents infinite
    /// traversal of recursive List graphs, including Tuple-List reference cycles.
    /// </summary>
    internal static class SequenceEquality
    {
        internal static OperationResult Compare(IMemContext memory, DataValue left, DataValue right)
        {
            var pending = new Stack<(DataValue Left, DataValue Right)>();
            var seen = new HashSet<(DataTypeId Type, ulong Left, ulong Right)>();
            pending.Push((left, right));

            while (pending.Count > 0)
            {
                var pair = pending.Pop();
                bool tuple = pair.Left.dataType == DataTypeId.Tuple &&
                             pair.Right.dataType == DataTypeId.Tuple;
                bool list = pair.Left.dataType == DataTypeId.List &&
                            pair.Right.dataType == DataTypeId.List;
                bool dict = pair.Left.dataType == DataTypeId.Dict &&
                            pair.Right.dataType == DataTypeId.Dict;
                if (!tuple && !list && !dict)
                {
                    if (pair.Left.dataType == DataTypeId.List ||
                        pair.Left.dataType == DataTypeId.Tuple ||
                        pair.Left.dataType == DataTypeId.Dict ||
                        pair.Right.dataType == DataTypeId.List ||
                        pair.Right.dataType == DataTypeId.Tuple ||
                        pair.Right.dataType == DataTypeId.Dict)
                        return OperationResult.Success(DataValue.FromBool(false));
                    OperationResult result = OperationDispatcher.Start(OperationId.ValueEquals,
                        new[] { pair.Left, pair.Right }, memory).Result;
                    if (result.Status == OperationStatus.Continued)
                        return OperationResult.OpError(RuntimeError.InternalVmError(
                            "Nested continued equality is not yet supported in sequence equality."));
                    if (result.Status != OperationStatus.Success)
                        return result;
                    if (result.Value.dataType != DataTypeId.Bool)
                        return OperationResult.OpError(RuntimeError.InternalVmError(
                            "ValueEquals returned non-Bool while comparing sequences."));
                    if (result.Value.value == 0)
                        return OperationResult.Success(DataValue.FromBool(false));
                    continue;
                }

                if (!pair.Left.TryGetHeapReference(out uint leftRef) ||
                    !pair.Right.TryGetHeapReference(out uint rightRef))
                    return OperationResult.OpError(RuntimeError.InvalidOperation(
                        "Sequence has an invalid heap reference."));
                if (!memory.TryGetHeap(leftRef, out HeapObject first) ||
                    !memory.TryGetHeap(rightRef, out HeapObject second))
                    return OperationResult.OpError(RuntimeError.InvalidMemoryAccess(
                        !memory.TryGetHeap(leftRef, out _) ? leftRef : rightRef));

                int leftCount;
                int rightCount;
                if (tuple && first is TupleHeapObject leftTuple && second is TupleHeapObject rightTuple)
                {
                    leftCount = leftTuple.Count;
                    rightCount = rightTuple.Count;
                    if (leftCount != rightCount) return OperationResult.Success(DataValue.FromBool(false));
                    if (!seen.Add((DataTypeId.Tuple, leftRef, rightRef))) continue;
                    for (int i = leftCount - 1; i >= 0; i--)
                        pending.Push((leftTuple.GetElement(i), rightTuple.GetElement(i)));
                }
                else if (list && first is ListHeapObject leftList && second is ListHeapObject rightList)
                {
                    leftCount = leftList.Count;
                    rightCount = rightList.Count;
                    if (leftCount != rightCount) return OperationResult.Success(DataValue.FromBool(false));
                    if (!seen.Add((DataTypeId.List, leftRef, rightRef))) continue;
                    for (int i = leftCount - 1; i >= 0; i--)
                        pending.Push((leftList.GetElement(i), rightList.GetElement(i)));
                }
                else if (dict && first is DictHeapObject leftDict && second is DictHeapObject rightDict)
                {
                    if (leftDict.Count != rightDict.Count)
                        return OperationResult.Success(DataValue.FromBool(false));
                    if (!seen.Add((DataTypeId.Dict, leftRef, rightRef))) continue;
                    for (int i = leftDict.Count - 1; i >= 0; i--)
                    {
                        if (!rightDict.TryFind(leftDict.GetKey(i), leftDict.GetKeyHash(i), memory,
                                out int found, out RuntimeError error))
                            return OperationResult.OpError(error);
                        if (found < 0)
                            return OperationResult.Success(DataValue.FromBool(false));
                        pending.Push((leftDict.GetValue(i), rightDict.GetValue(found)));
                    }
                }
                else
                {
                    return OperationResult.OpError(RuntimeError.InternalVmError(
                        "Sequence heap reference does not match its DataTypeId."));
                }
            }
            return OperationResult.Success(DataValue.FromBool(true));
        }
    }
}
