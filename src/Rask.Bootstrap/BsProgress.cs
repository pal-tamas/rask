namespace Rask.Bootstrap;

// A Bootstrap progress bar: <div class="progress" role="progressbar"><div class="progress-bar"
// style="width:N%"></div></div>. Value is required; Min/Max default to 0/100. Bootstrap 5.3 moves
// role/aria onto the outer .progress, which is what is emitted here.
public sealed class BsProgress : BsBlock
{
    public double Value { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }

    public BsColor? Color { get; set; }

    // Optional label rendered inside the bar (e.g. "60%").
    public string? Label { get; set; }

    public bool? Striped { get; set; }

    // Animates the stripes (implies Striped).
    public bool? Animated { get; set; }

    protected override Component? Render()
    {
        var min = Min ?? 0;
        var max = Max ?? 100;
        var range = max - min;
        var pct = range <= 0 ? 0 : Math.Clamp((Value - min) / range * 100, 0, 100);

        var barCls = BsClass.Join(
            "progress-bar",
            Striped is true || Animated is true ? "progress-bar-striped" : null,
            Animated is true ? "progress-bar-animated" : null,
            Color is { } c ? c.Bg() : null);

        var style = $"width:{BsClass.Num(pct)}%";
        var bar = Label is { } l
            ? Div(Class: barCls, Style: style)[l]
            : Div(Class: barCls, Style: style);

        var aria = new Dictionary<string, string?>
        {
            ["valuenow"] = BsClass.Num(Value),
            ["valuemin"] = BsClass.Num(min),
            ["valuemax"] = BsClass.Num(max),
        };

        return Div(Id: Id, Class: BsClass.Join("progress", Class), Role: "progressbar", Aria: aria)[bar];
    }
}
