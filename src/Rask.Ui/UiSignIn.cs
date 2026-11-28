using Rask.Core.Authentication;
using Rask.Core.Browser;
using Rask.Core.Globalization;
using Rask.Core.Routing;
using Rask.Wire;

namespace Rask;

/// <summary>
///     The sign-in card: a name, a password and a submit button on a plain card in the middle of the page, signing in
///     through <see cref="IAuth" />. The app places it on its own <c>/login</c> page.
/// </summary>
/// <remarks>
///     <code>
///     [AllowAnonymous, Guest, Route("/login")]
///     public sealed partial class LoginPage : Component
///     {
///         protected override Component? Render() => Ui.SignIn.Title("PPTA");
///     }
///
///     Ui.SignIn.Title("ESZAF").Logo("/logo.svg").NameLabel("Felhasználónév")   // an app that signs in by name
///     Ui.SignIn.Remember().Passkey()[Ui.Text[Ui.Link.Href("/register")["Create an account"]]]   // more under the form
///     </code>
///     <para>
///     A refusal is said under the form, in the words of the string catalog (<c>SignIn*</c>), and never says which half
///     was wrong. <c>?returnUrl=</c> — what Rask's challenge appends — is where a signed-in visitor goes on to, and only an
///     address of this site is followed (<see cref="LocalUrl" />); anything else lands on the root. The throttle and the
///     slowdown are <see cref="IAuth" />'s own: once they stop further tries, the card says so.
///     </para>
///     <para>
///     Not a Flux component: Flux's card, heading, inputs and button composed as Flux's own sign-in example lays them out.
///     The ids (<c>email</c>, <c>password</c>, <c>login-submit</c>, <c>login-error</c>) are what
///     <c>Rask.Testing.Playwright</c>'s sign-in drives.
///     </para>
/// </remarks>
public sealed partial class UiSignIn(IAuth auth, RouteState route, IWebAuthn webAuthn) : Component
{
    // What Rask's challenge appends (RaskServerOptions.SignInPath); the query is read case-insensitively, so the cookie
    // handler's own ReturnUrl is the same parameter.
    private const string ReturnUrlParameter = "returnUrl";

    private readonly UiSignInEntered _entered = new();
    private AuthError _refusal;
    private bool _passkeysSupported;

    /// <summary>The heading: the app's name. "Sign in" when unset.</summary>
    public new string? Title { get; set; }

    /// <summary>The app's mark above the heading: a string is an image's address, anything else is drawn as it is.</summary>
    public Component? Logo { get; set; }

    /// <summary>
    ///     The label of the first field, for an app whose accounts sign in by name (<c>[SignInName]</c>):
    ///     <c>.NameLabel("Felhasználónév")</c>. Unset, the field is the email address.
    /// </summary>
    public string? NameLabel { get; set; }

    /// <summary>Offers "Remember me": a session that outlives the browser session. Off unless set.</summary>
    public bool? Remember { get; set; }

    /// <summary>Offers signing in with a passkey beside the password, where the browser supports one. Off unless set.</summary>
    public bool? Passkey { get; set; }

    /// <inheritdoc />
    protected override async Task OnFirstRender()
    {
        // The support check is JavaScript, so it waits for a browser: on the first render this card is HTML on its way out.
        if (Passkey == true)
        {
            _passkeysSupported = await webAuthn.IsSupported().ConfigureAwait(false);
            await StateHasChangedAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex min-h-dvh flex-col items-center justify-center bg-white px-4 py-12 dark:bg-zinc-800").Attributes(("data-ui-sign-in", null))[
            Ui.Card.Class("w-full max-w-sm")[
                Div.Class("flex flex-col gap-6")[
                    Header(),
                    Form.Model(_entered).OnSubmit(Submit).Class("flex flex-col gap-6")[
                        NameField(),
                        Ui.Input.Bind(() => _entered.Password).Id("password").Type(InputType.Password).Viewable()
                            .Label(RaskStrings.Get(RaskString.SignInPassword, "Password"))
                            .Attributes(("autocomplete", "current-password")),
                        Remember == true
                            ? Ui.Checkbox.Bind(() => _entered.Remember).Id("remember").Label(RaskStrings.Get(RaskString.SignInRemember, "Remember me"))
                            : null,
                        Ui.Button.Primary.Type(Ui.ButtonType.Submit).Id("login-submit").Class("w-full")[RaskStrings.Get(RaskString.SignInSubmit, "Sign in")]
                    ],
                    Refusal(),
                    _passkeysSupported
                        ? Div.Class("flex flex-col gap-6")[
                            Ui.Separator.Text(RaskStrings.Get(RaskString.SignInOr, "or")),
                            Ui.Button.Id("login-passkey").Class("w-full").OnClick(SignInWithPasskey)[RaskStrings.Get(RaskString.SignInPasskey, "Sign in with a passkey")]
                        ]
                        : null,
                    Children is { } more ? Div.Class("space-y-2 text-center")[more] : null
                ]
            ]
        ];

    private Component Header() =>
        Div.Class("flex flex-col items-center gap-4 text-center")[
            Logo switch
            {
                null => null,
                global::Rask.Core.Components.Text { Value: var src } => Img.Src(src).Alt("").Class("h-12 max-w-full").Attributes(UiChromeImage.NotDragged),
                var mark => Div.Class("flex h-12 items-center justify-center")[mark],
            },
            Ui.Heading.Level(1).Xl[Title ?? RaskStrings.Get(RaskString.SignInHeading, "Sign in")]
        ];

    // By name: plain text, as typed. By address: an email field. Either way it is the password manager's "username".
    private UiInput<string> NameField() => NameLabel is { } label
        ? Ui.Input.Bind(() => _entered.Name).Id("email").Label(label).Autofocus(true)
            .Attributes(("autocomplete", "username"), ("autocapitalize", "none"), ("spellcheck", "false"))
        : Ui.Input.Bind(() => _entered.Name).Id("email").Type(InputType.Email).Autofocus(true)
            .Label(RaskStrings.Get(RaskString.SignInEmail, "Email"))
            .Attributes(("autocomplete", "username"));

    private Component? Refusal() => _refusal is AuthError.None
        ? null
        : Ui.Callout.Danger.Icon(Ui.IconName.XCircle).Role("alert").Id("login-error")[Ui.CalloutHeading[Sentence(_refusal)]];

    // A wrong name and a wrong password read alike: saying which would tell anybody which accounts exist here.
    private string Sentence(AuthError refusal) => refusal switch
    {
        AuthError.TooManyAttempts => RaskStrings.Get(RaskString.SignInTooManyAttempts, "Too many attempts. Wait a minute and try again."),
        AuthError.NotAllowed => RaskStrings.Get(RaskString.SignInNotAllowed, "This account is not allowed to sign in."),
        AuthError.EmailNotConfirmed => RaskStrings.Get(
            RaskString.SignInEmailNotConfirmed, "Confirm your email address before signing in. The link was sent when you registered."),
        AuthError.PasskeyRejected => RaskStrings.Get(
            RaskString.SignInPasskeyRejected, "That passkey could not be used. Try again, or use your password."),
        _ when NameLabel is not null => RaskStrings.Get(RaskString.SignInWrongName, "Wrong name or password."),
        _ => RaskStrings.Get(RaskString.SignInWrongEmail, "Wrong email or password."),
    };

    private async Task Submit()
    {
        var password = _entered.Password;
        _entered.Password = "";
        var result = await auth.SignIn(_entered.Name.Trim(), password, _entered.Remember, ReturnUrl()).ConfigureAwait(false);
        _refusal = result.Error;
    }

    // No name and no password: the authenticator offers the accounts it holds for this site.
    private async Task SignInWithPasskey()
    {
        var result = await auth.SignInWithPasskey(_entered.Remember, ReturnUrl()).ConfigureAwait(false);
        _refusal = result.Error;
    }

    // Only an address of this site: a crafted link cannot make the card an open redirect.
    private string ReturnUrl() =>
        LocalUrl.Sanitize(route.Query.TryGetValue(ReturnUrlParameter, out var asked) ? asked.ToString() : null);
}
