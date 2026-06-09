using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Example.Auth.WasmCookie;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();

// Same-origin HttpClient (served by the host) — the HttpOnly auth cookie rides every request automatically.
host.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });
host.Services.AddSingleton<ApiUserProvider>();
host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<ApiUserProvider>());
host.Services.AddSingleton<WasmLoginService>();

await host.RunAsync<App>();
