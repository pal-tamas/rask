using Microsoft.AspNetCore.DataProtection;
using Rask.Data;

namespace Rask;

/// <summary>
///     What a <c>Secret&lt;T&gt;</c> column is encrypted with on a server: the host's Data Protection key ring — the
///     ring a Rask deploy already persists to its volume, so a stored secret survives a redeploy and a key rotation.
/// </summary>
/// <remarks>
///     Its own purpose string, so a ciphertext from here is no antiforgery token, cookie or temporary link, and none of
///     those reads as a secret.
/// </remarks>
internal sealed class DataProtectionSecrets(IDataProtectionProvider provider) : ISecrets
{
    private const string Purpose = "Rask.Data.Secret.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string stored) => _protector.Unprotect(stored);
}
