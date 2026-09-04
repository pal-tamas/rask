namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     The SvelteKit meta framework sample: Kestrel on the public port, SvelteKit's own Node server
///     supervised beside it on loopback.
/// </summary>
/// <remarks>
///     Run from source rather than published: what this journey needs is exactly what
///     <c>dotnet build</c> already produced — <c>client/build/index.js</c>, next to the host, where the supervisor
///     looks for it. Two processes have to come up rather than one, and the first requests are
///     answered 503 with a Retry-After until the child is listening; the fixture polls through that.
/// </remarks>
public sealed class MetaSvelteKitAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Meta.SvelteKit";
}
