using Microsoft.JSInterop;

namespace Rask.Example.Auth.WasmJwt;

// Holds the bearer JWT in localStorage (survives a refresh) plus an in-memory copy the DelegatingHandler
// reads synchronously. Plain localStorage per the sample's choice — note the XSS caveat: a token in JS
// storage is readable by any script on the page. For maximum security prefer an HttpOnly cookie.
public sealed class TokenStore(IJSRuntime js)
{
    public string? Token { get; private set; }

    public async Task InitAsync() => Token = await js.InvokeAsync<string?>("localStorage.getItem", "rask.jwt");

    public async Task SetAsync(string token)
    {
        Token = token;
        await js.InvokeVoidAsync("localStorage.setItem", "rask.jwt", token);
    }

    public async Task ClearAsync()
    {
        Token = null;
        await js.InvokeVoidAsync("localStorage.removeItem", "rask.jwt");
    }
}
