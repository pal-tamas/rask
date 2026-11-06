namespace Rask;

/// <summary>What a control provides around the kit controls it draws as its parts: where they say what they cannot take.</summary>
/// <param name="Say">Hears the sentence, in the reader's language.</param>
internal sealed record UiUnreadRelay(Func<string, Task> Say)
{
    /// <summary>The relay the control being rendered is a part of, or nothing.</summary>
    internal static Func<string, Task>? Around() => Context.Get<UiUnreadRelay>()?.Say;
}
