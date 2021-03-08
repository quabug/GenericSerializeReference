using System;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;
using UnityEngine;

namespace GenericSerializeReference.Tests
{
    public class TestCecil
    {
        private AssemblyDefinition _assemblyDefinition;

        [SetUp]
        public void SetUp()
        {
            var assemblyLocation = GetType().Assembly.Location;
            _assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyLocation, new ReaderParameters
            {
                AssemblyResolver = new PostProcessorAssemblyResolver(new []
                {
                    GetType().Assembly.Location
                    , typeof(object).Assembly.Location
                })
            });
        }

        interface IGeneric<T, U> {}
        class TInt<T> : IGeneric<T, int> {}
        class IntU<U> : IGeneric<int, U> {}
        class IntInt : IGeneric<int, int> {}
        class TFloat<T> : IGeneric<T, float> {}
        class AnotherTFloat<T> : IGeneric<T, float> {}
        class TIntSub : TInt<int> {}

        [Test]
        public void should()
        {
            var module = _assemblyDefinition.MainModule;
            var types = new[]
                {
                    typeof(TInt<int>),
                    typeof(IntU<int>),
                    typeof(IntInt),
                    typeof(TFloat<int>),
                    typeof(AnotherTFloat<int>),
                    typeof(IGeneric<int, int>),
                    typeof(TIntSub),
                    typeof(IGeneric<,>),
                }.ToDictionary(type => type, type => module.ImportReference(type));
            foreach (var t in types)
            {
                var type = t.Value;
                Debug.Log($"{type.Name}: {type.Resolve().MetadataToken} {type.Resolve().Interfaces.FirstOrDefault()?.InterfaceType.Resolve().MetadataToken}");
            }

            Debug.Log($"{types[typeof(TIntSub)].Resolve().BaseType.Resolve().MetadataToken}");

            Assert.AreEqual(types[typeof(IGeneric<int, int>)].Resolve().MetadataToken,
                types[typeof(IntInt)].Resolve().Interfaces[0].InterfaceType.Resolve().MetadataToken);

            Assert.IsTrue(types[typeof(TIntSub)].Resolve().HasInterfaces);
        }
    }
}