using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// A component that asks for its own render re-renders its own subtree: the page holding it is clean, so the
// walk the request starts serves the page's cached output and only the child runs Render again.
public sealed partial class NestedRenderScopeTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_child_rendering_itself_from_its_own_handler_leaves_the_page_unrendered(bool bypassesCache)
    {
        using var host = new Host(bypassesCache);
        var before = host.Page.Renders;

        await host.Click();

        Assert.Equal(before, host.Page.Renders);
        Assert.Contains("shown:True", host.Html, StringComparison.Ordinal);
    }

    private sealed class Host : IRenderHandle, IDisposable
    {
        private readonly Lock _render = new();
        private readonly StubComponent _view;
        private readonly JsonDocument _payload = JsonDocument.Parse("{}");

        public Host(bool bypassesCache)
        {
            Page = new CountingPage(new Toggle(bypassesCache));
            _view = new StubComponent(() => Main[Page]) { RenderHandle = this };
            Render();
        }

        public CountingPage Page { get; }

        public string Html { get; private set; } = "";

        public async Task Click()
        {
            await _view.TryInvokeHandlerAsync(MarkupAssert.Attr(Html, "data-rask-on-click")!, _payload.RootElement);
            Render();
        }

        public Task RequestRender()
        {
            Render();
            return Task.CompletedTask;
        }

        Task IRenderHandle.RenderInScopeAsync() => RequestRender();

        public void Dispose() => _payload.Dispose();

        private void Render()
        {
            lock (_render)
            {
                Html = _view.RenderAsLiveRoot();
            }
        }
    }

    private sealed partial class CountingPage(Toggle toggle) : Component
    {
        public int Renders { get; private set; }

        protected override Component? Render()
        {
            Renders++;
            return Section[toggle];
        }
    }

    private sealed partial class Toggle(bool bypassesCache) : Component
    {
        private bool _shown;

        protected override bool BypassRenderCache => bypassesCache;

        protected override Component? Render() =>
            Button.OnClick(async () =>
            {
                _shown = !_shown;
                await StateHasChangedAsync();
            })["shown:" + _shown];
    }
}
