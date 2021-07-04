using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            using var resolver = new PostProcessorAssemblyResolver(compiledAssembly.References);
            using var assembly = compiledAssembly.LoadAssembly(resolver);
            var referenceAssemblies = compiledAssembly.LoadLibraryAssemblies(resolver).ToArray();
            try
            {
                var loggerAttributes = assembly.GetAttributesOf<GenericSerializeReferenceLoggerAttribute>();
                if (loggerAttributes.Any()) logger.LogLevel = (LogLevel)loggerAttributes.First().ConstructorArguments[0].Value;
                logger.Info($"process GenericSerializeReference on {assembly.Name.Name}({string.Join(",", referenceAssemblies.Select(r => r.Name.Name))})");
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
                // assembly.Write();
                var inMemoryAssembly = new InMemoryAssembly(pe.ToArray(), pdb.ToArray());
                return new ILPostProcessResult(inMemoryAssembly, logger.Messages);
            }
            finally
            {
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
                from attribute in property.GetAttributesOf<GenericSerializeReferenceAttribute>()
                select (type, property, attribute)
            )
            {
                if (property.GetMethod == null)
                {
                    logger.Warning($"Cannot process on property {property} without getter");
                    continue;
                }

                if (!property.PropertyType.IsGenericInstance)
                {
                    logger.Warning($"Cannot process on property {property} with non-generic type {property.PropertyType.Name}");
                    continue;
                }

                var wrapperName = $"<{property.Name}>__generic_serialize_reference";
                var wrapper = property.DeclaringType.CreateNestedStaticPrivateClass(wrapperName);
                var serializedFieldInterface = CreateInterface(wrapper);
                CreateDerivedClasses(property, wrapper, serializedFieldInterface);

                logger.Info($"generate nested class with interface {serializedFieldInterface.FullName}");
                var fieldNamePrefix = (string)attribute.ConstructorArguments[0].Value;
                GenerateField(module, property, serializedFieldInterface, fieldNamePrefix);
                modified = true;
            }
            return modified;

            TypeDefinition CreateInterface(TypeDefinition wrapper, string interfaceName = "IBase")
            {
                // .class interface nested public abstract auto ansi
                var interfaceAttributes = TypeAttributes.Class |
                                          TypeAttributes.Interface |
                                          TypeAttributes.NestedPublic |
                                          TypeAttributes.Abstract;
                var baseInterface = new TypeDefinition("", interfaceName, interfaceAttributes);
                wrapper.NestedTypes.Add(baseInterface);
                return baseInterface;
            }

            void CreateDerivedClasses(PropertyDefinition property, TypeDefinition wrapper, TypeDefinition baseInterface)
            {
                logger.Debug($"get derived {property.PropertyType.Module} {property.PropertyType} {property.PropertyType.Resolve()}");
                foreach (var derived in typeTree.GetOrCreateAllDerivedReference(property.PropertyType))
                {
                    var genericArguments = derived.IsGenericInstance
                        ? ((GenericInstanceType) derived).GenericArguments
                        : (IEnumerable<TypeReference>)Array.Empty<TypeReference>()
                    ;

                    if (genericArguments.All(arg => !arg.IsGenericParameter))
                    {
                        var className = derived.Name.Split('`')[0];
                        if (wrapper.NestedTypes.Any(t => t.Name == className))
                            className = derived.Resolve().NameWithOuterClasses();
                        // TODO: should handle if the className is still the same with any of existing type.
                        if (wrapper.NestedTypes.Any(t => t.Name == className))
                            logger.Warning($"Overwrite type with same name {className}");
                        var generated = derived.GenerateDerivedClass(genericArguments, className);
                        logger.Debug($"generate {generated.ToReadableName()}");
                        generated.Interfaces.Add(new InterfaceImplementation(baseInterface));
                        wrapper.NestedTypes.Add(generated);
                    }
                }
            }
        }

        internal static void GenerateField(
            ModuleDefinition module,
            PropertyDefinition property,
            TypeDefinition fieldType,
            string fieldNamePrefix)
        {
            var serializedField = CreateSerializeReferenceField(module, property, fieldType, fieldNamePrefix);
            InjectGetter(property, serializedField);
            InjectSetter(property, serializedField);
        }

        internal static FieldDefinition CreateSerializeReferenceField(
            ModuleDefinition module,
            PropertyDefinition property,
            TypeReference @interface,
            string namePrefix)
        {
            //.field private class GenericSerializeReference.Tests.TestMonoBehavior/__generic_serialize_reference_GenericInterface__/IBase _GenericInterface
            //  .custom instance void [UnityEngine.CoreModule]UnityEngine.SerializeReference::.ctor()
            //    = (01 00 00 00 )
            var serializedField = new FieldDefinition(
                $"{namePrefix}{property.Name}"
                , FieldAttributes.Private
                , @interface
            );
            serializedField.AddCustomAttribute<SerializeReference>(module);
            serializedField.AddCustomAttribute<GenericSerializeReferenceGeneratedFieldAttribute>(module);
            property.DeclaringType.Fields.Add(serializedField);
            return serializedField;
        }

        internal static void InjectGetter(PropertyDefinition property, FieldDefinition serializedField)
        {
            // --------add--------
            // IL_0000: ldarg.0      // this
            // IL_0001: ldfld        class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32> GenericSerializeReference.Tests.A::_Value
            // IL_0006: dup
            // IL_0007: brtrue.s     IL_0010
            // IL_0009: pop
            // --------add--------

            // IL_000a: ldarg.0      // this
            // IL_000b: ldfld        class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32> GenericSerializeReference.Tests.A::k__BackingField
            // IL_0010: ret
            if (property.GetMethod == null) return;
            var instructions = property.GetMethod.Body.Instructions;
            var ret = instructions.Last(i => i.OpCode == OpCodes.Ret);
            instructions.Insert(0, Instruction.Create(OpCodes.Ldarg_0));
            instructions.Insert(1, Instruction.Create(OpCodes.Ldfld, serializedField));
            instructions.Insert(2, Instruction.Create(OpCodes.Dup));
            instructions.Insert(3, Instruction.Create(OpCodes.Brtrue_S, ret));
            instructions.Insert(4, Instruction.Create(OpCodes.Pop));
        }

        internal static void InjectSetter(PropertyDefinition property, FieldDefinition serializedField)
        {
            //IL_0000: ldarg.0      // this
            //IL_0001: ldarg.1      // 'value'
            //IL_0002: stfld        class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32> GenericSerializeReference.Tests.A::'<Value>k__BackingField'
            // before ret
            // -------add-------
            // IL_0008: ldarg.0      // this
            // IL_0009: ldnull
            // IL_000a: stfld        class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32> GenericSerializeReference.Tests.A::_Value
            // -------add-------
            //IL_0007: ret
            if (property.SetMethod == null) return;
            var instructions = property.SetMethod.Body.Instructions;
            var retIndex = instructions.FindLastIndexOf(i => i.OpCode == OpCodes.Ret);
            instructions.Insert(retIndex + 0, Instruction.Create(OpCodes.Ldarg_0));
            instructions.Insert(retIndex + 1, Instruction.Create(OpCodes.Ldnull));
            instructions.Insert(retIndex + 2, Instruction.Create(OpCodes.Stfld, serializedField));
        }

    }
}