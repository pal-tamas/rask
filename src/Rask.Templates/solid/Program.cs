using Company.RaskServer.Features.Hello;

// This server renders no pages of its own: the app is the front end in client/, built by its own toolchain and
// served here beside the endpoints its messages travel to, the accounts API at /api/auth and the operator
// console at /_rask. Every battery is on and every setting lives in appsettings.json under "Rask". An app that
// does without one says so here — app.Configure(c => c.Jobs.Off()) — see docs/configuration.md.
var app = RaskApp.Create(args);

// The starter's greeting counts visits in memory, so it answers before the app has a table of its own.
app.Services.AddSingleton<VisitCounter>();

app.Serve();
