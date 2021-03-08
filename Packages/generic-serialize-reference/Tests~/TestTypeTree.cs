using System;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;

namespace GenericSerializeReference.Tests
{
    public class TestTypeTree : CecilTestBase
    {
        private TypeTree _tree;
        private ModuleDefinition _module;

        interface IGeneric<T, U> {}
        class TInt<T> : IGeneric<T, int> {}
        class IntU<U> : IGeneric<int, U> {}
        class IntInt : IGeneric<int, int> {}
        class TFloat<T> : IGeneric<T, float> {}
        class AnotherTFloat<T> : IGeneric<T, float> {}
        class TIntSub : TInt<int> {}

        protected override void OnSetUp()
        {
            _module = _assemblyDefinition.MainModule;
            _tree = new TypeTree(_module.GetTypes());
        }

        [Test]
        public void should_get_derived_from_generic_interface()
        {
            CheckDerived(typeof(IGeneric<,>), typeof(TInt<>), typeof(IntU<>), typeof(IntInt), typeof(TFloat<>), typeof(AnotherTFloat<>), typeof(TIntSub));
        }

        [Test]
        public void should_get_derived_from_generic_interface_without_type_constraint()
        {
            CheckDerived(typeof(IGeneric<int,int>), typeof(TInt<int>), typeof(IntU<int>), typeof(IntInt), typeof(TFloat<int>), typeof(AnotherTFloat<int>), typeof(TIntSub));
        }

        interface I {}
        class A {}
            class AA : A {}
                class AAA : AA, I {}
                    class AAAA : AAA, I {}
                        class AAAAA : AAAA {}
                    class AAAB : AAA {}
                        class AAABA : AAAB {}
                        class AAABB : AAAB {}
                    class AAAC : AAA {}
            class AB : A {}
                class ABA : AB, I {}
                    class ABAA : ABA, I {}
                    class ABAB : ABA {}
                class ABB : AB, I {}
                class ABC : AB {}

        [Test]
        public void should_get_derived_from_class()
        {
            CheckDerived(
                typeof(A),
                    typeof(AA),
                        typeof(AAA),
                            typeof(AAAA),
                                typeof(AAAAA),
                            typeof(AAAB),
                                typeof(AAABA),
                                typeof(AAABB),
                            typeof(AAAC),
                    typeof(AB),
                        typeof(ABA),
                            typeof(ABAA),
                            typeof(ABAB),
                        typeof(ABB),
                        typeof(ABC)
            );

            CheckDerived(
                        typeof(AAA),
                            typeof(AAAA),
                                typeof(AAAAA),
                            typeof(AAAB),
                                typeof(AAABA),
                                typeof(AAABB),
                            typeof(AAAC)
            );

            CheckDerived(
                    typeof(AB),
                        typeof(ABA),
                            typeof(ABAA),
                            typeof(ABAB),
                        typeof(ABB),
                        typeof(ABC)
            );
        }

        [Test]
        public void should_get_derived_from_interface()
        {
            CheckDerived(
                typeof(I),
                    // typeof(AA),
                        typeof(AAA),
                            typeof(AAAA),
                                typeof(AAAAA),
                            typeof(AAAB),
                                typeof(AAABA),
                                typeof(AAABB),
                            typeof(AAAC),
                    // typeof(AB),
                        typeof(ABA),
                            typeof(ABAA),
                            typeof(ABAB),
                        typeof(ABB)
                        // typeof(ABC)
            );
        }

        void CheckDerived(Type @base, params Type[] types)
        {
            var tokens = _tree
                .GetDerived(_module.ToTypeDefinition(@base))
                .Select(type => type.MetadataToken)
                .ToArray()
            ;
            IsTokensContains(tokens, types);
        }

        void IsTokensContains(MetadataToken[] tokens, params Type[] types)
        {
            Assert.AreEqual(types.Length, tokens.Length);
            foreach (var type in types) Assert.Contains(_module.ToTypeDefinition(type).MetadataToken, tokens);
        }
    }
}