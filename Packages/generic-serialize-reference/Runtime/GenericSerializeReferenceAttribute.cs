using System;

namespace GenericSerializeReference
{
    [AttributeUsage(AttributeTargets.Property)]
    public class GenericSerializeReferenceAttribute : Attribute
    {
        public enum Mode { Game, Library }
        public const int PREFIX_INDEX = 0;
        public const int MODE_INDEX = 1;
        public GenericSerializeReferenceAttribute(string serializedFieldPrefix = "__", Mode mode = Mode.Game) =>
            (_, _) = (serializedFieldPrefix, mode);
    }
}