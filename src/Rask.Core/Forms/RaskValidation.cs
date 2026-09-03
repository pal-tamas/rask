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
    private static volatile Func<Type, IServiceProvider?, IAsyncFieldValidator?>[] _sources = [];

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
    ///         The source is asked for a validator for one model type and returns <see langword="null" />
    ///         when it has none. Sources are consulted in registration order and the first non-null answer
    ///         wins. Registering the same source twice registers it twice; call this once, from a
    ///         <c>[ModuleInitializer]</c>.
    ///     </para>
    /// </summary>
    /// <param name="source">
    ///     Given a model type and the render scope, returns a validator for that type or
    ///     <see langword="null" />.
    /// </param>
    public static void RegisterSource(Func<Type, IServiceProvider?, IAsyncFieldValidator?> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Copy-on-write: Resolve reads the array without a lock, so it must never see a half-built one.
        lock (Gate)
        {
            var next = new Func<Type, IServiceProvider?, IAsyncFieldValidator?>[_sources.Length + 1];
            Array.Copy(_sources, next, _sources.Length);
            next[^1] = source;
            _sources = next;
        }
    }

    /// <summary>
    ///     Asks every registered source for a validator for <paramref name="modelType" />, returning the
    ///     first one offered, or <see langword="null" /> when no source has one.
    /// </summary>
    /// <param name="modelType">The form model's type.</param>
    /// <param name="services">The render scope, for a validator with constructor dependencies.</param>
    /// <returns>A validator for the model, or <see langword="null" />.</returns>
    public static IAsyncFieldValidator? Resolve(Type modelType, IServiceProvider? services)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        foreach (var source in _sources)
        {
            if (source(modelType, services) is { } validator)
            {
                return validator;
            }
        }

        return null;
    }
}
