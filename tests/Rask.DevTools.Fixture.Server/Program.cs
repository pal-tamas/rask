using Rask.DevTools.Fixture.Server;
using Rask.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRask();

var app = builder.Build();
app.UseRask<App>();
app.Run();
