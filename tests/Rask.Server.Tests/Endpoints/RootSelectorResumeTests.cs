using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.DevTools;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     Resume asks the root selector whether a recorded path may be rebuilt, and the selector asks the devtools when they
///     are attached. A resume record skips the GET, so without this a devtools panel session could come back without the
///     admission check that decides who may open one.
/// </summary>
public class RootSelectorResumeTests
{
    [Fact]
    public void Without_the_devtools_every_path_may_resume()
    {
        var selector = new RaskRootSelector(HostFactory, []);

        Assert.True(selector.CanResume("/"));
        Assert.True(selector.CanResume("/_rask-devtools/"));
    }

    [Fact]
    public void With_the_devtools_the_answer_is_theirs()
    {
        var devTools = new RefusesOnePath("/_rask-devtools/");
        var selector = new RaskRootSelector(HostFactory, [], devTools);

        Assert.True(selector.CanResume("/"));
        Assert.False(selector.CanResume("/_rask-devtools/"));
    }

#pragma warning disable RASK014 // A factory for a root, which has no parent render context to construct it through.
    private static Component HostFactory(IServiceProvider services) => new TestApp(new RouteState());
#pragma warning restore RASK014

    private sealed class RefusesOnePath(string refused) : IRaskServerDevTools
    {
        public void MapEndpoints(IEndpointRouteBuilder endpoints, string pathBase)
        {
        }

        public DevToolsPageTag? PageTag(HttpContext context, string sessionId) => null;

        public int? RefusePanel(HttpContext context, string path) => null;

        public bool CanResume(string path) => path != refused;
    }
}
