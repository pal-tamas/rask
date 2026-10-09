using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.TestSupport;

namespace Rask.UiTests.Live;

/// <summary>
///     A page's callback that takes the kit component that raised it off the page, and then reloads: the reload
///     is the page's, so both of its reads finish.
/// </summary>
/// <remarks>
///     Each read passes no token, as <c>await Product.Where(…)</c> does, so it is cancelled with the work in
///     progress. That used to be the component whose handler raised the callback — the modal, the pager, the
///     tabs — which the callback's first line unmounts.
/// </remarks>
public sealed partial class KitCallbackLifetimeTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(Kit.ModalClose, "close", 0, "{}")]
    [InlineData(Kit.ModalEditor, "click", 0, "{}")]
    [InlineData(Kit.Select, "change", 0, "{\"value\":\"b\"}")]
    [InlineData(Kit.Pagination, "click", 0, "{}")]
    [InlineData(Kit.Tabs, "click", 2, "{}")]
    public async Task A_page_that_unmounts_the_component_that_raised_its_callback_still_reloads(
        Kit kit, string domEvent, int handler, string payload)
    {
        using var turn = new Turn(kit);

        await turn.Raise(domEvent, handler, payload);

        await turn.Shows("reloaded:6");
    }

    public enum Kit
    {
        ModalClose,
        ModalEditor,
        Select,
        Pagination,
        Tabs,
    }

    public sealed record Case(Kit Kit);

    // What a session is to a handler: a render while it awaits, and one after it.
    private sealed class Turn : IRenderHandle, IDisposable
    {
        private readonly Lock _render = new();
        private readonly ServiceProvider _services;
        private readonly StubComponent _view;
        private string _html = "";

        public Turn(Kit kit)
        {
            _services = new ServiceCollection().AddSingleton(new Case(kit)).BuildServiceProvider();
            _view = new StubComponent(() => KitCallbackPage) { RenderHandle = this };
            Render();
        }

        public async Task Raise(string domEvent, int handler, string payload)
        {
            var ids = Regex.Matches(
                _html, $"data-rask-on-{domEvent}=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
            using var frame = JsonDocument.Parse(payload);
            await _view.TryInvokeHandlerAsync(ids[handler].Groups[1].Value, frame.RootElement, _services);
            Render();
        }

        public Task Shows(string text) =>
            WaitFor.True(() => _html.Contains(text, StringComparison.Ordinal), $"'{text}' in: {_html}");

        public Task RequestRender()
        {
            Render();
            return Task.CompletedTask;
        }

        Task IRenderHandle.RenderInScopeAsync() => RequestRender();

        public void Dispose() => _services.Dispose();

        private void Render()
        {
            lock (_render)
            {
                _html = _view.RenderAsLiveRoot(_services);
            }
        }
    }
}

internal sealed partial class KitCallbackPage(KitCallbackLifetimeTests.Case shown) : Component
{
    private bool _closed;
    private int _rows;

    protected override Component? Render()
    {
        if (_closed)
        {
            return Span[$"reloaded:{_rows}"];
        }

        return shown.Kit switch
        {
            KitCallbackLifetimeTests.Kit.ModalClose => Ui.Modal.Open(true).OnClose(Reload)[P["Details"]],
            KitCallbackLifetimeTests.Kit.ModalEditor => Ui.Modal.Open(true)[KitCallbackEditor.OnSaved(Reload)],
            KitCallbackLifetimeTests.Kit.Select => Ui.Select.Value("a").OnChange(Picked)[
                Ui.SelectOption.Key("a").Value("a")["Alpha"], Ui.SelectOption.Key("b").Value("b")["Beta"]
            ],
            KitCallbackLifetimeTests.Kit.Pagination =>
                Ui.Pagination.Paginator(new UiPaginator { Page = 2, PerPage = 5, Total = 24 }).OnPage(Paged),
            _ => Ui.Tabs.OnChange(Picked)[
                Ui.Tab.Name("profile")["Profile"], Ui.Tab.Name("account")["Account"], Ui.Tab.Name("billing")["Billing"]
            ],
        };
    }

    private Task Picked(string value) => Reload();

    private Task Paged(int page) => Reload();

    // The page's own code: it takes the component that raised it off the page, then reads twice.
    private async Task Reload()
    {
        _closed = true;
        StateHasChanged();
        _rows = await Read();
        _rows += await Read();
    }

    // A read as a data call makes it: no token passed, so it is cancelled with the work in progress.
    private static async Task<int> Read()
    {
        var token = Current.Cancellation;
        await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return 3;
    }
}

// An editor in a modal: its own handler saves, then tells the page.
internal sealed partial class KitCallbackEditor : Component
{
    public Callback OnSaved { get; set; }

    protected override Component? Render() => Ui.Button.OnClick(Save)["Save"];

    private async Task Save()
    {
        await Task.Delay(1).ConfigureAwait(false);
        await OnSaved.Invoke();
    }
}
