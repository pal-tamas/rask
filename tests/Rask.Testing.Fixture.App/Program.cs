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
        // No kit: this app has no Tailwind build and writes no precompiled sheet, so with the kit on every page would
        // link a /css/rask-ui.css nothing serves (#1315) — and RaskSiteWalkTests, which walks this app, fails on it.
        c.Ui.Off();
    })
    .Run<App>();
