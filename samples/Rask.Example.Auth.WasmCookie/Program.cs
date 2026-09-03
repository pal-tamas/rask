using Microsoft.Extensions.DependencyInjection;
using Rask.Auth.Client;
using Rask.Cqrs.Client;
using Rask.Example.Auth.WasmCookie;
using Rask.Wasm;

var host = WasmHostBuilder.CreateDefault();

// Same-origin HttpClient (served by the host) — the HttpOnly auth cookie rides every request automatically.
host.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });
// The browser half of Rask.Auth. One call replaces the IUserProvider this sample used to hand-write
// and the login service beside it: IAuth posts to the host's /api/auth endpoints, IUserProvider reads
// /api/auth/me, and the current user is loaded before the first render so no page paints anonymous and
// then flips.
host.Services.AddRaskAuthClient();

// Remote dispatch. One line, and every message this bundle has a contract for travels to the host over
// the HttpClient above — same origin, so the HttpOnly auth cookie rides each request and the endpoint
// sees the signed-in user without being told who that is.
//
// Resolved lazily: BaseAddress reads the page origin back through the JS module, which only answers
// once RunAsync has imported it, so the client is built when the first dispatch asks for it.
host.Services.AddRaskCqrsClient();

await host.RunAsync<App>();
