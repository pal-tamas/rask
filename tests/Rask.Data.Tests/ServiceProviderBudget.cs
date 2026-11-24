using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

[assembly: AssemblyFixture(typeof(Rask.Data.Tests.ServiceProviderBudget))]

namespace Rask.Data.Tests;

/// <summary>
///     Keeps this run well under the twenty internal service providers EF Core lets one process build. Every distinct
///     options shape — a provider, a <c>ReplaceService</c>, a warning, a singleton interceptor, even the ORDER the
///     extensions were added in — is one more, for the life of the test process; past twenty EF Core refuses the next
///     context (<c>ManyServiceProvidersCreatedWarning</c>) and whichever test happens to open it fails, order-dependent.
/// </summary>
/// <remarks>
///     It listens for EF Core's own "service provider created" events and fails the test that builds one past
///     <see cref="Budget" />, naming every provider the run built, the test that built it and what made it new — so the
///     suite stops with the culprit while there is still room, never with EF Core's refusal in an unrelated test. A new
///     shape is rarely needed: build options with <see cref="OptionsLayout.LikeAddDbContext{TContext}" />, give a
///     context's model its own type rather than a key factory, and share <see cref="EachContextItsOwnModel" />.
/// </remarks>
internal sealed class ServiceProviderBudget : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    /// <summary>Four short of EF Core's twenty, so one new shape is a decision and never a surprise.</summary>
    internal const int Budget = 16;

    private readonly List<string> _built = [];
    private readonly IDisposable _subscription;

    public ServiceProviderBudget() => _subscription = DiagnosticListener.AllListeners.Subscribe(this);

    public void Dispose() => _subscription.Dispose();

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener value)
    {
        if (value.Name == DbLoggerCategory.Name)
        {
            value.Subscribe(this, static name => IsProviderBuilt(name));
        }
    }

    // Other listeners may enable more of EF Core's events, and each subscriber hears every event written.
    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        if (!IsProviderBuilt(value.Key))
        {
            return;
        }

        string[] built;
        lock (_built)
        {
            _built.Add($"{TestContext.Current.Test?.TestDisplayName ?? "(outside a test)"}: {value.Value}");
            if (_built.Count <= Budget)
            {
                return;
            }

            built = [.. _built];
        }

        throw new InvalidOperationException(
            $"This run has built {built.Length} of EF Core's internal service providers, past this suite's budget of "
            + $"{Budget} (EF Core refuses a process past twenty). Each line is one, with the test that built it and what "
            + "made its options new; share an existing shape instead (see ServiceProviderBudget):"
            + Environment.NewLine + string.Join(Environment.NewLine, built));
    }

    void IObserver<DiagnosticListener>.OnCompleted()
    {
    }

    void IObserver<DiagnosticListener>.OnError(Exception error)
    {
    }

    void IObserver<KeyValuePair<string, object?>>.OnCompleted()
    {
    }

    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error)
    {
    }

    private static bool IsProviderBuilt(string name) =>
        name == CoreEventId.ServiceProviderCreated.Name || name == CoreEventId.ServiceProviderDebugInfo.Name;
}
