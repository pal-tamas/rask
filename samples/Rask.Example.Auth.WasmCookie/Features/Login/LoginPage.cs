using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmCookie.Features;

[AllowAnonymous]
[Route("login")]
public sealed partial class LoginPage(IAuth auth) : Component
{
    private readonly LoginModel _model = new();
    private string? _error;

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
                    "Try ada@example.com / Password1 — the demo account this sample seeds."]
            ]
        ];

    private async Task SubmitAsync(LoginModel m)
    {
        // The same call a server-rendered page makes. In the browser it posts to the host's
        // /api/auth/login, refreshes IUserProvider so every component reading the current user
        // re-renders, and navigates — none of which this page has to know.
        var result = await auth.SignInAsync(m.Email, m.Password, returnUrl: ReturnUrl ?? "/members");

        if (!result.Succeeded)
        {
            _error = "Wrong email or password.";
        }
    }
}
