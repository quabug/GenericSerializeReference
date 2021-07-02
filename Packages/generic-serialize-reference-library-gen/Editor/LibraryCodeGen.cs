using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace GenericSerializeReference.Library
{
    [InitializeOnLoad]
    public static class LibraryCodeGen
    {
        public static string GetCurrentFilePath([System.Runtime.CompilerServices.CallerFilePath] string fileName = null)
        {
            return fileName;
        }

        static LibraryCodeGen()
        {
        //     CompilationPipeline.compilationFinished -= OnCompilationFinished;
        //     CompilationPipeline.compilationFinished += OnCompilationFinished;
        //
        //     CompilationPipeline.assemblyCompilationFinished -= Log;
        //     CompilationPipeline.assemblyCompilationFinished += Log;
        // }
        //
        // static void OnCompilationFinished(object _)
        // {
            Debug.Log("finished");
            try
            {
                EditorApplication.LockReloadAssemblies();

                const string assemblyName = "";

                var assemblyPath = $"{Path.GetDirectoryName(GetCurrentFilePath())}/../GenericSerializeReference.Library.CodeGen.dll";
                var resolver = new DefaultAssemblyResolver();
                resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(LibraryCodeGen).Assembly.Location));
                var readerParameters = new ReaderParameters {AssemblyResolver = resolver, ReadWrite = true, ThrowIfSymbolsAreNotMatching = false};
                using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
                var properties = InterfaceOnlyProperties().ToArray();
                var types = properties.Select(t => t.property.PropertyType.GetGenericTypeDefinition())
                        .SelectMany(genericType => TypeCache.GetTypesDerivedFrom(genericType))
                        .Select(type => assembly.MainModule.ImportReference(type).Resolve())
                    ;
                var typeTree = new TypeTree(types);
                // var field = new FieldDefinition("test", FieldAttributes.Private, assembly.MainModule.ImportReference(typeof(string)));
                // codeGenType.Fields.Add(field);

                foreach (var baseType in properties.Select(t =>
                    assembly.MainModule.ImportReference(t.property.PropertyType)))
                {
                    var wrapper = new TypeDefinition(
                        ""
                        , $"<GenericSerializeReference>__{baseType.FullName.Replace('.', '_')}"
                        , TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract | TypeAttributes.NotPublic | TypeAttributes.BeforeFieldInit
                    );
                    wrapper.BaseType = assembly.MainModule.ImportReference(typeof(object));

                    assembly.MainModule.Types.Add(wrapper);
                    // var derived = typeTree.GetOrCreateAllDerivedReference(baseType);
                    // var genericArguments = derived.IsGenericInstance
                    //     ? ((GenericInstanceType) derived).GenericArguments
                    //     : (IEnumerable<TypeReference>)Array.Empty<TypeReference>()
                    // ;
                }

                assembly.Write();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ex);
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
            }

            static IEnumerable<(PropertyInfo property, GenericSerializeReferenceAttribute attribute)> InterfaceOnlyProperties()
            {
                return from asm in AppDomain.CurrentDomain.GetAssemblies()
                    from type in IgnoreException(asm.GetTypes, Array.Empty<Type>())
                    from propertyInfo in IgnoreException(
                        () => type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        , Array.Empty<PropertyInfo>())
                    from attribute in IgnoreException(
                        propertyInfo.GetCustomAttributes<GenericSerializeReferenceAttribute>
                        , Array.Empty<GenericSerializeReferenceAttribute>())
                    where attribute.GenerateMode == GenericSerializeReferenceAttribute.Mode.InterfaceOnly
                    where propertyInfo.PropertyType.IsGenericType
                    select (propertyInfo, attribute);
            }
        }

        static T IgnoreException<T>(this Func<T> func, T @default = default)
        {
            try
            {
                return func();
            }
            catch
            {
                return @default;
            }
        }
    }
}