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

namespace GenericSerializeReference.Library
{
    public class GenericSerializeReferenceLibraryPostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance()
        {
            return this;
        }

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            return compiledAssembly.Name == "Assembly-CSharp";
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var logger = new ILPostProcessorLogger(new List<DiagnosticMessage>());
            var (assembly, referenceAssemblies) = GenericSerializeReferencePostProcessor.LoadAssemblyDefinition(
                compiledAssembly, name => name.StartsWith("Library")
            );

            try
            {
                var allTypes = referenceAssemblies.Append(assembly)
                    .Where(asm => !asm.Name.Name.StartsWith("Unity.") &&
                                  !asm.Name.Name.StartsWith("UnityEditor.") &&
                                  !asm.Name.Name.StartsWith("UnityEngine.")
                    )
                    .SelectMany(asm => asm.MainModule.GetAllTypes())
                    .ToArray()
                ;
                var modified = Process(assembly.MainModule, allTypes, logger);
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

                // if (Directory.Exists(GenericSerializeReferencePostProcessor.TempDirectory))
                    // Directory.Delete(GenericSerializeReferencePostProcessor.TempDirectory, true);
            }
        }

        private bool IsInterfaceOnlyAttribute(CustomAttribute attribute)
        {
            var mode = (GenericSerializeReferenceAttribute.Mode)
                attribute.ConstructorArguments[GenericSerializeReferenceAttribute.MODE_INDEX].Value;
            return mode == GenericSerializeReferenceAttribute.Mode.InterfaceOnly;
        }

        private bool Process(ModuleDefinition module, IReadOnlyList<TypeDefinition> types, ILPostProcessorLogger logger)
        {
            var modified = false;

            var typeTree = new TypeTree(types);
            logger.Info($"tree: {typeTree}");

            foreach (var (type, property, attribute) in
                from type in types
                where type.IsClass && !type.IsAbstract
                from property in type.Properties
                where property.PropertyType.IsGenericInstance
                from attribute in property.GetAttributesOf<GenericSerializeReferenceAttribute>()
                where IsInterfaceOnlyAttribute(attribute)
                select (type, property, attribute)
            )
            {
                var fieldNamePrefix = (string)attribute.ConstructorArguments[GenericSerializeReferenceAttribute.PREFIX_INDEX].Value;
                var fieldName = $"{fieldNamePrefix}{property.Name}";
                var field = type.Fields.FirstOrDefault(field => field.Name == fieldName);
                if (field == null)
                {
                    logger.Warning($"Cannot process on property {property} without corresponding field by name of {fieldName} ({string.Join(",", type.Fields.Select(f => f.FullName))})");
                    continue;
                }

                var baseInterface = field.FieldType;
                var baseGeneric = property.PropertyType;

                var wrapper = new TypeDefinition(
                    "<GenericSerializeReference>." + type.FullName.Replace('.', '_')
                    , $"{property.Name}"
                    , TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract | TypeAttributes.Public | TypeAttributes.BeforeFieldInit
                );
                module.Types.Add(wrapper);

                logger.Warning($"create implementation: {baseInterface.FullName} {baseGeneric.FullName}");
                CreateDerived(module, typeTree, wrapper, baseGeneric, baseInterface);

                modified = true;
            }
            return modified;
        }

        void CreateDerived(ModuleDefinition module, TypeTree typeTree, TypeDefinition wrapper, TypeReference genericRoot, TypeReference baseInterface)
        {
            foreach (var d in typeTree.GetOrCreateAllDerivedReference(genericRoot))
            {
                var derived = module.ImportReference(d);
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
                        Debug.LogWarning($"Overwrite type with same name {className}");
                    var classAttributes = TypeAttributes.Class | TypeAttributes.NestedPublic | TypeAttributes.BeforeFieldInit;
                    var generated = new TypeDefinition("", className, classAttributes);
                    generated.BaseType = derived.HasGenericParameters ? derived.MakeGenericInstanceType(genericArguments.ToArray()) : derived;
                    var ctor = module.ImportReference(derived.Resolve().GetConstructors().First(c => !c.HasParameters)).Resolve();
                    var ctorCall = new MethodReference(ctor.Name, module.ImportReference(ctor.ReturnType))
                    {
                        DeclaringType = generated.BaseType,
                        HasThis = ctor.HasThis,
                        ExplicitThis = ctor.ExplicitThis,
                        CallingConvention = ctor.CallingConvention,
                    };
                    generated.AddEmptyCtor(ctorCall);
                    var interfaceImplementation = new InterfaceImplementation(baseInterface);
                    generated.Interfaces.Add(interfaceImplementation);
                    wrapper.NestedTypes.Add(generated);
                }
            }
        }
    }
}