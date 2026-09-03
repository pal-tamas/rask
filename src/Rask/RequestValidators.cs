using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Forms;
using Rask.Cqrs;
using Rask.Validation.FluentValidation;

namespace Rask;

// The two built-in request validators, and where they live.
//
// Not in Rask.Cqrs: that package is standalone by design — DI abstractions and nothing else — which is
// what lets it publish trim-clean and be used outside Rask entirely. Putting reflection (DataAnnotations)
// or a third-party dependency (FluentValidation) inside it would end both of those properties.
//
// Not in Rask.Validation.FluentValidation either: a forms-only app should not acquire Rask.Cqrs by
// referencing a validation package.
//
// So they live here, in the package that already references every side. Referencing Rask IS the
// reference set, which is the same reasoning RaskBatteryWiring is a plain method rather than a
// discovery generator.

/// <summary>
///     Validates a dispatched request with its <c>System.ComponentModel.DataAnnotations</c> attributes.
/// </summary>
/// <typeparam name="TRequest">The request being dispatched.</typeparam>
public sealed class DataAnnotationsRequestValidator<TRequest> : IRequestValidator<TRequest>
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the validator over the dispatch scope.</summary>
    /// <param name="services">The scope a custom ValidationAttribute resolves services from.</param>
    public DataAnnotationsRequestValidator(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RequestValidationError>> ValidateAsync(
        TRequest request, CancellationToken cancellationToken)
    {
        // One switch means one switch. On the server c.Validation.Off() also clears
        // CqrsOptions.ValidateRequests, so this is redundant there; on WebAssembly there is no options
        // object to configure, and without this line turning validation off would stop forms validating
        // and quietly leave requests being validated.
        if (request is null || !RaskValidation.AutoValidate)
        {
            return ValueTask.FromResult<IReadOnlyList<RequestValidationError>>([]);
        }

        // The same pass a Form runs, shaped for a caller with no EditContext.
        var entries = DataAnnotationsFieldValidator.Validate(request, _services);
        if (entries.Count == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<RequestValidationError>>([]);
        }

        var errors = new List<RequestValidationError>(entries.Count);
        foreach (var entry in entries)
        {
            errors.Add(new RequestValidationError(entry.Field, entry.Message));
        }

        return ValueTask.FromResult<IReadOnlyList<RequestValidationError>>(errors);
    }
}

/// <summary>
///     Validates a dispatched request with the <c>AbstractValidator&lt;T&gt;</c> written for it, if
///     there is one — the same validator a <c>Form</c> over that type would use.
/// </summary>
/// <typeparam name="TRequest">The request being dispatched.</typeparam>
public sealed class FluentValidationRequestValidator<TRequest> : IRequestValidator<TRequest>
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the validator over the dispatch scope.</summary>
    /// <param name="services">The scope the discovered validator's dependencies come from.</param>
    public FluentValidationRequestValidator(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RequestValidationError>> ValidateAsync(
        TRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !RaskValidation.AutoValidate
            || RaskValidators.Find(typeof(TRequest)) is not { } factory)
        {
            return [];
        }

        if (factory(_services) is not IValidator validator)
        {
            return [];
        }

        var result = await validator
            .ValidateAsync(new ValidationContext<object>(request), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsValid)
        {
            return [];
        }

        var errors = new List<RequestValidationError>(result.Errors.Count);
        foreach (var failure in result.Errors)
        {
            errors.Add(new RequestValidationError(failure.PropertyName ?? string.Empty, failure.ErrorMessage));
        }

        return errors;
    }
}

/// <summary>
///     Validates a request in the browser before it travels, using the same two passes the server runs.
/// </summary>
/// <remarks>
///     Non-generic because the remote transport holds the message as <see cref="object" />: resolving a
///     generic validator from it would mean <c>MakeGenericType</c>, and reflection is exactly what
///     <c>Rask.Cqrs.Client</c> has none of. Both passes take an object anyway.
/// </remarks>
public sealed class RaskRemoteRequestValidator : IRemoteRequestValidator
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the validator over the app's scope.</summary>
    /// <param name="services">The scope validators resolve their own dependencies from.</param>
    public RaskRemoteRequestValidator(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RequestValidationError>> ValidateAsync(
        object request, CancellationToken cancellationToken)
    {
        if (request is null || !RaskValidation.AutoValidate)
        {
            return [];
        }

        var errors = new List<RequestValidationError>();

        foreach (var entry in DataAnnotationsFieldValidator.Validate(request, _services))
        {
            errors.Add(new RequestValidationError(entry.Field, entry.Message));
        }

        if (RaskValidators.Find(request.GetType()) is { } factory
            && factory(_services) is IValidator validator)
        {
            var result = await validator
                .ValidateAsync(new ValidationContext<object>(request), cancellationToken)
                .ConfigureAwait(false);

            foreach (var failure in result.Errors)
            {
                errors.Add(new RequestValidationError(failure.PropertyName ?? string.Empty, failure.ErrorMessage));
            }
        }

        return errors;
    }
}

/// <summary>
///     Registers the built-in request validators.
/// </summary>
public static class RaskRequestValidation
{
    /// <summary>
    ///     Adds the DataAnnotations and FluentValidation request validators, so every dispatched
    ///     request is checked before its handler runs. Called for you by the <c>Rask</c> package.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddRaskRequestValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Open generics: one registration covers every request type, resolved closed by the generated
        // invoker. TryAddEnumerable keeps a second AddRask() call from doubling the pass.
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IRequestValidator<>), typeof(DataAnnotationsRequestValidator<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IRequestValidator<>), typeof(FluentValidationRequestValidator<>)));

        // The client-side pre-check. AddRaskCqrsClient asks for this optionally, so registering it is
        // what turns "fail fast in the browser" on; without it a remote request is only validated once
        // it reaches the server.
        services.TryAddSingleton<IRemoteRequestValidator, RaskRemoteRequestValidator>();

        return services;
    }
}
