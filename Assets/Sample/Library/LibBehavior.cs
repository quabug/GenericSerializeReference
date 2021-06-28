using GenericSerializeReference;
using UnityEngine;
public class LibBehavior : MonoBehaviour
{
    [GenericSerializeReference]
    public ILibInterface<int> Value { get; set; }
}
