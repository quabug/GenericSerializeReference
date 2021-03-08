using NUnit.Framework;

namespace GenericSerializeReference.Tests
{
    public class A
    {
  //.property instance class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32> Value()
  //{
  //  .custom instance void [GenericSerializeReference]GenericSerializeReference.GenericSerializeReferenceAttribute::.ctor()
  //    = (01 00 00 00 )
  //  .get instance class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32> GenericSerializeReference.Tests.A::get_Value()
  //  .set instance void GenericSerializeReference.Tests.A::set_Value(class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32>)
  //} // end of property A::Value
        [GenericSerializeReference] public SingleGeneric.IInterface<int> Value
        {
    // --------add--------
    // IL_0000: ldarg.0      // this
    // IL_0001: ldfld        class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32> GenericSerializeReference.Tests.A::_Value
    // IL_0006: dup
    // IL_0007: brtrue.s     IL_0010
    // IL_0009: pop
    // --------add--------

    // IL_000a: ldarg.0      // this
    // IL_000b: ldfld        class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32> GenericSerializeReference.Tests.A::k__BackingField
    // IL_0010: ret
    get;
    // {
    //     if (_Value != null) return _Value;
    // return default;
    // }

    //IL_0000: ldarg.0      // this
    //IL_0001: ldarg.1      // 'value'
    //IL_0002: stfld        class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32> GenericSerializeReference.Tests.A::'<Value>k__BackingField'

    // before ret
    // -------add-------
    // IL_0008: ldarg.0      // this
    // IL_0009: ldnull
    // IL_000a: stfld        class GenericSerializeReference.Tests.SingleGeneric/IInterface`1<int32> GenericSerializeReference.Tests.A::_Value
    // -------add-------

    //IL_0007: ret

    set;
        }
    }

    public class Test
    {
        interface IBase {}
        class MultipleGenericObject : MultipleGeneric.Object<int, float>, IBase {}

        [Test]
        public void t()
        {
            IBase value = null;
            // var value = (new MultipleGenericObject());
            var obj = (MultipleGeneric.Object<int, float>)value;
            // var value = (MultipleGenericObject) obj;
        }
    }
}