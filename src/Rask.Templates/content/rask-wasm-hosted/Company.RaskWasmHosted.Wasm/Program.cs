using Company.RaskWasmHosted.Wasm;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wasm;
//#if (auth)
using Rask.Core.Authentication;
//#endif

// PathBase is auto-detected from <base href>. Publish with /p:RaskPathBase=/myapp
// for sub-path deploys, or override via CreateDefault(o => o.PathBase = "/myapp").
var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });
host.Services.AddSingleton<IWeatherForecastService, HttpWeatherForecastService>();
//#if (auth)
// Hydrates the user from the host's /api/me (HttpOnly cookie); WasmLoginService drives sign-in/out.
host.Services.AddSingleton<ApiUserProvider>();
host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<ApiUserProvider>());
host.Services.AddSingleton<WasmLoginService>();
//#endif

await host.RunAsync<App>();
