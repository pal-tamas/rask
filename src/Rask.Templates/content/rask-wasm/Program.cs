using Company.RaskWasm;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wasm;
//#if (auth)
using Rask.Core.Authentication;
//#endif

// PathBase is auto-detected at boot from <base href>. For sub-path deploys
// (e.g. GH Pages at https://<user>.github.io/<repo>/), publish with
// /p:RaskPathBase=/<repo> — the framework rewrites the published
// index.html's <base href> so the runtime picks up the prefix on first paint
// and head-emitted asset URLs are scoped under /<repo>/_rask/a/{hash}.{ext}. Override
// explicitly via WasmHostBuilder.CreateDefault(o => o.PathBase = "/myapp")
// if you need to set it from .NET code.
var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton<IWeatherForecastService, LocalWeatherForecastService>();
//#if (auth)

// A standalone SPA has no host of its own — point this at YOUR auth API (CORS-enabled).
const string authApiBaseAddress = "https://api.example.com/"; // TODO: your auth API
host.Services.AddSingleton<TokenStore>();
host.Services.AddSingleton(sp =>
    new HttpClient(new BearerTokenHandler(sp.GetRequiredService<TokenStore>()) { InnerHandler = new HttpClientHandler() })
    {
        BaseAddress = new Uri(authApiBaseAddress)
    });
host.Services.AddSingleton<JwtUserProvider>();
host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<JwtUserProvider>());
host.Services.AddSingleton<JwtLoginService>();
//#endif

await host.RunAsync<App>();
