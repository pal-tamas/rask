using Company.RaskServer.Features.Shared;

// Every battery is on and every setting lives in appsettings.json under "Rask". An app that does without
// one says so here — app.Configure(c => c.Jobs.Off()) — see docs/configuration.md.
RaskApp.Create(args).Run<App>();
