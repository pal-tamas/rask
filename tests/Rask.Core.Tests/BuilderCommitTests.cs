using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Html.Components;

#pragma warning disable RASK014 // the tests render the very instance they hand to the root

namespace Rask.Core.Tests;

// The deferred commit is what runs OnMount for a component an entry built. These pin the case it used
// to miss.
//
// A chain writes only what it names, and only a FOLDING prop goes through BuilderRuntime.Track. So a
// chain that sets nothing, or sets only a callback, or only Children, never marks the
// child prop-changed — and marking is what allocates the child's LiveState. The commit then read a null
// LiveState as "this child never reached GetOrCreate" and returned, so the lifecycle never ran.
//
// Every one of these renders through RenderAsLiveRoot with NO render handle, because that is the
// condition the guard mis-read: a live session allocates LiveState on every GetOrCreate'd child through
// RenderHandle, so a test that goes through a session passes either way. A server-rendered first paint
// does not, and neither does ToHtml.
internal sealed partial class CommitProbe : Component
{
    internal int Mounts;
    internal int PropsChanges;

    public string? Word { get; set; }

    public Action? OnPing { get; set; }

    protected override void OnMount() => Mounts++;

    protected override void OnPropsChanged() => PropsChanges++;

    protected override Component? Render() => Span[Word ?? "ok"];
}

// The shape at the centre of the bug: an entry, and not one folding prop named.
internal sealed partial class BareEntryHost : Component
{
    internal CommitProbe? Probe;

    protected override Component? Render() => Div[Probe = CommitProbe];
}

// A chain that names only a CALLBACK. Callbacks are deliberately outside the fold — a fresh
// closure every render must not read as a change — so this names a prop and still never calls Track.
internal sealed partial class CallbackOnlyEntryHost : Component
{
    internal CommitProbe? Probe;

    protected override Component? Render() => Div[Probe = CommitProbe.OnPing(() => { })];
}

// And a chain that names only Children, which is not a prop at all.
internal sealed partial class ChildrenOnlyEntryHost : Component
{
    internal CommitProbe? Probe;

    // The indexer hands back Component, so the probe is captured before it is indexed — same tree, and
    // the chain still names nothing but Children.
    protected override Component? Render() => Div[(Probe = CommitProbe)[Span["x"]]];
}

// The user-visible symptom, in the component that has it worst: Authorize wires its IUserProvider in
// OnMount, so a gate whose lifecycle never ran sees an anonymous principal and renders nothing at all.
internal sealed partial class GateHost : Component
{
    protected override Component? Render() => Div[Authorize[Span["CHILD"]]];
}

public class BuilderCommitTests
{
    private static IServiceProvider WithUser(string name)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "test");
        return new ServiceCollection()
            .AddSingleton<IUserProvider>(new SignedIn(new ClaimsPrincipal(identity)))
            .BuildServiceProvider();
    }

    [Fact]
    public void An_entry_that_names_no_prop_still_mounts()
    {
        var host = new BareEntryHost();
        host.RenderAsLiveRoot(RenderHarness.EmptyServices());

        Assert.Equal(1, host.Probe!.Mounts);
    }

    [Fact]
    public void An_entry_that_names_only_a_callback_still_mounts()
    {
        var host = new CallbackOnlyEntryHost();
        host.RenderAsLiveRoot(RenderHarness.EmptyServices());

        Assert.Equal(1, host.Probe!.Mounts);
    }

    [Fact]
    public void An_entry_that_names_only_children_still_mounts()
    {
        var host = new ChildrenOnlyEntryHost();
        host.RenderAsLiveRoot(RenderHarness.EmptyServices());

        Assert.Equal(1, host.Probe!.Mounts);
    }

    // Mounting is only half of it: the child has to be in the output too. A commit that ran the
    // lifecycle on a child nothing rendered would satisfy every assertion above.
    [Fact]
    public void An_entry_that_names_no_prop_renders_its_child()
    {
        var entry = new BareEntryHost();

        Assert.Equal(
            "<div><span>ok</span></div>",
            entry.RenderAsLiveRoot(RenderHarness.EmptyServices()));
    }

    // Mounting once is half of it: a second render must not mount again, which is what the commit's
    // "already initialised and nothing changed" short-circuit is for.
    [Fact]
    public void A_propless_entry_mounts_once_across_renders()
    {
        var host = new BareEntryHost();
        var services = RenderHarness.EmptyServices();

        host.RenderAsLiveRoot(services);
        host.RenderAsLiveRoot(services);
        host.RenderAsLiveRoot(services);

        Assert.Equal(1, host.Probe!.Mounts);
    }

    [Fact]
    public void An_auth_gate_built_by_an_entry_sees_the_signed_in_user()
    {
        var html = new GateHost().RenderAsLiveRoot(WithUser("alice"));

        Assert.Contains("CHILD", html, StringComparison.Ordinal);
    }

    private sealed class SignedIn(ClaimsPrincipal principal) : IUserProvider
    {
        public ClaimsPrincipal Current { get; } = principal;

        public bool IsLoading => false;

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }
}
