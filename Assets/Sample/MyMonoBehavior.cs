using System;
using GenericSerializeReference;
using UnityEngine;

public interface IMyInterface<T> {}
public class MyIntObject : IMyInterface<int> {}
public class MyGenericObject<T> : IMyInterface<T> {}
public struct StructWillNotShow : IMyInterface<int> {}
public class MyMonoBehavior : MonoBehaviour
{
    [GenericSerializeReference(mode: GenerateMode.Embed)]
    public IMyInterface<int> Value { get; set; }
    //
    // [GenericSerializeReference("_serialized")]
    // public IMyInterface<int> Foo { get; set; }
    // private int __Foo;

    private void Awake()
    {
        Debug.Log($"{name}.{nameof(Value)} is {Value.GetType()}");
    }
}