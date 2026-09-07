namespace Rask.Examples.E2E.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class WasmExampleCollection
    : ICollectionFixture<WasmExampleAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "WasmExample";
}

// Playwright with no application behind it: for journeys that drive a page they build themselves
// (data: URLs, hand-written HTML) rather than one a host serves.
[CollectionDefinition(Name)]
public sealed class BrowserOnlyCollection : ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "BrowserOnly";
}
