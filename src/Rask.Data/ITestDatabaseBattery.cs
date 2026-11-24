using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     A battery that adds to a <see cref="TestDatabase" />'s services what it adds to an app's — announced by the
///     battery's own assembly, which Rask.Data must not reference.
/// </summary>
internal interface ITestDatabaseBattery
{
    /// <summary>Registers into <paramref name="services" /> what an app over <paramref name="model" /> would have.</summary>
    void Wire<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(IServiceCollection services, IModel model)
        where TContext : DbContext;
}
