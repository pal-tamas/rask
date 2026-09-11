using Microsoft.Extensions.Logging;
using Rask.Storage.Backends;
using Rask.Storage.Serving;

namespace Rask.Storage;

/// <summary>
/// The resolved pieces the file routes, <see cref="IFiles"/> and the sweep share. Resolving it validates the
/// configuration, which the startup check does at boot.
/// </summary>
internal sealed class StorageRuntime(
    StorageOptions options,
    IBlobBackend backend,
    TemporaryUrlProtector protector,
    TimeProvider time,
    ILogger<StorageRuntime> logger)
{
    private volatile bool _endpointsMapped;

    public StorageOptions Options { get; } = options;

    public IBlobBackend Backend { get; } = backend;

    public TemporaryUrlProtector Protector { get; } = protector;

    public TimeProvider Time { get; } = time;

    public ILogger Logger { get; } = logger;

    /// <summary>Set by <c>MapRaskStorage</c>, so the startup check can say when links would 404.</summary>
    public bool EndpointsMapped
    {
        get => _endpointsMapped;
        set => _endpointsMapped = value;
    }
}
