namespace Rask.Core.Tests.Globalization;

// RaskCulture.IsEnabled and the RaskStrings sources are process-wide: the classes that turn them on and off
// take turns.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CultureCollection
{
    public const string Name = "Culture";
}
