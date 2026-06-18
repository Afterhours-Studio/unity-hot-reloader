namespace UnityReloader.Editor.Compilation.CodeRewriting
{
    public class RewriteStepResult
    {
        public RewriteStepStatus Status { get; }
        public string TransformedSource { get; }
        public string DiagnosticMessage { get; }
        public string StepName { get; }

        public RewriteStepResult(RewriteStepStatus status, string transformedSource, string diagnosticMessage, string stepName)
        {
            Status = status;
            TransformedSource = transformedSource;
            DiagnosticMessage = diagnosticMessage;
            StepName = stepName;
        }

        public static RewriteStepResult Changed(string source, string stepName) =>
            new RewriteStepResult(RewriteStepStatus.Changed, source, null, stepName);

        public static RewriteStepResult NoOp(string source, string stepName) =>
            new RewriteStepResult(RewriteStepStatus.NoOp, source, null, stepName);

        public static RewriteStepResult Unsupported(string source, string message, string stepName) =>
            new RewriteStepResult(RewriteStepStatus.Unsupported, source, message, stepName);
    }
}
