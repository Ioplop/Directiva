using System;
using System.Collections.Generic;

namespace Directiva.CodeUI
{
    public enum RefactorOperationKind
    {
        Rename,
        Move,
        Delete
    }

    public readonly struct RefactorRequest
    {
        public readonly RefactorOperationKind Kind;
        public readonly string SourcePath;
        public readonly string TargetPath;

        public RefactorRequest(RefactorOperationKind kind, string sourcePath, string targetPath)
        {
            Kind = kind;
            SourcePath = sourcePath ?? string.Empty;
            TargetPath = targetPath ?? string.Empty;
        }
    }

    public sealed class RefactorPlan
    {
        public static readonly RefactorPlan NoChanges = new RefactorPlan(
            false,
            Array.Empty<CodeDiagnostic>(),
            Array.Empty<CodeDiagnostic>());

        public bool RequiresRefactor { get; }
        public IReadOnlyList<CodeDiagnostic> NewErrors { get; }
        public IReadOnlyList<CodeDiagnostic> NewWarnings { get; }

        public RefactorPlan(
            bool requiresRefactor,
            IReadOnlyList<CodeDiagnostic> newErrors,
            IReadOnlyList<CodeDiagnostic> newWarnings)
        {
            RequiresRefactor = requiresRefactor;
            NewErrors = newErrors ?? Array.Empty<CodeDiagnostic>();
            NewWarnings = newWarnings ?? Array.Empty<CodeDiagnostic>();
        }
    }

    public interface IRefactorer
    {
        RefactorPlan Prepare(DirectivaCode codebase, RefactorRequest request);

        /// <summary>
        /// Aplica cambios de código previamente preparados.
        /// V1 usa NoOpRefactorer, por lo que no modifica nada.
        /// </summary>
        void Apply(DirectivaCode codebase, RefactorPlan plan);
    }
}
