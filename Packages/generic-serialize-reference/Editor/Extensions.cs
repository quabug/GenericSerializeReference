using System;
using System.Collections.Generic;
using Mono.Cecil;

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
    }
}