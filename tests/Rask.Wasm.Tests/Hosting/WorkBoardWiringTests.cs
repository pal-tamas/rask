using Microsoft.Extensions.DependencyInjection;
using Rask.Background;
using Rask.Cqrs;

namespace Rask.Wasm.Tests.Hosting;

// Ui.WorkNotices and Ui.WorkList in a browser half (#1348): the batteries give them the server's board, and the watch a
// way to the server, when the app is a client of one — and wire nothing when it is not, or has the kit off.
public sealed class WorkBoardWiringTests
{
    [Fact]
    public void A_client_of_a_server_reads_its_bell_s_work_from_the_server()
    {
        var services = new ServiceCollection().AddSingleton<IRemoteDispatch>(new NoServer());

        WasmHostBuilderExtensions.Wire(WasmHostBuilder.CreateDefault(), services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<RemoteWorkBoard>(scope.ServiceProvider.GetRequiredService<IWorkBoard>());
        Assert.True(RemoteContractRegistry.TryGet(typeof(WatchOwnWork), out _));
        Assert.True(RemoteContractRegistry.TryGet(typeof(ReadOwnWork), out _));
    }

    [Fact]
    public void An_app_with_no_server_has_no_board_to_read()
    {
        var services = new ServiceCollection();

        WasmHostBuilderExtensions.Wire(WasmHostBuilder.CreateDefault(), services);

        Assert.DoesNotContain(services, static d => d.ServiceType == typeof(IWorkBoard));
    }

    [Fact]
    public void With_the_kit_off_there_is_no_bell_to_feed()
    {
        var services = new ServiceCollection().AddSingleton<IRemoteDispatch>(new NoServer());

        WasmHostBuilderExtensions.Wire(WasmHostBuilder.CreateDefault().Configure(static c => c.Ui.Off()), services);

        Assert.DoesNotContain(services, static d => d.ServiceType == typeof(IWorkBoard));
    }

    [Fact]
    public void Without_the_query_cache_there_is_no_bell_to_feed()
    {
        var services = new ServiceCollection().AddSingleton<IRemoteDispatch>(new NoServer());

        WasmHostBuilderExtensions.Wire(WasmHostBuilder.CreateDefault().Configure(static c => c.Query.Off()), services);

        Assert.DoesNotContain(services, static d => d.ServiceType == typeof(IWorkBoard));
    }

    // Only its presence is read: the wiring asks whether this app sends to a server, not what it sends.
    private sealed class NoServer : IRemoteDispatch
    {
        public Task<TResult> Send<TResult>(RemoteContract contract, object message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task Send(RemoteContract contract, object message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task Publish(RemoteContract contract, object e, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
