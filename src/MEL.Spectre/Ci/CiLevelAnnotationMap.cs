using Microsoft.Extensions.Logging;

namespace MEL.Spectre.Ci;

internal sealed class CiLevelAnnotationMap
{
    private readonly CiAnnotation?[] _annotations = new CiAnnotation?[(int)LogLevel.None + 1];

    public CiLevelAnnotationMap(IReadOnlyDictionary<LogLevel, CiAnnotation?> annotations)
    {
        foreach (var (level, annotation) in annotations)
        {
            _annotations[(int)level] = annotation;
        }
    }

    public CiAnnotation? Get(LogLevel level)
    {
        var index = (int)level;
        return (uint)index < (uint)_annotations.Length ? _annotations[index] : null;
    }
}
