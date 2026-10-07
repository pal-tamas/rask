namespace Rask.Blazor;

/// <summary>What a hosted element's <c>change</c> reports, and so what Blazor's binder is waiting for.</summary>
internal enum BlazorValueKind
{
    /// <summary>The element's value, as a string.</summary>
    Text,

    /// <summary>A checkbox's checked state, as a <see cref="bool" />.</summary>
    Checked,

    /// <summary>A <c>&lt;select multiple&gt;</c>'s whole selection, as a <c>string[]</c>.</summary>
    Values,
}
