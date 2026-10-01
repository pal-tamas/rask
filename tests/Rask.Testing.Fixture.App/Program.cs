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
    })
    .Run<App>();
