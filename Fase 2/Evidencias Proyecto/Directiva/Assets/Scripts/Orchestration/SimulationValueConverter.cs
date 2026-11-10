using System;
using System.Collections.Generic;
using DMath;
using DSimulation;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DOrchestration
{
    /// <summary>
    /// The sole conversion boundary between DScript values and the public simulation API.
    /// Neither the VM nor the data types contain a reference to DSimulation.
    /// </summary>
    internal static class SimulationValueConverter
    {
        // This sentinel is never handed to simulation methods; it only ensures overload
        // resolution rejects a DScript type instead of mistaking it for nullable None.
        private static readonly object Unsupported = new object();

        internal static object ToHost(DataValue value, IMemContext memory)
        {
            switch (value.dataType)
            {
                case DataTypeId.None: return null;
                case DataTypeId.Int: return unchecked((long)value.value);
                case DataTypeId.Bool: return value.value != 0;
                case DataTypeId.Decimal: return new Fixed(unchecked((long)value.value), raw: true);
                case DataTypeId.SimulationObject:
                    return value.TryGetSimulationObjectId(out int id)
                        ? (object)new SimulationEntityReference(id) : Unsupported;
                case DataTypeId.SimulationEnum:
                    return ToHostEnum(value);
                case DataTypeId.Tuple:
                    return TryGetVector(value, memory, out FixedVector2 vector)
                        ? (object)vector : Unsupported;
                default:
                    return Unsupported;
            }
        }

        private static object ToHostEnum(DataValue value)
        {
            if (!value.TryGetSimulationEnum(out SimulationEnumKind kind, out int id))
                return Unsupported;
            Type type = kind switch
            {
                SimulationEnumKind.Side => typeof(SimulationSideRelation),
                SimulationEnumKind.Type => typeof(SimulationEntityType),
                SimulationEnumKind.MoveMode => typeof(SimulationMoveMode),
                SimulationEnumKind.ActionMode => typeof(SimulationActionMode),
                _ => null
            };
            return type != null && Enum.IsDefined(type, id)
                ? Enum.ToObject(type, id) : Unsupported;
        }

        private static bool TryGetVector(DataValue value, IMemContext memory, out FixedVector2 vector)
        {
            vector = FixedVector2.Zero;
            if (!value.TryGetHeapReference(out uint reference) ||
                !memory.TryGetHeap(reference, out HeapObject obj) ||
                obj is not TupleHeapObject tuple || tuple.Count != 2 ||
                !TryGetFixed(tuple.GetElement(0), out Fixed x) ||
                !TryGetFixed(tuple.GetElement(1), out Fixed y))
                return false;
            vector = new FixedVector2(x, y);
            return true;
        }

        private static bool TryGetFixed(DataValue value, out Fixed result)
        {
            if (value.dataType == DataTypeId.Decimal)
            {
                result = new Fixed(unchecked((long)value.value), raw: true);
                return true;
            }
            if (value.dataType == DataTypeId.Int)
            {
                try
                {
                    result = new Fixed(unchecked((long)value.value));
                    return true;
                }
                catch (OverflowException) { }
            }
            result = default;
            return false;
        }

        internal static OperationResult FromHost(object value, IMemContext memory)
        {
            if (value == null) return OperationResult.Success(DataValue.None());
            switch (value)
            {
                case bool boolean: return OperationResult.Success(DataValue.FromBool(boolean));
                case int integer: return OperationResult.Success(DataValue.FromInt(integer));
                case long longInteger: return OperationResult.Success(DataValue.FromInt(longInteger));
                case Fixed number: return OperationResult.Success(DataValue.FromDecimalRaw(number.RawValue));
                case SimulationEntityReference entity:
                    return entity.Id > 0
                        ? OperationResult.Success(DataValue.FromSimulationObject(entity.Id))
                        : OperationResult.OpError(RuntimeError.InvalidOperation("Simulation returned an invalid entity ID."));
                case FixedVector2 vector:
                    return AllocateTuple(memory, new[]
                    {
                        DataValue.FromDecimalRaw(vector.X.RawValue),
                        DataValue.FromDecimalRaw(vector.Y.RawValue)
                    });
                case List<SimulationEntityReference> entities:
                {
                    var values = new DataValue[entities.Count];
                    for (int i = 0; i < entities.Count; i++)
                    {
                        if (entities[i].Id <= 0)
                            return OperationResult.OpError(RuntimeError.InvalidOperation(
                                "Simulation returned an invalid entity ID in a list."));
                        values[i] = DataValue.FromSimulationObject(entities[i].Id);
                    }
                    return memory.TryAllocHeap(new ListHeapObject(values), out uint reference)
                        ? OperationResult.Success(DataValue.FromListReference(reference))
                        : OperationResult.OpError(RuntimeError.OutOfMemory());
                }
                case SimulationSideRelation side:
                    return OperationResult.Success(DataValue.FromSimulationEnum(SimulationEnumKind.Side, (int)side));
                case SimulationEntityType type:
                    return OperationResult.Success(DataValue.FromSimulationEnum(SimulationEnumKind.Type, (int)type));
                case SimulationMoveMode move:
                    return OperationResult.Success(DataValue.FromSimulationEnum(SimulationEnumKind.MoveMode, (int)move));
                case SimulationActionMode action:
                    return OperationResult.Success(DataValue.FromSimulationEnum(SimulationEnumKind.ActionMode, (int)action));
                default:
                    return OperationResult.OpError(RuntimeError.InvalidOperation(
                        "Unsupported simulation result type: " + value.GetType().Name));
            }
        }

        private static OperationResult AllocateTuple(IMemContext memory, DataValue[] values)
        {
            return memory.TryAllocHeap(new TupleHeapObject(values), out uint reference)
                ? OperationResult.Success(DataValue.FromTupleReference(reference))
                : OperationResult.OpError(RuntimeError.OutOfMemory());
        }
    }
}
