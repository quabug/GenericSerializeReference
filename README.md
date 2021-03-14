Like `SerializeReference` but works for generic type.

```c#
public interface IMyInterface<T> {}
public class MyIntObject : IMyInterface<int> {}

public class MyMonoBehavior : MonoBehaviour
{
    [GenericSerializeReference]
    public IMyInterface<int> Value { get; set; }
}
```
![image](https://user-images.githubusercontent.com/683655/111064372-b47b6280-84ee-11eb-90c2-22cfbdc65cc0.png)

# Limitations
- Only types from referenced assemblies could be show up in inspector.
- Not support `struct` type.
- Not support generic field.

# Costs
- Extra time to generate IL instructions while building assembly
- Extra memory space to store a generated field for each property.

# How it works
```c#
public class MyMonoBehavior : MonoBehaviour
{
    // [GenericSerializeReference]
    // public IMyInterface<int> Value { get; set; }

    // 1. gather derived types of property (`IMyInterface<>`)
    //    then generate a concrete version of those types and make them all implement `IBase` interface
    private static class <Value>__generic_serialize_reference
    {
        public interface IBase {}
        public class MyIntObject : global::MyIntObject, IBase {}
    }

    // 2. create a field named _Value with `IBase` type
    //    which should be able to serialized by `SerializeReference` attribute
    [SerializeReference, GenericSerializeReferenceGeneratedField]
    private <Value>__generic_serialize_reference.IBase _Value;
    
    // 3. inject code into property's getter and setter
    //    make sure property get value from serialized field first
    //    and set serialized field into null to avoid get from it next time.
    [GenericSerializeReference]
    public IMyInterface<int> Value
    {
        get
        {
            return (IMyInterface<int>) _Value ?? <Value>k__backingField;
        }
        set
        {
            <Value>k__backingField = value;
            _Value = null;
        }
    }
}
```
