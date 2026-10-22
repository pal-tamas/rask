namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxPillsExample : Component
{
    private ICollection<string> Categories { get; set; } = ["fantasy", "science-fiction", "thriller"];

    protected override Component? Render() =>
        Ui.CheckboxGroup.Bind(() => Categories).Label("Categories").Pills[
            Ui.Checkbox.Value("fantasy").Label("Fantasy"),
            Ui.Checkbox.Value("science-fiction").Label("Science fiction"),
            Ui.Checkbox.Value("horror").Label("Horror"),
            Ui.Checkbox.Value("mystery").Label("Mystery"),
            Ui.Checkbox.Value("romance").Label("Romance"),
            Ui.Checkbox.Value("autobiography").Label("Autobiography"),
            Ui.Checkbox.Value("thriller").Label("Thriller"),
            Ui.Checkbox.Value("poetry").Label("Poetry"),
            Ui.Checkbox.Value("children").Label("Children")
        ];
}
