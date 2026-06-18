using System.Reflection;
using UnityReloader.Runtime;

namespace UnityReloader.Editor.Compilation
{
    public class PatchApplicator
    {
        public PatchResult Apply(
            Assembly assembly,
            IAssemblyChangesLoader loader,
            AssemblyChangesLoaderEditorOptionsNeededInBuild options)
        {
            return loader.DynamicallyUpdateMethodsForCreatedAssembly(assembly, options);
        }
    }
}
