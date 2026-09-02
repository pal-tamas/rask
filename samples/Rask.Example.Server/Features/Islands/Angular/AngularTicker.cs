namespace Rask.Example.Server.Features.Islands;

/// <summary>
///     A quote ticker rendered by <c>AngularTicker.ts</c> — an ordinary standalone Angular component
///     used as an ordinary Rask component.
/// </summary>
/// <remarks>
///     <para>
///         In a folder of its own for the same reason <see cref="SolidSpark" /> is: Angular writes the
///         same plain <c>.ts</c> a Lit element does, so the two plugins are scoped to their own island
///         directories and the build refuses an arrangement where they overlap.
///     </para>
///     <para>
///         The one runtime here whose bootstrap is asynchronous — <c>createApplication()</c> returns a
///         promise — so this is also the island that proves props arriving before the application
///         resolves are held rather than dropped. Pressing "Raise the reading" repeatedly on a cold
///         page is exactly that race.
///     </para>
///     <para>
///         Keeps a tick count of its own that C# never sees, so a C# re-render has to reach it through
///         <c>ComponentRef.setInput</c> rather than by remounting.
///     </para>
/// </remarks>
public sealed partial class AngularTicker : Rask.External.AngularComponent
{
    /// <summary>The symbol being quoted.</summary>
    public required string Symbol { get; set; }

    /// <summary>The quote C# owns, in whole currency units.</summary>
    public int Quote { get; set; }

    /// <summary>Runs when the reader asks for a refresh, so C# can move the quote.</summary>
    public Action? OnRefreshRequested { get; set; }
}
