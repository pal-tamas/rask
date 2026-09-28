using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask;
using Rask.Core.Forms;
using Rask.Cqrs;

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
internal sealed class DataAnnotationsRequestValidator<TRequest> : IRequestValidator<TRequest>
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
    public ValueTask<IReadOnlyList<RequestValidationError>> Validate(TRequest request)
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
