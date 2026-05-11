namespace Rask.Core.Routing;

public readonly record struct RouteUrl(string Path, string? QueryString = null, Type? PageType = null)
{
    public static RouteUrl External(string url) => new(url);

    public override string ToString() => string.IsNullOrEmpty(QueryString) ? Path : Path + QueryString;

    public static implicit operator RouteUrl(string url) => new(url);
    public static implicit operator string(RouteUrl url) => url.ToString();
}
