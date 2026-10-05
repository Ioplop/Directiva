using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.DataTypes
{
    public abstract class DTValue : DataType, IOpHash
    {
        protected static ulong Resolve(DataValue value)
            => value.value;

        public abstract OperationResult Hash(OperationCall opCall);
    }
}