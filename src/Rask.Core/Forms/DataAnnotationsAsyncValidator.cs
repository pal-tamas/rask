using Rask.Core.Diagnostics;

namespace Rask.Core.Forms;

// The asynchronous half of a form's DataAnnotations pass (.NET 11): an AsyncValidationAttribute's IsValidAsync and an
// IAsyncValidatableObject's ValidateAsync, awaited. A Form registers it beside DataAnnotationsFieldValidator, and only
// for a model that reaches such a rule (AsyncAnnotationRules.Reached) — every other form stays synchronous.
//
// Nothing here is new machinery: it is an IAsyncFieldValidator, so EditContext already gives it what the issue asks
// for. While it runs the field reads IsValidating (and the ValidatingIndicator shows); a value changed again cancels
// the run in flight and only the latest one's answer lands; it runs when the field's own bind timing validates it
// (on change, or at a .Debounce pause), exactly as an async inline Validate does. What it adds is the fault: a rule
// that throws leaves "Validation could not be completed." on ITS field — the form cannot be submitted past it — and
// tells the developer why through RaskDiagnostics. Never a silent pass.
internal sealed class DataAnnotationsAsyncValidator : IAsyncFieldValidator, IFieldScopedValidator
{
    private readonly IServiceProvider? _services;

    internal DataAnnotationsAsyncValidator(IServiceProvider? services) => _services = services;

    /// <summary>Only a field with an async attribute, or on an <c>IAsyncValidatableObject</c>, awaits this.</summary>
    public bool Covers(FieldIdentifier field) => AsyncAnnotationRules.Of(field.Model.GetType())?.Covers(field.FieldName) == true;

    /// <summary>
    ///     The submit: every object in the model graph with async rules, validated whole — the synchronous pass leaves
    ///     those objects to this one, so a rule is never asked twice.
    /// </summary>
    public async ValueTask Validate(EditContext context, CancellationToken cancellationToken)
    {
        foreach (var node in ModelGraphWalker.Walk(context.Model).ToList())
        {
            if (AsyncAnnotationRules.Of(node.GetType()) is not { } rules)
            {
                continue;
            }

            // A rule that throws is a message on its own field; the node's other fields are still asked.
            var results = await AsyncAnnotationPass.Results(
                node, rules, _services, (member, ex) => Faulted(node, member, ex), cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            foreach (var (member, message) in results)
            {
                context.AddValidationMessage(new FieldIdentifier(node, member), message);
            }
        }
    }

    /// <summary>One field: its async attributes, then the object's own rules that name it.</summary>
    public async ValueTask ValidateField(EditContext context, FieldIdentifier field, CancellationToken cancellationToken)
    {
        var owner = field.Model;
        if (AsyncAnnotationRules.Of(owner.GetType()) is not { } rules)
        {
            return;
        }

        var messages = new List<string>();
        try
        {
            if (rules.Find(field.FieldName) is { Async.Length: > 0 } member)
            {
                messages.AddRange(await AsyncAnnotationPass.Member(owner, member, includeSync: false, _services, cancellationToken)
                    .ConfigureAwait(false));
            }

            if (messages.Count == 0 && rules.AsyncObject)
            {
                await foreach (var (_, message) in AsyncAnnotationPass.ObjectRules(owner, field.FieldName, _services, cancellationToken)
                                   .ConfigureAwait(false))
                {
                    messages.Add(message);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            messages = [Faulted(owner, field.FieldName, ex)];
        }

        // Superseded while it ran — the reader changed the value again: this answer is about a value no longer there.
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var message in messages)
        {
            context.AddValidationMessage(field, message);
        }
    }

    // Told to the developer, and said on the field: the form cannot be submitted past a rule that could not answer.
    private static string Faulted(object owner, string member, Exception ex)
    {
        RaskDiagnostics.Report(
            RaskLogLevel.Warning,
            "Rask.Forms",
            $"An async validation rule on {owner.GetType().Name}{(member.Length > 0 ? "." + member : "")} threw",
            ex);
        return EditContext.CouldNotValidate;
    }
}
