using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask;
using Rask.Core.Forms;
using Rask.Cqrs;

namespace Rask;

/// <summary>
///     Validates a dispatched request with the <c>AbstractValidator&lt;T&gt;</c> written for it, if
///     there is one — the same validator a <c>Form</c> over that type would use.
/// </summary>
/// <typeparam name="TRequest">The request being dispatched.</typeparam>
internal sealed class FluentValidationRequestValidator<TRequest> : IRequestValidator<TRequest>
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
    public async ValueTask<IReadOnlyList<RequestValidationError>> Validate(TRequest request)
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
            .ValidateAsync(new ValidationContext<object>(request), Ambient.CancellationToken)
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
