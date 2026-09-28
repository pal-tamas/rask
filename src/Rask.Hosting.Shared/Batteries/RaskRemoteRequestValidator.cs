using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask;
using Rask.Core.Forms;
using Rask.Cqrs;

namespace Rask;

/// <summary>
///     Validates a request in the browser before it travels, using the same two passes the server runs.
/// </summary>
/// <remarks>
///     Non-generic because the remote transport holds the message as <see cref="object" />: resolving a
///     generic validator from it would mean <c>MakeGenericType</c>, and reflection is exactly what
///     <c>Rask.Cqrs.Client</c> has none of. Both passes take an object anyway.
/// </remarks>
internal sealed class RaskRemoteRequestValidator : IRemoteRequestValidator
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Creates the validator.</summary>
    /// <param name="scopes">
    ///     Makes a scope per validation run. A scope factory rather than an <see cref="IServiceProvider" />
    ///     because this is a singleton (<c>IRemoteDispatch</c>, which consumes it, is one): injecting the
    ///     provider would hand it the ROOT container, and a validator or a custom
    ///     <c>ValidationAttribute</c> asking for anything scoped — a <c>DbContext</c>, a repository —
    ///     would fail resolution under the default scope validation.
    /// </param>
    public RaskRemoteRequestValidator(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        _scopes = scopes;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RequestValidationError>> Validate(object request)
    {
        if (request is null || !RaskValidation.AutoValidate)
        {
            return [];
        }

        var errors = new List<RequestValidationError>();

        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;

        foreach (var entry in DataAnnotationsFieldValidator.Validate(request, services))
        {
            errors.Add(new RequestValidationError(entry.Field, entry.Message));
        }

        if (RaskValidators.Find(request.GetType()) is { } factory
            && factory(services) is IValidator validator)
        {
            var result = await validator
                .ValidateAsync(new ValidationContext<object>(request), Ambient.CancellationToken)
                .ConfigureAwait(false);

            foreach (var failure in result.Errors)
            {
                errors.Add(new RequestValidationError(failure.PropertyName ?? string.Empty, failure.ErrorMessage));
            }
        }

        return errors;
    }
}
