using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Shared;
using Rask.Example.Shared.Demos;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton(_ =>
    new HttpClient { BaseAddress = new Uri("https://jsonplaceholder.typicode.com/") });
host.Services.AddSingleton<IBannedWordService, BannedWordService>();

await host.RunAsync<App>();
