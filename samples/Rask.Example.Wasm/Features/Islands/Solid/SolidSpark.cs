namespace Rask.Example.Wasm.Features.Islands;

/// <summary>
///     A sparkline rendered by <c>SolidSpark.tsx</c> — an ordinary Solid component used as an ordinary
///     Rask component.
/// </summary>
/// <remarks>
///     <para>
///         In a folder of its own, and that is load-bearing rather than tidy. Solid and React both
///         compile <c>.tsx</c>, so their Vite plugins are each scoped to the directories their own
///         islands live in. Sharing a folder — or nesting one inside the other — leaves both plugins
///         claiming the same files, and the loser's island is compiled with the wrong JSX transform:
///         it builds, it ships, and it mounts nothing. The build refuses that arrangement by name.
///     </para>
///     <para>
///         Byte-identical to the Server showcase's <c>SolidSpark.tsx</c>, which is the claim: nothing
///         in it knows which host it is on. Here a callback is a <c>[JSExport]</c> call straight into
///         this tab's own runtime rather than a trip over the live WebSocket.
///     </para>
/// </remarks>
public sealed partial class SolidSpark : Rask.External.SolidComponent
{
    /// <summary>The readings to plot, newest last.</summary>
    public required IReadOnlyList<int> Readings { get; set; }

    /// <summary>The caption above the sparkline.</summary>
    public required string Caption { get; set; }

    /// <summary>Runs with the index the reader hovered, so C# can echo it back.</summary>
    public Action<int>? OnPointHovered { get; set; }
}
