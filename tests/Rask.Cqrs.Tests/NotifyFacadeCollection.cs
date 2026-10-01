namespace Rask.Cqrs.Tests;

// Notify keeps the container it publishes on in a static, and every container that resolves a dispatcher
// replaces it. A test that relies on the configured root therefore cannot run beside any other class that
// builds a container: the publish lands in that one, whose handlers were never given this test's services.
// Parallelization is disabled outright because serialising the members against each other is not enough.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NotifyFacadeCollection
{
    public const string Name = "NotifyFacade";
}
