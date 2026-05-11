using System.Net.Http.Json;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Example.Authentication;
using Microsoft.AspNetCore.Authorization;
using static Rask.Core.Tags;

namespace Rask.Example.Wasm.Authentication;

[Route("/login")]
[AllowAnonymous]
public sealed class LoginPage(HttpClient http, Navigator navigator, HttpUserProvider users) : Component
{
    private readonly LoginModel _model = new();
    private bool _error;
    private bool _submitting;

    [QueryParam("returnUrl")] public string? ReturnUrl { get; set; }

    public override Component Render() =>
        Div(Class: "row justify-content-center", Children:
        [
            Div(Class: "col-md-5", Children:
            [
                H1(Class: "h3 mb-4", Children: ["Sign in"]),
                _error
                    ? Div(Class: "alert alert-danger", Children: ["Invalid username or password."])
                    : Fragment(),
                Form(Model: _model, OnValidSubmitAsync: HandleSubmitAsync, Children:
                [
                    Div(Class: "mb-3", Children:
                    [
                        Label(For: "username", Class: "form-label", Children: ["Username"]),
                        Input(Bind: () => _model.Username, Name: "username", Id: "username",
                            Class: "form-control", Required: true, Autofocus: true),
                        ValidationMessage(For: () => _model.Username, Class: "invalid-feedback d-block")
                    ]),
                    Div(Class: "mb-3", Children:
                    [
                        Label(For: "password", Class: "form-label", Children: ["Password"]),
                        Input(Bind: () => _model.Password, Type: "password", Name: "password", Id: "password",
                            Class: "form-control", Required: true),
                        ValidationMessage(For: () => _model.Password, Class: "invalid-feedback d-block")
                    ]),
                    Button(Type: "submit", Class: "btn btn-primary", Disabled: _submitting,
                        Children: [_submitting ? "Signing in…" : "Sign in"])
                ]),
                P(Class: "form-text mt-3 mb-0", Children:
                [
                    "Try ", Strong(Children: ["alice"]), " / ", Strong(Children: ["password"]),
                    " or ", Strong(Children: ["bob"]), " / ", Strong(Children: ["password"]), "."
                ])
            ])
        ]);

    private async Task HandleSubmitAsync(LoginModel model)
    {
        _submitting = true;
        _error = false;
        try
        {
            var safeReturnUrl = string.IsNullOrEmpty(ReturnUrl) ? "/" : ReturnUrl;
            var resp = await http.PostAsJsonAsync(
                "auth/login",
                new LoginRequest(model.Username, model.Password, safeReturnUrl));
            if (!resp.IsSuccessStatusCode)
            {
                _error = true;
                return;
            }

            var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
            await users.RefreshAsync();
            navigator.Navigate(body?.RedirectUrl ?? safeReturnUrl);
        }
        finally
        {
            _submitting = false;
        }
    }

    private sealed record LoginRequest(string? Username, string? Password, string ReturnUrl);

    private sealed record LoginResponse(bool Success, string? RedirectUrl);
}
