using System;
using System.Collections.Generic;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    internal sealed class CallFrame
    {
        private readonly Stack<LocalContext> contexts = new();

        private readonly struct LocalContext
        {
            public int FirstLocal { get; }
            public int LocalCount { get; }

            public LocalContext(int firstLocal, int localCount)
            {
                FirstLocal = firstLocal;
                LocalCount = localCount;
            }
        }

        public FunctionContext Function { get; }
        public LocalMemory Locals { get; }
        public int EvalStackBase { get; }
        public int InstructionPointer { get; set; }

        public CallFrame(
            FunctionContext function,
            int evalStackBase,
            ReadOnlySpan<DataValue> arguments)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));

            if (arguments.Length != function.ParameterCount)
                throw new ArgumentException("Argument count does not match function parameter count.", nameof(arguments));

            EvalStackBase = evalStackBase;
            InstructionPointer = 0;
            Locals = new LocalMemory(function.LocalCount);

            for (int i = 0; i < arguments.Length; i++)
                Locals.TrySet(i, arguments[i]);
        }

        public bool TryEnterContext(int firstLocal, int localCount)
        {
            if (!Locals.IsValidRange(firstLocal, localCount))
                return false;

            Locals.ResetRange(firstLocal, localCount);
            contexts.Push(new LocalContext(firstLocal, localCount));
            return true;
        }

        public bool TryExitContext()
        {
            if (contexts.Count == 0)
                return false;

            var context = contexts.Pop();
            Locals.ResetRange(context.FirstLocal, context.LocalCount);
            return true;
        }
    }
}
