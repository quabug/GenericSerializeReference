Similar like [`SerializeReference`](https://docs.unity3d.com/ScriptReference/SerializeReference.html) but works on generic property.

```c#
public interface IMyInterface<T> {}
public class MyIntObject : IMyInterface<int> {}
public class MyGenericObject<T> : IMyInterface<T> {}

public class MyMonoBehavior : MonoBehaviour
{
    [GenericSerializeReference]
    public IMyInterface<int> Value { get; set; }
}
```
![image](https://user-images.githubusercontent.com/683655/111073521-0be2f800-851a-11eb-956e-a3044f141093.png)

## Requirement
Unity3D 2020.2+ (not guaranteed to work below 2020.2)

## Installation
[OpenUPM](https://openupm.com/packages/com.quabug.generic-serialize-reference/): `openupm add com.quabug.generic-serialize-reference`

## Limitations
- Not support `struct` type.
- Not support generic field.
- Not support variance.

## Costs
- Extra time to generate IL code while building assembly
- Extra memory space to store a generated field for each property.

## How it works

### AssemblyCSharp Mode
```c#
// Generate derived types into AssemblyCSharp.dll
public class MyMonoBehavior : MonoBehaviour
{
    // [GenericSerializeReference(mode: GenerateMode.AssemblyCSharp)]
    // public IMyInterface<int> Value { get; set; }

    // 1. create a field named _Value with `IBase` type
    //    which should be able to serialized by `SerializeReference` attribute
    [SerializeReference, GenericSerializeReferenceGeneratedField]
    private GenericSerializeReference.IBase _Value;
    
    // 2. inject code into property's getter and setter
    //    make sure property get value from serialized field first
    //    and setter set serialized field into null to avoid get from it next time.
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

// AssemblyCSharp.dll
// 3. gather derived types of property (`IMyInterface<int>`)
//    then generate a non-generic version of those types and make them all implement `IBase` interface
namespace <GenericSerializeReference>
{
    static class IMyInterface`1<System_Int32>
    {
        class MyIntObject : global::MyIntObject, GenericSerializeReference.IBase {}
    }
}
```

### Embed Mode
```c#
// Embed into current class
public class MyMonoBehavior : MonoBehaviour
{
    // [GenericSerializeReference(mode: GenerateMode.Embed)]
    // public IMyInterface<int> Value { get; set; }

    // 1. create a field named _Value with `IBase` type
    //    which should be able to serialized by `SerializeReference` attribute
    [SerializeReference, GenericSerializeReferenceGeneratedField]
    private <Value>__generic_serialize_reference.IBase _Value;
    
    // 2. inject code into property's getter and setter
    //    make sure property get value from serialized field first
    //    and setter set serialized field into null to avoid get from it next time.
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
    
    // 3. gather derived types of property (`IMyInterface<int>`)
    //    then generate a non-generic version of those types and make them all implement `IBase` interface
    private static class <Value>__generic_serialize_reference
    {
        public interface IBase {}
        public class MyIntObject : global::MyIntObject, IBase {}
    }

}
```

## License
[MIT](https://github.com/quabug/GenericSerializeReference/blob/main/LICENSE)

[Drawer](Packages/generic-serialize-reference/Editor/GenericSerializeReferenceFieldAttributeDrawer.cs) modified from [TextusGames](https://github.com/TextusGames)'s [UnitySerializedReferenceUI](https://github.com/TextusGames/UnitySerializedReferenceUI) with [MIT](https://github.com/TextusGames/UnitySerializedReferenceUI/blob/master/Assets/Textus/SerializeReferenceUI/LICENSE.txt) license
