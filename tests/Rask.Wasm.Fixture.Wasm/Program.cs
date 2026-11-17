using Microsoft.Extensions.DependencyInjection;
using Rask.Auth.Client;
using Rask.Cqrs.Client;
using Rask.Wasm;
using Rask.Wasm.Fixture.Wasm;

// Who is signed in comes from the host's /api/auth/me, as a Rask.Auth server answers it.
var host = WasmHostBuilder.CreateDefault();
host.Services.AddRaskAuthClient();

// A client of its server, as a wasm-hosted app's browser half is: its messages, and its bell's work (WorkPage), travel
// there. An [OffThread] message stays in this app's own worker.
host.Services.AddRaskCqrsClient();

// English and Hungarian, and the app's own validation messages the .NET 11 way: its shared resources,
// Messages/ValidationMessages.hu.resx, named in AddValidation's LocalizerProvider.
host.UseCulture(c =>
{
    c.SupportedCultures.Add("en");
    c.SupportedCultures.Add("hu");
});
host.Services.AddLocalization(o => o.ResourcesPath = "Messages");
host.Services.AddValidation(o => o.LocalizerProvider = static (_, factory) => factory.Create(typeof(ValidationMessages)));
await host.Run<App>();
