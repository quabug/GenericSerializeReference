using GenericSerializeReference;
using UnityEngine;
public class LibBehavior : MonoBehaviour
{
    [GenericSerializeReference(mode: GenericSerializeReferenceAttribute.Mode.Library)]
    public ILibInterface<int> Value { get; set; }
}
