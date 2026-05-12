using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseRask();

app.Run();
