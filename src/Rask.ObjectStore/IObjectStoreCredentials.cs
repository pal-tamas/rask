using Microsoft.Extensions.Options;

namespace Rask.ObjectStore;

/// <summary>
///     What a store needs to sign a request. For S3 and everything S3-compatible this is an access key and
///     secret (plus a session token when the credential is temporary); for Azure it is a SAS token, which
///     carries its own authority and needs no signing.
/// </summary>
/// <param name="AccessKeyId">S3 access key id. Unused for Azure.</param>
/// <param name="SecretAccessKey">S3 secret access key. Unused for Azure.</param>
/// <param name="SessionToken">
///     Optional STS session token, sent as <c>x-amz-security-token</c>. Set when the credential is
///     temporary.
/// </param>
/// <param name="SasToken">
///     Azure SAS token, with or without a leading <c>?</c>. Unused for S3.
/// </param>
public sealed record ObjectStoreCredential(
    string? AccessKeyId = null,
    string? SecretAccessKey = null,
    string? SessionToken = null,
    string? SasToken = null);

/// <summary>
///     Supplies the credential for each request. Asked per call rather than captured once, so a credential
///     that expires — an STS session, a time-boxed SAS — can be refreshed without rebuilding the store.
/// </summary>
public interface IObjectStoreCredentials
{
    /// <summary>
    ///     The credential to sign the next request with, or <c>null</c> if none is available yet — which a
    ///     store surfaces as an <see cref="InvalidOperationException" /> rather than sending an unsigned
    ///     request.
    /// </summary>
    ValueTask<ObjectStoreCredential?> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Holds a credential in memory for the life of the process and <b>never writes it anywhere</b>. The
///     store for a credential the user supplies at runtime — pasting an access key, or opening a link
///     carrying a SAS.
/// </summary>
/// <remarks>
///     <para>
///         In a browser this is the difference between a credential that dies with the tab and one that any
///         later script injection can read back. There is deliberately no persistence option and no
///         constructor that takes one: a caller who wants the credential to survive a reload has to write
///         that code themselves, and will notice they are doing it.
///     </para>
///     <para>
///         Scope the credential itself before handing it out — read-only where the client only reads, and
///         narrowed to one bucket or prefix. Anything in the page can use whatever the credential can do
///         for as long as it is set.
///     </para>
/// </remarks>
public sealed class InMemoryObjectStoreCredentials : IObjectStoreCredentials
{
    private ObjectStoreCredential? _credential;

    /// <summary>Creates an empty holder — calls fail until <see cref="Set" /> is called.</summary>
    public InMemoryObjectStoreCredentials()
    {
    }

    /// <summary>Creates a holder already carrying <paramref name="credential" />.</summary>
    public InMemoryObjectStoreCredentials(ObjectStoreCredential credential) => _credential = credential;

    /// <summary>Whether a credential is currently held.</summary>
    public bool HasCredential => _credential is not null;

    /// <summary>Replaces the held credential.</summary>
    public void Set(ObjectStoreCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _credential = credential;
    }

    /// <summary>Forgets the held credential — the sign-out path.</summary>
    public void Clear() => _credential = null;

    /// <inheritdoc />
    public ValueTask<ObjectStoreCredential?> GetAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_credential);
}

/// <summary>
///     Reads the credential from <see cref="ObjectStoreOptions" /> — the server-side case, where it comes
///     from configuration, an environment variable, or a secret store.
/// </summary>
public sealed class OptionsObjectStoreCredentials(IOptionsMonitor<ObjectStoreOptions> options)
    : IObjectStoreCredentials
{
    /// <inheritdoc />
    public ValueTask<ObjectStoreCredential?> GetAsync(CancellationToken cancellationToken = default)
    {
        var o = options.CurrentValue;
        if (o.SasToken is { Length: > 0 })
        {
            return ValueTask.FromResult<ObjectStoreCredential?>(new ObjectStoreCredential(SasToken: o.SasToken));
        }

        if (o.AccessKeyId is { Length: > 0 } && o.SecretAccessKey is { Length: > 0 })
        {
            return ValueTask.FromResult<ObjectStoreCredential?>(
                new ObjectStoreCredential(o.AccessKeyId, o.SecretAccessKey, o.SessionToken));
        }

        return ValueTask.FromResult<ObjectStoreCredential?>(null);
    }
}
