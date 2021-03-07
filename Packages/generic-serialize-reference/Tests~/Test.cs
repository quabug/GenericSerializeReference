using NUnit.Framework;

namespace GenericSerializeReference.Tests
{
    public class Base
    {

    }

    public class A : Base
    {
        [GenericSerializeReference] public SingleGeneric.IInterface<int> Value;
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