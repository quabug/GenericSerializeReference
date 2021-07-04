using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using UnityEditor;
using UnityEditor.Compilation;
using Debug = UnityEngine.Debug;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace GenericSerializeReference.Library
{
    [InitializeOnLoad]
    public static class LibraryCodeGen
    {
        internal const string AssemblyName = "GenericSerializeReference.Library";
        private const string _assemblyFile = AssemblyName + ".dll";

        private static HashSet<string> _compiledAssemblies = new HashSet<string>();

        static LibraryCodeGen()
        {
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationStarted += OnCompilationStarted;

            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;

            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;

            static void OnAssemblyCompilationFinished(string assembly, CompilerMessage[] _)
            {
                _compiledAssemblies.Add(Path.GetFileName(assembly));
            }

            static void OnCompilationStarted(object _)
            {
                _compiledAssemblies.Clear();
            }

            static void OnCompilationFinished(object _)
            {
                if (!_compiledAssemblies.Contains(_assemblyFile))
                {
                    var file = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(AssemblyName);
                    AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
                }
            }
        }
    }
}