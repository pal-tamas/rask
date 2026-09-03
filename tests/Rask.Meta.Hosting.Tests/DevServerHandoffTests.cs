using Microsoft.Extensions.DependencyInjection;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     What the host does when <c>rask dev</c> says a dev server is already running the front end.
/// </summary>
/// <remarks>
///     <para>
///         A dev session has no built front end — the CLI passes <c>RaskMetaBuild=false</c> so a full
///         production build of Nuxt or Next does not run on every save — so without this the supervisor
///         refuses to start and takes the session down before its first page.
///     </para>
///     <para>
///         In that session the framework's own dev server is the front end, which is the case
///         <see cref="MetaHostingOptions.SuperviseNode" /> already describes. All that was missing was
///         the port, and the port is known only to the process that started it.
///     </para>
/// </remarks>
[Collection(MetaHostCollection.Name)]
public sealed class DevServerHandoffTests
{
    [Fact]
    public void A_dev_server_url_stops_supervision_and_points_at_it()
    {
        var options = new MetaHostingOptions { Port = 8123 };

        RaskMetaServiceCollectionExtensions.ApplyDevServer(options, _ => "http://localhost:5173");

        Assert.False(options.SuperviseNode);
        Assert.Equal(5173, options.Port);
    }

    [Fact]
    public void A_bare_port_is_accepted_too()
    {
        var options = new MetaHostingOptions();

        RaskMetaServiceCollectionExtensions.ApplyDevServer(options, _ => "3000");

        Assert.False(options.SuperviseNode);
        Assert.Equal(3000, options.Port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yes")]
    [InlineData("localhost")]
    [InlineData("0")]
    [InlineData("99999")]
    public void Anything_that_is_not_an_address_leaves_the_host_alone(string? value)
    {
        // A deployed app has this unset, and an app that inherited a stray value must not quietly stop
        // supervising the process it is deployed to run.
        var options = new MetaHostingOptions { Port = 8123 };

        RaskMetaServiceCollectionExtensions.ApplyDevServer(options, _ => value);

        Assert.True(options.SuperviseNode);
        Assert.Equal(8123, options.Port);
    }

    [Fact]
    public void It_reads_the_variable_the_CLI_writes()
    {
        string? asked = null;

        RaskMetaServiceCollectionExtensions.ApplyDevServer(new MetaHostingOptions(), name =>
        {
            asked = name;
            return null;
        });

        // The name is the contract with `rask dev`, which sets it in the environment it hands to
        // dotnet watch. Nothing else connects the two halves, so nothing else would notice a rename.
        Assert.Equal("RASK_META_DEV", asked);
    }

    [Fact]
    public void The_dev_session_wins_over_a_port_the_app_pinned()
    {
        // The one place the usual precedence is inverted, and deliberately: an app that pins o.Port for
        // production would otherwise defeat every dev session on a framework whose dev server listens
        // somewhere else — while this value is set by the dev tool for the life of one session.
        try
        {
            Environment.SetEnvironmentVariable("RASK_META_DEV", "http://localhost:5173");

            var services = new ServiceCollection();
            services.AddRaskMeta(o =>
            {
                o.Framework = MetaFramework.Nuxt;
                o.Port = 8123;
            });

            var options = services.BuildServiceProvider().GetRequiredService<MetaHostingOptions>();

            Assert.False(options.SuperviseNode);
            Assert.Equal(5173, options.Port);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RASK_META_DEV", null);
        }
    }
}
