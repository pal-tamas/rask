using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentValidation;
using Rask.Core.Forms;
using Rask.Values;

namespace Rask;

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
    private static readonly Dictionary<object, ValidatorEntry[]> Groups = [];
    private static readonly Dictionary<Type, Func<IServiceProvider?, object>> Manual = [];
    private static volatile Registry _registry = Registry.Empty;

    /// <summary>
    ///     Replaces every validator registered under <paramref name="groupKey" />. Called by generated code, once per
    ///     assembly, and again by hot reload when that assembly's validators change.
    /// </summary>
    /// <param name="groupKey">The generated registry type standing for one assembly.</param>
    /// <param name="entries">Each validator, made by <see cref="Entry{T}" />, <see cref="Inline{T}" /> or <see cref="Project{TModel, TOwner}" />.</param>
    public static void Replace(object groupKey, IEnumerable<ValidatorEntry> entries)
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
    ///     Registers one validator by hand, for a model whose validator the generator cannot see or construct. A manual
    ///     registration replaces what was generated for the same model type.
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

    /// <summary>An <c>AbstractValidator&lt;T&gt;</c> class the generator found. Called by generated code.</summary>
    /// <typeparam name="T">The type it validates.</typeparam>
    /// <param name="factory">Builds it, given the scope its constructor's dependencies come from.</param>
    /// <returns>The registry's entry.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ValidatorEntry Entry<T>(Func<IServiceProvider?, IValidator<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new ValidatorEntry(typeof(T), factory, Combined<T>, StatedFacetReader.Of<T>);
    }

    /// <summary>
    ///     A type's own <c>static void Rules(InlineValidator&lt;T&gt; rules)</c>: its rules, declared once and kept — a
    ///     method of the type can take no services. Called by generated code.
    /// </summary>
    /// <typeparam name="T">The type that states them.</typeparam>
    /// <param name="rules">Its <c>Rules</c> method.</param>
    /// <returns>The registry's entry.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ValidatorEntry Inline<T>(Action<InlineValidator<T>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var stated = new Lazy<IValidator<T>>(() =>
        {
            var validator = new InlineValidator<T>();
            rules(validator);
            return validator;
        });
        return new ValidatorEntry(typeof(T), _ => stated.Value, Combined<T>, StatedFacetReader.Of<T>);
    }

    /// <summary>
    ///     A generated form model, which borrows the rules its aggregate or entity states, member by member: a form over
    ///     the model asks them as each field is committed. Called by generated code.
    /// </summary>
    /// <typeparam name="TModel">The generated form model.</typeparam>
    /// <typeparam name="TOwner">The aggregate or entity it is the form of.</typeparam>
    /// <param name="members">Which of the model's members carries which of the owner's.</param>
    /// <returns>The registry's entry.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ValidatorEntry Project<TModel, TOwner>(Action<ModelProjection<TModel, TOwner>> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        var projection = new ModelProjection<TModel, TOwner>();
        members(projection);
        return new ValidatorEntry(
            typeof(TModel),
            services => projection.Build(Owners(typeof(TOwner), services), services),
            Combined<TModel>,
            StatedFacetReader.Of<TModel>,
            projection);
    }

    /// <summary>
    ///     What a value object's own <c>static void Rules(InlineValidator&lt;T&gt; rules)</c> says against it: what its
    ///     generated <c>Rules()</c> yields, so its constructor refuses it after its base's declared rules. A failure about
    ///     <c>Value</c> is the value's own; one about another member is under that member. Called by generated code.
    /// </summary>
    /// <param name="value">The value object being made.</param>
    /// <returns>One problem per broken rule.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IEnumerable<Problem> Problems(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (Find(value.GetType())?.Invoke(null) is not IValidator validator)
        {
            return [];
        }

        return validator.Validate(new ValidationContext<object>(value)).Errors.Select(static e =>
            string.IsNullOrEmpty(e.PropertyName) || string.Equals(e.PropertyName, "Value", StringComparison.Ordinal)
                ? new Problem(null, UnnamedValue.Said(e.ErrorMessage))
                : new Problem(e.PropertyName, e.ErrorMessage));
    }

    /// <summary>
    ///     Whether <paramref name="modelType" /> is a generated form model that borrows the rules its aggregate states —
    ///     what a request carrying one is checked by, beside its own validator.
    /// </summary>
    /// <param name="modelType">The type to ask about.</param>
    /// <returns><see langword="true" /> when the type borrows rules that are there to run.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool BorrowsRules(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        var registry = _registry;
        return registry.States(modelType) && registry.Entries(modelType).Any(static e => e.Projection is not null);
    }

    // Manual registrations are applied LAST so they replace the generated ones, the same ordering CqrsRegistry uses.
    private static void Rebuild()
    {
        _registry = Registry.Of(Groups.Values.SelectMany(static group => group), Manual);
        StatedRules.Changed();
    }

    // What validates the owner of a projection, built: its own validators, never another projection.
    private static List<IValidator> Owners(Type owner, IServiceProvider? services) =>
        [.. _registry.Entries(owner).Where(static e => e.Projection is null)
            .Select(e => e.Build(services)).OfType<IValidator>()];

    private static IValidator Combined<T>(IReadOnlyList<IValidator> validators)
    {
        if (validators.Count == 1)
        {
            return validators[0];
        }

        var all = new InlineValidator<T>();
        foreach (var validator in validators.OfType<IValidator<T>>())
        {
            all.Include(validator);
        }

        return all;
    }

    internal static Registry Current => _registry;

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
                + "to Page.Render(...) in a unit test.");
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
    ///     The factory that builds the validator for <paramref name="modelType" />, or <see langword="null" /> when
    ///     nothing validates it. Several validators of one type — its own <c>Rules</c> and an <c>AbstractValidator</c>
    ///     class — are built as one.
    /// </summary>
    /// <param name="modelType">The model or request type to look up.</param>
    /// <returns>A factory taking the scope to resolve the validator's own dependencies from.</returns>
    public static Func<IServiceProvider?, object>? Find(Type modelType) => _registry.Find(modelType);

    // The handshake. Rask.Core cannot reference this package — Core is bundled into every host and stays
    // third-party-free — so the dependency runs the other way: this assembly announces itself, and Core
    // asks whatever announced itself whether it has a validator for a given model.
    //
    // CA2255 warns off module initializers in libraries because a surprise on load is hard to trace.
    // This is the case the rule names as the exception: it hands a delegate to a host that cannot
    // reference back, and the alternative is asking every app to write a line of startup whose only
    // purpose is to say "yes, really". The other things it touches are FluentValidation's display-name
    // resolver and its language manager, the hooks FluentValidation documents for exactly this, and an
    // app that sets either itself replaces Rask's.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        FluentDisplayNames.Install();
        CatalogLanguageManager.Install();
        StatedRules.Use(FluentStatedRules.Instance);
        RaskValidation.RegisterSource(
            static modelType => Find(modelType) is not null,
            static (modelType, services) =>
                Find(modelType) is { } factory && factory(services) is IValidator validator
                    ? new FluentValidationFieldValidator(validator)
                    : null);
    }
}
