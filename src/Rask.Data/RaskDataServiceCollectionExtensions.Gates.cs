using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

public static partial class RaskDataServiceCollectionExtensions
{
    /// <summary>
    ///     Says when a gate of the app's own kind is open — a module the tenant bought, a licence, a feature — for the
    ///     members its entities gate with <c>b.Gated(x =&gt; x.Card, Module.Tachograph)</c>.
    /// </summary>
    /// <typeparam name="TGate">The kind of gate: <c>Module</c>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="isOpen">
    ///     Whether <c>gate</c> is open for the work in flight, given the services of its scope — the signed-in user's, a
    ///     job's — or the app's outside one.
    /// </param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///     Asked once per gate per save. A save that writes a gated member with no rule for its kind of gate is refused
    ///     rather than guessed at: shut would lose what was typed, open would let anyone write it. The last rule added
    ///     for a kind wins.
    /// </remarks>
    public static IServiceCollection AddRaskGate<TGate>(this IServiceCollection services, Func<IServiceProvider, TGate, bool> isOpen)
        where TGate : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(isOpen);

        services.AddSingleton(new GateRule(typeof(TGate), (scope, gate) => isOpen(scope, (TGate)gate)));
        return services;
    }
}
