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
                var serializedFieldInterface = CreateWrapperClass(property);
                logger.Debug($"generate nested class with interface {serializedFieldInterface.FullName}");
                //.field private class GenericSerializeReference.Tests.TestMonoBehavior/__generic_serialize_reference_GenericInterface__/IBase _GenericInterface
                //  .custom instance void [UnityEngine.CoreModule]UnityEngine.SerializeReference::.ctor()
                //    = (01 00 00 00 )
                var serializedField = new FieldDefinition(
                    $"_{property.Name}"
                    , FieldAttributes.Private
                    , serializedFieldInterface
                );
                var backingField = property.DeclaringType.Fields.First(field => field.Name == $"<{property.Name}>k__BackingField");
                serializedField.CustomAttributes.Add(CreateCustomAttribute<SerializeReference>());
                foreach (var customAttribute in backingField.CustomAttributes)
                    serializedField.CustomAttributes.Add(customAttribute);
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
                var propertyTypeDefinition = property.PropertyType.Resolve();
                var derivedTypes = typeTree.GetDirectDerived(propertyTypeDefinition);
                logger.Debug($"create {string.Join(",", derivedTypes)}");

                foreach (var derivedDef in derivedTypes)
                {
                    var baseCtor = derivedDef.GetConstructors().FirstOrDefault(ctor => !ctor.HasParameters);
                    if (baseCtor == null) continue;
                    var baseCtorRef = module.ImportReference(baseCtor);

                    var derivedReference = module.ImportReference(derivedDef);
                    try
                    {
                        var genericArguments = derivedDef.ResolveGenericArguments(property.PropertyType);
                        if (genericArguments.All(arg => !arg.IsGenericParameter))
                        {
                            var generated = GenerateDerivedClass(derivedReference, genericArguments);
                            generated.AddEmptyCtor(baseCtorRef);
                            logger.Debug($"generate {generated.ToReadableName()} : {property.PropertyType.ToReadableName()}");
                            generated.Interfaces.Add(new InterfaceImplementation(baseInterface));
                            wrapper.NestedTypes.Add(generated);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug($"cannot generate {derivedReference.ToReadableName()} : {property.PropertyType.ToReadableName()}: {ex}");
                    }
                }
                return baseInterface;
            }

            TypeDefinition GenerateDerivedClass(TypeReference baseType, IReadOnlyList<TypeReference> genericArguments)
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
                var className = baseType.Name.Split('`')[0];
                var classAttributes = TypeAttributes.Class | TypeAttributes.NestedPublic | TypeAttributes.BeforeFieldInit;
                var type = new TypeDefinition("", className, classAttributes);
                type.BaseType = baseType.MakeGenericInstanceType(genericArguments.ToArray());
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