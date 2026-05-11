namespace Rask.Core.Components;

public sealed class Article : Component<Article.Props>
{
    public Article(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Article(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "article";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
