using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Rask.Wire;

namespace Company.RaskServer.Features.Auth;

public sealed class PasskeyModel
{
    public string Name { get; set; } = "";
}

// Every device signed in to this account: one Session row each. Signing out the others deletes their rows, and
// each of those devices finds itself signed out on its next request. Passkeys live here too — they are the other
// thing a person keeps on a device, and taking one away is the same kind of decision.
[Authorize]
[Route("/devices")]
public sealed partial class DevicesPage(IAuth auth, IUserProvider users, IWebAuthn webAuthn) : AuthPage
{
    private readonly PasskeyModel _passkey = new();
    private IReadOnlyList<SessionRead> _sessions = [];
    private IReadOnlyList<PasskeyRead> _passkeys = [];
    private bool _signedOutOthers;
    private bool _passkeysSupported;
    private AuthError _passkeyError;

    protected override Component? HeadAssets => Title["Your devices"];

    protected override async Task OnMount() => await LoadAsync();

    // The support check is JavaScript, so it waits for a browser to exist: on the first render this page is HTML
    // on its way out, with nothing to ask.
    protected override async Task OnFirstRendered()
    {
        _passkeysSupported = await webAuthn.IsSupportedAsync();
        StateHasChanged();
    }

    protected override Component? Content =>
        [
            H1.Class("text-2xl font-bold")["Your devices"],
            _signedOutOthers ? Ok("devices-signed-out", "Every other device is signed out.") : null,
            Ul.Class("list")[
                _sessions.Select(session =>
                    Li.Key(session.Id).Class("list-row")[
                        Div[
                            Div.Class("font-semibold")[Describe(session.UserAgent)],
                            Div.Class("text-xs opacity-70")[
                                session.Id == users.Current.SessionId()
                                    ? "This device"
                                    : "Last seen " + session.LastSeenAt.ToString("g", CultureInfo.CurrentCulture)]
                        ]
                    ])
            ],
            _sessions.Count > 1
                ? Div.Class("card-actions mt-2")[
                    Button.Type("button").Id("devices-sign-out-others").Class("btn btn-outline btn-block").OnClick(SignOutOthersAsync)[
                        "Sign out every other device"]
                ]
                : null,
            Passkeys
        ];

    // A passkey is a second way into this account, so the list says what each one is and offers to take it away.
    private Component? Passkeys =>
        [
            Div.Class("divider")["Passkeys"],
            _passkeyError is AuthError.None ? null : Error("passkey-error", AuthMessages.For(_passkeyError)),
            _passkeys.Count == 0
                ? P.Class("text-sm opacity-70")["No passkeys yet. Add one to sign in without a password."]
                : Ul.Class("list").Id("passkey-list")[
                    _passkeys.Select(passkey =>
                        Li.Key(passkey.Id).Class("list-row")[
                            Div[
                                Div.Class("font-semibold")[passkey.Name],
                                Div.Class("text-xs opacity-70")[
                                    passkey.LastUsedAt is { } used
                                        ? "Last used " + used.ToString("g", CultureInfo.CurrentCulture)
                                        : "Never used"]
                            ],
                            Button.Type("button").Class("btn btn-ghost btn-xs").OnClick(() => RemoveAsync(passkey.Id))[
                                "Remove"]
                        ])
                ],
            _passkeysSupported
                ? Form.Model(_passkey).OnSubmit(AddAsync)[
                    Field(
                        "passkey-name",
                        "Name this device",
                        Input.Bind(() => _passkey.Name).Id("passkey-name").Class("input w-full")),
                    Div.Class("card-actions mt-2")[
                        Button.Type("submit").Id("passkey-add").Class("btn btn-outline btn-block")["Add a passkey"]
                    ]
                ]
                : P.Class("text-sm opacity-70")["This browser cannot use passkeys."]
        ];

    private async Task AddAsync(PasskeyModel model)
    {
        // The whole ceremony runs inside this click: the browser prompts, the authenticator signs, the server
        // verifies. A dismissed dialog comes back as a refusal rather than an exception.
        var result = await auth.AddPasskeyAsync(model.Name);
        _passkeyError = result.Error;

        if (result.Succeeded)
        {
            model.Name = "";
            await LoadAsync();
        }
    }

    private async Task RemoveAsync(Guid id)
    {
        var result = await auth.RemovePasskeyAsync(id);
        _passkeyError = result.Error;
        await LoadAsync();
    }

    private async Task SignOutOthersAsync()
    {
        await auth.SignOutOtherDevicesAsync();
        _signedOutOthers = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (users.Current.UserId() is not { } me)
        {
            _sessions = [];
            _passkeys = [];
            return;
        }

        // Read faces, like any other aggregate's: no context to inject, nothing tracked, and each read
        // opens and disposes its own. Your own aggregates work the same way — Product.Read.Where(…).
        _sessions = await Session.Read
            .Where(s => s.UserId == me)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(CancellationToken);

        _passkeys = await Passkey.Read
            .Where(p => p.UserId == me)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(CancellationToken);
    }

    private static string Describe(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent) ? "Unknown browser"
        : userAgent.Length > 80 ? userAgent[..80] + "…"
        : userAgent;
}
