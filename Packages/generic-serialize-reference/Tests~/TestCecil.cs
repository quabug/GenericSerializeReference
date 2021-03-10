using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GenericSerializeReference.Tests
{
    public class TestCecil : CecilTestBase
    {
        [Test]
        public void should_resolve_unity_type()
        {
            var type = ImportReference<ExitPlayMode>().Resolve();
            Assert.AreEqual(1, type.Interfaces.Count);
            var interfaceReference = type.Interfaces.First();
            Assert.NotNull(interfaceReference);
            Assert.NotNull(interfaceReference.InterfaceType);
            Assert.NotNull(interfaceReference.InterfaceType.Resolve());
        }

        [Test]
        public void should_get_generic_type_definition_from_generic_type()
        {
            var typeDef = ImportReference<MultipleGeneric.IInterface<int, int>>().Resolve();
            Assert.NotNull(typeDef);
        }

        [Test]
        public void should_resolve_generic_types_in_another_assembly()
        {
            var typeRef = ImportReference<AnotherAssembly.IGeneric<int,int>>();
            Debug.Log($"{typeRef} {typeRef.Module}");
            Assert.NotNull(typeRef);
            Assert.NotNull(typeRef.Resolve());
        }

        [Test]
        public void should_resolve_types_in_another_assembly()
        {
            var typeRef = ImportReference<AnotherAssembly>();
            Debug.Log($"{typeRef} {typeRef.Module}");
            Assert.NotNull(typeRef);
            Assert.NotNull(typeRef.Resolve());
        }

        class AnotherGeneric : AnotherAssembly.Generic {}

        [Test]
        public void should_resolve_types_inherited_from_another_assembly()
        {
            var typeRef = ImportReference<AnotherGeneric>();
            Debug.Log($"{typeRef} {typeRef.Module}");
            Assert.NotNull(typeRef);
            Assert.NotNull(typeRef.Resolve());

            var baseType = typeRef.Resolve().BaseType;
            Debug.Log($"{baseType} {baseType.Module}");
            Assert.NotNull(baseType);
            Assert.NotNull(baseType.Resolve());
        }

        [Test]
        public void should_resolve_types_in_system_assembly()
        {
            var typeRef = ImportReference<Attribute>();
            Debug.Log($"{typeRef} {typeRef.Module}");
            Assert.NotNull(typeRef);
            Assert.NotNull(typeRef.Resolve());
        }
    }
}