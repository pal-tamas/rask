using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Example.Components;
using Rask.Example.Wasm.Authentication;
using Rask.Wasm;
using Microsoft.Extensions.DependencyInjection;

var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });
host.Services.AddSingleton<IWeatherForecastService, HttpWeatherForecastService>();

host.Services.AddSingleton<HttpUserProvider>();
host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<HttpUserProvider>());

host.Services.AddSingleton(new RaskAuthorizationOptions
{
    ChallengePath = "/login",
    ForbidPath = "/forbidden"
});

await host.RunAsync<App>();
