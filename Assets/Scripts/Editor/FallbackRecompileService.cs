using ImmersiveVrToolsCommon.Runtime.Logging;
using UnityEditor;
using UnityEditor.Compilation;

namespace UnityReloader.Editor
{
    public class FallbackRecompileService
    {
        private volatile bool _pending;

        // Thread-safe: called from background compile thread on hot-reload failure.
        public void Request()
        {
            _pending = true;
        }

        // Must be called on the main thread (EditorApplication.update).
        public void ProcessIfPending()
        {
            if (!_pending) return;
            _pending = false;

            if ((bool)UnityReloaderPreference.AutoRecompileOnHotReloadFailure.GetEditorPersistedValueOrDefault()
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating)
            {
                LoggerScoped.LogWarning("Unity Reloader: hot reload failed - triggering a full recompile so the change is applied (Unity auto-refresh is off).");
                CompilationPipeline.RequestScriptCompilation();
            }
        }
    }
}
