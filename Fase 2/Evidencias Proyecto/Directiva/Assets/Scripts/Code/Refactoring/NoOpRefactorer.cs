namespace Directiva.CodeUI
{
    public sealed class NoOpRefactorer : IRefactorer
    {
        public RefactorPlan Prepare(DirectivaCode codebase, RefactorRequest request) =>
            RefactorPlan.NoChanges;

        public void Apply(DirectivaCode codebase, RefactorPlan plan)
        {
            // Deliberadamente vacío en v1.
        }
    }
}
