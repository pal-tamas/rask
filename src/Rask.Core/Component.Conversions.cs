using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using Rask.Core.Components;
using Rask.Core.Forms;

namespace Rask.Core;

public abstract partial class Component
{
    // Collection-expression builder targeted by the [CollectionBuilder] attribute above. A
    // `Render()`/`Head` body written as `[Doctype(), Html(...)]` lands here and is wrapped in a
    // (tagless, internal) Fragment so the whole render pipeline keeps operating on a single
    // Component. Public because the compiler emits this call at each collection-expression site,
    // including in user assemblies where Fragment is not visible. NOT named `Create`: that would
    // shadow a user component named `Create` (its generated factory) via base-member lookup.
    public static Component RaskFragment(ReadOnlySpan<Component?> items) => new Fragment(items.ToArray());

    // Heterogeneous-literal children: `Div()["Score: ", 42, Span()]`. These implicit conversions
    // (formerly on the deleted `Component` struct) let strings/primitives/dates flow into a children
    // list as auto-created Text nodes. Value types render with InvariantCulture so the HTML stays
    // locale-independent and byte-stable for the diff codec — matching Forms/BindingHelpers and
    // RouteValueParser. Narrower integer types widen to `int`; `char` renders the character.
    // Accepts string? so a nullable expression (e.g. `entity.Value?.ToString()`) can flow straight into
    // a children list; null becomes an empty text node rather than forcing callers to write `?? ""`.
    public static implicit operator Component(string? text) => new Text { Value = text ?? "" };

    public static implicit operator Component(int value) => Format(value);

    public static implicit operator Component(long value) => Format(value);

    public static implicit operator Component(double value) => Format(value);

    public static implicit operator Component(float value) => Format(value);

    public static implicit operator Component(decimal value) => Format(value);

    public static implicit operator Component(bool value) => new Text { Value = value ? "True" : "False" };

    public static implicit operator Component(char value) => new Text { Value = value.ToString() };

    public static implicit operator Component(Guid value) => new Text { Value = value.ToString() };

    public static implicit operator Component(DateOnly value) => Format(value);

    public static implicit operator Component(TimeOnly value) => Format(value);

    public static implicit operator Component(DateTime value) => Format(value);

    public static implicit operator Component(DateTimeOffset value) => Format(value);

    public static implicit operator Component(TimeSpan value) => Format(value);

    private static Text Format<T>(T value)
        where T : IFormattable =>
        new Text { Value = value.ToString(null, CultureInfo.InvariantCulture) };
}
