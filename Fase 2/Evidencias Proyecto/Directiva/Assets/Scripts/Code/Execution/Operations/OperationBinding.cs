using System;

namespace DSExecution.Operations
{
    [AttributeUsage(
        AttributeTargets.Method,
        AllowMultiple = false,
        Inherited = false
    )]
    public sealed class OperationBindingAttribute : Attribute
    {
        public OperationId OperationId { get; }

        public OperationBindingAttribute(OperationId operationId)
        {
            OperationId = operationId;
        }
    }
}