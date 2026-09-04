using Microsoft.EntityFrameworkCore;
using Rask.Auth;
using Rask.Cqrs.Server;
using Rask.Example.Auth.WasmCookie.Host;
using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Accounts, on ASP.NET Core Identity: real password hashing, lockout, and the /api/auth endpoints the
// browser bundle talks to. The cookie scheme comes with it, so nothing here registers one by hand.
builder.Services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite("Data Source=wasmcookie-auth.db"));
builder.Services.AddRaskAuth<AuthDbContext>();
builder.Services.AddRask(); // Rask.Wasm.Hosting — response compression for the AppBundle

// The endpoint half of remote dispatch, plus what its handlers need. One call registers Rask.Cqrs and
// every handler in this assembly; the browser bundle's AddRaskCqrsClient() is the other side of it.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<VisitCounter>();
builder.Services.AddRaskCqrsServer();

var app = builder.Build();

// Populates HttpContext.User from the cookie so /api/me reflects the signed-in user. (No UseAuthorization
// — there are no [Authorize] endpoints; the WASM client gates content with the Authorize component.)
app.UseAuthentication();

// The auth API the browser bundle talks to: register, login, logout and me, under /api/auth. Same
// origin, so the HttpOnly cookie rides every request and no token ever reaches JavaScript.
//
// BEFORE UseRask, whose catch-all serves the bundle for anything unmatched and would otherwise answer
// these with the app shell.
app.MapRaskAuth();

// Answers the messages the bundle dispatches: GET and POST on /_rask/cqrs/request/{name}, the verb
// carrying what IQuery and ICommand already declare. BEFORE UseRask, whose catch-all serves the bundle
// for anything unmatched and would otherwise answer these with the app shell.
app.MapRaskCqrs();

// A demo account, so the sample can be signed into the moment it is cloned. A real app seeds nobody:
// the first person to register becomes the administrator, and while no account exists that registration
// wants the one-time token from the startup log.
await AuthSeed.EnsureDemoUserAsync(app.Services);

app.UseRask(); // serve the published WASM AppBundle

app.Run();
