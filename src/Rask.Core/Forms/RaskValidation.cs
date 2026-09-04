namespace Rask.Core.Forms;

// The built-in validation switchboard.
//
// Two jobs, and the second one is why this type exists at all:
//
//   1. AutoValidate — the global default a Form reads before registering anything.
//   2. Validator sources — the inversion that lets Core use FluentValidation without referencing it.
//      Rask.Core cannot take a dependency on the FluentValidation package (Core is bundled into every
//      host and must stay third-party-free), so the dependency runs the other way: the
//      Rask.Validation.FluentValidation assembly announces itself with a [ModuleInitializer] that calls
//      RegisterSource, exactly the handshake src/Rask/Browser/WasmHostBuilderExtensions.cs uses for the
//      WASM batteries. Nothing here knows what FluentValidation is.
//
// A source answers TWO questions, and the split is load-bearing. "Is there a validator for this type?"
// is asked on every render and must not build anything: constructing eagerly meant a validator with
// constructor dependencies threw out of Render() when there was no scope to resolve them from, and it
// froze the rules at first render so editing a RuleFor and hot-reloading changed nothing.
/// <summary>
///     Controls Rask's built-in validation.
///     <para>
///         Validation is on by default: a <c>Form</c> validates its model's
///         <c>System.ComponentModel.DataAnnotations</c> attributes, and any validator discovered for the
///         model type, with nothing declared. Set <see cref="AutoValidate" /> to <see langword="false" />
///         to turn that off everywhere, or take a form out of it individually with the form's own
///         <c>AutoValidate</c> step.
///     </para>
/// </summary>
public static class RaskValidation
{
    private static readonly Lock Gate = new();
    private static volatile ValidatorSource[] _sources = [];

    /// <summary>
    ///     Whether a <c>Form</c> validates its model with no validator declared. <see langword="true" />
    ///     by default.
    ///     <para>
    ///         An app hosted by the <c>Rask</c> package says this as
    ///         <c>app.Configure(c =&gt; c.Validation.Off())</c>, which sets this property; setting it
    ///         directly is how a lean host or a WebAssembly app does the same thing.
    ///     </para>
    /// </summary>
    public static bool AutoValidate { get; set; } = true;

    /// <summary>
    ///     Registers a source of validators for model types — the seam an integration package plugs into
    ///     so Core can use it without referencing it.
    ///     <para>
    ///         Sources are consulted in registration order and the first answer wins. Registering the
    ///         same source twice registers it twice; call this once, from a <c>[ModuleInitializer]</c>.
    ///     </para>
    /// </summary>
    /// <param name="has">
    ///     Whether this source has a validator for the type. Asked on every render, so it must not build
    ///     one.
    /// </param>
    /// <param name="resolve">
    ///     Builds the validator, given the scope its own constructor dependencies come from. Asked once
    ///     per validation run, not per render.
    /// </param>
    public static void RegisterSource(
        Func<Type, bool> has,
        Func<Type, IServiceProvider?, IAsyncFieldValidator?> resolve)
    {
        ArgumentNullException.ThrowIfNull(has);
        ArgumentNullException.ThrowIfNull(resolve);

        // Copy-on-write: the readers below take no lock, so they must never see a half-built array.
        lock (Gate)
        {
            var next = new ValidatorSource[_sources.Length + 1];
            Array.Copy(_sources, next, _sources.Length);
            next[^1] = new ValidatorSource(has, resolve);
            _sources = next;
        }
    }

    /// <summary>
    ///     Whether anything has a validator for <paramref name="modelType" />. Answers without building
    ///     one, so a form can ask on every render.
    /// </summary>
    /// <param name="modelType">The form model's type.</param>
    /// <returns><see langword="true" /> when some source can supply a validator.</returns>
    public static bool HasValidatorFor(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        foreach (var source in _sources)
        {
            if (source.Has(modelType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Builds the validator for <paramref name="modelType" />, or <see langword="null" /> when
    ///     nothing validates it.
    /// </summary>
    /// <param name="modelType">The form model's type.</param>
    /// <param name="services">The scope a validator's constructor dependencies come from.</param>
    /// <returns>A validator for the model, or <see langword="null" />.</returns>
    public static IAsyncFieldValidator? Resolve(Type modelType, IServiceProvider? services)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        foreach (var source in _sources)
        {
            if (source.Resolve(modelType, services) is { } validator)
            {
                return validator;
            }
        }

        return null;
    }

    private readonly record struct ValidatorSource(
        Func<Type, bool> Has,
        Func<Type, IServiceProvider?, IAsyncFieldValidator?> Resolve);
}

// What a Form actually registers. Holding the model type rather than a built validator is what makes
// editing a RuleFor and hot-reloading take effect: the rules are re-read on each validation run, not
// frozen at the render that first mounted the form.
internal sealed class DiscoveredFieldValidator : IAsyncFieldValidator
{
    private readonly Type _modelType;
    private readonly IServiceProvider? _services;

    internal DiscoveredFieldValidator(Type modelType, IServiceProvider? services)
    {
        _modelType = modelType;
        _services = services;
    }

    public ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken) =>
        RaskValidation.Resolve(_modelType, _services) is { } validator
            ? validator.ValidateAsync(context, cancellationToken)
            : default;

    public ValueTask ValidateFieldAsync(
        EditContext context, FieldIdentifier field, CancellationToken cancellationToken) =>
        RaskValidation.Resolve(_modelType, _services) is { } validator
            ? validator.ValidateFieldAsync(context, field, cancellationToken)
            : default;
}
