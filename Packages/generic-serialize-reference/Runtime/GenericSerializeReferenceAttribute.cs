using System;

namespace GenericSerializeReference
{
    [AttributeUsage(AttributeTargets.Property)]
    public class GenericSerializeReferenceAttribute : Attribute
    {
        public string SerializedFieldPrefix { get; }
        public GenericSerializeReferenceAttribute(string serializedFieldPrefix = "__") =>
            SerializedFieldPrefix = serializedFieldPrefix;
    }
}