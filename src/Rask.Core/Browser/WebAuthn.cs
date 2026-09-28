using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IWebAuthn" />, backed by the unified <see cref="IJSRuntime" />. The
///     <c>ArrayBuffer</c>-heavy credential shapes can't be expressed through dotted identifiers, so options
///     and results go through the framework's <c>__raskWebAuthn</c> helper, which base64url-encodes the
///     binary fields at the boundary.
/// </summary>
public sealed class WebAuthn(IJSRuntime js) : IWebAuthn
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskWebAuthn.isSupported");

    /// <inheritdoc />
    public ValueTask<bool> IsPlatformAuthenticatorAvailableAsync() =>
        js.InvokeAsync<bool>("__raskWebAuthn.platformAuthenticatorAvailable");

    /// <inheritdoc />
    public ValueTask<AttestationResult?> CreateAsync(PublicKeyCredentialCreationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return js.InvokeAsync<AttestationResult?>("__raskWebAuthn.create", options);
    }

    /// <inheritdoc />
    public ValueTask<AssertionResult?> GetAsync(PublicKeyCredentialRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return js.InvokeAsync<AssertionResult?>("__raskWebAuthn.get", options);
    }
}
