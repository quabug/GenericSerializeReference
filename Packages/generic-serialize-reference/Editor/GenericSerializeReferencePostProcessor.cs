using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using UnityEngine;

namespace GenericSerializeReference
{
    public class GenericSerializeReferencePostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance()
        {
            return this;
        }

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            var thisAssemblyName = GetType().Assembly.GetName().Name;
            var runtimeAssemblyName = typeof(GenericSerializeReferenceAttribute).Assembly.GetName().Name;
            return compiledAssembly.Name != thisAssemblyName &&
                   compiledAssembly.References.Any(f => Path.GetFileNameWithoutExtension(f) == runtimeAssemblyName);
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var logger = new ILPostProcessorLogger(new List<DiagnosticMessage>());
            var (assembly, referenceAssemblies) = LoadAssemblyDefinition(compiledAssembly, name => name.StartsWith("Library/ScriptAssemblies"));
            logger.Info($"process GenericSerializeReference on {assembly.Name.Name}({string.Join(",", referenceAssemblies.Select(r => r.Name.Name))})");
            try
            {
                var allTypes = referenceAssemblies.Append(assembly)
                    .Where(asm => !asm.Name.Name.StartsWith("Unity.")
                                  && !asm.Name.Name.StartsWith("UnityEditor.")
                                  && !asm.Name.Name.StartsWith("UnityEngine.")
                              )
                    .SelectMany(asm => asm.MainModule.GetAllTypes())
                ;
                logger.Debug($"all types: {string.Join(", ", allTypes.Select(t => t.Name))}");
                var typeTree = new TypeTree(allTypes);
                logger.Debug($"tree: {typeTree}");
                var modified = Process(assembly.MainModule, typeTree, logger);
                if (!modified) return new ILPostProcessResult(null, logger.Messages);

                var pe = new MemoryStream();
                var pdb = new MemoryStream();
                var writerParameters = new WriterParameters
                {
                    SymbolWriterProvider = new PortablePdbWriterProvider(), SymbolStream = pdb, WriteSymbols = true
                };
                assembly.Write(pe, writerParameters);
                return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), logger.Messages);
            }
            finally
            {
                assembly.Dispose();
                foreach (var reference in referenceAssemblies) reference.Dispose();
            }
        }

        private bool Process(ModuleDefinition module, TypeTree typeTree, ILPostProcessorLogger logger)
        {
            var modified = false;
            foreach (var (type, property, attribute) in
                from type in module.GetAllTypes()
                where type.IsClass && !type.IsAbstract
                from property in type.Properties.ToArray() // able to change `Properties` during looping
                where property.PropertyType.IsGenericInstance
                from attribute in GetAttributesOf<GenericSerializeReferenceAttribute>(property)
                select (type, property, attribute)
            )
            {
                var serializedFieldInterface = CreateWrapperClass(property);
                logger.Debug($"generate nested class with interface {serializedFieldInterface.FullName}");
                //.field private class GenericSerializeReference.Tests.TestMonoBehavior/__generic_serialize_reference_GenericInterface__/IBase _GenericInterface
                //  .custom instance void [UnityEngine.CoreModule]UnityEngine.SerializeReference::.ctor()
                //    = (01 00 00 00 )
                var serializedField = new FieldDefinition($"_{property.Name}", FieldAttributes.Private,
                    serializedFieldInterface);
                serializedField.CustomAttributes.Add(CreateCustomAttribute<SerializeReference>());
                property.DeclaringType.Fields.Add(serializedField);
                logger.Debug($"add field into {property.DeclaringType.FullName}");
                modified = true;
            }
            return modified;

            CustomAttribute CreateCustomAttribute<T>(params Type[] constructorTypes) where T : Attribute
            {
                return new CustomAttribute(module.ImportReference(typeof(T).GetConstructor(constructorTypes)));
            }

            TypeDefinition/*interface*/ CreateWrapperClass(PropertyDefinition property)
            {
                // .class nested public abstract sealed auto ansi beforefieldinit
                //   <$PropertyName>__generic_serialize_reference
                //     extends [mscorlib]System.Object
                var typeAttributes = TypeAttributes.Class |
                                     TypeAttributes.Sealed |
                                     TypeAttributes.Abstract |
                                     TypeAttributes.NestedPrivate |
                                     TypeAttributes.BeforeFieldInit;
                var wrapper = new TypeDefinition("", $"<{property.Name}>__generic_serialize_reference", typeAttributes);
                wrapper.BaseType = property.Module.ImportReference(typeof(System.Object));
                property.DeclaringType.NestedTypes.Add(wrapper);

                // .class interface nested public abstract auto ansi
                var interfaceAttributes = TypeAttributes.Class |
                                          TypeAttributes.Interface |
                                          TypeAttributes.NestedPublic |
                                          TypeAttributes.Abstract;
                var baseInterface = new TypeDefinition("", "IBase", interfaceAttributes);
                wrapper.NestedTypes.Add(baseInterface);

                logger.Debug($"get derived {property.PropertyType.Module} {property.PropertyType} {property.PropertyType.Resolve()}");
                // var derivedTypes = typeTree.GetDerived(property.PropertyType.Resolve());
                // logger.Debug($"create {string.Join(",", derivedTypes)}");

                return baseInterface;
            }
        }

        private static (AssemblyDefinition compiled, AssemblyDefinition[] references)
            LoadAssemblyDefinition(ICompiledAssembly compiledAssembly, Func<string, bool> referencePredicate)
        {
            var resolver = new PostProcessorAssemblyResolver(compiledAssembly.References);
            var symbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData.ToArray());
            var readerParameters = new ReaderParameters
            {
                SymbolStream = symbolStream,
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                AssemblyResolver = resolver,
                ReflectionImporterProvider = new PostProcessorReflectionImporterProvider(),
                ReadingMode = ReadingMode.Immediate,
            };
            var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData.ToArray());
            var assembly = AssemblyDefinition.ReadAssembly(peStream, readerParameters);
            var referenceAssemblies = compiledAssembly.References.Where(referencePredicate).Select(resolver.Resolve).ToArray();
            return (assembly, referenceAssemblies);
        }

        private IEnumerable<CustomAttribute> GetAttributesOf<T>([NotNull] ICustomAttributeProvider provider) where T : Attribute
        {
            return provider.HasCustomAttributes ?
                provider.CustomAttributes.Where(IsAttributeOf) :
                Enumerable.Empty<CustomAttribute>();

            static bool IsAttributeOf(CustomAttribute attribute) => attribute.AttributeType.FullName == typeof(T).FullName;
        }
    }
}