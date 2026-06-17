using System.Collections;
using UnityReloader.Tests.Runtime.Integration.CodePatterns;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityReloader.Tests.Editor.Integration.CodePatterns
{
    public class AccessingNewFieldsAddedAtRuntimeSameNameAsStaticClassFieldTest : CompileWithRedirectTestBase
    {
        private static readonly string RelativeFilePath = @$"Runtime\Integration\CodePatterns\{nameof(AccessingNewFieldsAddedAtRuntimeSameNameAsStaticClassField)}.cs";

        [UnityTest]
        public IEnumerator TestStaticFieldAccess_StandardCompilation_CorrectRewriteCreated()
        {
            TestCompileAndDetour(ResolveFullTestFilePath(RelativeFilePath));

            yield return null;
        }
    }
}