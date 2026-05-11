using System.Globalization;

namespace Rask.Core.Components;

public sealed class Iframe : Component<Iframe.Props>
{
    public Iframe(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Iframe(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "iframe";

    public new sealed record Props(
        string? Src = null,
        string? Srcdoc = null,
        string? Name = null,
        string? Sandbox = null,
        string? Allow = null,
        int? Width = null,
        int? Height = null,
        string? Loading = null,
        string? ReferrerPolicy = null,
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

            if (Src is not null)
            {
                yield return new KeyValuePair<string, string?>("src", Src);
            }

            if (Srcdoc is not null)
            {
                yield return new KeyValuePair<string, string?>("srcdoc", Srcdoc);
            }

            if (Name is not null)
            {
                yield return new KeyValuePair<string, string?>("name", Name);
            }

            if (Sandbox is not null)
            {
                yield return new KeyValuePair<string, string?>("sandbox", Sandbox);
            }

            if (Allow is not null)
            {
                yield return new KeyValuePair<string, string?>("allow", Allow);
            }

            if (Width is not null)
            {
                yield return new KeyValuePair<string, string?>("width",
                    Width.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Height is not null)
            {
                yield return new KeyValuePair<string, string?>("height",
                    Height.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Loading is not null)
            {
                yield return new KeyValuePair<string, string?>("loading", Loading);
            }

            if (ReferrerPolicy is not null)
            {
                yield return new KeyValuePair<string, string?>("referrerpolicy", ReferrerPolicy);
            }
        }
    }
}
