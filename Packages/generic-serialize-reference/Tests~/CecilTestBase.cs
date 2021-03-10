using System;
using Mono.Cecil;
using NUnit.Framework;

namespace GenericSerializeReference.Tests
{
    public class CecilTestBase
    {
        protected AssemblyDefinition _assemblyDefinition;

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
                    , typeof(AnotherAssembly).Assembly.Location
                })
            });
            OnSetUp();
        }

        protected virtual void OnSetUp() {}

        protected TypeReference ImportReference<T>() => ImportReference(typeof(T));
        protected TypeReference ImportReference(Type type) => _assemblyDefinition.MainModule.ImportReference(type);
    }
}