namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     Pins each framework's built server entry and the environment variables it reads.
/// </summary>
/// <remarks>
///     These look like restatements of constants, and that is exactly what makes them worth having.
///     Every value here was read out of the framework's own current documentation rather than
///     remembered, and each one is load-bearing at a moment when nothing else can check it: the
///     supervisor executes <see cref="MetaFramework.ServerEntry" /> in a container, and a wrong path or
///     a wrong variable name produces a process that either never starts or starts listening somewhere
///     nobody forwards to. A test is the only place that knowledge survives the next person's
///     reasonable-sounding guess.
/// </remarks>
public class MetaFrameworkTests
{
    /// <summary>
    ///     The four Nitro-based frameworks converge on one entry path.
    /// </summary>
    /// <remarks>
    ///     This convergence is the whole reason the adapter seam is data rather than a class per
    ///     framework — six frameworks, three server shapes, and four of them identical here.
    /// </remarks>
    [Theory]
    [InlineData("nuxt")]
    [InlineData("tanstack-start")]
    [InlineData("solidstart")]
    public void Nitro_frameworks_share_one_server_entry(string name)
    {
        var framework = name switch
        {
            "nuxt" => MetaFramework.Nuxt,
            "tanstack-start" => MetaFramework.TanStackStart,
            _ => MetaFramework.SolidStart,
        };

        Assert.Equal(name, framework.Name);
        Assert.Equal(".output/server/index.mjs", framework.ServerEntry);
    }

    /// <summary>Analog is Nitro too, but does not emit under <c>.output</c>.</summary>
    [Fact]
    public void Analog_emits_under_its_own_dist_directory()
    {
        Assert.Equal("dist/analog/server/index.mjs", MetaFramework.Analog.ServerEntry);
    }

    /// <summary>SvelteKit's <c>adapter-node</c> entry.</summary>
    [Fact]
    public void SvelteKit_uses_the_adapter_node_entry()
    {
        Assert.Equal("build/index.js", MetaFramework.SvelteKit.ServerEntry);
    }

    /// <summary>
    ///     Next reads <c>HOSTNAME</c> where everything else reads <c>HOST</c>.
    /// </summary>
    /// <remarks>
    ///     The single most consequential one-word difference in this package. Get it wrong and the
    ///     standalone server ignores the bind address it was given and listens on <c>0.0.0.0</c> — so a
    ///     container that publishes a port exposes an unauthenticated renderer beside the app, and
    ///     everything still appears to work.
    /// </remarks>
    [Fact]
    public void Next_reads_HOSTNAME_rather_than_HOST()
    {
        Assert.Equal("HOSTNAME", MetaFramework.Next.HostVariable);
        Assert.Equal("server.js", MetaFramework.Next.ServerEntry);
    }

    /// <summary>Everything else reads <c>HOST</c>, and all six read <c>PORT</c>.</summary>
    [Fact]
    public void Every_other_framework_reads_HOST_and_all_read_PORT()
    {
        MetaFramework[] nitro =
        [
            MetaFramework.Nuxt,
            MetaFramework.TanStackStart,
            MetaFramework.SolidStart,
            MetaFramework.Analog,
            MetaFramework.SvelteKit,
        ];

        Assert.All(nitro, f => Assert.Equal("HOST", f.HostVariable));
        Assert.All([.. nitro, MetaFramework.Next], f => Assert.Equal("PORT", f.PortVariable));
    }

    /// <summary>
    ///     A preset can be adjusted without restating it.
    /// </summary>
    /// <remarks>
    ///     The reason <see cref="MetaFramework" /> is a record: an app whose build lands somewhere
    ///     unusual changes the one field that differs and keeps everything else the preset knows.
    /// </remarks>
    [Fact]
    public void A_preset_can_be_customised_with_a_with_expression()
    {
        var custom = MetaFramework.Next with { ServerEntry = "custom/server.js" };

        Assert.Equal("custom/server.js", custom.ServerEntry);
        Assert.Equal("HOSTNAME", custom.HostVariable);
        Assert.Equal("server.js", MetaFramework.Next.ServerEntry);
    }
}
