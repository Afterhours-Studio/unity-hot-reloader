using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UnityReloader.Editor.Compilation.CodeRewriting
{
    public class SyntaxRewriterAdapter : ISourceRewriter
    {
        private readonly string _stepName;
        private readonly Func<SyntaxNode, RewriteContext, SyntaxNode> _visitFn;

        public SyntaxRewriterAdapter(string stepName, Func<SyntaxNode, RewriteContext, SyntaxNode> visitFn)
        {
            _stepName = stepName;
            _visitFn = visitFn;
        }

        public RewriteStepResult Rewrite(string sourceCode, RewriteContext context)
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode,
                new CSharpParseOptions(preprocessorSymbols: context.PreprocessorSymbols));
            var root = tree.GetRoot();
            var newRoot = _visitFn(root, context);
            var transformed = newRoot.ToFullString();
            var status = transformed != sourceCode ? RewriteStepStatus.Changed : RewriteStepStatus.NoOp;
            return new RewriteStepResult(status, transformed, null, _stepName);
        }
    }
}
