using UnityEngine;

namespace GenericSerializeReference.Tests
{
    public class TestMonoBehavior : MonoBehaviour
    {
        [GenericSerializeReference]
        public MultipleGeneric.IInterface<int, float> GenericInterface { get; set; }
        //
        // [SerializeReference]
        // private <GenericInterface>__generic_serialize_reference.IBase _GenericInterface;
        // public static class <GenericInterface>__generic_serialize_reference
        // {
        //     public interface IBase {}
        //     public class Object : MultipleGeneric.Object<int, float>, IBase {}
        //     public class SubObject : MultipleGeneric.SubObject<int, float>, IBase {}
        //     public class PartialObject : MultipleGeneric.PartialObject<float>, IBase {}
        // }
    }
}