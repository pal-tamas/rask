using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Rask.Core.Authentication;
using Rask.Core.Caching;
using Rask.Core.Routing;

#pragma warning disable RASK014 // StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

// Cached.Name(…)[Widget]: the widget's markup is drawn once, stored, and handed to every later render that keys the
// same — with the widget never mounted. Each "visit" below is a fresh root over the same store, as a later request is.
public sealed partial class CachedTests : global::Rask.Core.RaskMarkup
{
    private readonly FakeTime _time = new();
    private readonly MemoryFragmentStore _store;
    private readonly CachedProbeCounts _counts = new();

    public CachedTests() => _store = new MemoryFragmentStore(new CachedOptions(), _time);

    [Fact]
    public void A_miss_draws_the_wrapped_component_and_a_later_visit_is_served_its_markup_without_mounting_it()
    {
        var visit = Visitor();

        var first = Render(() => Cached.Name("sales")[CachedProbe.Counts(_counts)], visit);
        var second = Render(() => Cached.Name("sales")[CachedProbe.Counts(_counts)], visit);

        Assert.Equal("<p>sales 1</p>", first);
        Assert.Equal(first, second);
        Assert.Equal(1, _counts.Mounts);
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public void Two_fragments_of_different_names_are_kept_apart()
    {
        var visit = Visitor();

        Render(() => Cached.Name("sales")[CachedProbe.Counts(_counts)], visit);
        Render(() => Cached.Name("stock")[CachedProbe.Counts(_counts)], visit);

        Assert.Equal(2, _counts.Mounts);
    }

    [Fact]
    public void A_query_value_it_varies_by_keys_a_fragment_of_its_own_and_one_it_does_not_vary_by_is_ignored()
    {
        Render(() => Cached.Name("orders").VaryByQuery("status")[CachedProbe.Counts(_counts)], Visitor(query: "status=open&page=1"));

        Render(() => Cached.Name("orders").VaryByQuery("status")[CachedProbe.Counts(_counts)], Visitor(query: "status=open&page=2"));
        Render(() => Cached.Name("orders").VaryByQuery("status")[CachedProbe.Counts(_counts)], Visitor(query: "status=shipped"));

        Assert.Equal(2, _counts.Mounts);
    }

    [Fact]
    public void Varying_by_route_keys_each_path_apart()
    {
        Render(() => Cached.Name("crumbs").VaryByRoute()[CachedProbe.Counts(_counts)], Visitor(path: "/orders/1"));

        Render(() => Cached.Name("crumbs").VaryByRoute()[CachedProbe.Counts(_counts)], Visitor(path: "/orders/1"));
        Render(() => Cached.Name("crumbs").VaryByRoute()[CachedProbe.Counts(_counts)], Visitor(path: "/orders/2"));

        Assert.Equal(2, _counts.Mounts);
    }

    [Fact]
    public void A_header_it_varies_by_keys_apart_and_with_no_request_to_read_it_is_drawn_live_and_never_stored()
    {
        Render(() => Cached.Name("ua").VaryByHeader("User-Agent")[CachedProbe.Counts(_counts)], Visitor(userAgent: "Firefox"));

        Render(() => Cached.Name("ua").VaryByHeader("User-Agent")[CachedProbe.Counts(_counts)], Visitor(userAgent: "Firefox"));
        Render(() => Cached.Name("ua").VaryByHeader("User-Agent")[CachedProbe.Counts(_counts)], Visitor(userAgent: "Safari"));
        Render(() => Cached.Name("ua").VaryByHeader("User-Agent")[CachedProbe.Counts(_counts)], Visitor(noRequest: true));

        Assert.Equal(3, _counts.Mounts);
        Assert.Equal(2, _store.Count);
    }

    [Fact]
    public void A_value_of_the_apps_own_keys_apart()
    {
        Render(() => Cached.Name("product").VaryBy(7)[CachedProbe.Counts(_counts)], Visitor());

        Render(() => Cached.Name("product").VaryBy(7)[CachedProbe.Counts(_counts)], Visitor());
        Render(() => Cached.Name("product").VaryBy(8)[CachedProbe.Counts(_counts)], Visitor());

        Assert.Equal(2, _counts.Mounts);
    }

    [Fact]
    public void The_culture_is_always_in_the_key()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Render(() => Cached.Name("totals")[CachedProbe.Counts(_counts)], Visitor());

            CultureInfo.CurrentCulture = new CultureInfo("hu-HU");
            Render(() => Cached.Name("totals")[CachedProbe.Counts(_counts)], Visitor());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }

        Assert.Equal(2, _counts.Mounts);
    }

    [Fact]
    public void One_users_markup_is_never_served_to_another_and_never_to_a_visitor_signed_out()
    {
        var ann = Render(() => Cached.Name("inbox")[CachedProbe.Counts(_counts)], Visitor(user: "ann"));

        var bob = Render(() => Cached.Name("inbox")[CachedProbe.Counts(_counts)], Visitor(user: "bob"));
        var guest = Render(() => Cached.Name("inbox")[CachedProbe.Counts(_counts)], Visitor());

        Assert.Equal(3, _counts.Mounts);
        Assert.NotEqual(ann, bob);
        Assert.NotEqual(ann, guest);
    }

    [Fact]
    public void Across_users_one_copy_serves_every_user_of_the_tenant()
    {
        Render(() => Cached.Name("filters").AcrossUsers()[CachedProbe.Counts(_counts)], Visitor(user: "ann", tenant: "acme"));

        Render(() => Cached.Name("filters").AcrossUsers()[CachedProbe.Counts(_counts)], Visitor(user: "bob", tenant: "acme"));

        Assert.Equal(1, _counts.Mounts);
    }

    [Fact]
    public void Across_users_still_never_crosses_a_tenant()
    {
        Render(() => Cached.Name("filters").AcrossUsers()[CachedProbe.Counts(_counts)], Visitor(user: "ann", tenant: "acme"));

        Render(() => Cached.Name("filters").AcrossUsers()[CachedProbe.Counts(_counts)], Visitor(user: "ann", tenant: "globex"));

        Assert.Equal(2, _counts.Mounts);
    }

    [Fact]
    public void A_signed_in_user_with_no_id_to_tell_them_apart_is_drawn_live_and_never_stored()
    {
        Render(() => Cached.Name("inbox")[CachedProbe.Counts(_counts)], Visitor(user: string.Empty));

        Render(() => Cached.Name("inbox")[CachedProbe.Counts(_counts)], Visitor(user: string.Empty));

        Assert.Equal(2, _counts.Mounts);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public void It_is_drawn_afresh_once_its_lifetime_has_passed()
    {
        Render(() => Cached.Name("sales").For(TimeSpan.FromMinutes(10))[CachedProbe.Counts(_counts)], Visitor());

        _time.Advance(TimeSpan.FromMinutes(9));
        Render(() => Cached.Name("sales").For(TimeSpan.FromMinutes(10))[CachedProbe.Counts(_counts)], Visitor());
        _time.Advance(TimeSpan.FromMinutes(2));
        Render(() => Cached.Name("sales").For(TimeSpan.FromMinutes(10))[CachedProbe.Counts(_counts)], Visitor());

        Assert.Equal(2, _counts.Mounts);
    }

    [Fact]
    public void A_sliding_fragment_lives_while_it_is_read_and_lapses_once_it_is_not()
    {
        Render(() => Cached.Name("sales").Sliding(TimeSpan.FromMinutes(2))[CachedProbe.Counts(_counts)], Visitor());

        for (var read = 0; read < 3; read++)
        {
            _time.Advance(TimeSpan.FromMinutes(1.5));
            Render(() => Cached.Name("sales").Sliding(TimeSpan.FromMinutes(2))[CachedProbe.Counts(_counts)], Visitor());
        }

        var stillOne = _counts.Mounts;
        _time.Advance(TimeSpan.FromMinutes(3));
        Render(() => Cached.Name("sales").Sliding(TimeSpan.FromMinutes(2))[CachedProbe.Counts(_counts)], Visitor());

        Assert.Equal(1, stillOne);
        Assert.Equal(2, _counts.Mounts);
    }

    [Fact]
    public async Task Forgetting_a_tag_or_a_name_draws_its_fragments_afresh()
    {
        Render(() => Cached.Name("price-list").Tags("catalog")[CachedProbe.Counts(_counts)], Visitor());
        Render(() => Cached.Name("menu").Tags("catalog")[CachedProbe.Counts(_counts)], Visitor());
        Render(() => Cached.Name("footer")[CachedProbe.Counts(_counts)], Visitor());

        await _store.Forget(FragmentKeys.Tag("catalog"), TestContext.Current.CancellationToken);
        var afterTag = _store.Count;
        await _store.Forget(FragmentKeys.Tag("footer"), TestContext.Current.CancellationToken);

        Assert.Equal(1, afterTag);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task Cached_fragments_forget_reaches_the_store_of_the_work_in_flight()
    {
        var services = Visitor();
        Render(() => Cached.Name("menu").Tags("catalog")[CachedProbe.Counts(_counts)], services);

        using (Ambient.Enter(services))
        {
            await CachedFragments.Forget("catalog", TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public void Past_the_size_limit_the_least_recently_read_fragment_goes_first()
    {
        // Each probe's markup is 14 chars, 28 bytes: room for two, not three.
        var store = new MemoryFragmentStore(new CachedOptions { SizeLimit = 70 }, _time);
        var visit = Visitor(store: store);
        Render(() => Cached.Name("a")[CachedProbe.Counts(_counts)], visit);
        _time.Advance(TimeSpan.FromSeconds(1));
        Render(() => Cached.Name("b")[CachedProbe.Counts(_counts)], visit);
        _time.Advance(TimeSpan.FromSeconds(1));
        Render(() => Cached.Name("a")[CachedProbe.Counts(_counts)], visit);

        _time.Advance(TimeSpan.FromSeconds(1));
        Render(() => Cached.Name("c")[CachedProbe.Counts(_counts)], visit);
        Render(() => Cached.Name("a")[CachedProbe.Counts(_counts)], visit);
        var beforeB = _counts.Mounts;
        Render(() => Cached.Name("b")[CachedProbe.Counts(_counts)], visit);

        Assert.Equal(3, beforeB);
        Assert.Equal(4, _counts.Mounts);
        Assert.True(store.Size <= 70);
    }

    [Fact]
    public void A_fragment_larger_than_the_entry_limit_is_drawn_live_every_time()
    {
        var store = new MemoryFragmentStore(new CachedOptions { MaxEntrySize = 10 }, _time);
        var visit = Visitor(store: store);

        Render(() => Cached.Name("big")[CachedProbe.Counts(_counts)], visit);
        Render(() => Cached.Name("big")[CachedProbe.Counts(_counts)], visit);

        Assert.Equal(2, _counts.Mounts);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void An_event_handler_in_a_wrapped_component_is_refused_and_names_the_fragment()
    {
        var visit = Visitor();

        var refused = Assert.ThrowsAny<Exception>(() => Render(() => Cached.Name("cart")[CachedClickable], visit));

        var message = Innermost(refused).Message;
        Assert.Contains("Cached.Name(\"cart\") holds an event handler (data-rask-on-click)", message, StringComparison.Ordinal);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public void A_bound_input_in_a_wrapped_component_is_refused()
    {
        var visit = Visitor();

        var refused = Assert.ThrowsAny<Exception>(() => Render(() => Cached.Name("form")[CachedBoundInput], visit));

        Assert.Contains("an event handler", Innermost(refused).ToString(), StringComparison.Ordinal);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task A_component_still_loading_is_not_stored_until_its_data_has_painted()
    {
        var gate = new TaskCompletionSource();
        var visit = Visitor();
        var page = new StubComponent(() => Cached.Name("slow")[CachedSlowProbe.Gate(gate.Task)]);

        var placeholder = page.RenderAsLiveRoot(visit);
        var storedWhileLoading = _store.Count;
        gate.SetResult();
        var loaded = await RenderOnceSettled(page, visit, "<p>loaded</p>");
        var later = Render(() => Cached.Name("slow")[CachedSlowProbe.Gate(gate.Task)], visit);

        Assert.Equal("<p>loading</p>", placeholder);
        Assert.Equal(0, storedWhileLoading);
        Assert.Equal("<p>loaded</p>", loaded);
        Assert.Equal("<p>loaded</p>", later);
    }

    // The hook's continuation comes back through its context on the pool; render until it has painted, or give up.
    private static async Task<string> RenderOnceSettled(StubComponent page, IServiceProvider services, string expected)
    {
        var html = page.RenderAsLiveRoot(services);
        for (var attempt = 0; attempt < 200 && html != expected; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            html = page.RenderAsLiveRoot(services);
        }

        return html;
    }

    [Fact]
    public void A_live_page_keeps_its_stored_markup_as_one_verbatim_frame_while_the_key_holds()
    {
        var visit = Visitor();
        Render(() => Cached.Name("sales")[CachedProbe.Counts(_counts)], visit);
        var page = new StubComponent(() => Div[Cached.Name("sales")[CachedProbe.Counts(_counts)]]);

        var first = page.RenderAsLiveRoot(visit);
        page.StateHasChanged();
        var again = page.RenderAsLiveRoot(visit);

        Assert.Equal("<div><p>sales 1</p></div>", first);
        Assert.Equal(first, again);
        Assert.Equal(1, _counts.Mounts);
    }

    [Fact]
    public void A_fragment_with_no_name_says_how_to_give_it_one()
    {
        var visit = Visitor();

        var refused = Assert.ThrowsAny<Exception>(() => Render(() => Cached.Name(" ")[CachedProbe.Counts(_counts)], visit));

        Assert.Contains("Cached.Name(\"sales-summary\")", Innermost(refused).Message, StringComparison.Ordinal);
    }

    private static Exception Innermost(Exception e) => e.InnerException is { } inner ? Innermost(inner) : e;

    private static string Render(Func<Component> page, IServiceProvider services) =>
        new StubComponent(page).RenderAsLiveRoot(services);

    private IServiceProvider Visitor(
        string? user = null,
        string? tenant = null,
        string path = "/",
        string? query = null,
        string? userAgent = null,
        bool noRequest = false,
        MemoryFragmentStore? store = null)
    {
        var route = new RouteState { Path = path };
        if (query is not null)
        {
            var values = query.Split('&').Select(p => p.Split('='))
                .ToDictionary(p => p[0], p => new StringValues(p[1]), StringComparer.OrdinalIgnoreCase);
            route.Query = new QueryCollection(values);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (userAgent is not null)
        {
            headers["User-Agent"] = userAgent;
        }

        return new ServiceCollection()
            .AddSingleton<IFragmentStore>(store ?? _store)
            .AddSingleton<IUserProvider>(new CachedVisitor(Principal(user, tenant)))
            .AddSingleton(route)
            .AddSingleton<IRequestHeaders>(new CachedHeaders(noRequest ? null : headers))
            .BuildServiceProvider();
    }

    private static ClaimsPrincipal Principal(string? user, string? tenant)
    {
        var claims = new List<Claim>();
        if (tenant is not null)
        {
            claims.Add(new Claim(FragmentKeys.TenantClaim, tenant));
        }

        if (user is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(claims));
        }

        if (user.Length > 0)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, user));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private sealed class CachedVisitor(ClaimsPrincipal principal) : IUserProvider
    {
        public ClaimsPrincipal Current { get; } = principal;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class CachedHeaders(Dictionary<string, string>? headers) : IRequestHeaders
    {
        public string? Header(string name) =>
            headers is null ? null : headers.TryGetValue(name, out var value) ? value : string.Empty;
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

/// <summary>How many times a <see cref="CachedProbe" /> mounted, one per test.</summary>
public sealed class CachedProbeCounts
{
    public int Mounts { get; set; }
}

/// <summary>Counts its mounts and draws which mount it is, so a stored copy is told from a fresh draw.</summary>
public sealed partial class CachedProbe : Component
{
    private int _mount;

    /// <summary>Where the mounts are counted.</summary>
    public CachedProbeCounts? Counts { get; set; }

    protected override Task OnMount()
    {
        if (Counts is { } counts)
        {
            _mount = ++counts.Mounts;
        }

        return Task.CompletedTask;
    }

    protected override Component? Render() => P[$"sales {_mount}"];
}

/// <summary>A button: what a cached fragment must refuse to store.</summary>
public sealed partial class CachedClickable : Component
{
    protected override Component? Render() => Button.OnClick(() => { })["Add to cart"];
}

/// <summary>A bound input: what a cached fragment must refuse to store.</summary>
public sealed partial class CachedBoundInput : Component
{
    private readonly Draft _draft = new();

    protected override Component? Render() => Input.Bind(() => _draft.Name);

    private sealed class Draft
    {
        public string Name { get; set; } = string.Empty;
    }
}

/// <summary>Draws "loading" until its gate opens, as a component reading its data in OnMount does.</summary>
public sealed partial class CachedSlowProbe : Component
{
    private bool _loaded;

    /// <summary>What it waits for.</summary>
    public Task? Gate { get; set; }

    protected override async Task OnMount()
    {
        if (Gate is { } gate)
        {
            await gate;
        }

        _loaded = true;
    }

    protected override Component? Render() => P[_loaded ? "loaded" : "loading"];
}
