using System;

namespace GenericSerializeReference.Library
{
    [AttributeUsage(AttributeTargets.Property)]
    public class GenericSerializeReferenceInAssemblyCSharpAttribute : Attribute
    {
        public GenericSerializeReferenceInAssemblyCSharpAttribute(Type interfaceType, string serializedFieldPrefix = "__") {}
    }
}