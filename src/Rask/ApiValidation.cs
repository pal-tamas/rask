using System.Buffers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Forms;
using Rask.Validation.FluentValidation;

namespace Rask;

// Validation on the HTTP seam, and why it is shaped like this.
//
// A form and a dispatched request both run two passes: the model's DataAnnotations attributes, then the
// AbstractValidator<T> discovered for its type. An endpoint used to run neither half of the second one —
// an AbstractValidator<T> did not run on a controller action or a minimal API, and no ASYNC rule ran
// anywhere, because MVC's ModelState/IModelValidator and Validator.TryValidateObject are both
// synchronous and a MustAsync rule cannot ride a synchronous pass. That is the whole reason the two
// filters below are asynchronous.
//
// Where this lives: here, not in Rask.Api. The two engines are Rask.Core (DataAnnotations) and
// Rask.Validation.FluentValidation, and Rask.Api references neither — it is the HTTP hosting package,
// and an app that wants a controller should not acquire FluentValidation to get one. This is the same
// reasoning, and the same home, as the built-in request validators in RequestValidators.cs: the package
// that already references every side does the wiring.
//
// Two seams, two mechanisms, ONE failure shape — the 400 documented at docs/validation.md#rejected, so a
// client written against remote dispatch works unchanged against an endpoint.
//
//   controllers    ModelState (AddMvcCore().AddDataAnnotations(), the platform's own synchronous pass)
//                  + this IAsyncActionFilter for the discovered validator, merged into one document.
//
//   minimal APIs   this endpoint filter, running BOTH passes.
//
// The minimal-API half deliberately does NOT switch on .NET 10's AddValidation(). That was the first
// design, and it was measured rather than assumed: the filter AddValidation() installs is OUTERMOST —
// outside every filter a group or a route can add — so it short-circuits before anything of ours runs,
// and it answers `application/json` with `{"title":…,"errors":…}`, carrying neither `status` nor `type`.
// A body that failed a [Required] would therefore never reach the async pass at all (the exact
// "my rule silently never ran" failure this exists to remove) and would answer in a shape no Rask client
// branches on, with no supported hook anywhere to reshape it. Running the attribute pass ourselves —
// through DataAnnotationsFieldValidator, the same engine a form and a dispatched request use — costs one
// walk of the model graph and gives one merged document instead of two rival ones.

/// <summary>
///     Validation for HTTP endpoints: the <c>AbstractValidator&lt;T&gt;</c> discovered for a request
///     type, and every asynchronous rule in it, applied to controller actions and minimal APIs alike.
///     <para>
///         Wired for you by the <c>Rask</c> package. An app that does without says
///         <c>app.Configure(c =&gt; c.Validation.Off())</c>.
///     </para>
/// </summary>
public static class RaskApiValidation
{
    // The stable `type` a rejection carries, byte for byte the one Rask.Cqrs.Server sends
    // (its ValidationProblemType). Duplicated rather than shared because this package does not reference
    // Rask.Cqrs.Server — the point of the constant is that a client branches on it, so the two are pinned
    // together by ApiValidationTests.The_problem_type_is_the_one_remote_dispatch_sends.
    internal const string ProblemType =
        "https://github.com/pal-tamas/rask/blob/main/docs/validation.md#rejected";

    internal const string ProblemTitle = "Validation failed";

    /// <summary>
    ///     Adds the asynchronous validation pass for HTTP endpoints: a global action filter for
    ///     controllers, and the convention <see cref="RequireRaskValidation{TBuilder}" /> puts on minimal
    ///     APIs.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    ///     Called for you by the <c>Rask</c> package whenever the validation battery is on. It also
    ///     suppresses MVC's own <c>ModelStateInvalidFilter</c> — not to weaken the check but to move it:
    ///     that filter answers at order -2000, before any asynchronous rule could run, and its
    ///     <c>ValidationProblemDetails</c> is a second failure shape for the same event. The filter added
    ///     here rejects the same requests, with the same <c>errors</c> map, in the shape documented at
    ///     <c>docs/validation.md#rejected</c>.
    /// </remarks>
    public static IServiceCollection AddRaskApiValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure rather than a marker service: MvcOptions is built once, and the guard makes a second
        // AddRaskApiValidation() (or a hand-assembled app that also called it) idempotent per instance.
        services.Configure<MvcOptions>(static options =>
        {
            foreach (var filter in options.Filters)
            {
                if (filter is ActionFilter)
                {
                    return;
                }
            }

            options.Filters.Add(new ActionFilter());
        });

        services.Configure<ApiBehaviorOptions>(static options =>
            options.SuppressModelStateInvalidFilter = true);

        return services;
    }

    /// <summary>
    ///     Validates the bodies a minimal API endpoint binds, with the model's DataAnnotations attributes
    ///     and the <c>AbstractValidator&lt;T&gt;</c> discovered for its type, rejecting a failure with the
    ///     400 documented at <c>docs/validation.md#rejected</c>.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint, group or route builder being conventioned.</typeparam>
    /// <param name="builder">What to attach the filter to.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         An app hosted by <c>RaskApp</c> gets this already: everything mapped through
    ///         <c>app.MapEndpoints(e =&gt; …)</c> is mapped into a group carrying it. Reach for this
    ///         directly only for endpoints mapped somewhere else — on the built
    ///         <see cref="WebApplication" />, or in a lean host with no <c>RaskApp</c>.
    ///     </para>
    ///     <para>
    ///         Controllers do not need it. They are validated by the global action filter
    ///         <see cref="AddRaskApiValidation" /> registers, because MVC runs its own filter pipeline
    ///         rather than the endpoint one.
    ///     </para>
    /// </remarks>
    public static TBuilder RequireRaskValidation<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddEndpointFilter(ValidateEndpointAsync);
    }

    // ---- the two seams -------------------------------------------------------------------------

    private static async ValueTask<object?> ValidateEndpointAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // One switch means one switch: c.Validation.Off() sets RaskValidation.AutoValidate, and an
        // endpoint mapped before that took effect must not keep validating.
        if (!RaskValidation.AutoValidate)
        {
            return await next(context).ConfigureAwait(false);
        }

        var http = context.HttpContext;
        var services = http.RequestServices;
        var parameters = http.GetEndpoint()?.Metadata.GetMetadata<MethodInfo>()?.GetParameters();
        var isService = services.GetService<IServiceProviderIsService>();

        Dictionary<string, List<string>>? errors = null;

        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] is not { } argument)
            {
                continue;
            }

            var parameter = parameters is not null && i < parameters.Length ? parameters[i] : null;
            if (!IsUserInput(argument, parameter, isService))
            {
                continue;
            }

            // The attribute pass. A minimal API has no ModelState, so unlike the controller filter this
            // one runs it — the same DataAnnotationsFieldValidator a form and a dispatched request use.
            foreach (var entry in DataAnnotationsFieldValidator.Validate(argument, services))
            {
                Add(ref errors, entry.Field, entry.Message);
            }

            var failures = await DiscoveredAsync(argument, services, http.RequestAborted)
                .ConfigureAwait(false);

            if (failures is not null)
            {
                foreach (var (field, message) in failures)
                {
                    Add(ref errors, field, message);
                }
            }
        }

        return errors is null
            ? await next(context).ConfigureAwait(false)
            : Reject(http, errors);
    }

    // MVC's own pipeline, because a controller is not an endpoint-filter endpoint: its filters run
    // inside the one RequestDelegate MVC installs, and ModelState is where the platform's synchronous
    // pass has already put its findings by the time this runs.
    private sealed class ActionFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);

            if (!RaskValidation.AutoValidate)
            {
                await next().ConfigureAwait(false);
                return;
            }

            var http = context.HttpContext;
            var services = http.RequestServices;
            var isService = services.GetService<IServiceProviderIsService>();

            Dictionary<string, List<string>>? errors = null;

            // The platform's synchronous pass, already run: AddMvcCore().AddDataAnnotations() put every
            // [Required]/[Range] failure here, and model binding put its own conversion failures here
            // too. Carried over rather than repeated, so a field never reports the same rule twice.
            foreach (var (field, entry) in context.ModelState)
            {
                foreach (var error in entry.Errors)
                {
                    // ErrorMessage is empty when the entry carries an EXCEPTION — a body that would not
                    // deserialize, say. That message is written for an operator and can name types and
                    // paths, so it is replaced rather than forwarded; the status and the field name are
                    // what the caller can act on.
                    Add(
                        ref errors,
                        field,
                        string.IsNullOrEmpty(error.ErrorMessage)
                            ? "The value is not valid."
                            : error.ErrorMessage);
                }
            }

            foreach (var argument in context.ActionArguments.Values)
            {
                if (argument is null || !IsUserInput(argument, parameter: null, isService))
                {
                    continue;
                }

                var failures = await DiscoveredAsync(argument, services, http.RequestAborted)
                    .ConfigureAwait(false);

                if (failures is not null)
                {
                    foreach (var (field, message) in failures)
                    {
                        Add(ref errors, field, message);
                    }
                }
            }

            if (errors is null)
            {
                await next().ConfigureAwait(false);
                return;
            }

            http.Response.Headers.CacheControl = "no-store";
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentType = "application/problem+json",
                Content = Problem(errors),
            };
        }
    }

    // ---- the shared pass -----------------------------------------------------------------------

    // The discovered AbstractValidator<T>, run ASYNCHRONOUSLY. This is the half neither seam had: a
    // MustAsync rule — "is this reference already used?" — is the common reason to reach for
    // FluentValidation at all, and it cannot ride ModelState or Validator.TryValidateObject.
    private static async ValueTask<List<KeyValuePair<string, string>>?> DiscoveredAsync(
        object model, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (RaskValidators.Find(model.GetType()) is not { } factory
            || factory(services) is not IValidator validator)
        {
            return null;
        }

        var result = await validator
            .ValidateAsync(new ValidationContext<object>(model), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsValid)
        {
            return null;
        }

        var failures = new List<KeyValuePair<string, string>>(result.Errors.Count);
        foreach (var failure in result.Errors)
        {
            failures.Add(new KeyValuePair<string, string>(
                failure.PropertyName ?? string.Empty, failure.ErrorMessage));
        }

        return failures;
    }

    // Which bound arguments are things the CALLER supplied. Everything else — the DbContext, the
    // HttpContext, a ClaimsPrincipal — is the container's, and walking a DbContext's object graph with
    // DataAnnotations would be a performance disaster and a correctness one.
    //
    // IServiceProviderIsService is the same question minimal APIs ask themselves when deciding whether a
    // parameter comes from DI, and asking it costs nothing: unlike GetService it resolves nothing.
    private static bool IsUserInput(
        object argument, ParameterInfo? parameter, IServiceProviderIsService? isService)
    {
        var type = argument.GetType();

        // Nothing to walk: a primitive, a string or an enum has no properties carrying rules, and no
        // AbstractValidator<T> is written for one.
        if (type.IsPrimitive || type.IsEnum || argument is string)
        {
            return false;
        }

        // A framework type is never the caller's body. Named by namespace rather than by a list, so
        // HttpContext, ClaimsPrincipal, CancellationToken, IFormFile, Stream and everything like them are
        // covered without the list going stale.
        if (type.Namespace is { } ns
            && (ns.StartsWith("System", StringComparison.Ordinal)
                || ns.StartsWith("Microsoft", StringComparison.Ordinal)))
        {
            return false;
        }

        if (parameter is not null)
        {
            foreach (var attribute in parameter.GetCustomAttributes(inherit: true))
            {
                if (attribute is IFromServiceMetadata or FromKeyedServicesAttribute)
                {
                    return false;
                }
            }

            return isService?.IsService(parameter.ParameterType) != true;
        }

        return isService?.IsService(type) != true;
    }

    private static void Add(ref Dictionary<string, List<string>>? errors, string field, string message)
    {
        errors ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);

        if (!errors.TryGetValue(field, out var messages))
        {
            messages = [];
            errors[field] = messages;
        }

        messages.Add(message);
    }

    private static IResult Reject(HttpContext http, Dictionary<string, List<string>> errors)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Text(
            Problem(errors), "application/problem+json", statusCode: StatusCodes.Status400BadRequest);
    }

    // RFC 9457, written by hand rather than through a serializer: it is four known fields, and it keeps
    // the shape identical to the one Rask.Cqrs.Server writes without either package importing the other.
    private static string Problem(Dictionary<string, List<string>> errors)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", ProblemType);
            writer.WriteString("title", ProblemTitle);
            writer.WriteNumber("status", StatusCodes.Status400BadRequest);
            writer.WriteStartObject("errors");

            foreach (var (field, messages) in errors)
            {
                writer.WriteStartArray(field);
                foreach (var message in messages)
                {
                    writer.WriteStringValue(message);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
