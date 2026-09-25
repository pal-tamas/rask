namespace Rask.Generators.ScopedScripts;

internal sealed class TsLiteral(TsLiteralKind kind) : ITsType
{
    public TsLiteralKind Kind { get; } = kind;
}
