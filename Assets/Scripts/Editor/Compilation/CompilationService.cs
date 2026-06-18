using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ImmersiveVRTools.Editor.Common.Utilities;
using UnityReloader.Runtime;

namespace UnityReloader.Editor.Compilation
{
    public class CompilationServiceResult
    {
        public bool RequiresFullRecompile { get; }
        public string PlanLog { get; }
        public CompileResult CompilerResult { get; }

        public CompilationServiceResult(bool requiresFullRecompile, string planLog, CompileResult compilerResult)
        {
            RequiresFullRecompile = requiresFullRecompile;
            PlanLog = planLog;
            CompilerResult = compilerResult;
        }
    }

    public class CompilationService
    {
        public CompilationServiceResult Compile(List<string> sourceFiles, UnityMainThreadDispatcher mainThreadDispatcher)
        {
            var hotReloadPlan = SourceChangeClassifier.Classify(
                sourceFiles,
                DynamicCompilationBase.ActiveScriptCompilationDefines);

            var planLog = BuildPlanLog(hotReloadPlan);

            if (hotReloadPlan.RequiresFullRecompile)
            {
                return new CompilationServiceResult(true, planLog, null);
            }

            var compileResult = DynamicAssemblyCompiler.Compile(sourceFiles, mainThreadDispatcher);
            return new CompilationServiceResult(false, planLog, compileResult);
        }

        private static string BuildPlanLog(HotReloadPlan plan)
        {
            var sb = new StringBuilder("Hot reload pre-analysis:");
            var pairs = plan.Files.Zip(plan.Reasons, (f, r) => (file: f, reason: r))
                .GroupBy(x => x.file);
            foreach (var group in pairs)
            {
                sb.AppendLine();
                sb.Append($"  {Path.GetFileName(group.Key)}: ");
                sb.Append(string.Join("; ", group.Select(x => x.reason)));
            }
            if (plan.RequiresFullRecompile)
                sb.AppendLine().Append("  -> structural changes detected - will skip hot compile and request full recompile.");
            return sb.ToString();
        }
    }
}
