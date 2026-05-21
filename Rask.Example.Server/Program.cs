using Rask.Example.Shared;
using Rask.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();
builder.Services.AddExampleServices();

var app = builder.Build();

app.UseStaticFiles();
app.UseRask<App>();

app.Run();
