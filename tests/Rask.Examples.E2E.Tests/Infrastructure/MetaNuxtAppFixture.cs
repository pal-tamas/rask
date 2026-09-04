namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     The Nuxt meta framework sample: Kestrel on the public port, Nuxt's own Node server supervised
///     beside it on loopback.
/// </summary>
/// <remarks>
///     <para>
///         Run from source rather than published, because what this journey needs is exactly what
///         <c>dotnet build</c> already produced — <c>client/.output/server/index.mjs</c>, next to the
///         host, where the supervisor looks for it. Publishing would copy the same tree somewhere else
///         and prove nothing extra; the publish wiring has its own test in
///         <c>Rask.Meta.Hosting.Tests</c>.
///     </para>
///     <para>
///         Two processes have to come up, not one, so readiness takes longer than a plain host: the
///         supervisor starts node only after Kestrel binds, and the first request is answered 503 with
///         a Retry-After until the child is listening. That is the designed behaviour, and the fixture
///         polls through it.
///     </para>
/// </remarks>
public sealed class MetaNuxtAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Meta.Nuxt";
}
