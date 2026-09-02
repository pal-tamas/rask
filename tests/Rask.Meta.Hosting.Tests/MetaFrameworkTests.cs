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
        Assert.Equal(".next/standalone/server.js", MetaFramework.Next.ServerEntry);
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
    ///     Next runs from inside its standalone directory; everything else from the app root.
    /// </summary>
    /// <remarks>
    ///     Taken from each framework's own documented invocation rather than assumed to be uniform.
    ///     Nitro and SvelteKit are documented as <c>node .output/server/index.mjs</c> from the app
    ///     root; Next's Docker guidance copies the standalone tree to the working directory and runs
    ///     <c>node server.js</c> from within it. Running Next from the app root instead would leave it
    ///     resolving its own files against the wrong directory.
    /// </remarks>
    [Fact]
    public void Only_next_runs_from_a_subdirectory()
    {
        Assert.Equal(".next/standalone", MetaFramework.Next.WorkingSubdirectory);

        MetaFramework[] fromAppRoot =
        [
            MetaFramework.Nuxt,
            MetaFramework.TanStackStart,
            MetaFramework.SolidStart,
            MetaFramework.Analog,
            MetaFramework.SvelteKit,
        ];

        Assert.All(fromAppRoot, f => Assert.Equal(string.Empty, f.WorkingSubdirectory));
    }

    /// <summary>
    ///     Every framework's client assets are declared, and Next needs two roots.
    /// </summary>
    /// <remarks>
    ///     Nitro's four converge on <c>.output/public</c> the same way their server entries converge —
    ///     which is what keeps this a table rather than a class hierarchy.
    /// </remarks>
    [Fact]
    public void Client_assets_are_declared_for_every_framework()
    {
        Assert.Equal(
            [new StaticRoot(string.Empty, ".output/public")],
            MetaFramework.Nuxt.StaticRoots);
        Assert.Equal(
            [new StaticRoot(string.Empty, "dist/analog/public")],
            MetaFramework.Analog.StaticRoots);
        Assert.Equal(
            [new StaticRoot(string.Empty, "build/client")],
            MetaFramework.SvelteKit.StaticRoots);
        Assert.Equal(
            [
                new StaticRoot(string.Empty, "public"),
                new StaticRoot("/_next/static", ".next/static"),
            ],
            MetaFramework.Next.StaticRoots);
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
        Assert.Equal(".next/standalone/server.js", MetaFramework.Next.ServerEntry);
    }
}
