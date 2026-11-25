using Rask.Core.Live;

namespace Rask.Storage.Tests;

/// <summary>The classes that move the process's path base, which run alone.</summary>
/// <remarks>
///     <c>LiveOptions.PathBase</c> is published through <c>AppContext</c>, process-wide, and every stored file's URL and
///     every mapped endpoint is read under it — so while a test has it moved, any other class's URL would carry it.
///     Parallelization is disabled outright, because it is every class building a URL these must not meet.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PathBaseCollection
{
    public const string Name = "path-base";
}

[Collection(PathBaseCollection.Name)]
public sealed class PathBaseTests
{
    [Fact]
    public async Task Urls_honour_the_path_base()
    {
        await using var harness = new StorageHarness();

        try
        {
            // Set on Rask.Core's side, read on Storage's through AppContext: this is what pins the two copies of the
            // data name together (RaskPathBase.DataName, LiveOptions.PathBaseDataName).
            LiveOptions.PathBase = "/appA";

            Assert.StartsWith("/appA/_rask/files/public/", harness.Files.Url(Guid.NewGuid()));
        }
        finally
        {
            LiveOptions.PathBase = string.Empty;
        }
    }
}
