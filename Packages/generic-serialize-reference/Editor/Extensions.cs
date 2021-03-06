using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace GenericSerializeReference
{
    internal static class EnumerableExtension
    {
        public static IEnumerable<T> Yield<T>(this T value)
        {
            yield return value;
        }

        public static int FindLastIndexOf<T>(this IList<T> list, Predicate<T> predicate)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (predicate(list[i]))
                    return i;
            }
            return -1;
        }
    }

    internal static class ReflectionExtension
    {
        public static string ToReadableName(this Type type)
        {
            return type.IsGenericType ? Regex.Replace(type.ToString(), @"(\w+)`\d+\[(.*)\]", "$1<$2>") : type.ToString();
        }
    }

    internal static class PostProcessorExtension
    {
        public static AssemblyDefinition LoadAssembly(this ICompiledAssembly compiledAssembly, IAssemblyResolver resolver)
        {
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

        public static IEnumerable<AssemblyDefinition> LoadLibraryAssemblies(this ICompiledAssembly compiledAssembly, PostProcessorAssemblyResolver resolver)
        {
            return compiledAssembly.References.Where(name => name.StartsWith("Library")).Select(resolver.Resolve);
        }

        public static ILPostProcessorLogger CreateLogger(this AssemblyDefinition assembly)
        {
            var logger = new ILPostProcessorLogger(new List<DiagnosticMessage>());
            var loggerAttributes = assembly.GetAttributesOf<GenericSerializeReferenceLoggerAttribute>();
            if (loggerAttributes.Any()) logger.LogLevel = (LogLevel)loggerAttributes.First().ConstructorArguments[0].Value;
            return logger;
        }
    }

    internal static class CecilExtension
    {
        public static string ToReadableName(this TypeReference type)
        {
            if (!type.IsGenericInstance) return type.Name;
            return $"{type.Name.Split('`')[0]}<{string.Join(",", ((GenericInstanceType)type).GenericArguments.Select(a => a.Name))}>";
        }

        public static IReadOnlyList<TypeReference> ResolveGenericArguments(this TypeDefinition self, TypeReference @base)
        {
            if (!@base.IsGenericInstance)
                return self.IsGenericInstance ? self.GenericParameters.ToArray() : Array.Empty<TypeReference>();

            var genericBase = (GenericInstanceType) @base;
            var selfBase = ParentTypes(self).Where(p => IsTypeEqual(p, @base))
                // let it throw
                .Select(p => (GenericInstanceType)p)
                .First(p => IsPartialGenericMatch(p, genericBase))
            ;
            var genericArguments = new TypeReference[self.GenericParameters.Count];
            var selfBaseGenericArguments = selfBase.GenericArguments.ToList();
            for (var i = 0; i < self.GenericParameters.Count; i++)
            {
                var genericParameter = self.GenericParameters[i];
                var index = selfBaseGenericArguments.FindIndex(type => type.Name == genericParameter.Name);
                if (index >= 0) genericArguments[i] = genericBase.GenericArguments[index];
                else genericArguments[i] = self.GenericParameters[i];
            }
            return genericArguments;
        }

        public static IEnumerable<TypeReference> ParentTypes(this TypeDefinition type)
        {
            var parents = Enumerable.Empty<TypeReference>();
            if (type.HasInterfaces) parents = type.Interfaces.Select(i => i.InterfaceType);
            if (type.BaseType != null) parents = parents.Append(type.BaseType);
            return parents;
        }

        public static bool IsPartialGenericMatch(this GenericInstanceType partial, GenericInstanceType concrete)
        {
            if (!IsTypeEqual(partial, concrete))
                throw new ArgumentException($"{partial} and {concrete} have different type");
            if (partial.GenericArguments.Count != concrete.GenericArguments.Count)
                throw new ArgumentException($"{partial} and {concrete} have different count of generic arguments"); ;
            if (concrete.GenericArguments.Any(arg => arg.IsGenericParameter))
                throw new ArgumentException($"{concrete} is not a concrete generic type"); ;

            return partial.GenericArguments
                .Zip(concrete.GenericArguments, (partialArgument, concreteArgument) => (partialArgument, concreteArgument))
                .All(t => t.partialArgument.IsGenericParameter || IsTypeEqual(t.partialArgument, t.concreteArgument))
            ;
        }

        public static bool IsTypeEqual(this TypeReference lhs, TypeReference rhs)
        {
            return IsTypeEqual(lhs.Resolve(), rhs.Resolve());
        }

        public static bool IsTypeEqual(this TypeDefinition lhs, TypeDefinition rhs)
        {
            return lhs != null && rhs != null &&
                   lhs.MetadataToken == rhs.MetadataToken &&
                   lhs.Module.Name == rhs.Module.Name
                ;
        }


        //.method public hidebysig specialname rtspecialname instance void
        //  .ctor() cil managed
        //{
        //  .maxstack 8

        //  IL_0000: ldarg.0      // this
        //  IL_0001: call         instance void class [GenericSerializeReference.Tests]GenericSerializeReference.Tests.MultipleGeneric/Object`2<int32, float32>::.ctor()
        //  IL_0006: nop
        //  IL_0007: ret

        //} // end of method Object::.ctor
        public static void AddEmptyCtor(this TypeDefinition type, MethodReference baseCtor)
        {
            var attributes = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
            var ctor = new MethodDefinition(".ctor", attributes, baseCtor.ReturnType);
            ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, baseCtor));
            ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(ctor);
        }

        public static TypeReference CreateTypeReference(this TypeDefinition type, IReadOnlyList<TypeReference> genericArguments)
        {
            return type.HasGenericParameters
                ? (TypeReference) type.MakeGenericInstanceType(genericArguments.ToArray())
                : type
            ;
        }

        public static bool IsPublicOrNestedPublic(this TypeDefinition type)
        {
            foreach (var t in type.GetSelfAndAllDeclaringTypes())
            {
                if ((t.IsNested && !t.IsNestedPublic) || (!t.IsNested && !t.IsPublic)) return false;
            }
            return true;
        }

        public static string NameWithOuterClasses(this TypeDefinition type)
        {
            return type.GetSelfAndAllDeclaringTypes().Aggregate("", (name, t) => $"{t.Name}.{name}");
        }

        public static IEnumerable<TypeDefinition> GetSelfAndAllDeclaringTypes(this TypeDefinition type)
        {
            yield return type;
            while (type.DeclaringType != null)
            {
                yield return type.DeclaringType;
                type = type.DeclaringType;
            }
        }

        public static CustomAttribute AddCustomAttribute<T>(
            this ICustomAttributeProvider attributeProvider
            , ModuleDefinition module
            , params Type[] constructorTypes
        ) where T : Attribute
        {
            var attribute = new CustomAttribute(module.ImportReference(typeof(T).GetConstructor(constructorTypes)));
            attributeProvider.CustomAttributes.Add(attribute);
            return attribute;
        }

        public static TypeDefinition GenerateDerivedClass(this TypeReference baseType, IEnumerable<TypeReference> genericArguments, string className, ModuleDefinition module = null)
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
            module ??= baseType.Module;
            var classAttributes = TypeAttributes.Class | TypeAttributes.NestedPublic | TypeAttributes.BeforeFieldInit;
            var type = new TypeDefinition("", className, classAttributes);
            type.BaseType = baseType.HasGenericParameters ? baseType.MakeGenericInstanceType(genericArguments.ToArray()) : baseType;
            var ctor = module.ImportReference(baseType.Resolve().GetConstructors().First(c => !c.HasParameters)).Resolve();
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

        public static TypeDefinition CreateNestedStaticPrivateClass(this TypeDefinition type, string name)
        {
            // .class nested private abstract sealed auto ansi beforefieldinit
            //   <$PropertyName>__generic_serialize_reference
            //     extends [mscorlib]System.Object
            var typeAttributes = TypeAttributes.Class |
                                 TypeAttributes.Sealed |
                                 TypeAttributes.Abstract |
                                 TypeAttributes.NestedPrivate |
                                 TypeAttributes.BeforeFieldInit;
            var nestedType = new TypeDefinition("", name, typeAttributes);
            nestedType.BaseType = type.Module.ImportReference(typeof(object));
            type.NestedTypes.Add(nestedType);
            return nestedType;
        }

        public static IEnumerable<CustomAttribute> GetAttributesOf<T>([NotNull] this ICustomAttributeProvider provider) where T : Attribute
        {
            return provider.HasCustomAttributes ?
                provider.CustomAttributes.Where(IsAttributeOf) :
                Enumerable.Empty<CustomAttribute>();

            static bool IsAttributeOf(CustomAttribute attribute) => attribute.AttributeType.FullName == typeof(T).FullName;
        }
    }
}