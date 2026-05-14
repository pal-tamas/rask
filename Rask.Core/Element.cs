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

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        if (Id is not null)
        {
            yield return new KeyValuePair<string, string?>("id", Id);
        }

        if (Class is not null)
        {
            yield return new KeyValuePair<string, string?>("class", Class);
        }

        if (Style is not null)
        {
            yield return new KeyValuePair<string, string?>("style", Style);
        }

        if (Data is null)
        {
            yield break;
        }

        foreach (var kv in Data)
        {
            yield return new KeyValuePair<string, string?>($"data-{kv.Key}", kv.Value);
        }
    }
}
