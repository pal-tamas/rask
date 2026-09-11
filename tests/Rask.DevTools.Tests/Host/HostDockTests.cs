using Rask.TestSupport;

namespace Rask.DevTools.Tests.Host;

/// <summary>
///     The devtools pill and drawer, driven in Node against a stub DOM: where they mount, how they open, the shortcut,
///     and the remembered dock side.
/// </summary>
public sealed class HostDockTests
{
    [Fact]
    public void The_dock_mounts_opens_docks_and_answers_the_shortcut()
    {
        // No node on PATH — the JS-driven check cannot run. Deliberately not a failure: node is not required to build
        // or test Rask, and the devtools E2E drives the same dock in a real browser.
        var result = NodeFixture.Run("HostDockFixture");
        if (result is null)
        {
            return;
        }

        var r = result.Value;
        bool Bool(string name) => r.GetProperty(name).GetBoolean();
        int Int(string name) => r.GetProperty(name).GetInt32();
        string? Str(string name) => r.GetProperty(name).GetString();

        // 1. Mounted after <body> (so body keeps its slot in the diff's paths), kept by the morph, and closed.
        Assert.True(Bool("mountedAfterBody"), "the dock is not the next child of <html> after <body>");
        Assert.True(Bool("managed"));
        Assert.True(Bool("closedAtStart"));
        Assert.Equal(0, Int("framesAtStart"));
        Assert.Equal("bottom", Str("sideAtStart"));
        Assert.True(Bool("shortcutListenerIsCapture"), "the shortcut must run before the runtime's key forwarding");

        // 2. The pill opens the drawer and frames the panel.
        Assert.True(Bool("openAfterPill"));
        Assert.Equal(1, Int("frameCount"));
        Assert.Equal("/_rask-devtools/?inspect=s1", Str("frameSrc"));

        // 3. The shortcut closes, never reaches the app, and gives focus back; Ctrl+D alone is the browser's.
        Assert.True(Bool("closedByShortcut"));
        Assert.True(Bool("shortcutSwallowed"));
        Assert.True(Bool("focusReturnedToPill"));
        Assert.True(Bool("plainIgnored"));
        Assert.True(Bool("reopenedByMac"));
        Assert.Equal(1, Int("framesAfterReopen"));

        // 4. The side is remembered.
        Assert.Equal("right", Str("storedSide"));
        Assert.Equal("right", Str("drawerSide"));
        Assert.Equal("true", Str("rightPressed"));
        Assert.Equal("right", Str("sideOnNextInstall"));

        // 5. Blocked storage costs the memory, not the dock.
        Assert.False(Bool("blockedThrew"));
        Assert.Equal("bottom,right", Str("blockedSide"));

        // 6. Nothing to frame, no frame.
        Assert.Equal(0, Int("framesWithoutPanel"));
    }
}
