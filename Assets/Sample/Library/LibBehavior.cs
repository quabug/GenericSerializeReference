using GenericSerializeReference;
using UnityEngine;

[assembly: GenericSerializeReferenceLoggerAttribute(LogLevel.Debug)]
public class LibBehavior : MonoBehaviour
{
    [GenericSerializeReference(mode: GenericSerializeReferenceAttribute.Mode.InterfaceOnly)]
    public ILibInterface<int> Value { get; set; }
}
