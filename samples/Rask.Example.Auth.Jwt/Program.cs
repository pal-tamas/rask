using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Rask.Example.Auth.Jwt;
using Rask.Server;

var builder = WebApplication.CreateBuilder(args);

// The JWT is held in ProtectedSessionStorage — encrypted at rest via ASP.NET Data Protection, stored in the
// browser's sessionStorage, decrypted server-side. The raw token never appears in JS, in a cookie, or in the
// WebSocket URL. Login validates it into a principal and sets it on the live session directly.
builder.Services.AddDataProtection();
builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();
builder.Services.AddSingleton<JwtIssuer>();
builder.Services.AddSingleton<JwtValidator>();
builder.Services.AddRask();

var app = builder.Build();

app.MapStaticAssets();
app.UseRouting();
app.UseRask<App>();

app.Run();
