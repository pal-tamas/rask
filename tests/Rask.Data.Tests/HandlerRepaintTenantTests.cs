using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Data.Tests;

/// <summary>
///     A repaint made while a handler awaits inside a tenant scope of its own — <c>Tenant.Across()</c>,
///     <c>Tenant.None()</c> — is drawn in the tenant the handler was dispatched in. Inherited from the continuation
///     that asked for it, the page rendered with no tenant and showed its "no tenant" branch for one frame on every
///     click, though the handler's own reads were right and the log was clean.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class HandlerRepaintTenantTests
{
    private static readonly Guid Acme = Guid.NewGuid();

    private static JsonElement NoPayload => JsonDocument.Parse("{}").RootElement;

    [Fact]
    public async Task A_handlers_repaint_while_it_reads_across_tenants_is_drawn_in_the_tenant_it_was_dispatched_in()
    {
        using var session = new TenantSession();
        var page = session.Page;
        using var dispatchedIn = Tenant.Use(Acme);
        page.On(async () =>
        {
            using (Tenant.Across())
            {
                await ReadLeavingTheHandlersContext();
            }
        });

        await page.Press();

        Assert.NotEmpty(session.Repainted);
        Assert.All(session.Repainted, tenant => Assert.Equal(Acme, tenant));
    }

    [Fact]
    public async Task A_handlers_repaint_while_it_has_cleared_the_tenant_is_drawn_in_the_tenant_it_was_dispatched_in()
    {
        using var session = new TenantSession();
        var page = session.Page;
        using var dispatchedIn = Tenant.Use(Acme);
        page.On(async () =>
        {
            using (Tenant.None())
            {
                await ReadLeavingTheHandlersContext();
            }
        });

        await page.Press();

        Assert.NotEmpty(session.Repainted);
        Assert.All(session.Repainted, tenant => Assert.Equal(Acme, tenant));
    }

    [Fact]
    public async Task The_handler_itself_keeps_the_scope_it_opened_while_its_repaints_leave_it()
    {
        using var session = new TenantSession();
        var page = session.Page;
        using var dispatchedIn = Tenant.Use(Acme);
        var acrossAfterTheAwait = false;
        page.On(async () =>
        {
            using (Tenant.Across())
            {
                await ReadLeavingTheHandlersContext();
                acrossAfterTheAwait = Tenant.IsAcrossTenants;
            }
        });

        await page.Press();

        Assert.True(acrossAfterTheAwait);
        Assert.All(session.Repainted, tenant => Assert.Equal(Acme, tenant));
    }

    // As a data layer's read does: it leaves the handler's context, so its awaits come back in the handler's scope on
    // the pool, and the handler's continuation is posted from there.
    private static async Task ReadLeavingTheHandlersContext() => await Task.Delay(5).ConfigureAwait(false);

    // A live session whose repaint records the tenant it was drawn in, and nothing else. No budget: every
    // continuation of a handler is repainted.
    private sealed class TenantSession() : LiveSessionBase(new TenantPage(), new ServiceCollection().BuildServiceProvider(), LiveDiffMode.Auto)
    {
        private readonly List<Guid?> _repainted = [];

        public TenantPage Page => (TenantPage)View;

        public IReadOnlyList<Guid?> Repainted
        {
            get
            {
                lock (_repainted)
                {
                    return [.. _repainted];
                }
            }
        }

        protected override Task RequestRenderInternalAsync(bool publishOnly) => Task.CompletedTask;

        protected override Task RenderInScopeCoreAsync()
        {
            lock (_repainted)
            {
                _repainted.Add(Tenant.Key);
            }

            return Task.CompletedTask;
        }

        protected override ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame) => default;

        // Nothing to tear down: no pooled buffer was ever rented.
        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class TenantPage : Component
    {
        private string? _handler;

        public void On(Func<Task> handler) => _handler = RegisterHandler(handler);

        public async Task Press() => Assert.True(await TryInvokeHandlerAsync(_handler!, NoPayload));

        protected override Component? Render() => null;
    }
}
