namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     The Next.js meta framework sample: Kestrel on the public port, Next's standalone server
///     supervised beside it on loopback.
/// </summary>
/// <remarks>
///     The other server shape on this lane. Nuxt, SolidStart and TanStack build through Nitro to
///     <c>.output/server/index.mjs</c>; Next writes <c>.next/standalone/server.js</c> and deliberately
///     omits <c>public</c> and <c>.next/static</c> from it, assuming a CDN in front. Here Kestrel is
///     the thing in front and serves them itself, which is the arrangement this journey exercises.
/// </remarks>
public sealed class MetaNextAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Meta.Next";
}
