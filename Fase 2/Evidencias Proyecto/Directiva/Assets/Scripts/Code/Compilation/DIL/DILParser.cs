#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using DSExecution.DataTypes;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSCompilation.DIL
{
    /// <summary>
    /// Parses a root DIL module and its imports into the immutable CodeContext consumed by the VM.
    /// Parsing is multi-pass: functions are assigned ids first, globals are laid out second, and
    /// function bodies are assembled only after all numeric symbols are known.
    /// </summary>
    public sealed class DILParser
    {
        private sealed class SourceLine
        {
            public int Number { get; }
            public string Text { get; }

            public SourceLine(int number, string text)
            {
                Number = number;
                Text = text;
            }
        }

        private sealed class FunctionDefinition
        {
            public string Name { get; }
            public string QualifiedName { get; }
            public int DeclarationLine { get; }
            public int ParameterCount { get; set; } = -1;
            public int LocalCount { get; set; } = -1;
            public int Id { get; set; } = -1;

            public FunctionDefinition(string moduleName, string name, int declarationLine)
            {
                Name = name;
                QualifiedName = moduleName + "." + name;
                DeclarationLine = declarationLine;
            }
        }

        private sealed class ModuleDefinition
        {
            public string Name { get; }
            public SourceLine[] Lines { get; }
            public List<string> Imports { get; } = new();
            public List<string> Globals { get; } = new();
            public List<FunctionDefinition> Functions { get; } = new();

            public ModuleDefinition(string name, SourceLine[] lines)
            {
                Name = name;
                Lines = lines;
            }
        }

        private readonly struct PendingJump
        {
            public InstructionId Id { get; }
            public string Label { get; }
            public int InstructionIndex { get; }
            public int SourceLine { get; }

            public PendingJump(
                InstructionId id,
                string label,
                int instructionIndex,
                int sourceLine)
            {
                Id = id;
                Label = label;
                InstructionIndex = instructionIndex;
                SourceLine = sourceLine;
            }
        }

        // Container operations are intentionally excluded from the first DIL implementation.
        // In particular, POP currently means the VM evaluation-stack instruction, not OperationId.Pop.
        private static readonly Dictionary<string, OperationId> OperationMnemonics =
            new(StringComparer.Ordinal)
            {
                ["ADD"] = OperationId.Add,
                ["SUBTRACT"] = OperationId.Subtract,
                ["MULTIPLY"] = OperationId.Multiply,
                ["DIVIDE"] = OperationId.Divide,
                ["FLOOR_DIVIDE"] = OperationId.FloorDivide,
                ["MODULO"] = OperationId.Modulo,
                ["POWER"] = OperationId.Power,
                ["VALUE_EQUALS"] = OperationId.ValueEquals,
                ["REFERENCE_EQUALS"] = OperationId.ReferenceEquals,
                ["GREATER_THAN"] = OperationId.GreaterThan,
                ["GREATER_OR_EQUAL"] = OperationId.GreaterOrEqual,
                ["LESS_THAN"] = OperationId.LessThan,
                ["LESS_OR_EQUAL"] = OperationId.LessOrEqual,
                ["NOT"] = OperationId.Not,
                ["HASH"] = OperationId.Hash,
                ["IS_TRUTHY"] = OperationId.IsTruthy
            };

        private readonly IDILSourceProvider sourceProvider;
        private readonly DILParserOptions options;

        public DILParser(
            IDILSourceProvider sourceProvider,
            DILParserOptions? options = null)
        {
            this.sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
            this.options = options ?? new DILParserOptions();
        }

        /// <summary>
        /// Parses a root module and all modules reachable through its # IMPORT annotations.
        /// The first function declared in the root module becomes the default CodeContext entry.
        /// Individual VMs may override that default when they are created.
        /// </summary>
        public CodeContext Parse(string rootModuleName)
        {
            if (!IsValidQualifiedName(rootModuleName))
                throw new ArgumentException($"Invalid DIL module name '{rootModuleName}'.", nameof(rootModuleName));

            var modules = LoadModuleGraph(rootModuleName);
            var orderedModules = modules.Values
                .OrderBy(module => module.Name, StringComparer.Ordinal)
                .ToArray();

            var functionIds = AssignFunctionIds(orderedModules);
            var globalSlots = AssignGlobalSlots(orderedModules);

            if (globalSlots.Count > options.MaxGlobalSlots)
            {
                throw new DILParseException(
                    rootModuleName,
                    0,
                    $"Program requires {globalSlots.Count} global slots, " +
                    $"but the configured limit is {options.MaxGlobalSlots}."
                );
            }

            if (!modules[rootModuleName].Functions.Any())
            {
                throw new DILParseException(
                    rootModuleName,
                    0,
                    "The root DIL module must declare at least one function."
                );
            }

            var compiledFunctions = CompileFunctions(
                orderedModules,
                functionIds,
                globalSlots
            );

            int defaultEntryId = modules[rootModuleName].Functions[0].Id;

            return new CodeContext(
                defaultEntryId,
                globalSlots.Count,
                compiledFunctions
            );
        }

        /// <summary>
        /// Returns true when a dot-separated DIL name consists entirely of valid identifier segments.
        /// </summary>
        public static bool IsValidQualifiedName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] segments = value.Split('.');
            if (segments.Length == 0)
                return false;

            for (int i = 0; i < segments.Length; i++)
            {
                if (!IsValidIdentifier(segments[i]))
                    return false;
            }

            return true;
        }

        private Dictionary<string, ModuleDefinition> LoadModuleGraph(string rootModuleName)
        {
            var modules = new Dictionary<string, ModuleDefinition>(StringComparer.Ordinal);
            LoadModuleRecursive(rootModuleName, modules);
            return modules;
        }

        private void LoadModuleRecursive(
            string moduleName,
            Dictionary<string, ModuleDefinition> modules)
        {
            if (modules.ContainsKey(moduleName))
                return;

            if (!IsValidQualifiedName(moduleName))
            {
                throw new DILParseException(
                    moduleName,
                    0,
                    $"Invalid module name '{moduleName}'."
                );
            }

            if (!sourceProvider.TryReadModule(moduleName, out string sourceText))
            {
                throw new DILParseException(
                    moduleName,
                    0,
                    $"DIL module '{moduleName}' could not be found."
                );
            }

            var module = ScanModule(moduleName, sourceText ?? string.Empty);
            modules.Add(moduleName, module);

            foreach (string import in module.Imports)
                LoadModuleRecursive(import, modules);
        }

        private static ModuleDefinition ScanModule(string moduleName, string sourceText)
        {
            SourceLine[] lines = SplitLines(sourceText);
            var module = new ModuleDefinition(moduleName, lines);
            var functionNames = new HashSet<string>(StringComparer.Ordinal);

            FunctionDefinition? currentFunction = null;
            bool importsOpen = true;
            bool functionBodyStarted = false;

            foreach (SourceLine sourceLine in lines)
            {
                string line = PrepareLine(sourceLine.Text);
                if (line.Length == 0)
                    continue;

                if (TryParseAnnotation(line, out string annotation, out string? value))
                {
                    if (annotation == "IMPORT")
                    {
                        if (currentFunction != null || !importsOpen)
                            Throw(moduleName, sourceLine.Number, "# IMPORT annotations must appear at the beginning of the file.");

                        RequireAnnotationValue(moduleName, sourceLine.Number, annotation, value);
                        if (!IsValidQualifiedName(value))
                            Throw(moduleName, sourceLine.Number, $"Invalid import module name '{value}'.");

                        module.Imports.Add(value!);
                        continue;
                    }

                    importsOpen = false;

                    if (annotation == "GLOBAL")
                    {
                        if (currentFunction != null)
                            Throw(moduleName, sourceLine.Number, "# GLOBAL cannot appear inside a function.");

                        RequireAnnotationValue(moduleName, sourceLine.Number, annotation, value);
                        if (!IsValidIdentifier(value!))
                            Throw(moduleName, sourceLine.Number, $"Invalid global name '{value}'.");

                        module.Globals.Add(value!);
                        continue;
                    }

                    if (annotation == "FUNCTION")
                    {
                        if (currentFunction != null)
                            Throw(moduleName, sourceLine.Number, "A function cannot begin before the previous # END_FUNCTION.");

                        RequireAnnotationValue(moduleName, sourceLine.Number, annotation, value);
                        if (!IsValidIdentifier(value!))
                            Throw(moduleName, sourceLine.Number, $"Invalid function name '{value}'.");

                        if (!functionNames.Add(value!))
                            Throw(moduleName, sourceLine.Number, $"Function '{value}' is declared more than once in this file.");

                        currentFunction = new FunctionDefinition(moduleName, value!, sourceLine.Number);
                        module.Functions.Add(currentFunction);
                        functionBodyStarted = false;
                        continue;
                    }

                    if (annotation == "PARAMS")
                    {
                        if (currentFunction == null)
                            Throw(moduleName, sourceLine.Number, "# PARAMS must appear inside a function.");
                        if (functionBodyStarted)
                            Throw(moduleName, sourceLine.Number, "# PARAMS must appear before the function body.");
                        if (currentFunction!.ParameterCount >= 0)
                            Throw(moduleName, sourceLine.Number, "# PARAMS is declared more than once for this function.");

                        currentFunction.ParameterCount = ParseNonNegativeInt(
                            moduleName,
                            sourceLine.Number,
                            annotation,
                            value
                        );
                        continue;
                    }

                    if (annotation == "LOCALS")
                    {
                        if (currentFunction == null)
                            Throw(moduleName, sourceLine.Number, "# LOCALS must appear inside a function.");
                        if (functionBodyStarted)
                            Throw(moduleName, sourceLine.Number, "# LOCALS must appear before the function body.");
                        if (currentFunction!.LocalCount >= 0)
                            Throw(moduleName, sourceLine.Number, "# LOCALS is declared more than once for this function.");

                        currentFunction.LocalCount = ParseNonNegativeInt(
                            moduleName,
                            sourceLine.Number,
                            annotation,
                            value
                        );
                        continue;
                    }

                    if (annotation == "END_FUNCTION")
                    {
                        if (value != null)
                            Throw(moduleName, sourceLine.Number, "# END_FUNCTION does not take a value.");
                        if (currentFunction == null)
                            Throw(moduleName, sourceLine.Number, "# END_FUNCTION has no matching # FUNCTION.");

                        ValidateFunctionHeader(moduleName, sourceLine.Number, currentFunction!);
                        currentFunction = null;
                        functionBodyStarted = false;
                        continue;
                    }

                    ValidateNumericSymbolAnnotation(moduleName, sourceLine.Number, annotation, value);
                    continue;
                }

                importsOpen = false;

                if (currentFunction == null)
                    Throw(moduleName, sourceLine.Number, "Executable DIL must appear inside a function.");

                functionBodyStarted = true;
            }

            if (currentFunction != null)
            {
                Throw(
                    moduleName,
                    currentFunction.DeclarationLine,
                    $"Function '{currentFunction.Name}' is missing # END_FUNCTION."
                );
            }

            return module;
        }

        private static Dictionary<string, int> AssignFunctionIds(ModuleDefinition[] orderedModules)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            int nextId = 0;

            foreach (ModuleDefinition module in orderedModules)
            {
                foreach (FunctionDefinition function in module.Functions)
                {
                    function.Id = nextId;
                    result.Add(function.QualifiedName, nextId);
                    nextId++;
                }
            }

            return result;
        }

        private static Dictionary<string, int> AssignGlobalSlots(ModuleDefinition[] orderedModules)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (ModuleDefinition module in orderedModules)
            {
                foreach (string globalName in module.Globals)
                {
                    if (!result.ContainsKey(globalName))
                        result.Add(globalName, result.Count);
                }
            }

            return result;
        }

        private static FunctionContext[] CompileFunctions(
            ModuleDefinition[] orderedModules,
            Dictionary<string, int> functionIds,
            Dictionary<string, int> globalSlots)
        {
            int functionCount = functionIds.Count;
            var compiled = new FunctionContext[functionCount];

            var baseSymbols = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in functionIds)
                baseSymbols.Add(pair.Key, pair.Value);
            foreach (var pair in globalSlots)
                baseSymbols.Add(pair.Key, pair.Value);

            foreach (ModuleDefinition module in orderedModules)
            {
                CompileModule(module, baseSymbols, globalSlots.Count, compiled);
            }

            return compiled;
        }

        private static void CompileModule(
            ModuleDefinition module,
            Dictionary<string, long> baseSymbols,
            int globalCount,
            FunctionContext[] output)
        {
            var symbols = new Dictionary<string, long>(baseSymbols, StringComparer.Ordinal);
            FunctionDefinition? currentFunction = null;
            List<Instruction>? instructions = null;
            Dictionary<string, int>? labels = null;
            List<PendingJump>? pendingJumps = null;

            int functionIndex = 0;

            foreach (SourceLine sourceLine in module.Lines)
            {
                string line = PrepareLine(sourceLine.Text);
                if (line.Length == 0)
                    continue;

                if (TryParseAnnotation(line, out string annotation, out string? value))
                {
                    switch (annotation)
                    {
                        case "IMPORT":
                        case "GLOBAL":
                        case "PARAMS":
                        case "LOCALS":
                            continue;

                        case "FUNCTION":
                            currentFunction = module.Functions[functionIndex++];
                            instructions = new List<Instruction>();
                            labels = new Dictionary<string, int>(StringComparer.Ordinal);
                            pendingJumps = new List<PendingJump>();
                            continue;

                        case "END_FUNCTION":
                            if (currentFunction == null || instructions == null || labels == null || pendingJumps == null)
                                Throw(module.Name, sourceLine.Number, "Internal DIL parser state is inconsistent.");

                            ResolveJumps(
                                module.Name,
                                currentFunction!,
                                instructions!,
                                labels!,
                                pendingJumps!
                            );

                            output[currentFunction!.Id] = new FunctionContext(
                                currentFunction.Id,
                                currentFunction.QualifiedName,
                                currentFunction.ParameterCount,
                                currentFunction.LocalCount,
                                instructions!.ToArray()
                            );

                            currentFunction = null;
                            instructions = null;
                            labels = null;
                            pendingJumps = null;
                            continue;

                        default:
                            long symbolValue = ParseSymbolValue(
                                module.Name,
                                sourceLine.Number,
                                annotation,
                                value
                            );
                            symbols[annotation] = symbolValue;
                            continue;
                    }
                }

                if (currentFunction == null || instructions == null || labels == null || pendingJumps == null)
                    Throw(module.Name, sourceLine.Number, "Executable DIL must appear inside a function.");

                if (TryParseLabel(line, out string? label))
                {
                    if (!labels!.TryAdd(label!, instructions!.Count))
                        Throw(module.Name, sourceLine.Number, $"Label '{label}' is declared more than once in this function.");
                    continue;
                }

                ParseInstruction(
                    module.Name,
                    sourceLine.Number,
                    currentFunction!,
                    line,
                    symbols,
                    globalCount,
                    output.Length,
                    instructions!,
                    pendingJumps!
                );
            }
        }

        private static void ParseInstruction(
            string moduleName,
            int lineNumber,
            FunctionDefinition function,
            string line,
            Dictionary<string, long> symbols,
            int globalCount,
            int functionCount,
            List<Instruction> instructions,
            List<PendingJump> pendingJumps)
        {
            string[] tokens = SplitTokens(line);
            if (tokens.Length == 0)
                return;

            string mnemonic = tokens[0];

            if (OperationMnemonics.TryGetValue(mnemonic, out OperationId operationId))
            {
                RequireTokenCount(moduleName, lineNumber, mnemonic, tokens, 1);
                instructions.Add(Instruction.Operation(operationId));
                return;
            }

            switch (mnemonic)
            {
                case "PASS":
                    RequireTokenCount(moduleName, lineNumber, mnemonic, tokens, 1);
                    instructions.Add(Instruction.Nop());
                    return;

                case "PUSH":
                    instructions.Add(ParsePush(moduleName, lineNumber, tokens, symbols));
                    return;

                case "POP":
                    RequireTokenCount(moduleName, lineNumber, mnemonic, tokens, 1);
                    instructions.Add(Instruction.Pop());
                    return;

                case "LOAD_LOCAL":
                {
                    int localIndex = ParseIntOperand(moduleName, lineNumber, mnemonic, tokens, 1, symbols);
                    ValidateLocalIndex(moduleName, lineNumber, function, localIndex);
                    instructions.Add(Instruction.LoadLocal(localIndex));
                    return;
                }

                case "STORE_LOCAL":
                {
                    int localIndex = ParseIntOperand(moduleName, lineNumber, mnemonic, tokens, 1, symbols);
                    ValidateLocalIndex(moduleName, lineNumber, function, localIndex);
                    instructions.Add(Instruction.StoreLocal(localIndex));
                    return;
                }

                case "LOAD_GLOBAL":
                {
                    int globalIndex = ParseIntOperand(moduleName, lineNumber, mnemonic, tokens, 1, symbols);
                    ValidateGlobalIndex(moduleName, lineNumber, globalCount, globalIndex);
                    instructions.Add(Instruction.LoadGlobal(globalIndex));
                    return;
                }

                case "STORE_GLOBAL":
                {
                    int globalIndex = ParseIntOperand(moduleName, lineNumber, mnemonic, tokens, 1, symbols);
                    ValidateGlobalIndex(moduleName, lineNumber, globalCount, globalIndex);
                    instructions.Add(Instruction.StoreGlobal(globalIndex));
                    return;
                }

                case "CALL":
                {
                    int functionId = ParseIntOperand(moduleName, lineNumber, mnemonic, tokens, 1, symbols);
                    if ((uint)functionId >= (uint)functionCount)
                        Throw(moduleName, lineNumber, $"Function id {functionId} does not exist in this DIL program.");

                    instructions.Add(Instruction.Call(functionId));
                    return;
                }

                case "RETURN":
                    RequireTokenCount(moduleName, lineNumber, mnemonic, tokens, 1);
                    instructions.Add(Instruction.Return());
                    return;

                case "CONTEXT_START":
                {
                    RequireTokenCount(moduleName, lineNumber, mnemonic, tokens, 3);
                    int firstLocal = ResolveInt32(moduleName, lineNumber, tokens[1], symbols);
                    int localCount = ResolveInt32(moduleName, lineNumber, tokens[2], symbols);

                    if (firstLocal < 0 || localCount < 0 || (long)firstLocal + localCount > function.LocalCount)
                    {
                        Throw(
                            moduleName,
                            lineNumber,
                            $"CONTEXT_START range [{firstLocal}, {(long)firstLocal + localCount}) " +
                            $"is outside the {function.LocalCount} local slots of '{function.QualifiedName}'."
                        );
                    }

                    instructions.Add(Instruction.ContextStart(firstLocal, localCount));
                    return;
                }

                case "CONTEXT_END":
                    RequireTokenCount(moduleName, lineNumber, mnemonic, tokens, 1);
                    instructions.Add(Instruction.ContextEnd());
                    return;

                case "JUMP":
                    ParseJump(moduleName, lineNumber, InstructionId.Jump, mnemonic, tokens, symbols, instructions, pendingJumps);
                    return;

                case "JUMP_IF_TRUE":
                    ParseJump(moduleName, lineNumber, InstructionId.JumpIfTrue, mnemonic, tokens, symbols, instructions, pendingJumps);
                    return;

                case "JUMP_IF_FALSE":
                    ParseJump(moduleName, lineNumber, InstructionId.JumpIfFalse, mnemonic, tokens, symbols, instructions, pendingJumps);
                    return;

                default:
                    Throw(moduleName, lineNumber, $"Unknown DIL instruction or operation '{mnemonic}'.");
                    return;
            }
        }

        private static Instruction ParsePush(
            string moduleName,
            int lineNumber,
            string[] tokens,
            Dictionary<string, long> symbols)
        {
            if (tokens.Length < 2)
                Throw(moduleName, lineNumber, "PUSH requires a type.");

            string type = tokens[1];

            switch (type)
            {
                case "NONE":
                    RequireTokenCount(moduleName, lineNumber, "PUSH NONE", tokens, 2);
                    return Instruction.Push(DataValue.None());

                case "BOOL":
                    RequireTokenCount(moduleName, lineNumber, "PUSH BOOL", tokens, 3);
                    if (tokens[2] == "TRUE")
                        return Instruction.Push(DataValue.FromBool(true));
                    if (tokens[2] == "FALSE")
                        return Instruction.Push(DataValue.FromBool(false));
                    Throw(moduleName, lineNumber, "BOOL literal must be TRUE or FALSE.");
                    break;

                case "INT":
                    RequireTokenCount(moduleName, lineNumber, "PUSH INT", tokens, 3);
                    return Instruction.Push(
                        DataValue.FromInt(ResolveInt64(moduleName, lineNumber, tokens[2], symbols))
                    );

                case "DECIMAL":
                    RequireTokenCount(moduleName, lineNumber, "PUSH DECIMAL", tokens, 3);
                    string decimalToken = ResolveSymbolText(moduleName, lineNumber, tokens[2], symbols);
                    return Instruction.Push(
                        DataValue.FromDecimalRaw(ParseDecimalRaw(moduleName, lineNumber, decimalToken))
                    );

                default:
                    Throw(moduleName, lineNumber, $"Unsupported PUSH type '{type}' in the current DIL implementation.");
                    break;
            }

            throw new InvalidOperationException("Unreachable DIL PUSH parser state.");
        }

        private static void ParseJump(
            string moduleName,
            int lineNumber,
            InstructionId instructionId,
            string mnemonic,
            string[] tokens,
            Dictionary<string, long> symbols,
            List<Instruction> instructions,
            List<PendingJump> pendingJumps)
        {
            RequireTokenCount(moduleName, lineNumber, mnemonic, tokens, 2);
            string target = tokens[1];

            if (target.StartsWith("$", StringComparison.Ordinal) || IsSignedIntegerToken(target))
            {
                int targetIndex = ResolveInt32(moduleName, lineNumber, target, symbols);
                instructions.Add(CreateJump(instructionId, targetIndex));
                return;
            }

            if (!IsValidIdentifier(target))
                Throw(moduleName, lineNumber, $"Invalid jump label '{target}'.");

            int instructionIndex = instructions.Count;
            instructions.Add(CreateJump(instructionId, 0));
            pendingJumps.Add(new PendingJump(instructionId, target, instructionIndex, lineNumber));
        }

        private static void ResolveJumps(
            string moduleName,
            FunctionDefinition function,
            List<Instruction> instructions,
            Dictionary<string, int> labels,
            List<PendingJump> pendingJumps)
        {
            foreach (PendingJump pending in pendingJumps)
            {
                if (!labels.TryGetValue(pending.Label, out int target))
                {
                    Throw(
                        moduleName,
                        pending.SourceLine,
                        $"Unknown label '{pending.Label}' in function '{function.QualifiedName}'."
                    );
                }

                if ((uint)target >= (uint)instructions.Count)
                {
                    Throw(
                        moduleName,
                        pending.SourceLine,
                        $"Label '{pending.Label}' points past the end of function '{function.QualifiedName}'."
                    );
                }

                instructions[pending.InstructionIndex] = CreateJump(pending.Id, target);
            }

            for (int i = 0; i < instructions.Count; i++)
            {
                Instruction instruction = instructions[i];
                if (instruction.Id != InstructionId.Jump &&
                    instruction.Id != InstructionId.JumpIfTrue &&
                    instruction.Id != InstructionId.JumpIfFalse)
                {
                    continue;
                }

                if ((uint)instruction.OperandA >= (uint)instructions.Count)
                {
                    Throw(
                        moduleName,
                        function.DeclarationLine,
                        $"Jump target {instruction.OperandA} is outside function '{function.QualifiedName}'."
                    );
                }
            }
        }

        private static Instruction CreateJump(InstructionId id, int target)
        {
            return id switch
            {
                InstructionId.Jump => Instruction.Jump(target),
                InstructionId.JumpIfTrue => Instruction.JumpIfTrue(target),
                InstructionId.JumpIfFalse => Instruction.JumpIfFalse(target),
                _ => throw new ArgumentOutOfRangeException(nameof(id))
            };
        }

        private static int ParseIntOperand(
            string moduleName,
            int lineNumber,
            string mnemonic,
            string[] tokens,
            int operandIndex,
            Dictionary<string, long> symbols)
        {
            RequireTokenCount(moduleName, lineNumber, mnemonic, tokens, operandIndex + 1);
            return ResolveInt32(moduleName, lineNumber, tokens[operandIndex], symbols);
        }

        private static int ResolveInt32(
            string moduleName,
            int lineNumber,
            string token,
            Dictionary<string, long> symbols)
        {
            long value = ResolveInt64(moduleName, lineNumber, token, symbols);
            if (value < int.MinValue || value > int.MaxValue)
                Throw(moduleName, lineNumber, $"Value '{token}' is outside the Int32 range required by this instruction.");

            return (int)value;
        }

        private static long ResolveInt64(
            string moduleName,
            int lineNumber,
            string token,
            Dictionary<string, long> symbols)
        {
            string resolved = ResolveSymbolText(moduleName, lineNumber, token, symbols);

            if (!long.TryParse(
                    resolved,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out long value))
            {
                Throw(moduleName, lineNumber, $"'{token}' is not a valid Int64 value.");
            }

            return value;
        }

        private static string ResolveSymbolText(
            string moduleName,
            int lineNumber,
            string token,
            Dictionary<string, long> symbols)
        {
            if (!token.StartsWith("$", StringComparison.Ordinal))
                return token;

            string symbolName = token.Substring(1);
            if (!IsValidQualifiedName(symbolName))
                Throw(moduleName, lineNumber, $"Invalid parse symbol '{token}'.");

            if (!symbols.TryGetValue(symbolName, out long value))
                Throw(moduleName, lineNumber, $"Undefined parse symbol '{token}'.");

            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static long ParseDecimalRaw(string moduleName, int lineNumber, string token)
        {
            if (string.IsNullOrEmpty(token))
                Throw(moduleName, lineNumber, "Decimal literal cannot be empty.");

            int index = 0;
            bool negative = false;

            if (token[0] == '+' || token[0] == '-')
            {
                negative = token[0] == '-';
                index++;
            }

            if (index >= token.Length)
                Throw(moduleName, lineNumber, $"Invalid decimal literal '{token}'.");

            int decimalPoint = token.IndexOf('.', index);
            string integerPart;
            string fractionalPart;

            if (decimalPoint < 0)
            {
                integerPart = token.Substring(index);
                fractionalPart = string.Empty;
            }
            else
            {
                if (token.IndexOf('.', decimalPoint + 1) >= 0)
                    Throw(moduleName, lineNumber, $"Invalid decimal literal '{token}'.");

                integerPart = token.Substring(index, decimalPoint - index);
                fractionalPart = token.Substring(decimalPoint + 1);
            }

            if (integerPart.Length == 0 || !ContainsOnlyDigits(integerPart))
                Throw(moduleName, lineNumber, $"Invalid decimal literal '{token}'.");

            if (fractionalPart.Length > 0 && !ContainsOnlyDigits(fractionalPart))
                Throw(moduleName, lineNumber, $"Invalid decimal literal '{token}'.");

            int fractionalDigits = GetDecimalScaleDigits();
            string truncatedFraction = fractionalPart.Length > fractionalDigits
                ? fractionalPart.Substring(0, fractionalDigits)
                : fractionalPart;

            string paddedFraction = truncatedFraction.PadRight(fractionalDigits, '0');

            BigInteger magnitude = BigInteger.Parse(integerPart, CultureInfo.InvariantCulture) * DTDecimal.Scale;
            if (paddedFraction.Length > 0)
                magnitude += BigInteger.Parse(paddedFraction, CultureInfo.InvariantCulture);

            BigInteger signed = negative ? -magnitude : magnitude;
            if (signed < long.MinValue || signed > long.MaxValue)
                Throw(moduleName, lineNumber, $"Decimal literal '{token}' overflows the Directiva decimal range.");

            return (long)signed;
        }

        private static int GetDecimalScaleDigits()
        {
            long scale = DTDecimal.Scale;
            int digits = 0;

            while (scale > 1 && scale % 10 == 0)
            {
                digits++;
                scale /= 10;
            }

            if (scale != 1)
                throw new InvalidOperationException("DTDecimal.Scale must be a power of ten for DIL decimal parsing.");

            return digits;
        }

        private static void ValidateLocalIndex(
            string moduleName,
            int lineNumber,
            FunctionDefinition function,
            int localIndex)
        {
            if ((uint)localIndex >= (uint)function.LocalCount)
            {
                Throw(
                    moduleName,
                    lineNumber,
                    $"Local slot {localIndex} is outside the {function.LocalCount} local slots of '{function.QualifiedName}'."
                );
            }
        }

        private static void ValidateGlobalIndex(
            string moduleName,
            int lineNumber,
            int globalCount,
            int globalIndex)
        {
            if ((uint)globalIndex >= (uint)globalCount)
                Throw(moduleName, lineNumber, $"Global slot {globalIndex} is outside the program's {globalCount} global slots.");
        }

        private static void ValidateFunctionHeader(
            string moduleName,
            int lineNumber,
            FunctionDefinition function)
        {
            if (function.ParameterCount < 0)
                Throw(moduleName, lineNumber, $"Function '{function.Name}' is missing # PARAMS.");

            if (function.LocalCount < 0)
                Throw(moduleName, lineNumber, $"Function '{function.Name}' is missing # LOCALS.");

            if (function.LocalCount < function.ParameterCount)
            {
                Throw(
                    moduleName,
                    lineNumber,
                    $"Function '{function.Name}' declares {function.ParameterCount} parameters but only {function.LocalCount} local slots."
                );
            }
        }

        private static int ParseNonNegativeInt(
            string moduleName,
            int lineNumber,
            string annotation,
            string? value)
        {
            RequireAnnotationValue(moduleName, lineNumber, annotation, value);

            if (!int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int result))
            {
                Throw(moduleName, lineNumber, $"# {annotation} requires a non-negative Int32 value.");
            }

            return result;
        }

        private static void ValidateNumericSymbolAnnotation(
            string moduleName,
            int lineNumber,
            string symbolName,
            string? value)
        {
            ParseSymbolValue(moduleName, lineNumber, symbolName, value);
        }

        private static long ParseSymbolValue(
            string moduleName,
            int lineNumber,
            string symbolName,
            string? value)
        {
            if (!IsValidQualifiedName(symbolName))
                Throw(moduleName, lineNumber, $"Invalid parse symbol name '{symbolName}'.");

            RequireAnnotationValue(moduleName, lineNumber, symbolName, value);

            if (!long.TryParse(
                    value,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out long result))
            {
                Throw(moduleName, lineNumber, $"Parse symbol '{symbolName}' must contain exactly one integer number.");
            }

            return result;
        }

        private static bool TryParseAnnotation(
            string line,
            out string annotation,
            out string? value)
        {
            annotation = string.Empty;
            value = null;

            if (!line.StartsWith("#", StringComparison.Ordinal))
                return false;

            string content = line.Substring(1).Trim();
            if (content.Length == 0)
                return true;

            int colon = content.IndexOf(':');
            if (colon < 0)
            {
                annotation = content.Trim();
                return true;
            }

            annotation = content.Substring(0, colon).Trim();
            value = content.Substring(colon + 1).Trim();
            return true;
        }

        private static bool TryParseLabel(string line, out string? label)
        {
            label = null;
            if (!line.EndsWith(":", StringComparison.Ordinal))
                return false;

            string candidate = line.Substring(0, line.Length - 1).Trim();
            if (!IsValidIdentifier(candidate))
                return false;

            label = candidate;
            return true;
        }

        private static string[] SplitTokens(string line)
        {
            return line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries
            );
        }

        private static SourceLine[] SplitLines(string sourceText)
        {
            string normalized = sourceText
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            string[] lines = normalized.Split('\n');
            var result = new SourceLine[lines.Length];

            for (int i = 0; i < lines.Length; i++)
                result[i] = new SourceLine(i + 1, lines[i]);

            return result;
        }

        private static string PrepareLine(string line)
        {
            int commentIndex = line.IndexOf("//", StringComparison.Ordinal);
            if (commentIndex >= 0)
                line = line.Substring(0, commentIndex);

            return line.Trim();
        }

        private static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            char first = value[0];
            if (!(first == '_' || char.IsLetter(first)))
                return false;

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!(c == '_' || char.IsLetterOrDigit(c)))
                    return false;
            }

            return true;
        }

        private static bool IsSignedIntegerToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            int start = token[0] == '+' || token[0] == '-' ? 1 : 0;
            if (start == token.Length)
                return false;

            for (int i = start; i < token.Length; i++)
            {
                if (!char.IsDigit(token[i]))
                    return false;
            }

            return true;
        }

        private static bool ContainsOnlyDigits(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                    return false;
            }

            return true;
        }

        private static void RequireAnnotationValue(
            string moduleName,
            int lineNumber,
            string annotation,
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                Throw(moduleName, lineNumber, $"# {annotation} requires a value after ':'.");
        }

        private static void RequireTokenCount(
            string moduleName,
            int lineNumber,
            string instruction,
            string[] tokens,
            int expected)
        {
            if (tokens.Length != expected)
            {
                Throw(
                    moduleName,
                    lineNumber,
                    $"{instruction} expects {expected - 1} operand(s), but received {tokens.Length - 1}."
                );
            }
        }

        private static void Throw(string moduleName, int lineNumber, string message)
        {
            throw new DILParseException(moduleName, lineNumber, message);
        }
    }
}
