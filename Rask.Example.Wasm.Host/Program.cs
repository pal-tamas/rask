using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Opt into brotli + gzip response compression for the AppBundle. UseRask wires the
// response-compression middleware ahead of UseStaticFiles when this registration is
// present; without it the bundle still serves, just uncompressed.
builder.Services.AddRask();

var app = builder.Build();

app.UseRask();

app.Run();
