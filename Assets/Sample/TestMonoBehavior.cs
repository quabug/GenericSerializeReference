using System;
using GenericSerializeReference;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: GenericSerializeReferenceLogger(LogLevel.Debug)]

namespace GenericSerializeReference.Sample
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


    public static class SingleGeneric
    {
        public interface IInterface<T> {}

        [Serializable]
        public class Object<T> : IInterface<T>
        {
            public T Value;
        }

        [Serializable]
        public class SubObject<T> : Object<T>
        {
            public T[] SubValue;
        }

        [Serializable]
        public class DoubleObject : IInterface<double>
        {
            public double Value;
        }
    }

    public static class MultipleGeneric
    {
        [Preserve]
        public interface IInterface<T, U> {}

        [Serializable]
        public class Object<T, U> : IInterface<T, U>
        {
            public T ValueT;
            public U ValueU;
        }

        [Serializable]
        public class SubObject<U, T> : Object<T, U>
        {
            public T[] SubValueT;
            public U[] SubValueU;
        }

        [Serializable]
        public class PartialObject<T> : Object<T, int>
        {
            public double ValueDouble;
        }

        [Serializable]
        public class NonGeneric : Object<float, int>, IInterface<int, float>
        {
            public double ValueDouble;
        }
    }
}