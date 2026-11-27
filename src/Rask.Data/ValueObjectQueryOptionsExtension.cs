using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     Registers <see cref="ValueObjectMemberTranslator" />, so a query reads a value object's one value from its column,
///     and <see cref="OptionalComplexValueValidator" />, so an optional value object whose members are all optional is held
///     in its row's columns whatever the provider.
/// </summary>
/// <remarks>
///     Registered whatever the provider: EF Core applies the extensions in no set order, and only a relational
///     provider ever asks for a member translator.
/// </remarks>
internal sealed class ValueObjectQueryOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Adds the extension to <paramref name="optionsBuilder" />, once.</summary>
    internal static void Add(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.Options.FindExtension<ValueObjectQueryOptionsExtension>() is null)
        {
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new ValueObjectQueryOptionsExtension());
        }
    }

    // EF Core's services builder activates the plugin by reflection without a trimming annotation, so its constructor
    // is kept here.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ValueObjectMemberTranslator))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(MomentDayTranslator))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(UtcMomentConventionSetPlugin))]
    public void ApplyServices(IServiceCollection services)
    {
        // The moment's day first: it refuses a part of the time read off a UTC moment's .Value, which the other reads.
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMemberTranslatorPlugin, MomentDayTranslator>()
            .TryAdd<IMemberTranslatorPlugin, ValueObjectMemberTranslator>()
            .TryAdd<IConventionSetPlugin, UtcMomentConventionSetPlugin>();

        // An optional value object whose members are all optional, on a provider Rask does not configure.
        OptionalComplexValueValidator.Register(services);
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:ValueObjectQuery"] = "1";
    }
}
