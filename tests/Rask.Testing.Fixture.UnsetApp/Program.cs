using Rask;
using Rask.Testing.Fixture.UnsetApp;

// As an app reads its production database's address: a boot without it is a mistake, and says so.
_ = Environment.GetEnvironmentVariable(UnsetApp.DatabaseVariable)
    ?? throw new InvalidOperationException($"{UnsetApp.DatabaseVariable} is not set.");

RaskApp.Create(args).Run<App>();
