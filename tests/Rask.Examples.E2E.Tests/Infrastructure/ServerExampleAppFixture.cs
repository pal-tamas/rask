namespace Rask.Examples.E2E.Tests.Infrastructure;

public sealed class ServerExampleAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Server";
    protected override int Port => 5099;
}
