// This server renders no pages of its own: the app is the WebAssembly build of Client/, served here beside the
// endpoints its messages travel to and the operator console at /_rask. Every battery is on and every setting
// lives in appsettings.json under "Rask". An app that does without one says so here —
// app.Configure(c => c.Jobs.Off()) — see docs/configuration.md.
RaskApp.Create(args).Serve();
