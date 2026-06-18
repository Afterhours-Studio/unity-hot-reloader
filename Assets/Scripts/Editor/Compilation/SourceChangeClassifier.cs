using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityReloader.Runtime;

namespace UnityReloader.Editor.Compilation
{
    // Classifies source-level changes BEFORE compilation using Roslyn SyntaxTree only (no semantic model).
    // Comparison is made against the currently loaded assembly via reflection.
    // Output drives pre-compile logging and the early-exit optimisation for structural changes.
    internal static class SourceChangeClassifier
    {
        internal static HotReloadPlan Classify(
            IReadOnlyList<string> filePaths,
            IReadOnlyList<string> preprocessorSymbols)
        {
            var files = new List<string>();
            var classifications = new List<ChangeClassification>();
            var reasons = new List<string>();
            var requiresFullRecompile = false;

            IEnumerable<string> symbols = preprocessorSymbols ?? Array.Empty<string>();
            var parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(symbols);
            var typeCache = ProjectTypeCache.AllTypesInNonDynamicGeneratedAssemblies;

            foreach (var filePath in filePaths)
            {
                if (!File.Exists(filePath))
                    continue;

                string source;
                try { source = File.ReadAllText(filePath); }
                catch { continue; }

                SyntaxTree tree;
                try { tree = CSharpSyntaxTree.ParseText(source, parseOptions); }
                catch { continue; }

                var root = tree.GetRoot();
                var addedAnyForFile = false;

                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (IsCompilerGeneratedNode(typeDecl))
                        continue;

                    var fullTypeName = BuildFullTypeName(typeDecl);
                    if (!typeCache.TryGetValue(fullTypeName, out var existingType))
                    {
                        files.Add(filePath);
                        classifications.Add(ChangeClassification.NewType);
                        reasons.Add($"New type '{fullTypeName}' in '{Path.GetFileName(filePath)}'");
                        addedAnyForFile = true;
                        continue;
                    }

                    if (HasFieldOrPropertyCountChange(typeDecl, existingType))
                    {
                        files.Add(filePath);
                        classifications.Add(ChangeClassification.FieldSignature);
                        reasons.Add($"Field or property change in '{fullTypeName}' - requires full recompile");
                        requiresFullRecompile = true;
                        addedAnyForFile = true;
                    }

                    var existingMethodNames = BuildExistingMethodNameSet(existingType);

                    foreach (var methodDecl in typeDecl.Members.OfType<MethodDeclarationSyntax>())
                    {
                        var methodName = methodDecl.Identifier.Text;
                        var isGeneric = methodDecl.TypeParameterList?.Parameters.Count > 0;
                        var isExisting = existingMethodNames.Contains(methodName);

                        ChangeClassification cls;
                        string reason;
                        var needsFullRecompile = false;

                        if (isGeneric)
                        {
                            cls = ChangeClassification.GenericMethod;
                            reason = $"Generic method '{methodName}' in '{fullTypeName}' - requires full recompile";
                            needsFullRecompile = true;
                        }
                        else if (!isExisting)
                        {
                            if (IsEffectivelyPrivate(methodDecl))
                            {
                                cls = ChangeClassification.NewPrivateMethod;
                                reason = $"New private method '{methodName}' in '{fullTypeName}' - will hot reload";
                            }
                            else
                            {
                                cls = ChangeClassification.NewPublicMethod;
                                reason = $"New public/protected/internal method '{methodName}' in '{fullTypeName}' - requires full recompile";
                                needsFullRecompile = true;
                            }
                        }
                        else
                        {
                            cls = ChangeClassification.MethodBody;
                            reason = $"Method body change '{methodName}' in '{fullTypeName}' - will hot reload";
                        }

                        files.Add(filePath);
                        classifications.Add(cls);
                        reasons.Add(reason);
                        if (needsFullRecompile) requiresFullRecompile = true;
                        addedAnyForFile = true;
                    }
                }

                if (!addedAnyForFile)
                {
                    files.Add(filePath);
                    classifications.Add(ChangeClassification.Unknown);
                    reasons.Add($"Could not classify changes in '{Path.GetFileName(filePath)}'");
                }
            }

            return new HotReloadPlan(
                files.ToArray(),
                classifications.ToArray(),
                requiresFullRecompile,
                reasons.ToArray());
        }

        private static HashSet<string> BuildExistingMethodNameSet(Type type)
        {
            return new HashSet<string>(
                type.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Select(m => m.Name),
                StringComparer.Ordinal);
        }

        private static bool HasFieldOrPropertyCountChange(TypeDeclarationSyntax typeDecl, Type existingType)
        {
            int syntaxFieldCount = typeDecl.Members
                .OfType<FieldDeclarationSyntax>()
                .SelectMany(f => f.Declaration.Variables)
                .Count();

            int syntaxPropertyCount = typeDecl.Members
                .OfType<PropertyDeclarationSyntax>()
                .Count();

            int loadedFieldCount = existingType
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Count(f => !f.IsDefined(typeof(CompilerGeneratedAttribute), false));

            int loadedPropertyCount = existingType
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Length;

            return syntaxFieldCount != loadedFieldCount
                || syntaxPropertyCount != loadedPropertyCount;
        }

        private static string BuildFullTypeName(TypeDeclarationSyntax typeDecl)
        {
            var name = typeDecl.Identifier.Text;

            if (typeDecl.TypeParameterList?.Parameters.Count > 0)
                name += $"`{typeDecl.TypeParameterList.Parameters.Count}";

            var parent = typeDecl.Parent;
            while (parent is TypeDeclarationSyntax parentType)
            {
                var parentName = parentType.Identifier.Text;
                if (parentType.TypeParameterList?.Parameters.Count > 0)
                    parentName += $"`{parentType.TypeParameterList.Parameters.Count}";
                name = $"{parentName}+{name}";
                parent = parentType.Parent;
            }

            var ns = GetEnclosingNamespace(typeDecl);
            return ns != null ? $"{ns}.{name}" : name;
        }

        private static string GetEnclosingNamespace(SyntaxNode node)
        {
            foreach (var ancestor in node.Ancestors())
            {
                if (ancestor is NamespaceDeclarationSyntax ns)
                    return ns.Name.ToString();
                if (ancestor is FileScopedNamespaceDeclarationSyntax fns)
                    return fns.Name.ToString();
            }
            return null;
        }

        private static bool IsCompilerGeneratedNode(TypeDeclarationSyntax typeDecl)
        {
            return typeDecl.Identifier.Text.Contains("<")
                || typeDecl.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(a => a.Name.ToString().Contains("CompilerGenerated"));
        }

        private static bool IsEffectivelyPrivate(MethodDeclarationSyntax methodDecl)
        {
            var modifiers = methodDecl.Modifiers;
            if (modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)))
                return true;
            return !modifiers.Any(m =>
                m.IsKind(SyntaxKind.PublicKeyword) ||
                m.IsKind(SyntaxKind.ProtectedKeyword) ||
                m.IsKind(SyntaxKind.InternalKeyword));
        }
    }
}
