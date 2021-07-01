using System;

namespace GenericSerializeReference
{
    [AttributeUsage(AttributeTargets.Property)]
    public class GenericSerializeReferenceAttribute : Attribute
    {
        public const int PREFIX_INDEX = 0;
        public const int MODE_INDEX = 1;

        public enum Mode { EmbedClasses, InterfaceOnly }
        public Mode GenerateMode { get; }

        public GenericSerializeReferenceAttribute(string serializedFieldPrefix = "__", Mode mode = Mode.EmbedClasses) =>
            GenerateMode = mode;
    }
}