namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Boots the Server showcase for the Native + Server smoke. It IS the ordinary
///     <c>Rask.Example.Server</c> host — in Native + Server mode a device's WebView just points at a remote
///     Rask Server and speaks the normal <c>rask.js</c>/WebSocket protocol, so there's nothing native to
///     serve here. Runs on its own port so it never clashes with <see cref="ServerExampleAppFixture" />
///     (its own shard) when the suite runs locally.
/// </summary>
public sealed class NativeServerSmokeAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Server";
    protected override int Port => 5095;

    // Serve the published host so the WebView-hosted shell gets production static-asset serving, matching
    // what a real Native + Server deployment points at.
    protected override bool RunPublished => true;
}
