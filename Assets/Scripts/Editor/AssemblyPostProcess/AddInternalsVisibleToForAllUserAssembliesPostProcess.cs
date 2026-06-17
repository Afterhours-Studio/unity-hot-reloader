using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityReloader.Editor.AssemblyPostProcess
{
    [InitializeOnLoad]
    public static class AddInternalsVisibleToForAllUserAssembliesPostProcess
    {
        public static readonly DirectoryInfo AdjustedAssemblyRoot;

        private static readonly Assembly CecilAssembly;
        private static readonly Type AssemblyDefinitionType;
        private static readonly Type ReaderParametersType;
        private static readonly Type CustomAttributeType;
        private static readonly Type CustomAttributeArgumentType;
        private static readonly Type DefaultAssemblyResolverType;

        static AddInternalsVisibleToForAllUserAssembliesPostProcess()
        {
            AdjustedAssemblyRoot = new DirectoryInfo(Path.Combine(Application.dataPath, "..", "Temp", "Unity Reloader", "AdjustedDlls"));

            CecilAssembly = typeof(HarmonyLib.Harmony).Assembly;
            AssemblyDefinitionType = CecilAssembly.GetType("Mono.Cecil.AssemblyDefinition");
            ReaderParametersType = CecilAssembly.GetType("Mono.Cecil.ReaderParameters");
            CustomAttributeType = CecilAssembly.GetType("Mono.Cecil.CustomAttribute");
            CustomAttributeArgumentType = CecilAssembly.GetType("Mono.Cecil.CustomAttributeArgument");
            DefaultAssemblyResolverType = CecilAssembly.GetType("Mono.Cecil.DefaultAssemblyResolver");
        }

        // Builds a Cecil resolver that can find every assembly referenced by the recompiled code. Without it, writing
        // the assembly fails (AssemblyResolutionException) when a referenced assembly - e.g. a third-party SDK with no
        // metadata version - lives outside Cecil's default search path.
        private static object CreateAssemblyResolverWithProjectSearchDirectories()
        {
            var resolver = Activator.CreateInstance(DefaultAssemblyResolverType);
            var addSearchDirectory = DefaultAssemblyResolverType.GetMethod("AddSearchDirectory", new[] { typeof(string) });

            foreach (var directory in GetLoadedAssemblyDirectories())
                addSearchDirectory.Invoke(resolver, new object[] { directory });

            return resolver;
        }

        private static IEnumerable<string> GetLoadedAssemblyDirectories()
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string location;
                try
                {
                    location = assembly.Location;
                }
                catch
                {
                    continue; //dynamic assemblies throw on Location - skip
                }

                if (string.IsNullOrEmpty(location))
                    continue;

                var directory = Path.GetDirectoryName(location);
                if (!string.IsNullOrEmpty(directory))
                    directories.Add(directory);
            }

            return directories;
        }

        public static string CreateAssemblyWithInternalsContentsVisibleTo(Assembly changedAssembly, string visibleToAssemblyName)
        {
            if (!AdjustedAssemblyRoot.Exists)
                AdjustedAssemblyRoot.Create();

            // var readerParams = new ReaderParameters { ReadWrite = false, AssemblyResolver = resolver };
            var readerParams = Activator.CreateInstance(ReaderParametersType);
            ReaderParametersType.GetProperty("ReadWrite").SetValue(readerParams, false);
            var assemblyResolver = CreateAssemblyResolverWithProjectSearchDirectories();
            ReaderParametersType.GetProperty("AssemblyResolver").SetValue(readerParams, assemblyResolver);

            // var assembly = AssemblyDefinition.ReadAssembly(path, readerParams);
            var readAssemblyMethod = AssemblyDefinitionType.GetMethod("ReadAssembly", new[] { typeof(string), ReaderParametersType });
            var assemblyDef = readAssemblyMethod.Invoke(null, new[] { changedAssembly.Location, readerParams });

            try
            {
                // var mainModule = assembly.MainModule;
                var mainModule = AssemblyDefinitionType.GetProperty("MainModule").GetValue(assemblyDef);
                var moduleDefinitionType = mainModule.GetType();

                // var attributeCtor = mainModule.ImportReference(typeof(InternalsVisibleToAttribute).GetConstructor(...));
                var importRefMethod = moduleDefinitionType.GetMethod("ImportReference", new[] { typeof(MethodBase) });
                var attrCtorInfo = typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute).GetConstructor(new[] { typeof(string) });
                var attributeCtor = importRefMethod.Invoke(mainModule, new object[] { attrCtorInfo });

                // var attribute = new CustomAttribute(attributeCtor);
                var attribute = Activator.CreateInstance(CustomAttributeType, attributeCtor);

                // var typeSystem = mainModule.TypeSystem;
                var typeSystem = moduleDefinitionType.GetProperty("TypeSystem").GetValue(mainModule);
                var stringTypeRef = typeSystem.GetType().GetProperty("String").GetValue(typeSystem);

                // attribute.ConstructorArguments.Add(new CustomAttributeArgument(mainModule.TypeSystem.String, visibleToAssemblyName));
                var ctorArg = Activator.CreateInstance(CustomAttributeArgumentType, stringTypeRef, visibleToAssemblyName);
                var ctorArgs = (IList)CustomAttributeType.GetProperty("ConstructorArguments").GetValue(attribute);
                ctorArgs.GetType().GetMethod("Add").Invoke(ctorArgs, new[] { ctorArg });

                // assembly.CustomAttributes.Add(attribute);
                var customAttrs = (IList)AssemblyDefinitionType.GetProperty("CustomAttributes").GetValue(assemblyDef);
                customAttrs.GetType().GetMethod("Add").Invoke(customAttrs, new[] { attribute });

                // var newAssemblyPath = ...;
                var assemblyName = AssemblyDefinitionType.GetProperty("Name").GetValue(assemblyDef);
                var assemblyNameStr = (string)assemblyName.GetType().GetProperty("Name").GetValue(assemblyName);
                var newAssemblyPath = new FileInfo(Path.Combine(AdjustedAssemblyRoot.FullName, assemblyNameStr) + ".dll").FullName;

                // assembly.Write(newAssemblyPath);
                AssemblyDefinitionType.GetMethod("Write", new[] { typeof(string) }).Invoke(assemblyDef, new object[] { newAssemblyPath });

                return newAssemblyPath;
            }
            finally
            {
                // AssemblyDefinition and the resolver are IDisposable
                if (assemblyDef is IDisposable disposable)
                    disposable.Dispose();
                if (assemblyResolver is IDisposable disposableResolver)
                    disposableResolver.Dispose();
            }
        }
    }
}