using System.Globalization;
using System.Text;

namespace Rask;

/// <summary>
/// Work in progress, with a known amount left: a track and the bar filling it.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's <c>flux:progress</c>. It IS the <c>&lt;ui-progress role="progressbar"&gt;</c>, 6px tall and the
/// width of its container — <c>.Class("h-3")</c> makes it taller — and it reports <see cref="Value" /> and
/// <see cref="Max" /> as <c>aria-valuenow</c> and <c>aria-valuemax</c> exactly as given.
/// </para>
/// <para>
/// The bar is <see cref="Value" /> over <see cref="Max" />, held between empty and full, and moves there
/// over 300ms. The share is also on the element as <c>--ui-progress</c> (a number, 0–100) and
/// <c>--ui-progress-percentage</c>, for a label or a ring drawn from the same figure.
/// </para>
/// <para>
/// A <see cref="UiElement" />, so the name a screen reader needs is <c>.Aria("label", "Upload")</c> or
/// <c>.Aria("labelledby", id)</c>.
/// </para>
/// </remarks>
public sealed partial class UiProgress : UiElement
{
    private static readonly IReadOnlyDictionary<string, string?> Marker =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-progress"] = "" };

    private static readonly IReadOnlyDictionary<string, string?> Empty =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>How far along it is, from 0 to <see cref="Max" />. Unset, 0.</summary>
    public double? Value { get; set; }

    /// <summary>What <see cref="Value" /> is out of. Unset, 100.</summary>
    public double? Max { get; set; }

    /// <summary>The bar's colour. Unset, the accent.</summary>
    public Ui.Color? Color { get; set; }

    /// <inheritdoc />
    protected override string TagName => "ui-progress";

    private double Share
    {
        get
        {
            var share = (Value ?? 0) / (Max ?? 100) * 100;
            return double.IsNaN(share) ? 0 : Math.Clamp(share, 0, 100);
        }
    }

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "relative block w-full [:where(&)]:h-1.5 overflow-hidden rounded-full bg-zinc-200 dark:bg-white/10",
            Class);

    /// <inheritdoc />
    private protected override string? ResolveRole() => Role ?? "progressbar";

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?>? ResolveData()
    {
        if (Data is not { Count: > 0 } own)
        {
            return Marker;
        }

        return new Dictionary<string, string?>(own, StringComparer.Ordinal) { ["ui-progress"] = "" };
    }

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?>? ResolveAria()
    {
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["valuemin"] = "0",
            ["valuenow"] = Number(Value ?? 0),
            ["valuemax"] = Number(Max ?? 100),
        };

        // A name the call site wrote wins, as everywhere in the kit.
        foreach (var (name, value) in Aria ?? Empty)
        {
            aria[name] = value;
        }

        return aria;
    }

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        var own = Style;
        var share = Number(Share);
        Style = $"--ui-progress:{share};--ui-progress-percentage:{share}%{(own is null ? "" : ";" + own)}";

        try
        {
            base.WriteAttributes(sb);
        }
        finally
        {
            Style = own;
        }
    }

    /// <inheritdoc />
    protected override IEnumerable<Component?> RenderChildren() =>
    [
        Div
            .Class(UiClass.Compose("h-full rounded-full transition-[width] duration-300 ease-out", Fill(Color)))
            .Style("width:var(--ui-progress-percentage)"),
    ];

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    // Measured on blue and purple, light and dark: the 600 of the hue, and its 400 on a dark surface.
    private static string Fill(Ui.Color? color) => color switch
    {
        Ui.Color.Red => "bg-red-600 dark:bg-red-400",
        Ui.Color.Orange => "bg-orange-600 dark:bg-orange-400",
        Ui.Color.Amber => "bg-amber-600 dark:bg-amber-400",
        Ui.Color.Yellow => "bg-yellow-600 dark:bg-yellow-400",
        Ui.Color.Lime => "bg-lime-600 dark:bg-lime-400",
        Ui.Color.Green => "bg-green-600 dark:bg-green-400",
        Ui.Color.Emerald => "bg-emerald-600 dark:bg-emerald-400",
        Ui.Color.Teal => "bg-teal-600 dark:bg-teal-400",
        Ui.Color.Cyan => "bg-cyan-600 dark:bg-cyan-400",
        Ui.Color.Sky => "bg-sky-600 dark:bg-sky-400",
        Ui.Color.Blue => "bg-blue-600 dark:bg-blue-400",
        Ui.Color.Indigo => "bg-indigo-600 dark:bg-indigo-400",
        Ui.Color.Violet => "bg-violet-600 dark:bg-violet-400",
        Ui.Color.Purple => "bg-purple-600 dark:bg-purple-400",
        Ui.Color.Fuchsia => "bg-fuchsia-600 dark:bg-fuchsia-400",
        Ui.Color.Pink => "bg-pink-600 dark:bg-pink-400",
        Ui.Color.Rose => "bg-rose-600 dark:bg-rose-400",
        Ui.Color.Slate => "bg-slate-600 dark:bg-slate-400",
        Ui.Color.Gray => "bg-gray-600 dark:bg-gray-400",
        Ui.Color.Zinc => "bg-zinc-600 dark:bg-zinc-400",
        Ui.Color.Neutral => "bg-neutral-600 dark:bg-neutral-400",
        Ui.Color.Stone => "bg-stone-600 dark:bg-stone-400",
        _ => "bg-fx-accent",
    };
}
