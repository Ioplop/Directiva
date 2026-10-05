# nullable enable
using System;
using System.Linq;
using System.Reflection;
using DSExecution.Operations;
using DSExecution.Values;



namespace DSExecution.DataTypes
{
    public abstract class DataType : IOpValueEquality
    {
        // TODO: Initialize this singleton registry. Each DType is responsible of registering itself here upon initialization... right? Check!
        private static readonly DataType?[] dataTypes = CreateTypes();

        private static class Registry
        {
            internal static readonly DataType?[] Types = CreateTypes();
        }

        public static DataType? GetDataType(DataTypeId id)
            => Registry.Types[(int)id];

        private delegate OperationResult OperationHandler(OperationCall operationCall);

        private OperationHandler?[] operations;

        private OperationHandler? GetOperation(OperationId id)
            => operations[(int)id];


        /// <summary>
        /// Se encarga de generar la lista de referencias a cada tipo de dato.
        /// </summary>
        private static DataType?[] CreateTypes()
        {
            int maxDataTypeId = Enum
                .GetValues(typeof(DataTypeId))
                .Cast<DataTypeId>()
                .Max(id => (int)id);

            DataType?[] result = new DataType?[maxDataTypeId + 1];

            result[(int)DataTypeId.Int] = DTInt.Instance;
            // Remaining to add...
            // Uninitialized
            // None
            // Bool
            // Decimal
            // String
            // List
            // Set
            // Dict
            // Unit
            // Base
            // Resource

            return result;
        }

        protected DataType()
        {
            int maxOperationId = Enum
                .GetValues(typeof(OperationId))
                .Cast<OperationId>()
                .Max(id => (int)id);

            operations = new OperationHandler?[maxOperationId + 1];
            PopulateOperations();
        }

        private void PopulateOperations()
        {
            // Obtener tipo de esta clase particular
            Type concreteType = GetType();

            // Iterar sobre cada interfaz que esta clase implementa.
            foreach (Type interfaceType in concreteType.GetInterfaces())
            {
                // Obtenemos cuales son los métodos en esta clase que están asociados a los declarados por una interfaz en cuestión.
                InterfaceMapping map =
                    concreteType.GetInterfaceMap(interfaceType);

                for (int i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    // Información de un método en particular, desde el lado de la interfaz.
                    MethodInfo interfaceMethod =
                        map.InterfaceMethods[i];

                    // Vemos si tiene binding a una de nuestras operaciones.
                    var binding =
                        interfaceMethod.GetCustomAttribute<
                            OperationBindingAttribute
                        >();

                    if (binding == null)
                        continue;

                    // Obtener id de operación basados en OperationId
                    int operationIndex =
                        (int)binding.OperationId;

                    // Validamos que la operación no esté implementada varias veces. (aunque permite override del método en clases hijas porque las
                    // implementaciones de clases base que fueron sobrescritas no aparecen en esta lista)
                    if (operations[operationIndex] != null)
                    {
                        throw new InvalidOperationException(
                            $"DataType {concreteType.Name} " +
                            $"registered operation " +
                            $"{binding.OperationId} twice."
                        );
                    }

                    // Aquí traemos información del método implementado (No la declaración de interfaz)
                    MethodInfo targetMethod =
                        map.TargetMethods[i];

                    // Creamos delegado para el método en cuestión.
                    var handler =
                        (OperationHandler)targetMethod.CreateDelegate(
                            typeof(OperationHandler),
                            this
                        );

                    // Registrar método de operación.
                    operations[operationIndex] = handler;
                }
            }
        }

        public OperationResult Operate(OperationId operationId, OperationCall opCall)
        {
            OperationHandler? operationHandler = GetOperation(operationId);

            if (operationHandler == null)
                return OperationResult.NotImplemented();

            return operationHandler(opCall);
        }

        public virtual OperationResult ValueEquals(OperationCall opCall)
        {
            var left = opCall.Arguments[0];
            var right = opCall.Arguments[1];
            if (left.dataType == right.dataType)
            {
                var result = left.value == right.value;
                return OperationResult.Success(DataValue.FromBool(result));
            }
            return OperationResult.NotImplemented();
        }
    }
}