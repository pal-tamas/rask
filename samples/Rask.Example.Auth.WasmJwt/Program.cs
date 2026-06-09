using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Example.Auth.WasmJwt;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton<TokenStore>();
// HttpClient with a BearerTokenHandler that attaches the JWT from the TokenStore to every request.
host.Services.AddSingleton(sp =>
    new HttpClient(new BearerTokenHandler(sp.GetRequiredService<TokenStore>()) { InnerHandler = new HttpClientHandler() })
    {
        BaseAddress = new Uri(WasmHostBuilder.BaseAddress)
    });
host.Services.AddSingleton<JwtUserProvider>();
host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<JwtUserProvider>());
host.Services.AddSingleton<JwtLoginService>();

await host.RunAsync<App>();
