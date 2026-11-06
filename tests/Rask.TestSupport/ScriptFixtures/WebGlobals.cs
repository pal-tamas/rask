namespace Rask.TestSupport.ScriptFixtures;

/// <summary>The prelude every fixture runs after: the web platform globals a bare ECMAScript engine has none of.</summary>
internal static class WebGlobals
{
    private static readonly Lazy<string> Text = new(() =>
    {
        using var stream = typeof(WebGlobals).Assembly.GetManifestResourceStream("Rask.TestSupport.ScriptFixtures.web-globals.js")
                           ?? throw new InvalidOperationException("web-globals.js is not embedded in Rask.TestSupport.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string Source => Text.Value;
}
