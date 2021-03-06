using System;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;

namespace GenericSerializeReference.Tests
{
    public class TestTypeTree : CecilTestBase
    {
        private TypeTree _tree;

        interface IGeneric<T, U> {}
        class TInt<T> : IGeneric<T, int> {}
        class IntU<U> : IGeneric<int, U> {}
        class IntInt : IGeneric<int, int> {}
        class TFloat<T> : IGeneric<T, float> {}
        class AnotherTFloat<T> : IGeneric<T, float> {}
        class TIntSub : TInt<int> {}

        protected override void OnSetUp()
        {
            _tree = new TypeTree(_module.GetTypes());
        }

        [Test]
        public void should_get_derived_from_generic_interface()
        {
            CheckDerivedIgnoreGenericParameters(typeof(IGeneric<,>), typeof(TInt<>), typeof(IntU<>), typeof(IntInt), typeof(TFloat<>), typeof(AnotherTFloat<>), typeof(TIntSub));
        }

        [Test]
        public void should_get_derived_from_generic_interface_by_ignoring_generic_parameters()
        {
            CheckDerivedIgnoreGenericParameters(typeof(IGeneric<int,int>), typeof(TInt<int>), typeof(IntU<int>), typeof(IntInt), typeof(TFloat<int>), typeof(AnotherTFloat<int>), typeof(TIntSub));
        }

        [Test]
        public void should_get_derived_from_concrete_generic_interface()
        {
            CheckDerived(typeof(IGeneric<int,int>), typeof(TInt<int>), typeof(IntU<int>), typeof(IntInt), typeof(TIntSub));
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
            var derivedTypes = _tree
                .GetOrCreateAllDerivedReference(_module.ImportReference(@base), publicOnly: false)
                .Select(type => type.FullName)
                .ToArray()
            ;
            Assert.That(derivedTypes, Is.EquivalentTo(types.Select(type => _module.ImportReference(type).FullName)));
        }

        void CheckDerivedIgnoreGenericParameters(Type @base, params Type[] types)
        {
            var tokens = _tree
                .GetAllDerivedDefinition(_module.ImportReference(@base).Resolve())
                .Select(type => type.MetadataToken)
                .ToArray()
            ;
            Assert.That(tokens, Is.EquivalentTo(types.Select(type => _module.ImportReference(type).Resolve().MetadataToken)));
        }
    }
}