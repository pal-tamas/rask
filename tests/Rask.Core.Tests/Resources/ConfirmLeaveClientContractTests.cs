namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract for the unsaved-changes guard (<c>Form.Model(m).ConfirmLeave("…")</c>): every
///     way out of a page asks <c>rask-leave.ts</c>, and the two that live in the hosts ask through ONE shared
///     function.
/// </summary>
/// <remarks>
///     Structural, like its neighbours here: the host entry points boot a transport against a live document.
///     The behaviour — cancel keeps the page and what was typed, a save ends the guard, Back is put back — is
///     <c>ConfirmLeaveHookTests</c> in <c>Rask.Server.E2E.Tests</c>, which also refuses a reload. Closing the
///     tab is the one exit no test can start and then observe, so that the listener is there only while a
///     form is unsaved is pinned here as well.
/// </remarks>
public class ConfirmLeaveClientContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string LeaveJs => Read("src", "Rask.Core", "Resources", "rask-leave.ts");

    [Theory]
    [InlineData("src/Rask.Server/Resources/rask.ts")]
    [InlineData("src/Rask.Wasm/Resources/rask.wasm.ts")]
    public void A_navigation_the_reader_starts_asks_the_guard_before_anything_is_sent(string host)
    {
        var js = Read(host.Split('/'));

        var navigate = js[js.IndexOf("function navigate(url: URL, replace: boolean): void {", StringComparison.Ordinal)..];
        var asked = navigate.IndexOf("if (!mayLeave(() => navigate(url, replace))) return;", StringComparison.Ordinal);
        var sent = navigate.IndexOf("send(", StringComparison.Ordinal);

        Assert.Contains("import { mayLeave } from \"../../Rask.Core/Resources/rask-owned.js\";", js, StringComparison.Ordinal);
        Assert.True(asked >= 0 && asked < sent, $"{host} sends a navigation without asking the guard first");
        Assert.True(js.Contains("sendTypedFirst(payload);", StringComparison.Ordinal), $"{host} no longer sends typed values ahead of what it sends");
        Assert.False(js.Contains("flushInputsNow()", StringComparison.Ordinal), $"{host} flushes typed values on its own, where the reader may not yet have chosen to stay");
    }

    [Fact]
    public void The_guard_answers_the_hosts_through_the_seam_and_hears_back_and_forward_ahead_of_them()
    {
        var js = LeaveJs;

        var answers = js.Contains("seam.leave = ask;", StringComparison.Ordinal);
        var takesThePlace = js.Contains("const place = seam.reserved.popstate;", StringComparison.Ordinal);

        Assert.True(answers, "rask-leave.ts no longer answers mayLeave()");
        Assert.True(takesThePlace, "rask-leave.ts no longer takes the place kept ahead of the host's popstate listener");
        Assert.Contains("e.stopImmediatePropagation();", js, StringComparison.Ordinal);
    }

    [Fact]
    public void Closing_the_tab_is_guarded_only_while_a_form_holds_unsaved_edits()
    {
        var js = LeaveJs;

        var handler = js[js.IndexOf("const unloading = function", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("};", StringComparison.Ordinal)];

        Assert.Contains("window.addEventListener(\"beforeunload\", unloading);", js, StringComparison.Ordinal);
        Assert.Contains("window.removeEventListener(\"beforeunload\", unloading);", handler, StringComparison.Ordinal);
        Assert.Contains("e.preventDefault();", handler, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([_repoRoot, .. parts]));

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Rask.slnx was not found above the test directory.");
    }
}
