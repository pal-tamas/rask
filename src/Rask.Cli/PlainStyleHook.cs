using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rask.Cli;

/// <summary>
/// Strips every style from what is written, leaving the text and the cursor control intact.
/// </summary>
/// <remarks>
/// Turning Spectre's color system off removes <em>colors</em> but not decorations, so bold and dim still
/// emit SGR escapes. That is not what this CLI promises: <c>NO_COLOR</c> is documented as falling back to
/// plain text, and it always has produced output with no escape sequences in it at all.
/// <para>
/// The alternative — declaring the whole stream non-ANSI — would take the cursor control with it, and
/// with it the status spinner and the arrow-key prompts. Someone who set <c>NO_COLOR</c> asked for plain
/// text, not for a lesser tool. Attached as a render hook so it covers prompts and any other renderable
/// that carries its own markup, not just the styles this CLI names itself.
/// </para>
/// </remarks>
internal sealed class PlainStyleHook : IRenderHook
{
    public IEnumerable<IRenderable> Process(RenderOptions options, IEnumerable<IRenderable> renderables) =>
        renderables.Select(IRenderable (renderable) => new Unstyled(renderable));

    private sealed class Unstyled(IRenderable inner) : IRenderable
    {
        public Measurement Measure(RenderOptions options, int maxWidth) => inner.Measure(options, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
            inner.Render(options, maxWidth)
                .Select(segment => segment.IsLineBreak ? segment : new Segment(segment.Text, Style.Plain));
    }
}
