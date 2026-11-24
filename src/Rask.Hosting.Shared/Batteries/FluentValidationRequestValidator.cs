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
        if (request is null || !RequestValidationSwitch.Validates)
        {
            return [];
        }

        var errors = new List<RequestValidationError>();
        if (RaskValidators.Find(typeof(TRequest)) is { } factory && factory(_services) is IValidator validator)
        {
            await Add(errors, validator, request).ConfigureAwait(false);
        }

        // A generated form model the request carries borrows its aggregate's rules (RaskValidators.BorrowsRules): they are
        // asked of what arrived, as its DataAnnotations attributes are, so the wire refuses what the form would have.
        foreach (var node in ModelGraphWalker.Walk(request).Skip(1).Where(static n => RaskValidators.BorrowsRules(n.GetType())))
        {
            if (RaskValidators.Find(node.GetType())?.Invoke(_services) is IValidator borrowed)
            {
                await Add(errors, borrowed, node).ConfigureAwait(false);
            }
        }

        return errors;
    }

    private static async Task Add(List<RequestValidationError> errors, IValidator validator, object instance)
    {
        var result = await validator
            .ValidateAsync(new ValidationContext<object>(instance), Ambient.CancellationToken)
            .ConfigureAwait(false);

        foreach (var failure in result.Errors)
        {
            errors.Add(new RequestValidationError(failure.PropertyName ?? string.Empty, failure.ErrorMessage));
        }
    }
}
