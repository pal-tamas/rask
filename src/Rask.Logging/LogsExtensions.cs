namespace Rask.Logging;

/// <summary>The trim steps on an injected <see cref="ILogs" />, worded as on <see cref="Logs" />.</summary>
public static class LogsExtensions
{
    extension(ILogs logs)
    {
        /// <inheritdoc cref="Logs.Trim(CancellationToken)" />
        public Trimming Trim(CancellationToken cancellationToken = default) =>
#pragma warning disable CA2208 // CA2208 cannot see a C# 14 extension receiver
            new(logs ?? throw new ArgumentNullException(nameof(logs)), null, null, cancellationToken);
#pragma warning restore CA2208
    }
}
