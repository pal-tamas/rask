namespace Rask.Core.Components;

/// <summary>
///     Isolates a run of text whose direction is unknown — a user-supplied name, say — so its bidirectional
///     resolution cannot reorder the text around it.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/bdi">MDN</see>
/// </summary>
[Tag("bdi")]
public sealed partial class Bdi : Element
{
}
