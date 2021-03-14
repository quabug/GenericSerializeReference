using UnityEngine;

namespace GenericSerializeReference.Sample
{
    [CreateAssetMenu(fileName = "TestSO", menuName = "TestSO", order = 0)]
    public class TestSO : ScriptableObject
    {
        [GenericSerializeReference]
        public MultipleGeneric.IInterface<int, float> IntFloat { get; set; }
    }
}