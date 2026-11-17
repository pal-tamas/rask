using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Wasm.Tests.Hosting;

[OffThread]
public sealed record Shout(string Word, byte[] Echo) : IQuery<string>;

public sealed class ShoutHandler(HttpClient http) : IQueryHandler<Shout, string>
{
    public Task<string> Handle(Shout query) =>
        Task.FromResult($"{query.Word.ToUpperInvariant()} {query.Echo.Length} {http.BaseAddress}");
}

// The worker's .NET end, as rask-worker.ts drives it: Start with the app's assembly, then a frame in and a frame out.
public sealed class OffThreadHostTests
{
    [Fact]
    public async Task A_started_worker_answers_a_frame_with_its_handlers_and_an_http_client_on_the_pages_origin()
    {
        await OffThreadHost.Start(typeof(OffThreadHostTests).Assembly.GetName().Name!, "en-US", "https://shop.example/");
        Assert.True(RemoteContractRegistry.TryGet(typeof(Shout).FullName!, out var contract));
        var frame = await OffThreadRequest.Write(contract!, new Shout("hi", [1, 2, 3]), TestContext.Current.CancellationToken);

        var answer = await OffThreadHost.Run(1, frame);

        Assert.Equal("HI 3 https://shop.example/", OffThreadAnswer.Read<string>(contract!, answer));
    }

    [Fact]
    public void Off_the_browser_the_wiring_leaves_the_handlers_where_they_are()
    {
        var services = new ServiceCollection();

        OffThreadWiring.Add(services, new OffThreadOptions { Warm = true });

        Assert.DoesNotContain(services, static d => d.ServiceType == typeof(OffThreadDispatch));
    }
}
