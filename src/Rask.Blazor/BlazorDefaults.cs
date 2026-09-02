namespace Rask.Blazor;

/// <summary>The names the C# half and the rendered markup have to agree on.</summary>
/// <remarks>
///     Constants rather than literals repeated at each site, so a test can assert the rendered
///     attribute against the same symbol the renderer wrote — the shape
///     <c>ExternalWireContractTests</c> uses to keep the two halves of the islands feature from
///     drifting apart.
/// </remarks>
public static class BlazorDefaults
{
    /// <summary>The element a hosted Blazor component renders into.</summary>
    /// <remarks>
    ///     Hyphenated so the browser parses it as an <c>HTMLUnknownElement</c> rather than guessing:
    ///     it never participates in tag-omission rules, CSS can address every island as a class of
    ///     thing, and devtools shows the boundary exactly where it is.
    /// </remarks>
    public const string HostTag = "rask-blazor";

    /// <summary>Identifies the island. Matches the C# type's simple name; unique per app (RASK064).</summary>
    public const string NameAttribute = "name";

    /// <summary>The hosted component's full type name, for devtools and for support questions.</summary>
    public const string ComponentAttribute = "component";
}
