using System;

namespace GenericSerializeReference
{
    public enum GenerateMode { Embed, AssemblyCSharp }

    [AttributeUsage(AttributeTargets.Property)]
    public class GenericSerializeReferenceAttribute : Attribute
    {
        public const int FIELD_PREFIX_INDEX = 0;
        public const int MODE_PREFIX = 1;
        public GenericSerializeReferenceAttribute(string serializedFieldPrefix = "__", GenerateMode mode = GenerateMode.AssemblyCSharp) {}
    }
}