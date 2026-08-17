namespace Rask.Core.Components;

/// <summary>
///     User input from a keyboard, voice, or any other text-entry device — typically a key or a chord.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/kbd">MDN</see>
/// </summary>
public sealed class Kbd : Element
{
    protected override string TagName => "kbd";
}
