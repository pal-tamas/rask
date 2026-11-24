using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     Where a <see cref="Rask.Values.Secret{T}" /> column finds its <see cref="ISecrets" />: the work in progress's
///     services, else the one the host set at start.
/// </summary>
/// <remarks>
///     A value converter is built once per model and called with no services to hand, so it asks here. The host's
///     <see cref="Default" /> covers what runs outside any work — a seeder at start, a test.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SecretKeeping
{
    private static ISecrets? _default;

    /// <summary>The secrets used when the work in progress has none: set by the host at start, or by a test.</summary>
    public static ISecrets? Default
    {
        get => Volatile.Read(ref _default);
        set => Volatile.Write(ref _default, value);
    }

    /// <summary>The secrets to encrypt with now.</summary>
    /// <exception cref="InvalidOperationException">Nothing registered any.</exception>
    internal static ISecrets Current =>
        Ambient.Services?.GetService<ISecrets>() ?? Default ?? throw new InvalidOperationException(
            "A Secret<T> column was written or read, but no ISecrets is registered to encrypt it with. A Rask.Server host " +
            "registers one over its Data Protection key ring; a host wired by hand registers its own " +
            "(services.AddSingleton<ISecrets, …>()), and a test sets SecretKeeping.Default.");
}
