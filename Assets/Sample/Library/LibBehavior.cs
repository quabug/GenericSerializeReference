using GenericSerializeReference;
using UnityEngine;

[assembly: GenericSerializeReferenceLoggerAttribute(LogLevel.Debug)]
public class LibBehavior : MonoBehaviour
{
    [GenericSerializeReference(mode: GenerateMode.AssemblyCSharp)]
    public ILibInterface<int> Int { get; set; }

    [GenericSerializeReference(mode: GenerateMode.AssemblyCSharp)]
    public ILibInterface<float> Float { get; set; }
}
