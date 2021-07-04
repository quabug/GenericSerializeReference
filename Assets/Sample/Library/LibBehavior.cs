using GenericSerializeReference;
using GenericSerializeReference.Library;
using UnityEngine;

[assembly: GenericSerializeReferenceLoggerAttribute(LogLevel.Debug)]
public class LibBehavior : MonoBehaviour
{
    private interface Value_IBase {}
    [GenericSerializeReferenceInAssemblyCSharp(typeof(Value_IBase))]
    public ILibInterface<int> Value { get; set; }
}
