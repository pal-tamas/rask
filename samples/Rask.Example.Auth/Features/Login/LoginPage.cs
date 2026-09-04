using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Example.Auth.Features;

// THIS PAGE IS OPTIONAL. Rask routes its own /login, /register and /logout, so an app that deletes this
// file still signs people in. It is here to show the other half: declaring a page at a route the
// framework already serves REPLACES the framework's, because an app's own routes are registered first
// and the earlier registration wins a duplicate template.
//
// What it does not replace is the mechanism. auth.SignInAsync is the same call the built-in page makes,
// and on this host it drives the ticket relay — a WebSocket cannot write a Set-Cookie, so signing in
// mints a single-use ticket the browser redeems over ordinary HTTP before the socket reconnects.
[AllowAnonymous]
[Route("login")]
public sealed partial class LoginPage(IAuth auth) : Component
{
    private readonly LoginModel _model = new();
    private string? _error;

    // The cookie middleware appends ?ReturnUrl= on its challenge redirect; QueryParam binds it (case-insensitive).
    [QueryParam] public string? ReturnUrl { get; set; }

    protected override Component? Render() =>
        Div.Id("login").Class("rounded-xl bg-white shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:ring-slate-700 mx-auto").Style("max-width:24rem")[
            Div.Class("p-5")[
                H1.Class("mb-1 text-lg font-semibold text-2xl mb-3")["Sign in"],
                _error is null
                    ? null
                    : Div.Id("login-error").Class("rounded-lg px-4 py-3 text-sm bg-red-50 text-red-900 dark:bg-red-950 dark:text-red-200 py-2")[_error],
                Form.Model(_model).OnValidSubmitAsync(SubmitAsync)[
                    Div.Class("mb-3")[
                        Label.For("email").Class("mb-1 block text-sm font-medium")["Email"],
                        Input.Bind(() => _model.Email).Id("email").Class("w-full rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-900 placeholder:text-slate-400 focus:border-violet-500 focus:outline-none dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100")
                    ],
                    Div.Class("mb-3")[
                        Label.For("password").Class("mb-1 block text-sm font-medium")["Password"],
                        Input.Bind(() => _model.Password).Id("password").Type(InputType.Password).Class("w-full rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-900 placeholder:text-slate-400 focus:border-violet-500 focus:outline-none dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100")
                    ],
                    Button.Type("submit").Id("login-submit").Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-violet-600 text-white hover:bg-violet-500 w-full")["Sign in"]
                ],
                P.Class("text-slate-500 dark:text-slate-400 text-sm mt-3 mb-0")[
                    "Try ada@example.com (admin) or bob@example.com (user) — both with Password1."],
                // Straight to the FRAMEWORK's page. Replacing /login replaces one page, not the flow:
                // /forgot-password, /reset-password and /confirm-email are still there, and the reset
                // email goes out through the mail battery — which, with no SMTP configured, writes it
                // to ./mail-pickup as an .eml you can open.
                //
                // Fully qualified because this file's own namespace generates a Routes of its own, and
                // importing both would make the bare name ambiguous.
                P.Class("text-slate-500 dark:text-slate-400 text-sm mt-2 mb-0")[
                    NavLink
                        .Href(Rask.Auth.Pages.Routes.ForgotPasswordPage())
                        .Id("go-forgot")
                        .Class("text-violet-600 hover:text-violet-500 dark:text-violet-400")[
                            "Forgotten your password?"]]
            ]
        ];

    private async Task SubmitAsync(LoginModel m)
    {
        var result = await auth.SignInAsync(m.Email, m.Password, returnUrl: ReturnUrl ?? "/members");

        if (!result.Succeeded)
        {
            // Deliberately not "no such account": saying which half was wrong turns this page into an
            // account-existence oracle.
            _error = "Wrong email or password.";
        }
    }
}

/// <summary>The form's model. Settable properties, so two-way binding can write to them.</summary>
public sealed class LoginModel
{
    public string Email { get; set; } = "";

    public string Password { get; set; } = "";
}
