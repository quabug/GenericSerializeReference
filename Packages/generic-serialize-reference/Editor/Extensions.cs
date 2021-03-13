using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace GenericSerializeReference
{
    internal static class EnumerableExtension
    {
        public static IEnumerable<T> Yield<T>(this T value)
        {
            yield return value;
        }
    }

    internal static class CecilExtension
    {
        public static TypeDefinition ToTypeDefinition<T>(this ModuleDefinition module) =>
            module.ToTypeDefinition(typeof(T));

        public static TypeDefinition ToTypeDefinition(this ModuleDefinition module, Type type) =>
            module.ImportReference(type).Resolve();

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
    }
}