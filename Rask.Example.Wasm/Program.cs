using Rask.Example.Shared;
using Rask.Wasm;
using Microsoft.Extensions.DependencyInjection;

var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton(_ =>
    new HttpClient { BaseAddress = new Uri("https://jsonplaceholder.typicode.com/") });

await host.RunAsync<App>();
