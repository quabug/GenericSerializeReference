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

namespace GenericSerializeReference.Library
{
    public class GenericSerializeReferenceGenerateFieldPostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance()
        {
            return this;
        }

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            var thisAssemblyName = GetType().Assembly.GetName().Name;
            var runtimeAssemblyName = typeof(GenericSerializeReferenceInAssemblyCSharpAttribute).Assembly.GetName().Name;
            return compiledAssembly.Name != thisAssemblyName &&
                   compiledAssembly.References.Any(f => Path.GetFileNameWithoutExtension(f) == runtimeAssemblyName);
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var resolver = new PostProcessorAssemblyResolver(compiledAssembly.References);
            var assembly = compiledAssembly.LoadAssembly(resolver);
            var logger = assembly.CreateLogger();
            logger.Info($"process GenericSerializeReference_FieldOnly on {assembly.Name.Name}");
            var module = assembly.MainModule;
            var modified = false;

            foreach (var (type, property, attribute) in
                from type in module.GetAllTypes()
                where type.IsClass && !type.IsAbstract
                from property in type.Properties.ToArray() // able to change `Properties` during looping
                where property.PropertyType.IsGenericInstance
                from attribute in property.GetAttributesOf<GenericSerializeReferenceInAssemblyCSharpAttribute>()
                select (type, property, attribute)
            )
            {
                var fieldType = (TypeDefinition) attribute.ConstructorArguments[0].Value;
                var fieldPrefix = (string)attribute.ConstructorArguments[1].Value;
                logger.Info($"generate field {fieldPrefix}{property.Name} on {property.DeclaringType.FullName}");
                GenericSerializeReferencePostProcessor.GenerateField(module, property, fieldType, fieldPrefix);
                modified = true;
            }

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
    }
}