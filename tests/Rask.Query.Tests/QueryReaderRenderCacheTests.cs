using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Cqrs;
using Rask.Testing;

namespace Rask.Querying.Tests;

/// <summary>
///     A component that reads a query stays in the render cache: a walk something else asked for serves its cached
///     output, and the query itself re-renders it whenever what it shows moves.
/// </summary>
public class QueryReaderRenderCacheTests
{
    private static readonly QueryOptions Fresh = new() { StaleTime = TimeSpan.FromHours(1) };

    private static (ServiceProvider Services, CountingDispatcher Dispatcher) Session()
    {
        var dispatcher = new CountingDispatcher();
        var services = new ServiceCollection()
            .AddSingleton<IDispatcher>(dispatcher)
            .AddRaskQuery()
            .BuildServiceProvider();
        return (services, dispatcher);
    }

    [Fact]
    public async Task A_page_reading_a_query_is_not_rendered_again_by_a_child_rendering_itself()
    {
        var (services, _) = Session();
        var page = new OrdersPage(Fresh);
        var rendered = Page.Render(page, services);
        rendered.Shows("orders:first");
        var before = page.Renders;

        await rendered.Click("Tick");

        rendered.Shows("ticks:1");
        Assert.Equal(before, page.Renders);
    }

    [Fact]
    public void Data_arriving_after_the_first_render_reaches_the_page_that_read_it()
    {
        var (services, dispatcher) = Session();
        dispatcher.Block();
        var rendered = Page.Render(new OrdersPage(Fresh), services);
        rendered.Shows("orders:loading");

        dispatcher.Release();

        rendered.Shows("orders:first");
    }

    [Fact]
    public void Data_arriving_after_the_first_render_reaches_a_child_that_read_it()
    {
        var (services, dispatcher) = Session();
        dispatcher.Block();
        var rendered = Page.Render(new OrdersPage(Fresh), services);
        rendered.Shows("child:loading");

        dispatcher.Release();

        rendered.Shows("child:first");
    }

    [Fact]
    public void An_invalidation_refetches_and_shows_the_new_data_in_every_reader()
    {
        var (services, dispatcher) = Session();
        var rendered = Page.Render(new OrdersPage(Fresh), services);
        rendered.Shows("orders:first");

        dispatcher.Result = "second";
        services.GetRequiredService<IQueryClient>().Invalidate<GetOrders>();

        rendered.Shows("orders:second");
        rendered.Shows("child:second");
    }

    [Fact]
    public async Task A_reload_from_another_component_shows_the_fetch_and_then_its_result()
    {
        var (services, dispatcher) = Session();
        var rendered = Page.Render(new OrdersPage(Fresh), services);
        rendered.Shows("child:first");
        dispatcher.Block();
        dispatcher.Result = "reloaded";

        await rendered.Click("Reload");

        rendered.Shows("fetching:True");
        dispatcher.Release();
        rendered.Shows("child:reloaded");
        rendered.Shows("fetching:False");
    }

    [Fact]
    public void A_polled_query_shows_each_new_result()
    {
        var (services, dispatcher) = Session();
        var polled = new QueryOptions { StaleTime = TimeSpan.FromHours(1), RefetchInterval = TimeSpan.FromMilliseconds(20) };
        var rendered = Page.Render(new OrdersPage(polled), services);
        rendered.Shows("orders:first");

        dispatcher.Result = "polled";

        rendered.Shows("orders:polled");
        rendered.Shows("child:polled");
    }

    [Fact]
    public void A_reader_coming_back_shows_the_cached_data_and_then_what_its_refetch_brought()
    {
        var (services, dispatcher) = Session();
        var screen = new Screen(new QueryOptions { StaleTime = TimeSpan.Zero });
        var rendered = Page.Render(screen, services);
        rendered.Shows("list:first");
        screen.Show(list: false);
        rendered.DoesNotShow("list:");
        dispatcher.Block();
        dispatcher.Result = "revisited";

        screen.Show(list: true);

        rendered.Shows("list:first");
        dispatcher.Release();
        rendered.Shows("list:revisited");
    }

    [Fact]
    public async Task A_write_then_going_to_the_list_shows_the_written_rows()
    {
        var (services, dispatcher) = Session();
        var screen = new Screen(Fresh);
        var rendered = Page.Render(screen, services);
        rendered.Shows("list:first");
        screen.Show(list: false);
        rendered.Shows("Ship and go");
        dispatcher.Result = "shipped";

        await rendered.Click("Ship and go");

        rendered.Shows("list:shipped");
    }

    [Fact]
    public async Task A_key_moved_by_a_click_keeps_the_previous_rows_until_the_next_arrive()
    {
        var (services, dispatcher) = Session();
        var rendered = Page.Render(new PagedPage(), services);
        rendered.Shows("rows:first placeholder:False");
        dispatcher.Block();
        dispatcher.Result = "page two";

        await rendered.Click("Next");

        rendered.Shows("rows:first placeholder:True");
        dispatcher.Release();
        rendered.Shows("rows:page two placeholder:False");
        rendered.Shows("shared:page two");
    }

    [Fact]
    public async Task A_key_moved_to_a_cached_entry_reaches_the_other_readers_of_the_query()
    {
        var (services, dispatcher) = Session();
        var rendered = Page.Render(new PagedPage(), services);
        rendered.Shows("shared:first");
        dispatcher.Result = "page two";
        await rendered.Click("Next");
        rendered.Shows("shared:page two");
        dispatcher.Result = "never asked";

        await rendered.Click("Back");

        rendered.Shows("rows:first");
        rendered.Shows("shared:first");
    }

    /// <summary>Reads its orders in Render, and holds a child that reads the same query and one that renders itself.</summary>
    private sealed class OrdersPage : Component
    {
        private readonly QueryOptions _options;
        private readonly OrdersLine _child;
        private readonly Ticker _ticker = new();
        private Query<string>? _orders;

        public OrdersPage(QueryOptions options)
        {
            _options = options;
            _child = new OrdersLine(() => _orders!);
        }

        public int Renders { get; private set; }

        protected override Component? Render()
        {
            Renders++;
            _orders = QueryClient.Query(new GetOrders(1), _options);
            return Div[
                P["orders:" + (_orders.Data ?? "loading")],
                _child,
                _ticker,
                new Reloader(() => _orders!)
            ];
        }
    }

    /// <summary>A child reading the page's query.</summary>
    private sealed class OrdersLine(Func<Query<string>> orders) : Component
    {
        protected override Component? Render()
        {
            var query = orders();
            return P["child:" + (query.Data ?? "loading"), " fetching:" + query.IsFetching];
        }
    }

    /// <summary>Renders only itself, from its own button.</summary>
    private sealed class Ticker : Component
    {
        private int _ticks;

        protected override Component? Render() => Div[
            Button.OnClick(async () =>
            {
                _ticks++;
                await StateHasChangedAsync();
            })["Tick"],
            Span["ticks:" + _ticks]
        ];
    }

    /// <summary>Reloads a query it never reads.</summary>
    private sealed class Reloader(Func<Query<string>> orders) : Component
    {
        protected override Component? Render() => Button.OnClick(() => { _ = orders().Reload(); })["Reload"];
    }

    /// <summary>Two screens: a list reading a query, and a form whose command changes what the list holds.</summary>
    private sealed class Screen(QueryOptions options) : Component
    {
        private bool _list = true;

        public void Show(bool list)
        {
            _list = list;
            StateHasChanged();
        }

        protected override Component? Render() => _list
            ? new ListScreen(options)
            : Button.OnClick(async () =>
            {
                await QueryClient.Command<ShipOrder>().Send(new ShipOrder(1));
                _list = true;
            })["Ship and go"];
    }

    private sealed class ListScreen(QueryOptions options) : Component
    {
        protected override Component? Render()
        {
            var orders = QueryClient.Query(new GetOrders(1), options);
            return P["list:" + orders.Data];
        }
    }

    /// <summary>A paged list keeping the previous page, with a pager and a second reader of the same query.</summary>
    private sealed class PagedPage : Component
    {
        private readonly OrdersLineShared _shared;
        private int _page = 1;
        private Query<string>? _orders;

        public PagedPage() => _shared = new OrdersLineShared(() => _orders!);

        protected override Component? Render()
        {
            _orders = QueryClient.Query(new GetOrders(_page), new QueryOptions { KeepPreviousData = true, StaleTime = TimeSpan.FromHours(1) });
            return Div[
                P["rows:" + _orders.Data + " placeholder:" + _orders.IsPlaceholderData],
                Button.OnClick(() => { _page++; })["Next"],
                Button.OnClick(() => { _page--; })["Back"],
                _shared
            ];
        }
    }

    private sealed class OrdersLineShared(Func<Query<string>> orders) : Component
    {
        protected override Component? Render() => P["shared:" + orders().Data];
    }
}
