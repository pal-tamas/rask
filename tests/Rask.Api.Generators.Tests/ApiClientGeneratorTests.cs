using System.Linq;
using Microsoft.CodeAnalysis;
using Rask.Generators.TestSupport;

namespace Rask.Api.Generators.Tests;

/// <summary>
///     What the generator reports, and what it refuses to emit.
/// </summary>
/// <remarks>
///     <para>
///         The MVC types are declared as stubs in <see cref="Mvc" /> rather than referenced. That is not
///         a shortcut: the generator matches them by name and namespace precisely so it needs no ASP.NET
///         reference of its own, and a suite that could only run with the real assemblies present would
///         stop pinning that property.
///     </para>
///     <para>
///         Whether a generated client actually reaches the right URL is not asked here — no test over
///         emitted text can answer it. That is what <c>Rask.Api.Client.Tests</c> is for, where real
///         controllers are hosted and called.
///     </para>
/// </remarks>
public sealed class ApiClientGeneratorTests
{
    /// <summary>Stub MVC surface, in the namespaces the generator matches on.</summary>
    private const string Mvc = """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class ControllerBase { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ApiControllerAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class RouteAttribute : System.Attribute
            {
                public RouteAttribute(string template) { }
            }
            public class HttpGetAttribute : System.Attribute
            {
                public HttpGetAttribute() { }
                public HttpGetAttribute(string template) { }
            }
            public class HttpPostAttribute : System.Attribute
            {
                public HttpPostAttribute() { }
                public HttpPostAttribute(string template) { }
            }
            public sealed class ActionResult<T> { }
            public interface IActionResult { }
            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
            public sealed class ProducesResponseTypeAttribute : System.Attribute
            {
                public ProducesResponseTypeAttribute(System.Type type, int statusCode) { }
            }
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class FromBodyAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class FromHeaderAttribute : System.Attribute { }
        }
        """;

    /// <summary>
    ///     The minimal-API surface, stubbed to the shape the generator matches on: the extension class
    ///     by name, its first parameter by type, and <c>RouteGroupBuilder</c> as a receiver.
    /// </summary>
    private const string Routing = """
        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
            public sealed class RouteGroupBuilder : IEndpointRouteBuilder { }
        }
        namespace Microsoft.AspNetCore.Builder
        {
            using Microsoft.AspNetCore.Routing;
            public static class EndpointRouteBuilderExtensions
            {
                public static RouteGroupBuilder MapGroup(this IEndpointRouteBuilder builder, string prefix) => null!;
                public static object MapGet(this IEndpointRouteBuilder builder, string pattern, System.Delegate handler) => null!;
            }
        }
        """;

    private static GeneratorRun Run(string source, IReadOnlyDictionary<string, string>? options = null) =>
        GeneratorHarness.Run(source + Mvc, new ApiClientGenerator(), options, "Rask.Api.Client", "Rask.Wire");

    private static bool Reported(GeneratorRun run, string id) =>
        run.Diagnostics.Any(d => d.Id == id);

    [Fact]
    public void A_controller_gets_a_client_named_after_it()
    {
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            public sealed record Post(int Id, string Title);
            [ApiController]
            [Route("api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<Post> Get(int id) => null!;
            }
            """);

        var source = run.GeneratedSource("__RaskApiClients");

        Assert.Contains("class PostsClient", source, StringComparison.Ordinal);
        Assert.Contains("Get(int id", source, StringComparison.Ordinal);
        Assert.Contains("\"api/posts/\"", source, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void RASK068_reports_a_catch_all_route_rather_than_guessing()
    {
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/files")]
            public sealed class FilesController : ControllerBase
            {
                [HttpGet("{*path}")]
                public ActionResult<string> Read(string path) => null!;
            }
            """);

        Assert.True(Reported(run, "RASK068"));
        Assert.False(run.HasGeneratedSource("__RaskApiClients"));
    }

    [Fact]
    public void RASK068_reports_a_route_token_no_parameter_supplies()
    {
        // The URL would otherwise be built with a hole in it and address the wrong resource.
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<string> Get() => null!;
            }
            """);

        Assert.True(Reported(run, "RASK068"));
    }

    [Fact]
    public void RASK069_reports_two_actions_claiming_one_client_method()
    {
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<string> Get(int id) => null!;
                [HttpGet("by-slug/{slug}")]
                public ActionResult<string> Get(string slug) => null!;
            }
            """);

        Assert.True(Reported(run, "RASK069"));
    }

    [Fact]
    public void RASK070_reports_an_IActionResult_with_nothing_to_infer_from()
    {
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpGet("{id}")]
                public IActionResult Get(int id) => null!;
            }
            """);

        Assert.True(Reported(run, "RASK070"));
    }

    [Fact]
    public void ProducesResponseType_answers_RASK070_and_the_method_is_generated()
    {
        // The fix the diagnostic names has to actually work, or the message is a dead end.
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            public sealed record Post(int Id);
            [ApiController]
            [Route("api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpGet("{id}")]
                [ProducesResponseType(typeof(Post), 200)]
                public IActionResult Get(int id) => null!;
            }
            """);

        Assert.False(Reported(run, "RASK070"));
        Assert.Contains("Post> Get(int id", run.GeneratedSource("__RaskApiClients"), StringComparison.Ordinal);
    }

    [Fact]
    public void RASK067_reports_a_body_shape_with_no_wire_encoding()
    {
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpPost("")]
                public ActionResult<string> Create([FromBody] System.IDisposable body) => null!;
            }
            """);

        Assert.True(Reported(run, "RASK067"));
    }

    [Fact]
    public void An_action_answering_nothing_gets_a_Task_returning_method()
    {
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpPost("touch/{id}")]
                public System.Threading.Tasks.Task Touch(int id) => null!;
            }
            """);

        var source = run.GeneratedSource("__RaskApiClients");

        Assert.Contains("Tasks.Task Touch(int id", source, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void The_baked_flag_suppresses_emission_entirely()
    {
        // The browser companion compiles the client baked out of the server assembly. If the generator
        // also emitted one there, every client type would be declared twice (CS0101). This is the guard,
        // and it is asserted by turning the flag on for a compilation that otherwise generates a client.
        const string source = """
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<string> Get(int id) => null!;
            }
            """;

        Assert.True(Run(source).HasGeneratedSource("__RaskApiClients"));

        var baked = Run(source, new Dictionary<string, string>
        {
            [ApiClientGenerator.BakedProperty] = "true",
        });

        Assert.False(baked.HasGeneratedSource("__RaskApiClients"));
    }

    [Fact]
    public void A_FromHeader_parameter_costs_ONE_endpoint_and_not_the_whole_compilation()
    {
        // It used to cost all of them. The emitter threw on a header-bound parameter, believing the
        // generator refused them earlier — it did not — and an exception from a generator is CS8785,
        // which takes down every client in the compilation rather than the one it cannot express.
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/tenants")]
            public sealed class TenantsController : ControllerBase
            {
                [HttpGet("by-header")]
                public ActionResult<string> ByHeader([FromHeader] string tenant) => null!;
            }

            [ApiController]
            [Route("api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<string> Get(int id) => null!;
            }
            """);

        Assert.True(Reported(run, "RASK068"));

        // The other controller still got its client — the assertion that would have failed before.
        var source = run.GeneratedSource("__RaskApiClients");
        Assert.Contains("class PostsClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ByHeader", source, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void The_generated_path_is_relative_so_a_sub_path_deployment_survives()
    {
        // A root-absolute path replaces the whole path of HttpClient.BaseAddress, so an app under
        // /myapp/ would send its calls to the site root and 404 — issue #893, which Rask.Cqrs.Client
        // already carries a fix for. Asserted on the emitted text because the failure only shows up in
        // a deployment under a prefix, which no unit test hosts.
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<string> Get(int id) => null!;
            }
            """);

        var source = run.GeneratedSource("__RaskApiClients");

        Assert.Contains("\"api/posts/\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/api/posts/\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_endpoint_mapped_on_a_MapGroup_is_refused_rather_than_given_a_wrong_url()
    {
        // The group's prefix lives on the RouteGroupBuilder, not at this call site, so the client would
        // call "/{id}" where the server answers "/api/widgets/{id}". Emitting that is worse than
        // emitting nothing: it type-checks on both sides and 404s in production.
        var run = Run("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;

            public static class Endpoints
            {
                public static void Map(IEndpointRouteBuilder app)
                {
                    // Held in a local, which is how groups are normally written — and what a syntax-only
                    // check for the name "MapGroup" would miss.
                    var widgets = app.MapGroup("/api/widgets");
                    widgets.MapGet("/{id}", (int id) => id);
                }
            }
            """ + Routing);

        Assert.True(Reported(run, "RASK068"));
        Assert.False(run.HasGeneratedSource("__RaskApiClients"));
    }

    [Fact]
    public void A_route_token_this_generator_does_not_substitute_is_refused()
    {
        // [area] used to survive into the emitted URL as literal text — matched by nothing, 404 at run
        // time, no diagnostic. Same class as the MapGroup bug: an almost-right URL is worse than none.
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("[area]/api/posts")]
            public sealed class PostsController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<string> Get(int id) => null!;
            }
            """);

        Assert.True(Reported(run, "RASK068"));
        Assert.False(run.HasGeneratedSource("__RaskApiClients"));
    }

    [Fact]
    public void A_controller_token_is_substituted_whatever_its_case()
    {
        // ASP.NET's own replacement is case-insensitive, so [Controller] is as legal as [controller].
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/[Controller]")]
            public sealed class PostsController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<string> Get(int id) => null!;
            }
            """);

        var source = run.GeneratedSource("__RaskApiClients");

        Assert.Contains("\"api/Posts/\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[Controller]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_class_that_only_looks_like_a_controller_is_ignored()
    {
        // [ApiController] without ControllerBase is not an MVC controller, and treating it as one would
        // generate a client for something that answers no route at all.
        var run = Run("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/posts")]
            public sealed class NotAController
            {
                [HttpGet("{id}")]
                public ActionResult<string> Get(int id) => null!;
            }
            """);

        Assert.False(run.HasGeneratedSource("__RaskApiClients"));
    }
}
