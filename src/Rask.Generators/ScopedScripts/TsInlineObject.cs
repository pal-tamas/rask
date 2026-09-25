namespace Rask.Generators.ScopedScripts;

/// <summary>An object shape written inline in a signature, which has no name to give its record.</summary>
internal sealed class TsInlineObject(TsObject shape) : ITsType
{
    public TsObject Shape { get; } = shape;
}
