using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Unity."+nameof(GenericSerializeReference)+".CodeGen")]
[assembly: InternalsVisibleTo("GenericSerializeReference.Library.Editor")]

namespace GenericSerializeReference
{
    [AttributeUsage(AttributeTargets.Field)]
    internal class GenericSerializeReferenceGeneratedFieldAttribute : PropertyAttribute
    {
        public Type PropertyType { get; }
        public GenericSerializeReferenceGeneratedFieldAttribute(Type propertyType) => PropertyType = propertyType;
    }
}
