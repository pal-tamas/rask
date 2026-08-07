namespace Rask.Examples.E2E.Tests.Infrastructure;

// Boots the JWT + Server sample (samples/Rask.Example.Auth.Jwt) for the login round-trip E2E.
public sealed class JwtServerAuthAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Auth.Jwt";
}
