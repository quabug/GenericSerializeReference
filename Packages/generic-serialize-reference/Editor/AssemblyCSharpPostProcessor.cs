using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace GenericSerializeReference
{
    public class AssemblyCSharpPostProcessor : ILPostProcessor
    {
        private const string _overrideAssemblyCSharp = "GenericSerializeReference.OverrideAssemblyCSharp";

        public override ILPostProcessor GetInstance()
        {
            return this;
        }

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (compiledAssembly.Name == _overrideAssemblyCSharp) return true;
            var overrideDll = $"{_overrideAssemblyCSharp}.dll";
            var hasOverride = compiledAssembly.References.Any(@ref => @ref.EndsWith(overrideDll));
            return compiledAssembly.Name == "Assembly-CSharp" && !hasOverride;
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            using var resolver = new PostProcessorAssemblyResolver(compiledAssembly.References);
            using var assembly = compiledAssembly.LoadAssembly(resolver);
            var referenceAssemblies = compiledAssembly.LoadLibraryAssemblies(resolver).ToArray();
            var logger = assembly.CreateLogger();
            logger.Info($"process GenericSerializeReference.AssemblyCSharp on {assembly.Name.Name}({string.Join(",", compiledAssembly.References.Where(r => r.StartsWith("Library")))})");

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
                foreach (var reference in referenceAssemblies) reference.Dispose();
            }
        }

        private bool Process(ModuleDefinition module, IReadOnlyList<TypeDefinition> types, ILPostProcessorLogger logger)
        {
            var modified = false;
            var typeTree = new TypeTree(types);
            var wrappers = new Dictionary<TypeReference, TypeDefinition>();
            foreach (var (type, property, attribute) in
                from type in types
                where type.IsClass && !type.IsAbstract
                from property in type.Properties
                where property.PropertyType.IsGenericInstance
                from attribute in property.GetAttributesOf<GenericSerializeReferenceAttribute>()
                where IsAssemblyCSharpMode(attribute)
                select (type, property, attribute)
            )
            {
                var baseInterface = module.ImportReference(typeof(IBase));
                var baseGeneric = property.PropertyType;

                if (!wrappers.TryGetValue(baseGeneric, out var wrapper))
                {
                    wrapper = new TypeDefinition(
                        "<GenericSerializeReference>" + baseGeneric.Namespace,
                        baseGeneric.FullName.Replace('.', '_'),
                        TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract | TypeAttributes.Public | TypeAttributes.BeforeFieldInit
                    );
                    wrappers[baseGeneric] = wrapper;

                    foreach (var derived in typeTree
                        .GetOrCreateAllDerivedReference(baseGeneric)
                        .Select(module.ImportReference))
                    {
                        var generated = CreateDerived(wrapper, derived, baseInterface);
                        if (generated != null)
                        {
                            logger.Debug($"generate {derived.FullName}");
                            wrapper.NestedTypes.Add(generated);
                            modified = true;
                        }
                    }
                }
                module.Types.Add(wrapper);
            }
            return modified;

            TypeDefinition CreateDerived(TypeDefinition wrapper, TypeReference derived, TypeReference baseInterface)
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
                    return generated;
                }
                return null;
            }
        }

        static bool IsAssemblyCSharpMode(CustomAttribute attribute)
        {
            var mode = (GenerateMode) attribute.ConstructorArguments[1].Value;
            return mode == GenerateMode.AssemblyCSharp;
        }
    }
}