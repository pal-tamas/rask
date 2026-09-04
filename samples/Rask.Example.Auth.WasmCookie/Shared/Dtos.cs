namespace Rask.Example.Auth.WasmCookie;

// Form-bound model (settable props for two-way binding). The auth request and response shapes are the
// framework's now — Rask.Core.Authentication.AuthApi carries them, so both halves agree on one
// definition rather than each keeping a copy.
public sealed class LoginModel
{
    public string Email { get; set; } = "";

    public string Password { get; set; } = "";
}
