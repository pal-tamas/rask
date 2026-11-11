using Rask.Site.E2E.Tests.Infrastructure;
using Rask.TestFiles;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The site rask.sh serves is a Release publish, and carries no WebAssembly debugger (see
///     <see cref="DebuggerFreePublish" />).
/// </summary>
public sealed class PublishedSiteDebuggerTests(WasmExampleAppFixture app)
{
    [Fact]
    public void The_published_site_carries_no_webassembly_debugger()
    {
        var publishDir = Path.GetDirectoryName(app.PublishedWwwroot)!;

        var found = DebuggerFreePublish.DebuggerFilesIn(publishDir);

        Assert.True(Directory.Exists(Path.Combine(app.PublishedWwwroot, "_framework")), $"no _framework under {app.PublishedWwwroot}.");
        Assert.Empty(found);
    }
}
