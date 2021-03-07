using System;

namespace GenericSerializeReference
{
    [AttributeUsage(AttributeTargets.Field)]
    public class GenericSerializeReferenceAttribute : Attribute
    {
        public string SerializedFieldPrefix = "_";
    }
}