namespace Rask.Examples.E2E.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class ServerExampleCollection
    : ICollectionFixture<ServerExampleAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "ServerExample";
}

[CollectionDefinition(Name)]
public sealed class WasmExampleCollection
    : ICollectionFixture<WasmExampleAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "WasmExample";
}

[CollectionDefinition(Name)]
public sealed class StandaloneWasmExampleCollection
    : ICollectionFixture<StandaloneWasmAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "StandaloneWasmExample";
}

[CollectionDefinition(Name)]
public sealed class SubPathWasmExampleCollection
    : ICollectionFixture<SubPathWasmAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "SubPathWasmExample";
}
