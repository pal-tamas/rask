using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rask.Core.Messaging;
using Rask.Hosting.Shared;
using StackExchange.Redis;

namespace Rask.Redis;

/// <summary>
/// The <see cref="IBroadcastBackplane" /> over Redis pub/sub: one channel per cross-host topic, named
/// <see cref="RedisOptions.ChannelPrefix" /> + the topic's name, and one connection for the process.
/// </summary>
/// <remarks>
/// <para>
/// <b>The connection</b> is the app's own <see cref="IConnectionMultiplexer" /> when it registered one, and otherwise
/// one this opens from <c>Rask:ConnectionStrings:Redis</c> as the host starts — so a missing connection string stops
/// the start, naming the key, rather than the first page that subscribes. It is opened with
/// <c>AbortOnConnectFail=false</c>: a Redis that is down at start, or restarts later, is reconnected in the
/// background, and the subscriptions come back with it.
/// </para>
/// <para>
/// <b>At most once, like the hub.</b> A publish Redis cannot take is logged and dropped: this host's subscribers
/// already have the message, and failing the publisher — usually a request whose work is committed — would report a
/// failure of something that happened.
/// </para>
/// </remarks>
internal sealed partial class RedisBroadcastBackplane : IBroadcastBackplane, IHostedService, IDisposable, IAsyncDisposable
{
    private readonly Guid _host = Guid.NewGuid();
    private readonly string _prefix;
    private readonly ILogger<RedisBroadcastBackplane> _logger;
    private readonly Lazy<Task<IConnectionMultiplexer>> _connection;
    private readonly bool _ownsConnection;

    public RedisBroadcastBackplane(
        IServiceProvider services,
        IOptions<RedisOptions> options,
        ILogger<RedisBroadcastBackplane> logger)
    {
        _prefix = options.Value.ChannelPrefix;
        _logger = logger;

        if (services.GetService<IConnectionMultiplexer>() is { } shared)
        {
            _connection = new Lazy<Task<IConnectionMultiplexer>>(Task.FromResult(shared));
            return;
        }

        _ownsConnection = true;
        _connection = new Lazy<Task<IConnectionMultiplexer>>(async () =>
        {
            var configuration = ConfigurationOptions.Parse(RaskOptionsRegistration.ConnectionString(services, "Redis"));
            configuration.AbortOnConnectFail = false;
            return await ConnectionMultiplexer.ConnectAsync(configuration).ConfigureAwait(false);
        });
    }

    public async Task PublishAsync(string topic, byte[] payload, CancellationToken cancellationToken)
    {
        try
        {
            var redis = await _connection.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            await redis.GetSubscriber()
                .PublishAsync(Channel(topic), BackplaneEnvelope.Encode(_host, payload))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPublishFailed(_logger, topic, ex);
        }
    }

    public void Subscribe(string topic, Action<ReadOnlyMemory<byte>> received) => _ = SubscribeAsync(topic, received);

    public Task StartAsync(CancellationToken cancellationToken) => _connection.Value.WaitAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Both shapes: a container disposed synchronously throws on a singleton that is only IAsyncDisposable.
    public void Dispose() => OwnedConnection()?.Dispose();

    public ValueTask DisposeAsync() => OwnedConnection()?.DisposeAsync() ?? default;

    // Only a connection this opened, and only once it has: disposing must never be what opens one.
    private IConnectionMultiplexer? OwnedConnection() =>
        _ownsConnection && _connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully
            ? _connection.Value.Result
            : null;

    private async Task SubscribeAsync(string topic, Action<ReadOnlyMemory<byte>> received)
    {
        try
        {
            var redis = await _connection.Value.ConfigureAwait(false);
            var queue = await redis.GetSubscriber().SubscribeAsync(Channel(topic)).ConfigureAwait(false);

            // A ChannelMessageQueue hands its messages over one at a time, in order — the hub's contract.
            queue.OnMessage(message => Receive(topic, message.Message, received));
        }
#pragma warning disable CA1031 // Runs detached from the subscribing component; a failure can only be logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSubscribeFailed(_logger, topic, ex);
        }
    }

    private void Receive(string topic, RedisValue value, Action<ReadOnlyMemory<byte>> received)
    {
        if (!BackplaneEnvelope.TryOpen((ReadOnlyMemory<byte>)value, _host, out var payload))
        {
            return;
        }

        try
        {
            received(payload);
        }
#pragma warning disable CA1031 // One bad message must not end the channel's queue.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogReceiveFailed(_logger, topic, ex);
        }
    }

    private RedisChannel Channel(string topic) => RedisChannel.Literal(_prefix + topic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not carry a broadcast on '{Topic}' to the other hosts")]
    private static partial void LogPublishFailed(ILogger logger, string topic, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not receive the broadcast topic '{Topic}' from the other hosts")]
    private static partial void LogSubscribeFailed(ILogger logger, string topic, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A broadcast on '{Topic}' from another host could not be delivered")]
    private static partial void LogReceiveFailed(ILogger logger, string topic, Exception exception);
}
