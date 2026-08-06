namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract for the lagging-frame guards the change dispatch arms. Structural
///     assertions over the shipped <c>.js</c>, for the same reason as
///     <see cref="ShutdownClientContractTests" />: the host runtimes are IIFEs that boot against a live
///     document and still carry unsubstituted <c>@@RASK_*@@</c> splice markers in the Resources copy, so
///     they cannot be executed in Node. The behaviour of the guards themselves is covered by
///     MorphValueGuardTests / MorphCheckedGuardTests / MorphSelectedGuardTests, which do run the shared
///     modules; what is pinned here is that the hosts actually ARM them, which nothing else would catch.
/// </summary>
public class FormGuardClientContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string ServerJs => Read("src", "Rask.Server", "Resources", "rask.js");
    private static string WasmJs => Read("src", "Rask.Wasm", "Resources", "rask.wasm.js");
    private static string MorphJs => Read("src", "Rask.Core", "Resources", "rask-morph.js");

    [Fact]
    public void Both_hosts_arm_the_guards_through_the_shared_recorder()
    {
        // The invariant #588 came from. Each host carried its own hand-copied block of "record what a
        // lagging frame would have to carry" — and the two copies covered `value` and `checked` while
        // neither covered `selected`, so a <select> had no guard at all. One recorder, called from both,
        // is what stops that recurring: a control added to it is covered everywhere at once.
        foreach (var js in new[] { ServerJs, WasmJs })
        {
            var dispatch = ChangeDispatch(js);
            Assert.Contains("raskNotePendingFormState(t);", dispatch, StringComparison.Ordinal);

            // And it may not go back to noting a guard inline, which is how the two copies drifted
            // apart. (The Server's redeploy restore arms raskNotePendingChecked separately and
            // legitimately — see ShutdownClientContractTests — hence scoping this to the dispatch.)
            Assert.DoesNotContain("raskNotePendingValue(", dispatch, StringComparison.Ordinal);
            Assert.DoesNotContain("raskNotePendingChecked(", dispatch, StringComparison.Ordinal);
            Assert.DoesNotContain("raskNotePendingSelected(", dispatch, StringComparison.Ordinal);
        }
    }

    // The change listener, up to the message it sends — the window in which a guard has to be armed,
    // because after the send the server's answer is already on its way.
    private static string ChangeDispatch(string js)
    {
        var listener = js[js.IndexOf("addEventListener(\"change\"", StringComparison.Ordinal)..];
        var end = listener.IndexOf("data-rask-on-change\"), type: \"change\"", StringComparison.Ordinal);
        Assert.True(end > 0, "could not find the change dispatch's send in this client");
        return listener[..end];
    }

    [Fact]
    public void The_recorder_covers_all_three_form_properties()
    {
        // syncFormProperty mirrors exactly three attributes onto their IDL properties (value, checked,
        // selected). Each needs a guard, or a re-render the server computed before the user's edit
        // overwrites it — the desync that produced #588 for the one that was missing.
        var js = MorphJs;
        var fn = js[js.IndexOf("function raskNotePendingFormState", StringComparison.Ordinal)..];
        var body = fn[..fn.IndexOf("\n}", StringComparison.Ordinal)];

        Assert.Contains("raskNotePendingValue(", body, StringComparison.Ordinal);
        Assert.Contains("raskNotePendingChecked(", body, StringComparison.Ordinal);
        Assert.Contains("raskNotePendingSelected(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_guarded_property_is_consulted_where_the_diff_applies_it()
    {
        // The other half: recording is useless unless the apply path asks. syncFormProperty is the diff
        // codec's single write point for all three, and `selected` was the branch that never asked.
        var js = Read("src", "Rask.Core", "Resources", "rask-dom.js");
        var fn = js[js.IndexOf("function syncFormProperty", StringComparison.Ordinal)..];
        var body = fn[..fn.IndexOf("\n}", StringComparison.Ordinal)];

        Assert.Contains("raskShouldSuppressValue(", body, StringComparison.Ordinal);
        Assert.Contains("raskShouldSuppressChecked(", body, StringComparison.Ordinal);
        Assert.Contains("raskShouldSuppressSelected(", body, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([_repoRoot, .. parts]));

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}
