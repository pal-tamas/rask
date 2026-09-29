namespace Rask.Core.Routing;

public readonly record struct RouteUrl(string Path, string? QueryString = null, Type? PageType = null)
{
    public static RouteUrl External(string url) => new(url);

    public override string ToString() => string.IsNullOrEmpty(QueryString) ? Path : Path + QueryString;

    /// <summary>
    ///     Navigates here, from an event handler or a save — <c>Routes.ProductsPage().Go()</c> — with nothing
    ///     injected. Works inside a component, where the page's bare name is its chain entry rather than its type.
    /// </summary>
    /// <param name="replace">Replace the current history entry instead of adding one.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public void Go(bool replace = false) => Navigator.RequireCurrent().NavigateTo(this, replace);

    public static implicit operator RouteUrl(string url) => new(url);
    public static implicit operator string(RouteUrl url) => url.ToString();
}
