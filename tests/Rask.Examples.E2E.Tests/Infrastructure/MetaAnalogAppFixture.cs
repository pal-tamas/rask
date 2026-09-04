namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     The Analog meta framework sample: Kestrel on the public port, Analog's own Nitro server
///     supervised beside it on loopback.
/// </summary>
/// <remarks>
///     Run from source rather than published, exactly as the other five are: what this journey needs
///     is what <c>dotnet build</c> already produced, next to the host, where the supervisor looks for
///     it. Two processes have to come up rather than one, and the first requests are answered 503 with
///     a Retry-After until the child is listening; the fixture polls through that.
/// </remarks>
public sealed class MetaAnalogAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Meta.Analog";
}
