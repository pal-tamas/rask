namespace Rask.Core.Components;

/// <summary>
///     Deprecated, no-op marker. The Rask runtime <c>&lt;script&gt;</c> is now injected
///     automatically as the last child of <c>&lt;body&gt;</c> by the serializer (see
///     <see cref="HtmlSerializer" />), so apps no longer need to place this component.
///     <para>
///         Retained for source compatibility: existing trees that still contain
///         <c>RaskRuntimeScript()</c> render nothing here and pick up the single
///         framework-injected script, so there is no double emission. New apps should omit it.
///     </para>
/// </summary>
public sealed class RaskRuntimeScript : Component
{
    protected override Component? Render() => new Raw(string.Empty);
}
