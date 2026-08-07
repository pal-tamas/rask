namespace Rask.Examples.E2E.Tests.Infrastructure;

public sealed class ServerExampleAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Server";
    // The Server journey runs the slow-3G step, which needs production static-asset serving
    // (brotli-compressed, ETag/304-revalidated package _content). Run the published host.
    protected override bool RunPublished => true;
}
