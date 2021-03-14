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
            var loggerAttributes = GetAttributesOf<GenericSerializeReferenceLoggerAttribute>(assembly);
            if (loggerAttributes.Any()) logger.LogLevel = (LogLevel)loggerAttributes.First().ConstructorArguments[0].Value;
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
                if (property.GetMethod == null)
                {
                    logger.Warning($"Cannot process on property {property} without getter");
                    continue;
                }

                var serializedFieldInterface = CreateWrapperClass(property);
                logger.Info($"generate nested class with interface {serializedFieldInterface.FullName}");
                var fieldNamePrefix = (string)attribute.ConstructorArguments[0].Value;
                var serializedField = CreateSerializeReferenceField(property, serializedFieldInterface, fieldNamePrefix);
                InjectGetter(property, serializedField);
                InjectSetter(property, serializedField);
                modified = true;
            }
            return modified;

            void InjectGetter(PropertyDefinition property, FieldDefinition serializedField)
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

            void InjectSetter(PropertyDefinition property, FieldDefinition serializedField)
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

            FieldDefinition CreateSerializeReferenceField(PropertyDefinition property, TypeReference @interface, string namePrefix)
            {
                //.field private class GenericSerializeReference.Tests.TestMonoBehavior/__generic_serialize_reference_GenericInterface__/IBase _GenericInterface
                //  .custom instance void [UnityEngine.CoreModule]UnityEngine.SerializeReference::.ctor()
                //    = (01 00 00 00 )
                var serializedField = new FieldDefinition(
                    $"{namePrefix}{property.Name}"
                    , FieldAttributes.Private
                    , @interface
                );
                serializedField.CustomAttributes.Add(CreateCustomAttribute<SerializeReference>());
                serializedField.CustomAttributes.Add(CreateCustomAttribute<GenericSerializeReferenceGeneratedFieldAttribute>());
                // var backingField = property.DeclaringType.Fields.First(field => field.Name == $"<{property.Name}>k__BackingField");
                // foreach (var customAttribute in backingField.CustomAttributes)
                //     serializedField.CustomAttributes.Add(customAttribute);
                property.DeclaringType.Fields.Add(serializedField);
                logger.Debug($"add field into {property.DeclaringType.FullName}");
                return serializedField;
            }

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
                var propertyTypeDefinition = property.PropertyType.Resolve();
                var derivedTypes = typeTree.GetDirectDerived(propertyTypeDefinition);
                // TODO: should avoid recursive process with side effect?
                //       return an enumerable of type reference with info to generate?
                foreach (var derivedDef in derivedTypes) RecursiveProcess(derivedDef, property.PropertyType);
                return baseInterface;

                void RecursiveProcess(TypeDefinition type, TypeReference baseType)
                {
                    logger.Info($"create {type}({type.Module.Name}) : {baseType.ToReadableName()}");
                    if (!type.IsPublicOrNestedPublic()) return;
                    var baseCtor = type.GetConstructors().FirstOrDefault(ctor => !ctor.HasParameters);
                    if (baseCtor == null) return;

                    var typeReference = module.ImportReference(type);
                    IReadOnlyList<TypeReference> genericArguments = Array.Empty<TypeReference>();
                    try
                    {
                        genericArguments = type.ResolveGenericArguments(baseType);
                    }
                    catch (Exception ex)
                    {
                        logger.Debug($"cannot resolve {typeReference.ToReadableName()} : {baseType.ToReadableName()}: {ex}");
                        return;
                    }

                    logger.Debug($"resolve generic arguments: {string.Join(",", genericArguments.Select(a => a.Name))}");
                    if (genericArguments.All(arg => !arg.IsGenericParameter))
                    {

                        var className = typeReference.Name.Split('`')[0];
                        if (wrapper.NestedTypes.Any(t => t.Name == className))
                            className = typeReference.Resolve().NameWithOuterClasses();
                        // TODO: should handle if the className is still the same with any of existing type.
                        if (wrapper.NestedTypes.Any(t => t.Name == className))
                            logger.Warning($"Overwrite type with same name {className}");
                        var generated = GenerateDerivedClass(typeReference, genericArguments, className);
                        logger.Debug($"generate {generated.ToReadableName()} : {baseType.ToReadableName()}");
                        generated.Interfaces.Add(new InterfaceImplementation(baseInterface));
                        wrapper.NestedTypes.Add(generated);
                    }

                    baseType = type.HasGenericParameters
                        ? (TypeReference) type.MakeGenericInstanceType(genericArguments.ToArray())
                        : type
                    ;
                    logger.Debug($"baseType = {baseType}");
                    var derivedTypes = typeTree.GetDirectDerived(type);
                    foreach (var derivedDef in derivedTypes) RecursiveProcess(derivedDef, baseType);
                }
            }

            TypeDefinition GenerateDerivedClass(TypeReference baseType, IReadOnlyList<TypeReference> genericArguments, string className)
            {
                // .class nested public auto ansi beforefieldinit
                //   Object
                //     extends class [GenericSerializeReference.Tests]GenericSerializeReference.Tests.MultipleGeneric/Object`2<int32, float32>
                //     implements GenericSerializeReference.Tests.TestMonoBehavior/IBase
                // {

                //   .method public hidebysig specialname rtspecialname instance void
                //     .ctor() cil managed
                //   {
                //     .maxstack 8

                //     IL_0000: ldarg.0      // this
                //     IL_0001: call         instance void class [GenericSerializeReference.Tests]GenericSerializeReference.Tests.MultipleGeneric/Object`2<int32, float32>::.ctor()
                //     IL_0006: nop
                //     IL_0007: ret

                //   } // end of method Object::.ctor
                // } // end of class Object
                var classAttributes = TypeAttributes.Class | TypeAttributes.NestedPublic | TypeAttributes.BeforeFieldInit;
                var type = new TypeDefinition("", className, classAttributes);
                type.BaseType = baseType.HasGenericParameters ? baseType.MakeGenericInstanceType(genericArguments.ToArray()) : baseType;
                var ctor = baseType.Resolve().GetConstructors().First(c => !c.HasParameters);
                var ctorCall = new MethodReference(ctor.Name, module.ImportReference(ctor.ReturnType))
                {
                    DeclaringType = type.BaseType,
                    HasThis = ctor.HasThis,
                    ExplicitThis = ctor.ExplicitThis,
                    CallingConvention = ctor.CallingConvention,
                };
                type.AddEmptyCtor(ctorCall);
                return type;
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