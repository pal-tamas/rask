using Microsoft.EntityFrameworkCore;
using Rask;
using Rask.Example.Auth;

var app = RaskApp.Create(args);

// THERE IS NO AUTH CODE HERE, AND THAT IS THE POINT. Naming a database is what tells Rask which one the
// batteries belong to; accounts are one of them, so this app can already register somebody, sign them in
// and sign them out. /login, /register and /logout are routed by the framework, the cookie scheme is
// registered for you, and RaskApp puts UseAuthentication/UseAuthorization ahead of UseRask on its own —
// the order the principal has to be populated in, and the mistake RASK024 exists to catch.
//
// To do without any of it: app.Configure(c => c.Auth.Off()).
app.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=auth-sample.db"));

var built = app.Build<App>();

// Two demo accounts, so the sample runs the moment it is cloned. A real app seeds nobody — see AuthSeed.
await AuthSeed.EnsureDemoUsersAsync(built.Services);

await built.RunAsync();
