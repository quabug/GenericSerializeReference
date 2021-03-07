using UnityEngine;

namespace GenericSerializeReference.Tests
{
    public class TestMonoBehavior : MonoBehaviour//, ISerializationCallbackReceiver
    {
        [GenericSerializeReference]
        public MultipleGeneric.IInterface<int, float> GenericInterface;
        //
        // [SerializeReference]
        // private __generic_serialize_reference_GenericInterface__.IBase _GenericInterface;
        // public static class __generic_serialize_reference_GenericInterface__
        // {
        //     public interface IBase {}
        //     public class Object : MultipleGeneric.Object<int, float>, IBase {}
        //     public class SubObject : MultipleGeneric.SubObject<int, float>, IBase {}
        //     public class PartialObject : MultipleGeneric.PartialObject<float>, IBase {}
        // }
        //
        // public void OnBeforeSerialize()
        // {
        // }
        //
        // public void OnAfterDeserialize()
        // {
        //     GenericInterface = (MultipleGeneric.IInterface<int, float>)_GenericInterface;
        //     _GenericInterface = null;
        //     int a = 1;
        // }
    }
}