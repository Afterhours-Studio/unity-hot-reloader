using System.Collections.Generic;

namespace UnityReloader.Editor.Compilation.CodeRewriting
{
    public class RewriteContext
    {
        public IReadOnlyList<string> PreprocessorSymbols { get; }
        public bool WriteRewriteReasonAsComment { get; }
        public List<string> StrippedUsingDirectives { get; } = new List<string>();

        public RewriteContext(IReadOnlyList<string> preprocessorSymbols, bool writeRewriteReasonAsComment)
        {
            PreprocessorSymbols = preprocessorSymbols;
            WriteRewriteReasonAsComment = writeRewriteReasonAsComment;
        }
    }
}
