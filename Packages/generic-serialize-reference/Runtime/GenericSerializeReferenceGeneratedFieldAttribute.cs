using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Unity."+nameof(GenericSerializeReference)+".CodeGen")]

namespace GenericSerializeReference
{
    [AttributeUsage(AttributeTargets.Field)]
    internal class GenericSerializeReferenceGeneratedFieldAttribute : PropertyAttribute
    {
        public GenericSerializeReferenceAttribute.Mode Mode { get; }
        public GenericSerializeReferenceGeneratedFieldAttribute(GenericSerializeReferenceAttribute.Mode mode) => Mode = mode;
    }
}
