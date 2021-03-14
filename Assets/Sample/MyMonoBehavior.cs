using GenericSerializeReference;
using UnityEngine;

public interface IMyInterface<T> {}
public class MyIntObject : IMyInterface<int> {}
public struct StructWillNotShow : IMyInterface<int> {}
public class MyMonoBehavior : MonoBehaviour
{
    [GenericSerializeReference]
    public IMyInterface<int> Value { get; set; }

    [GenericSerializeReference("_serialized")]
    public IMyInterface<int> Foo { get; set; }
    private int __Foo;
}