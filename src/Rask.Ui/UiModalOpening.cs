namespace Rask;

/// <summary>Who hears a lazy <see cref="UiModal" /> open, provided under its name: a filter set's <c>OnOpen</c>.</summary>
/// <param name="Opened">Runs when the reader opens it, before what it holds is drawn.</param>
internal sealed record UiModalOpening(Callback Opened);
