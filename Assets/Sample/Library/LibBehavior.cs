using GenericSerializeReference;
using UnityEngine;
public class LibBehavior : MonoBehaviour
{
    [GenericSerializeReference(mode: GenericSerializeReferenceAttribute.Mode.InterfaceOnly)]
    public ILibInterface<int> Value { get; set; }
}
