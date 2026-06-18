#if UNITY_EDITOR || LiveScriptReload_Enabled
using System;

namespace UnityReloader.Runtime
{
    public enum ChangeClassification
    {
        MethodBody,
        NewPrivateMethod,
        NewPublicMethod,
        FieldSignature,
        NewType,
        GenericMethod,
        Unknown
    }

    /// <summary>
    /// Describes the intent of a hot reload batch before compilation runs.
    /// The capability-layer pass (Prompt 4) will populate this fully;
    /// for now it is defined so downstream consumers can reference the type.
    /// </summary>
    public class HotReloadPlan
    {
        public string[] Files { get; }
        public ChangeClassification[] Classifications { get; }
        public bool RequiresFullRecompile { get; }
        public string[] Reasons { get; }

        public HotReloadPlan(string[] files, ChangeClassification[] classifications, bool requiresFullRecompile, string[] reasons)
        {
            Files = files ?? Array.Empty<string>();
            Classifications = classifications ?? Array.Empty<ChangeClassification>();
            RequiresFullRecompile = requiresFullRecompile;
            Reasons = reasons ?? Array.Empty<string>();
        }
    }

    public class PatchResult
    {
        public bool Success { get; }
        public ChangeClassification[] Applied { get; }
        public ChangeClassification[] FallenBackToRecompile { get; }
        public string ErrorMessage { get; }

        public PatchResult(bool success, ChangeClassification[] applied, ChangeClassification[] fallenBackToRecompile, string errorMessage = null)
        {
            Success = success;
            Applied = applied ?? Array.Empty<ChangeClassification>();
            FallenBackToRecompile = fallenBackToRecompile ?? Array.Empty<ChangeClassification>();
            ErrorMessage = errorMessage;
        }

        public static PatchResult Succeeded(ChangeClassification[] applied, ChangeClassification[] fallenBack = null)
            => new PatchResult(true, applied, fallenBack ?? Array.Empty<ChangeClassification>());

        public static PatchResult Failed(string errorMessage)
            => new PatchResult(false, Array.Empty<ChangeClassification>(), Array.Empty<ChangeClassification>(), errorMessage);
    }
}
#endif
