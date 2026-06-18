namespace UnityReloader.Editor.Compilation.CodeRewriting
{
    public interface ISourceRewriter
    {
        RewriteStepResult Rewrite(string sourceCode, RewriteContext context);
    }
}
