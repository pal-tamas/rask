using Rask;
using Rask.Testing.Fixture.App;

RaskApp.Create(args, builder => builder.Services
        .AddSingleton(new Greeting("Hello from Program.cs"))
        .AddSingleton<Guestbook>())
    .Configure(c =>
    {
        c.Data.Off();
        c.Auth.Off();
        c.Jobs.Off();
        c.Mail.Off();
        c.Cache.Off();
        c.Storage.Off();
        c.Push.Off();
        c.Ops.Off();
        c.Snapshots.Off();
        // The kit stays ON: this app has no Tailwind build and writes no precompiled sheet, and the host serves the
        // /css/rask-ui.css its pages link out of the kit itself (#1315) — RaskSiteWalkTests, which walks this app, fails
        // on any stylesheet that does not answer.
    })
    .Run<App>();
