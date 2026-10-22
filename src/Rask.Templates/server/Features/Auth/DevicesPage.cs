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

    protected override string? PageTitle => "Your devices";

    protected override async Task OnMount() => await Load();

    // The support check is JavaScript, so it waits for a browser to exist: on the first render this page is HTML
    // on its way out, with nothing to ask.
    protected override async Task OnFirstRender()
    {
        _passkeysSupported = await webAuthn.IsSupported();
        StateHasChanged();
    }

    protected override Component? Content =>
        [
            Title("Your devices"),
            _signedOutOthers ? Ok("devices-signed-out", "Every other device is signed out.") : null,
            Ul.Class(List)[
                _sessions.Select(session =>
                    Li.Key(session.Id).Class(Row)[
                        Div.Class("min-w-0")[
                            Ui.Heading.Class("truncate")[Describe(session.UserAgent)],
                            Ui.Text.Sm[
                                session.Id == users.Current.SessionId()
                                    ? "This device"
                                    : "Last seen " + session.LastSeenAt.ToString("g", CultureInfo.CurrentCulture)]
                        ]
                    ])
            ],
            _sessions.Count > 1
                ? Ui.Button.Id("devices-sign-out-others").Class("w-full").OnClick(SignOutOthers)["Sign out every other device"]
                : null,
            Passkeys
        ];

    // A passkey is a second way into this account, so the list says what each one is and offers to take it away.
    private Component? Passkeys =>
        [
            Ui.Separator.Text("Passkeys"),
            _passkeyError is AuthError.None ? null : Error("passkey-error", AuthMessages.For(_passkeyError)),
            _passkeys.Count == 0
                ? Ui.Text["No passkeys yet. Add one to sign in without a password."]
                : Ul.Class(List).Id("passkey-list")[
                    _passkeys.Select(passkey =>
                        Li.Key(passkey.Id).Class(Row)[
                            Div.Class("min-w-0")[
                                Ui.Heading.Class("truncate")[passkey.Name],
                                Ui.Text.Sm[
                                    passkey.LastUsedAt is { } used
                                        ? "Last used " + used.ToString("g", CultureInfo.CurrentCulture)
                                        : "Never used"]
                            ],
                            Ui.Button.Ghost.Xs.OnClick(() => Remove(passkey.Id))["Remove"]
                        ])
                ],
            _passkeysSupported
                ? Form.Model(_passkey).OnSubmit(Add).Class("flex flex-col gap-6")[
                    Ui.Input.Bind(() => _passkey.Name).Id("passkey-name").Label("Name this device"),
                    Ui.Button.Type(Ui.ButtonType.Submit).Id("passkey-add").Class("w-full")["Add a passkey"]
                ]
                : Ui.Text["This browser cannot use passkeys."]
        ];

    // A divided list: one row per device or passkey, its button at the end of the row.
    private const string List = "divide-y divide-zinc-800/10 dark:divide-white/20";

    private const string Row = "flex items-center justify-between gap-4 py-3";

    private async Task Add(PasskeyModel model)
    {
        // The whole ceremony runs inside this click: the browser prompts, the authenticator signs, the server
        // verifies. A dismissed dialog comes back as a refusal rather than an exception.
        var result = await auth.AddPasskey(model.Name);
        _passkeyError = result.Error;

        if (result.Succeeded)
        {
            model.Name = "";
            await Load();
        }
    }

    private async Task Remove(Guid id)
    {
        var result = await auth.RemovePasskey(id);
        _passkeyError = result.Error;
        await Load();
    }

    private async Task SignOutOthers()
    {
        await auth.SignOutOtherDevices();
        _signedOutOthers = true;
        await Load();
    }

    private async Task Load()
    {
        if (users.Current.UserId() is not { } me)
        {
            _sessions = [];
            _passkeys = [];
            return;
        }

        // Read faces, like any other aggregate's: no context to inject, nothing tracked, and each read
        // opens and disposes its own. Your own aggregates work the same way — Product.Where(…).
        _sessions = await Session
            .Where(s => s.UserId == me)
            .OrderByDescending(s => s.LastSeenAt);

        _passkeys = await Passkey
            .Where(p => p.UserId == me)
            .OrderByDescending(p => p.CreatedAt);
    }

    private static string Describe(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent) ? "Unknown browser"
        : userAgent.Length > 80 ? userAgent[..80] + "…"
        : userAgent;
}
