using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using UnityEngine;

namespace GenericSerializeReference.Editor
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
            logger.Info($"process GenericSerializeReference on {compiledAssembly.Name})");
            using var assemblyDefinition = LoadAssemblyDefinition(compiledAssembly);
            var module = assemblyDefinition.MainModule;

            var modified = false;
            foreach (var (type, field, attribute) in
                from type in assemblyDefinition.MainModule.GetAllTypes()
                where type.IsClass && !type.IsAbstract
                from field in type.Fields.ToArray() // able to change `Fields` during looping
                where field.FieldType.IsGenericInstance
                from attribute in GetAttributesOf<GenericSerializeReferenceAttribute>(field)
                select (type, field, attribute)
            )
            {
                var serializedFieldInterface = CreateWrapperClass(field);
                logger.Debug($"generate nested class with interface {serializedFieldInterface.FullName}");
                //.field private class GenericSerializeReference.Tests.TestMonoBehavior/__generic_serialize_reference_GenericInterface__/IBase _GenericInterface
                //  .custom instance void [UnityEngine.CoreModule]UnityEngine.SerializeReference::.ctor()
                //    = (01 00 00 00 )
                var serializedField = new FieldDefinition($"_{field.Name}", FieldAttributes.Private, serializedFieldInterface);
                serializedField.CustomAttributes.Add(CreateCustomAttribute<SerializeReference>());
                field.DeclaringType.Fields.Add(serializedField);
                logger.Debug($"add field into {field.DeclaringType.FullName}");
                modified = true;
            }

            if (!modified) return new ILPostProcessResult(null, logger.Messages);

            var pe = new MemoryStream();
            var pdb = new MemoryStream();
            var writerParameters = new WriterParameters
            {
                SymbolWriterProvider = new PortablePdbWriterProvider(), SymbolStream = pdb, WriteSymbols = true
            };
            assemblyDefinition.Write(pe, writerParameters);
            return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), logger.Messages);

            CustomAttribute CreateCustomAttribute<T>(params Type[] constructorTypes) where T : Attribute
            {
                return new CustomAttribute(module.ImportReference(typeof(T).GetConstructor(constructorTypes)));
            }
        }


        private static AssemblyDefinition LoadAssemblyDefinition(ICompiledAssembly compiledAssembly)
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
            return AssemblyDefinition.ReadAssembly(peStream, readerParameters);
        }

        private IEnumerable<CustomAttribute> GetAttributesOf<T>([NotNull] ICustomAttributeProvider provider)
            where T : Attribute
        {
            return provider.HasCustomAttributes ?
                provider.CustomAttributes.Where(IsAttributeOf) :
                Enumerable.Empty<CustomAttribute>();

            static bool IsAttributeOf(CustomAttribute attribute) =>
                attribute.AttributeType.FullName == typeof(T).FullName;
        }

        private TypeDefinition/*interface*/ CreateWrapperClass(FieldDefinition field)
        {
            // .class nested public abstract sealed auto ansi beforefieldinit
            //   __generic_serialize_reference_Generic__
            //     extends [mscorlib]System.Object
            var typeAttributes = TypeAttributes.Class |
                                 TypeAttributes.Sealed |
                                 TypeAttributes.Abstract |
                                 TypeAttributes.BeforeFieldInit;
            typeAttributes |= (field.IsPublic ? TypeAttributes.NestedPublic : TypeAttributes.NestedPrivate);
            var wrapper = new TypeDefinition(
                ""
                , $"__generic_serialize_reference_{field.Name}__"
                , typeAttributes
            );
            wrapper.BaseType = field.Module.ImportReference(typeof(System.Object));
            field.DeclaringType.NestedTypes.Add(wrapper);

            // .class interface nested public abstract auto ansi
            //   IBase
            // {
            // } // end of class IBase
            var interfaceAttributes = TypeAttributes.Class
                                      | TypeAttributes.Interface
                                      | TypeAttributes.NestedPublic
                                      | TypeAttributes.Abstract
                                  ;
            var baseInterface = new TypeDefinition("", "IBase", interfaceAttributes);
            wrapper.NestedTypes.Add(baseInterface);
            return baseInterface;
        }
    }
}