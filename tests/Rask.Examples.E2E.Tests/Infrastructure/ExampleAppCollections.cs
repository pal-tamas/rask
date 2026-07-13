namespace Rask.Examples.E2E.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class ServerExampleCollection
    : ICollectionFixture<ServerExampleAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "ServerExample";
}

[CollectionDefinition(Name)]
public sealed class EfCoreExampleCollection
    : ICollectionFixture<EfCoreExampleAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "EfCoreExample";
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
public sealed class SiteExampleCollection
    : ICollectionFixture<SiteWasmAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "SiteExample";
}

[CollectionDefinition(Name)]
public sealed class SubPathWasmExampleCollection
    : ICollectionFixture<SubPathWasmAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "SubPathWasmExample";
}

[CollectionDefinition(Name)]
public sealed class PlaygroundExampleCollection
    : ICollectionFixture<PlaygroundAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "PlaygroundExample";
}

[CollectionDefinition(Name)]
public sealed class AuthExampleCollection
    : ICollectionFixture<AuthExampleAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "AuthExample";
}

[CollectionDefinition(Name)]
public sealed class JwtServerAuthExampleCollection
    : ICollectionFixture<JwtServerAuthAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "JwtServerAuthExample";
}

[CollectionDefinition(Name)]
public sealed class WasmCookieAuthExampleCollection
    : ICollectionFixture<WasmCookieAuthAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "WasmCookieAuthExample";
}

[CollectionDefinition(Name)]
public sealed class WasmJwtAuthExampleCollection
    : ICollectionFixture<WasmJwtAuthAppFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "WasmJwtAuthExample";
}

// Native on-device E2E lives in tests/Rask.Native.Appium.Tests (Appium drives the real app on an
// emulator/simulator) — there is no headless native collection here.
