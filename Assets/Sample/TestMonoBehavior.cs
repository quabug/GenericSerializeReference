using CareBoo.Serially;
using GenericSerializeReference;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: GenericSerializeReferenceLogger(LogLevel.Debug)]

namespace GenericSerializeReference.Tests
{
    public class TestMonoBehavior : MonoBehaviour
    {
        [GenericSerializeReference]
        [field: SerializeReferenceButton]
        public MultipleGeneric.IInterface<int, float> GenericInterface { get; set; }

        public int Int { get; set; }

        // [SerializeReference]
        // private <GenericInterface>__generic_serialize_reference.IBase _GenericInterface;
        // public static class <GenericInterface>__generic_serialize_reference
        // {
            public interface IBase {}
            public class Object : MultipleGeneric.Object<int, float>, IBase {}
            // public class SubObject : MultipleGeneric.SubObject<float, int>, IBase {}
            // public class PartialObject : MultipleGeneric.PartialObject<float>, IBase {}
            // public class NonGeneric : MultipleGeneric.NonGeneric, IBase {}
        // }
    }
}