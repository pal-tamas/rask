using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Shared;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton(_ =>
    new HttpClient { BaseAddress = new Uri("https://jsonplaceholder.typicode.com/") });

await host.RunAsync<App>();
