namespace Rask.Batteries.Generators.Tests;

/// <summary>The design-time factory <c>dotnet ef</c> builds an app's own context through, from its configuration.</summary>
/// <remarks>
/// The server host's two seams — <c>UseRaskDatabase</c> and <c>WebApplication</c> — are stubbed by name rather than
/// referenced: the generator gates on their names, and pulling Rask.Server and ASP.NET Core into this harness would hand
/// every other generator in the suite a different compilation. The real ones are compiled against by every app, and by
/// Rask.Server.Tests, which runs this generator as an analyzer.
/// </remarks>
public class DesignTimeFactoryGeneratorTests
{
    private const string ServerHost = """
        namespace Microsoft.AspNetCore.Builder
        {
            public sealed class WebApplicationOptions
            {
                public string[]? Args { get; init; }
                public string? ApplicationName { get; init; }
            }

            public sealed class WebApplicationBuilder
            {
                public Microsoft.Extensions.Configuration.IConfiguration Configuration => null!;
            }

            public sealed class WebApplication
            {
                public static WebApplicationBuilder CreateBuilder(WebApplicationOptions options) => null!;
            }
        }

        namespace Rask
        {
            public static class RaskDatabaseDbContextOptionsExtensions
            {
                public static Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TContext> UseRaskDatabase<TContext>(
                    this Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TContext> optionsBuilder, System.IServiceProvider services)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => optionsBuilder;

                public static Microsoft.EntityFrameworkCore.DbContextOptionsBuilder UseRaskDatabase(
                    this Microsoft.EntityFrameworkCore.DbContextOptionsBuilder optionsBuilder, System.IServiceProvider services) =>
                    optionsBuilder;
            }

            public class RaskAppDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<RaskAppDbContext> options)
                : Microsoft.EntityFrameworkCore.DbContext(options);
        }

        namespace Rask.Server
        {
            public static class RaskDataHostExtensions
            {
                public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddRaskData<TContext>(
                    this Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    System.Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> configure)
                    where TContext : Microsoft.EntityFrameworkCore.DbContext => services;
            }
        }

        """;

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(ServerHost + source, new DesignTimeFactoryGenerator(), "Rask.Data", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_context_the_app_declares_is_built_from_the_configured_database()
    {
        var source = """
            namespace Shop
            {
                public sealed class ShopDb(Microsoft.EntityFrameworkCore.DbContextOptions<ShopDb> options)
                    : Microsoft.EntityFrameworkCore.DbContext(options);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratedCompileErrors());
        var factory = run.GeneratedSource("__RaskDesignTimeFactory.Shop.ShopDb");
        Assert.Contains(
            "internal sealed class __RaskShopDbDesignTimeFactory : "
            + "global::Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<global::Shop.ShopDb>",
            factory,
            StringComparison.Ordinal);
        Assert.Contains("global::Rask.RaskDatabaseDbContextOptionsExtensions.UseRaskDatabase(", factory, StringComparison.Ordinal);
        Assert.Contains("ApplicationName = typeof(global::Shop.ShopDb).Assembly.GetName().Name,", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("is not set", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void A_context_taking_untyped_options_is_built_too()
    {
        var source = """
            namespace Shop
            {
                public class Outer
                {
                    internal sealed class ShopDb(Microsoft.EntityFrameworkCore.DbContextOptions options)
                        : Microsoft.EntityFrameworkCore.DbContext(options);
                }
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "internal sealed class __RaskOuter_ShopDbDesignTimeFactory",
            run.GeneratedSource("__RaskDesignTimeFactory.Shop.Outer.ShopDb"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_context_the_app_wrote_a_factory_for_gets_none()
    {
        var source = """
            namespace Shop
            {
                public sealed class ShopDb(Microsoft.EntityFrameworkCore.DbContextOptions<ShopDb> options)
                    : Microsoft.EntityFrameworkCore.DbContext(options);

                public sealed class ShopDbAtDesignTime : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<ShopDb>
                {
                    public ShopDb CreateDbContext(string[] args) => null!;
                }
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(
            run.RunResult.Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName.Contains("__RaskDesignTimeFactory", StringComparison.Ordinal));
    }

    [Fact]
    public void The_context_a_RaskApp_builds_from_its_battery_switches_gets_none()
    {
        var source = """
            namespace Shop
            {
                public sealed class ShopDb(Microsoft.EntityFrameworkCore.DbContextOptions<Rask.RaskAppDbContext> options)
                    : Rask.RaskAppDbContext(options);

                public sealed class Seeded : Microsoft.EntityFrameworkCore.DbContext
                {
                    public Seeded(int seed) { }
                }
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(
            run.RunResult.Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName.Contains("__RaskDesignTimeFactory", StringComparison.Ordinal));
    }

    [Fact]
    public void A_context_wired_by_hand_with_its_own_provider_needs_the_setting_named()
    {
        var source = """
            namespace Shop
            {
                using Rask;
                using Rask.Server;

                public sealed class ShopDb(Microsoft.EntityFrameworkCore.DbContextOptions<ShopDb> options)
                    : Microsoft.EntityFrameworkCore.DbContext(options);

                public sealed class LogDb(Microsoft.EntityFrameworkCore.DbContextOptions<LogDb> options)
                    : Microsoft.EntityFrameworkCore.DbContext(options);

                public static class Wiring
                {
                    public static void Wire(Microsoft.Extensions.DependencyInjection.IServiceCollection services, System.IServiceProvider sp)
                    {
                        services.AddRaskData<ShopDb>(o => o.EnableDetailedErrors());
                        services.AddRaskData<LogDb>(o => o.UseRaskDatabase(sp));
                    }
                }
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "Rask:Database:Provider is not set, so dotnet ef cannot tell which database ShopDb's migrations are for",
            run.GeneratedSource("__RaskDesignTimeFactory.Shop.ShopDb"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("is not set", run.GeneratedSource("__RaskDesignTimeFactory.Shop.LogDb"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_context_its_options_constructor_cannot_finish_gets_none()
    {
        var source = """
            namespace Shop
            {
                public sealed class TenantDb(Microsoft.EntityFrameworkCore.DbContextOptions<TenantDb> options)
                    : Microsoft.EntityFrameworkCore.DbContext(options)
                {
                    public required string Tenant { get; init; }
                }

                public sealed class OldDb : Microsoft.EntityFrameworkCore.DbContext
                {
                    [System.Obsolete("Pass a tenant.")]
                    public OldDb(Microsoft.EntityFrameworkCore.DbContextOptions<OldDb> options) : base(options) { }
                }
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(
            run.RunResult.Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName.Contains("__RaskDesignTimeFactory.Shop.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_compilation_without_the_server_host_gets_none()
    {
        var source = """
            namespace Shop
            {
                public sealed class ShopDb(Microsoft.EntityFrameworkCore.DbContextOptions<ShopDb> options)
                    : Microsoft.EntityFrameworkCore.DbContext(options);
            }
            """;

        var run = GeneratorHarness.Run(source, new DesignTimeFactoryGenerator(), "Rask.Data", "Microsoft.EntityFrameworkCore");

        Assert.Empty(run.RunResult.Results.SelectMany(static r => r.GeneratedSources));
    }
}
