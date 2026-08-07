namespace Rask.Example.Shared.Features;

// Bootstrap progress bars. Value sets the fill (Min/Max default to 0/100); Color themes the bar and
// Striped/Animated add the moving-stripe treatment. Bootstrap 5.3 puts role/aria on the outer
// .progress, which BsProgress emits for you.
public sealed partial class BsProgressDemo : Component
{
    protected override Component? Render() =>
        Div(Class: "vstack gap-3")[
            BsProgress(Value: 25),
            BsProgress(Value: 50, Color: BsColor.Success, Label: "50%"),
            BsProgress(Value: 75, Color: BsColor.Info, Striped: true),
            BsProgress(Value: 100, Color: BsColor.Warning, Striped: true, Animated: true)
        ];
}
