using System;

namespace GenericSerializeReference
{
    [AttributeUsage(AttributeTargets.Property)]
    public class GenericSerializeReferenceAttribute : Attribute
    {
        public GenericSerializeReferenceAttribute(string serializedFieldPrefix = "__") {}
    }
}