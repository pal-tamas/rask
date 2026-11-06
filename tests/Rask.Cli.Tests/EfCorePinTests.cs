namespace Rask.Cli.Tests;

/// <summary>
///     The EF Core version the build stamps onto the CLI, which every EF package it puts into an app is pinned to.
/// </summary>
public sealed class EfCorePinTests
{
    [Fact]
    public void The_stamped_EF_Core_version_is_the_one_Directory_Packages_props_pins()
    {
        var pinned = RepoPins.Package("Microsoft.EntityFrameworkCore");

        var stamped = EfCorePin.Version;

        Assert.Equal(pinned, stamped);
    }
}
