using System;
using GenericSerializeReference;
using UnityEngine;

[assembly: GenericSerializeReferenceLogger(LogLevel.Debug)]

namespace GenericSerializeReference.Tests
{
    public class TestMonoBehavior : MonoBehaviour
    {
        public class Object : MultipleGeneric.IInterface<int, float>
        {
            public int V;
        }

        [GenericSerializeReference]
        public MultipleGeneric.IInterface<int, float> IntFloat { get; set; }

        [GenericSerializeReference]
        public MultipleGeneric.IInterface<float, int> FloatInt { get; set; }

        [GenericSerializeReference]
        public MultipleGeneric.IInterface<int, int> IntInt { get; set; }

        [GenericSerializeReference]
        public SingleGeneric.IInterface<int> Int { get; set; }

        [GenericSerializeReference]
        public SingleGeneric.IInterface<double> Double { get; set; }

        [GenericSerializeReference]
        public SingleGeneric.Object<int> IntObject { get; set; }

        [GenericSerializeReference]
        public MultipleGeneric.Object<int, int> IntIntObject { get; set; }

        private void Awake()
        {
            Debug.Log($"{IntFloat.GetType()}: \n {JsonUtility.ToJson(IntFloat)}");
            Debug.Log("set to null");
            IntFloat = null;
            Debug.Log(IntFloat == null ? "null" : IntFloat.ToString());
            Debug.Log($"set to {nameof(TestMonoBehavior)}.{nameof(Object)}");
            IntFloat = new Object { V = 5 };
            Debug.Log($"{IntFloat.GetType()}: \n {JsonUtility.ToJson(IntFloat)}");
        }
    }
}