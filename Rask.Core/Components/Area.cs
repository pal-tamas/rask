namespace Rask.Core.Components;

public sealed class Area : Component<Area.Props>
{
    public Area(Props? props = null) : base(props, null) { }

    protected override string TagName => "area";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Alt = null,
        string? Coords = null,
        string? Shape = null,
        string? Href = null,
        string? Target = null,
        string? Rel = null,
        string? Download = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data)
    {
        public override IEnumerable<KeyValuePair<string, string?>> ToAttributes()
        {
            foreach (var kv in base.ToAttributes())
            {
                yield return kv;
            }

            if (Alt is not null)
            {
                yield return new KeyValuePair<string, string?>("alt", Alt);
            }

            if (Coords is not null)
            {
                yield return new KeyValuePair<string, string?>("coords", Coords);
            }

            if (Shape is not null)
            {
                yield return new KeyValuePair<string, string?>("shape", Shape);
            }

            if (Href is not null)
            {
                yield return new KeyValuePair<string, string?>("href", Href);
            }

            if (Target is not null)
            {
                yield return new KeyValuePair<string, string?>("target", Target);
            }

            if (Rel is not null)
            {
                yield return new KeyValuePair<string, string?>("rel", Rel);
            }

            if (Download is not null)
            {
                yield return new KeyValuePair<string, string?>("download", Download);
            }
        }
    }
}
