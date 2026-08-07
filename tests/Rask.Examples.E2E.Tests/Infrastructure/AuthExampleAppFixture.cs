namespace Rask.Examples.E2E.Tests.Infrastructure;

// Boots the minimal cookie-auth sample (samples/Rask.Example.Auth) for the login round-trip E2E.
public sealed class AuthExampleAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Auth";
}
