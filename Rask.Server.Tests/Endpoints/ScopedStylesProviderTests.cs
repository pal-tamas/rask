using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.ScopedCss;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.Endpoints;

[Collection("ScopedCss")]
public class ScopedStylesProviderTests
{
    public ScopedStylesProviderTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public void AddRask_RegistersIRaskScopedStyles()
    {
        var services = new ServiceCollection();
        services.AddRask();
        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IRaskScopedStyles>());
    }

    [Fact]
    public async Task RootGet_AppWithScopedCss_EmitsScopedCssLink()
    {
        using var host = RaskTestHost.Create<ScopedCssApp>();

        var response = await host.Http.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        var hash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(hash);
        Assert.Contains($"href=\"/_rask/scoped.css?v={hash}\"", body);
        Assert.Contains("rel=\"stylesheet\"", body);
        Assert.Contains("data-rask-scoped", body);
    }

    public sealed class ScopedCssApp : Component
    {
        protected override string? Css => ".tag { color: red; }";

        protected override Component Render() =>
            Fragment()[
                Doctype(),
                Html()[
                    Head()[Title()["test"], RaskScopedStyles()],
                    Body()[Div(Class: "tag")["hi"]]
                ]
            ];
    }
}
