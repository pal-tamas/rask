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

    [Fact]
    public void Every_host_reports_the_change_value_through_the_shared_helper()
    {
        // #595's half of the same invariant. Each host computed the change frame's `value` itself, and
        // both got <select multiple> wrong in the same way — `select.value` is the FIRST selected
        // option, so picking three reported one. The fix has to live in one place or the next control
        // with a non-obvious "current value" repeats it.
        foreach (var js in new[] { ServerJs, WasmJs })
        {
            var dispatch = ChangeDispatch(js);
            Assert.Contains("raskChangeFrameValue(", dispatch, StringComparison.Ordinal);
            Assert.Contains("raskChangeFrameValues(", dispatch, StringComparison.Ordinal);

            // And no host may go back to reading the property directly, which is what it did before.
            Assert.DoesNotContain("t.checked ? \"true\" : \"false\"", dispatch, StringComparison.Ordinal);
            Assert.DoesNotContain("el.checked ? \"true\" : \"false\"", dispatch, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_shared_helper_reports_the_whole_selection_of_a_multiple_select()
    {
        var js = MorphJs;
        var fn = js[js.IndexOf("function raskChangeFrameValues", StringComparison.Ordinal)..];
        var body = fn[..fn.IndexOf("\n}", StringComparison.Ordinal)];

        // The point of the function: every picked option, not el.value.
        Assert.Contains("selectedOptions", body, StringComparison.Ordinal);
        Assert.Contains("multiple", body, StringComparison.Ordinal);
    }

    // The change listener, up to the message it sends — the window in which a guard has to be armed,
    // because after the send the server's answer is already on its way.
    // The whole body of the `change` listener. Brace-matched rather than cut at a marker: the frame is
    // now assembled over several statements instead of inline in the send call, and every marker that
    // would delimit it (the send's argument shape, `type: "change"`) pins a spelling rather than the
    // invariant — and the listener's file-upload branch has a send of its own that comes first.
    private static string ChangeDispatch(string js)
    {
        var at = js.IndexOf("addEventListener(\"change\"", StringComparison.Ordinal);
        Assert.True(at > 0, "could not find the change listener in this client");

        var open = js.IndexOf('{', at);
        Assert.True(open > 0, "could not find the change listener's body");

        var depth = 0;
        for (var i = open; i < js.Length; i++)
        {
            if (js[i] == '{')
            {
                depth++;
            }
            else if (js[i] == '}' && --depth == 0)
            {
                return js[open..i];
            }
        }

        Assert.Fail("the change listener's body is unbalanced");
        return string.Empty;
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
