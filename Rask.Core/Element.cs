using System.Text;

namespace Rask.Core;

// HTML element base. Carries the universal HTML attributes (Id/Class/Style/Data) so that
// tag classes (Div, Span, Input, …) inherit them and their generated factories expose them
// as optional parameters. User components extend Component directly and stay free of these
// HTML-only concerns.
public abstract class Element : Component
{
    public string? Id { get; set; }
    public string? Class { get; set; }
    public string? Style { get; set; }
    public IReadOnlyDictionary<string, string?>? Data { get; set; }

    // Subclasses transform the `class` attribute value without re-implementing the universal
    // id/class/style/data-* walk. NavLink overrides this to splice in its active class.
    protected virtual string? ResolveClass() => Class;

    protected override void WriteAttributes(StringBuilder sb)
    {
        if (Id is not null) AppendAttr(sb, "id", Id);
        var cls = ResolveClass();
        if (cls is not null) AppendAttr(sb, "class", cls);
        if (Style is not null) AppendAttr(sb, "style", Style);
        if (Data is null) return;
        foreach (var kv in Data)
        {
            AppendAttr(sb, "data-" + kv.Key, kv.Value);
        }
    }
}
