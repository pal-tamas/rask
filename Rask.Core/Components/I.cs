namespace Rask.Core.Components;

public sealed class I : Component<I.Props>
{
    public I(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public I(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "i";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
