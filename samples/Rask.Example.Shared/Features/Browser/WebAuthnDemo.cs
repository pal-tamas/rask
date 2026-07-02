using System.Buffers.Text;
using System.Security.Cryptography;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IWebAuthn" /> — register a passkey, then sign in with it. The challenge is normally issued
///     and verified by your <em>backend</em>; this demo generates it client-side and just displays the
///     returned attestation/assertion (no server verification), to show the browser round-trip.
/// </summary>
public sealed class WebAuthnDemo(IWebAuthn webAuthn) : Component
{
    // A stable user handle for this demo session (a real app uses the account's server-side id).
    private readonly string _userId = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
    private string? _credentialId;
    private string _status = "(idle)";
    private string _support = "(unchecked)";

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            if (!await webAuthn.IsSupportedAsync())
            {
                _support = "WebAuthn not supported in this browser";
            }
            else
            {
                var platform = await webAuthn.IsPlatformAuthenticatorAvailableAsync();
                _support = platform
                    ? "Supported — platform authenticator available"
                    : "Supported — security key only";
            }
        }
        catch (Exception ex)
        {
            _support = "Support check failed: " + ex.Message;
        }

        StateHasChanged();
    }

    protected override Component? Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(Class: "btn btn-primary btn-sm", Id: "webauthn-create", OnClickAsync: Create)[
                        I(Class: "bi bi-fingerprint me-1"), "Create passkey"],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "webauthn-auth",
                        Disabled: _credentialId is null, OnClickAsync: Authenticate)["Authenticate"]
                ],
                Div(Class: "small text-secondary")["Support: ", Code(Id: "webauthn-support")[_support]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "webauthn-status")[_status]]
            ]
        ];

    private async Task Create()
    {
        try
        {
            var result = await webAuthn.CreateAsync(new PublicKeyCredentialCreationOptions
            {
                Challenge = NewChallenge(),
                Rp = new RelyingParty("Rask Showcase"),
                User = new PublicKeyCredentialUser(_userId, "demo@rask.dev", "Rask Demo"),
                AuthenticatorSelection = new AuthenticatorSelection { UserVerification = "preferred" }
            });

            if (result is null)
            {
                _status = "Registration cancelled";
                return;
            }

            _credentialId = result.Id;
            _status = $"Passkey created (credential {Shorten(result.Id)}) — now Authenticate";
        }
        catch (Exception ex)
        {
            _status = "Registration failed: " + ex.Message;
        }
    }

    private async Task Authenticate()
    {
        try
        {
            var result = await webAuthn.GetAsync(new PublicKeyCredentialRequestOptions
            {
                Challenge = NewChallenge(),
                UserVerification = "preferred",
                AllowCredentials = _credentialId is null
                    ? null
                    : [new CredentialDescriptor(_credentialId)]
            });

            _status = result is null
                ? "Authentication cancelled"
                : $"Signed in — assertion received (signature {Shorten(result.Signature)}). A real app verifies it server-side.";
        }
        catch (Exception ex)
        {
            _status = "Authentication failed: " + ex.Message;
        }
    }

    private static string NewChallenge() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static string Shorten(string b64) => b64.Length <= 12 ? b64 : b64[..12] + "…";
}
