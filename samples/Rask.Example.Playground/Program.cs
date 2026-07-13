using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Playground;
using Rask.Example.Playground.Compiler;
using Rask.Wasm;

// The playground compiles Rask component C# entirely in the browser (Roslyn + the Rask source generator)
// and renders the result live inside its own component tree — no server round-trip. See docs/playground.md.
var host = WasmHostBuilder.CreateDefault();

// The in-browser compiler downloads the shipped _framework assemblies to use as Roslyn metadata
// references; HttpClient's base carries any sub-path (the GitHub Pages /Rask/playground/ prefix). Read
// BaseAddress lazily inside the factory — it resolves only after RunAsync imports the JS module.
host.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });
host.Services.AddSingleton<WasmReferenceLoader>();

await host.RunAsync<PlaygroundApp>();
