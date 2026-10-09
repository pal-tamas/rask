using System.Diagnostics.CodeAnalysis;
using Rask.Core.Diagnostics;
using Rask.Wire;

namespace Rask.Core.Forms;

// Asks the store's own rules — a unique index, a range that must not overlap — about a field as it is
// committed, and about the whole model on submit, and puts what they say where a refused save would have:
// under the fields each failure names. The form registers one only for a model a store announced rules for
// (RaskValidation.RegisterStoreRules), and EditContext keeps it last and away from a value still being typed.
internal sealed class StoreRuleValidator : IAsyncFieldValidator
{
    private readonly Type _modelType;
    private readonly IServiceProvider? _services;

    // What the store said about each field's value the last time it was asked.
    private readonly Dictionary<FieldIdentifier, Answer> _answers = [];

    internal StoreRuleValidator(EditContext form, Type modelType, IServiceProvider? services)
    {
        _modelType = modelType;
        _services = services;

        // A rule over two fields changes its answer when either does, so an answer holds only while every
        // OTHER field stands still. The form owns this validator, so the subscription ends with it.
        form.FieldChanged += (_, changed) => ForgetAllBut(changed.Field);
    }

    /// <summary>The submit: every rule, once the form's own have passed, so a known duplicate never reaches the save.</summary>
    public async ValueTask Validate(EditContext context, CancellationToken cancellationToken)
    {
        _answers.Clear();
        if (context.HasValidationMessages())
        {
            return;
        }

        var failures = await Ask(context.Model, null, cancellationToken).ConfigureAwait(false);
        if (failures is not null)
        {
            FieldFailurePlacement.Place(context, failures, null);
        }
    }

    /// <summary>One committed field. A value the store has already answered for is not asked about again.</summary>
    public async ValueTask ValidateField(
        EditContext context, FieldIdentifier field, CancellationToken cancellationToken)
    {
        var value = Read(field);
        if (!_answers.TryGetValue(field, out var answer) || !Equals(answer.Value, value))
        {
            if (FieldPath.Of(context.Model, field) is not { } path
                || await Ask(context.Model, path, cancellationToken).ConfigureAwait(false) is not { } failures)
            {
                return;
            }

            answer = new Answer(value, failures);
            if (Remembers(value))
            {
                _answers[field] = answer;
            }
        }

        context.ClearChecked(field);
        FieldFailurePlacement.Place(context, answer.Failures, field);
    }

    // Null when there was nothing to ask or the store could not answer. A check that fails is not a rule
    // that failed: the save still asks the store, and says what it finds.
    private async ValueTask<IReadOnlyList<FieldFailure>?> Ask(
        object model, string? field, CancellationToken cancellationToken)
    {
        try
        {
            if (RaskValidation.ResolveStoreRules(_modelType, DispatchServicesScope.Current ?? _services) is not { } rules)
            {
                return null;
            }

            var failures = await rules.Check(model, field, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return failures;
        }
#pragma warning disable CA1031 // Whatever the store threw, the form goes on: this check is a courtesy before the save.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning,
                "Rask.Forms",
                $"The store's rules for {_modelType.Name} could not be checked before the save",
                ex);
            return null;
        }
    }

    private void ForgetAllBut(FieldIdentifier changed)
    {
        if (_answers.Count == 0 || (_answers.Count == 1 && _answers.ContainsKey(changed)))
        {
            return;
        }

        var kept = _answers.TryGetValue(changed, out var answer);
        _answers.Clear();
        if (kept)
        {
            _answers[changed] = answer;
        }
    }

    // A collection is refilled in place, so the same reference can hold a different selection.
    private static bool Remembers(object? value) => value is null || ModelGraphWalker.IsLeaf(value.GetType());

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperty on the runtime type of a bound model object: a control is bound to this " +
                        "property, which is what preserves it, as for EditContext.RegisterFieldValidator.")]
    private static object? Read(FieldIdentifier field) =>
        field.Model.GetType().GetProperty(field.FieldName)?.GetValue(field.Model);

    private readonly record struct Answer(object? Value, IReadOnlyList<FieldFailure> Failures);
}
