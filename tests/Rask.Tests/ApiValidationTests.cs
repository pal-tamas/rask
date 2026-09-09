using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Forms;

namespace Rask.Tests;

/// <summary>
///     Validation on the two HTTP seams: a controller action and a minimal API endpoint.
/// </summary>
/// <remarks>
///     <para>
///         Every test here asks a running server, over real HTTP, and every one of them asserts on the
///         REJECTION and its contents rather than on a request succeeding. A rule that silently never
///         runs is this repo's worst failure mode, and a test that only checks a 200 is exactly how one
///         ships: the endpoint answers, the validator never executed, and nothing anywhere says so.
///     </para>
///     <para>
///         The asynchronous rule gets the same treatment twice over. <c>OrderRules.Runs</c> counts the
///         <c>MustAsync</c> body's own executions, so "a 400 came back" and "the async rule ran" are two
///         separate assertions — a synchronous <c>[Required]</c> failure can produce the first without
///         the second, and that difference is the whole issue.
///     </para>
/// </remarks>
public sealed class ApiValidationTests
{
    private const string DocumentedProblemType =
        "https://github.com/pal-tamas/rask/blob/main/docs/validation.md#rejected";

    private static RaskApp NewApp(Action<RaskApp>? arrange = null)
    {
        // The application name is what seeds MVC's ApplicationPartManager, and without it a controller
        // declared in a test assembly is never discovered — the route 404s and every assertion below
        // becomes a test of the not-found guard instead. RaskAppTests names it the same way.
        var app = RaskApp.Create(
            ["--applicationName", typeof(OrdersController).Assembly.GetName().Name!],
            builder =>
            {
                builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
                builder.Services.AddSingleton<Ledger>();
            });

        arrange?.Invoke(app);

        // The minimal API half. Mapped through MapEndpoints, which is where an app writes them and where
        // the validation convention is attached — an endpoint filter reaches only what is mapped into the
        // group carrying it, so this seam is the one that has to be proven.
        app.MapEndpoints(endpoints =>
        {
            endpoints.MapPost("/api/minimal-orders", (Order body) => TypedResults.Ok(body.Reference));

            // The guard for "a service is not a body": Ledger has a [Required] member that is never set,
            // so an endpoint injecting it answers 400 the moment the container's objects get validated.
            endpoints.MapPost(
                "/api/minimal-orders-with-service",
                (Order body, Ledger ledger) => TypedResults.Ok(body.Reference));
        });

        return app;
    }

    private static async Task<WebApplication> StartAsync(Action<RaskApp>? arrange = null)
    {
        var app = NewApp(arrange).Build<TestApp>();
        await app.StartAsync();
        return app;
    }

    private static string BaseAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

    private static async Task<HttpResponseMessage> PostAsync(WebApplication app, string path, string json)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseAddress(app)) };
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(path, content);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static string[] MessagesFor(JsonElement problem, string field)
    {
        Assert.Equal(DocumentedProblemType, problem.GetProperty("type").GetString());
        Assert.Equal("Validation failed", problem.GetProperty("title").GetString());
        Assert.Equal(400, problem.GetProperty("status").GetInt32());

        var errors = problem.GetProperty("errors");
        Assert.True(
            errors.TryGetProperty(field, out var messages),
            $"no '{field}' in errors: {errors}");

        return [.. messages.EnumerateArray().Select(message => message.GetString()!)];
    }

    // ---- the documented failure shape, from both seams -----------------------------------------

    [Fact]
    public async Task A_controller_rejects_an_invalid_body_with_the_documented_problem_document()
    {
        var app = await StartAsync();

        try
        {
            var response = await PostAsync(app, "/api/orders", """{"quantity":5}""");
            var problem = await ProblemAsync(response);

            Assert.NotEmpty(MessagesFor(problem, nameof(Order.Reference)));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task A_minimal_api_endpoint_rejects_an_invalid_body_with_the_documented_problem_document()
    {
        // The half that had NOTHING before: a minimal API ran no validation at all, so a [Required] on a
        // body was silently unenforced there while the identical controller rejected it.
        var app = await StartAsync();

        try
        {
            var response = await PostAsync(app, "/api/minimal-orders", """{"quantity":5}""");
            var problem = await ProblemAsync(response);

            Assert.NotEmpty(MessagesFor(problem, nameof(Order.Reference)));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Both_seams_answer_a_rejection_in_the_same_shape()
    {
        // The claim the issue is actually about: one client handles a rejection from either seam. Asserted
        // by comparing the two documents rather than by two separate schema checks, because "same shape"
        // is the property and two passing schema checks can still disagree.
        var app = await StartAsync();

        try
        {
            var fromController = await ProblemAsync(
                await PostAsync(app, "/api/orders", """{"reference":"ok","quantity":900}"""));
            var fromMinimal = await ProblemAsync(
                await PostAsync(app, "/api/minimal-orders", """{"reference":"ok","quantity":900}"""));

            Assert.Equal(
                fromController.GetProperty("type").GetString(),
                fromMinimal.GetProperty("type").GetString());
            Assert.Equal(
                MessagesFor(fromController, nameof(Order.Quantity)),
                MessagesFor(fromMinimal, nameof(Order.Quantity)));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void The_problem_type_is_the_one_remote_dispatch_sends()
    {
        // Pinned as a literal on purpose. Rask.Cqrs.Server holds the same string as an internal constant
        // and this package cannot reference it, so nothing but a test keeps the two from drifting — and a
        // client branching on `type` is the documented way to detect a rejection.
        Assert.Equal(DocumentedProblemType, RaskApiValidation.ProblemType);
    }

    // ---- the asynchronous rule, on both seams --------------------------------------------------

    [Fact]
    public async Task An_async_rule_runs_on_a_controller_action()
    {
        var app = await StartAsync();
        var before = OrderRules.Runs;

        try
        {
            var response = await PostAsync(
                app, "/api/orders", $$"""{"reference":"{{OrderRules.TakenReference}}","quantity":1}""");
            var problem = await ProblemAsync(response);

            Assert.Contains("That reference is already used.", MessagesFor(problem, nameof(Order.Reference)));

            // Not merely "a 400 came back": the MustAsync body itself executed. ModelState could never
            // have produced this, which is why the filter had to be asynchronous.
            Assert.True(OrderRules.Runs > before, "the MustAsync rule never ran");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task An_async_rule_runs_on_a_minimal_api_endpoint()
    {
        var app = await StartAsync();
        var before = OrderRules.Runs;

        try
        {
            var response = await PostAsync(
                app,
                "/api/minimal-orders",
                $$"""{"reference":"{{OrderRules.TakenReference}}","quantity":1}""");
            var problem = await ProblemAsync(response);

            Assert.Contains("That reference is already used.", MessagesFor(problem, nameof(Order.Reference)));
            Assert.True(OrderRules.Runs > before, "the MustAsync rule never ran");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task The_validator_a_form_runs_is_the_one_the_endpoint_runs()
    {
        // Written once, enforced in three places. The form half goes through the very validator a
        // Form<Order> registers — RaskValidation.Resolve is what DiscoveredFieldValidator calls — so this
        // is the seam, not a re-implementation of it.
        var app = await StartAsync();

        try
        {
            using var scope = app.Services.CreateScope();
            var validator = RaskValidation.Resolve(typeof(Order), scope.ServiceProvider);
            Assert.NotNull(validator);

            var context = new EditContext(new Order
            {
                Reference = OrderRules.TakenReference,
                Quantity = 1,
            });
            context.AddValidator(validator!);
            await context.ValidateAsync();

            var fromForm = context.GetValidationEntries()
                .Where(entry => entry.Field == nameof(Order.Reference))
                .Select(entry => entry.Message)
                .ToArray();

            Assert.Contains("That reference is already used.", fromForm);

            var problem = await ProblemAsync(await PostAsync(
                app,
                "/api/minimal-orders",
                $$"""{"reference":"{{OrderRules.TakenReference}}","quantity":1}"""));

            Assert.Equal(fromForm, MessagesFor(problem, nameof(Order.Reference)));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    // ---- what must still get through -----------------------------------------------------------

    [Fact]
    public async Task A_valid_body_reaches_the_handler_on_both_seams()
    {
        var app = await StartAsync();

        try
        {
            var controller = await PostAsync(app, "/api/orders", """{"reference":"ok","quantity":2}""");
            Assert.Equal(HttpStatusCode.OK, controller.StatusCode);

            var minimal = await PostAsync(app, "/api/minimal-orders", """{"reference":"ok","quantity":2}""");
            Assert.Equal(HttpStatusCode.OK, minimal.StatusCode);
            Assert.Equal("\"ok\"", await minimal.Content.ReadAsStringAsync());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task An_injected_service_is_not_mistaken_for_a_body()
    {
        // Ledger has an unset [Required] member, so if the filter validated the container's objects this
        // answers 400 — and, worse, a DbContext would have its whole object graph walked per request.
        var app = await StartAsync();

        try
        {
            var response = await PostAsync(
                app, "/api/minimal-orders-with-service", """{"reference":"ok","quantity":2}""");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    // ---- the off switch -------------------------------------------------------------------------

    [Fact]
    public async Task Turning_validation_off_turns_the_discovered_validator_off_on_both_seams()
    {
        var app = await StartAsync(host => host.Configure(c => c.Validation.Off()));
        var before = OrderRules.Runs;

        try
        {
            var body = $$"""{"reference":"{{OrderRules.TakenReference}}","quantity":1}""";

            var controller = await PostAsync(app, "/api/orders", body);
            Assert.Equal(HttpStatusCode.OK, controller.StatusCode);

            var minimal = await PostAsync(app, "/api/minimal-orders", body);
            Assert.Equal(HttpStatusCode.OK, minimal.StatusCode);

            Assert.Equal(before, OrderRules.Runs);
        }
        finally
        {
            await app.StopAsync();

            // Process-wide, so it is put back rather than left for whatever runs next. RaskApp.Build sets
            // it, which is why every test in this assembly is serialised (AssemblyInfo.cs).
            RaskValidation.AutoValidate = true;
        }
    }
}
