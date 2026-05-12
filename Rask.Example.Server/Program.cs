using Rask.Example.Shared;
using Rask.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();
builder.Services.AddSingleton(_ =>
    new HttpClient { BaseAddress = new Uri("https://jsonplaceholder.typicode.com/") });

var app = builder.Build();

app.UseStaticFiles();
app.UseRask<App>();

app.Run();
