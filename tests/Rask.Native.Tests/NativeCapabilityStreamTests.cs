using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Browser;

namespace Rask.Native.Tests;

/// <summary>
///     The six members that push instead of returning. A JSON envelope cannot carry a callback, so the
///     handle stays native-side and the page refers to it by an id it chose itself.
/// </summary>
public class NativeCapabilityStreamTests
{
    [Fact]
    public async Task Starting_a_watch_delivers_readings_to_the_page()
    {
        var battery = new FakeBattery();
        var scripts = new List<string>();
        var services = Services(s =>
        {
            s.AddSingleton<IBattery>(battery);
            s.AddSingleton<NativeCapabilitySubscriptions>();
        });

        await Handle(services, scripts, """{"type":"capability","id":"1","component":"battery","op":"watch","data":"{\"sub\":\"s1\"}"}""");

        await battery.EmitAsync(new BatteryStatus(0.42, true, null, null));

        var evt = Event(scripts);
        Assert.Equal("s1", evt.GetProperty("sub").GetString());
        Assert.Contains("0.42", evt.GetProperty("payload").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The id is the page's, not the host's. That is what lets a reading which beats the reply still
    ///     find its callback — the page registered one before it ever sent the request.
    /// </summary>
    [Fact]
    public async Task A_reading_that_arrives_before_the_reply_still_carries_the_pages_id()
    {
        var battery = new FakeBattery();
        var scripts = new List<string>();
        var services = Services(s =>
        {
            s.AddSingleton<IBattery>(battery);
            s.AddSingleton<NativeCapabilitySubscriptions>();
        });

        // The backend pushes during the start call, before the envelope has replied.
        battery.EmitOnWatch = new BatteryStatus(0.1, false, null, null);

        await Handle(services, scripts, """{"type":"capability","id":"1","component":"battery","op":"watch","data":"{\"sub\":\"chosen\"}"}""");

        var evt = Event(scripts);
        Assert.Equal("chosen", evt.GetProperty("sub").GetString());
    }

    /// <summary>
    ///     Releasing must actually stop the backend. A GPS watch or a wake lock that outlives the page that
    ///     asked for it is a battery complaint with no visible cause.
    /// </summary>
    [Fact]
    public async Task Releasing_a_watch_disposes_the_backends_handle()
    {
        var battery = new FakeBattery();
        var scripts = new List<string>();
        var services = Services(s =>
        {
            s.AddSingleton<IBattery>(battery);
            s.AddSingleton<NativeCapabilitySubscriptions>();
        });

        await Handle(services, scripts, """{"type":"capability","id":"1","component":"battery","op":"watch","data":"{\"sub\":\"s1\"}"}""");
        Assert.False(battery.Released);

        await Handle(services, scripts, """{"type":"capability","id":"2","component":"battery","op":"unwatch","data":"\"s1\""}""");

        Assert.True(battery.Released);
    }

    /// <summary>
    ///     Disposing the app releases whatever is still running. Nothing the app started should outlive it.
    /// </summary>
    [Fact]
    public async Task Disposing_the_registry_releases_every_live_subscription()
    {
        var battery = new FakeBattery();
        var subscriptions = new NativeCapabilitySubscriptions();
        var scripts = new List<string>();
        var services = Services(s =>
        {
            s.AddSingleton<IBattery>(battery);
            s.AddSingleton(subscriptions);
        });

        await Handle(services, scripts, """{"type":"capability","id":"1","component":"battery","op":"watch","data":"{\"sub\":\"s1\"}"}""");

        await subscriptions.DisposeAsync();

        Assert.True(battery.Released);
    }

    /// <summary>Releasing an id nobody holds is a no-op — a reloaded page will do exactly this.</summary>
    [Fact]
    public async Task Releasing_an_unknown_subscription_is_not_an_error()
    {
        var scripts = new List<string>();
        var services = Services(s => s.AddSingleton<NativeCapabilitySubscriptions>());

        await Handle(services, scripts, """{"type":"capability","id":"9","component":"battery","op":"unwatch","data":"\"ghost\""}""");

        var reply = Reply(scripts);
        Assert.True(reply.GetProperty("success").GetBoolean());
    }

    private static async Task Handle(IServiceProvider services, List<string> scripts, string message) =>
        await NativeCapabilities.TryHandleAsync(
            Encoding.UTF8.GetBytes(message),
            services,
            script =>
            {
                scripts.Add(script);
                return default;
            });

    private static IServiceProvider Services(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static JsonElement Event(List<string> scripts) =>
        Unwrap(scripts.Single(s => s.Contains("capabilityEvent", StringComparison.Ordinal)));

    private static JsonElement Reply(List<string> scripts) =>
        Unwrap(scripts.Single(s => s.Contains("capabilityResult", StringComparison.Ordinal)));

    private static JsonElement Unwrap(string script)
    {
        var open = script.IndexOf('"', StringComparison.Ordinal);
        var close = script.LastIndexOf('"');
        var json = JsonSerializer.Deserialize<string>(script[open..(close + 1)])!;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private sealed class FakeBattery : IBattery
    {
        private Func<BatteryStatus, Task>? _onChange;

        public bool Released { get; private set; }

        /// <summary>A reading pushed from inside WatchAsync, before the caller has its handle.</summary>
        public BatteryStatus? EmitOnWatch { get; set; }

        public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

        public ValueTask<BatteryStatus?> GetStatusAsync() => ValueTask.FromResult<BatteryStatus?>(null);

        public async ValueTask<IAsyncDisposable> WatchAsync(Func<BatteryStatus, Task> onChange)
        {
            _onChange = onChange;
            if (EmitOnWatch is { } early)
            {
                await onChange(early);
            }

            return new Handle(this);
        }

        public Task EmitAsync(BatteryStatus status) => _onChange?.Invoke(status) ?? Task.CompletedTask;

        private sealed class Handle(FakeBattery owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Released = true;
                return default;
            }
        }
    }
}
