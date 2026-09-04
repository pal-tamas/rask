namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     Every test class that calls <c>AddRaskMeta</c>, run one at a time.
/// </summary>
/// <remarks>
///     <c>AddRaskMeta</c> reads <c>RASK_META_DEV</c> from the environment, and the environment is
///     process-wide: a test that sets it to prove the seam would otherwise land in the middle of a
///     concurrent class's host, silently turning supervision off there. xUnit runs classes in a
///     collection sequentially, which is the cheap fix and the one the casebook keeps arriving at —
///     process-wide state is not something a parallel suite can share.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class MetaHostCollection
{
    public const string Name = "meta-host";
}
