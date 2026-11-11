using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Forms;

// The DataAnnotations pass over one object whose type has async rules (AsyncAnnotationRules), in the order .NET's
// asynchronous Validator runs it: per property, [Required] first, then the other synchronous attributes, then — only
// when those passed — the async ones; then the type's own attributes when no property failed; then the object's
// own rules. Every failure is worded where it is known which attribute wrote it (AttributeMessages.Worded), so an
// app's resources and Rask's catalog word an async rule exactly as they word a synchronous one.
//
// One deliberate difference from .NET's Validator, the same one the synchronous pass makes: the object's own rules
// (IAsyncValidatableObject.ValidateAsync) run even when an attribute failed, as ASP.NET Core MVC runs them, so a
// form shows every reason at once.
internal static class AsyncAnnotationPass
{
    /// <summary>Every failure of <paramref name="node" />, as (member, message); an empty member is the object's.</summary>
    /// <param name="node">The object to validate.</param>
    /// <param name="rules">Its rules.</param>
    /// <param name="services">The scope a rule resolves services from.</param>
    /// <param name="faulted">
    ///     What a rule that throws says on its member — a form's "could not be completed" — or null to let the
    ///     exception through, as a dispatched request does.
    /// </param>
    /// <param name="cancellationToken">Cancels the rules in flight.</param>
    internal static async Task<List<(string Member, string Message)>> Results(
        object node, AsyncAnnotationRules rules, IServiceProvider? services, Func<string, Exception, string>? faulted,
        CancellationToken cancellationToken)
    {
        var results = new List<(string Member, string Message)>();
        foreach (var member in rules.Members)
        {
            try
            {
                foreach (var message in await Member(node, member, includeSync: true, services, cancellationToken).ConfigureAwait(false))
                {
                    results.Add((member.Name, message));
                }
            }
            catch (Exception ex) when (faulted is not null && ex is not OperationCanceledException)
            {
                results.Add((member.Name, faulted(member.Name, ex)));
            }
        }

        try
        {
            if (results.Count == 0)
            {
                await TypeChecks(node, rules, services, results, cancellationToken).ConfigureAwait(false);
            }

            await foreach (var (member, message) in ObjectRules(node, null, services, cancellationToken).ConfigureAwait(false))
            {
                if (!results.Contains((member, message)))
                {
                    results.Add((member, message));
                }
            }
        }
        catch (Exception ex) when (faulted is not null && ex is not OperationCanceledException)
        {
            results.Add((string.Empty, faulted(string.Empty, ex)));
        }

        return results;
    }

    /// <summary>
    ///     What one property says: its synchronous attributes first when <paramref name="includeSync" /> — a form's
    ///     field check leaves those to the synchronous pass, which has already passed them — then its async ones.
    /// </summary>
    internal static async Task<List<string>> Member(
        object owner, AsyncAnnotationRules.Member member, bool includeSync, IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        var value = member.Property.GetValue(owner);
        var context = Context(owner, member.Name, services);
        var messages = new List<string>();
        if (includeSync)
        {
            if (member.Required is { } required && Failed(required, value, context) is { } missing)
            {
                messages.Add(AttributeMessages.Worded(required, missing, owner, member.Name, value, services));
                return messages;
            }

            foreach (var check in member.Sync)
            {
                if (Failed(check, value, context) is { } failed)
                {
                    messages.Add(AttributeMessages.Worded(check, failed, owner, member.Name, value, services));
                }
            }

            if (messages.Count > 0)
            {
                return messages;
            }
        }

        // One at a time, and the first that fails is the field's answer: a second lookup against the same value is
        // a second round trip for an error the reader cannot see until the first is fixed.
        foreach (var check in member.Async)
        {
            var result = await check.GetValidationResultAsync(value, context, cancellationToken).ConfigureAwait(false);
            if (result is not null && result != ValidationResult.Success)
            {
                messages.Add(AttributeMessages.Worded(check, result, owner, member.Name, value, services));
                break;
            }
        }

        return messages;
    }

    /// <summary>
    ///     The object's own rules — <see cref="IAsyncValidatableObject.ValidateAsync" />, or the synchronous
    ///     <see cref="IValidatableObject.Validate" /> of an object that has no async one — limited to the failures
    ///     naming <paramref name="only" /> when it is given.
    /// </summary>
    internal static async IAsyncEnumerable<(string Member, string Message)> ObjectRules(
        object owner, string? only, IServiceProvider? services,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<ValidationResult>? said = owner switch
        {
            IAsyncValidatableObject awaited => awaited.ValidateAsync(Context(owner, null, services), cancellationToken),
            IValidatableObject validatable => (validatable.Validate(Context(owner, null, services)) ?? []).ToAsyncEnumerable(),
            _ => null,
        };

        if (said is null)
        {
            yield break;
        }

        await foreach (var result in said.ConfigureAwait(false))
        {
            // ValidationResult.Success is null, and an object is free to yield it for "this one is fine".
            if (result is null)
            {
                continue;
            }

            var members = result.MemberNames.Where(static m => m is not null).ToList();
            var message = result.ErrorMessage ?? DataAnnotationsFieldValidator.Invalid;
            if (only is not null)
            {
                if (members.Contains(only, StringComparer.Ordinal))
                {
                    yield return (only, message);
                }

                continue;
            }

            if (members.Count == 0)
            {
                yield return (string.Empty, message);
            }

            foreach (var member in members)
            {
                yield return (member, message);
            }
        }
    }

    private static async Task TypeChecks(
        object node, AsyncAnnotationRules rules, IServiceProvider? services, List<(string Member, string Message)> results,
        CancellationToken cancellationToken)
    {
        var context = Context(node, null, services);
        foreach (var check in rules.TypeChecks)
        {
            var result = check is AsyncValidationAttribute awaited
                ? await awaited.GetValidationResultAsync(node, context, cancellationToken).ConfigureAwait(false)
                : check.GetValidationResult(node, context);
            if (result is null || result == ValidationResult.Success)
            {
                continue;
            }

            var members = result.MemberNames.Where(static m => m is not null).ToList();
            var message = AttributeMessages.Worded(check, result, node, string.Empty, node, services);
            if (members.Count == 0)
            {
                results.Add((string.Empty, message));
            }

            results.AddRange(members.Select(member => (member, message)));
        }
    }

    private static ValidationResult? Failed(ValidationAttribute check, object? value, ValidationContext context) =>
        check.GetValidationResult(value, context) is { } result && result != ValidationResult.Success ? result : null;

    // ASP.NET Core parity, as in the synchronous pass: the scope's services reach a custom attribute through
    // validationContext.GetService<T>() — which is how an async rule finds its DbContext.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "ValidationContext's no-display-name ctor reflects over the model the app validates; the app " +
                        "preserves its models for validation (DataAnnotationsFieldValidator).")]
    private static ValidationContext Context(object owner, string? member, IServiceProvider? services) =>
        member is null ? new ValidationContext(owner, services, null) : new ValidationContext(owner, services, null) { MemberName = member };
}
