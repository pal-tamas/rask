using System.Runtime.CompilerServices;
using FluentValidation;
using Rask.Core.Forms;

namespace Rask.Validation.FluentValidation;

// Where the generator puts what it found.
//
// The compilation's AbstractValidator<T> types are discovered at BUILD time and registered from a
// [ModuleInitializer] the generator emits into the app assembly, so nothing here scans assemblies and
// nothing reflects — which is what lets a WebAssembly app keep this and still publish trimmed.
//
// Storage mirrors CqrsRegistry deliberately: a volatile dictionary rebuilt wholesale under a lock, so a
// lookup racing a hot-reload refresh sees either the old table or the new one and never a half-built
// one; and Replace is keyed by a group object so re-running one assembly's registration REPLACES that
// assembly's entries rather than appending to them, which is what makes deleting the last validator in
// a file actually remove it.
/// <summary>
///     The validators Rask discovered in your app, keyed by the model type each one validates.
///     <para>
///         You do not normally call any of this: writing
///         <c>public sealed class OrderValidator : AbstractValidator&lt;Order&gt;</c> is the whole
///         registration, and a <c>Form&lt;Order&gt;</c> finds it. The members are public because the
///         generated registration code has to reach them.
///     </para>
/// </summary>
public static class RaskValidators
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<object, (Type Model, Func<IServiceProvider?, object> Factory)[]> Groups = [];
    private static readonly Dictionary<Type, Func<IServiceProvider?, object>> Manual = [];
    private static volatile IReadOnlyDictionary<Type, Func<IServiceProvider?, object>> _table =
        new Dictionary<Type, Func<IServiceProvider?, object>>();

    /// <summary>
    ///     Replaces every validator registered under <paramref name="groupKey" />. Called by generated
    ///     code, once per assembly, and again by hot reload when that assembly's validators change.
    /// </summary>
    /// <param name="groupKey">The generated registry type standing for one assembly.</param>
    /// <param name="entries">Each model type and the factory that builds its validator.</param>
    public static void Replace(
        object groupKey,
        IEnumerable<(Type Model, Func<IServiceProvider?, object> Factory)> entries)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(entries);

        lock (Gate)
        {
            Groups[groupKey] = entries.ToArray();
            Rebuild();
        }
    }

    /// <summary>
    ///     Registers one validator by hand, for a model whose validator the generator cannot see or
    ///     construct. A manual registration wins over a generated one for the same model type.
    /// </summary>
    /// <param name="modelType">The model the validator validates.</param>
    /// <param name="factory">Builds the validator, given the render scope.</param>
    public static void Register(Type modelType, Func<IServiceProvider?, object> factory)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(factory);

        lock (Gate)
        {
            Manual[modelType] = factory;
            Rebuild();
        }
    }

    // Manual registrations are applied LAST so they beat the generated ones, the same ordering
    // CqrsRegistry uses for the same reason.
    private static void Rebuild()
    {
        var next = new Dictionary<Type, Func<IServiceProvider?, object>>();
        foreach (var group in Groups.Values)
        {
            foreach (var (model, factory) in group)
            {
                next[model] = factory;
            }
        }

        foreach (var (model, factory) in Manual)
        {
            next[model] = factory;
        }

        _table = next;
    }

    /// <summary>
    ///     Resolves a validator's constructor dependency. Called by generated code — a validator that
    ///     takes services is constructed from the render scope.
    /// </summary>
    /// <typeparam name="T">The service the validator asked for.</typeparam>
    /// <param name="services">The render scope, which is <see langword="null" /> outside a live render.</param>
    /// <returns>The resolved service.</returns>
    /// <exception cref="InvalidOperationException">
    ///     There is no scope to resolve from — the form was rendered without a service provider.
    /// </exception>
    public static T Service<T>(IServiceProvider? services)
        where T : notnull
    {
        // Without this the generated `new OrderValidator(sp.GetRequiredService<IRepo>())` would be a
        // NullReferenceException from inside generated code the author never wrote, which is the worst
        // possible place to land. Name the validator's shape and where the scope comes from instead.
        if (services is null)
        {
            throw new InvalidOperationException(
                $"A validator needs '{typeof(T)}' from dependency injection, but this form was rendered "
                + "with no service provider. Render it through a host that has one, or pass a provider "
                + "to RaskTest.Render(...) in a unit test.");
        }

        if (services.GetService(typeof(T)) is not T service)
        {
            throw new InvalidOperationException(
                $"A validator needs '{typeof(T)}' from dependency injection, but nothing is "
                + "registered for it. Register it in Program.cs.");
        }

        return service;
    }

    /// <summary>
    ///     The factory that builds the validator for <paramref name="modelType" />, or
    ///     <see langword="null" /> when nothing validates it.
    /// </summary>
    /// <param name="modelType">The model or request type to look up.</param>
    /// <returns>A factory taking the scope to resolve the validator's own dependencies from.</returns>
    public static Func<IServiceProvider?, object>? Find(Type modelType) =>
        _table.TryGetValue(modelType, out var factory) ? factory : null;

    // The handshake. Rask.Core cannot reference this package — Core is bundled into every host and stays
    // third-party-free — so the dependency runs the other way: this assembly announces itself, and Core
    // asks whatever announced itself whether it has a validator for a given model.
    //
    // CA2255 warns off module initializers in libraries because a surprise on load is hard to trace.
    // This is the case the rule names as the exception: it hands a delegate to a host that cannot
    // reference back, touches nothing else, and the alternative is asking every app to write a line of
    // startup whose only purpose is to say "yes, really".
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install() => RaskValidation.RegisterSource(static (modelType, services) =>
        Find(modelType) is { } factory && factory(services) is IValidator validator
            ? new FluentValidationFieldValidator(validator)
            : null);
}
