using Rask.Core.Routing;

namespace Rask.Core.Forms;

/// <summary>
///     App-level hooks for the string-to-value binding used by routes and forms.
/// </summary>
public static class RaskBinding
{
    /// <summary>
    ///     Registers a custom <see cref="IParsable{TSelf}" /> value type so it can be bound from a
    ///     string (a form field, route segment, or query value) without runtime code generation.
    ///     <para>
    ///         Only needed for a <b>full WASM AOT</b> publish (<c>RaskWasmAot=true</c>), and only for
    ///         <b>custom</b> value types used in <b>form models</b> — every BCL <c>IParsable</c>
    ///         primitive is registered automatically, and custom types used as routed-page
    ///         <c>[RouteParam]</c>/<c>[QueryParam]</c> properties are registered by the source
    ///         generator. Under the default interpreter build this call is optional (the framework
    ///         falls back to reflection). Call once at startup, before the first bind.
    ///     </para>
    /// </summary>
    /// <typeparam name="T">A value type implementing <see cref="IParsable{TSelf}" />.</typeparam>
    public static void RegisterParsable<T>() where T : IParsable<T> => TypedParserRegistry.Register<T>();
}
