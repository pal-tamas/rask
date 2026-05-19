namespace Rask.Core.Tests.Lifecycle;

// Test classes that redirect Console.Error / Console.Out via Console.SetError / SetOut
// share global static state — running them in parallel produces a flake where one test
// captures the other's output (or nothing at all). Group them under one collection so
// xUnit serialises them. Add this collection's [Collection] attribute to any new test
// class that calls Console.SetError / Console.SetOut.
[CollectionDefinition("ConsoleRedirect", DisableParallelization = true)]
public class ConsoleRedirectCollection
{
}
