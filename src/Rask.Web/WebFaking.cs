namespace Rask.Web;

/// <summary><c>Navigator.Clipboard.Fake()</c>: what makes any web object fakeable in a test.</summary>
public static class WebFaking
{
    /// <summary>Stands in for this web object for the rest of the test's flow, until the returned fake is disposed of.</summary>
    public static WebFake<T> Fake<T>(this T web)
        where T : JsObject =>
        new(web);
}
